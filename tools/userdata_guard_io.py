#!/usr/bin/env python3
"""G4 D5 (WP-089, C-00 05.10) · small, exact helpers for tools/userdata-guard.sh (zsh has no portable sha/fsync/pwd).

  sha <file>                 sha256 of the file, or MISSING
  lstart <pid>               the process start time in one fixed C-locale format (ps -o lstart), 3 tries; empty → exit 1
  atomic-write <dest>        stdin → <dest>.tmp.<pid> in the same folder, fsync, rename over <dest>
  pwhome                     the real (passwd) home directory, resolved — never $HOME
  root-check <root>          G4 Q7: <root> is absolute, exists, is a directory, is not a symlink and does not resolve
                             under <pwhome>/Library → exit 0; else one reason line, exit 64
  under <path> <dir>         exit 0 when realpath(<path>) is <dir> or below it (by st_dev/st_ino), else 1
  same <a> <b>               exit 0 when both name the same file system object, else 1
  casefold <dir> <name>…     every entry of <dir> whose case-folded name equals one of <name> but is not spelled the same
  plist-ok <file>…           exit 0 when every existing file loads as a plist, else the first unreadable path, exit 65
  sample <dir> <regex> <owner pid> [<holder pid>]
                             one sampler tick: every process whose command matches <regex> → <dir>/players.log
                             (`<epoch>⇥<pid> <command>`, the WP-066M format) and <dir>/players-lineage.tsv
                             (`<utc>⇥<pid>⇥<ppid>⇥<lstart>⇥<owned>`; owned = 1 when the owner or the slot holder pid is
                             the process or one of its ancestors, ≤ 64 steps)
  index-append <ledger> <field>…   one tab-separated line → <ledger>/INDEX.tsv under an flock on <ledger>/.lock (G4 D6 Q5)
  restore-area               G4 D6 R9: one area's write phase — JSON config on stdin, JSON result on stdout (restore_area)
  verify-area                G4 D6 R10: one area's verification — JSON config on stdin, JSON result on stdout (verify_area)
Read-only except atomic-write, sample, index-append and restore-area. Exit 2 = usage.
"""
import hashlib
import os
import plistlib
import pwd
import subprocess
import sys
import time


def sha(path):
    try:
        h = hashlib.sha256()
        with open(path, "rb") as f:
            for block in iter(lambda: f.read(1 << 20), b""):
                h.update(block)
        return h.hexdigest()
    except (FileNotFoundError, IsADirectoryError):
        return "MISSING"


def lstart(pid):
    env = {k: v for k, v in os.environ.items() if k != "TZ"}
    env["LC_ALL"] = "C"
    for attempt in range(3):
        r = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True, env=env)
        value = " ".join(r.stdout.split())
        if value:
            return value
        if attempt < 2:
            time.sleep(0.1)
    return ""


def atomic_write(dest):
    data = sys.stdin.buffer.read()
    tmp = "%s.tmp.%d" % (dest, os.getpid())
    with open(tmp, "wb") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, dest)


def pwhome():
    return os.path.realpath(pwd.getpwuid(os.getuid()).pw_dir)


def under(path, directory):
    """realpath(path) is directory or below it — by (st_dev, st_ino) of each ancestor, so a case-variant spelling on a
    case-insensitive volume (/users/erdem vs /Users/Erdem) cannot slip past (D5 review NIT-4)."""
    try:
        target = os.stat(directory)
    except OSError:
        p, d = os.path.realpath(path), os.path.realpath(directory)
        return p == d or p.startswith(d.rstrip("/") + "/")
    p = os.path.realpath(path)
    while True:
        try:
            st = os.stat(p)
            if (st.st_dev, st.st_ino) == (target.st_dev, target.st_ino):
                return True
        except OSError:
            pass
        parent = os.path.dirname(p)
        if parent == p:
            return False
        p = parent


def root_check(root):
    if not os.path.isabs(root):
        return "DD_GUARD_ROOT %r is not absolute" % root
    if os.path.islink(root.rstrip("/") or "/"):   # D5 review NIT-4: "…/link/" is a symlink too
        return "DD_GUARD_ROOT %s is a symlink" % root
    if not os.path.isdir(root):
        return "DD_GUARD_ROOT %s does not exist or is not a directory" % root
    if under(root, os.path.join(pwhome(), "Library")):
        return "DD_GUARD_ROOT %s resolves under the real user library %s/Library" % (root, pwhome())
    return None


def sample(directory, regex, owner, holder=None):
    import re
    env = {k: v for k, v in os.environ.items() if k != "TZ"}
    env["LC_ALL"] = "C"
    r = subprocess.run(["ps", "-axo", "pid=,ppid=,lstart=,command="], capture_output=True, text=True, env=env)
    rows, parent = [], {}
    for line in r.stdout.splitlines():
        parts = line.split(None, 7)
        if len(parts) < 8:
            continue
        pid, ppid, start, command = parts[0], parts[1], " ".join(parts[2:7]), parts[7]
        parent[pid] = ppid
        rows.append((pid, ppid, start, command))
    roots = {str(owner)} | ({str(holder)} if holder else set())
    now = int(time.time())
    utc = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(now))
    pat = re.compile(regex)
    log, lineage = [], []
    for pid, ppid, start, command in rows:
        if not pat.search(command) or "userdata_guard_io.py" in command:
            continue
        owned, p, steps = 0, pid, 0
        while p and p not in ("0", "1") and steps < 64:
            if p in roots:
                owned = 1
                break
            p, steps = parent.get(p), steps + 1
        log.append("%d\t%s %s\n" % (now, pid, command))
        lineage.append("%s\t%s\t%s\t%s\t%d\n" % (utc, pid, ppid, start, owned))
    if log:
        with open(os.path.join(directory, "players.log"), "a") as f:
            f.writelines(log)
        with open(os.path.join(directory, "players-lineage.tsv"), "a") as f:
            f.writelines(lineage)


# ---- G4 D6 (WP-089, C-00 05.10): the restore write path -------------------------------------------------------------
import ctypes
import errno
import fcntl
import json
import re

F_FULLFSYNC = 51
RENAME_SWAP, RENAME_EXCL = 0x2, 0x4
ABSENT = "MISSING"
PLIST_SUFFIX = " (launch-counter keys excluded)"
DEFAULTS_TIMEOUT = 10
LOCKER_RE = r"Contents/MacOS/(Deaths Demolition|DeathsDemolition)|/Unity\.app/Contents/MacOS/Unity"
_libc = None


def libc():
    global _libc
    if _libc is None:
        _libc = ctypes.CDLL(None, use_errno=True)
        _libc.renamex_np.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint]
    return _libc


def fullsync(fd):
    try:
        fcntl.fcntl(fd, F_FULLFSYNC)
    except OSError:
        os.fsync(fd)


def fsync_dir(path):
    fd = os.open(path, os.O_RDONLY)
    try:
        fullsync(fd)
    finally:
        os.close(fd)


def read_lines(path):
    """{absolute path: sha|digest} from a before/after-run txt (the `<64hex|MISSING>␠␠<path>[ (launch…)]` grammar)."""
    out = {}
    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
    except FileNotFoundError:
        return out
    for line in text.splitlines():
        if "  " not in line:
            continue
        digest, path_ = line.split("  ", 1)
        if path_.endswith(PLIST_SUFFIX):
            path_ = path_[: -len(PLIST_SUFFIX)]
            if digest == EMPTY_DIGEST:   # an older window recorded an empty plist by its digest: ≡ absent
                digest = ABSENT
        out[path_] = digest
    return out


def read_plain(path):
    try:
        with open(path, encoding="utf-8") as f:
            return [l.rstrip("\n") for l in f if l.strip()]
    except FileNotFoundError:
        return []


def plist_dict(raw_or_path, volatile):
    try:
        if isinstance(raw_or_path, (bytes, bytearray)):
            d = plistlib.loads(raw_or_path) if raw_or_path.strip() else None
        else:
            with open(raw_or_path, "rb") as f:
                d = plistlib.load(f)
    except (FileNotFoundError, plistlib.InvalidFileException, ValueError, OSError):
        return None
    if d is None:
        return None
    for k in volatile:
        d.pop(k, None)
    return d


EMPTY_DIGEST = hashlib.sha256(b"{}").hexdigest()


def plist_digest_of(d):
    """§2.2 NIT: an absent plist ≡ {} (defaults exports a missing absolute-path domain as an empty dict)."""
    if not d:
        return ABSENT
    return hashlib.sha256(json.dumps(d, sort_keys=True, default=repr).encode()).hexdigest()


def defaults(*args, input_bytes=None):
    try:
        r = subprocess.run(["defaults", *args], input=input_bytes, capture_output=True, timeout=DEFAULTS_TIMEOUT)
        return r.returncode, r.stdout
    except subprocess.TimeoutExpired:
        return 124, b""


def export_dict(domain, volatile):
    rc, out = defaults("export", domain, "-")
    if rc != 0:
        return None, rc
    return plist_dict(out, volatile), 0


def next_slot(directory, name):
    os.makedirs(directory, exist_ok=True)
    n = 1
    while os.path.lexists(os.path.join(directory, "%s.%d" % (name, n))):
        n += 1
    return os.path.join(directory, "%s.%d" % (name, n))


def index_append(ledger, fields):
    os.makedirs(ledger, exist_ok=True)
    with open(os.path.join(ledger, ".lock"), "a") as lk:
        fcntl.flock(lk.fileno(), fcntl.LOCK_EX)
        try:
            with open(os.path.join(ledger, "INDEX.tsv"), "a", encoding="utf-8") as f:
                f.write("\t".join(str(x) for x in fields) + "\n")
                f.flush()
                os.fsync(f.fileno())
        finally:
            fcntl.flock(lk.fileno(), fcntl.LOCK_UN)


def copy_synced(src, dst):
    """src → dst (new file), F_FULLFSYNC, sha-verified; the source is opened without following a symlink."""
    fd_in = os.open(src, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        fd_out = os.open(dst, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        try:
            while True:
                block = os.read(fd_in, 1 << 20)
                if not block:
                    break
                os.write(fd_out, block)
            fullsync(fd_out)
        finally:
            os.close(fd_out)
    finally:
        os.close(fd_in)
    if sha(src) != sha(dst):
        raise OSError(errno.EIO, "copy verification failed: %s" % dst)


def lock_holder(folder, own_pid):
    """pid of a live Player/Unity holding <folder>/prototype-profile.json.lock (D5 S7 rule), else None."""
    try:
        with open(os.path.join(folder, "prototype-profile.json.lock"), encoding="utf-8", errors="replace") as f:
            text = f.read()
    except FileNotFoundError:
        return None
    m = re.search(r"^pid=(\d+)", text, re.M)
    if not m:
        return None
    pid = int(m.group(1))
    if pid in (own_pid, os.getpid()):
        return None
    try:
        os.kill(pid, 0)
    except (ProcessLookupError, PermissionError):
        return None
    r = subprocess.run(["ps", "-o", "command=", "-p", str(pid)], capture_output=True, text=True)
    return pid if re.search(LOCKER_RE, r.stdout) else None


def player_live(regex):
    r = subprocess.run(["pgrep", "-fl", regex], capture_output=True, text=True)
    return r.returncode == 0 and r.stdout.strip() != ""


def area_symlink(cfg):
    """the area folder, the plist folder or any ancestor up to the user root is a symlink (SHOULD-5)."""
    for start, stop in ((cfg["folder"], cfg["rootAS"]), (os.path.dirname(cfg["plist"]), cfg["rootPR"])):
        p = start
        while True:
            if os.path.islink(p):
                return p
            if p == stop or os.path.dirname(p) == p or not p.startswith(stop):
                break
            p = os.path.dirname(p)
    return None


def area_files(cfg):
    """the area's protected file paths: recorded (before / first after-run) ∪ present now; side files first, the
    primary prototype-profile.json last (D6 (d))."""
    folder, names = cfg["folder"], cfg["names"]
    paths = set()
    for d in (cfg["_before"], cfg["_first"]):
        paths.update(p for p in d if p.startswith(folder + "/"))
    for n in names:
        if os.path.lexists(os.path.join(folder, n)):
            paths.add(os.path.join(folder, n))
    if os.path.isdir(folder):
        for e in os.listdir(folder):
            if e.startswith("prototype-profile.json.") and not e.endswith(".lock") and not e.startswith(".ddguard-"):
                paths.add(os.path.join(folder, e))
    primary = os.path.join(folder, "prototype-profile.json")
    return sorted(p for p in paths if p != primary) + ([primary] if primary in paths else [])


def live_sha(path):
    if os.path.islink(path):
        return "SYMLINK"
    return sha(path)


def _excl(fn, directory, name):
    """fn(dst) into the next free <directory>/<name>.<N>; another writer taking the same slot (EEXIST) → the next one."""
    while True:
        dst = next_slot(directory, name)
        try:
            fn(dst)
            return dst
        except FileExistsError:
            continue


def _rename_excl(src, dst):
    if libc().renamex_np(src.encode(), dst.encode(), RENAME_EXCL) != 0:
        e = ctypes.get_errno()
        if e == errno.EEXIST:
            raise FileExistsError(e, os.strerror(e), dst)
        raise OSError(e, os.strerror(e), dst)


def move_into(src, directory, name, side_tmp):
    """src → <directory>/<name>.<N>, never over an existing slot (D6 review: exclusive quarantine). Across volumes the
    file is first renamed to side_tmp in its own folder (atomic: a later writer by name cannot land in it), copied,
    verified, then unlinked."""
    os.makedirs(directory, exist_ok=True)
    try:
        return _excl(lambda dst: _rename_excl(src, dst), directory, name)
    except OSError as e:
        if e.errno != errno.EXDEV:
            raise
    os.rename(src, side_tmp)
    fsync_dir(os.path.dirname(src))
    dst = _excl(lambda d: copy_synced(side_tmp, d), directory, name)
    os.unlink(side_tmp)
    fsync_dir(os.path.dirname(src))
    return dst


def keep_copy(src, directory, name):
    os.makedirs(directory, exist_ok=True)
    return _excl(lambda dst: copy_synced(src, dst), directory, name)


def keep_bytes(raw, directory, name):
    os.makedirs(directory, exist_ok=True)

    def put(dst):
        fd = os.open(dst, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        try:
            os.write(fd, raw)
            fullsync(fd)
        finally:
            os.close(fd)
    return _excl(put, directory, name)


def write_lock(directory):
    """the write helper's lifetime lock (D6 review: a SIGKILLed guard shell leaves its helper running without the DIR
    lock; the next restore's R1 probes this one). Returns the open file, or None when another helper holds it."""
    f = open(os.path.join(directory, ".restore-write.lock"), "a")
    try:
        fcntl.lockf(f.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        f.close()
        return None
    return f


def restore_area(cfg):
    """G4 D6 R9 for one area. cfg: dir, area, id, ledger, folder, names, plist, domain, pxName, snapFolder, snapPlist,
    before (txt), first (txt), volatile, playerRe, checkPlayer, rootAS, rootPR, guardPid. Signals are ignored by the
    caller (SIG_IGN is inherited). Result: {"paths": [...], "written": n, "classes": [...]}."""
    cfg["_before"], cfg["_first"] = read_lines(cfg["before"]), read_lines(cfg["first"])
    res = {"area": cfg["area"], "paths": [], "written": 0, "classes": []}
    qroot = os.path.join(cfg["ledger"], "quarantine", cfg["id"])
    utc = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    id12 = cfg["id"][:12]

    def note(entry, cls):
        entry["status"] = cls
        res["paths"].append(entry)
        if cls not in DONE and cls not in res["classes"]:
            res["classes"].append(cls)

    def index(path, digest, reason, cls):
        index_append(cfg["ledger"], [utc, cfg["id"], cfg["dir"], path, digest, reason, cls])

    def finish():
        # D6 review: a race before any write is "nothing restored" (75); once anything in the area was written it is 30
        if res["written"]:
            for e in res["paths"]:
                if e.get("status") == "RACED-EARLY":
                    e["status"] = "RACED-LATE"
            res["classes"] = list(dict.fromkeys("RACED-LATE" if c == "RACED-EARLY" else c for c in res["classes"]))
        return res

    # paths this window already restored (any earlier attempt): one that changed since was changed by someone else, even
    # back to the run's bytes — STALE, never written again (D6 data-loss review MUST-1)
    done_file = os.path.join(cfg["dir"], "written-%s.txt" % cfg["area"])
    done = set(read_plain(done_file))

    def mark_done(path):
        with open(done_file, "a", encoding="utf-8") as f:
            f.write(path + "\n"); f.flush(); fullsync(f.fileno())

    lock = write_lock(cfg["dir"])
    if lock is None:
        note({"path": cfg["folder"], "why": "another restore's write helper of this DIR is still running"}, "BUSY")
        return res

    link = area_symlink(cfg)
    if link:
        for p in area_files(cfg) + [cfg["plist"]]:
            note({"path": p, "why": "symlink " + link}, "SYMLINK")
        return res

    def cas(path, first):
        """(ok, why) — the lock / Player / content compare-and-swap right before a write (content last)."""
        h = lock_holder(cfg["folder"], cfg["guardPid"])
        if h:
            return False, "profile lock held by live pid %d" % h
        if cfg["checkPlayer"] and player_live(cfg["playerRe"]):
            return False, "a Player is running"
        now = live_sha(path)
        return (now == first, "" if now == first else "changed since the run ended (%s, want %s)" % (now[:8], first[:8]))

    def raced(entry, why):
        entry["why"] = why
        note(entry, "RACED-LATE" if res["written"] else "RACED-EARLY")

    for path in area_files(cfg):
        name = os.path.basename(path)
        exp = cfg["_before"].get(path, ABSENT)
        first = cfg["_first"].get(path, ABSENT)
        entry = {"path": path, "exp": exp, "first": first}
        side = os.path.join(cfg["folder"], ".ddguard-%s-mv-%d.tmp" % (id12, os.getpid()))
        try:
            live = live_sha(path)
            entry["live"] = live
            if live == "SYMLINK":
                note(entry, "SYMLINK"); continue
            if exp != ABSENT and sha(os.path.join(cfg["snapFolder"], name)) != exp:
                note(entry, "SNAPCORRUPT"); continue
            if live == exp:
                note(entry, "OK"); continue
            if live != first or path in done:
                if path in done:
                    entry["why"] = "changed after this window restored it"
                note(entry, "STALE"); continue
            ok, why = cas(path, first)
            if not ok:
                raced(entry, why); continue
            if exp == ABSENT:   # the run created it: moved to quarantine/<id>/created (never deleted) — INV-5 / Q5
                dst = move_into(path, os.path.join(qroot, "created", cfg["area"]), name, side)
                fsync_dir(cfg["folder"])
                entry["quarantine"] = [dst]
                res["written"] += 1
                moved = sha(dst)
                index(path, moved, "created", "" if moved == first else "RACED")
                if moved != first:   # a save landed between the CAS and the move: kept, reported
                    raced(entry, "changed between the CAS and the move"); continue
                mark_done(path); note(entry, "CREATED-MOVED"); continue
            if live != ABSENT:   # INV-4: the bytes about to be replaced are kept first; no copy → not written
                dst = keep_copy(path, os.path.join(qroot, "displaced", cfg["area"]), name)
                entry.setdefault("quarantine", []).append(dst)
                kept = sha(dst)
                index(path, kept, "displaced", "")
                if kept != first:   # D6 review N4: the kept copy must be the bytes the decision was about
                    raced(entry, "changed while it was being kept"); continue
            tmp = os.path.join(cfg["folder"], ".ddguard-%s-%d.tmp" % (id12, os.getpid()))
            copy_synced(os.path.join(cfg["snapFolder"], name), tmp)
            ok, why = cas(path, first)
            if not ok:
                os.unlink(tmp); raced(entry, why); continue
            flags = RENAME_SWAP if os.path.lexists(path) else RENAME_EXCL
            if libc().renamex_np(tmp.encode(), path.encode(), flags) != 0:
                e = ctypes.get_errno()
                os.unlink(tmp)
                raise OSError(e, os.strerror(e))
            fsync_dir(cfg["folder"])
            res["written"] += 1
            if flags == RENAME_SWAP:   # tmp now holds the replaced bytes
                old = sha(tmp)
                if old != first:   # changed between the last CAS and the swap: keep those bytes too (SHOULD-3)
                    dst = move_into(tmp, os.path.join(qroot, "displaced", cfg["area"]), name, side)
                    fsync_dir(cfg["folder"])
                    entry.setdefault("quarantine", []).append(dst)
                    index(path, old, "displaced", "RACED")
                    raced(entry, "changed between the CAS and the swap"); continue
                os.unlink(tmp); fsync_dir(cfg["folder"])
            mark_done(path); note(entry, "WRITTEN")
        except OSError as e:   # D6 review N2: one unreadable / unwritable path never stops the others (INV-2)
            entry["why"] = str(e)
            note(entry, "WRITE_FAILED")

    # the plist: attempted whatever the file steps did (D6 (e))
    p = cfg["plist"]
    vol = cfg["volatile"]
    exp = cfg["_before"].get(p, ABSENT)
    first = cfg["_first"].get(p, ABSENT)
    entry = {"path": p, "exp": exp, "first": first}
    if os.path.islink(p):
        note(entry, "SYMLINK"); return finish()
    snap = plist_dict(cfg["snapPlist"], vol) if os.path.exists(cfg["snapPlist"]) else None
    if plist_digest_of(snap) != exp:   # D6 review N5: also when the window recorded no plist
        note(entry, "SNAPCORRUPT"); return finish()
    marker = os.path.join(cfg["dir"], ".plist-%s.writing" % cfg["pxName"])
    displaced_dir = os.path.join(qroot, "displaced", "PR")

    def decide(exported, disk):
        live = plist_digest_of(exported)
        if live == exp and disk == exp:
            return "OK"
        if live == first and disk == first:
            return "WRITE"
        # MUST-5: OUR OWN half-done write (D6 review MUST-1: only while this window's marker exists — set before the first
        # delete, removed after the import) — every live key holds a first or a snapshot value
        try:   # the marker names the export kept right before that write (the run's values)
            with open(marker, encoding="utf-8") as f:
                first_export = f.read().strip()
        except FileNotFoundError:
            return "STALE"
        if first_export and os.path.exists(first_export) and exported is not None:
            fd = plist_dict(first_export, vol) or {}
            sd = snap or {}
            if all(exported[k] in (fd.get(k, object()), sd.get(k, object())) for k in exported):
                return "WRITE"
        return "STALE"

    try:
        exported, erc = export_dict(cfg["domain"], vol)
        if erc != 0:   # the live value is unknown: never STALE (a user change) by guess, never written
            entry["why"] = "defaults export failed (%d) — live value unknown, not written" % erc
            note(entry, "WRITE_FAILED"); return finish()
        disk = plist_digest_of(plist_dict(p, vol))
        entry["live"], entry["liveDisk"] = plist_digest_of(exported), disk
        d = decide(exported, disk)
        if d == "WRITE" and p in done:
            entry["why"] = "changed after this window restored it"; d = "STALE"
        if d != "WRITE":
            note(entry, d); return finish()
        h = lock_holder(cfg["folder"], cfg["guardPid"])
        if h or (cfg["checkPlayer"] and player_live(cfg["playerRe"])):
            raced(entry, "lock / Player at the plist write"); return finish()
        # the CAS right before the write (design (a), D6 review MUST-2): this export is both the kept copy and the check
        rc, raw = defaults("export", cfg["domain"], "-")
        if rc != 0:
            raise OSError(errno.EIO, "defaults export failed (%d) — the live plist cannot be kept, not written" % rc)
        dst = keep_bytes(raw, displaced_dir, cfg["pxName"] + ".plist")
        entry.setdefault("quarantine", []).append(dst)
        now = plist_dict(raw, vol)
        index(p, plist_digest_of(now), "displaced", "")
        if decide(now, plist_digest_of(plist_dict(p, vol))) != "WRITE":
            raced(entry, "the plist changed before the write (kept: %s)" % dst); return finish()
        if not os.path.exists(marker):   # a retry keeps the first attempt's marker (its export holds the run's values)
            with open(marker, "w") as f:
                f.write(dst + "\n"); f.flush(); fullsync(f.fileno())
            fsync_dir(cfg["dir"])
        failed = []
        for k in sorted(set(now or {}) - set(snap or {})):
            if defaults("delete", cfg["domain"], k)[0] != 0:
                failed.append("delete " + k)
        if snap:
            tmp = os.path.join(cfg["dir"], ".import-%s.plist" % cfg["pxName"])
            with open(tmp, "wb") as f:
                plistlib.dump(snap, f, fmt=plistlib.FMT_BINARY)
            if defaults("import", cfg["domain"], tmp)[0] != 0:
                failed.append("import")
            os.unlink(tmp)
        res["written"] += 1
        if failed:   # the marker stays: the retry may finish this half-done write (MUST-5)
            entry["why"] = ", ".join(failed); note(entry, "WRITE_FAILED")
        else:
            os.unlink(marker); fsync_dir(cfg["dir"])
            mark_done(p); note(entry, "WRITTEN")
    except OSError as e:
        entry["why"] = str(e); note(entry, "WRITE_FAILED")
    return finish()


DONE = ("OK", "WRITTEN", "CREATED-MOVED")


def verify_area(cfg):
    """G4 D6 R10: every protected path of the area against before (sha; plist on disk AND through defaults), and the
    keys the snapshot does not have. A path the write phase left unwritten by decision (STALE, RACED-*, SYMLINK, …) already
    carries its class in restore-<area>.json (cfg "result") and is listed under "skipped", not as a mismatch — a RACED-EARLY
    stays 75. Result: {"ok": bool, "mismatch": [...], "extraKeys": [...], "skipped": [...]}."""
    cfg["_before"], cfg["_first"] = read_lines(cfg["before"]), {}
    vol = cfg["volatile"]
    status = {}
    if cfg.get("result") and os.path.exists(cfg["result"]):
        with open(cfg["result"], encoding="utf-8") as f:
            status = {e["path"]: e["status"] for e in json.load(f)["paths"]}
    mismatch, skipped = [], []
    for path in area_files(cfg):
        if status.get(path, "OK") not in DONE:
            skipped.append(path); continue
        exp = cfg["_before"].get(path, ABSENT)
        if live_sha(path) != exp:
            mismatch.append(path)
    p = cfg["plist"]
    extra = []
    if status.get(p, "OK") not in DONE:
        skipped.append(p)
    else:
        exp = cfg["_before"].get(p, ABSENT)
        exported, _ = export_dict(cfg["domain"], vol)
        if plist_digest_of(plist_dict(p, vol)) != exp or plist_digest_of(exported) != exp:
            mismatch.append(p)
        snap = plist_dict(cfg["snapPlist"], vol) if os.path.exists(cfg["snapPlist"]) else None
        extra = sorted(set(exported or {}) - set(snap or {}))
    return {"ok": not mismatch and not extra, "mismatch": mismatch, "extraKeys": extra, "skipped": skipped}


def main(argv):
    if not argv:
        print(__doc__, file=sys.stderr)
        return 2
    cmd, args = argv[0], argv[1:]
    if cmd == "sha" and len(args) == 1:
        print(sha(args[0]))
        return 0
    if cmd == "lstart" and len(args) == 1:
        value = lstart(args[0])
        print(value)
        return 0 if value else 1
    if cmd == "atomic-write" and len(args) == 1:
        atomic_write(args[0])
        return 0
    if cmd == "pwhome" and not args:
        print(pwhome())
        return 0
    if cmd == "root-check" and len(args) == 1:
        why = root_check(args[0])
        if why:
            print(why)
            return 64
        return 0
    if cmd == "same" and len(args) == 2:   # the same file system object (case-insensitive volumes: by st_dev/st_ino)
        try:
            a, b = os.stat(args[0]), os.stat(args[1])
        except OSError:
            return 1
        return 0 if (a.st_dev, a.st_ino) == (b.st_dev, b.st_ino) else 1
    if cmd == "under" and len(args) == 2:
        return 0 if under(args[0], args[1]) else 1
    if cmd == "casefold" and len(args) >= 2:
        d, names = args[0], {n.casefold(): n for n in args[1:]}
        if os.path.isdir(d):
            for entry in sorted(os.listdir(d)):
                want = names.get(entry.casefold())
                if want is not None and entry != want:
                    print(os.path.join(d, entry))
        return 0
    if cmd == "plist-ok" and args:
        for p in args:
            if os.path.lexists(p):
                try:
                    with open(p, "rb") as f:
                        plistlib.load(f)
                except Exception:
                    print(p)
                    return 65
        return 0
    if cmd == "index-append" and len(args) >= 2:
        index_append(args[0], args[1:])
        return 0
    if cmd in ("restore-area", "verify-area") and not args:
        cfg = json.load(sys.stdin)
        print(json.dumps(restore_area(cfg) if cmd == "restore-area" else verify_area(cfg)))
        return 0
    if cmd == "sample" and len(args) in (3, 4):
        sample(*args)
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
