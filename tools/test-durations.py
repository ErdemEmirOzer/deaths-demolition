#!/usr/bin/env python3
"""WP-051 · tools/test-durations.py <results.xml>… [--top N] [--slow SECONDS] [--json out]
Per-test duration table from Unity Test Framework NUnit XML: totals, tests slower than --slow,
top N, per-fixture totals, non-passed list. Exit 0 always (reporting only)."""
import sys, json, xml.etree.ElementTree as ET
args = sys.argv[1:]; top = 15; slow = 10.0; jout = None; files = []
i = 0
while i < len(args):
    a = args[i]
    if a == '--top': top = int(args[i+1]); i += 2
    elif a == '--slow': slow = float(args[i+1]); i += 2
    elif a == '--json': jout = args[i+1]; i += 2
    else: files.append(a); i += 1
report = {}
for f in files:
    try: r = ET.parse(f).getroot()
    except Exception as e: print(f"{f}: cannot parse ({e})"); continue
    rows = []
    for tc in r.iter('test-case'):
        fn = tc.get('fullname') or ''; parts = fn.split('.')
        rows.append({'fixture': parts[-2] if len(parts) > 1 else '', 'name': tc.get('name'), 'duration': float(tc.get('duration') or 0), 'result': tc.get('result')})
    rows.sort(key=lambda x: -x['duration'])
    total = sum(x['duration'] for x in rows)
    fx = {}
    for x in rows: fx[x['fixture']] = fx.get(x['fixture'], 0) + x['duration']
    slowrows = [x for x in rows if x['duration'] > slow]
    bad = [x for x in rows if x['result'] != 'Passed']
    print(f"== {f}\n   tests={len(rows)} passed={len(rows)-len(bad)} non-passed={len(bad)} total={total:.0f}s suite={float(r.get('duration') or 0):.0f}s  >{slow:.0f}s: {len(slowrows)} ({sum(x['duration'] for x in slowrows):.0f}s)")
    for x in rows[:top]: print(f"   {x['duration']:7.1f}s  {x['fixture']}.{x['name']}  {x['result']}")
    print("   fixtures: " + ", ".join(f"{k} {v:.0f}s" for k, v in sorted(fx.items(), key=lambda kv: -kv[1])[:8]))
    for x in bad: print(f"   NON-PASSED {x['result']}: {x['fixture']}.{x['name']} ({x['duration']:.1f}s)")
    report[f] = {'tests': len(rows), 'nonPassed': len(bad), 'totalSeconds': round(total, 1), 'slow': [x for x in slowrows], 'fixtures': {k: round(v, 1) for k, v in fx.items()}, 'top': rows[:top], 'bad': bad}
if jout: json.dump(report, open(jout, 'w'), indent=1)
