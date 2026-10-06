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


def measurement_steps(contract, quiet):
    """The chain's measurement sessions: one per distinct (suite, filter) among the gates whose measuredBy has this
    quiet flag, in contract order; each step lists the gates it is expected to write. Step id = r2-<first gate id>.
    A session is (suite, filter, env): the same test under other switches is another session."""
    steps, by_key = [], {}
    for gate in contract["gates"]:
        measured = gate.get("measuredBy")
        if not measured or bool(measured.get("quiet", False)) != quiet:
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


def evaluate_gate(gate, commit, measure=None, review=None, cases=None):
    """One gate row: {id, kind, owner, status PASS|FAIL|BLOCKED|REPORT, reason, values}.
    measure/review: the parsed artifacts bound for this gate (None = not bound); cases: {test full name: result} from
    every bound NUnit XML (None = no XML bound)."""
    row = {"id": gate["id"], "area": gate.get("area"), "kind": gate["kind"], "owner": gate["owner"], "title": gate.get("title"),
           "status": "PASS", "reason": None, "values": {}}

    def stop(status, reason):
        row["status"], row["reason"] = status, reason
        return row

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
    return row


def summarize(rows):
    """R2 step status: FAIL if any gate/human row FAILs, else BLOCKED if any is BLOCKED, else PASS (reports ignored)."""
    judged = [r for r in rows if r["kind"] != "report"]
    if any(r["status"] == "FAIL" for r in judged):
        return "FAIL"
    if any(r["status"] == "BLOCKED" for r in judged):
        return "BLOCKED"
    return "PASS"
