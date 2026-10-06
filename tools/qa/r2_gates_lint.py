#!/usr/bin/env python3
"""C-00B B-02 (WP-091, C-00 05.10): the R2 gate contract, its prose and its C# writers must say the same thing.

Three places describe one R2 gate:
  - tools/qa/r2-gates.json      the thresholds (checks[].key, report[]), owner, kind, measuredBy, required tests;
  - tools/qa/r2-measure-contract.md   the prose: which gates are human / report, owner splits, paths, test names, keys;
  - the C# tests that call R2Olcum.Yaz(gate, source, values, samples): the keys they actually write.
A gate no test writes may name its owner's Python writer in its prose section ("Writer: `tools/qa/<tool>.py`", WP-085 S3):
the tool must exist and name every contract key of the gate as a string literal.

r2_gates.py judges an artifact against the JSON at acceptance time, when it is too late: a key the test never writes is a
BLOCKED gate on the FINAL run, an extra key is silently ignored, a measuredBy filter that names no test runs an empty
session. This lint reads all three statically (no Unity) and names every disagreement before the chain runs.

Levels: ERROR (the chain will judge wrongly or block), WARN (likely BLOCKED on the FINAL run, or a rule the contract
states is bent), INFO (inventory: report-only keys, planned checks, nullable keys). Exit 0 = no ERROR (WARN too with
--strict), 1 = findings at that level, 2 = an input is missing or the JSON contract itself is invalid.

Usage: python3 tools/qa/r2_gates_lint.py [--repo DIR] [--json] [--strict]
It never writes a file and never edits the contract (thresholds change only by a reviewed commit, r2-measure-contract.md).
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r2_gates  # noqa: E402

PROJECT = "prototype/DeathsDemolition_PlayableConcept"
OWNER = re.compile(r"S-(0[1-9]|10)")
GATE_ID = r"[A-Z][0-9]+[a-z]?"
# measuredBy filters are test.sh --filter regexes; one that is a plain full name is what an [Explicit] test needs
PLAIN_NAME = re.compile(r"[A-Za-z_][\w.]*")
# camelCase words in the prose that are vocabulary, not measurement keys
PROSE_WORDS = {"measuredBy", "reviewVerdicts", "measureSchema", "reviewSchema", "floorLimited"}


class Finding:
    __slots__ = ("level", "code", "gate", "message", "where")

    def __init__(self, level, code, gate, message, where=None):
        self.level, self.code, self.gate, self.message, self.where = level, code, gate, message, where

    def as_dict(self):
        return {"level": self.level, "code": self.code, "gate": self.gate, "message": self.message, "where": self.where}

    def __repr__(self):
        return f"{self.level} {self.code} {self.gate}: {self.message}"


# ---------------------------------------------------------------------------------------------------- C# scanning


def blank_code(text):
    """text with comments and string/char literal contents replaced by spaces (same length, newlines kept), so braces,
    parentheses and commas can be matched on it while offsets still index the original."""
    out = list(text)
    i, n = 0, len(text)

    def wipe(a, b):
        for k in range(a, b):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            wipe(i, j)
            i = j
        elif c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            wipe(i, j)
            i = j
        elif c == "@" and nxt == '"':
            j = i + 2
            while j < n:
                if text[j] == '"' and j + 1 < n and text[j + 1] == '"':
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            wipe(i + 2, j)
            i = j + 1
        elif c == '"' or c == "'":
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            wipe(i + 1, min(j, n))
            i = j + 1
        else:
            i += 1
    return "".join(out)


def match_close(code, start, open_ch, close_ch):
    """index of the bracket closing the one at code[start] (code already blanked); -1 when unbalanced."""
    depth = 0
    for k in range(start, len(code)):
        ch = code[k]
        if ch == open_ch:
            depth += 1
        elif ch == close_ch:
            depth -= 1
            if depth == 0:
                return k
    return -1


GENERIC = re.compile(r"\b\w+\s*<[\w\s,?.<>\[\]]*?>")
CLASS_DECL = re.compile(r"\b(?:class|struct)\s+(\w+)[^{;]*\{")
METHOD_DECL = re.compile(r"\b(?:public|private|internal|protected)\s+(?:static\s+|async\s+|override\s+|virtual\s+)*"
                         r"(?:[\w.]+(?:<[^<>()]*>)?(?:\[\])?\??)\s+(\w+)\s*\(")
NAMESPACE = re.compile(r"\bnamespace\s+([\w.]+)")
TEST_ATTR = re.compile(r"\b(?:Test|UnityTest|TestCase|TestCaseSource)\b")
EXPLICIT_ATTR = re.compile(r"\bExplicit\b")


def _attributes_before(code, pos):
    """the attribute text ([...] blocks, possibly several lines) right above the declaration at pos."""
    k = code.rfind("\n", 0, pos)
    block = []
    line_start = k
    while line_start > 0:
        prev = code.rfind("\n", 0, line_start)
        line = code[prev + 1:line_start].strip()
        if not line:
            line_start = prev
            continue   # a blanked doc comment or an empty line
        if line.startswith("[") and line.endswith("]"):
            block.append(line)
            line_start = prev
            continue
        break
    head = code[k + 1:pos]   # attributes on the declaration's own line, e.g. "[Test] public void X()"
    return " ".join(block) + " " + head


class CsFile:
    def __init__(self, path, rel, text):
        self.path, self.rel, self.text = path, rel, text
        self.code = blank_code(text)
        m = NAMESPACE.search(self.code)
        self.namespace = m.group(1) if m else ""
        self.classes = []    # (name, body_start, body_end, explicit)
        for cm in CLASS_DECL.finditer(self.code):
            brace = cm.end() - 1
            end = match_close(self.code, brace, "{", "}")
            attrs = _attributes_before(self.code, cm.start())
            self.classes.append((cm.group(1), brace, end if end > 0 else len(self.code), bool(EXPLICIT_ATTR.search(attrs))))
        self.methods = []    # dicts: name, start, body_start, body_end, test, explicit, full
        for mm in METHOD_DECL.finditer(self.code):
            if mm.group(1) in ("if", "for", "foreach", "while", "switch", "using", "return", "new"):
                continue
            paren = mm.end() - 1
            close = match_close(self.code, paren, "(", ")")
            if close < 0:
                continue
            rest = self.code[close + 1:close + 200].lstrip()
            if not rest.startswith("{"):
                continue   # an abstract/extern declaration or an expression-bodied member
            body = close + 1 + (len(self.code[close + 1:close + 200]) - len(rest))
            end = match_close(self.code, body, "{", "}")
            attrs = _attributes_before(self.code, mm.start())
            chain = self.class_chain(mm.start())
            is_test = bool(TEST_ATTR.search(attrs)) and bool(chain)
            explicit = bool(EXPLICIT_ATTR.search(attrs)) or any(c[3] for c in chain)
            full = ".".join(filter(None, [self.namespace, "+".join(c[0] for c in chain), mm.group(1)]))
            self.methods.append({"name": mm.group(1), "start": mm.start(), "body_start": body,
                                 "body_end": end if end > 0 else len(self.code), "test": is_test, "explicit": explicit,
                                 "full": full, "line": self.line(mm.start())})

    def class_chain(self, pos):
        return [c for c in self.classes if c[1] < pos < c[2]]

    def enclosing_method(self, pos):
        inside = [m for m in self.methods if m["body_start"] < pos < m["body_end"]]
        return max(inside, key=lambda m: m["body_start"]) if inside else None

    def line(self, pos):
        return self.text.count("\n", 0, pos) + 1


def _split_args(code_slice):
    flat = GENERIC.sub(lambda m: "G" * len(m.group(0)), code_slice)
    args, depth, last = [], 0, 0
    for k, ch in enumerate(flat):
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            args.append((last, k))
            last = k + 1
    args.append((last, len(flat)))
    return args


STRING_LIT = re.compile(r'^\s*"([^"\\]*)"\s*$')
IDENT = re.compile(r"^\s*([A-Za-z_][\w.]*)\s*$")
INIT_KEY = re.compile(r'\{\s*"(\w+)"\s*,\s*([^,}]*)')
INDEX_KEY = r'\b{v}\s*\[\s*"(\w+)"\s*\]\s*='
ADD_KEY = r'\b{v}\s*\.\s*Add\s*\(\s*"(\w+)"'


def _initializer_keys(text):
    keys, nullable = [], []
    for km in INIT_KEY.finditer(text):
        keys.append(km.group(1))
        if km.group(2).strip() == "null":
            nullable.append(km.group(1))
    return keys, nullable


def _resolve_string(ident, cs, all_files):
    name = ident.split(".")[-1]
    pat = re.compile(r'\b(?:const\s+string|string|var)\s+' + re.escape(name) + r'\s*=\s*"([^"\\]*)"\s*;')
    m = pat.search(cs.text)
    if m:
        return m.group(1)
    if "." in ident:
        for other in all_files:
            m = pat.search(other.text)
            if m:
                return m.group(1)
    return None


def scan_writes(files):
    """every R2Olcum.Yaz call: gate, source, unconditional keys, conditional keys, nullable keys, enclosing test."""
    writes = []
    for cs in files:
        for m in re.finditer(r"\bR2Olcum\s*\.\s*Yaz\s*\(", cs.code):
            paren = m.end() - 1
            close = match_close(cs.code, paren, "(", ")")
            if close < 0:
                continue
            code_args = cs.code[paren + 1:close]
            text_args = cs.text[paren + 1:close]
            spans = _split_args(code_args)
            arg = [text_args[a:b].strip() for a, b in spans]
            w = {"file": cs.rel, "line": cs.line(m.start()), "gate": None, "source": None, "source_expr": None,
                 "keys": [], "conditional": [], "nullable": [], "values_expr": None, "test": None, "explicit": False,
                 "helper": None}
            if len(arg) < 4:
                w["problem"] = "R2Olcum.Yaz with fewer than four arguments"
                writes.append(w)
                continue
            gm = STRING_LIT.match(arg[0])
            w["gate"] = gm.group(1) if gm else None
            if w["gate"] is None:
                w["problem"] = "gate id is not a string literal: " + arg[0]
            sm = STRING_LIT.match(arg[1])
            if sm:
                w["source"] = sm.group(1)
            else:
                im = IDENT.match(arg[1])
                w["source_expr"] = arg[1]
                w["source"] = _resolve_string(im.group(1), cs, files) if im else None
            encl = cs.enclosing_method(m.start())
            values = arg[2]
            w["values_expr"] = values if len(values) < 80 else values[:77] + "..."
            vm = IDENT.match(values)
            if vm and encl:
                var = vm.group(1)
                region_text = cs.text[encl["body_start"]:m.start()]
                region_code = cs.code[encl["body_start"]:m.start()]
                dm = re.search(r"\b" + re.escape(var) + r"\s*=\s*new\s+(?:Dictionary\s*<[^>]*>)?\s*\(?\)?\s*\{", region_code)
                if dm:
                    brace = dm.end() - 1
                    end = match_close(region_code, brace, "{", "}")
                    w["keys"], w["nullable"] = _initializer_keys(region_text[brace:end + 1])
                w["conditional"] = [k for k in re.findall(INDEX_KEY.format(v=re.escape(var)), region_text)
                                    + re.findall(ADD_KEY.format(v=re.escape(var)), region_text) if k not in w["keys"]]
            else:
                w["keys"], w["nullable"] = _initializer_keys(values)
            if encl:
                if encl["test"]:
                    w["test"], w["explicit"] = encl["full"], encl["explicit"]
                else:
                    w["helper"] = encl["full"]
            writes.append(w)
    return writes


def load_cs(repo):
    root = Path(repo) / PROJECT / "Assets"
    files = []
    for path in sorted(root.rglob("*.cs")):
        try:
            text = path.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeDecodeError):
            continue
        files.append(CsFile(path, str(path.relative_to(repo)), text))
    return files


# ---------------------------------------------------------------------------------------------------- prose scanning


def strip_fences(text):
    return re.sub(r"```.*?```", "", text, flags=re.S)


def parse_prose(text):
    """facts the contract prose states: id lists, owner splits, gate sections, backticked paths/names/keys/checks."""
    body = strip_fences(text)
    facts = {"human": None, "report": None, "tests_examples": None, "owners": {}, "sections": {}, "chain_made": set(),
             "tool_writers": {}, "paths": [], "test_names": []}
    m = re.search(r"^\s*((?:" + GATE_ID + r",\s*)*" + GATE_ID + r"):\s*one review file", body, re.M)
    if m:
        facts["human"] = re.findall(GATE_ID, m.group(1))
    m = re.search(r'`kind:\s*"report"`\s*\(([^)]*)\)', body)
    if m:
        facts["report"] = re.findall(GATE_ID, m.group(1))
    m = re.search(r"`tests` in `r2-gates\.json`, e\.g\.\s*([^)]*)\)", body)
    if m:
        facts["tests_examples"] = re.findall(GATE_ID, m.group(1))
    for om in re.finditer(r"\b(" + GATE_ID + r")\s*=\s*[^=;()]*?\((S-\d\d)\)", body):
        facts["owners"][om.group(1)] = om.group(2)
    parts = re.split(r"^## ", body, flags=re.M)
    for part in parts[1:]:
        head, _, rest = part.partition("\n")
        hm = re.match(r"(" + GATE_ID + r")\b", head)
        if not hm:
            continue
        gid = hm.group(1)
        ticks = re.findall(r"`([^`]+)`", head + "\n" + rest)
        facts["sections"][gid] = {
            "keys": sorted({t for t in ticks if re.fullmatch(r"[a-z][a-z0-9]*[A-Z]\w*", t) and t not in PROSE_WORDS}),
            "checks": [(c.group(1), c.group(2), float(c.group(3))) for t in ticks
                       for c in [re.fullmatch(r"(\w+)\s*(<=|>=|==)\s*(-?[0-9.]+)", t)] if c],
            "report_words": bool(re.search(r"\breport(?:ed|s)?\b", head + rest, re.I)),
        }
        if re.search(r"No owner test", rest):
            facts["chain_made"].add(gid)
        wm = re.search(r"\bWriter:\s*`(tools/qa/[\w.-]+\.py)`", head + "\n" + rest)
        if wm:
            facts["tool_writers"][gid] = wm.group(1)
    for t in re.findall(r"`([^`\s]+)`", body):
        # r2-P5.json and the like are artifact names the chain writes, not repo files
        if re.search(r"\.(cs|py|json|sh|md)$", t) and not t.startswith(("K/", "<", "r2-")) and "<" not in t:
            facts["paths"].append(t)
        elif re.fullmatch(r"(?:[A-Z]\w*\.)*[A-Z]\w*Tests\.\w+", t) or (
                re.fullmatch(r"[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+", t) and re.search(r"[a-z]", t)):   # not DD_R2_COMMIT
            facts["test_names"].append(t)
    return facts


def _resolve_path(repo, rel):
    repo = Path(repo)
    for base in (repo, repo / PROJECT, repo / "tools", repo / "tools" / "qa"):
        if (base / rel).exists():
            return base / rel
    return None


# ---------------------------------------------------------------------------------------------------- the lint


def lint(contract, prose, files, writes, repo=None):
    out = []

    def add(level, code, gate, message, where=None):
        out.append(Finding(level, code, gate, message, where))

    gates = {g["id"]: g for g in contract["gates"]}
    tests = {}
    for cs in files:
        for meth in cs.methods:
            if meth["test"]:
                tests.setdefault(meth["full"], (cs.rel, meth["line"], meth["explicit"]))
    by_gate = {}
    for w in writes:
        where = f"{w['file']}:{w['line']}"
        if w.get("problem"):
            add("ERROR", "WRITER-UNPARSED", w.get("gate") or "?", w["problem"], where)
            continue
        by_gate.setdefault(w["gate"], []).append(w)

    # --- the JSON alone: owners, report-only markers, overlap
    for gid, g in gates.items():
        if not OWNER.fullmatch(g["owner"]):
            add("ERROR", "OWNER", gid, f"owner {g['owner']!r} is not a session S-01..S-10")
        check_keys = [c["key"] for c in g.get("checks", [])]
        report_keys = list(g.get("report", []))
        dup = sorted({k for k in check_keys if check_keys.count(k) > 1} | (set(check_keys) & set(report_keys)))
        if dup:
            add("ERROR", "KEY-TWICE", gid, "key listed twice (checks/report): " + ", ".join(dup))
        if g["kind"] == "report":
            add("INFO", "REPORT-GATE", gid, "report entry (never fails): " + (", ".join(report_keys) or "no keys"))
        elif report_keys:
            add("INFO", "REPORT-ONLY", gid, "report-only keys next to the checks: " + ", ".join(report_keys))
        if g["kind"] == "gate" and not check_keys and g.get("tests"):
            add("INFO", "TEST-ONLY", gid, "gate judged on required tests alone: " + ", ".join(g["tests"]))

    # --- writers vs the JSON
    for gid, ws in sorted(by_gate.items()):
        where = ", ".join(f"{w['file']}:{w['line']}" for w in ws)
        g = gates.get(gid)
        if g is None:
            add("ERROR", "GATE-UNKNOWN", gid, "R2Olcum.Yaz writes a gate that r2-gates.json does not have", where)
            continue
        if g["kind"] == "human":
            add("ERROR", "WRITER-HUMAN", gid, "a human gate is reviewed, but a test writes a measurement for it", where)
            continue
        wanted = [c["key"] for c in g.get("checks", [])] + list(g.get("report", []))
        check_keys = {c["key"] for c in g.get("checks", [])}
        written, maybe = set(), set()
        for w in ws:
            written.update(w["keys"])
            maybe.update(w["conditional"])
            if not w["keys"] and not w["conditional"]:
                add("ERROR", "WRITER-KEYS", gid, f"cannot read the keys of {w['values_expr']!r}", f"{w['file']}:{w['line']}")
        missing = [k for k in wanted if k not in written and k not in maybe]
        if missing:
            add("ERROR", "KEY-MISSING", gid, "contract key never written by the test (BLOCKED at acceptance): "
                + ", ".join(missing), where)
        cond_checks = sorted(k for k in maybe - written if k in check_keys)
        if cond_checks:
            add("WARN", "KEY-CONDITIONAL", gid, "a check key is written only on some paths: " + ", ".join(cond_checks), where)
        extra = sorted((written | maybe) - set(wanted))
        if extra:
            add("ERROR", "KEY-EXTRA", gid, "written but not in r2-gates.json (contract: values carry exactly the gate's "
                "keys; the judge ignores them): " + ", ".join(extra), where)
        for w in ws:
            wh = f"{w['file']}:{w['line']}"
            for k in w["nullable"]:
                add("INFO", "KEY-NULL", gid, f"{k} written as null on one path ({w.get('values_expr')}) — BLOCKED by design", wh)
            if w["source"] is None:
                add("ERROR", "SOURCE-UNRESOLVED", gid, f"source {w['source_expr']!r} is not a string the lint can resolve", wh)
            elif w["test"] and w["source"] != w["test"]:
                add("ERROR", "SOURCE-MISMATCH", gid, f"source {w['source']} but the call sits in {w['test']}", wh)
            elif w["helper"] and w["source"] not in tests:
                add("ERROR", "SOURCE-MISMATCH", gid, f"source {w['source']} (written from helper {w['helper']}) is not a test", wh)
        sources = sorted({w["source"] for w in ws if w["source"]})
        if len(sources) > 1:
            add("WARN", "SOURCE-MANY", gid, "several tests write this gate (last one wins): " + ", ".join(sources), where)
        measured = g.get("measuredBy")
        if measured:
            pat = _filter_regex(measured["filter"])
            for s in sources:
                if pat and not pat.search(s):
                    add("ERROR", "MEASUREDBY-SOURCE", gid, f"measuredBy filter {measured['filter']!r} does not run the "
                        f"writer {s}", where)
        else:
            for w in ws:
                if w["explicit"]:
                    add("WARN", "EXPLICIT-UNRUN", gid, f"the writer {w['test']} is [Explicit] and the gate has no "
                        "measuredBy: the chain's sessions never run it, so <E>/r2/ gets the artifact only from a hand run",
                        f"{w['file']}:{w['line']}")

    # --- measuredBy and required tests vs the C# tests
    for gid, g in gates.items():
        measured = g.get("measuredBy")
        if measured:
            pat = _filter_regex(measured["filter"])
            hits = [t for t in tests if pat and pat.search(t)] if pat else []
            if pat is None:
                add("ERROR", "MEASUREDBY-REGEX", gid, f"measuredBy filter is not a valid regex: {measured['filter']!r}")
            elif not hits:
                add("ERROR", "MEASUREDBY-NOTEST", gid, f"measuredBy filter {measured['filter']!r} names no test in the repo")
            else:
                for t in hits:
                    if tests[t][2] and measured["filter"] != t:
                        add("ERROR", "EXPLICIT-FILTER", gid, f"{t} is [Explicit]: the filter must be its full name, "
                            f"not {measured['filter']!r}")
                suite_dir = "/EditMode/" if measured["suite"] == "editmode" else "/PlayMode/"
                wrong = [t for t in hits if suite_dir not in tests[t][0]]
                if wrong:
                    add("ERROR", "MEASUREDBY-SUITE", gid, f"suite {measured['suite']} but the test lives elsewhere: "
                        + ", ".join(f"{t} ({tests[t][0]})" for t in wrong))
        for t in g.get("tests", []):
            if t not in tests:
                add("ERROR", "TEST-MISSING", gid, "required test not found in the repo: " + t)
            elif tests[t][2]:
                covered = any(_filter_covers(o.get("measuredBy"), t) for o in gates.values())
                if not covered:
                    add("WARN", "TEST-EXPLICIT", gid, f"required test {t} is [Explicit] and no measuredBy session names "
                        "it: it is in a bound XML only if a session runs it by name")
        if g["kind"] in ("gate", "report") and gid not in by_gate:
            wanted = [c["key"] for c in g.get("checks", [])] + list(g.get("report", []))
            if not wanted:
                continue
            if gid in prose["chain_made"]:
                add("INFO", "CHAIN-MADE", gid, "artifact written by the chain itself (no owner test)")
            elif measured:
                add("ERROR", "NO-WRITER", gid, f"measuredBy runs {measured['filter']} but no R2Olcum.Yaz writes this "
                    f"gate (owner {g['owner']})")
            elif gid in prose.get("tool_writers", {}):
                _tool_writer(add, gid, g, prose["tool_writers"][gid], wanted, repo)
            else:
                add("WARN", "NO-WRITER", gid, f"no test writes this gate yet and no measuredBy (owner {g['owner']}): "
                    "BLOCKED on the FINAL run unless the owner's own run drops it into <E>/r2/")

    # --- the prose vs the JSON
    kinds = {k: sorted(gid for gid, g in gates.items() if g["kind"] == k) for k in ("human", "report")}
    for kind in ("human", "report"):
        listed = prose.get(kind)
        if listed is None:
            add("WARN", "PROSE-LIST", "-", f"the prose list of {kind} gates was not found")
        elif sorted(listed) != kinds[kind]:
            add("ERROR", "PROSE-LIST", "-", f"prose {kind} gates {sorted(listed)} but r2-gates.json has {kinds[kind]}")
    for gid in prose.get("tests_examples") or []:
        if gid not in gates:
            add("ERROR", "PROSE-GATE", gid, "named in the prose's required-tests list but not in r2-gates.json")
        elif not gates[gid].get("tests"):
            add("ERROR", "PROSE-TESTS", gid, "the prose says it requires a test identity, r2-gates.json has no tests")
    for gid, owner in prose["owners"].items():
        if gid in gates and gates[gid]["owner"] != owner:
            add("ERROR", "PROSE-OWNER", gid, f"prose owner {owner}, r2-gates.json owner {gates[gid]['owner']}")
    for gid, sec in prose["sections"].items():
        g = gates.get(gid)
        if g is None:
            add("ERROR", "PROSE-GATE", gid, "the prose has a section for a gate r2-gates.json does not have")
            continue
        keys = {c["key"]: c for c in g.get("checks", [])}
        report = set(g.get("report", []))
        unknown = [k for k in sec["keys"] if k not in keys and k not in report]
        if unknown:
            add("ERROR", "PROSE-KEY", gid, "the prose section names keys the gate does not have: " + ", ".join(unknown))
        for key, op, limit in sec["checks"]:
            if key in keys:
                c = keys[key]
                if c["op"] != op or float(c["limit"]) != limit:
                    add("ERROR", "PROSE-LIMIT", gid, f"prose says {key} {op} {limit:g}, r2-gates.json {c['op']} {c['limit']}")
            elif key in report:
                add("INFO", "PLANNED-CHECK", gid, f"{key} is report-only now; the prose plans a check {key} {op} {limit:g}")
            else:
                add("ERROR", "PROSE-KEY", gid, f"the prose names a check on {key}, the gate has no such key")
    if repo is not None:
        for p in sorted(set(prose["paths"])):
            if _resolve_path(repo, p) is None:
                add("ERROR", "PROSE-PATH", "-", f"the prose names a file that is not in the repo: {p}")
    for name in sorted(set(prose["test_names"])):
        if not any(t == name or t.endswith("." + name) for t in tests):
            add("ERROR", "PROSE-TEST", "-", f"the prose names a test that is not in the repo: {name}")
    order = {"ERROR": 0, "WARN": 1, "INFO": 2}
    out.sort(key=lambda f: (order[f.level], f.gate, f.code))
    return out


def _tool_writer(add, gid, gate, tool, wanted, repo):
    """a gate whose artifact the owner's Python tool writes (the prose names it): the tool exists and names every key."""
    path = _resolve_path(repo, tool) if repo is not None else None
    if repo is not None and path is None:
        add("ERROR", "TOOL-WRITER", gid, f"the prose names the writer {tool}, which is not in the repo")
        return
    text = path.read_text(encoding="utf-8") if path is not None else ""
    missing = [k for k in wanted if path is not None and not re.search(r"[\"']" + re.escape(k) + r"[\"']", text)]
    if missing:
        add("ERROR", "KEY-MISSING", gid, f"contract key never named by the writer {tool} (BLOCKED at acceptance): "
            + ", ".join(missing))
    else:
        add("INFO", "TOOL-WRITER", gid, f"artifact written by the owner's tool {tool} (owner {gate['owner']}, no C# writer): "
            "<E>/r2/ gets it from the owner's run")


def _filter_regex(text):
    try:
        return re.compile(text)
    except re.error:
        return None


def _filter_covers(measured, test):
    if not measured:
        return False
    pat = _filter_regex(measured["filter"])
    return bool(pat and pat.search(test))


# ---------------------------------------------------------------------------------------------------- CLI


def run(repo):
    repo = Path(repo)
    gates_path = repo / "tools/qa/r2-gates.json"
    prose_path = repo / "tools/qa/r2-measure-contract.md"
    contract = r2_gates.load_contract(gates_path.read_text(encoding="utf-8"))
    prose = parse_prose(prose_path.read_text(encoding="utf-8"))
    files = load_cs(repo)
    writes = scan_writes(files)
    return lint(contract, prose, files, writes, repo), writes


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]))
    ap.add_argument("--json", action="store_true", help="dd-r2-gates-lint-v1 JSON on stdout")
    ap.add_argument("--strict", action="store_true", help="WARN fails too")
    args = ap.parse_args(argv)
    try:
        findings, writes = run(args.repo)
    except (OSError, ValueError) as exc:   # a missing file, bad JSON, or r2_gates.ContractError
        print(f"r2_gates_lint: {exc}", file=sys.stderr)
        return 2
    counts = {lvl: sum(1 for f in findings if f.level == lvl) for lvl in ("ERROR", "WARN", "INFO")}
    if args.json:
        print(json.dumps({"schema": "dd-r2-gates-lint-v1", "counts": counts, "writers": len(writes),
                          "findings": [f.as_dict() for f in findings]}, ensure_ascii=False, indent=1))
    else:
        for f in findings:
            print(f"{f.level:5} {f.code:18} {f.gate:4} {f.message}" + (f"  [{f.where}]" if f.where else ""))
        print(f"r2_gates_lint: {len(writes)} R2Olcum.Yaz calls · ERROR {counts['ERROR']} · WARN {counts['WARN']} · INFO {counts['INFO']}")
    failing = counts["ERROR"] + (counts["WARN"] if args.strict else 0)
    return 1 if failing else 0


if __name__ == "__main__":
    sys.exit(main())
