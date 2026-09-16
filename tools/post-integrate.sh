#!/bin/zsh
# WP-031 · tools/post-integrate.sh <tag> [--dest DIR]
# C-00's chain right after an --ff-only merge + tag on main:
#   1. tag must exist and point at main HEAD          2. tools/backup-repo.sh (two-pass, verified)
#   3. tools/cleanup-merged.sh --dry-run (report only) 4. tools/disk-report.sh
#   5. build footprint line (WP-046): newest footprint.json → <dest>/footprint-<tag>.json + Δ vs previous tag
#   6. LFS check: `git lfs ls-files` count vs .git/lfs/objects count (objects ≥ pointers)
# Writes <dest>/post-integrate-<tag>.txt (default dest ~/Backups/deaths-demolition/reports).
# Exit: 0 ok · 2 tag mismatch · 3 backup not verified · 4 LFS check failed
set -u
source "${0:A:h}/dd-env.sh"
TAG="${1:-}"; shift 2>/dev/null || true; DEST="$HOME/Backups/deaths-demolition/reports"
while [ $# -gt 0 ]; do case "$1" in --dest) DEST="$2"; shift ;; *) echo "usage: post-integrate.sh <tag> [--dest DIR]" >&2; exit 64 ;; esac; shift; done
[ -n "$TAG" ] || { echo "usage: post-integrate.sh <tag>" >&2; exit 64; }
mkdir -p "$DEST"; OUT="$DEST/post-integrate-$TAG.txt"; : > "$OUT"
say() { echo "$*" | tee -a "$OUT"; }
say "post-integrate $TAG  $(date -u +%FT%TZ)  repo=$DD_REPO"
HEAD=$(git -C "$DD_REPO" rev-parse main); TAGC=$(git -C "$DD_REPO" rev-parse "$TAG^{commit}" 2>/dev/null || echo none)
say "main=$HEAD tag=$TAG -> $TAGC"
[ "$HEAD" = "$TAGC" ] || { say "TAG MISMATCH: $TAG does not point at main HEAD"; exit 2; }
say; say "== backup"; "$DD_TOOLS_DIR/backup-repo.sh" 2>&1 | tee -a "$OUT"; B=${pipestatus[1]}
say "backup exit=$B"
say; say "== cleanup candidates (dry-run; C-00 applies with user approval)"; "$DD_TOOLS_DIR/cleanup-merged.sh" --dry-run 2>&1 | tee -a "$OUT"
say; say "== disk"; "$DD_TOOLS_DIR/disk-report.sh" 2>&1 | tee -a "$OUT"
say; say "== build footprint (last known, WP-046)"
# newest footprint.json under the build root (dev or release) → copy as footprint-<tag>.json, print line + delta vs previous tag
FP=$(ls -t "$DD_BUILD_ROOT"/*/footprint.json ~/Desktop/KADRO/context/projects/deaths-demolition/wp-*/evidence/*/footprint.json 2>/dev/null | head -1)
if [ -n "$FP" ]; then
  cp "$FP" "$DEST/footprint-$TAG.json"
  PREV=$(ls -t "$DEST"/footprint-*.json 2>/dev/null | grep -v "footprint-$TAG.json" | head -1)
  python3 - "$FP" "${PREV:-}" "$TAG" <<'PY' | tee -a "$OUT"
import json, sys, os
cur = json.load(open(sys.argv[1])); prev = json.load(open(sys.argv[2])) if sys.argv[2] and os.path.isfile(sys.argv[2]) else None
line = f"{sys.argv[3]}: mode={cur.get('mode') or '?'} app={cur.get('appMB')} MB complete={cur.get('completeBuildMB')} MB user={cur.get('totalUserAssetsMB')} MB cap={cur.get('capMB')} {'WARN cap exceeded' if cur.get('capExceeded') else 'ok'} (stamp {cur.get('stamp')})"
if prev:
    d = (cur.get('completeBuildMB') or 0) - (prev.get('completeBuildMB') or 0)
    line += f"  Δ vs {os.path.basename(sys.argv[2])[10:-5]} ({prev.get('mode') or '?'}): {d:+.1f} MB"
    for k in ('Textures', 'Meshes', 'Sounds'):
        a = (cur.get('categories', {}).get(k) or {}).get('mb', 0); b = (prev.get('categories', {}).get(k) or {}).get('mb', 0)
        line += f", {k} {a-b:+.1f}"
print(line)
PY
else say "no footprint.json found (run tools/build.sh first)"; fi
say; say "== LFS"; P=$(git -C "$DD_REPO" lfs ls-files 2>/dev/null | wc -l | tr -d ' '); O=$(find "$(git -C "$DD_REPO" rev-parse --path-format=absolute --git-common-dir)/lfs/objects" -type f 2>/dev/null | wc -l | tr -d ' ')
say "lfs pointers(main)=$P objects=$O"
[ "$O" -ge "$P" ] || { say "LFS CHECK FAILED: fewer objects than pointers"; exit 4; }
[ "$B" -eq 0 ] || { say "BACKUP NOT VERIFIED (exit $B) — partial backup kept, re-run backup-repo.sh"; exit 3; }
say; say "OK  report=$OUT"
exit 0
