#!/bin/zsh
# WP-099 (S-01, C-00 08.10) · tools/release-publish.sh — hand a checked Mac release to the user, without delay after FINAL.
#
# usage: release-publish.sh <release build dir> [--dest-root /Users/Erdem/DD-BUILDS] [--dry-run]
#   <release build dir>  the --out of `tools/build.sh release --smoke`; --dest-root must be an existing directory
# Refuses (nothing copied) unless every check of that build passed: report.json mode=release, ok=true, build.errorsCS=0,
#   smoke.status=ok, head a git sha, appManifestSha256 = the manifest file's sha256 · footprint.json gateBytes ≤ capBytes
#   (DEC-093) · `app_manifest.py verify` clean · `codesign --verify --deep --strict` clean. Any unreadable record refuses.
# Then copies the user's set to <dest-root>/<head 8>-mac/ — the .app, the Burst debug folder, the build/size/smoke reports
#   and the smoke screenshots; not the smoke's user-data backups or sandbox profile — as APFS clones on the same volume
#   (a plain copy across volumes), verifies the copy (manifest + codesign) and only then names it. Never overwrites (64).
# The app is ad-hoc signed (Unity's default; no Developer ID): it opens from this Mac; a copy that reached another Mac
#   through a download/AirDrop carries the quarantine flag and Gatekeeper refuses it — System Settings › Privacy &
#   Security › "Open Anyway", or `xattr -dr com.apple.quarantine DeathsDemolition.app` (Ctrl-click no longer bypasses it).
# exit: 0 published (or --dry-run: every check passed) · 1 a check failed · 2 the copy did not verify · 64 usage / dest
#   exists · 128+n signal (a half copy stays as <dest>.tmp.<pid> — named on stderr, never in place of <dest>)
set -u
SELF="${0:A}"; HERE="${SELF:h}"
usage() { sed -n '3,17p' "$SELF" >&2; exit 64; }
SRC=""; ROOT="/Users/Erdem/DD-BUILDS"; DRY=0
while [ $# -gt 0 ]; do
  case "$1" in
    --dest-root) [ $# -ge 2 ] && [ -n "$2" ] || { echo "release-publish: --dest-root needs a directory" >&2; exit 64; }; ROOT="$2"; shift ;;
    --dry-run) DRY=1 ;;
    -h|--help) usage ;;
    -*) echo "release-publish: unknown option $1" >&2; exit 64 ;;
    *) [ -z "$SRC" ] && SRC="$1" || { echo "release-publish: one build dir only" >&2; exit 64; } ;;
  esac
  shift
done
[ -n "$SRC" ] && [ -d "$SRC" ] || usage
[ -d "$ROOT" ] || { echo "release-publish: --dest-root $ROOT is not a directory (it is never created here)" >&2; exit 64; }
SRC="${SRC:A}"; ROOT="${ROOT:A}"
APP="$SRC/DeathsDemolition.app"

# --- the build's own record (gt MUST-1, 08.10: any failure of this check is a refusal, never a pass) ---------------------
CHECK=$(python3 - "$SRC" <<'PY'
import hashlib, json, re, sys
from pathlib import Path
d = Path(sys.argv[1]); bad = []; head8 = ""
try:
    def load(n):
        try:
            v = json.loads((d / n).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            bad.append("%s unreadable" % n); return {}
        if not isinstance(v, dict):
            bad.append("%s is not an object" % n); return {}
        return v
    r, f = load("report.json"), load("footprint.json")
    b, s = r.get("build"), r.get("smoke")
    b = b if isinstance(b, dict) else (bad.append("report.json build is not an object") or {})
    s = s if isinstance(s, dict) else (bad.append("report.json smoke is not an object") or {})
    r.get("mode") == "release" or bad.append("report.json mode=%s (not release)" % r.get("mode"))
    r.get("ok") is True or bad.append("report.json ok=%s" % r.get("ok"))
    (b.get("errorsCS") == 0 and b.get("errorsCS") is not False) or bad.append("build errorsCS=%s" % b.get("errorsCS"))
    s.get("status") == "ok" or bad.append("smoke status=%s (build.sh release --smoke)" % s.get("status"))
    g, c = f.get("gateBytes"), f.get("capBytes")
    (type(g) is int and type(c) is int and 0 < g <= c) or bad.append("footprint gateBytes=%s capBytes=%s" % (g, c))
    head = r.get("head")
    if isinstance(head, str) and re.fullmatch(r"[0-9a-f]{8,40}", head):
        head8 = head[:8]
    else:
        bad.append("report.json head=%r is not a git sha" % (head,))
    man = d / "app-manifest.sha256"
    if man.is_file():   # gt S-1: the manifest is the one build.sh wrote, not one rewritten after a change to the .app
        want = r.get("appManifestSha256")
        got = hashlib.sha256(man.read_bytes()).hexdigest()
        want == got or bad.append("app-manifest.sha256 sha %s != report.json appManifestSha256 %s" % (got[:12], str(want)[:12]))
    else:
        bad.append("no app-manifest.sha256")
except Exception as e:   # anything unforeseen refuses
    bad.append("record check failed: %s: %s" % (type(e).__name__, e))
print(json.dumps({"head8": head8, "bad": bad}))
PY
); PRC=$?
FAILS=$(print -r -- "$CHECK" | python3 -c 'import json, sys
try:
    v = json.loads(sys.stdin.read()); print("\n".join(v["bad"]))
except Exception as e:
    print("record check output unreadable: %s" % e)' 2>&1)
HEAD8=$(print -r -- "$CHECK" | python3 -c 'import json, sys
try: print(json.loads(sys.stdin.read())["head8"])
except Exception: print("")' 2>/dev/null)
[ $PRC -eq 0 ] || FAILS+=$'\n'"record check exited $PRC"
[[ "$HEAD8" =~ '^[0-9a-f]{8}$' ]] || FAILS+=$'\n'"no valid head sha (got '$HEAD8')"
[ -d "$APP" ] || FAILS+=$'\n'"no $APP"
if [ -z "${FAILS//$'\n'/}" ]; then   # the record is clean: now the files themselves
  python3 "$HERE/app_manifest.py" verify "$SRC" > /dev/null 2>&1 || FAILS+=$'\n'"app_manifest.py verify $SRC failed (the build changed since it was written)"
  codesign --verify --deep --strict "$APP" > /dev/null 2>&1 || FAILS+=$'\n'"codesign --verify --deep --strict failed"
fi
if [ -n "${FAILS//$'\n'/}" ]; then
  echo "release-publish: NOT published — $SRC:" >&2; print -r -- "$FAILS" | sed '/^$/d; s/^/  - /' >&2; exit 1
fi
SIGN=$(codesign -dv "$APP" 2>&1 | sed -n 's/^Signature=//p')
STAMP=$(python3 -c 'import json, sys; print(((json.load(open(sys.argv[1])).get("build") or {}).get("stamp")) or "?")' "$SRC/report.json" 2>/dev/null)
DEST="$ROOT/$HEAD8-mac"
[ -e "$DEST" ] && { echo "release-publish: $DEST exists — a published build is never overwritten" >&2; exit 64; }
echo "release-publish: checks ok — release $HEAD8 (stamp $STAMP), smoke ok, footprint under cap, manifest verified, signature ${SIGN:-?}"
[ "$DRY" -eq 1 ] && { echo "release-publish: --dry-run — would copy to $DEST"; exit 0; }

# --- copy (APFS clones on one volume) and verify, then name ----------------------------------------------------------
TMP="$DEST.tmp.$$"
trap 'echo "release-publish: interrupted — the half copy stays at $TMP (never in place of $DEST)" >&2; exit 130' INT
trap 'echo "release-publish: interrupted — the half copy stays at $TMP (never in place of $DEST)" >&2; exit 143' TERM
trap 'echo "release-publish: interrupted — the half copy stays at $TMP (never in place of $DEST)" >&2; exit 129' HUP
mkdir "$TMP" || exit 2
for n in DeathsDemolition.app DeathsDemolition_BurstDebugInformation_DoNotShip app-manifest.sha256 build-report.json build.log \
         footprint.json footprint.md report.json smoke-report.json timeline.txt "$SRC"/e*-*.png(N:t); do
  [ -e "$SRC/$n" ] || continue
  cp -c -R -p "$SRC/$n" "$TMP/" 2>/dev/null || cp -R -p "$SRC/$n" "$TMP/" || { echo "release-publish: copy of $n failed — $TMP" >&2; exit 2; }
done
python3 "$HERE/app_manifest.py" verify "$TMP" > "$TMP/publish-verify.txt" 2>&1 \
  || { echo "release-publish: the copy does not verify — $TMP/publish-verify.txt (left for a look)" >&2; exit 2; }
codesign --verify --deep --strict "$TMP/DeathsDemolition.app" >> "$TMP/publish-verify.txt" 2>&1 \
  || { echo "release-publish: the copy's signature does not verify — $TMP (left for a look)" >&2; exit 2; }
echo "codesign ok" >> "$TMP/publish-verify.txt"
# gt S-2: a destination that appeared meanwhile is never entered or replaced
[ -e "$DEST" ] && { echo "release-publish: $DEST appeared during the copy — not replaced; the verified copy is $TMP" >&2; exit 64; }
mv -n "$TMP" "$DEST" && [ ! -e "$TMP" ] || { echo "release-publish: could not name $TMP as $DEST" >&2; exit 2; }
echo "release-publish: published $DEST/DeathsDemolition.app (from $SRC)"
exit 0
