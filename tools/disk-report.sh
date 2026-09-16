#!/bin/zsh
# WP-031 · tools/disk-report.sh [--root DIR] [--json FILE]
# Disk breakdown of the KADRO project workspace: worktrees (Library vs rest), build products,
# evidence, reports, backups, asset-production, the repository's .git (+LFS) and free space.
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
ROOT="$HOME/Desktop/KADRO/context/projects/deaths-demolition"; JSON=""
while [ $# -gt 0 ]; do case "$1" in --root) ROOT="$2"; shift ;; --json) JSON="$2"; shift ;; *) echo "usage: disk-report.sh [--root DIR] [--json FILE]" >&2; exit 64 ;; esac; shift; done
mb() { [ -e "$1" ] && du -sk "$1" 2>/dev/null | awk '{printf "%d", $1/1024}' || echo 0; }
sum() { local t=0; for d in "$@"; do [ -e "$d" ] && t=$((t + $(mb "$d"))); done; echo $t; }
GITDIR="$(git -C "$DD_REPO" rev-parse --path-format=absolute --git-common-dir)"
WT=$(sum "$ROOT"/wp-*/worktree); LIB=$(sum "$ROOT"/wp-*/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Library)
BUILD=$(sum "$ROOT"/wp-*/verify-build* "$ROOT"/wp-*/build-* "$ROOT"/builds "$ROOT"/wp-*/verify "$ROOT"/wp-*/verify-final)
EVID=$(sum "$ROOT"/wp-*/evidence); REP=$(sum "$ROOT"/wp-*/reports); BAK=$(sum "$ROOT"/wp-*/backup "$HOME/Backups/deaths-demolition")
AP=$(mb "$ROOT/asset-production"); GIT=$(mb "$GITDIR"); LFS=$(mb "$GITDIR/lfs"); ALL=$(mb "$ROOT")
FREE=$(df -k "$ROOT" | awk 'NR==2{printf "%d", $4/1024}'); SWAP=$(sysctl -n vm.swapusage 2>/dev/null | awk '{print $6}')
printf "%-34s %8s MB\n" "workspace total ($ROOT)" "$ALL"
printf "%-34s %8s MB  (Library %s MB, %s worktrees)\n" "  worktrees" "$WT" "$LIB" "$(ls -d "$ROOT"/wp-*/worktree 2>/dev/null | wc -l | tr -d ' ')"
printf "%-34s %8s MB\n" "  build products / verify copies" "$BUILD"
printf "%-34s %8s MB\n" "  evidence" "$EVID"
printf "%-34s %8s MB\n" "  reports" "$REP"
printf "%-34s %8s MB  (incl. ~/Backups/deaths-demolition)\n" "  backups" "$BAK"
printf "%-34s %8s MB\n" "  asset-production" "$AP"
printf "%-34s %8s MB  (LFS %s MB)\n" "repository .git" "$GIT" "$LFS"
printf "%-34s %8s MB  swap used %s\n" "free on volume" "$FREE" "${SWAP:-?}"
[ -n "$JSON" ] && cat > "$JSON" <<J
{ "utc": "$(date -u +%FT%TZ)", "root": "$ROOT", "totalMB": $ALL, "worktreesMB": $WT, "libraryMB": $LIB, "buildMB": $BUILD, "evidenceMB": $EVID, "reportsMB": $REP, "backupsMB": $BAK, "assetProductionMB": $AP, "gitMB": $GIT, "lfsMB": $LFS, "freeMB": $FREE, "swap": "${SWAP:-}" }
J
exit 0
