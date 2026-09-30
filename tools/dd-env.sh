#!/bin/zsh
# WP-025 · shared environment for the tools/ scripts. Sourced, not executed.
# Resolves the repository root from the script location so every worktree is self-contained.
DD_TOOLS_DIR="${0:A:h}"
: "${DD_REPO:=${DD_TOOLS_DIR:h}}"
DD_PROJECT="$DD_REPO/prototype/DeathsDemolition_PlayableConcept"
: "${DD_UNITY:=/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}"
: "${DD_BUILD_ROOT:=$HOME/Desktop/KADRO/context/projects/deaths-demolition/builds}"
: "${DD_MAX_UNITY:=2}"          # MAKİNE KURALI: at most two Unity batch processes on this machine
: "${DD_WAIT_SECONDS:=3600}"    # how long a script waits for a free slot before giving up (exit 75)
DD_APP_NAME="DeathsDemolition.app"
DD_WIN_DIR="DeathsDemolition-win64"; DD_WIN_EXE="DeathsDemolition.exe"   # WP-066W (DEC-089) Windows x64 output
DD_BUNDLE_ID="com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
DD_PRODUCT="Deaths Demolition - Playable Concept"

dd_utc() { date -u +%Y%m%dT%H%M%SZ; }
dd_log() { echo "$(date -u +%FT%TZ) $*"; [ -n "$DD_TIMELINE" ] && echo "$(date -u +%FT%TZ) $*" >> "$DD_TIMELINE"; }
dd_unity_count() { pgrep -f "Unity.app/Contents/MacOS/Unity.* -batchmode" 2>/dev/null | wc -l | tr -d ' '; }
# dd_disk_gate [path] (C-00 30.09 · machine rule §10): the one resource gate before any Unity or Player launch — free disk on
# <path>'s volume (default the repo) ≥ 15 GiB and used swap ≤ 12 GiB; else one reason line and 75 (busy — nothing started).
# Unreadable df / sysctl counts as not ready. compile-check, test.sh, build.sh and player-run.sh call it (single source).
DD_DISK_MIN_KIB=15728640; DD_SWAP_MAX_MB=12288
dd_disk_gate() {
  local free swap
  free=$(df -k "${1:-$DD_REPO}" 2>/dev/null | awk 'NR==2{print $4}')
  swap=$(sysctl -n vm.swapusage 2>/dev/null | sed -n 's/.*used = \([0-9]*\)[.0-9]*M.*/\1/p')
  if [[ "$free" != <-> ]] || [ "$free" -lt "$DD_DISK_MIN_KIB" ]; then
    dd_log "disk-gate: free disk ${free:-unknown} KiB < $DD_DISK_MIN_KIB (15 GiB) on ${1:-$DD_REPO} — not started (75)"; return 75; fi
  if [[ "$swap" != <-> ]] || [ "$swap" -gt "$DD_SWAP_MAX_MB" ]; then
    dd_log "disk-gate: used swap ${swap:-unknown} MB > $DD_SWAP_MAX_MB (12 GiB) — not started (75)"; return 75; fi
  dd_log "disk-gate: ok freeKiB=$free swapUsedMB=$swap"; return 0
}
# Waits until fewer than DD_MAX_UNITY batch editors run. Returns 75 on timeout (EX_TEMPFAIL).
dd_wait_slot() {
  local waited=0
  while [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; do
    [ "$waited" -ge "$DD_WAIT_SECONDS" ] && { dd_log "busy: $(dd_unity_count) Unity batch processes for ${waited}s — giving up"; return 75; }
    [ "$waited" -eq 0 ] && dd_log "queue: $(dd_unity_count) Unity batch processes running (limit $DD_MAX_UNITY), waiting"
    sleep 30; waited=$((waited+30))
  done
  return 0
}
# Machine slot lock (C-00 rule, 2026-09-16): at most 2 Unity batch / 1 heavy Blender on this machine.
# tools/slot.sh acquire blocks until a slot is free; the lock is bound to the calling shell's pid.
DD_SLOT="$DD_TOOLS_DIR/slot.sh"; DD_SLOT_TAG="${DD_SLOT_TAG:-$(basename "$DD_REPO")-$$}"
# dd_slot_acquire [kind]  — kind: unity (default, ≤2) · blender (≤1) · smoke (≤1: a player smoke writes the real profile,
# two at once corrupt the user-data guard — slot.sh v4). Direct call: the lock is bound to THIS shell's pid.
# Codex-audit M-07 (C-00 30.09): both return slot.sh's own exit code (they used to return 0 whatever slot.sh said).
dd_slot_acquire() { [ -x "$DD_SLOT" ] || return 0; local kind="${1:-unity}" rc; dd_log "slot: acquiring $kind slot as $DD_SLOT_TAG ($(dd_unity_count) Unity batch running)"; local out="${TMPDIR:-/tmp}/dd-slot-$$.txt"; "$DD_SLOT" acquire "$kind" "$DD_SLOT_TAG" > "$out"; rc=$?; dd_log "slot: $(cat "$out")"; [ "$rc" -eq 0 ] || dd_log "slot: acquire $kind failed (slot.sh exit $rc)"; rm -f "$out"; return $rc; }
dd_slot_release() { [ -x "$DD_SLOT" ] || return 0; local kind="${1:-unity}" rc; "$DD_SLOT" release "$kind" "$DD_SLOT_TAG" >/dev/null 2>&1; rc=$?; if [ "$rc" -eq 0 ]; then dd_log "slot: released $kind $DD_SLOT_TAG"; else dd_log "slot: release $kind $DD_SLOT_TAG failed (slot.sh exit $rc)"; fi; return $rc; }
dd_sha() { [ -f "$1" ] && shasum -a 256 "$1" | cut -d' ' -f1 || echo MISSING; }
# dd_project_open <projectDir> — 0 a Unity editor has <projectDir> open · 1 none has · 2 the process list is unavailable.
# Shared by main-ci.sh and compile-check.sh (single source). Unity's projectPath is resolved too, so a launch through an
# alias stays busy. Codex-audit M-06 (C-00 30.09): the flag is matched case-insensitively (Unity Hub launches with
# `-projectpath`); a foreign path is resolved with strict=False, and one that cannot be resolved or does not exist (e.g. a
# Unity whose worktree was removed) is skipped with a stderr line instead of failing the whole check (it locked CI at 75).
dd_project_open() {
  python3 - "$1" <<'PY_PROCESS'
from pathlib import Path
import re, subprocess, sys
try:
    target = Path(sys.argv[1]).resolve(strict=False)
    rows = subprocess.check_output(["ps", "-axo", "pid=,args="], text=True).splitlines()
except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
    print("dd_project_open: process check unavailable: %s" % error, file=sys.stderr)
    sys.exit(2)
# M-12 review MUST-1: any unexpected error while reading the list is "cannot tell" (2 = busy), never "not open" (1);
# SHOULD-1: a relative -projectPath cannot be resolved against Unity's own cwd → busy too.
try:
    for row in rows:
        pid, _, command = row.strip().partition(" ")
        command = command.strip()
        executable = command.split(" -", 1)[0].strip()
        if not executable.endswith("/Unity.app/Contents/MacOS/Unity"): continue
        match = re.search(r"(?:^|\s)-projectPath\s+(.*?)(?=\s+-[A-Za-z]|$)", command, re.IGNORECASE)
        if not match: continue
        raw = match.group(1).strip()
        if len(raw) >= 2 and raw[0] == raw[-1] and raw[0] in "\"'": raw = raw[1:-1]
        if not raw.startswith("/"):
            print("dd_project_open: Unity pid %s has a relative projectPath (cannot tell which project — busy): %s" % (pid, raw), file=sys.stderr)
            sys.exit(2)
        try:
            path = Path(raw).resolve(strict=False)
        except (OSError, RuntimeError, ValueError) as error:
            print("dd_project_open: skipped a Unity projectPath that cannot be resolved (%s): %s" % (error, raw), file=sys.stderr)
            continue
        if path == target: sys.exit(0)
        if not path.exists():
            print("dd_project_open: skipped a Unity projectPath that does not exist: %s" % raw, file=sys.stderr)
except SystemExit:
    raise
except Exception as error:
    print("dd_project_open: process check failed (%s) — treating the checkout as busy" % error, file=sys.stderr)
    sys.exit(2)
sys.exit(1)
PY_PROCESS
}

# WP-051D (C-00): Unity batch runs sometimes hang for hours after the OpenCL/lightmapper init (concurrent GPU load;
# not the lighting settings). dd_unity_watched <log> <limitMin|0> <unity command…> runs the command (which must write
# -logFile <log>) in the background and watches the log: no growth for DD_STALL_MIN (default 20) minutes = stall →
# only that pid and its children are stopped, the log is kept as <log>.stallN and the run is retried ONCE; a second
# stall returns 124. <limitMin> > 0 is the old total limit per attempt (also 124). DD_UNITY_OUTCOME = ok | killed |
# stalled | stalled-retried-ok. DD_STALL_SEC / DD_STALL_TICK override the threshold / check interval (self-tests).
# Codex-audit Y1 / G4 Q10 (C-00 30.09): DD_UNITY_PID = the running Unity pid ("" otherwise) for a caller's signal cleanup;
# the poll sleeps as `sleep & wait` so a trapped INT/TERM/HUP runs at once instead of after the tick.
dd_kill_tree() { pkill -P "$1" 2>/dev/null; kill "$1" 2>/dev/null; sleep 5; kill -9 "$1" 2>/dev/null; }
# Codex-audit Y1 / G4 Q10 (C-00 30.09): dd_stop_child <pid> <graceSec> — signal-safe stop of a script's OWN child: snapshots
# <pid> + descendants (read-only pgrep -P walk), sends TERM to <pid> ONLY (no pkill/killall), waits ≤ grace until none is
# alive (zombie = gone). 0 all gone · 1 still alive, DD_CHILD_ALIVE = that pid (an orphaned Unity can still write user data).
DD_UNITY_PID=""; DD_CHILD_ALIVE=""
dd_pid_live() { local s; s=$(ps -o stat= -p "$1" 2>/dev/null) || return 1; s=${s// /}; [ -n "$s" ] && [[ "$s" != Z* ]]; }
dd_tree() { local c; print -r -- "$1"; for c in ${(f)"$(pgrep -P "$1" 2>/dev/null)"}; do dd_tree "$c"; done; }
dd_stop_child() {
  local pid=$1 grace=$2 t=0 p; local -a tree; tree=(${(f)"$(dd_tree "$pid")"})
  kill -TERM "$pid" 2>/dev/null
  while :; do
    DD_CHILD_ALIVE=""; for p in $tree; do dd_pid_live "$p" && { DD_CHILD_ALIVE=$p; break; }; done
    [ -z "$DD_CHILD_ALIVE" ] && return 0
    [ "$t" -ge "$grace" ] && return 1
    sleep 1; t=$((t + 1))
  done
}
dd_unity_watched() {
  local log="$1" limit="$2"; shift 2
  local stall=${DD_STALL_SEC:-$(( ${DD_STALL_MIN:-20} * 60 ))} tick=${DD_STALL_TICK:-15} attempt=1 upid size last quiet t0
  DD_UNITY_OUTCOME=ok
  while :; do
    "$@" >/dev/null 2>&1 &
    upid=$!; DD_UNITY_PID=$upid; t0=$(date +%s); last=-1; quiet=0
    while kill -0 $upid 2>/dev/null; do
      sleep $tick </dev/null >/dev/null 2>&1 & wait $!
      size=$(stat -f %z "$log" 2>/dev/null || echo 0)
      if [ "$size" != "$last" ]; then last=$size; quiet=0; else quiet=$((quiet + tick)); fi
      if [ "$limit" -gt 0 ] && [ $(( $(date +%s) - t0 )) -ge $(( limit * 60 )) ]; then
        dd_log "WATCHDOG: ${limit} min passed — killing Unity pid $upid"; dd_kill_tree $upid; wait $upid 2>/dev/null; DD_UNITY_PID=""
        DD_UNITY_OUTCOME=killed; return 124
      fi
      if [ "$quiet" -ge "$stall" ]; then
        dd_log "WATCHDOG stall: Unity pid $upid wrote nothing to its log for ${quiet}s (attempt $attempt) — stopping that pid only"
        dd_kill_tree $upid; wait $upid 2>/dev/null; DD_UNITY_PID=""
        [ -f "$log" ] && mv "$log" "$log.stall$attempt"
        if [ "$attempt" -ge 2 ]; then dd_log "WATCHDOG stall: the retry stalled too — giving up (exit 124)"; DD_UNITY_OUTCOME=stalled; return 124; fi
        attempt=$((attempt + 1)); dd_log "WATCHDOG stall: retrying once"
        continue 2
      fi
    done
    wait $upid; local x=$?; DD_UNITY_PID=""
    [ "$attempt" -gt 1 ] && DD_UNITY_OUTCOME=stalled-retried-ok
    return $x
  done
}

# Codex-audit M-05 (C-00 30.09): CI checkout owner lease — single source for main-ci.sh and compile-check.sh.
# Compile and CI serialize only the same PHYSICAL checkout: the key is sha256 of the canonical checkout path, the lease is
# the directory <parent>/.dd-ci-<key>.lease (mkdir = atomic claim) holding ONE file owner-<token> with pid=/start=/token=
# (written as .tmp-<token> and renamed, so a reader sees a whole owner file or none). A lease is never reclaimed or
# deleted by these tools — a stale one is diagnosed read-only (compile-check.sh --lease-status) and quarantined by C-00.
# dd_pstart <pid>: the process start in ONE fixed format for every writer and reader (slot.sh D1 lesson: plain `ps lstart`
# follows LANG/TZ — a tr_TR or TZ=UTC caller would read a live owner as "another process").
dd_pstart() { env -u TZ LC_ALL=C ps -o lstart= -p "$1" 2>/dev/null | tr -s ' ' | sed 's/^ //;s/ $//'; }
# dd_ci_canon <dir>: canonical checkout path (symlinks resolved; a missing leaf is allowed under a real parent). 3 = error.
dd_ci_canon() {
  python3 - "$1" <<'PY_CANON'
from pathlib import Path
import os, sys
try:
    requested = Path(sys.argv[1]).absolute()
    if os.path.lexists(requested):
        target = requested.resolve(strict=True)
        if not target.is_dir(): raise ValueError("checkout is not a directory")
    else:
        target = requested.parent.resolve(strict=True) / requested.name
        if not target.parent.is_dir(): raise ValueError("checkout parent is not a directory")
    if "\n" in str(target): raise ValueError("newline in checkout path")
    print(target)
except (OSError, RuntimeError, ValueError) as error:
    print("cannot resolve CI checkout: " + str(error), file=sys.stderr)
    sys.exit(3)
PY_CANON
}
# dd_ci_lease_locate <dir>: DD_CI_CHECKOUT (canonical) + DD_CI_LEASE (its lease path). 0 · 3 error. Read-only.
dd_ci_lease_locate() {
  DD_CI_CHECKOUT=$(dd_ci_canon "$1") && [ -n "$DD_CI_CHECKOUT" ] || return 3
  local key=$(print -rn -- "$DD_CI_CHECKOUT" | shasum -a 256 | cut -d' ' -f1)
  [ "${#key}" -eq 64 ] || return 3
  DD_CI_LEASE="${DD_CI_CHECKOUT:h}/.dd-ci-$key.lease"
}
# dd_ci_lease_inspect: reads $DD_CI_LEASE (never writes) → DD_CI_LEASE_VERDICT (FREE · HELD-LIVE · STALE-DEAD-OWNER ·
# STALE-PID-REUSED · MALFORMED · SYMLINK), DD_CI_LEASE_WHY and the owner fields DD_CI_LEASE_{FILE,PID,START,NOW,ALIVE}.
# K3 (slot.sh): an alive pid whose current start cannot be read is HELD-LIVE, never stale. A record written before M-05
# (plain `ps lstart` in the writer's locale/TZ) that matches the caller's own plain form also counts as the same process.
dd_ci_lease_inspect() {
  local -a owners others; local f name body
  DD_CI_LEASE_VERDICT=""; DD_CI_LEASE_WHY=""; DD_CI_LEASE_FILE=""; DD_CI_LEASE_PID=""; DD_CI_LEASE_START=""
  DD_CI_LEASE_NOW=""; DD_CI_LEASE_ALIVE=""
  if [ -L "$DD_CI_LEASE" ]; then DD_CI_LEASE_VERDICT=SYMLINK; DD_CI_LEASE_WHY="the lease path is a symlink (-> $(readlink "$DD_CI_LEASE"))"; return; fi
  if [ ! -e "$DD_CI_LEASE" ]; then DD_CI_LEASE_VERDICT=FREE; DD_CI_LEASE_WHY="no lease"; return; fi
  if [ ! -d "$DD_CI_LEASE" ]; then DD_CI_LEASE_VERDICT=MALFORMED; DD_CI_LEASE_WHY="the lease path is not a directory"; return; fi
  for f in "$DD_CI_LEASE"/*(DN) ; do
    name=${f:t}
    if [[ "$name" == owner-* ]]; then owners+=("$f"); else others+=("$name"); fi
  done
  if [ "${#owners}" -eq 0 ]; then
    DD_CI_LEASE_VERDICT=MALFORMED
    DD_CI_LEASE_WHY="no owner file (${#others} other entries) — an acquire in progress, or its owner died between mkdir and write: run again"
    return
  fi
  if [ "${#owners}" -gt 1 ] || [ "${#others}" -gt 0 ]; then
    DD_CI_LEASE_VERDICT=MALFORMED; DD_CI_LEASE_WHY="${#owners} owner files and ${#others} other entries (expected exactly one owner file)"; return
  fi
  DD_CI_LEASE_FILE=${owners[1]}
  if [ -L "$DD_CI_LEASE_FILE" ] || [ ! -f "$DD_CI_LEASE_FILE" ]; then
    DD_CI_LEASE_VERDICT=MALFORMED; DD_CI_LEASE_WHY="the owner entry is not a regular file"; return
  fi
  body=$(cat -- "$DD_CI_LEASE_FILE" 2>/dev/null) || { DD_CI_LEASE_VERDICT=MALFORMED; DD_CI_LEASE_WHY="the owner file cannot be read"; return; }
  local -a lines; lines=("${(@f)body}")
  DD_CI_LEASE_PID=${${lines[1]:-}#pid=}; DD_CI_LEASE_START=${${lines[2]:-}#start=}
  local token=${${lines[3]:-}#token=}
  if [ "${#lines}" -ne 3 ] || [[ "${lines[1]}" != pid=* ]] || [[ "${lines[2]}" != start=* ]] || [[ "${lines[3]}" != token=* ]] ||
     [[ "$DD_CI_LEASE_PID" != <1-> ]] || [ -z "$DD_CI_LEASE_START" ] || [ "owner-$token" != "${DD_CI_LEASE_FILE:t}" ]; then
    DD_CI_LEASE_VERDICT=MALFORMED; DD_CI_LEASE_WHY="the owner file is not pid=<n> / start=<lstart> / token=<file token>"; return
  fi
  if ! dd_pid_live "$DD_CI_LEASE_PID"; then
    DD_CI_LEASE_ALIVE=no; DD_CI_LEASE_VERDICT=STALE-DEAD-OWNER; DD_CI_LEASE_WHY="owner pid $DD_CI_LEASE_PID is not running"; return
  fi
  DD_CI_LEASE_ALIVE=yes; DD_CI_LEASE_NOW=$(dd_pstart "$DD_CI_LEASE_PID")
  local legacy=$(ps -o lstart= -p "$DD_CI_LEASE_PID" 2>/dev/null | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
  if [ -z "$DD_CI_LEASE_NOW" ]; then
    DD_CI_LEASE_VERDICT=HELD-LIVE; DD_CI_LEASE_WHY="owner pid alive, its start cannot be read now — treated as the owner (K3)"
  elif [ "$DD_CI_LEASE_START" = "$DD_CI_LEASE_NOW" ] || [ "$DD_CI_LEASE_START" = "$legacy" ]; then
    DD_CI_LEASE_VERDICT=HELD-LIVE; DD_CI_LEASE_WHY="owner pid alive and started at the recorded time"
  else
    DD_CI_LEASE_VERDICT=STALE-PID-REUSED; DD_CI_LEASE_WHY="pid $DD_CI_LEASE_PID now belongs to a process started at another time"
  fi
}
# dd_ci_lease_acquire <dir>: claims the lease of <dir>'s canonical checkout (sets DD_CI_CHECKOUT — callers use it as their
# checkout path from here on). 0 held · 75 busy (a live or not-yet-written owner) · 3 error, symlink, malformed or stale
# lease (never reclaimed: `compile-check.sh --lease-status` + C-00). Release with dd_ci_lease_release (EXIT cleanup).
DD_CI_LEASE=""; DD_CI_LEASE_HELD=0; DD_CI_LEASE_READY=0; DD_CI_LEASE_TOKEN=""; DD_CI_LEASE_OWNER=""
dd_ci_lease_acquire() {
  dd_ci_lease_locate "$1" || return 3
  DD_CI_LEASE_TOKEN=$(python3 -c 'import uuid; print(uuid.uuid4())') && [ -n "$DD_CI_LEASE_TOKEN" ] || return 3
  local started=$(dd_pstart $$)
  [ -n "$started" ] || { echo "CI checkout lease: cannot read this process's start time" >&2; return 3; }
  DD_CI_LEASE_OWNER="pid=$$"$'\n'"start=$started"$'\n'"token=$DD_CI_LEASE_TOKEN"
  if ! mkdir -m 700 -- "$DD_CI_LEASE" 2>/dev/null; then
    dd_ci_lease_inspect
    case "$DD_CI_LEASE_VERDICT" in
      HELD-LIVE) echo "CI checkout lease busy: $DD_CI_CHECKOUT (owner pid $DD_CI_LEASE_PID)" >&2; return 75 ;;
      MALFORMED) if [ -z "$DD_CI_LEASE_FILE" ] && [ -d "$DD_CI_LEASE" ] && [ ! -L "$DD_CI_LEASE" ]; then
                   echo "CI checkout lease busy: $DD_CI_CHECKOUT (no owner file yet: $DD_CI_LEASE_WHY)" >&2; return 75; fi ;;
    esac
    echo "CI checkout lease refused: $DD_CI_LEASE is $DD_CI_LEASE_VERDICT — $DD_CI_LEASE_WHY; never reclaimed: tools/compile-check.sh --lease-status, then C-00 (tools/README.md)" >&2
    return 3
  fi
  DD_CI_LEASE_HELD=1
  print -r -- "$DD_CI_LEASE_OWNER" > "$DD_CI_LEASE/.tmp-$DD_CI_LEASE_TOKEN" &&
    mv -- "$DD_CI_LEASE/.tmp-$DD_CI_LEASE_TOKEN" "$DD_CI_LEASE/owner-$DD_CI_LEASE_TOKEN" || return 3
  DD_CI_LEASE_READY=1
}
# dd_ci_lease_release: removes only OUR lease (the owner file must still be ours, the lease not a symlink). 0 · 3.
dd_ci_lease_release() {
  [ "$DD_CI_LEASE_HELD" -eq 1 ] || return 0
  [ -d "$DD_CI_LEASE" ] && [ ! -L "$DD_CI_LEASE" ] || return 3
  if [ "$DD_CI_LEASE_READY" -eq 1 ]; then
    [ -f "$DD_CI_LEASE/owner-$DD_CI_LEASE_TOKEN" ] &&
      [ "$(cat "$DD_CI_LEASE/owner-$DD_CI_LEASE_TOKEN")" = "$DD_CI_LEASE_OWNER" ] || return 3
  fi
  rm -f -- "$DD_CI_LEASE/owner-$DD_CI_LEASE_TOKEN" "$DD_CI_LEASE/.tmp-$DD_CI_LEASE_TOKEN" || return 3
  rmdir -- "$DD_CI_LEASE" || return 3
  DD_CI_LEASE_HELD=0
}
# dd_ci_lease_status <dir>: read-only report (compile-check.sh --lease-status). 0 FREE · 75 HELD-LIVE · 3 stale/malformed/
# symlink/error — deletes nothing; a stale lease is C-00's to quarantine (tools/README.md).
dd_ci_lease_status() {
  dd_ci_lease_locate "$1" || { print -r -- "verdict: ERROR — cannot resolve the checkout $1"; return 3; }
  dd_ci_lease_inspect
  print -r -- "checkout: $DD_CI_CHECKOUT"
  print -r -- "lease: $DD_CI_LEASE"
  print -r -- "owner file: ${DD_CI_LEASE_FILE:-(none)}"
  print -r -- "owner pid: ${DD_CI_LEASE_PID:-(none)}"
  print -r -- "recorded start: ${DD_CI_LEASE_START:-(none)}"
  print -r -- "current start: ${DD_CI_LEASE_NOW:-(none)}   (env -u TZ LC_ALL=C ps -o lstart=)"
  print -r -- "owner alive: ${DD_CI_LEASE_ALIVE:-(n/a)}"
  local same="(n/a)"; [ "$DD_CI_LEASE_VERDICT" = HELD-LIVE ] && same=yes; [ "$DD_CI_LEASE_VERDICT" = STALE-PID-REUSED ] && same=no
  print -r -- "same process: $same"
  print -r -- "verdict: $DD_CI_LEASE_VERDICT — $DD_CI_LEASE_WHY"
  case "$DD_CI_LEASE_VERDICT" in FREE) return 0 ;; HELD-LIVE) return 75 ;; *) return 3 ;; esac
}
