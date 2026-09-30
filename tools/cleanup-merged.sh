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
#   WP-079G (C-00 30.09): a registered worktree whose folder is gone is "prunable": size 0, act PRUNE, reason
#   "prunable (folder missing) → git worktree prune" (--apply runs the prune it already ran after removals; a locked one
#   is kept). Every size helper returns a number (empty/non-numeric → 0 + a note on stderr) — the "bad math expression"
#   crash. Keep-guard (tools/cleanup_guard.py) before every removal, fail closed: `.dd-keep` in the folder or an ancestor below $HOME,
#   in use (a process names it; Player/Unity/slot-owner cwd + relative args), recent (sections 2/3: newest mtime inside
#   < 24 h), protected path segment, or any check that cannot be evaluated → keep. Section 5 reports the non-standard
#   folders (worktree-*, perf-*, release-*, rc-*, wp-*/builds, ~/.codex/worktrees/*) and never deletes them.
#   Sizes: apparentMB = apparent size (clones share blocks) — `du` counts every `cp -c` clone in full; realGainMB =
#   estimated real gain = sum of APFS private bytes (tools/apfs_real_gain.py, lower bound; "n/a (clone-shared)" when the
#   volume cannot report it).
# Exit: 0 ok (dry-run or applied) · 1 a removal failed · 64 usage
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
ROOT="$HOME/Desktop/KADRO/context/projects/deaths-demolition"; APPLY=0
while [ $# -gt 0 ]; do case "$1" in --dry-run) APPLY=0 ;; --apply) APPLY=1 ;; --root) ROOT="$2"; shift ;; *) echo "usage: cleanup-merged.sh [--dry-run] [--apply] [--root DIR]" >&2; exit 64 ;; esac; shift; done
MAIN_WT="$(git -C "$DD_REPO" rev-parse --path-format=absolute --git-common-dir)"; MAIN_WT="${MAIN_WT:h}"
MAIN_HEAD=$(git -C "$MAIN_WT" rev-parse main)
SELF_WT="$(git -C "$DD_REPO" rev-parse --show-toplevel)"
# WP-079G: every size is a number — empty/non-numeric (du failed, folder gone) → 0 with a note on stderr.
num() { case "$1" in ''|*[!0-9]*) print -u2 -r -- "note: size '${1}' of ${2:-?} is not a number → 0"; print 0 ;; *) print -r -- "$1" ;; esac; }
mb() { local k; [ -e "$1" ] || { print 0; return; }; k=$(du -sk "$1" 2>/dev/null | awk 'NR==1{print $1}'); k=$(num "$k" "$1"); print $((k / 1024)); }
# estimated real gain (MB, APFS private bytes, lower bound) or "n/a"
rg() { local r; r=$(python3 "$DD_TOOLS_DIR/apfs_real_gain.py" "$1" 2>/dev/null | awk -F'\t' 'NR==1{print $1}'); case "$r" in ''|*[!0-9]*) print n/a ;; *) print -r -- "$r" ;; esac; }
# keep-guard verdict for one folder ("ok" or "keep (…)"); no answer = keep (fail closed)
guard() { local v; v=$(python3 "$DD_TOOLS_DIR/cleanup_guard.py" "$@" 2>/dev/null | awk -F'\t' 'NR==1{print $1}'); print -r -- "${v:-keep (unknown: guard did not answer)}"; }
row() { printf "%-10s %-10s %-6s %-46s %s\n" "$@"; }
hdr() { row "apparentMB" "realGainMB" "act" "$1" "reason"; }
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
echo "sizes: apparentMB = apparent size (clones share blocks; du counts every cp -c clone in full) · realGainMB = estimated real gain (APFS private bytes, lower bound; n/a (clone-shared) = not measurable)"
TOTAL=0; RTOTAL=0; RNA=0; FAIL=0; OTHER=0
LIST=$(mktemp -t dd-cleanup-wt) || exit 1
trap 'rm -f "$LIST"' EXIT
REG=$(git -C "$MAIN_WT" worktree list --porcelain | sed -n 's/^worktree //p')
addreal() { case "$1" in n/a) RNA=1 ;; *) RTOTAL=$((RTOTAL + $(num "$1" "real gain"))) ;; esac; }

echo; echo "== 1) worktrees (branch integrated into main, clean, not main/self; prunable = registered but folder missing)"
hdr worktree
# porcelain lines: "worktree <path>" / "branch refs/heads/x" / "detached" / "prunable …" / "locked …" — paths may contain spaces
wt=""; br=""; fl=""
git -C "$MAIN_WT" worktree list --porcelain | { while IFS= read -r line; do
  case "$line" in
    "worktree "*) wt="${line#worktree }"; br=""; fl="" ;;
    "branch "*)   br="${line#branch }" ;;
    detached)     br="detached" ;;
    prunable*)    fl="${fl}p" ;;
    locked*)      fl="${fl}l" ;;
    "")           [ -n "$wt" ] && printf '%s\t%s\t%s\n' "$wt" "${br:-detached}" "${fl:--}"; wt=""; br=""; fl="" ;;
  esac
done; [ -n "$wt" ] && printf '%s\t%s\t%s\n' "$wt" "${br:-detached}" "${fl:--}"; } | while IFS=$'\t' read -r wt br fl; do
  [ "$wt" = "$MAIN_WT" ] && continue
  short="${wt#$ROOT/}"
  # WP-079G: a registered worktree whose folder is gone has no size (du printed nothing → "bad math expression").
  if [ ! -d "$wt" ] || [[ "$fl" == *p* ]]; then
    if [[ "$fl" == *l* ]]; then row 0 0 "keep" "$short" "prunable (folder missing) but locked → git worktree unlock, then git worktree prune"
    else row 0 0 "PRUNE" "$short" "prunable (folder missing) → git worktree prune"; printf 'PRUNE\t0\t0\t%s\n' "$wt" >> "$LIST"; fi
    continue
  fi
  size=$(mb "$wt")
  if [ "$br" = "detached" ]; then row "$size" "-" "keep" "$short" "detached HEAD"; continue; fi
  b="${br#refs/heads/}"; tip=$(git -C "$MAIN_WT" rev-parse "$b" 2>/dev/null)
  if [ "$wt" = "$SELF_WT" ]; then row "$size" "-" "keep" "$short" "this script's worktree ($b)"; continue; fi
  how="integrated"
  if ! git -C "$MAIN_WT" merge-base --is-ancestor "$tip" "$MAIN_HEAD"; then
    if picked_into_main "$b"; then how="integrated by cherry-pick"
    else row "$size" "-" "keep" "$short" "not integrated ($b)"; continue; fi
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
  if [ "$own" -eq 0 ]; then row "$size" "-" "keep" "$short" "no package commit of its own yet ($b, tip $(git -C "$MAIN_WT" rev-parse --short=8 "$tip"))"; continue; fi
  # WP-031B/WP-079G keep-guard: .dd-keep, in use (a process names it — e.g. a main-ci driver — or a Player/Unity/slot
  # owner works in it), protected, or cannot tell → keep. Worktrees skip the recency rule (dirty check below).
  v=$(guard --no-recent "$wt")
  if [ "$v" != "ok" ]; then row "$size" "-" "keep" "$short" "$v ($b)"; continue; fi
  dirty=$(git -C "$wt" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
  if [ "$dirty" != "0" ]; then row "$size" "-" "keep" "$short" "dirty ($dirty paths, $b)"; continue; fi
  real=$(rg "$wt")
  row "$size" "$real" "REMOVE" "$short" "$how $b ($(git -C "$MAIN_WT" rev-parse --short=8 "$tip"))"
  printf 'REMOVE\t%s\t%s\t%s\n' "$size" "$real" "$wt" >> "$LIST"
done
PRUNE=0
while IFS=$'\t' read -r act size real wt; do
  [ -n "$wt" ] || continue
  TOTAL=$((TOTAL + $(num "$size" "$wt"))); addreal "$real"
  if [ "$act" = "PRUNE" ]; then PRUNE=1; continue; fi
  if [ "$APPLY" -eq 1 ]; then git -C "$MAIN_WT" worktree remove "$wt" && echo "removed worktree $wt" || { echo "FAILED worktree remove $wt"; FAIL=1; }; fi
  PRUNE=1
done < "$LIST"
[ "$APPLY" -eq 1 ] && [ "$PRUNE" -eq 1 ] && git -C "$MAIN_WT" worktree prune && echo "git worktree prune done"

# WP-079G: remove a folder only when the keep-guard says ok (with the 24 h recency rule); otherwise report why it stays.
rmrow() { local d="$1" size real v
  size=$(mb "$d"); v=$(guard "$d")
  if [ "$v" != "ok" ]; then row "$size" "-" "keep" "${d#$ROOT/}" "$v"; return; fi
  real=$(rg "$d"); TOTAL=$((TOTAL + size)); addreal "$real"; row "$size" "$real" "REMOVE" "${d#$ROOT/}" ""
  if [ "$APPLY" -eq 1 ]; then rm -rf "$d" && echo "removed $d" || { echo "FAILED rm $d"; FAIL=1; }; fi
}

echo; echo "== 2) build products and verification copies (verify-build*, build-*, verify, verify-final, builds/*)"
hdr folder
for d in "$ROOT"/wp-*/verify-build* "$ROOT"/wp-*/build-* "$ROOT"/wp-*/verify "$ROOT"/wp-*/verify-final "$ROOT"/builds/*; do
  [ -d "$d" ] || continue
  case "$d" in *evidence*|*reports*|*backup*|*asset-production*|*prompts*|*manifest*) continue ;; esac
  rmrow "$d"
done

echo; echo "== 3) orphan worktree folders (not registered in git worktree list)"
hdr folder
for d in "$ROOT"/wp-*/worktree/deaths-demolition; do
  [ -d "$d" ] || continue
  echo "$REG" | grep -qxF "$d" && continue
  rmrow "$d"
done

echo; echo "== 4) manual candidates (never deleted here): .app bundles inside evidence/"
for a in "$ROOT"/wp-*/evidence/**/*.app(N); do row "$(mb "$a")" "-" "manual" "${a#$ROOT/}" ""; done

# WP-079G: non-standard folders are reported with their size and never deleted here. A registered worktree inside one
# is judged by 1) (its own rules); the folder row says so.
echo; echo "== 5) other folders (report only, never deleted here): worktree-*, perf-*, release-*, rc-*, wp-*/builds, ~/.codex/worktrees/*"
hdr folder
CODEX="$HOME/.codex/worktrees"
for d in "$ROOT"/worktree-* "$ROOT"/perf-* "$ROOT"/release-* "$ROOT"/rc-* \
         "$ROOT"/wp-*/worktree-* "$ROOT"/wp-*/perf-* "$ROOT"/wp-*/release-* "$ROOT"/wp-*/rc-* "$ROOT"/wp-*/builds "$CODEX"/*; do
  [ -d "$d" ] || continue
  size=$(mb "$d"); real=$(rg "$d"); OTHER=$((OTHER + size))
  n=$(print -r -- "$REG" | awk -v p="$d/" 'index($0, p) == 1' | wc -l | tr -d ' ')
  note=""; [ "$n" -gt 0 ] && note="$n registered worktree(s) inside → see 1)" || { [[ "${d:t}" == worktree-* || "$d" == "$CODEX"/* ]] && note="no registered worktree inside"; }
  short="${d#$ROOT/}"; short="${short/#$HOME/~}"
  row "$size" "$real" "report" "$short" "$(guard "$d")${note:+ · $note}"
done
echo "report-only (5): ${OTHER} MB apparent size (clones share blocks)"

RSUM="≥ ${RTOTAL} MB"; [ "$RNA" -eq 1 ] && RSUM="n/a (clone-shared)"
echo; echo "TOTAL reclaimable (1+2+3): ${TOTAL} MB apparent size (clones share blocks) · estimated real gain ${RSUM}  mode=$MODE  failures=$FAIL"
exit $FAIL
