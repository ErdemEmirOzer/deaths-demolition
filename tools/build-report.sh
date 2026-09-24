#!/bin/zsh
# WP-046 · tools/build-report.sh <build-dir|build.log> [--json out.json] [--md out.md] [--top N] [--cap MB]
#
# Parses the "Build Report" block Unity prints at the end of a player build log into:
#   * category table (Textures / Meshes / Sounds / Shaders / Other Assets / Levels / Scripts / DLLs / headers), MB and %
#   * "Used Assets … sorted by uncompressed size": top N rows (MB, %, path)
#   * roll-up by package/folder: _Art/Characters, _Art/Environment, _Art/VFX, _Art/Audio, _Art/Textures (world), _Art/UI, _Art/other,
#     TextMesh Pro, _Prototype, Packages, other
#   * complete build size (log) and the real .app size (du) when a build dir is given, plus mode (dev/release)
#     from build-report.json when present, and a cap check (--cap MB, default 5120 = 5 GB, DEC-093 user decision 2026-09-24
#     replacing the 550 MB cap of DEC-086/DEC-090; warning only).
# Given a directory: uses <dir>/build.log (+ build-report.json, DeathsDemolition*.app). Writes footprint.json/.md
# into the directory by default. Exit 0 (cap exceeded prints a WARN line, never fails) · 2 no build report in log · 64 usage
set -u
setopt nullglob
IN="${1:-}"; shift 2>/dev/null || true
JSON=""; MD=""; TOP=25; CAP=5120   # DEC-093: 5 GB
while [ $# -gt 0 ]; do case "$1" in --json) JSON="$2"; shift ;; --md) MD="$2"; shift ;; --top) TOP="$2"; shift ;; --cap) CAP="$2"; shift ;; *) echo "usage: build-report.sh <build-dir|build.log> [--json f] [--md f] [--top N] [--cap MB]" >&2; exit 64 ;; esac; shift; done
[ -n "$IN" ] || { echo "usage: build-report.sh <build-dir|build.log> [--json f] [--md f] [--top N] [--cap MB]" >&2; exit 64; }
if [ -d "$IN" ]; then DIR="$IN"; LOG="$IN/build.log"; else LOG="$IN"; DIR="$(dirname "$IN")"; fi
[ -f "$LOG" ] || { echo "no log: $LOG" >&2; exit 64; }
[ -n "$JSON" ] || JSON="$DIR/footprint.json"; [ -n "$MD" ] || MD="$DIR/footprint.md"
MODE=""; [ -f "$DIR/build-report.json" ] && MODE=$(grep -o '"mode": "[a-z]*"' "$DIR/build-report.json" | head -1 | cut -d'"' -f4)
# WP-066W: a Windows build's report carries "target": "win64" (macOS reports have no target field) → label only.
TGT=""; [ -f "$DIR/build-report.json" ] && TGT=$(grep -o '"target": "[a-z0-9]*"' "$DIR/build-report.json" | head -1 | cut -d'"' -f4)
[ -n "$TGT" ] && MODE="$MODE-$TGT"
CMP=""; [ -f "$DIR/build-report.json" ] && CMP=$(grep -o '"compression": "[a-z0-9]*"' "$DIR/build-report.json" | head -1 | cut -d'"' -f4)
[ -n "$CMP" ] && MODE="$MODE-$CMP"   # WP-066C: label only
STAMP=""; [ -f "$DIR/build-report.json" ] && STAMP=$(grep -o '"describe": "[^"]*"' "$DIR/build-report.json" | head -1 | cut -d'"' -f4)
APPKB=0; for a in "$DIR"/*.app "$DIR"/DeathsDemolition-win64; do [ -d "$a" ] && APPKB=$(du -sk "$a" | cut -f1); break; done
python3 - "$LOG" "$JSON" "$MD" "$TOP" "$CAP" "$MODE" "$STAMP" "$APPKB" "$DIR" <<'PY'
import sys, re, json, datetime
log, jpath, mpath, top, cap, mode, stamp, appkb, d = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), float(sys.argv[5]), sys.argv[6], sys.argv[7], int(sys.argv[8]), sys.argv[9]
lines = open(log, encoding='utf-8', errors='replace').read().split('\n')
starts = [i for i, l in enumerate(lines) if l.strip() == 'Build Report']
if not starts: print("no 'Build Report' block in", log); sys.exit(2)
i = starts[-1]
def mb(v, u): return float(v) * (1/1024 if u == 'kb' else 1024 if u == 'gb' else 1)
cat = {}; total_user = complete = None; used = []
j = i + 1
while j < len(lines):
    l = lines[j]
    m = re.match(r'^([A-Za-z][A-Za-z ]+?)\s{2,}([0-9.]+) (kb|mb|gb)\s+([0-9.]+)%', l)
    if m:
        name = m.group(1).strip(); v = mb(m.group(2), m.group(3)); p = float(m.group(4))
        if name == 'Total User Assets': total_user = v
        else: cat[name] = {'mb': round(v, 2), 'pct': p}
    m2 = re.match(r'^Complete build size\s+([0-9.]+) (kb|mb|gb)', l)
    if m2: complete = mb(m2.group(1), m2.group(2))
    if l.startswith('Used Assets and files from the Resources folder'):
        k = j + 1
        while k < len(lines):
            m3 = re.match(r'^\s*([0-9.]+) (kb|mb|gb)\s+([0-9.]+)%\s+(.*)$', lines[k])
            if not m3: break
            used.append({'mb': round(mb(m3.group(1), m3.group(2)), 3), 'pct': float(m3.group(3)), 'path': m3.group(4).strip()}); k += 1
        break
    j += 1
def bucket(p):
    if p.startswith('Assets/_Art/Characters'): return '_Art/Characters'
    if p.startswith('Assets/_Art/Environment'): return '_Art/Environment'
    if p.startswith('Assets/_Art/VFX'): return '_Art/VFX'
    if p.startswith('Assets/_Art/Audio'): return '_Art/Audio'
    if p.startswith('Assets/_Art/Textures'): return '_Art/Textures (world)'
    if p.startswith('Assets/_Art/UI'): return '_Art/UI'
    if p.startswith('Assets/_Art/'): return '_Art/other'
    if p.startswith('Assets/TextMesh Pro'): return 'TextMesh Pro'
    if p.startswith('Assets/_Prototype'): return '_Prototype'
    if p.startswith('Packages/'): return 'Packages'
    return 'other'
roll = {}
for u in used:
    b = bucket(u['path']); r = roll.setdefault(b, {'mb': 0.0, 'files': 0}); r['mb'] += u['mb']; r['files'] += 1
for r in roll.values(): r['mb'] = round(r['mb'], 2)
appmb = round(appkb / 1024, 1)
warn = appmb > cap if appmb else (complete or 0) > cap
out = {'utc': datetime.datetime.utcnow().strftime('%Y-%m-%dT%H:%M:%SZ'), 'dir': d, 'log': log, 'mode': mode, 'stamp': stamp,
       'appMB': appmb, 'completeBuildMB': round(complete or 0, 1), 'totalUserAssetsMB': round(total_user or 0, 1), 'capMB': cap, 'capExceeded': warn,
       'categories': cat, 'rollup': dict(sorted(roll.items(), key=lambda kv: -kv[1]['mb'])), 'usedAssets': len(used), 'top': used[:top]}
json.dump(out, open(jpath, 'w'), indent=1)
md = []
md.append(f"# Build footprint — {mode or '?'} {stamp or ''}  ({out['utc']})")
md.append(f"app (du) **{appmb} MB** · complete build size (log) **{out['completeBuildMB']} MB** · user assets {out['totalUserAssetsMB']} MB · cap {cap:.0f} MB → {'**WARN: cap exceeded**' if warn else 'ok'} (dev builds carry debug symbols; the cap is for release)")
md.append("\n| Category | MB | % |\n|---|---:|---:|")
for k, v in sorted(cat.items(), key=lambda kv: -kv[1]['mb']): md.append(f"| {k} | {v['mb']:.1f} | {v['pct']:.1f} |")
md.append("\n| Folder | MB | files |\n|---|---:|---:|")
for k, v in out['rollup'].items(): md.append(f"| {k} | {v['mb']:.1f} | {v['files']} |")
md.append(f"\n| Top {top} used assets | MB | % |\n|---|---:|---:|")
for u in used[:top]: md.append(f"| {u['path']} | {u['mb']:.2f} | {u['pct']:.1f} |")
open(mpath, 'w').write('\n'.join(md) + '\n')
print(f"footprint mode={mode or '?'} app={appmb}MB complete={out['completeBuildMB']}MB user={out['totalUserAssetsMB']}MB cap={cap:.0f} {'WARN cap exceeded' if warn else 'ok'} json={jpath}")
for k, v in sorted(cat.items(), key=lambda kv: -kv[1]['mb'])[:4]: print(f"  {k:<14}{v['mb']:>8.1f} MB {v['pct']:>6.1f}%")
for k, v in list(out['rollup'].items())[:5]: print(f"  {k:<18}{v['mb']:>8.1f} MB  {v['files']} files")
PY
exit $?
