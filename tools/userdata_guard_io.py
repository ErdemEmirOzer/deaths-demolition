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
  window-owner <window.json> G4 D7 R4: the receipt's owner (re-entrant: the slot holder) alive with its start time → 0, else 1
  foreign-players <lineage>  G4 D7: owned=0 Players of players-lineage.tsv as JSON; 1 when there is any
  ledger-begin <L> <po>      G4 D8 S6: record JSON on stdin; under L/.lock: 0 written (state snapshotting) · 65 refused
                             (a dirty / abandoned overlapping window; the message on stdout) · 75 a live foreign window
                             overlaps (wait outside the lock and ask again)
  ledger-update <L> <id>     G4 D8: JSON patch on stdin merged into windows/<id>.json (areas merged); 1 no such record
  ledger-close <L> <id>      G4 D8 R12: windows/<id>.json → closed/<id>.json · ledger-abort: → aborted/<id>.json
  ledger-find-dir <L> <dir>  the open record whose dir is <dir> (JSON), 1 none
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


def window_owner(path):
    """G4 D7 R4 (WINDOW_LOST): the window's real owner — the slot holder of a re-entrant snapshot, else the snapshot's
    caller — still alive with the start time recorded in the receipt. lstart() already asks ps 3 times; an empty answer
    counts as lost (the safe direction). Returns (alive: bool, why)."""
    with open(path, encoding="utf-8") as f:
        w = json.load(f)
    r = w.get("receipt") or {}
    who = r.get("holder") or r.get("owner")
    if not who or not who.get("pid"):
        return False, "the receipt names no owner"
    now = lstart(who["pid"])
    if not now:
        return False, "owner pid %s is gone (or ps gave no answer 3 times)" % who["pid"]
    if now != who.get("lstart"):
        return False, "owner pid %s was reused (started %s, the receipt says %s)" % (who["pid"], now, who.get("lstart"))
    return True, "owner pid %s alive" % who["pid"]


def foreign_players(path):
    """G4 D7 FOREIGN_WRITER: the players-lineage.tsv rows (utc, pid, ppid, lstart, owned) of Players outside the window's
    own process tree (owned=0), one per pid."""
    rows = {}
    for line in read_plain(path):
        cols = line.split("\t")
        if len(cols) == 5 and cols[4] == "0":
            rows.setdefault(cols[1], {"pid": int(cols[1]), "ppid": int(cols[2]), "lstart": cols[3], "firstSeen": cols[0]})
    return list(rows.values())


# ---- G4 D8 (WP-089, C-00 05.10): the window ledger L/windows/<id>.json ------------------------------------------------
LEDGER_LIVE = ("snapshotting", "open", "restoring")


class LedgerLock:
    def __init__(self, ledger):
        self.path = os.path.join(ledger, ".lock")

    def __enter__(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        self.f = open(self.path, "a")
        deadline = time.monotonic() + 60   # leaf lock, held only for one read-modify-write; bounded (§2.7, D8 review NIT-1)
        while True:
            try:
                fcntl.flock(self.f.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                return self
            except OSError:
                if time.monotonic() >= deadline:
                    self.f.close()
                    raise OSError(errno.ETIMEDOUT, "ledger lock %s busy for 60 s" % self.path)
                time.sleep(0.2)

    def __exit__(self, *exc):
        fcntl.flock(self.f.fileno(), fcntl.LOCK_UN)
        self.f.close()


def ledger_write(path, record):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = "%s.tmp.%d" % (path, os.getpid())
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(record, f, indent=1)
        f.flush()
        fullsync(f.fileno())
    os.rename(tmp, path)
    fsync_dir(os.path.dirname(path))


def ledger_records(ledger):
    folder = os.path.join(ledger, "windows")
    out = []
    for name in sorted(os.listdir(folder)) if os.path.isdir(folder) else []:
        if name.endswith(".json"):
            try:
                with open(os.path.join(folder, name), encoding="utf-8") as f:
                    out.append(json.load(f))
            except (ValueError, OSError):
                out.append({"id": name[:-5], "state": "dirty", "dir": "?", "windowAreas": ["editor", "player"],
                            "why": "unreadable ledger record"})
    return out


def record_owner(rec):
    slot = rec.get("slot") or {}
    return slot.get("holder") or rec.get("owner") or {}


def proc_state(pid):
    """(stat, lstart) of a pid from ONE `ps` call, lstart normalised as lstart() gives it; ("", "") when ps answers nothing
    (3 tries, as lstart())."""
    env = {k: v for k, v in os.environ.items() if k != "TZ"}
    env["LC_ALL"] = "C"
    for attempt in range(3):
        r = subprocess.run(["ps", "-o", "stat=,lstart=", "-p", str(pid)], capture_output=True, text=True, env=env)
        parts = r.stdout.split()
        if len(parts) >= 2:
            return parts[0], " ".join(parts[1:])
        if attempt < 2:
            time.sleep(0.1)
    return "", ""


def gone(who):
    """K3 (tools/slot.sh, C-00 30.09 M5) for the ledger too (D8 queue reviews 06.10): a process counts as gone only when
    kill -0 fails, or both start times are known and differ (pid reused), or it is a zombie — exited, not yet reaped by its
    parent (technical-writer 06.10, scenario 8z: kill -0 still answers for it). An empty `ps` answer (transient, or the
    recorded start time was never known) never makes a live pid dead — and a dead pid is dead whatever lstart answered."""
    try:
        pid = int((who or {}).get("pid") or 0)
    except (TypeError, ValueError):
        return True
    if pid <= 1:
        return True
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return True
    except PermissionError:
        pass
    then = (who or {}).get("lstart") or ""
    stat, now = proc_state(pid)
    if stat.startswith("Z"):
        return True
    return bool(now and then and now != then)


def alive(who):
    return not gone(who)


def ancestors(pid):
    env = {k: v for k, v in os.environ.items() if k != "TZ"}
    r = subprocess.run(["ps", "-axo", "pid=,ppid="], capture_output=True, text=True, env=env)
    parent = dict(line.split()[:2] for line in r.stdout.splitlines() if len(line.split()) >= 2)
    out, p, steps = set(), str(pid), 0
    while p and p not in ("0", "1") and steps < 64:
        out.add(int(p))
        p, steps = parent.get(p), steps + 1
    return out


def refusal(rec, live):
    import calendar
    try:
        age = (time.time() - calendar.timegm(time.strptime(rec.get("utc", ""), "%Y-%m-%dT%H:%M:%SZ"))) / 60.0
    except ValueError:
        age = -1
    return ('REFUSED — ledger window %s: DIR=%s state=%s owner=%s alive=%d age=%.0fmin next: '
            '"userdata-guard.sh restore %s --scope %s" — only once the run that opened it has ended (if it does not '
            'converge: C-00)'
            % (rec.get("id"), rec.get("dir"), rec.get("state"), (rec.get("owner") or {}).get("pid"), live, age, rec.get("dir"),
               rec.get("scope", "all")))


TICKET_NAME = r"^(\d+)-(.+)\.json$"   # L/queue/<time_ns>-<window id>.json; anything else in queue/ is not a ticket


def queue_tickets(ledger):
    """[(path, ns, ticket)] of L/queue/<ns>-<id>.json, oldest first. Other names (a note, a writer's *.tmp.<pid>, done/)
    are skipped; an unreadable ticket is retired to done/ (call under LedgerLock)."""
    import re
    folder = os.path.join(ledger, "queue")
    out = []
    for name in os.listdir(folder) if os.path.isdir(folder) else []:
        m = re.match(TICKET_NAME, name)
        if not m:
            continue
        path = os.path.join(folder, name)
        try:
            with open(path, encoding="utf-8") as f:
                out.append((path, int(m.group(1)), json.load(f)))
        except (ValueError, OSError):
            retire_ticket(ledger, path, "unreadable")
    out.sort(key=lambda x: (x[1], os.path.basename(x[0])))
    return out


def retire_ticket(ledger, path, reason):
    """a ticket leaves the queue by a move to L/queue/done/<name>.<reason>.json — nothing under L is ever deleted (G4 D8
    review 06.10, git-safety SHOULD-1). reason: granted · refused · left · reaped · unreadable · exit<rc>."""
    done = os.path.join(ledger, "queue", "done")
    os.makedirs(done, exist_ok=True)
    base = os.path.basename(path)[:-len(".json")] + "." + reason
    dest, n = os.path.join(done, base + ".json"), 1
    while os.path.exists(dest):
        n += 1
        dest = os.path.join(done, "%s.%d.json" % (base, n))
    try:
        os.rename(path, dest)
    except FileNotFoundError:
        return
    fsync_dir(done)


def typical_minutes(ledger, areas):
    """median duration (min) of the last 20 closed windows over these areas, or None."""
    import calendar
    folder = os.path.join(ledger, "closed")
    spans = []
    for name in sorted(os.listdir(folder))[-200:] if os.path.isdir(folder) else []:
        try:
            with open(os.path.join(folder, name), encoding="utf-8") as f:
                rec = json.load(f)
            if set(rec.get("windowAreas") or []) & areas:
                t0 = calendar.timegm(time.strptime(rec["utc"], "%Y-%m-%dT%H:%M:%SZ"))
                t1 = calendar.timegm(time.strptime(rec["utcUpdated"], "%Y-%m-%dT%H:%M:%SZ"))
                spans.append((t1 - t0) / 60.0)
        except (ValueError, OSError, KeyError):
            continue
    spans = sorted(spans[-20:])
    return spans[len(spans) // 2] if spans else None


def age_minutes(utc):
    import calendar
    try:
        return (time.time() - calendar.timegm(time.strptime(utc, "%Y-%m-%dT%H:%M:%SZ"))) / 60.0
    except (ValueError, TypeError):
        return 0.0


def utc_now():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def ledger_begin(ledger, record, po, waiter=None):
    """G4 D8 S6 + the C-00 05.10 queue. (code, message): 0 the record is written (state snapshotting) · 65 refused (an
    overlapping dirty / abandoned window, or this snapshot's own caller died while it queued) · 75 wait: a live foreign
    window overlaps, or an older live ticket of the FIFO queue L/queue/ does — this snapshot holds (or now takes) its own
    ticket; the caller polls outside the lock. Liveness and nesting are judged by the window's OWNER (the snapshot's
    caller), never by the slot holder (D8 review); liveness follows K3 (gone()). The estimate is computed after the lock is
    let go (06.10 reviews: no closed/ scan under L/.lock)."""
    mine = set(record.get("windowAreas") or [])
    qdir = os.path.join(ledger, "queue")
    with LedgerLock(ledger):
        mine_up = None

        def nested(who):
            nonlocal mine_up
            if mine_up is None:
                mine_up = ancestors(po)
            return int(who.get("pid", 0)) in mine_up

        tickets, own = [], None
        for path, ns, t in queue_tickets(ledger):   # dead waiters leave the queue (their own ticket, never a record)
            if t.get("id") == record.get("id"):
                own = (path, ns, t)
            elif not t.get("waiter") or gone(t["waiter"]) or gone(t.get("owner") or {}):
                # the waiting GUARD, or the caller it waits for, is gone — or the ticket names no waiter at all (no guard
                # writes one; an owner-only ticket outlives its guard while a long-lived shell lives on)
                retire_ticket(ledger, path, "reaped")
            else:
                tickets.append((path, ns, t))

        def leave_queue(reason):
            if own:
                retire_ticket(ledger, own[0], reason)

        # 06.10 reviews (MUST-4): a caller that died while its snapshot waited (here, or in the S2 player-slot wait) gets no
        # window — one opened for a dead owner would refuse every later snapshot of these areas (65, "abandoned")
        if gone(record.get("owner") or {}):
            leave_queue("left")
            return 65, "the caller pid %s is gone — no window opened for it (DIR=%s); nothing to restore" % (
                (record.get("owner") or {}).get("pid"), record.get("dir"))

        blocker, held = None, set()
        for rec in ledger_records(ledger):
            areas = set(rec.get("windowAreas") or [])
            if rec.get("id") == record.get("id") or rec.get("state") == "closed":   # 06.10 reviews (MUST-2): a closed
                continue                                                            # record only waits for its move
            who = rec.get("owner") or {}
            if not mine & areas:
                # gt 06.10 N1: an ANCESTOR's live window over other areas still holds them — a ticket over them waits for
                # it, and it waits for us; it is not ahead of us either
                if rec.get("state") in LEDGER_LIVE and nested(who) and alive(who):
                    held |= areas
                continue
            live = alive(who)
            restorer = rec.get("restorer")
            if rec.get("state") == "restoring" and restorer and alive(restorer):   # being restored → wait
                blocker = blocker or rec
                continue
            if rec.get("state") == "dirty" or rec.get("state") not in LEDGER_LIVE or not live \
                    or (rec.get("state") == "restoring" and restorer and not alive(restorer)):
                leave_queue("refused")
                return 65, refusal(rec, live)
            if nested(who):   # nested inside that window (LIFO): allowed
                held |= areas
                continue
            blocker = blocker or rec
        # 06.10 reviews (MUST-1): a ticket over an area an ANCESTOR's window holds cannot start before that window closes,
        # and that window waits for us — it is not ahead of a nested snapshot (else: deadlock until DD_GUARD_QUEUE_WAIT)
        mine_ns = own[1] if own else None
        ahead = [(ns, t) for p, ns, t in tickets
                 if mine & set(t.get("windowAreas") or []) and not held & set(t.get("windowAreas") or [])
                 and (mine_ns is None or ns < mine_ns) and not nested(t.get("owner") or {})]
        if blocker is None and not ahead:
            record = dict(record)
            if own:
                record["queuedUtc"] = own[2].get("utc")   # 06.10 review SHOULD-4: the window's age starts now, not at the queue
            record["utc"] = utc_now()
            if waiter:
                record["guard"] = {"pid": waiter, "lstart": lstart(waiter)}
            ledger_write(os.path.join(ledger, "windows", record["id"] + ".json"), record)
            leave_queue("granted")
            return 0, "ledger window %s snapshotting" % record["id"]
        place = ""
        if own is None:
            wl = lstart(waiter) if waiter else ""
            if not wl:   # 06.10 review MUST-3: a ticket carries the waiter and its start time (K3 can judge it)
                place = " (no queue place yet: %s — next poll)" % ("the waiter's start time is unknown" if waiter else "no waiter pid")
            else:
                ns = max([time.time_ns()] + [n + 1 for _, n, _ in tickets])   # FIFO even if the wall clock steps back
                os.makedirs(qdir, exist_ok=True)
                ticket = {"id": record["id"], "dir": record.get("dir"), "owner": record.get("owner"),
                          "waiter": {"pid": waiter, "lstart": wl},
                          "windowAreas": sorted(mine), "utc": utc_now()}
                ledger_write(os.path.join(qdir, "%d-%s.json" % (ns, record["id"])), ticket)
    # outside L/.lock: the estimate (reads up to 200 closed records)
    typical = typical_minutes(ledger, mine)
    unknown = "unknown (no closed window over these areas yet)"
    if blocker is not None:
        bw = (blocker.get("restorer") if blocker.get("state") == "restoring" else None) or blocker.get("owner") or {}
        age = age_minutes(blocker.get("utc"))
        first = "%s (pid %s, %s, %.0f min)" % (blocker.get("dir"), bw.get("pid"), blocker.get("state"), age)
        if typical is None:
            est = unknown
        elif age > typical:
            est = "overdue: open %.0f min, typical %.0f min%s" % (
                age, typical, " — then ~%.0f min for the %d ahead" % (typical * len(ahead), len(ahead)) if ahead else "")
        else:
            est = "~%.0f min" % (typical - age + typical * len(ahead))
    else:
        t = ahead[0][1]
        first = "%s (pid %s, queued %.0f min)" % (t.get("dir"), (t.get("owner") or {}).get("pid"), age_minutes(t.get("utc")))
        est = "~%.0f min" % (typical * len(ahead)) if typical is not None else unknown
    return 75, "queue position %d — ahead: %s — estimate %s%s" % (len(ahead) + 1, first, est, place)


def ledger_dequeue(ledger, wid, reason="left"):
    with LedgerLock(ledger):
        for path, ns, t in queue_tickets(ledger):
            if t.get("id") == wid:
                retire_ticket(ledger, path, reason)


def ledger_update(ledger, wid, patch, locked=False):
    if not locked:
        with LedgerLock(ledger):
            return ledger_update(ledger, wid, patch, locked=True)
    path = os.path.join(ledger, "windows", wid + ".json")
    if True:
        try:
            with open(path, encoding="utf-8") as f:
                rec = json.load(f)
        except FileNotFoundError:
            return None
        areas = dict(rec.get("areas") or {})
        for a, v in (patch.pop("areas", {}) or {}).items():
            # D8 review MUST-1: an area outside this restore's scope is "released" only while it is still open — a dirty or
            # finalized one keeps its value (a narrower re-restore never closes a window it did not restore)
            if v == "released" and areas.get(a) in ("dirty", "finalized"):
                continue
            areas[a] = v
        if patch.get("state") == "auto":   # R12: dirty while any area is dirty, else closed
            patch["state"] = "dirty" if "dirty" in areas.values() else "closed"
        rec.update(patch); rec["areas"] = areas; rec["utcUpdated"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        ledger_write(path, rec)
        if rec.get("state") == "closed":   # 06.10 reviews (MUST-2): closed and moved in ONE locked step — a queued waiter
            _move_locked(ledger, wid, "closed")   # never meets a closed record in windows/ (ledger-close is then a no-op)
        return rec


def _move_locked(ledger, wid, to):
    src, dest = os.path.join(ledger, "windows", wid + ".json"), os.path.join(ledger, to, wid + ".json")
    if not os.path.exists(src):
        return os.path.exists(dest)   # already moved (R12 closes in the same step): done, not a failure
    os.makedirs(os.path.join(ledger, to), exist_ok=True)
    os.rename(src, dest)
    fsync_dir(os.path.join(ledger, to))
    fsync_dir(os.path.join(ledger, "windows"))
    return True


def ledger_move(ledger, wid, to):
    with LedgerLock(ledger):
        return _move_locked(ledger, wid, to)


def ledger_find_dir(ledger, directory):
    real = os.path.realpath(directory)
    for rec in ledger_records(ledger):
        if rec.get("dir") == real:
            return rec
    return None


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
        index_append(cfg["ledger"], [utc, cfg["id"], cfg["dir"], path, digest, reason, cls or cfg.get("indexClass", "")])

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
    if cmd == "window-owner" and len(args) == 1:
        owner_ok, why = window_owner(args[0])   # not `alive`: the liveness helper mark-missing calls below
        print(why)
        return 0 if owner_ok else 1
    if cmd == "foreign-players" and len(args) == 1:
        rows = foreign_players(args[0])
        print(json.dumps(rows))
        return 1 if rows else 0
    if cmd == "ledger-begin" and len(args) in (2, 3):
        code, msg = ledger_begin(args[0], json.load(sys.stdin), int(args[1]), int(args[2]) if len(args) == 3 else None)
        print(msg)
        return code
    if cmd == "ledger-update" and len(args) == 2:
        rec = ledger_update(args[0], args[1], json.load(sys.stdin))
        if rec is None:
            return 1
        print(rec.get("state"))
        return 0
    if cmd == "ledger-mark-missing" and len(args) == 3:
        # SNAPSHOT_MISSING: the ledger knows this DIR but it cannot be restored → dirty; never a live snapshot in progress
        with LedgerLock(args[0]):   # find + check + update in ONE locked step (D8 review NIT-3)
            rec = ledger_find_dir(args[0], args[1])
            if rec is None:
                return 1
            if rec.get("state") == "snapshotting" and alive(rec.get("owner") or {}):
                return 2
            ledger_update(args[0], rec["id"], {"state": "dirty", "why": "SNAPSHOT_MISSING: " + args[2]}, locked=True)
        print(rec["id"])
        return 0
    if cmd == "ledger-dequeue" and len(args) in (2, 3):
        ledger_dequeue(args[0], args[1], args[2] if len(args) == 3 else "left")
        return 0
    if cmd in ("ledger-close", "ledger-abort") and len(args) == 2:
        return 0 if ledger_move(args[0], args[1], "closed" if cmd == "ledger-close" else "aborted") else 1
    if cmd == "ledger-find-dir" and len(args) == 2:
        rec = ledger_find_dir(args[0], args[1])
        if rec is None:
            return 1
        print(json.dumps(rec))
        return 0
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
