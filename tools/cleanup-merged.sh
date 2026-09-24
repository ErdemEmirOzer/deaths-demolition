#!/bin/zsh
# WP-031 · ADR-031-1 · tools/cleanup-merged.sh [--dry-run] [--apply] [--root DIR]
#
# Reclaims disk from finished packages. DEFAULT IS DRY-RUN (a report only). --apply deletes and is
# run by C-00 with the user's approval; never from a package session.
#   1. Worktrees whose branch is integrated into main (branch tip is an ancestor of main AND the tip
#      is the branch's own package commit: a tag named after the branch's wp-number points at it or
#      the commit subject starts with "WP-NNN"), are clean (porcelain 0) and are not the main
#      worktree or the one running this script → `git worktree remove` + `git worktree prune`.
#      A fresh worktree whose branch still equals an older main is kept (WP-039 fix).
#      WP-031B: integrate.sh cherry-picks, so a branch's tip is usually NOT an ancestor of main.
#      Such a branch also counts as integrated when it has commits of its own (merge-base..tip) and
#      every one of them is in main since the merge-base: same patch-id (`git cherry`) or, for a
#      3-way-merged pick, the identical commit message (integrate.sh commits with `git commit -C`).
#      A worktree that a running process names in its command line is kept ("in use").
#      Branches and tags are NEVER deleted.
#   2. Build products and Library-less verification copies under the KADRO package folders:
#      <root>/wp-*/verify-build*, <root>/wp-*/build-*, <root>/wp-*/verify, <root>/wp-*/verify-final,
#      <root>/builds/* (tools/build.sh outputs).
#   3. Orphan worktree folders: <root>/wp-*/worktree that git no longer lists (already pruned).
# Never touched: evidence/, reports/, README*, backup/, prompts/, manifest/, asset-production/,
# ~/Backups. .app bundles that live inside evidence/ are listed as manual candidates only.
# Exit: 0 ok (dry-run or applied) · 1 a removal failed · 64 usage
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
ROOT="$HOME/Desktop/KADRO/context/projects/deaths-demolition"; APPLY=0
while [ $# -gt 0 ]; do case "$1" in --dry-run) APPLY=0 ;; --apply) APPLY=1 ;; --root) ROOT="$2"; shift ;; *) echo "usage: cleanup-merged.sh [--dry-run] [--apply] [--root DIR]" >&2; exit 64 ;; esac; shift; done
MAIN_WT="$(git -C "$DD_REPO" rev-parse --path-format=absolute --git-common-dir)"; MAIN_WT="${MAIN_WT:h}"
MAIN_HEAD=$(git -C "$MAIN_WT" rev-parse main)
SELF_WT="$(git -C "$DD_REPO" rev-parse --show-toplevel)"
mb() { du -sk "$1" 2>/dev/null | awk '{printf "%d", $1/1024}'; }
# WP-031B: 0 when every own commit of branch $1 (merge-base..tip) is in main by patch-id or message.
picked_into_main() { local b="$1" base c n=0 msgs
  base=$(git -C "$MAIN_WT" merge-base "$MAIN_HEAD" "$b" 2>/dev/null) || return 1
  [ -n "$(git -C "$MAIN_WT" rev-list --no-merges "$base..$b")" ] || return 1
  msgs=$(git -C "$MAIN_WT" log --format='%H' "$base..$MAIN_HEAD" | while read -r c; do git -C "$MAIN_WT" log -1 --format='%B' "$c" | shasum | cut -c1-40; done)
  git -C "$MAIN_WT" cherry "$MAIN_HEAD" "$b" "$base" | while read -r mark c; do
    [ "$mark" = "-" ] && continue
    echo "$msgs" | grep -qxF "$(git -C "$MAIN_WT" log -1 --format='%B' "$c" | shasum | cut -c1-40)" || return 1
  done
}
MODE=$( [ "$APPLY" -eq 1 ] && echo APPLY || echo DRY-RUN )
echo "cleanup-merged $MODE  main=$MAIN_HEAD  root=$ROOT  $(date -u +%FT%TZ)"
TOTAL=0; FAIL=0

echo; echo "== 1) worktrees (branch integrated into main, clean, not main/self)"
printf "%-8s %-6s %-46s %s\n" "MB" "act" "worktree" "reason"
# porcelain lines: "worktree <path>" / "branch refs/heads/x" / "detached" — paths may contain spaces
wt=""; br=""
git -C "$MAIN_WT" worktree list --porcelain | { while IFS= read -r line; do
  case "$line" in
    "worktree "*) wt="${line#worktree }"; br="" ;;
    "branch "*)   br="${line#branch }" ;;
    detached)     br="detached" ;;
    "")           [ -n "$wt" ] && [ -n "$br" ] && printf '%s\t%s\n' "$wt" "$br"; wt=""; br="" ;;
  esac
done; [ -n "$wt" ] && [ -n "$br" ] && printf '%s\t%s\n' "$wt" "$br"; } | while IFS=$'\t' read -r wt br; do
  [ "$wt" = "$MAIN_WT" ] && continue
  short="${wt#$ROOT/}"; size=$(mb "$wt")
  if [ "$br" = "detached" ]; then printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "detached HEAD"; continue; fi
  b="${br#refs/heads/}"; tip=$(git -C "$MAIN_WT" rev-parse "$b" 2>/dev/null)
  if [ "$wt" = "$SELF_WT" ]; then printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "this script's worktree ($b)"; continue; fi
  how="integrated"
  if ! git -C "$MAIN_WT" merge-base --is-ancestor "$tip" "$MAIN_HEAD"; then
    if picked_into_main "$b"; then how="integrated by cherry-pick"
    else printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "not integrated ($b)"; continue; fi
  fi
  # WP-039 fix: a branch whose tip is an OLD main (fresh worktree, no package commit yet) is also an
  # ancestor of main. "Integrated" means the tip is the branch's own package commit: a tag named after
  # the branch's wp-number points at the tip, or the tip's subject starts with that "WP-NNN".
  wp=$(printf '%s' "$b" | grep -oE '^wp-[0-9]+[a-z]*' || true)
  own=0
  if [ -n "$wp" ]; then
    git -C "$MAIN_WT" tag --points-at "$tip" | grep -qiE "^${wp}(-|$)" && own=1
    git -C "$MAIN_WT" log -1 --format=%s "$tip" | grep -qiE "^${wp}([^0-9a-z]|$)" && own=1
  fi
  [ "$how" != "integrated" ] && own=1   # picked commits are the branch's own by construction
  if [ "$own" -eq 0 ]; then printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "no package commit of its own yet ($b, tip $(git -C "$MAIN_WT" rev-parse --short=8 "$tip"))"; continue; fi
  # WP-031B: a worktree a running process names in its command line (e.g. a main-ci driver) is in use.
  busy=$(pgrep -f -- "$wt/" 2>/dev/null | grep -vx "$$" | head -1)
  if [ -n "$busy" ]; then printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "in use (pid $busy, $b)"; continue; fi
  dirty=$(git -C "$wt" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
  if [ "$dirty" != "0" ]; then printf "%-8s %-6s %-46s %s\n" "$size" "keep" "$short" "dirty ($dirty paths, $b)"; continue; fi
  printf "%-8s %-6s %-46s %s\n" "$size" "REMOVE" "$short" "$how $b ($(git -C "$MAIN_WT" rev-parse --short=8 "$tip"))"
  printf '%s\t%s\n' "$size" "$wt" >> "/tmp/dd-cleanup-wt-$$.txt"
done
if [ -f "/tmp/dd-cleanup-wt-$$.txt" ]; then
  while IFS=$'\t' read -r size wt; do TOTAL=$((TOTAL+size)); if [ "$APPLY" -eq 1 ]; then git -C "$MAIN_WT" worktree remove "$wt" && echo "removed worktree $wt" || { echo "FAILED worktree remove $wt"; FAIL=1; }; fi; done < "/tmp/dd-cleanup-wt-$$.txt"
  rm -f "/tmp/dd-cleanup-wt-$$.txt"
  [ "$APPLY" -eq 1 ] && git -C "$MAIN_WT" worktree prune
fi

echo; echo "== 2) build products and verification copies (verify-build*, build-*, verify, verify-final, builds/*)"
printf "%-8s %-6s %s\n" "MB" "act" "folder"
for d in "$ROOT"/wp-*/verify-build* "$ROOT"/wp-*/build-* "$ROOT"/wp-*/verify "$ROOT"/wp-*/verify-final "$ROOT"/builds/*; do
  [ -d "$d" ] || continue
  case "$d" in *evidence*|*reports*|*backup*|*asset-production*|*prompts*|*manifest*) continue ;; esac
  size=$(mb "$d"); TOTAL=$((TOTAL+size)); printf "%-8s %-6s %s\n" "$size" "REMOVE" "${d#$ROOT/}"
  if [ "$APPLY" -eq 1 ]; then rm -rf "$d" && echo "removed $d" || { echo "FAILED rm $d"; FAIL=1; }; fi
done

echo; echo "== 3) orphan worktree folders (not registered in git worktree list)"
printf "%-8s %-6s %s\n" "MB" "act" "folder"
REG=$(git -C "$MAIN_WT" worktree list --porcelain | sed -n 's/^worktree //p')
for d in "$ROOT"/wp-*/worktree/deaths-demolition; do
  [ -d "$d" ] || continue
  echo "$REG" | grep -qxF "$d" && continue
  size=$(mb "$d"); TOTAL=$((TOTAL+size)); printf "%-8s %-6s %s\n" "$size" "REMOVE" "${d#$ROOT/}"
  if [ "$APPLY" -eq 1 ]; then rm -rf "$d" && echo "removed $d" || { echo "FAILED rm $d"; FAIL=1; }; fi
done

echo; echo "== 4) manual candidates (never deleted here): .app bundles inside evidence/"
for a in "$ROOT"/wp-*/evidence/**/*.app(N); do printf "%-8s %-6s %s\n" "$(mb "$a")" "manual" "${a#$ROOT/}"; done

echo; echo "TOTAL reclaimable (1+2+3): ${TOTAL} MB  mode=$MODE  failures=$FAIL"
exit $FAIL
