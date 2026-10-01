#!/usr/bin/env python3
"""WP-078 (C-00 30.09 M-14, M-17) · the one user-data guard receipt module for the tools/qa Player runners.

Every Player run of wp078_perf / wp078_bot goes: snapshot → run → (finally) restore, with one verdict:
  * the guard is the canonical CI-driver copy (G4 D4, C-00 01.10): GUARD = ~/Desktop/KADRO/…/wp-079/driver/tools/
    userdata-guard.sh (dd-env.sh DD_GUARD_CANON), a fixed path — no env override; canon_refusal() names why a caller must
    stop with 64 (DD_GUARD_ROOT set, or GUARD missing) before it takes anything;
  * restore runs with INT/TERM/HUP ignored in the guard child (G4 D2 MUST-1: a Ctrl-C or a group signal cannot cut the
    guard between its delete and its import);
  * snapshot exit codes: 0 ok · 30 a live DD Player (wait) · 75 a lock held (wait) · anything else → 7 STOP;
  * restore always runs (try/finally in the callers; SIGTERM becomes SystemExit 143 so finally runs too);
    restore 75 (lock held) is retried a bounded number of times, then 7 and "VERİ GERİ YÜKLENMEDİ" is written;
    any other restore error → 7;
  * verdict (C-00 25.09): verified=1, no out-of-scope change, and touched = 0 — or touched = 1 where the one changed
    file is the player PlayerPrefs plist and every changed key is Unity's own window state (Screenmanager* /
    UnitySelectMonitor). touched ≥ 1 with no such key change means another file changed → STOP. This is the only
    copy of the rule (the KADRO player-run.sh / prefs-diff.py copy is retired).
"""
import os
import plistlib
import re
import signal
import subprocess
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
GUARD = str(Path.home() / "Desktop/KADRO/context/projects/deaths-demolition/wp-079/driver/tools/userdata-guard.sh")
PLAYER_PREFS_DOMAIN = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
ENGINE_KEY_PREFIX = "Screenmanager"
ENGINE_KEYS_EXACT = ("UnitySelectMonitor",)
IGNORED_KEYS = ("unity.player_session_count", "unity.player_sessionid")
STOP = 7
NOT_RESTORED = 65   # G4 D2 review MUST-1: never a wait code (6/30/75 are retried) — STOP, C-00 resolves
MARKER = "userdata-NOT-RESTORED.txt"
MARKER_RESOLVED = "userdata-NOT-RESTORED.resolved"   # written by C-00 only
SIGNALS = (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)


def guard_path():
    """G4 D4: the one canonical guard (the CI driver's copy), never a worktree or main-checkout copy."""
    return GUARD


def canon_refusal():
    """None when the canonical guard may run; otherwise the reason the caller stops with 64 (nothing taken yet)."""
    if os.environ.get("DD_GUARD_ROOT"):
        return "DD_GUARD_ROOT is set (guard self-test only)"
    if not (os.path.isfile(GUARD) and os.access(GUARD, os.X_OK)):
        return f"canonical guard {GUARD} missing or not executable"
    return None


def _ignore_signals():
    """preexec for the restore child: SIG_IGN survives exec, so the guard (and its children) cannot be interrupted — as
    long as the guard sets no INT/TERM/HUP trap of its own (zsh, unlike POSIX sh, runs a trap for a signal ignored at
    entry; ff06 traps only EXIT — keep it that way, G4 D5)."""
    for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(signum, signal.SIG_IGN)


class DeferredSignals:
    """G4 D2 (MUST-1): inside the block INT/TERM/HUP are recorded, not acted on — a guard restore, the Player wait or a
    child's TERM → KILL cannot be cut short (subprocess.run answers an exception with SIGKILL to its child). reraise()
    afterwards hands the first recorded signal to the handler that was in place before the block (the caller's own Python
    handler, e.g. wp078_accept's on_sigterm, or Python's KeyboardInterrupt), or — for a default handler — raises
    SystemExit(128+n). A signal ignored before the block (nohup, a background job) stays ignored. A nested block records
    nothing itself (the outermost one does); outside the main thread no handler can be set and nothing is deferred."""
    _active = None

    def __init__(self):
        self.pending, self._owner, self._saved = None, False, {}

    def __enter__(self):
        if DeferredSignals._active is None and threading.current_thread() is threading.main_thread():
            self._owner = True
            self._saved = {signum: signal.getsignal(signum) for signum in SIGNALS}
            for signum in SIGNALS:
                if self._saved[signum] != signal.SIG_IGN:   # review SHOULD-4: nohup / `&` keep their SIG_IGN
                    signal.signal(signum, self._record)
            DeferredSignals._active = self
        return self

    def _record(self, signum, _frame):
        if self.pending is None:
            self.pending = signum

    def __exit__(self, *_exc):
        if self._owner:
            for signum, handler in self._saved.items():
                signal.signal(signum, handler if handler is not None else signal.SIG_DFL)
            DeferredSignals._active = None
        return False

    def reraise(self):
        if self.pending is None:
            return
        signum, self.pending = self.pending, None
        handler = self._saved.get(signum)
        if callable(handler):          # review SHOULD-3: the caller's handler decides (it may only set a flag and return)
            handler(signum, None)
        elif signum == signal.SIGINT:
            raise KeyboardInterrupt
        else:
            raise SystemExit(128 + signum)


def stop_child(proc, grace=30.0):
    """G4 D2 (M12): TERM, ≤ grace s, then KILL — wp064_run and player-run.sh pass the TERM on to the Player, an immediate
    SIGKILL would orphan it. Signals meanwhile are deferred and dropped (the exception already on its way wins)."""
    with DeferredSignals():
        if proc.poll() is not None:
            return
        proc.terminate()
        try:
            proc.communicate(timeout=grace)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
        except (OSError, ValueError):
            try:
                proc.wait(timeout=grace)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()


def write_not_restored(ug, reason, live):
    ug = Path(ug)
    ug.mkdir(parents=True, exist_ok=True)
    (ug / MARKER).write_text(
        f"DIR {ug}\nreason {reason}\nlive {'; '.join(live)}\nutc {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}\n"
        "NOT restored: the snapshot is kept as is; no inline inventory was made. STOP — tell C-00; only C-00 writes "
        f"{MARKER_RESOLVED} next to this file.\n", encoding="utf-8")


def unresolved(markers):
    """The NOT-RESTORED markers among `markers` (paths) that C-00 has not resolved."""
    return sorted(str(m) for m in markers if not (Path(m).parent / MARKER_RESOLVED).exists())


def finish(ug, scope, players, wait_seconds=60.0, sleep=time.sleep, **retry):
    """G4 D2 (MUST-2b/3a): the caller's finally half, under DeferredSignals. players() lists the live game Players; the
    restore waits ≤ wait_seconds for none to be left. One still up → NO restore (this guard does not look at live
    Players: a restore under a writer would verify a lie): ug/userdata-NOT-RESTORED.txt, receipt code 65 — STOP, tell
    C-00 (65, not 30: callers retry 30 as a wait and would snapshot the unrestored data as a new baseline). Otherwise close(). Returns (receipt, signals); call signals.reraise() after the receipt is recorded."""
    ug = Path(ug)
    signals = DeferredSignals()
    with signals:
        live, waited = players(), 0.0
        while live and waited < wait_seconds:
            sleep(1.0)
            waited += 1.0
            live = players()
        if live:
            reason = f"a game Player was still up {int(waited)} s after the run ended"
            write_not_restored(ug, reason, live)
            return {"code": NOT_RESTORED, "why": f"NOT restored — {reason} ({ug / MARKER})", "restoreExit": None,
                    "verified": False, "touched": None, "outOfScopeChanged": None, "notRestored": str(ug / MARKER)}, signals
        return close(ug, scope, **retry), signals


def run_guard(command, directory, scope="player"):
    return subprocess.run([guard_path(), command, str(directory), "--scope", scope], capture_output=True, text=True,
                          preexec_fn=_ignore_signals if command == "restore" else None)


def snapshot_code(returncode):
    """C-00 30.09 M-14: 30 and 75 are 'wait' states and pass through; any other non-zero snapshot is a STOP (7)."""
    return returncode if returncode in (0, 30, 75) else STOP


def export_prefs(target):
    subprocess.run(["defaults", "export", PLAYER_PREFS_DOMAIN, str(target)], capture_output=True)


def _load_plist(path):
    try:
        with open(path, "rb") as handle:
            return plistlib.load(handle)
    except Exception:
        return {}


def changed_prefs(before, after):
    """Keys that differ between two PlayerPrefs plists (launch counters ignored)."""
    a, b = _load_plist(before), _load_plist(after)
    return sorted(k for k in set(a) | set(b) if k not in IGNORED_KEYS and a.get(k) != b.get(k))


def is_engine_key(key):
    return key.startswith(ENGINE_KEY_PREFIX) or key in ENGINE_KEYS_EXACT


def restore_fields(restore_text):
    last = restore_text.strip().splitlines()[-1] if restore_text.strip() else ""
    return dict(re.findall(r"(\w+)=(\S+)", last))


def verdict(ug, restore_text, after_plist):
    """(ok, why) for a restore that exited 0."""
    ug = Path(ug)
    fields = restore_fields(restore_text)
    if fields.get("verified") != "1":
        return False, "guard verified!=1"
    oos = ug / "out-of-scope-changed.txt"
    if oos.is_file() and oos.stat().st_size > 0:
        return False, "out-of-scope changes"
    touched = fields.get("touched")
    if touched in ("0", None):
        return True, "touched=0"
    keys = changed_prefs(ug / "snap" / "PR" / "p1.plist", after_plist)
    (ug / "prefs-diff.txt").write_text("\n".join(keys) + ("\n" if keys else ""), encoding="utf-8")
    if touched == "1" and keys and all(is_engine_key(k) for k in keys):
        return True, "touched=1 engine window keys only"
    return False, f"touched={touched} keys={keys}" + (" (a non-plist file changed)" if not keys else "")


def restore_with_retry(ug, scope="player", attempts=10, wait_seconds=30.0, sleep=time.sleep):
    """Restore with a bounded retry on 75 (a lock held); (code, CompletedProcess). Output appended to ug/restore.txt."""
    ug = Path(ug)
    ug.mkdir(parents=True, exist_ok=True)
    log = []
    result = None
    for attempt in range(1, attempts + 1):
        result = run_guard("restore", ug, scope)
        log.append(f"--- attempt {attempt} exit {result.returncode}\n{result.stdout}{result.stderr}")
        if result.returncode != 75 or attempt == attempts:
            break
        sleep(wait_seconds)
    text = "".join(log)
    if result.returncode == 75:
        text += f"\nVERİ GERİ YÜKLENMEDİ, snapshot {ug} — report to C-00\n"
    # WP-079G D6a: the guard itself appends its final line + exit=<n> to ug/restore.txt — append, never overwrite it
    with open(ug / "restore.txt", "a", encoding="utf-8") as f:
        f.write(text)
    return (0 if result.returncode == 0 else STOP), result


def close(ug, scope="player", **retry):
    """The finally-block half: export the player PlayerPrefs, restore (with retry), verdict → receipt dict:
    {code (0 or 7), why, restoreExit, verified, touched, outOfScopeChanged}."""
    ug = Path(ug)
    signals = DeferredSignals()   # G4 D2 MUST-1; a no-op inside finish() (its block records)
    with signals:
        export_prefs(ug / "prefs-after-run.plist")
        code, result = restore_with_retry(ug, scope, **retry)
        fields = restore_fields(result.stdout)
        oos = ug / "out-of-scope-changed.txt"
        receipt = {"restoreExit": result.returncode, "verified": fields.get("verified") == "1", "touched": fields.get("touched"),
                   "outOfScopeChanged": bool(oos.is_file() and oos.stat().st_size > 0)}
        if code:
            receipt = dict(receipt, code=STOP, why=f"guard restore exit {result.returncode}")
        else:
            ok, why = verdict(ug, result.stdout, ug / "prefs-after-run.plist")
            receipt = dict(receipt, code=0 if ok else STOP, why=why)
    signals.reraise()   # a direct caller (wp078_accept): the signal lands only after the restore is complete
    return receipt
