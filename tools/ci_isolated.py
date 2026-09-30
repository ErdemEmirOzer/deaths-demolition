#!/usr/bin/env python3
"""Codex-audit M-11 (C-00 30.09) · main-ci PlayMode filters from tools/ci-suites.txt + tools/ci-isolated.txt.

ci-isolated.txt: one test per line, TAB-separated `<regex>\t<where>\t<why>`; # = comment. <regex> is matched like Unity's
-testFilter (a .NET regex, search semantics, against the test's full name, e.g.
DD.Art.Tests.PlayMode.ArmorTests.Run_AP005v2_…); <where> = `alone` (main-ci runs it in its own PlayMode step after the core)
or `a3` (not in main-ci at all; S-10's A3 run). Every listed test is excluded from the core filter.

usage: ci_isolated.py check        <ci-isolated.txt>                   → exit 0 / 2 (malformed, reason on stderr)
       ci_isolated.py core-filter  <ci-isolated.txt> <ci-suites.txt>   → the core step's -testFilter
       ci_isolated.py alone        <ci-isolated.txt>                   → one `<step id>\t<filter>` line per `alone` entry"""
import re
import sys

WHERE = ("alone", "a3")


class Malformed(ValueError):
    pass


def parse_isolated(path):
    """[(pattern, where, why)] in file order; Malformed on any bad line (nothing is half-applied)."""
    entries = []
    try:
        lines = open(path, encoding="utf-8").read().splitlines()
    except FileNotFoundError:
        return entries   # no file → nothing isolated (the old behaviour)
    for n, line in enumerate(lines, 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        cols = line.split("\t")
        if len(cols) < 3 or not cols[2].strip():
            raise Malformed(f"{path}:{n}: expected <regex>\\t<where>\\t<why>")
        pattern, where = cols[0].strip(), cols[1].strip()
        if where not in WHERE:
            raise Malformed(f"{path}:{n}: where '{where}' is not one of {'/'.join(WHERE)}")
        validate_pattern(pattern, f"{path}:{n}")
        entries.append((pattern, where, cols[2].strip()))
    return entries


def validate_pattern(pattern, where="pattern"):
    if not pattern or any(c.isspace() for c in pattern):
        raise Malformed(f"{where}: empty pattern or whitespace in it")
    if ";" in pattern:   # Unity splits -testFilter on ';' into separate filters
        raise Malformed(f"{where}: ';' is not allowed (Unity splits -testFilter on it)")
    if "(?P" in pattern:   # Python-only named-group syntax; Unity compiles the filter with .NET Regex
        raise Malformed(f"{where}: '(?P' is Python-only syntax (.NET: (?<name>…))")
    try:
        rx = re.compile(pattern)
    except re.error as e:
        raise Malformed(f"{where}: not a regex: {e}")
    if rx.search("") is not None or rx.search("DD") is not None:
        raise Malformed(f"{where}: pattern matches (almost) anything — it would empty the core")
    return rx


def read_suites(path):
    """ci-suites.txt: one fixture class name per line; `#` starts a comment (whole line or trailing)."""
    names = []
    for n, line in enumerate(open(path, encoding="utf-8").read().splitlines(), 1):
        name = line.split("#", 1)[0].strip()
        if not name:
            continue
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
            raise Malformed(f"{path}:{n}: '{name}' is not a fixture class name")
        names.append(name)
    if not names:
        raise Malformed(f"{path}: no suites")
    return names


def core_filter(suites, entries):
    """`\\.(S1|S2|…)\\.` as before, prefixed by a negative lookahead that drops every isolated test:
    ^(?!.*(?:p1)|.*(?:p2)…) — at position 0 `.*(?:p)` succeeds iff re.search(p, name) would, so a test is excluded
    exactly when its `alone` filter (the bare pattern) matches it."""
    body = r"\.(" + "|".join(suites) + r")\."
    if not entries:
        return body
    return "^(?!" + "|".join(f".*(?:{p})" for p, _, _ in entries) + ")" + ".*" + body


def alone_steps(entries):
    """[(step id, filter)] for `alone` entries; id = playmode-alone-<n>-<last name segment>, also the out dir name."""
    steps = []
    for p, where, _ in entries:
        if where != "alone":
            continue
        m = re.match(r"\^?(\w+)", p.split("\\.")[-1])   # the test method name: last `\.` segment, leading identifier
        tail = m.group(1)[:60] if m else "test"
        steps.append((f"playmode-alone-{len(steps) + 1}-{tail}", f"(?:{p})"))
    return steps


def main(argv):
    try:
        if len(argv) == 3 and argv[1] == "check":
            parse_isolated(argv[2])
        elif len(argv) == 4 and argv[1] == "core-filter":
            print(core_filter(read_suites(argv[3]), parse_isolated(argv[2])))
        elif len(argv) == 3 and argv[1] == "alone":
            for sid, flt in alone_steps(parse_isolated(argv[2])):
                print(f"{sid}\t{flt}")
        else:
            print(__doc__.split("\n\n")[-1], file=sys.stderr)
            return 64
    except (Malformed, OSError) as e:
        print(f"ci-isolated: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
