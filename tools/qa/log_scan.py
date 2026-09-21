#!/usr/bin/env python3
"""Scan RC artefact logs and new macOS crash reports for actionable failures."""
import argparse
import json
import os
import re
import time


PATTERNS = [
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\bNullReferenceException\b",
        r"\bArgument(?:OutOfRange)?Exception\b",
        r"\bIndexOutOfRangeException\b",
        r"\bUnhandled exception\b",
        r"\bNative Crash Reporting\b",
        r"\bCrash!!!\b",
        r"\bSegmentation fault\b",
        r"\berror CS\d+\b",
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
    scanned = 0
    for base, _, files in os.walk(args.root):
        for name in files:
            if not name.endswith((".log", ".txt")):
                continue
            path = os.path.join(base, name)
            if path.endswith(("log-scan.txt", "rc-report.txt")):
                continue
            scanned += 1
            try:
                with open(path, encoding="utf-8", errors="replace") as handle:
                    for number, line in enumerate(handle, 1):
                        if any(pattern.search(line) for pattern in PATTERNS):
                            hits.append({"path": os.path.relpath(path, args.root), "line": number, "text": line.strip()[:500]})
            except OSError:
                pass

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
        "pass": not hits and not crashes,
        "filesScanned": scanned,
        "matches": hits,
        "newCrashReports": crashes,
    }
    with open(os.path.join(args.root, "log-scan.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    with open(os.path.join(args.root, "log-scan.txt"), "w", encoding="utf-8") as handle:
        handle.write(f"{'PASS' if report['pass'] else 'FAIL'} logs={scanned} matches={len(hits)} crashes={len(crashes)}\n")
        for hit in hits[:100]:
            handle.write(f"{hit['path']}:{hit['line']}: {hit['text']}\n")
        for crash in crashes:
            handle.write(f"crash: {crash}\n")
    print(f"{'PASS' if report['pass'] else 'FAIL'} logs={scanned} matches={len(hits)} crashes={len(crashes)}")
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
