#!/usr/bin/env python3
"""WP-078 (C-00 30.09 M-14, M-17) · the one user-data guard receipt module for the tools/qa Player runners.

Every Player run of wp078_perf / wp078_bot goes: snapshot → run → (finally) restore, with one verdict:
  * the guard is the MAIN checkout's tools/userdata-guard.sh (C-00 30.09 G4 Q9; DD_USERDATA_GUARD overrides);
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
import subprocess
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PLAYER_PREFS_DOMAIN = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
ENGINE_KEY_PREFIX = "Screenmanager"
ENGINE_KEYS_EXACT = ("UnitySelectMonitor",)
IGNORED_KEYS = ("unity.player_session_count", "unity.player_sessionid")
STOP = 7


def guard_path():
    """The main checkout's guard (the parent of the shared .git), never a worktree copy; DD_USERDATA_GUARD overrides."""
    if os.environ.get("DD_USERDATA_GUARD"):
        return os.environ["DD_USERDATA_GUARD"]
    common = subprocess.run(["git", "-C", str(REPO), "rev-parse", "--path-format=absolute", "--git-common-dir"],
                            capture_output=True, text=True).stdout.strip()
    return str((Path(common).parent if common else REPO) / "tools" / "userdata-guard.sh")


def run_guard(command, directory, scope="player"):
    return subprocess.run([guard_path(), command, str(directory), "--scope", scope], capture_output=True, text=True)


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
    """Restore with a bounded retry on 75 (a lock held); (code, CompletedProcess). Output → ug/restore.txt."""
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
    (ug / "restore.txt").write_text(text, encoding="utf-8")
    return (0 if result.returncode == 0 else STOP), result


def close(ug, scope="player", **retry):
    """The finally-block half: export the player PlayerPrefs, restore (with retry), verdict → receipt dict:
    {code (0 or 7), why, restoreExit, verified, touched, outOfScopeChanged}."""
    ug = Path(ug)
    export_prefs(ug / "prefs-after-run.plist")
    code, result = restore_with_retry(ug, scope, **retry)
    fields = restore_fields(result.stdout)
    oos = ug / "out-of-scope-changed.txt"
    receipt = {"restoreExit": result.returncode, "verified": fields.get("verified") == "1", "touched": fields.get("touched"),
               "outOfScopeChanged": bool(oos.is_file() and oos.stat().st_size > 0)}
    if code:
        return dict(receipt, code=STOP, why=f"guard restore exit {result.returncode}")
    ok, why = verdict(ug, result.stdout, ug / "prefs-after-run.plist")
    return dict(receipt, code=0 if ok else STOP, why=why)
