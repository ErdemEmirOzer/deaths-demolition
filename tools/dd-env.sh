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
