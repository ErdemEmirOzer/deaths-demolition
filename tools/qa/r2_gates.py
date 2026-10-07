#!/usr/bin/env python3
"""WP-091 R2 acceptance gates (S-10, C-00 05.10): the threshold contract and its evaluator.

tools/qa/r2-gates.json holds every R2 gate as data (H hero, L locomotion, Z zombie, S sound, P perf, U ui), with the
thresholds the owners proposed and C-00 approved (K/wp-078/R2-KABUL-TASLAK.md). An owner's measurement test writes one
measurement artifact per gate; a human gate gets one review artifact. This module validates the contract and judges
one gate from what was bound — it never opens files itself (wp078_report binds and hash-checks the evidence).

Measurement artifact (schema dd-r2-measure-v1):
  {"schema": "dd-r2-measure-v1", "gate": "L1", "commit": "<40-hex FINAL>", "source": "<test full name>",
   "values": {"avg": 0.031, "p95": 0.074}, "samples": 412}
Review artifact (schema dd-r2-review-v1):
  {"schema": "dd-r2-review-v1", "gate": "H5", "commit": "<40-hex>", "reviewer": "AD Fable", "verdict": "KABUL",
   "must": [{"id": "MUST-1", "closed": true}]}

Statuses: a gate with no artifact is BLOCKED (named); a value outside its check, a RED review, an open MUST or a
required test that did not pass is FAIL; a report entry is never FAIL (values recorded, status REPORT or BLOCKED when
nothing was measured). Ops: "<=", ">=", "==".
"""
import json
import math
import re

GATE_KINDS = ("gate", "report", "human")
ENV_NAME = re.compile(r"[A-Z][A-Z0-9_]*")
# the one placeholder an env value may carry: the KADRO project root, filled in by the chain from tools/dd_paths.py
# (never a machine path in the contract: move day changes dd_paths only; a guard's fake HOME cannot misplace it)
KPROJ_TOKEN = "{KPROJ}"
PLACEHOLDER = re.compile(r"\{[^}]*\}")
OPS = ("<=", ">=", "==")
# C-00 06.10 (user: top down only, tests too): a gate may name the camera modes it is measured in; a run that covers
# none of them (wp078_accept --modes) does not run it and the report writes OUT_OF_SCOPE, never FAIL or BLOCKED
SCOPE_MODES = ("topdown", "tp", "fp")
OUT_OF_SCOPE = "OUT_OF_SCOPE"
SCOPE_REASON = "kullanıcı 06.10: yalnız TopDown"


class ContractError(ValueError):
    pass


def load_contract(text):
    """Parse and validate r2-gates.json text; ContractError names the first problem."""
    data = json.loads(text)
    if not isinstance(data, dict) or data.get("schema") != "dd-r2-gates-v1":
        raise ContractError("r2-gates schema is not dd-r2-gates-v1")
    gates = data.get("gates")
    if not isinstance(gates, list) or not gates:
        raise ContractError("r2-gates has no gates")
    seen = set()
    for gate in gates:
        gid = gate.get("id") if isinstance(gate, dict) else None
        if not isinstance(gid, str) or not gid or gid in seen:
            raise ContractError(f"gate id missing or duplicate: {gid!r}")
        seen.add(gid)
        if gate.get("kind") not in GATE_KINDS:
            raise ContractError(f"{gid}: kind must be one of {GATE_KINDS}")
        if not isinstance(gate.get("owner"), str) or not gate["owner"]:
            raise ContractError(f"{gid}: owner missing")
        checks = gate.get("checks", [])
        if not isinstance(checks, list):
            raise ContractError(f"{gid}: checks is not a list")
        for check in checks:
            if not (isinstance(check, dict) and isinstance(check.get("key"), str) and check.get("op") in OPS
                    and isinstance(check.get("limit"), (int, float)) and not isinstance(check.get("limit"), bool)):
                raise ContractError(f"{gid}: invalid check {check!r}")
        tests = gate.get("tests", [])
        if not (isinstance(tests, list) and all(isinstance(t, str) and t for t in tests)):
            raise ContractError(f"{gid}: tests must be full test names")
        if gate["kind"] == "gate" and not checks and not tests:
            raise ContractError(f"{gid}: a gate needs checks or required tests")
        if gate["kind"] == "report" and checks:
            raise ContractError(f"{gid}: a report entry carries no checks (it never fails)")
        modes = gate.get("modes")
        if modes is not None and not (isinstance(modes, list) and modes and len(set(modes)) == len(modes)
                                      and all(m in SCOPE_MODES for m in modes)):
            raise ContractError(f"{gid}: modes is a non-empty list of distinct {SCOPE_MODES}")
        min_samples = gate.get("minSamples")
        if min_samples is not None:
            # C-00 07.10: a gate judged on a worst value or a count reads as a PASS on too few samples — the coverage floor
            if gate["kind"] == "human" or not (isinstance(min_samples, int) and not isinstance(min_samples, bool) and min_samples > 0):
                raise ContractError(f"{gid}: minSamples is a whole number > 0 on a measured gate")
        measured = gate.get("measuredBy")
        if measured is not None:
            if gate["kind"] == "human":
                raise ContractError(f"{gid}: a human gate is reviewed, not measured")
            if not (isinstance(measured, dict) and measured.get("suite") in ("playmode", "editmode")
                    and isinstance(measured.get("filter"), str) and measured["filter"]
                    and isinstance(measured.get("quiet", False), bool)):
                raise ContractError(f"{gid}: measuredBy needs suite playmode|editmode, a test filter and quiet true|false")
            env = measured.get("env", {})
            # the owner's own switches (e.g. DD_KIT_WEAPONS); DD_R2_* belongs to the chain and is never overridden
            if not (isinstance(env, dict) and all(isinstance(k, str) and ENV_NAME.fullmatch(k) and not k.startswith("DD_R2_")
                                                  and isinstance(v, str) for k, v in env.items())):
                raise ContractError(f"{gid}: measuredBy env must map UPPER_CASE names (not DD_R2_*) to strings")
            stray = [v for v in env.values() if set(PLACEHOLDER.findall(v)) - {KPROJ_TOKEN}]
            if stray:
                raise ContractError(f"{gid}: measuredBy env knows only the {KPROJ_TOKEN} placeholder: {stray}")
    return data


def out_of_scope(gate, modes):
    """True when the run covers none of the camera modes the gate is measured in (modes None = every mode)."""
    return modes is not None and bool(gate.get("modes")) and not set(gate["modes"]) & set(modes)


def measurement_steps(contract, quiet, modes=None):
    """The chain's measurement sessions: one per distinct (suite, filter) among the gates whose measuredBy has this
    quiet flag, in contract order; each step lists the gates it is expected to write. Step id = r2-<first gate id>.
    A session is (suite, filter, env): the same test under other switches is another session. modes (wp078_accept
    --modes): a gate out of scope for them is not measured."""
    steps, by_key = [], {}
    for gate in contract["gates"]:
        measured = gate.get("measuredBy")
        if not measured or bool(measured.get("quiet", False)) != quiet or out_of_scope(gate, modes):
            continue
        env = dict(sorted(measured.get("env", {}).items()))
        key = (measured["suite"], measured["filter"], tuple(env.items()))
        if key not in by_key:
            by_key[key] = {"id": "r2-" + gate["id"], "suite": measured["suite"], "filter": measured["filter"],
                           "env": env, "quiet": quiet, "gates": []}
            steps.append(by_key[key])
        by_key[key]["gates"].append(gate["id"])
    return steps


def expand_env(env, kproj):
    """measuredBy env with {KPROJ} replaced by the KADRO project root (dd_paths.KPROJ)."""
    return {k: v.replace(KPROJ_TOKEN, str(kproj)) for k, v in env.items()}


def _number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _check(value, op, limit):
    if op == "<=":
        return value <= limit
    if op == ">=":
        return value >= limit
    return value == limit


def evaluate_gate(gate, commit, measure=None, review=None, cases=None, modes=None):
    """One gate row: {id, kind, owner, status PASS|FAIL|BLOCKED|REPORT|OUT_OF_SCOPE, reason, values}.
    measure/review: the parsed artifacts bound for this gate (None = not bound); cases: {test full name: result} from
    every bound NUnit XML (None = no XML bound); modes: the run's camera modes (None = every mode)."""
    row = {"id": gate["id"], "area": gate.get("area"), "kind": gate["kind"], "owner": gate["owner"], "title": gate.get("title"),
           "status": "PASS", "reason": None, "values": {}}

    def stop(status, reason):
        row["status"], row["reason"] = status, reason
        return row

    if modes is not None and gate.get("modes"):
        row["modes"] = [m for m in gate["modes"] if m in modes]
        if not row["modes"]:
            return stop(OUT_OF_SCOPE, f"{SCOPE_REASON} (measured in {', '.join(gate['modes'])}; this run: {', '.join(modes)})")
        if len(row["modes"]) < len(gate["modes"]):
            row["outOfScopeModes"] = [m for m in gate["modes"] if m not in modes]

    if gate["kind"] == "human":
        if review is None:
            return stop("BLOCKED", "no review artifact (dd-r2-review-v1)")
        if review.get("schema") != "dd-r2-review-v1" or review.get("gate") != gate["id"]:
            return stop("FAIL", "review artifact is not a dd-r2-review-v1 for " + gate["id"])
        if review.get("commit") != commit:
            return stop("FAIL", "review was made on another commit")
        verdict = review.get("verdict")
        row["values"] = {"verdict": verdict, "reviewer": review.get("reviewer")}
        if verdict == "RED":
            return stop("FAIL", "review verdict RED")
        if verdict not in ("KABUL", "KABUL_DÜZELTMELERLE"):
            return stop("FAIL", "unknown review verdict " + repr(verdict))
        must = review.get("must", [])
        if not isinstance(must, list) or not all(isinstance(m, dict) for m in must):
            return stop("FAIL", "review MUST list is malformed")
        open_must = [str(m.get("id")) for m in must if m.get("closed") is not True]
        if open_must:
            return stop("FAIL", "open MUST: " + ", ".join(open_must))
        return row

    for test in gate.get("tests", []):
        if cases is None:
            return stop("BLOCKED", "no NUnit XML bound for required test " + test)
        result = cases.get(test)
        if result is None:
            return stop("BLOCKED", "required test not in the bound XML: " + test)
        if result != "Passed":
            return stop("FAIL", "required test " + result + ": " + test)

    wanted = [c["key"] for c in gate.get("checks", [])] + list(gate.get("report", []))
    if not wanted:
        return row   # a test-only gate, already judged above
    if measure is None:
        return stop("BLOCKED", "no measurement artifact (dd-r2-measure-v1)")
    if measure.get("schema") != "dd-r2-measure-v1" or measure.get("gate") != gate["id"]:
        return stop("FAIL", "measurement artifact is not a dd-r2-measure-v1 for " + gate["id"])
    if measure.get("commit") != commit:
        return stop("FAIL", "measured on another commit")
    values = measure.get("values")
    if not isinstance(values, dict):
        return stop("FAIL", "measurement has no values")
    # "the source must be in the recording" (C-00 05.10): an owner test that counted nothing must not hand the judge the
    # placeholders it started from (S-10 06.10: an S4 run with no weapon wrote float.MaxValue / 0, which pass every check).
    # samples is the R2Olcum count; the chain's P2 / P5 count their sound windows / R2 sessions (>= 1 when written);
    # an artifact without the field is judged as before.
    samples = measure.get("samples")
    if "samples" in measure and not (_number(samples) and samples > 0):   # a null count is no count (gt 06.10 N-9)
        return stop("BLOCKED", f"the measurement counted no samples (samples {samples!r}): nothing was measured")
    row["values"] = {key: values.get(key) for key in wanted}
    if gate["kind"] == "report":
        row["status"] = "REPORT"
        return row
    missing = [c["key"] for c in gate["checks"] if not _number(values.get(c["key"]))]
    if missing:
        return stop("BLOCKED", "measurement lacks numeric " + ", ".join(missing))
    failed = [f"{c['key']} {values[c['key']]} {c['op']} {c['limit']} is false" for c in gate["checks"]
              if not _check(values[c["key"]], c["op"], c["limit"])]
    if failed:
        return stop("FAIL", "; ".join(failed))
    # C-00 07.10: a coverage floor for a worst value or a count — more samples can only make either worse, so a FAIL on
    # few samples stands, but a PASS on fewer than the contract's minSamples is not one (BLOCKED, coverage missing)
    floor = gate.get("minSamples")
    if floor is not None:
        if "samples" not in measure:
            return stop("BLOCKED", f"kapsama bilinmiyor: the measurement has no samples count, the contract wants at least {floor}")
        if samples < floor:
            return stop("BLOCKED", f"kapsama eksik: the measurement counted {samples} samples, the contract wants at least {floor}")
    return row


def summarize(rows):
    """R2 step status: FAIL if any gate/human row FAILs, else BLOCKED if any is BLOCKED, else PASS (reports and
    OUT_OF_SCOPE rows ignored)."""
    judged = [r for r in rows if r["kind"] != "report" and r["status"] != OUT_OF_SCOPE]
    if any(r["status"] == "FAIL" for r in judged):
        return "FAIL"
    if any(r["status"] == "BLOCKED" for r in judged):
        return "BLOCKED"
    return "PASS"
