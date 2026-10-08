#!/bin/zsh
# WP-025 · shared environment for the tools/ scripts. Sourced, not executed.
# Resolves the repository root from the script location so every worktree is self-contained.
DD_TOOLS_DIR="${0:A:h}"
: "${DD_REPO:=${DD_TOOLS_DIR:h}}"
DD_PROJECT="$DD_REPO/prototype/DeathsDemolition_PlayableConcept"
: "${DD_UNITY:=/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}"
# WP-089 (C-00 05.10, iCloud move A): the KADRO root relative to HOME — the ONE place it is spelled for the shell tools.
# A constant, never taken from the environment (DD_GUARD_CANON derives from it). Move day: only this value changes
# (moved 06.10: Desktop/KADRO → Dev/KADRO, outside iCloud), together with tools/dd_paths.py KADRO_REL and slot.sh's fallback
# (test_dd_kadro pins all three and keeps Desktop/ and Documents/ out of the tools' code).
DD_KADRO_REL="Dev/KADRO"
DD_KADRO="$HOME/$DD_KADRO_REL"; DD_KPROJ="$DD_KADRO/context/projects/deaths-demolition"
: "${DD_BUILD_ROOT:=$DD_KPROJ/builds}"
: "${DD_MAX_UNITY:=2}"          # MAKİNE KURALI: at most two Unity batch processes on this machine
: "${DD_WAIT_SECONDS:=3600}"    # how long a script waits for a free slot before giving up (exit 75)
DD_APP_NAME="DeathsDemolition.app"
DD_WIN_DIR="DeathsDemolition-win64"; DD_WIN_EXE="DeathsDemolition.exe"   # WP-066W (DEC-089) Windows x64 output
DD_BUNDLE_ID="com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
DD_PRODUCT="Deaths Demolition - Playable Concept"

# G4 D4 (C-00 01.10): the ONE user-data guard every guarded caller runs (main-ci, rc.sh, tools/qa wp078_*): the CI driver
# worktree's copy, which C-00 keeps detached at main (main-ci M-12 refuses any other driver). A fixed path, not an env knob
# (a worktree's own, possibly older, guard is never used). dd_guard_canon_check: DD_GUARD_ROOT set (guard self-test knob)
# or the canonical guard missing → one reason line on stderr and 64, before the caller takes anything. WP-089 (C-00 05.10):
# then tools/guard_freshness.py — the driver's userdata-guard.sh / userdata_attrib.py ≠ main's → 64 as well (dd-env.sh /
# slot.sh differences and a driver that is not a git checkout only warn).
DD_GUARD_CANON="$DD_KPROJ/wp-079/driver/tools/userdata-guard.sh"
dd_guard_canon_check() {
  [ -z "${DD_GUARD_ROOT:-}" ] || { echo "guard: DD_GUARD_ROOT is set (guard self-test only) — refused, nothing taken (64)" >&2; return 64; }
  [ -f "$DD_GUARD_CANON" ] && [ -x "$DD_GUARD_CANON" ] || { echo "guard: canonical guard $DD_GUARD_CANON missing or not executable — refused, nothing taken (64)" >&2; return 64; }
  [ -f "$DD_TOOLS_DIR/guard_freshness.py" ] || return 0
  python3 "$DD_TOOLS_DIR/guard_freshness.py" "$DD_GUARD_CANON" || return 64
}

dd_utc() { date -u +%Y%m%dT%H%M%SZ; }
dd_log() { echo "$(date -u +%FT%TZ) $*"; [ -n "$DD_TIMELINE" ] && echo "$(date -u +%FT%TZ) $*" >> "$DD_TIMELINE"; }
dd_unity_count() { pgrep -f "Unity.app/Contents/MacOS/Unity.* -batchmode" 2>/dev/null | wc -l | tr -d ' '; }
# dd_disk_gate [path] (C-00 30.09 · machine rule §10): the one resource gate before any Unity or Player launch — free disk on
# <path>'s volume (default the repo) ≥ 15 GiB and used swap ≤ 12 GiB (≤ 16 GiB while memory_pressure shows ≥ 25 % free —
# WP-099, C-00 08.10); else one reason line and 75 (busy — nothing started).
# Unreadable df / sysctl counts as not ready. compile-check, test.sh, build.sh and player-run.sh call it (single source).
# C-00 06.10: used swap ≥ DD_SWAP_WARN_MB (10 GiB) is an early WARNING line (timeline) — nothing is stopped below 12 GiB.
# WP-099 (C-00 08.10): used swap alone stopped every launch while live memory was plentiful (swap moves up and down within
# minutes; free memory swings 32–64 %). Above 12 GiB the gate also reads `memory_pressure -Q` (system-wide free %): it
# stops only when free < DD_MEM_FREE_MIN_PCT (25 %) or unreadable; above DD_SWAP_HARD_MB (16 GiB) it always stops.
# Between 12 and 16 GiB with enough free memory: a WARNING line, started. Every stop/warning line carries both values.
DD_DISK_MIN_KIB=15728640; DD_SWAP_MAX_MB=12288; DD_SWAP_WARN_MB=10240; DD_SWAP_HARD_MB=16384; DD_MEM_FREE_MIN_PCT=25
dd_disk_gate() {
  local free swap frac cents memfree
  free=$(df -k "${1:-$DD_REPO}" 2>/dev/null | awk 'NR==2{print $4}')
  # C-00 06.10 (N-8): the decimals count — "used = 12288.50M" was read as 12288 and passed the 12 GiB stop. The value is
  # compared in hundredths of a MB (the stop: > 12 GiB, the warning: ≥ 10 GiB) and shown as sysctl gave it.
  swap=$(sysctl -n vm.swapusage 2>/dev/null | sed -n 's/.*used = \([0-9][0-9]*\(\.[0-9]*\)\{0,1\}\)M.*/\1/p')
  frac=${${swap#*.}[1,2]}; [[ "$swap" == *.* ]] || frac=""; while (( ${#frac} < 2 )); do frac+=0; done
  [[ "${swap%%.*}" == <-> && "$frac" == <-> ]] && cents=$(( ${swap%%.*} * 100 + frac ))
  if [[ "$free" != <-> ]] || [ "$free" -lt "$DD_DISK_MIN_KIB" ]; then
    dd_log "disk-gate: free disk ${free:-unknown} KiB < $DD_DISK_MIN_KIB (15 GiB) on ${1:-$DD_REPO} — not started (75)"; return 75; fi
  # below 12 GiB memory_pressure is never asked; every line above it (and an unreadable swap) carries both values
  [[ -z "$cents" ]] || (( cents > DD_SWAP_MAX_MB * 100 )) \
    && memfree=$(memory_pressure -Q 2>/dev/null | sed -n 's/.*free percentage: *\([0-9][0-9]*\)%.*/\1/p' | head -1)
  if [[ -z "$cents" ]]; then
    dd_log "disk-gate: used swap unknown, memory free ${memfree:-unknown}% — not started (75)"; return 75; fi
  if (( cents > DD_SWAP_HARD_MB * 100 )); then
    dd_log "disk-gate: used swap $swap MB > $DD_SWAP_HARD_MB (16 GiB), memory free ${memfree:-unknown}% — not started (75)"; return 75; fi
  if (( cents > DD_SWAP_MAX_MB * 100 )); then
    if [[ "$memfree" != <-> ]] || (( memfree < DD_MEM_FREE_MIN_PCT )); then
      dd_log "disk-gate: used swap $swap MB > $DD_SWAP_MAX_MB (12 GiB) and memory free ${memfree:-unknown}% < $DD_MEM_FREE_MIN_PCT% — not started (75)"; return 75; fi
    dd_log "WARNING disk-gate: used swap $swap MB > $DD_SWAP_MAX_MB (12 GiB) but memory free $memfree% ≥ $DD_MEM_FREE_MIN_PCT% — swap used above 12 GiB, live memory free ≥ 25 %; started anyway"
  elif (( cents >= DD_SWAP_WARN_MB * 100 )); then
    dd_log "WARNING disk-gate: used swap $swap MB ≥ $DD_SWAP_WARN_MB (10 GiB) — near the 12 GiB stop; started anyway"
  fi
  dd_log "disk-gate: ok freeKiB=$free swapUsedMB=$swap${memfree:+ memFreePct=$memfree}"; return 0
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
# dd_crash_sweep <project> <dest> <pid> <epoch>: the Editor switches its cwd to the project folder, so a mono crash's
# mono_crash.* dumps land THERE (gt 07.10: all 26 of the R2 FINAL's H2 dumps were prototype/…/mono_crash.mem.36926.*) —
# move this attempt's ones (the pid in the name, or written since the attempt began: one Unity per project at a time)
# next to the run's log. Never fails the run.
dd_crash_sweep() {
  local proj="$1" dest="$2" pid="$3" t0="$4" f n=0
  setopt localoptions nullglob
  [ -n "$proj" ] && [ -d "$proj" ] || return 0
  for f in "$proj"/mono_crash.*; do
    if [[ "${f:t}" == *".$pid."* ]] || [ "$(stat -f %m "$f" 2>/dev/null || echo 0)" -ge "$t0" ]; then
      mkdir -p "$dest" 2>/dev/null && mv "$f" "$dest/" 2>/dev/null && n=$((n + 1))
    fi
  done
  [ "$n" -gt 0 ] && dd_log "unity: $n mono_crash dump(s) of pid $pid moved from $proj to $dest"
  return 0
}

dd_unity_watched() {
  local log="$1" limit="$2"; shift 2
  local stall=${DD_STALL_SEC:-$(( ${DD_STALL_MIN:-20} * 60 ))} tick=${DD_STALL_TICK:-15} attempt=1 upid size last quiet t0
  DD_UNITY_OUTCOME=ok
  # C-00 06.10 (R3; the FINAL's z3 stop): Unity starts in its log's folder when the log path is absolute, so anything it
  # writes into its cwd before the Editor switches into the project lands there (`exec`: $! = Unity's own pid; a relative
  # log keeps the caller's cwd). The Editor's own mono_crash dumps land in the PROJECT folder (gt 07.10) — dd_crash_sweep
  # moves them next to the log after every attempt.
  local run_dir=""; [[ "$log" == /* ]] && [ -d "${log:h}" ] && run_dir="${log:h}"
  local -a cmd; cmd=("$@")
  local proj="" i
  for (( i = 1; i < ${#cmd}; i++ )); do
    if [ "${cmd[i]}" = -projectPath ]; then
      # gt 07.10 SHOULD: the cd below moves every relative path — -projectPath (a relative DD_REPO) and the executable (a
      # relative DD_UNITY) are made absolute against the caller's cwd first (:a — no symlink resolution)
      [ -n "$run_dir" ] && [[ "${cmd[i+1]}" != /* ]] && cmd[i+1]=${cmd[i+1]:a}
      proj="${cmd[i+1]}"
    fi
  done
  [ -n "$run_dir" ] && [[ "${cmd[1]}" == */* && "${cmd[1]}" != /* ]] && cmd[1]=${cmd[1]:a}
  local crash="${log:h}/mono-crash"
  while :; do
    if [ -n "$run_dir" ]; then ( cd "$run_dir" && exec "${cmd[@]}" ) >/dev/null 2>&1 &
    else "${cmd[@]}" >/dev/null 2>&1 &
    fi
    upid=$!; DD_UNITY_PID=$upid; t0=$(date +%s); last=-1; quiet=0
    while kill -0 $upid 2>/dev/null; do
      sleep $tick </dev/null >/dev/null 2>&1 & wait $!
      size=$(stat -f %z "$log" 2>/dev/null || echo 0)
      if [ "$size" != "$last" ]; then last=$size; quiet=0; else quiet=$((quiet + tick)); fi
      if [ "$limit" -gt 0 ] && [ $(( $(date +%s) - t0 )) -ge $(( limit * 60 )) ]; then
        dd_log "WATCHDOG: ${limit} min passed — killing Unity pid $upid"; dd_kill_tree $upid; wait $upid 2>/dev/null; DD_UNITY_PID=""
        dd_crash_sweep "$proj" "$crash" "$upid" "$t0"
        DD_UNITY_OUTCOME=killed; return 124
      fi
      if [ "$quiet" -ge "$stall" ]; then
        dd_log "WATCHDOG stall: Unity pid $upid wrote nothing to its log for ${quiet}s (attempt $attempt) — stopping that pid only"
        dd_kill_tree $upid; wait $upid 2>/dev/null; DD_UNITY_PID=""
        dd_crash_sweep "$proj" "$crash" "$upid" "$t0"
        [ -f "$log" ] && mv "$log" "$log.stall$attempt"
        if [ "$attempt" -ge 2 ]; then dd_log "WATCHDOG stall: the retry stalled too — giving up (exit 124)"; DD_UNITY_OUTCOME=stalled; return 124; fi
        attempt=$((attempt + 1)); dd_log "WATCHDOG stall: retrying once"
        continue 2
      fi
    done
    wait $upid; local x=$?; DD_UNITY_PID=""
    dd_crash_sweep "$proj" "$crash" "$upid" "$t0"
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

# Codex-audit M-10 (C-00 30.09): self-updating env scrub before main-ci's tests, rc.sh and a release / release-probe build.
# No fixed list: the names are every GetEnvironmentVariable("NAME") literal in the C# of the tree under test, so a new env
# read in C# widens the scrub by itself (e.g. DD_BUILD_EXTRA_SCENES, which can add scenes to a release build). Diagnostic
# runs that need such a variable use tools/test.sh directly (not scrubbed).
# Allow-list — the only names kept. HOME: Unity, git and the tests need the user's home; what they write under it is the
# user-data guard's job. ANY ADDITION NEEDS A WRITTEN REASON ON THIS LINE (C-00). Assigned unconditionally: a caller's
# environment cannot widen it.
DD_ENV_SCRUB_ALLOW="HOME"
# dd_scrub_code_env <tree>: unsets (in THIS shell, so every later child) each such name that is set, except the allow-list.
# DD_ENV_SCRUBBED = the cleared names (space-separated, LC_ALL=C sorted; "" = none), DD_ENV_SCRUB_NAMES = how many names
# the C# reads; one dd_log line. 0 · 3 git grep failed or a name could not be unset (fail closed: never run unscrubbed).
DD_ENV_SCRUBBED=""; DD_ENV_SCRUB_NAMES=0
dd_scrub_code_env() {
  local tree="$1" raw rc name; local -a names cleared allow
  DD_ENV_SCRUBBED=""; DD_ENV_SCRUB_NAMES=0; allow=(${=DD_ENV_SCRUB_ALLOW})
  # Names: GetEnvironmentVariable("X") literals PLUS every "DD_…" string literal in C# — env names read through a constant
  # (DD_WORLD_SOURCE, DD_QUALITY_TIER, …) would escape the literal pattern. Over-inclusion is harmless (only SET names are
  # unset; shader keywords like DD_FX_* are never env vars; C# reads none of the tools' own DD_* variables).
  raw=$(git -C "$tree" grep -h -o -e 'GetEnvironmentVariable("[A-Z0-9_]*"' -e '"DD_[A-Z0-9_]*[A-Z0-9]"' -- '*.cs'); rc=$?
  # git grep: 0 matches · 1 no match · anything else (128 not a repository, …) is an error
  if [ "$rc" -gt 1 ]; then
    dd_log "env-scrub: git grep failed in $tree (exit $rc) — not run unscrubbed (3)" >&2; return 3; fi
  names=(${(f)"$(print -r -- "$raw" | sed -n -e 's/^.*GetEnvironmentVariable("\([A-Z_][A-Z0-9_]*\)"$/\1/p' -e 's/^"\(DD_[A-Z0-9_]*\)"$/\1/p' | LC_ALL=C sort -u)"})
  DD_ENV_SCRUB_NAMES=${#names}
  for name in $names; do
    (( ${allow[(Ie)$name]} )) && continue
    [[ -v $name ]] || continue
    unset "$name" 2>/dev/null
    if [[ -v $name ]]; then dd_log "env-scrub: cannot unset $name (read by C# in $tree) — not run unscrubbed (3)" >&2; return 3; fi
    cleared+=("$name")
  done
  DD_ENV_SCRUBBED="${(j: :)cleared}"
  if [ "${#cleared}" -gt 0 ]; then dd_log "env-scrub: cleared ${#cleared} (${(j:,:)cleared}) from $tree"
  else dd_log "env-scrub: nothing to clear ($DD_ENV_SCRUB_NAMES names read by C#)"; fi
  return 0
}
# dd_env_scrubbed_json: DD_ENV_SCRUBBED as a JSON list (names are [A-Z0-9_], no escaping needed).
dd_env_scrubbed_json() { local n out=""; for n in ${=DD_ENV_SCRUBBED}; do out+="${out:+, }\"$n\""; done; print -r -- "[$out]"; }
