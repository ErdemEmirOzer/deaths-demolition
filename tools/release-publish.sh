#!/bin/zsh
# WP-099 (S-01, C-00 08.10) · tools/release-publish.sh — hand a checked Mac release to the user, without delay after FINAL.
#
# usage: release-publish.sh <release build dir> [--dest-root /Users/Erdem/DD-BUILDS] [--dry-run]
#   <release build dir>  the --out of `tools/build.sh release --smoke`
# Refuses (nothing copied) unless every check of that build passed:
#   report.json mode=release, ok=true, build.errorsCS=0, smoke.status=ok · footprint.json gateBytes ≤ capBytes (DEC-093)
#   · app-manifest.sha256 present and `app_manifest.py verify` clean · `codesign --verify --deep --strict` clean
# Then copies the user's set to <dest-root>/<head 8>-mac/ — the .app, the Burst debug folder, the build/size/smoke reports and
# the smoke screenshots; not the smoke's user-data backups or sandbox profile — with APFS clones (`cp -c`, no extra disk),
# and verifies the copy against the same app manifest. An existing destination is never overwritten (64).
# The app is ad-hoc signed (Unity's default; no Developer ID): Gatekeeper rejects it when downloaded, a local copy opens.
# exit: 0 published (or --dry-run: every check passed) · 1 a check failed · 2 the copy did not verify · 64 usage / dest exists
set -u
SELF="${0:A}"; HERE="${SELF:h}"
SRC=""; ROOT="/Users/Erdem/DD-BUILDS"; DRY=0
while [ $# -gt 0 ]; do
  case "$1" in
    --dest-root) ROOT="${2:-}"; shift ;;
    --dry-run) DRY=1 ;;
    -h|--help) sed -n '3,15p' "$SELF" >&2; exit 64 ;;
    -*) echo "release-publish: unknown option $1" >&2; exit 64 ;;
    *) [ -z "$SRC" ] && SRC="$1" || { echo "release-publish: one build dir only" >&2; exit 64; } ;;
  esac
  shift
done
[ -n "$SRC" ] && [ -d "$SRC" ] || { sed -n '3,15p' "$SELF" >&2; exit 64; }
SRC="${SRC:A}"
APP="$SRC/DeathsDemolition.app"

# --- the build's own record ----------------------------------------------------------------------------------------------
CHECK=$(python3 - "$SRC" <<'PY'
import json, sys
from pathlib import Path
d = Path(sys.argv[1]); bad = []
def load(n):
    try:
        return json.loads((d / n).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        bad.append("%s unreadable" % n); return {}
r, f = load("report.json"), load("footprint.json")
b, s = r.get("build") or {}, r.get("smoke") or {}
r.get("mode") == "release" or bad.append("report.json mode=%s (not release)" % r.get("mode"))
r.get("ok") is True or bad.append("report.json ok=%s" % r.get("ok"))
b.get("errorsCS") == 0 or bad.append("build errorsCS=%s" % b.get("errorsCS"))
s.get("status") == "ok" or bad.append("smoke status=%s (build.sh release --smoke)" % s.get("status"))
g, c = f.get("gateBytes"), f.get("capBytes")
(isinstance(g, int) and isinstance(c, int) and g <= c) or bad.append("footprint gateBytes=%s capBytes=%s" % (g, c))
head = r.get("head") or ""
len(head) >= 8 or bad.append("report.json head missing")
print(head[:8]); print("\n".join(bad))
PY
)
HEAD8=$(print -r -- "$CHECK" | head -1)
FAILS=$(print -r -- "$CHECK" | tail -n +2)
[ -d "$APP" ] || FAILS+=$'\n'"no $APP"
[ -f "$SRC/app-manifest.sha256" ] || FAILS+=$'\n'"no app-manifest.sha256"
if [ -z "${FAILS//$'\n'/}" ]; then   # the record is clean: now the files themselves
  python3 "$HERE/app_manifest.py" verify "$SRC" > /dev/null 2>&1 || FAILS+=$'\n'"app_manifest.py verify $SRC failed (the build changed since it was written)"
  codesign --verify --deep --strict "$APP" > /dev/null 2>&1 || FAILS+=$'\n'"codesign --verify --deep --strict failed"
fi
if [ -n "${FAILS//$'\n'/}" ]; then
  echo "release-publish: NOT published — $SRC:" >&2; print -r -- "$FAILS" | sed '/^$/d; s/^/  - /' >&2; exit 1
fi
SIGN=$(codesign -dv "$APP" 2>&1 | sed -n 's/^Signature=//p')
DEST="$ROOT/$HEAD8-mac"
[ -e "$DEST" ] && { echo "release-publish: $DEST exists — a published build is never overwritten" >&2; exit 64; }
echo "release-publish: checks ok — release $HEAD8, smoke ok, footprint under cap, manifest verified, signature ${SIGN:-?}"
[ "$DRY" -eq 1 ] && { echo "release-publish: --dry-run — would copy to $DEST"; exit 0; }

# --- copy (APFS clones) and verify --------------------------------------------------------------------------------------
mkdir -p "$DEST.tmp.$$" || exit 2
for n in DeathsDemolition.app DeathsDemolition_BurstDebugInformation_DoNotShip app-manifest.sha256 build-report.json build.log \
         footprint.json footprint.md report.json smoke-report.json timeline.txt "$SRC"/e*-*.png(N:t); do
  [ -e "$SRC/$n" ] || continue
  cp -c -R -p "$SRC/$n" "$DEST.tmp.$$/" 2>/dev/null || cp -R -p "$SRC/$n" "$DEST.tmp.$$/" || { echo "release-publish: copy of $n failed" >&2; exit 2; }
done
python3 "$HERE/app_manifest.py" verify "$DEST.tmp.$$" > "$DEST.tmp.$$/publish-verify.txt" 2>&1 \
  || { echo "release-publish: the copy does not verify — $DEST.tmp.$$/publish-verify.txt (left for a look)" >&2; exit 2; }
mv "$DEST.tmp.$$" "$DEST" || exit 2
echo "release-publish: published $DEST/DeathsDemolition.app (from $SRC)"
exit 0
