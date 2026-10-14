#!/bin/zsh
# WP-031 · tools/disk-report.sh [--root DIR] [--json FILE]
# Disk breakdown of the KADRO project workspace: worktrees (Library vs rest), build products,
# evidence, reports, backups, asset-production, the repository's .git (+LFS) and free space.
# WP-079G (C-00 30.09): every size helper returns a number (empty/non-numeric → 0 + a note on stderr); "other folders"
# lists the non-standard folders (worktree-*, perf-*, release-*, rc-* under the root and under wp-*, wp-*/builds)
# one per row. Sizes are the APPARENT size (clones share blocks): Library folders are `cp -cR`
# clones and du counts every clone in full, so removing one frees less. Per other-folder row the estimated real gain
# (tools/apfs_real_gain.py: sum of APFS private bytes, a lower bound; "n/a (clone-shared)" when not measurable).
# JSON: the old *MB keys are unchanged (apparent sizes); new keys sizeKind, realGainMethod, otherFoldersMB,
# otherFolders[{path, apparentMB, realGainMB|null, realGain}]. G4 D6 (WP-089, Q5): guardLedgerMB = the
# userdata-guard ledger (~/.dd-userdata-guard of the passwd home: quarantine + INDEX.tsv) — permanent, nothing deletes it.
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
ROOT="$DD_KPROJ"; JSON=""
while [ $# -gt 0 ]; do case "$1" in --root) ROOT="$2"; shift ;; --json) JSON="$2"; shift ;; *) echo "usage: disk-report.sh [--root DIR] [--json FILE]" >&2; exit 64 ;; esac; shift; done
num() { case "$1" in ''|*[!0-9]*) print -u2 -r -- "note: size '${1}' of ${2:-?} is not a number → 0"; print 0 ;; *) print -r -- "$1" ;; esac; }
mb() { local k; [ -e "$1" ] || { print 0; return; }; k=$(du -sk "$1" 2>/dev/null | awk 'NR==1{print $1}'); k=$(num "$k" "$1"); print $((k / 1024)); }
sum() { local t=0 d; for d in "$@"; do [ -e "$d" ] && t=$((t + $(mb "$d"))); done; print $t; }
rg() { local r; r=$(python3 "$DD_TOOLS_DIR/apfs_real_gain.py" "$1" 2>/dev/null | awk -F'\t' 'NR==1{print $1}'); case "$r" in ''|*[!0-9]*) print n/a ;; *) print -r -- "$r" ;; esac; }
GITDIR="$(git -C "$DD_REPO" rev-parse --path-format=absolute --git-common-dir)"
WT=$(sum "$ROOT"/wp-*/worktree); LIB=$(sum "$ROOT"/wp-*/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Library)
BUILD=$(sum "$ROOT"/wp-*/verify-build* "$ROOT"/wp-*/build-* "$ROOT"/builds "$ROOT"/wp-*/verify "$ROOT"/wp-*/verify-final)
EVID=$(sum "$ROOT"/wp-*/evidence); REP=$(sum "$ROOT"/wp-*/reports); BAK=$(sum "$ROOT"/wp-*/backup "$HOME/Backups/deaths-demolition")
AP=$(mb "$ROOT/asset-production"); GIT=$(mb "$GITDIR"); LFS=$(mb "$GITDIR/lfs"); ALL=$(mb "$ROOT")
FREE=$(df -k "$ROOT" | awk 'NR==2{printf "%d", $4/1024}'); FREE=$(num "$FREE" "free space"); SWAP=$(sysctl -n vm.swapusage 2>/dev/null | awk '{print $6}')
GLEDGER_DIR="${~:-~$(id -un)}/.dd-userdata-guard"; GLEDGER=$(mb "$GLEDGER_DIR")
OTHER_TSV=""; OTHER=0
for d in "$ROOT"/worktree-* "$ROOT"/perf-* "$ROOT"/release-* "$ROOT"/rc-* \
         "$ROOT"/wp-*/worktree-* "$ROOT"/wp-*/perf-* "$ROOT"/wp-*/release-* "$ROOT"/wp-*/rc-* "$ROOT"/wp-*/builds; do
  [ -d "$d" ] || continue
  s=$(mb "$d"); r=$(rg "$d"); OTHER=$((OTHER + s))
  OTHER_TSV+="${s}"$'\t'"${r}"$'\t'"${d}"$'\n'
done
echo "sizes: apparent size (clones share blocks) — Library folders are cp -c clones; du counts each clone in full"
printf "%-34s %8s MB\n" "workspace total ($ROOT)" "$ALL"
printf "%-34s %8s MB  (Library %s MB, %s worktrees)\n" "  worktrees" "$WT" "$LIB" "$(ls -d "$ROOT"/wp-*/worktree 2>/dev/null | wc -l | tr -d ' ')"
printf "%-34s %8s MB\n" "  build products / verify copies" "$BUILD"
printf "%-34s %8s MB\n" "  evidence" "$EVID"
printf "%-34s %8s MB\n" "  reports" "$REP"
printf "%-34s %8s MB  (incl. ~/Backups/deaths-demolition)\n" "  backups" "$BAK"
printf "%-34s %8s MB\n" "  asset-production" "$AP"
printf "%-34s %8s MB  (worktree-*, perf-*, release-*, rc-*, wp-*/builds)\n" "  other folders" "$OTHER"
print -rn -- "$OTHER_TSV" | while IFS=$'\t' read -r s r d; do
  [ "$r" = "n/a" ] && r="n/a (clone-shared)" || r="≥ $r MB"
  short="${d#$ROOT/}"; printf "    %-44s %8s MB apparent · estimated real gain %s\n" "${short/#$HOME/~}" "$s" "$r"
done
printf "%-34s %8s MB  (LFS %s MB)\n" "repository .git" "$GIT" "$LFS"
printf "%-34s %8s MB  (%s — quarantine, never deleted)\n" "userdata-guard ledger" "$GLEDGER" "${GLEDGER_DIR/#$HOME/~}"
printf "%-34s %8s MB  swap used %s\n" "free on volume" "$FREE" "${SWAP:-?}"
if [ -n "$JSON" ]; then
  OTHERS_JSON=$(print -rn -- "$OTHER_TSV" | python3 -c '
import json, sys
rows = []
for line in sys.stdin.read().splitlines():
    s, r, d = line.split("\t", 2)
    rows.append({"path": d, "apparentMB": int(s), "realGainMB": None if r == "n/a" else int(r),
                 "realGain": "n/a (clone-shared)" if r == "n/a" else ">= %s MB" % r})
print(json.dumps(rows))') || OTHERS_JSON="[]"
  cat > "$JSON" <<J
{ "utc": "$(date -u +%FT%TZ)", "root": "$ROOT", "sizeKind": "apparent size (clones share blocks)", "realGainMethod": "sum of APFS private bytes (getattrlist ATTR_CMNEXT_PRIVATESIZE), lower bound; null = n/a (clone-shared)", "totalMB": $ALL, "worktreesMB": $WT, "libraryMB": $LIB, "buildMB": $BUILD, "evidenceMB": $EVID, "reportsMB": $REP, "backupsMB": $BAK, "assetProductionMB": $AP, "otherFoldersMB": $OTHER, "gitMB": $GIT, "lfsMB": $LFS, "guardLedgerMB": $GLEDGER, "freeMB": $FREE, "swap": "${SWAP:-}", "otherFolders": ${OTHERS_JSON} }
J
fi
exit 0
