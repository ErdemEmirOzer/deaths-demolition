#!/usr/bin/env python3
"""Scan RC artefact logs and new macOS crash reports for actionable failures.

Fail-closed (WP-091 S-10, B-09 defects LS-1..LS-3, C-00 06.10): a log that exists but cannot be read is a FAIL, not a clean
file; only this tool's own outputs are skipped (by name, not by suffix); a root with no log at all proves nothing and FAILs."""
import argparse
import json
import os
import re
import time

OWN_OUTPUTS = ("log-scan.txt", "rc-report.txt")

PATTERNS = [
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\bNullReferenceException\b",
        r"\bArgument(?:OutOfRange|Null)?Exception\b",
        # no InvalidOperationException: three green EditMode tests throw it on purpose into the event buses, which log it
        # with Debug.LogException (TierAbilitiesTests "kötü dinleyici", UiEventsTests "abone hatası", UiNavigationModelTests
        # "test-dinleyici") — 48 lines in 16 of 43 real CI/A1 roots, so the pattern failed every RC (gt RF-1, S-10 06.10).
        # ArgumentNull / MissingReference: 0 lines in the same 43 roots.
        r"\bMissingReferenceException\b",
        r"\bIndexOutOfRangeException\b",
        r"\bUnhandled exception\b",
        r"\bNative Crash Reporting\b",
        r"\bCrash!!!",   # LS-3: no trailing \b — "!" is no word character, so "Crash!!!\b" never matched Unity's line
        r"\bSegmentation fault\b",
        r"\berror CS\d+\b",
        r"\bShader error in\b",
        r"\bBuildFailedException\b",
        r"\[QaRunBot\] write failed",
        r"\[QaReadabilityGate\] write failed",
        r"\bWITH FAILURES\b",
        r"\bAssertion failed\b",
    )
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("root")
    parser.add_argument("--since", type=int, required=True)
    args = parser.parse_args()
    hits = []
    unreadable = []
    scanned = 0
    for base, _, files in os.walk(args.root):
        for name in files:
            if not name.endswith((".log", ".txt")):
                continue
            path = os.path.join(base, name)
            if name in OWN_OUTPUTS:   # LS-2: this tool's own outputs, by name — "backlog-scan.txt" is a log to scan
                continue
            try:
                with open(path, encoding="utf-8", errors="replace") as handle:
                    for number, line in enumerate(handle, 1):
                        if any(pattern.search(line) for pattern in PATTERNS):
                            hits.append({"path": os.path.relpath(path, args.root), "line": number, "text": line.strip()[:500]})
            except OSError as error:   # LS-1: a log nobody could read was not scanned, so it cannot count as clean
                unreadable.append({"path": os.path.relpath(path, args.root), "error": error.strerror or str(error)})
                continue
            scanned += 1

    crash_dir = os.path.expanduser("~/Library/Logs/DiagnosticReports")
    crashes = []
    if os.path.isdir(crash_dir):
        for name in os.listdir(crash_dir):
            path = os.path.join(crash_dir, name)
            try:
                if os.path.getmtime(path) >= args.since and "Deaths" in name:
                    crashes.append(path)
            except OSError:
                pass

    report = {
        "schema": "dd-log-scan-v1",
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "pass": not hits and not crashes and not unreadable and scanned > 0,
        "filesScanned": scanned,
        "unreadable": unreadable,
        "matches": hits,
        "newCrashReports": crashes,
    }
    with open(os.path.join(args.root, "log-scan.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    with open(os.path.join(args.root, "log-scan.txt"), "w", encoding="utf-8") as handle:
        handle.write(f"{'PASS' if report['pass'] else 'FAIL'} logs={scanned} matches={len(hits)} crashes={len(crashes)}\n")
        if scanned == 0:
            handle.write("no log scanned: an empty root proves nothing\n")
        for item in unreadable:
            handle.write(f"unreadable: {item['path']}: {item['error']}\n")
        for hit in hits[:100]:
            handle.write(f"{hit['path']}:{hit['line']}: {hit['text']}\n")
        for crash in crashes:
            handle.write(f"crash: {crash}\n")
    print(f"{'PASS' if report['pass'] else 'FAIL'} logs={scanned} matches={len(hits)} crashes={len(crashes)}")
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
