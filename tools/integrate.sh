#!/bin/zsh
# WP-043 · ADR-043-1 · tools/integrate.sh <commit|branch> --tag <name> [--apply] [--allow-paths <glob,…>] [--s01]
#                                          [--message <tag msg>] [--repo DIR] [--no-post] [--subject-exempt <reason>]
#                                          [--acoustic-exempt <reason>]
#
# C-00's integration gate as one command. DEFAULT IS DRY-RUN (checks + report). --apply integrates.
#   Gate:  single parent · source worktree clean (if the branch has one) · every changed path inside the
#          allowed scope (Assets/_Prototype|_Art|TextMesh Pro, project README.md/PROTOTYPE-ONLY.md, planning/, tools/, wp-*/README*; more via
#          --allow-paths) · ProjectIntegrityTests.cs only with --s01 · secret/credential grep on added lines
#          · acoustic map baked from its source when the source touches it (gate 4c, C-00 10.10; also on the merged index;
#          --acoustic-exempt "<reason>" skips it, loudly)
#          · tag name free and slash-less · main worktree clean · LFS objects ≥ pointers · commit-message format
#          (Codex-audit K-02, C-00 30.09): subject `(WP|AP)-NNN…: … (C-00 dd.mm)`, body with a test line (starts
#          with Test/Tests/Guarded, any case, or mentions EditMode/PlayMode); --subject-exempt "<reason>" skips it
#          — and a WIP commit fails it ("WIP" in the subject label before the first ':', or a body line starting
#          "Not for main" / "Test: WIP"; C-00 06.10)
#          for C-00's own reverts/hotfixes and the reason is printed in the RESULT line (no silent bypass).
#   Apply: parent == main HEAD → git merge --ff-only; otherwise git cherry-pick --no-commit + automatic
#          planning resolution (03-ROADMAP: git 3-way merge, then each conflicting single line = base + both sides' character-level insertions in offset order, WP-043B/051C;
#          04-IMPLEMENTATION-PLAN: conflict blocks → HEAD then THEIRS, blank line between; 00/01 DEC/REQ tables: HEAD
#          rows then THEIRS rows, then re-numbered check) → any other
#          conflict → rollback and stop · non-planning paths must carry the source commit's change (blob identity, or
#          identical +/- hunks when git 3-way-merged the file) → else rollback and stop · git commit -C <source> (author, message, Co-Authored-By
#          kept) · annotated tag · tools/post-integrate.sh <tag> (backup + cleanup dry-run + disk + LFS).
#   Never: reset, rebase, push, add -A, branch/tag deletion. main HEAD changes only at the commit step.
#   --compile (WP-043C, with --apply): after the apply, tools/compile-check.sh compiles the new main HEAD in the
#          main-ci checkout (batchmode -quit, errorsCS) — commits that compile alone can break main together.
# Exit: 0 ok · 3 gate failed · 4 apply conflict (aborted, main unchanged) · 5 post-integrate failed · 6 main does not
#       compile after the apply (--compile; main keeps the commit — revert or hotfix) · 7 rollback unverified / dirty
#       paths left (main HEAD unchanged, working tree NOT clean — inspect) · 64 usage
set -u
setopt nullglob
REPO_EXPLICIT=${DD_REPO:+1}   # DD_REPO from the caller's environment counts as explicit, like --repo
source "${0:A:h}/dd-env.sh"
SRC=""; TAG=""; APPLY=0; ALLOW=""; S01=0; MSG=""; NOPOST=0; VERBOSE=0; COMPILE=0; EXEMPT=""; EXEMPT_SET=0; AC_EXEMPT=""; AC_EXEMPT_SET=0
say() { [ "$VERBOSE" -eq 1 ] && echo "$@"; return 0; }   # informational lines only with --verbose (HIZLI HAT §7: 3-line output)
while [ $# -gt 0 ]; do
  case "$1" in
    --tag) TAG="$2"; shift ;; --apply) APPLY=1 ;; --allow-paths) ALLOW="$2"; shift ;; --s01) S01=1 ;;
    --message) MSG="$2"; shift ;; --repo) DD_REPO="$2"; REPO_EXPLICIT=1; shift ;; --no-post) NOPOST=1 ;; --compile) COMPILE=1 ;; --verbose|-v) VERBOSE=1 ;;
    --subject-exempt) EXEMPT="${2-}"; EXEMPT_SET=1; [ $# -ge 2 ] && shift ;;
    --acoustic-exempt) AC_EXEMPT="${2-}"; AC_EXEMPT_SET=1; [ $# -ge 2 ] && shift ;;
    -*) echo "unknown option: $1" >&2; exit 64 ;;
    *) [ -z "$SRC" ] && SRC="$1" || { echo "one source only" >&2; exit 64; } ;;
  esac
  shift
done
[ -n "$SRC" ] && [ -n "$TAG" ] || { echo "usage: integrate.sh <commit|branch> --tag <name> [--apply] [--allow-paths a,b] [--s01] [--message m] [--repo DIR] [--no-post] [--compile] [--subject-exempt reason] [--verbose]" >&2; exit 64; }
# Codex-audit K-02 (C-00 30.09): the exemption is explicit and carries a reason — an empty one is a usage error.
[ "$EXEMPT_SET" -eq 0 ] || [[ "$EXEMPT" == *[![:space:]]* ]] || { echo "--subject-exempt needs a non-empty reason" >&2; exit 64; }
[ "$AC_EXEMPT_SET" -eq 0 ] || [[ "$AC_EXEMPT" == *[![:space:]]* ]] || { echo "--acoustic-exempt needs a non-empty reason" >&2; exit 64; }
# S-09 / C-00 05.10: without --repo (or DD_REPO) the repository is its MAIN worktree, not the checkout this script runs
# from — run from a (possibly dirty) linked worktree it used to judge that worktree as "main".
REPO_DEFAULTED=0
if [ -z "${REPO_EXPLICIT:-}" ]; then
  MAINWT=$(git -C "${0:A:h}" worktree list --porcelain 2>/dev/null | sed -n '1s/^worktree //p')
  if [ -n "$MAINWT" ] && [ "${MAINWT:A}" != "${DD_REPO:A}" ]; then
    echo "repo: the main worktree $MAINWT (this script runs from ${DD_REPO}; --repo overrides)"
    DD_REPO="$MAINWT"; REPO_DEFAULTED=1
  fi
fi
G=(git -C "$DD_REPO")
fail() { echo "GATE FAIL: $*"; FAILS=$((FAILS+1)); }
# Clean rollback of a failed --no-commit pick without git reset: every path the pick touched goes back
# to HEAD (restore) or is dropped again if HEAD never had it. main HEAD is untouched at this point.
# Codex-audit M-08 (C-00 30.09): returns 0 ONLY when the tree is verified clean; 7 when the rollback cannot be
# verified (any `git status` failure) or dirty paths remain — callers `abort_pick || exit 7; exit 4`, so a failed
# rollback is never reported as a clean "apply conflict (main unchanged)".
abort_pick() {
  local verified_status
  verified_status=$("${G[@]}" status --porcelain=v1 -z) || { echo "ROLLBACK UNVERIFIED: status failed; no cleanup"; return 7; }
  "${G[@]}" cherry-pick --quit >/dev/null 2>&1   # no sequencer state with --no-commit; --abort would fail
  # WP-051E: every dirty path from `status --porcelain -z`, parsed by a small python (rename entries carry
  # TWO paths, unmerged DU/UD/AA/UU entries are ordinary paths here). Up to 3 passes: a path that HEAD knows
  # is restored (index + worktree), anything else is dropped from the index and the disk.
  local pass=0 left=1
  while [ "$left" != "0" ] && [ "$pass" -lt 3 ]; do
    pass=$((pass+1))
    verified_status=$("${G[@]}" status --porcelain=v1 -z) || { echo "ROLLBACK UNVERIFIED: status failed; no path cleanup"; return 7; }
    print -rn -- "$verified_status" | python3 -c '
import sys
data = sys.stdin.buffer.read().split(b"\0"); i = 0; out = []
while i < len(data):
    e = data[i]; i += 1
    if len(e) < 4: continue
    xy, path = e[:2], e[3:]
    out.append(path)
    if xy[0:1] in (b"R", b"C") or xy[1:2] in (b"R", b"C"):   # rename/copy: next record is the original path
        if i < len(data) and data[i]: out.append(data[i])
        i += 1
sys.stdout.buffer.write(b"\n".join(out) + b"\n")
' | while IFS= read -r f; do
      [ -n "$f" ] || continue
      if "${G[@]}" cat-file -e "HEAD:$f" 2>/dev/null; then "${G[@]}" restore --staged --worktree -- "$f" 2>/dev/null
      else "${G[@]}" rm -q --cached --force -- "$f" >/dev/null 2>&1; rm -f "$DD_REPO/$f"; fi
    done
    verified_status=$("${G[@]}" status --porcelain) || { echo "ROLLBACK UNVERIFIED: post-restore status failed"; return 7; }
    left=0
    [ -z "$verified_status" ] || left=$(print -r -- "$verified_status" | wc -l | tr -d ' ')
  done
  echo "aborted: $left dirty paths remain (expected 0); main unchanged"
  [ "$left" = "0" ] || { echo "WARN: rollback left paths — inspect with: git -C '$DD_REPO' status"; "${G[@]}" status --porcelain | head -10; return 7; }
  return 0
}
FAILS=0
MAIN=$("${G[@]}" rev-parse main) || exit 64
C=$("${G[@]}" rev-parse --verify "$SRC^{commit}" 2>/dev/null) || { echo "unknown commit/branch: $SRC" >&2; exit 64; }
SUBJ=$("${G[@]}" log -1 --format=%s "$C")
echo "integrate $( [ "$APPLY" -eq 1 ] && echo APPLY || echo DRY-RUN ) $SRC → ${C:0:12} on main ${MAIN:0:12}: $SUBJ" | cut -c1-160
say "subject: $SUBJ"

# --- gate 1: single parent, not already on main
NPAR=$("${G[@]}" rev-list --parents -n1 "$C" | wc -w | tr -d ' '); NPAR=$((NPAR-1))
[ "$NPAR" -eq 1 ] || fail "commit must have exactly one parent (has $NPAR)"
"${G[@]}" merge-base --is-ancestor "$C" "$MAIN" && fail "commit is already contained in main"
PARENT=$("${G[@]}" rev-parse "$C^" 2>/dev/null || echo none)
FF=0; [ "$PARENT" = "$MAIN" ] && FF=1
say "parent=$PARENT mode=$( [ "$FF" -eq 1 ] && echo ff-only || echo cherry-pick )"

# --- gate 2: source worktree clean (if the source is a branch with a worktree)
SRCWT=$("${G[@]}" worktree list --porcelain | awk -v b="refs/heads/$SRC" '/^worktree /{w=substr($0,10)} /^branch /{if($2==b)print w}')
if [ -n "$SRCWT" ]; then
  if D=$(git -C "$SRCWT" status --porcelain); then
    [ -z "$D" ] && say "source worktree clean: $SRCWT" || fail "source worktree dirty: $SRCWT"
  else
    fail "cannot read source worktree status: $SRCWT"
  fi
fi

# --- gate 3: path scope
ALLOWED=("prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/" "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/" "prototype/DeathsDemolition_PlayableConcept/Assets/TextMesh Pro/" "prototype/DeathsDemolition_PlayableConcept/README.md" "prototype/DeathsDemolition_PlayableConcept/PROTOTYPE-ONLY.md" "planning/" "tools/")
for extra in ${(s:,:)ALLOW}; do ALLOWED+=("$extra"); done
FILES=("${(@f)$("${G[@]}" diff-tree --no-commit-id --name-only -r "$C")}")
say "changed paths: ${#FILES}"
for f in "${FILES[@]}"; do
  ok=0
  for a in "${ALLOWED[@]}"; do [[ "$f" == "$a"* ]] && ok=1; done
  [[ "$f" == wp-*/README* ]] && ok=1
  [[ "$f" == *ProjectIntegrityTests.cs ]] && [ "$S01" -eq 0 ] && { fail "ProjectIntegrityTests.cs is S-01-owned (pass --s01)"; ok=1; }
  [ "$ok" -eq 1 ] || fail "path outside allowed scope: $f"
done

# --- gate 4: secrets on added lines (false-positive words excluded)
# C-00 05.10: key constants are case-SENSITIVE (AKIA…, AIza…, ghp_, sk-, xox*, PRIVATE KEY) — `grep -Ei` read a Turkish
# test name (S-02 179f533c: …Sonraki·Atlananlari·Indirir, whose lower-case "a-k-i-A…" run looks like the AWS prefix) as
# an AWS key. Only the `password = "…"`-style assignment stays case-insensitive.
SECRET_KEYS='AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY|sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{30,}|xox[baprs]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z_-]{30,}'
SECRET_ASSIGN='(api[_-]?key|secret[_-]?key|password|passwd|access[_-]?token)[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']{8,}'
ADDED=$("${G[@]}" diff "$C^" "$C" -- . ':!*.png' ':!*.fbx' ':!*.asset' 2>/dev/null | grep -E '^\+[^+]')
HITS=$( { print -r -- "$ADDED" | grep -E "$SECRET_KEYS"; print -r -- "$ADDED" | grep -Ei "$SECRET_ASSIGN"; } \
  | grep -Ev 'CancellationToken|CompletionToken|tokenizer|TokenVerdict|PlaceholderJson|EXAMPLE|example\.com' | head -5)
if [ -n "$HITS" ]; then fail "secret-like content in added lines:"; echo "$HITS" | cut -c1-160; else say "secret grep: clean"; fi

# --- gate 4b: DEC/REQ numbering after the source is applied (ProjectIntegrityTests rule: DEC = 1..N in file
#     order, REQ strictly increasing; no number may already exist on main — WP-037B/WP-040 both took REQ-156)
check_ids() {  # $1 = planning/00 content file, $2 = planning/01 content file → prints problems, exit 1 on any
  python3 - "$1" "$2" <<'PY'
import sys, re, os
for a in sys.argv[1:3]:
    if not os.path.isfile(a): print(f"numbering check: not a file: {a!r} (argv={sys.argv[1:]})"); sys.exit(1)
# Same rule as ProjectIntegrityTests.TabloKimlikleri: collect, SORT numerically (the tables are not in
# authored order), then DEC must be exactly 1..N and REQ must have no duplicates (authored gaps allowed).
dec=sorted(int(m) for m in re.findall(r'^\| DEC-(\d{3}) \|', open(sys.argv[1],encoding='utf-8').read(), re.M))
req=sorted(int(m) for m in re.findall(r'^\| REQ-(\d{3}) \|', open(sys.argv[2],encoding='utf-8').read(), re.M))
bad=[]
for i,v in enumerate(dec):
    if v!=i+1: bad.append(f"DEC-{v:03d} (expected DEC-{i+1:03d}: duplicate or gap)"); break
for i in range(1,len(req)):
    if req[i]==req[i-1]: bad.append(f"REQ-{req[i]:03d} duplicated"); break
print("\n".join(bad)); sys.exit(1 if bad else 0)
PY
}
if printf '%s\n' "${FILES[@]}" | grep -qE '^planning/0[01]-'; then
  T0="${TMPDIR:-/tmp}/dd-int-$$"; mkdir -p "$T0"
  # (a) source tree itself must be consistent
  "${G[@]}" show "$C:planning/00-PROJECT-PLAN.md" > "$T0/00s.md" 2>/dev/null; "${G[@]}" show "$C:planning/01-REQUIREMENTS.md" > "$T0/01s.md" 2>/dev/null
  P=$(check_ids "$T0/00s.md" "$T0/01s.md") || fail "DEC/REQ numbering in the source tree: $P"
  # (b) numbers the source ADDS must not already exist on main
  for f in planning/00-PROJECT-PLAN.md planning/01-REQUIREMENTS.md; do
    "${G[@]}" show "main:$f" > "$T0/main.md" 2>/dev/null || continue
    # ids new in the source (present in C, absent in C^) must not exist on main
    "${G[@]}" show "$C:$f" 2>/dev/null | grep -oE '^\| (DEC|REQ)-[0-9]{3} \|' | sort -u > "$T0/c.ids"
    "${G[@]}" show "$C^:$f" 2>/dev/null | grep -oE '^\| (DEC|REQ)-[0-9]{3} \|' | sort -u > "$T0/p.ids"
    for id in $(comm -23 "$T0/c.ids" "$T0/p.ids" | grep -oE '(DEC|REQ)-[0-9]{3}'); do
      grep -qF "| $id |" "$T0/main.md" && fail "$id is new in the source but already exists on main ($f)"
    done
  done
  rm -rf "$T0"
  say "DEC/REQ numbering: checked"
fi

# --- gate 4c: the acoustic map is baked from its source (C-00 10.10; gt SHOULD-1 on c94625a7). AudioCatalogBuilder
#     .BuildAcousticSpaces re-bakes DD_AcousticSpaces.asset whenever its _sourceHash != sha256(acoustic-spaces-v6.json)[:16]
#     — a stale pair rewrites a tracked file in every Unity run: main-ci refuses its dirty CI checkout (exit 3), the FINAL
#     tree goes dirty. One check: tools/qa/acoustic_bake.py (main-ci runs it too, on every SHA — merges included). Here it
#     runs only when the source touches either file (a branch cut from an older, stale main that touches neither is not
#     refused); the pair after the integration = the source's version of a touched file, main's of the other — and after a
#     cherry-pick the merged index is checked again before the commit (gate 4c gt SHOULD-2b). The json and the asset go in
#     ONE commit; --acoustic-exempt "<reason>" lets a json-only commit through, loudly (the RESULT line), when its re-bake
#     follows at once.
AC_JSON="prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Environment/World/V6/acoustic-spaces-v6.json"
AC_MAP="prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Resources/DD_AcousticSpaces.asset"
AC_TOOL="${0:A:h}/qa/acoustic_bake.py"
AC_CHECK=0
if printf '%s\n' "${FILES[@]}" | grep -qxF -e "$AC_JSON" -e "$AC_MAP"; then
  if [ "$AC_EXEMPT_SET" -eq 1 ]; then
    say "acoustic map: not checked (--acoustic-exempt: $AC_EXEMPT)"
  elif [ ! -f "$AC_TOOL" ]; then
    fail "gate 4c: $AC_TOOL missing — the integrating checkout must carry main's tools"
  else
    AC_CHECK=1
    T4="${TMPDIR:-/tmp}/dd-int-ac-$$"; mkdir -p "$T4"
    for p in AC_JSON AC_MAP; do
      ref=main; printf '%s\n' "${FILES[@]}" | grep -qxF "${(P)p}" && ref=$C
      "${G[@]}" show "$ref:${(P)p}" > "$T4/$p" 2>/dev/null || rm -f "$T4/$p"
    done
    if P=$(python3 "$AC_TOOL" files "$T4/AC_JSON" "$T4/AC_MAP"); then say "acoustic map: $P"; else fail "gate 4c: $P"; fi
    rm -rf "$T4"
  fi
fi

# --- gate 5: tag
[[ "$TAG" == */* ]] && fail "tag name must not contain '/': $TAG"
"${G[@]}" rev-parse -q --verify "refs/heads/$TAG" >/dev/null 2>&1 && echo "WARN: a branch named '$TAG' exists — git will print 'refname is ambiguous'; prefer a tag name different from the branch"
"${G[@]}" rev-parse -q --verify "refs/tags/$TAG" >/dev/null 2>&1 && fail "tag already exists: $TAG"

# --- gate 6: main worktree clean
if MD=$("${G[@]}" status --porcelain); then
  [ -z "$MD" ] && say "main worktree clean" || fail "main worktree dirty: $DD_REPO (not the main checkout? pass --repo <main checkout>)"
else
  fail "cannot read main worktree status"
fi

# --- gate 7: LFS
LP=$("${G[@]}" lfs ls-files 2>/dev/null | wc -l | tr -d ' '); LO=$(find "$("${G[@]}" rev-parse --path-format=absolute --git-common-dir)/lfs/objects" -type f 2>/dev/null | wc -l | tr -d ' ')
[ "$LO" -ge "$LP" ] && say "lfs pointers=$LP objects=$LO" || fail "LFS objects ($LO) < pointers ($LP)"

# --- gate 8: commit-message format (Codex-audit K-02, C-00 30.09). Subject in the house format ending in the
#     C-00 date stamp; body names how the change was tested. --subject-exempt skips it, loudly (RESULT line).
SUBJ_RE='^(WP|AP)-[0-9]{3}[^:]*: .+ \(C-00 [0-9]{2}\.[0-9]{2}\)$'
# G4 D8 next slice (C-00 06.10; git-safety SHOULD-6, review NIT-1/2): WIP = "WIP" (any case) in the subject's label before
# its first ':' ("WP-087 WIP: …", "wip: …"), or a body line starting "Not for main" / "Test: WIP" as words — a word WIP later
# in a subject ("final WIP table"), "(not for main)" inside a sentence, "Not for maintenance" or "Test: wiped" are not.
# A ':' inside the label's own parentheses ends the label early (a known limit, never a false alarm).
WIP_SUBJ_RE='^([^:]*[^[:alnum:]:])?WIP([^[:alnum:]:][^:]*)?:'
WIP_BODY_RE='^[[:space:]]*(not for main([^[:alnum:]-]|$)|tests?:[[:space:]]*wip([^[:alnum:]]|$))'
XNOTE=""
[ "$AC_EXEMPT_SET" -eq 1 ] && XNOTE=" — acoustic gate 4c EXEMPT (--acoustic-exempt): $AC_EXEMPT"
if [ "$EXEMPT_SET" -eq 1 ]; then
  XNOTE="$XNOTE — commit-message gate EXEMPT (--subject-exempt): $EXEMPT"
  { print -r -- "$SUBJ" | grep -qiE "$WIP_SUBJ_RE" || "${G[@]}" log -1 --format=%b "$C" | grep -qiE "$WIP_BODY_RE"; } \
    && echo "WARNING: the exempted commit is WIP-labelled (review NIT-3) — $SUBJ"
else
  print -r -- "$SUBJ" | grep -qE "$SUBJ_RE" || fail "commit subject not '(WP|AP)-NNN…: … (C-00 dd.mm)': $SUBJ"
  BODY=$("${G[@]}" log -1 --format=%b "$C")
  # a WIP commit never reaches main (WIP_SUBJ_RE / WIP_BODY_RE above); --subject-exempt passes it, loudly
  print -r -- "$SUBJ" | grep -qiE "$WIP_SUBJ_RE" \
    && fail "WIP commit (subject label '…WIP…:') — WIP commits are not integrated; deliver a new, clean commit"
  print -r -- "$BODY" | grep -qiE "$WIP_BODY_RE" \
    && fail "WIP commit (a body line 'Not for main' / 'Test: WIP') — WIP commits are not integrated; deliver a new, clean commit"
  { print -r -- "$BODY" | grep -qiE '^[[:space:]]*(test|guarded)' || print -r -- "$BODY" | grep -qE 'EditMode|PlayMode'; } \
    || fail "commit body has no test line (a line starting with Test/Tests/Guarded, or naming EditMode/PlayMode)"
fi

if [ "$FAILS" -gt 0 ]; then echo "RESULT: gate failed ($FAILS) — see GATE FAIL lines above$XNOTE"; exit 3; fi
echo "RESULT: gate passed ($( [ "$FF" -eq 1 ] && echo ff-only || echo cherry-pick ), ${#FILES} paths, lfs $LP/$LO)$( [ "$APPLY" -eq 1 ] || echo ' — dry-run, add --apply')$XNOTE"
[ "$APPLY" -eq 1 ] || exit 0

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
        # WP-051C: git's own 3-way merge (diff3 markers) keeps every non-conflicting change — new lines added
        # elsewhere on either side no longer block the merge; only the conflicting hunks are resolved here,
        # each as one base line with both sides' character-level insertions (difflib) applied in offset order.
        "${G[@]}" show ":1:$f" > "/tmp/dd-int-base.$$"; "${G[@]}" show ":2:$f" > "/tmp/dd-int-ours.$$"; "${G[@]}" show ":3:$f" > "/tmp/dd-int-theirs.$$"
        git merge-file -p --diff3 -L ours -L base -L theirs "/tmp/dd-int-ours.$$" "/tmp/dd-int-base.$$" "/tmp/dd-int-theirs.$$" > "/tmp/dd-int-merged.$$" 2>/dev/null
        python3 - "/tmp/dd-int-merged.$$" "$DD_REPO/$f" <<'PY' || { echo "03-ROADMAP: automatic merge failed (a conflicting hunk is not a single-line insert-only change)"; rm -f /tmp/dd-int-*.$$; abort_pick || exit 7; exit 4; }
import sys, re, difflib
src = open(sys.argv[1], encoding='utf-8').read()
pat = re.compile(r'<<<<<<< ours\n(.*?)\n?\|\|\|\|\|\|\| base\n(.*?)\n?=======\n(.*?)\n?>>>>>>> theirs\n', re.S)
def inserts(b, v):
    sm = difflib.SequenceMatcher(None, b, v, autojunk=False); out = []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == 'equal': continue
        if tag != 'insert': raise ValueError(tag)
        out.append((i1, v[j1:j2]))
    return out
def merge_block(m):
    o, b, t = m.group(1), m.group(2), m.group(3)
    if '\n' in o or '\n' in b or '\n' in t: raise ValueError('multi-line hunk')
    ins = sorted(inserts(b, o) + inserts(b, t), key=lambda x: x[0]); merged = ''; pos = 0
    for off, text in ins: merged += b[pos:off] + text; pos = off
    return merged + b[pos:] + '\n'
try:
    out = pat.sub(merge_block, src)
except ValueError:
    sys.exit(1)
if '<<<<<<<' in out or '>>>>>>>' in out or '|||||||' in out: sys.exit(1)
open(sys.argv[2], 'w', encoding='utf-8').write(out)
PY
        rm -f "/tmp/dd-int-base.$$" "/tmp/dd-int-ours.$$" "/tmp/dd-int-theirs.$$" "/tmp/dd-int-merged.$$"; "${G[@]}" add "$f"; say "resolved $f (git 3-way + both sides' insertions on each conflicting line)" ;;
      planning/04-IMPLEMENTATION-PLAN.md|planning/00-PROJECT-PLAN.md|planning/01-REQUIREMENTS.md)
        # 04: sections → HEAD block, blank line, THEIRS block. 00/01: table rows → HEAD rows then THEIRS rows
        # (the post-merge DEC/REQ numbering check below guarantees the result is still unique and ordered).
        SEP=$'\n\n'; [ "$f" = planning/04-IMPLEMENTATION-PLAN.md ] || SEP=$'\n'
        DD_SEP="$SEP" python3 - "$DD_REPO/$f" <<'PY' || { echo "$f: automatic resolution failed"; abort_pick || exit 7; exit 4; }
import sys,re,os
p=sys.argv[1]; s=open(p,encoding='utf-8').read(); sep=os.environ.get('DD_SEP','\n\n')
pat=re.compile(r'<<<<<<< [^\n]*\n(.*?)\n?=======\n(.*?)\n?>>>>>>> [^\n]*\n', re.S)
n=[0]
def rep(m):
    n[0]+=1; a=m.group(1).rstrip('\n'); b=m.group(2).rstrip('\n'); return a+sep+b+'\n'
s2=pat.sub(rep,s)
if n[0]==0 or '<<<<<<<' in s2 or '>>>>>>>' in s2: sys.exit(1)
open(p,'w',encoding='utf-8').write(s2)
PY
        "${G[@]}" add "$f"; say "resolved $f (HEAD block then THEIRS block)" ;;
      *) echo "CONFLICT in $f — not auto-resolvable; aborting (main unchanged)"; "${G[@]}" diff --name-only --diff-filter=U; abort_pick || exit 7; exit 4 ;;
    esac
  done
  # verification: every non-planning path must carry the SAME CHANGE as the source commit. Blob identity
  # passes fast; when git 3-way-merged a file cleanly (both sides touched it) the blob differs, so the
  # source diff (+/- lines) and the staged diff against HEAD are compared hunk-for-hunk instead.
  BAD=0
  hunks() { grep -E '^[+-]' | grep -vE '^(\+\+\+|---) '; }
  for f in "${FILES[@]}"; do
    [[ "$f" == planning/* ]] && continue
    S=$("${G[@]}" rev-parse -q --verify "$C:$f" 2>/dev/null || echo deleted); I=$("${G[@]}" rev-parse -q --verify ":$f" 2>/dev/null || echo deleted)
    [ "$S" = "$I" ] && continue
    A=$("${G[@]}" diff "$C^" "$C" -- "$f" | hunks); B=$("${G[@]}" diff --cached HEAD -- "$f" | hunks)
    if [ "$A" = "$B" ]; then say "verified by hunks (3-way merged): $f"; else echo "VERIFY FAIL: $f (blob $S vs $I; hunks differ)"; BAD=1; fi
  done
  [ "$BAD" -eq 0 ] || { abort_pick || exit 7; exit 4; }
  # the merged planning tree must still number DEC/REQ uniquely and in order
  T1="${TMPDIR:-/tmp}/dd-int-$$-idx"; mkdir -p "$T1"
  "${G[@]}" show ":planning/00-PROJECT-PLAN.md" > "$T1/00.md" 2>/dev/null; "${G[@]}" show ":planning/01-REQUIREMENTS.md" > "$T1/01.md" 2>/dev/null
  P=$(check_ids "$T1/00.md" "$T1/01.md") || { echo "DEC/REQ numbering after merge: $P"; rm -rf "$T1"; abort_pick || exit 7; exit 4; }
  rm -rf "$T1"
  # gate 4c on the merged index (gt SHOULD-2b): git can merge a json the source and main both changed into a pair neither had
  if [ "$AC_CHECK" -eq 1 ]; then
    P=$(python3 "$AC_TOOL" index "$DD_REPO") || { echo "gate 4c after merge: $P"; abort_pick || exit 7; exit 4; }
  fi
  "${G[@]}" commit -q -C "$C" || { echo "commit failed"; abort_pick || exit 7; exit 4; }
fi
NEW=$("${G[@]}" rev-parse main)
"${G[@]}" tag -a "$TAG" -m "${MSG:-$SUBJ}" "$NEW"
echo "integrated: main=$NEW ($( [ "$FF" -eq 1 ] && echo ff-only || echo cherry-pick )) tag=$TAG tags=$("${G[@]}" tag | wc -l | tr -d ' ')"
if [ "$NOPOST" -eq 0 ] && [ -x "$DD_TOOLS_DIR/post-integrate.sh" ] && { [ "$DD_REPO" = "${DD_TOOLS_DIR:h}" ] || [ "$REPO_DEFAULTED" -eq 1 ]; }; then
  PI=$(DD_REPO="$DD_REPO" "$DD_TOOLS_DIR/post-integrate.sh" "$TAG" 2>&1); PX=$?
  if [ "$VERBOSE" -eq 1 ]; then echo "$PI" | tail -6; else echo "post-integrate: $(echo "$PI" | grep -E '^(backup exit=|lfs pointers|OK  report=|.*NOT VERIFIED|.*CHECK FAILED)' | tr '\n' ' ' | cut -c1-160)"; fi
  [ "$PX" -eq 0 ] || { echo "post-integrate failed (exit $PX) — run with --verbose"; exit 5; }
fi
# WP-043C: optional compile gate on the new main HEAD (end of a batch).
if [ "$COMPILE" -eq 1 ]; then
  CC=$("$DD_TOOLS_DIR/compile-check.sh" "$(git -C "$DD_REPO" rev-parse HEAD)" 2>&1); CX=$?
  echo "$CC" | grep -E '^RESULT:|error CS' | head -6
  [ "$CX" -eq 0 ] || { echo "compile gate: main HEAD does not compile / check not run (exit $CX) — revert or hotfix before the next batch"; exit 6; }
fi
exit 0
