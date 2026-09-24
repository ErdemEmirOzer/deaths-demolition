#!/usr/bin/env python3
"""WP-066M · attribution for tools/userdata-guard.sh restore: which Player wrote each changed user-data file.

players.log (the guard's 5 s sampler): "<utc epoch>\t<pid> <command line>". A changed file (a line of
after-run-<area>.txt that is not in before-<area>.txt) is attributed to every Player seen within ±10 s of its
mtime; the -logFile argument (else the .app path) names the session/run. Output: <dir>/attribution.txt.
"""
import os
import re
import sys
import time

d = sys.argv[1]
seen = []
try:
    for line in open(os.path.join(d, "players.log"), encoding="utf-8", errors="replace"):
        t, _, rest = line.rstrip("\n").partition("\t")
        pid, _, cmd = rest.strip().partition(" ")
        if t.isdigit():
            seen.append((int(t), pid, cmd))
except FileNotFoundError:
    pass


def path_of(line):
    return line.rstrip("\n").split("  ", 1)[-1].replace(" (launch-counter keys excluded)", "")


changed = set()
for area in ("editor", "player"):
    b, r = os.path.join(d, "before-%s.txt" % area), os.path.join(d, "after-run-%s.txt" % area)
    if os.path.isfile(b) and os.path.isfile(r):
        before, after = set(open(b, encoding="utf-8")), set(open(r, encoding="utf-8"))
        changed.update(path_of(l) for l in after - before)


def name(cmd):
    m = re.search(r"-logFile (\S+)", cmd) or re.search(r"(\S+\.app)", cmd)
    return m.group(1) if m else cmd


with open(os.path.join(d, "attribution.txt"), "w", encoding="utf-8") as out:
    for p in sorted(changed):
        if not os.path.exists(p):
            out.write("%s\tremoved\t-\n" % p)
            continue
        m = int(os.path.getmtime(p))
        who = sorted({(pid, cmd) for t, pid, cmd in seen if abs(t - m) <= 10})
        desc = "; ".join("pid %s %s" % (pid, name(cmd)) for pid, cmd in who) or "no Player alive (Editor, or between samples)"
        out.write("%s\tmtime %s\t%s\n" % (p, time.strftime("%H:%M:%SZ", time.gmtime(m)), desc))
