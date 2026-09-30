#!/usr/bin/env python3
"""WP-079G (C-00 30.09) · keep-guard for tools/cleanup-merged.sh: decides, per folder the tool could delete, whether it
must be kept. A user's --apply removed S-07's dev build finished the same afternoon (no in-use / recency check existed).

usage: cleanup_guard.py [--no-recent] PATH...   → one line per path: "<verdict>\t<path>" · exit 0 (64 usage)
verdicts (first match wins):
  keep (protected: <seg>)  a path segment is evidence / reports / backup / prompts / manifest / asset-production
  keep (.dd-keep)          the folder or an ancestor below $HOME has a `.dd-keep` file (a package protects its tree)
  keep (in use: pid N)     a live process names the folder: any process whose command line contains the folder path;
                           Player (`Contents/MacOS/(Deaths Demolition|DeathsDemolition)`) and Unity
                           (`…/Unity.app/Contents/MacOS/Unity`) processes also by working directory and by relative path
                           arguments (-projectPath / -logFile / -ddOut …) resolved against it; a live slot owner
                           (DD_SLOT_BASE, default /tmp/dd-slots/<kind>/<n>.<tag>.<pid>) and its descendants by command
                           line and working directory. The guard itself and its ancestors are ignored.
  keep (recent: …)         (unless --no-recent) the newest mtime of any entry inside is younger than DD_KEEP_RECENT_HOURS
                           (default 24); the scan stops at the first recent entry and at DD_KEEP_SCAN_LIMIT entries (200000)
  keep (unknown: …)        a check could not be evaluated (ps / lsof failed, unreadable folder or slot dir, scan limit):
                           fail closed
  ok                       none of the above"""
import os, re, subprocess, sys, time

PROTECTED = ("evidence", "reports", "backup", "prompts", "manifest", "asset-production")
PLAYER_RE = re.compile(r"Contents/MacOS/(Deaths Demolition|DeathsDemolition)")
UNITY_SUFFIX = "/Unity.app/Contents/MacOS/Unity"
SLOT_KINDS = ("unity", "blender", "smoke", "player")


class Unknown(Exception):
    pass


def _variants(path):
    out = {os.path.abspath(path).rstrip("/")}
    try:
        out.add(os.path.realpath(path).rstrip("/"))
    except OSError:
        pass
    return [p for p in out if p]


def _names(text, folder):
    """True when `text` (a command line or a path) contains `folder` as a whole path (followed by /, space, quote or end)."""
    i = text.find(folder)
    while i >= 0:
        j = i + len(folder)
        if j == len(text) or text[j] in "/ \t\"'=:":
            return True
        i = text.find(folder, i + 1)
    return False


def _alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


class Processes:
    def __init__(self):
        try:
            out = subprocess.run(["ps", "-axww", "-o", "pid=,ppid=,args="], capture_output=True, text=True, timeout=30)
        except (OSError, subprocess.SubprocessError) as e:
            raise Unknown("process list unavailable: %s" % e)
        if out.returncode != 0 or not out.stdout.strip():
            raise Unknown("process list unavailable (ps exit %d)" % out.returncode)
        self.args, self.ppid = {}, {}
        for row in out.stdout.splitlines():
            parts = row.strip().split(None, 2)
            if len(parts) < 2 or not parts[0].isdigit() or not parts[1].isdigit():
                continue
            pid, ppid = int(parts[0]), int(parts[1])
            self.args[pid], self.ppid[pid] = (parts[2] if len(parts) > 2 else ""), ppid
        me, n = os.getpid(), 0
        self.ignored = set()
        while me and me not in self.ignored and n < 64:  # the guard and its callers (cleanup-merged) are not "in use"
            self.ignored.add(me)
            me, n = self.ppid.get(me, 0), n + 1
        if os.getpid() not in self.args:
            raise Unknown("process list incomplete (own pid missing)")
        self.tracked = self._tracked()
        self.cwd = self._cwds(self.tracked)

    def _slot_owners(self):
        base = os.environ.get("DD_SLOT_BASE") or "/tmp/dd-slots"
        owners = set()
        if not os.path.exists(base):
            return owners
        for kind in SLOT_KINDS:
            d = os.path.join(base, kind)
            if not os.path.exists(d):
                continue
            try:
                names = os.listdir(d)
            except OSError as e:
                raise Unknown("slot dir unreadable: %s" % e)
            for name in names:
                tail = name.rsplit(".", 1)[-1]
                if tail.isdigit() and _alive(int(tail)):
                    owners.add(int(tail))
        return owners

    def _tracked(self):
        pids = set()
        for pid, a in self.args.items():
            exe = a.split(" -", 1)[0].strip()
            if PLAYER_RE.search(a) or exe.endswith(UNITY_SUFFIX):
                pids.add(pid)
        todo = list(self._slot_owners())
        while todo:  # slot owners and their descendants
            p = todo.pop()
            if p in pids:
                continue
            pids.add(p)
            todo.extend(c for c, pp in self.ppid.items() if pp == p)
        return {p for p in pids if p not in self.ignored}

    def _cwds(self, pids):
        live = sorted(p for p in pids if p in self.args)
        if not live:
            return {}
        try:
            out = subprocess.run(["lsof", "-a", "-p", ",".join(map(str, live)), "-d", "cwd", "-Fpn"],
                                 capture_output=True, text=True, timeout=60)
        except (OSError, subprocess.SubprocessError) as e:
            raise Unknown("lsof unavailable: %s" % e)
        cwd, pid = {}, None
        for line in out.stdout.splitlines():
            if line.startswith("p") and line[1:].isdigit():
                pid = int(line[1:])
            elif line.startswith("n") and pid is not None:
                cwd[pid] = line[1:]
        for p in live:  # a live tracked process whose cwd could not be read → cannot tell
            if p not in cwd and _alive(p):
                raise Unknown("working directory of pid %d unreadable" % p)
        return cwd

    def user(self, folder):
        variants = _variants(folder)
        for pid, a in self.args.items():
            if pid in self.ignored:
                continue
            if any(_names(a, v) for v in variants):
                return pid
        for pid in self.tracked:
            c = self.cwd.get(pid)
            if not c:
                continue
            if any(_names(c, v) or c == v for v in variants):
                return pid
            for tok in self.args.get(pid, "").split()[1:]:  # relative path arguments, resolved against the cwd
                tok = tok.strip("\"'")
                if not tok or tok.startswith(("/", "-")):
                    continue
                full = os.path.normpath(os.path.join(c, tok))
                if any(_names(full, v) or full == v for v in variants):
                    return pid
        return None


def recent(folder, hours, limit):
    cutoff = time.time() - hours * 3600
    try:
        st = os.lstat(folder)
    except OSError as e:
        raise Unknown("unreadable: %s" % e)
    if st.st_mtime >= cutoff:
        return folder
    stack, seen = [folder], 0
    while stack:
        d = stack.pop()
        try:
            with os.scandir(d) as it:
                for e in it:
                    seen += 1
                    if seen > limit:
                        raise Unknown("scan limit %d entries reached" % limit)
                    s = e.stat(follow_symlinks=False)
                    if s.st_mtime >= cutoff:
                        return e.path
                    if e.is_dir(follow_symlinks=False):
                        stack.append(e.path)
        except OSError as e:
            raise Unknown("unreadable: %s" % e)
    return None


def dd_keep_owner(folder):
    """The folder itself or any ancestor up to (not including) $HOME / the filesystem root that holds a `.dd-keep` file —
    a package owner can protect a whole `wp-NNN/` (e.g. a worktree whose Unity Library is reused) with one file."""
    here = os.path.abspath(folder)
    stop = {os.path.abspath(os.path.expanduser("~")), os.path.abspath(os.sep)}
    while here not in stop:
        if os.path.lexists(os.path.join(here, ".dd-keep")):
            return here
        parent = os.path.dirname(here)
        if parent == here:
            break
        here = parent
    return None


def verdicts(paths, check_recent=True):
    hours = float(os.environ.get("DD_KEEP_RECENT_HOURS") or 24)
    limit = int(os.environ.get("DD_KEEP_SCAN_LIMIT") or 200000)
    procs, perr = None, None
    try:
        procs = Processes()
    except Unknown as e:
        perr = str(e)
    for p in paths:
        seg = next((s for s in os.path.abspath(p).split(os.sep) if s in PROTECTED), None)
        if seg:
            yield "keep (protected: %s)" % seg, p
            continue
        try:
            keeper = dd_keep_owner(p)
            if keeper:
                yield "keep (.dd-keep%s)" % ("" if keeper == os.path.abspath(p) else " in " + keeper), p
                continue
            if perr:
                raise Unknown(perr)
            pid = procs.user(p)
            if pid:
                yield "keep (in use: pid %d)" % pid, p
                continue
            if check_recent:
                r = recent(p, hours, limit)
                if r:
                    age = (time.time() - os.lstat(r).st_mtime) / 3600
                    yield "keep (recent: %s %.1fh ago)" % (os.path.relpath(r, p), max(age, 0.0)), p
                    continue
            yield "ok", p
        except Unknown as e:
            yield "keep (unknown: %s)" % e, p
        except Exception as e:  # anything unexpected is "cannot tell" — fail closed
            yield "keep (unknown: %s)" % e, p


def main(argv):
    check_recent = True
    if argv and argv[0] == "--no-recent":
        check_recent, argv = False, argv[1:]
    if not argv:
        print(__doc__, file=sys.stderr)
        return 64
    for v, p in verdicts(argv, check_recent):
        print("%s\t%s" % (v, p))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
