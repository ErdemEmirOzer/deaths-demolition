#!/usr/bin/env python3
"""WP-078 (C-00 30.09 M-14, M-17) · the one user-data guard receipt module for the tools/qa Player runners.

Every Player run of wp078_perf / wp078_bot goes: snapshot → run → (finally) restore, with one verdict:
  * the guard is the canonical CI-driver copy (G4 D4, C-00 01.10): GUARD = ~/Dev/KADRO/…/wp-079/driver/tools/
    userdata-guard.sh (dd-env.sh DD_GUARD_CANON), a fixed path — no env override; canon_refusal() names why a caller must
    stop with 64 (DD_GUARD_ROOT set, or GUARD missing) before it takes anything;
  * restore runs with INT/TERM/HUP ignored in the guard child (G4 D2 MUST-1: a Ctrl-C or a group signal cannot cut the
    guard between its delete and its import);
  * snapshot exit codes: 0 ok · 30 a live DD Player (wait) · 75 a lock held (wait) · anything else → 7 STOP;
  * restore always runs (try/finally in the callers; SIGTERM becomes SystemExit 143 so finally runs too);
    restore 75 (lock held) is retried a bounded number of times, then 7 and "VERİ GERİ YÜKLENMEDİ" is written;
    any other restore error → 7;
  * verdict (C-00 25.09): verified=1, no out-of-scope change, and touched = 0 — or touched = 1 where the one changed
    file is the player PlayerPrefs plist (the guard's own before-player.txt / after-run-player.txt rows: touched is a
    per-area flag, not a count; gt 07.10) and every changed key is one the engine writes itself: the window state
    (Screenmanager* / UnitySelectMonitor) or the quality level (UnityGraphicsQuality — the Player saves it on every
    QualitySettings.SetQualityLevel, e.g. a -ddQualityTier probe; C-00 07.10). Any other changed player file, missing
    state rows, or any other changed key → STOP. This is the only copy of the rule (the KADRO player-run.sh /
    prefs-diff.py copy is retired).
"""
import os
import plistlib
import re
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402  (WP-089: the one KADRO root for the Python tools)
GUARD = str(dd_paths.KPROJ / "wp-079/driver/tools/userdata-guard.sh")
PLAYER_PREFS_DOMAIN = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
ENGINE_KEY_PREFIX = "Screenmanager"
# C-00 07.10: UnityGraphicsQuality is written by the Player itself (QualityTiers.ApplyOverride → SetQualityLevel); the
# guard restores it like the window keys (exact per domain, verified=1 still required) — not a reason to STOP
ENGINE_KEYS_EXACT = ("UnitySelectMonitor", "UnityGraphicsQuality")
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
    # WP-089 (C-00 05.10): the driver's guard must be main's (tools/guard_freshness.py, shared with dd_guard_canon_check)
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    import guard_freshness
    state, msg = guard_freshness.freshness(GUARD)
    if state == "stale":
        return msg
    if msg:
        print("guard: warning: " + msg, file=sys.stderr)
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


def oos_decide(ug):
    """WP-099 (C-00 08.10, FINAL audit Y4): an EDITOR window's out-of-scope change is judged by qa/oos_rule.py, the same
    rule rc.sh applies to an editor stage's guard 32 (player area only, every writer a guarded Player window restored
    verified=1, or — 2b — no Player alive and the change inside another player window's span). (ok, why)."""
    here = str(Path(__file__).resolve().parent)   # tools/qa: oos_rule lives next to this module
    if here not in sys.path:
        sys.path.insert(0, here)
    import oos_rule
    wait = os.environ.get("RC_OOS_WAIT")
    return oos_rule.decide(ug, [dd_paths.KPROJ], float(wait) if wait else 600.0, ledgers=oos_rule.default_ledgers())


def verdict(ug, restore_text, after_plist, scope="player"):
    """(ok, why) for a restore that exited 0 (or 32). Out-of-scope changes stop a player window; an editor window's go to
    oos_decide() — accepted ones pass with a WARNING in why, the rest stop as before."""
    ug = Path(ug)
    fields = restore_fields(restore_text)
    if fields.get("verified") != "1":
        return False, "guard verified!=1"
    oos = ug / "out-of-scope-changed.txt"
    warning = ""
    # G4 D7 (review SHOULD-1): a retry publishes its list as out-of-scope-changed.txt.N — the summary line is the truth
    if (oos.is_file() and oos.stat().st_size > 0) or fields.get("outOfScopeChanged", "0") != "0" or fields.get("class") == "OTHER":
        if scope != "editor":
            return False, "out-of-scope changes"
        try:
            ok, why = oos_decide(ug)
        except Exception as e:   # the rule could not judge — the old answer
            ok, why = False, "oos_rule failed: %s: %s" % (type(e).__name__, e)
        if not ok:
            return False, "out-of-scope changes (oos_rule: %s)" % why
        warning = " · WARNING out-of-scope change accepted by oos_rule: " + why
    touched = fields.get("touched")
    if touched in ("0", None):
        return True, "touched=0" + warning
    keys = changed_prefs(ug / "snap" / "PR" / "p1.plist", after_plist)
    (ug / "prefs-diff.txt").write_text("\n".join(keys) + ("\n" if keys else ""), encoding="utf-8")
    # gt 07.10 MUST-1: touched is the guard's per-area flag (any change → 1), not a file count — WHICH files changed comes
    # from the guard's own state rows of the player area (the out-of-scope check above covers the editor area)
    rows = changed_rows(ug, "player")
    if rows is None:
        return False, f"touched={touched} but the guard's player state rows are missing or unreadable"
    files = [r for r in rows if not r.endswith(PLIST_ROW_SUFFIX)]
    if files:
        return False, f"touched={touched} files={files} keys={keys}"
    if not rows:
        return False, f"touched={touched} but no player state row changed"
    if keys and all(is_engine_key(k) for k in keys):
        return True, f"touched={touched} engine keys only" + warning
    return False, f"touched={touched} keys={keys}" + (" (the plist row changed, no key differs)" if not keys else "")


PLIST_ROW_SUFFIX = " (launch-counter keys excluded)"   # userdata-guard.sh state_of: the area's PlayerPrefs plist row


def changed_rows(ug, area):
    """The guard's state rows ("<digest>  <path>", the plist row ending in PLIST_ROW_SUFFIX) of <area> that differ between
    before-<area>.txt and the run's FIRST after-run-<area>.txt (the pair the guard's touched flag compares) — the row's
    path part, sorted; None when either file is missing or unreadable."""
    try:
        before = set((Path(ug) / f"before-{area}.txt").read_text(encoding="utf-8").splitlines())
        after = set((Path(ug) / f"after-run-{area}.txt").read_text(encoding="utf-8").splitlines())
    except (OSError, UnicodeDecodeError):
        return None
    return sorted({row.split("  ", 1)[-1] for row in before ^ after})


OUT_OF_SCOPE = 32   # userdata-guard restore (G4 D7): verified in scope, another area changed


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
    # G4 D7: 32 = this window's area restored and verified, the other area changed — handed to verdict() like 0, which
    # still stops on out-of-scope changes (the acceptance behaviour before D7, when this case exited 0)
    return (0 if result.returncode in (0, OUT_OF_SCOPE) else STOP), result


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
                   "outOfScopeChanged": bool(oos.is_file() and oos.stat().st_size > 0)
                   or fields.get("outOfScopeChanged", "0") != "0" or fields.get("class") == "OTHER"}
        if code:
            receipt = dict(receipt, code=STOP, why=f"guard restore exit {result.returncode}")
        else:
            ok, why = verdict(ug, result.stdout, ug / "prefs-after-run.plist", scope)
            receipt = dict(receipt, code=0 if ok else STOP, why=why)
    signals.reraise()   # a direct caller (wp078_accept): the signal lands only after the restore is complete
    return receipt
