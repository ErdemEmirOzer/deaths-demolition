#!/bin/zsh
# WP-025 · ADR-025-1 · tools/backup-repo.sh [--dest DIR] [--keep N] [--verify-only DIR]
#
# Local, manual backup of the whole repository (there is no remote by user decision):
#   <dest>/<utc>/repo.bundle        git bundle --all   (every branch and tag, verified)
#   <dest>/<utc>/lfs/objects/…      rsync of .git/lfs/objects (content-addressed; counts must match)
#   <dest>/<utc>/BACKUP.json        summary: HEAD, refs, LFS object count/bytes, verification
# Default <dest> = ~/Backups/deaths-demolition. Only new directories are written; nothing of the
# user's is touched or deleted except older backups of this tool beyond --keep (default 3).
# Restore: git clone <bundle> <dir> && rsync -a <backup>/lfs/objects/ <dir>/.git/lfs/objects/ && git -C <dir> lfs checkout
# Exit: 0 ok · 10 bundle failed · 11 verify failed · 12 LFS mismatch (partial backup kept) · 13 no space · 64 usage
# LFS copy is two-pass (WP-031): re-run rsync until source/backup object counts agree (≤ 3 passes).
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
DEST="$HOME/Backups/deaths-demolition"; KEEP=3; VERIFY_ONLY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dest) DEST="$2"; shift ;;
    --keep) KEEP="$2"; shift ;;
    --verify-only) VERIFY_ONLY="$2"; shift ;;
    *) echo "usage: backup-repo.sh [--dest DIR] [--keep N] [--verify-only DIR]" >&2; exit 64 ;;
  esac
  shift
done
GITDIR="$(git -C "$DD_REPO" rev-parse --git-common-dir)"; [ "${GITDIR:0:1}" = "/" ] || GITDIR="$DD_REPO/$GITDIR"
LFSOBJ="$GITDIR/lfs/objects"
lfs_count() { [ -d "$1" ] && find "$1" -type f | wc -l | tr -d ' ' || echo 0; }
lfs_bytes() { [ -d "$1" ] && find "$1" -type f -exec stat -f %z {} + | awk '{s+=$1} END{print s+0}' || echo 0; }

verify() {
  local dir="$1" ok=1
  git bundle verify "$dir/repo.bundle" > "$dir/verify.txt" 2>&1 || ok=0
  local src=$(lfs_count "$LFSOBJ") dst=$(lfs_count "$dir/lfs/objects")
  echo "lfs objects source=$src backup=$dst" >> "$dir/verify.txt"
  [ "$src" = "$dst" ] || { echo "LFS OBJECT COUNT MISMATCH" >> "$dir/verify.txt"; return 12; }
  [ "$ok" -eq 1 ] || return 11
  return 0
}

if [ -n "$VERIFY_ONLY" ]; then verify "$VERIFY_ONLY"; X=$?; cat "$VERIFY_ONLY/verify.txt"; exit $X; fi

STAMP=$(dd_utc); DIR="$DEST/$STAMP"; mkdir -p "$DIR" || exit 13
NEED_KB=$(( $(du -sk "$GITDIR/objects" | cut -f1) + $(du -sk "$LFSOBJ" 2>/dev/null | cut -f1 || echo 0) ))
FREE_KB=$(df -k "$DEST" | awk 'NR==2{print $4}')
echo "$(date -u +%FT%TZ) backup start dest=$DIR needKB=$NEED_KB freeKB=$FREE_KB" | tee "$DIR/log.txt"
[ "$FREE_KB" -gt $((NEED_KB * 12 / 10)) ] || { echo "not enough space at $DEST (need ~${NEED_KB}K, free ${FREE_KB}K)" | tee -a "$DIR/log.txt"; exit 13; }

T0=$(date +%s)
git -C "$DD_REPO" bundle create "$DIR/repo.bundle" --all >> "$DIR/log.txt" 2>&1 || { echo "bundle failed" | tee -a "$DIR/log.txt"; exit 10; }
mkdir -p "$DIR/lfs/objects"
# WP-031: two-pass rsync. Parallel sessions add LFS objects while the first pass copies; repeat
# (at most 2 extra passes) until the source and backup counts agree. A remaining mismatch keeps
# the partial backup on disk and is reported through verify (exit 12).
PASSES=0
while [ -d "$LFSOBJ" ]; do
  PASSES=$((PASSES+1))
  rsync -a "$LFSOBJ/" "$DIR/lfs/objects/" >> "$DIR/log.txt" 2>&1
  SRC=$(lfs_count "$LFSOBJ"); DST=$(lfs_count "$DIR/lfs/objects")
  echo "$(date -u +%FT%TZ) lfs pass $PASSES source=$SRC backup=$DST" | tee -a "$DIR/log.txt"
  [ "$SRC" = "$DST" ] && break
  [ "$PASSES" -ge 3 ] && break
done
git -C "$DD_REPO" for-each-ref --format='%(refname) %(objectname)' > "$DIR/refs.txt"
verify "$DIR"; V=$?
T1=$(date +%s)
cat > "$DIR/BACKUP.json" <<JSON
{
  "utc": "$STAMP",
  "repo": "$DD_REPO",
  "head": "$(git -C "$DD_REPO" rev-parse HEAD)",
  "refs": $(wc -l < "$DIR/refs.txt" | tr -d ' '),
  "bundleBytes": $(stat -f %z "$DIR/repo.bundle"),
  "lfsObjects": $(lfs_count "$DIR/lfs/objects"),
  "lfsBytes": $(lfs_bytes "$DIR/lfs/objects"),
  "lfsObjectsSource": $(lfs_count "$LFSOBJ"),
  "lfsPasses": ${PASSES:-0},
  "verified": $([ "$V" -eq 0 ] && echo true || echo false),
  "seconds": $((T1-T0))
}
JSON
echo "$(date -u +%FT%TZ) backup done verify=$V seconds=$((T1-T0)) bundle=$(stat -f %z "$DIR/repo.bundle")B lfsObjects=$(lfs_count "$DIR/lfs/objects")" | tee -a "$DIR/log.txt"
# retention: keep the newest $KEEP backups made by this tool (directories named <utc>); skipped when this run is not verified
[ "$V" -eq 0 ] && ls -1d "$DEST"/[0-9]*T[0-9]*Z 2>/dev/null | sort | awk -v k="$KEEP" '{a[NR]=$0} END{for(i=1;i<=NR-k;i++)print a[i]}' | while read -r old; do [ -f "$old/BACKUP.json" ] && rm -rf "$old" && echo "pruned $old" | tee -a "$DIR/log.txt"; done
cat "$DIR/BACKUP.json"
exit $V
