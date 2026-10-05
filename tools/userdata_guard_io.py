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
Read-only except atomic-write and sample. Exit 2 = usage.
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
    if cmd == "sample" and len(args) in (3, 4):
        sample(*args)
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
