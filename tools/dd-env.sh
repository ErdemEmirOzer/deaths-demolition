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
dd_slot_acquire() { [ -x "$DD_SLOT" ] || return 0; local kind="${1:-unity}"; dd_log "slot: acquiring $kind slot as $DD_SLOT_TAG ($(dd_unity_count) Unity batch running)"; local out="${TMPDIR:-/tmp}/dd-slot-$$.txt"; "$DD_SLOT" acquire "$kind" "$DD_SLOT_TAG" > "$out"; dd_log "slot: $(cat "$out")"; rm -f "$out"; }
dd_slot_release() { [ -x "$DD_SLOT" ] || return 0; local kind="${1:-unity}"; "$DD_SLOT" release "$kind" "$DD_SLOT_TAG" >/dev/null 2>&1; dd_log "slot: released $kind $DD_SLOT_TAG"; }
dd_sha() { [ -f "$1" ] && shasum -a 256 "$1" | cut -d' ' -f1 || echo MISSING; }

# WP-051D (C-00): Unity batch runs sometimes hang for hours after the OpenCL/lightmapper init (concurrent GPU load;
# not the lighting settings). dd_unity_watched <log> <limitMin|0> <unity command…> runs the command (which must write
# -logFile <log>) in the background and watches the log: no growth for DD_STALL_MIN (default 20) minutes = stall →
# only that pid and its children are stopped, the log is kept as <log>.stallN and the run is retried ONCE; a second
# stall returns 124. <limitMin> > 0 is the old total limit per attempt (also 124). DD_UNITY_OUTCOME = ok | killed |
# stalled | stalled-retried-ok. DD_STALL_SEC / DD_STALL_TICK override the threshold / check interval (self-tests).
dd_kill_tree() { pkill -P "$1" 2>/dev/null; kill "$1" 2>/dev/null; sleep 5; kill -9 "$1" 2>/dev/null; }
dd_unity_watched() {
  local log="$1" limit="$2"; shift 2
  local stall=${DD_STALL_SEC:-$(( ${DD_STALL_MIN:-20} * 60 ))} tick=${DD_STALL_TICK:-15} attempt=1 upid size last quiet t0
  DD_UNITY_OUTCOME=ok
  while :; do
    "$@" >/dev/null 2>&1 &
    upid=$!; t0=$(date +%s); last=-1; quiet=0
    while kill -0 $upid 2>/dev/null; do
      sleep $tick
      size=$(stat -f %z "$log" 2>/dev/null || echo 0)
      if [ "$size" != "$last" ]; then last=$size; quiet=0; else quiet=$((quiet + tick)); fi
      if [ "$limit" -gt 0 ] && [ $(( $(date +%s) - t0 )) -ge $(( limit * 60 )) ]; then
        dd_log "WATCHDOG: ${limit} min passed — killing Unity pid $upid"; dd_kill_tree $upid; wait $upid 2>/dev/null
        DD_UNITY_OUTCOME=killed; return 124
      fi
      if [ "$quiet" -ge "$stall" ]; then
        dd_log "WATCHDOG stall: Unity pid $upid wrote nothing to its log for ${quiet}s (attempt $attempt) — stopping that pid only"
        dd_kill_tree $upid; wait $upid 2>/dev/null
        [ -f "$log" ] && mv "$log" "$log.stall$attempt"
        if [ "$attempt" -ge 2 ]; then dd_log "WATCHDOG stall: the retry stalled too — giving up (exit 124)"; DD_UNITY_OUTCOME=stalled; return 124; fi
        attempt=$((attempt + 1)); dd_log "WATCHDOG stall: retrying once"
        continue 2
      fi
    done
    wait $upid; local x=$?
    [ "$attempt" -gt 1 ] && DD_UNITY_OUTCOME=stalled-retried-ok
    return $x
  done
}
