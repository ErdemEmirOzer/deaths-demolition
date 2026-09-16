#!/bin/zsh
# WP-043 · ADR-043-1 · tools/integrate.sh <commit|branch> --tag <name> [--apply] [--allow-paths <glob,…>] [--s01]
#                                          [--message <tag msg>] [--repo DIR] [--no-post]
#
# C-00's integration gate as one command. DEFAULT IS DRY-RUN (checks + report). --apply integrates.
#   Gate:  single parent · source worktree clean (if the branch has one) · every changed path inside the
#          allowed scope (Assets/_Prototype|_Art|TextMesh Pro, planning/, tools/, wp-*/README*; more via
#          --allow-paths) · ProjectIntegrityTests.cs only with --s01 · secret/credential grep on added lines
#          · tag name free and slash-less · main worktree clean · LFS objects ≥ pointers.
#   Apply: parent == main HEAD → git merge --ff-only; otherwise git cherry-pick --no-commit + automatic
#          planning resolution (03-ROADMAP: both appended sentences kept after the common base line;
#          04-IMPLEMENTATION-PLAN: conflict blocks → HEAD then THEIRS, blank line between) → any other
#          conflict → cherry-pick --abort and stop · index blobs (non-planning) must equal the source
#          commit's blobs → else abort and stop · git commit -C <source> (author, message, Co-Authored-By
#          kept) · annotated tag · tools/post-integrate.sh <tag> (backup + cleanup dry-run + disk + LFS).
#   Never: reset, rebase, push, add -A, branch/tag deletion. main HEAD changes only at the commit step.
# Exit: 0 ok · 3 gate failed · 4 apply conflict (aborted, main unchanged) · 5 post-integrate failed · 64 usage
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
SRC=""; TAG=""; APPLY=0; ALLOW=""; S01=0; MSG=""; NOPOST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --tag) TAG="$2"; shift ;; --apply) APPLY=1 ;; --allow-paths) ALLOW="$2"; shift ;; --s01) S01=1 ;;
    --message) MSG="$2"; shift ;; --repo) DD_REPO="$2"; shift ;; --no-post) NOPOST=1 ;;
    -*) echo "unknown option: $1" >&2; exit 64 ;;
    *) [ -z "$SRC" ] && SRC="$1" || { echo "one source only" >&2; exit 64; } ;;
  esac
  shift
done
[ -n "$SRC" ] && [ -n "$TAG" ] || { echo "usage: integrate.sh <commit|branch> --tag <name> [--apply] [--allow-paths a,b] [--s01] [--message m] [--repo DIR] [--no-post]" >&2; exit 64; }
G=(git -C "$DD_REPO")
fail() { echo "GATE FAIL: $*"; FAILS=$((FAILS+1)); }
# Clean rollback of a failed --no-commit pick without git reset: every path the pick touched goes back
# to HEAD (restore) or is dropped again if HEAD never had it. main HEAD is untouched at this point.
abort_pick() {
  "${G[@]}" cherry-pick --quit >/dev/null 2>&1
  for f in "${FILES[@]}"; do
    if "${G[@]}" cat-file -e "HEAD:$f" 2>/dev/null; then "${G[@]}" restore --staged --worktree -- "$f" 2>/dev/null
    else "${G[@]}" rm -q --cached --force -- "$f" >/dev/null 2>&1; rm -f "$DD_REPO/$f"; fi
  done
  echo "aborted: $("${G[@]}" status --porcelain | wc -l | tr -d ' ') dirty paths remain (expected 0); main unchanged"
}
FAILS=0
MAIN=$("${G[@]}" rev-parse main) || exit 64
C=$("${G[@]}" rev-parse --verify "$SRC^{commit}" 2>/dev/null) || { echo "unknown commit/branch: $SRC" >&2; exit 64; }
SUBJ=$("${G[@]}" log -1 --format=%s "$C")
echo "integrate $( [ "$APPLY" -eq 1 ] && echo APPLY || echo DRY-RUN )  source=$SRC → $C  main=$MAIN"
echo "subject: $SUBJ"

# --- gate 1: single parent, not already on main
NPAR=$("${G[@]}" rev-list --parents -n1 "$C" | wc -w | tr -d ' '); NPAR=$((NPAR-1))
[ "$NPAR" -eq 1 ] || fail "commit must have exactly one parent (has $NPAR)"
"${G[@]}" merge-base --is-ancestor "$C" "$MAIN" && fail "commit is already contained in main"
PARENT=$("${G[@]}" rev-parse "$C^" 2>/dev/null || echo none)
FF=0; [ "$PARENT" = "$MAIN" ] && FF=1
echo "parent=$PARENT mode=$( [ "$FF" -eq 1 ] && echo ff-only || echo cherry-pick )"

# --- gate 2: source worktree clean (if the source is a branch with a worktree)
SRCWT=$("${G[@]}" worktree list --porcelain | awk -v b="refs/heads/$SRC" '/^worktree /{w=substr($0,10)} /^branch /{if($2==b)print w}')
if [ -n "$SRCWT" ]; then
  D=$(git -C "$SRCWT" status --porcelain | wc -l | tr -d ' ')
  [ "$D" = "0" ] && echo "source worktree clean: $SRCWT" || fail "source worktree dirty ($D paths): $SRCWT"
fi

# --- gate 3: path scope
ALLOWED=("prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/" "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/" "prototype/DeathsDemolition_PlayableConcept/Assets/TextMesh Pro/" "planning/" "tools/")
for extra in ${(s:,:)ALLOW}; do ALLOWED+=("$extra"); done
FILES=("${(@f)$("${G[@]}" diff-tree --no-commit-id --name-only -r "$C")}")
echo "changed paths: ${#FILES}"
for f in "${FILES[@]}"; do
  ok=0
  for a in "${ALLOWED[@]}"; do [[ "$f" == "$a"* ]] && ok=1; done
  [[ "$f" == wp-*/README* ]] && ok=1
  [[ "$f" == *ProjectIntegrityTests.cs ]] && [ "$S01" -eq 0 ] && { fail "ProjectIntegrityTests.cs is S-01-owned (pass --s01)"; ok=1; }
  [ "$ok" -eq 1 ] || fail "path outside allowed scope: $f"
done

# --- gate 4: secrets on added lines (false-positive words excluded)
SECRET='AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY|sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{30,}|xox[baprs]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z_-]{30,}|(api[_-]?key|secret[_-]?key|password|passwd|access[_-]?token)[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']{8,}'
HITS=$("${G[@]}" diff "$C^" "$C" -- . ':!*.png' ':!*.fbx' ':!*.asset' 2>/dev/null | grep -E '^\+[^+]' | grep -Ei "$SECRET" | grep -Ev 'CancellationToken|CompletionToken|tokenizer|TokenVerdict|PlaceholderJson|EXAMPLE|example\.com' | head -5)
if [ -n "$HITS" ]; then fail "secret-like content in added lines:"; echo "$HITS" | cut -c1-160; else echo "secret grep: clean"; fi

# --- gate 5: tag
[[ "$TAG" == */* ]] && fail "tag name must not contain '/': $TAG"
"${G[@]}" rev-parse -q --verify "refs/tags/$TAG" >/dev/null 2>&1 && fail "tag already exists: $TAG"

# --- gate 6: main worktree clean
MD=$("${G[@]}" status --porcelain | wc -l | tr -d ' ')
[ "$MD" = "0" ] && echo "main worktree clean" || fail "main worktree dirty ($MD paths)"

# --- gate 7: LFS
LP=$("${G[@]}" lfs ls-files 2>/dev/null | wc -l | tr -d ' '); LO=$(find "$("${G[@]}" rev-parse --path-format=absolute --git-common-dir)/lfs/objects" -type f 2>/dev/null | wc -l | tr -d ' ')
[ "$LO" -ge "$LP" ] && echo "lfs pointers=$LP objects=$LO" || fail "LFS objects ($LO) < pointers ($LP)"

if [ "$FAILS" -gt 0 ]; then echo "RESULT: gate failed ($FAILS)"; exit 3; fi
echo "RESULT: gate passed"
[ "$APPLY" -eq 1 ] || { echo "(dry-run; add --apply to integrate)"; exit 0; }

# ================================================================== apply
if [ "$FF" -eq 1 ]; then
  "${G[@]}" merge --ff-only "$C" >/dev/null || { echo "ff-only merge failed"; exit 4; }
else
  "${G[@]}" cherry-pick --no-commit "$C" >/dev/null 2>&1
  CONF=("${(@f)$("${G[@]}" diff --name-only --diff-filter=U)}")
  for f in "${CONF[@]}"; do
    [ -n "$f" ] || continue
    case "$f" in
      planning/03-ROADMAP.md)
        "${G[@]}" show ":1:$f" > "/tmp/dd-int-base.$$"; "${G[@]}" show ":2:$f" > "/tmp/dd-int-ours.$$"; "${G[@]}" show ":3:$f" > "/tmp/dd-int-theirs.$$"
        python3 - "/tmp/dd-int-base.$$" "/tmp/dd-int-ours.$$" "/tmp/dd-int-theirs.$$" "$DD_REPO/$f" <<'PY' || { echo "03-ROADMAP: automatic merge failed"; abort_pick; exit 4; }
import sys
base,ours,theirs=[open(p,encoding='utf-8').read().split('\n') for p in sys.argv[1:4]]
if len(base)!=len(ours) or len(base)!=len(theirs): sys.exit(1)   # only same-line appends are merged automatically
out=[]
for b,o,t in zip(base,ours,theirs):
    if o==t or t==b: out.append(o)
    elif o==b: out.append(t)
    elif o.startswith(b) and t.startswith(b): out.append(o+t[len(b):])
    else: sys.exit(1)
open(sys.argv[4],'w',encoding='utf-8').write('\n'.join(out))
PY
        rm -f "/tmp/dd-int-base.$$" "/tmp/dd-int-ours.$$" "/tmp/dd-int-theirs.$$"; "${G[@]}" add "$f"; echo "resolved $f (both appended sentences kept)" ;;
      planning/04-IMPLEMENTATION-PLAN.md)
        python3 - "$DD_REPO/$f" <<'PY' || { echo "04: automatic resolution failed"; abort_pick; exit 4; }
import sys,re
p=sys.argv[1]; s=open(p,encoding='utf-8').read()
pat=re.compile(r'<<<<<<< [^\n]*\n(.*?)\n?=======\n(.*?)\n?>>>>>>> [^\n]*\n', re.S)
n=[0]
def rep(m):
    n[0]+=1; a=m.group(1).rstrip('\n'); b=m.group(2).rstrip('\n'); return a+'\n\n'+b+'\n'
s2=pat.sub(rep,s)
if n[0]==0 or '<<<<<<<' in s2 or '>>>>>>>' in s2: sys.exit(1)
open(p,'w',encoding='utf-8').write(s2)
PY
        "${G[@]}" add "$f"; echo "resolved $f (HEAD section then THEIRS section)" ;;
      *) echo "CONFLICT in $f — not auto-resolvable; aborting (main unchanged)"; "${G[@]}" diff --name-only --diff-filter=U; abort_pick; exit 4 ;;
    esac
  done
  # verification: every non-planning path in the source commit must be byte-identical in the index
  BAD=0
  for f in "${FILES[@]}"; do
    [[ "$f" == planning/* ]] && continue
    S=$("${G[@]}" rev-parse -q --verify "$C:$f" 2>/dev/null || echo deleted); I=$("${G[@]}" rev-parse -q --verify ":$f" 2>/dev/null || echo deleted)
    [ "$S" = "$I" ] || { echo "VERIFY FAIL: $f (source $S, index $I)"; BAD=1; }
  done
  [ "$BAD" -eq 0 ] || { abort_pick; exit 4; }
  "${G[@]}" commit -q -C "$C" || { echo "commit failed"; abort_pick; exit 4; }
fi
NEW=$("${G[@]}" rev-parse main)
"${G[@]}" tag -a "$TAG" -m "${MSG:-$SUBJ}" "$NEW"
echo "integrated: main=$NEW ($( [ "$FF" -eq 1 ] && echo ff-only || echo cherry-pick )) tag=$TAG tags=$("${G[@]}" tag | wc -l | tr -d ' ')"
if [ "$NOPOST" -eq 0 ] && [ -x "$DD_TOOLS_DIR/post-integrate.sh" ] && [ "$DD_REPO" = "${DD_TOOLS_DIR:h}" ]; then
  DD_REPO="$DD_REPO" "$DD_TOOLS_DIR/post-integrate.sh" "$TAG" | tail -4 || { echo "post-integrate failed"; exit 5; }
fi
exit 0
