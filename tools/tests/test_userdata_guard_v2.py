#!/usr/bin/env python3
"""G4 v2 guard tests (WP-089, C-00 05.10; design v3.1 §2.9). One class per slice; run a slice alone:
    python3 tools/tests/test_userdata_guard_v2.py SnapshotTests
The suite re-executes itself under sandbox-exec (no writes to the REAL ~/Library or /private/tmp/dd-slots, no cfprefsd);
without sandbox-exec it fails (no silent skip). Fixture: a temp root (DD_GUARD_ROOT=<tmp>/root with AS/PR), the real
guard + helpers copied to <tmp>/tools, HOME=<tmp>/home, DD_SLOT_BASE=<tmp>/slots, and shims first on PATH:
  defaults  only absolute domains under <tmp>/root/PR (export / import / delete on <domain>.plist); any other domain → 77
            and a FOREIGN line in the shim log (the tests assert there is none)
  pgrep     the Player pattern answers from <tmp>/pgrep-player (live when the file exists); other patterns pass through
  cp        pass-through; <tmp>/cp-slow → the first copy sleeps (and touches <tmp>/cp-started); <tmp>/cp-mutate → after
            copying ui-settings.json the live file changes (a save landing mid-copy)
  df/sysctl plenty of disk / no swap for slot.sh's disk gate
No Unity, no Player, no fixture process carries the real Player name (a lock holder is <tmp>/Unity.app/…/Unity → sleep)."""
import hashlib, json, os, plistlib, shutil, signal, subprocess, sys, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
BUNDLE = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
PRODUCT = "Deaths Demolition - Playable Concept"
EDITOR_DOMAIN = "unity.Deaths Demolition." + PRODUCT
FILES = ("prototype-profile.json", "ui-settings.json", "load-timing.json", "input-settings.json", "input-overrides.json",
         "ui-hints.json")
KG_GOOD = {"Screenmanager Fullscreen mode": 1, "Screenmanager Resolution Width": 1920, "Screenmanager Resolution Height": 1080,
           "Screenmanager Window Position X": 0, "Screenmanager Window Position Y": 0}
SANDBOX_FLAG = "DD_GUARD_V2_SANDBOXED"

DEFAULTS_SHIM = r'''#!/usr/bin/env python3
import os, plistlib, sys
log, root = os.environ["SHIM_LOG"], os.environ["SHIM_PR"]
args = sys.argv[1:]
with open(log, "a") as f: f.write("defaults " + " ".join(args) + "\n")
if len(args) < 2: sys.exit(2)
cmd, dom = args[0], args[1]
if not dom.startswith(root + "/"):
    with open(log, "a") as f: f.write("FOREIGN " + dom + "\n")
    sys.exit(77)
path = dom if dom.endswith(".plist") else dom + ".plist"
def load():
    try:
        with open(path, "rb") as f: return plistlib.load(f)
    except FileNotFoundError: return None
def save(d):
    with open(path, "wb") as f: plistlib.dump(d, f, fmt=plistlib.FMT_BINARY)
tmp = os.environ["SHIM_TMP"]
flag = lambda n: os.path.exists(os.path.join(tmp, n))
if cmd == "export":
    if flag("defaults-fail-export"): sys.exit(1)
    if flag("defaults-export-stale"):   # cfprefsd never mirrors the disk: settle times out
        sys.stdout.buffer.write(plistlib.dumps({"stale": True})); sys.exit(0)
    d = load()
    if d is None: sys.exit(1)
    sys.stdout.buffer.write(plistlib.dumps(d))
elif cmd == "import":
    if flag("defaults-import-sleep"):
        open(os.path.join(tmp, "import-started"), "w").close(); import time; time.sleep(30)
    if flag("defaults-fail-import"): sys.exit(1)
    with open(args[2], "rb") as f: new = plistlib.load(f)
    d = load() or {}; d.update(new); save(d)
elif cmd == "delete":
    if flag("defaults-fail-delete"): sys.exit(1)
    d = load()
    if d is None or (len(args) > 2 and args[2] not in d): sys.exit(1)
    if len(args) > 2: d.pop(args[2]); save(d)
    else: os.remove(path)
else:
    sys.exit(2)
'''
PGREP_SHIM = r'''#!/bin/sh
for a in "$@"; do case "$a" in *"Contents/MacOS/(Deaths Demolition"*)
  echo "pgrep $*" >> "$SHIM_LOG"
  n=$(( $(cat "$SHIM_TMP/pgrep-count" 2>/dev/null || echo 0) + 1 )); echo $n > "$SHIM_TMP/pgrep-count"
  [ -x "$SHIM_TMP/pgrep-hook" ] && "$SHIM_TMP/pgrep-hook" "$n"
  [ -f "$SHIM_TMP/pgrep-player" ] && { cat "$SHIM_TMP/pgrep-player"; exit 0; }; exit 1 ;; esac; done
exec /usr/bin/pgrep "$@"
'''
CP_SHIM = r'''#!/bin/sh
if [ -f "$SHIM_TMP/cp-slow" ] && [ ! -f "$SHIM_TMP/cp-started" ]; then : > "$SHIM_TMP/cp-started"; sleep "$(cat "$SHIM_TMP/cp-slow")"; fi
if [ -f "$SHIM_TMP/cp-kg" ] && [ ! -f "$SHIM_TMP/cp-kg-done" ]; then : > "$SHIM_TMP/cp-kg-done"
  python3 -c 'import plistlib,sys; p=sys.argv[1]; d=plistlib.load(open(p,"rb")); d["Screenmanager Fullscreen mode"]=3; plistlib.dump(d,open(p,"wb"),fmt=plistlib.FMT_BINARY)' "$SHIM_TMP/root/PR/com.Deaths-Demolition.Deaths-Demolition---Playable-Concept.plist"; fi
/bin/cp "$@"; rc=$?
if [ -f "$SHIM_TMP/cp-mutate" ]; then for a in "$@"; do case "$a" in */AS/*/ui-settings.json) printf ' ' >> "$a" ;; esac; done; fi
exit $rc
'''
# /bin/ps is setuid root and a sandboxed process may not exec it: this ps answers the forms the guard stack uses
# (-o <fields> -p <pid>, -axo / -Ao <fields>; fields pid ppid lstart command args comm stat, "=" = no header) from libproc
# and KERN_PROCARGS2 (same-user processes; no setuid needed). The lstart text is C-locale ps style for every caller.
PS_SHIM = r'''#!/usr/bin/env python3
import ctypes, ctypes.util, struct, sys, time
lib = ctypes.CDLL(ctypes.util.find_library("proc") or "/usr/lib/libproc.dylib")
libc = ctypes.CDLL(ctypes.util.find_library("c"))
def bsdinfo(pid):
    buf = ctypes.create_string_buffer(136)
    if lib.proc_pidinfo(int(pid), 3, ctypes.c_uint64(0), buf, 136) != 136: return None
    status, pid_, ppid = struct.unpack_from("<I", buf, 4)[0], struct.unpack_from("<I", buf, 12)[0], struct.unpack_from("<I", buf, 16)[0]
    comm = buf.raw[48:64].split(b"\0")[0].decode("utf-8", "replace")
    start = struct.unpack_from("<Q", buf, 120)[0]
    return {"pid": pid_, "ppid": ppid, "status": status, "comm": comm, "start": start}
def args(pid):
    mib = (ctypes.c_int * 3)(1, 49, int(pid)); size = ctypes.c_size_t(1 << 18); buf = ctypes.create_string_buffer(size.value)
    if libc.sysctl(mib, 3, buf, ctypes.byref(size), None, ctypes.c_size_t(0)) != 0: return None
    raw = buf.raw[:size.value]; argc = struct.unpack_from("<i", raw, 0)[0]
    rest = raw[4:]; i = rest.index(b"\0"); exe = rest[:i].decode("utf-8", "replace"); rest = rest[i:].lstrip(b"\0")
    parts = rest.split(b"\0")[:argc]
    return " ".join(p.decode("utf-8", "replace") for p in parts) or exe
def field(info, name):
    if name == "pid": return str(info["pid"])
    if name == "ppid": return str(info["ppid"])
    if name == "lstart": return time.strftime("%a %b %e %H:%M:%S %Y", time.localtime(info["start"]))
    if name in ("command", "args"): return args(info["pid"]) or info["comm"]
    if name == "comm": return (args(info["pid"]) or info["comm"]).split(" ")[0]
    if name == "stat": return {1: "I", 2: "R", 3: "S", 4: "T", 5: "Z"}.get(info["status"], "S")
    raise SystemExit("ps shim: unsupported field " + name)
argv = sys.argv[1:]; fields = None; pids = None; every = False; i = 0
while i < len(argv):
    a = argv[i]
    if a in ("-o",): fields = argv[i + 1]; i += 2; continue
    if a in ("-p",): pids = [argv[i + 1]]; i += 2; continue
    if a.startswith("-") and a.endswith("o") and len(a) > 2:
        every = every or any(c in a for c in "axA"); fields = argv[i + 1]; i += 2; continue
    if a.startswith("-") and set(a[1:]) <= set("axA"): every = True; i += 1; continue
    raise SystemExit("ps shim: unsupported argument " + a)
names = [f.strip().rstrip("=") for f in (fields or "pid,command").split(",")]
if every:
    n = lib.proc_listallpids(None, 0); arr = (ctypes.c_int * (n * 2))(); n = lib.proc_listallpids(arr, ctypes.sizeof(arr))
    pids = [str(p) for p in arr[:n] if p > 0]
import os, re
real_player = re.compile(r"Contents/MacOS/(Deaths Demolition|DeathsDemolition)")
rc = 1
for pid in pids or []:
    info = bsdinfo(pid)
    if info is None: continue
    cmd = args(info["pid"]) or info["comm"]
    # P5 review r2 (06.10): <tmp>/ps-restore-lstart-unknown → a restoring guard's own start time is unknown (-o lstart= -p)
    if not every and names == ["lstart"] and "userdata-guard.sh restore" in cmd \
            and os.path.exists(os.environ.get("SHIM_TMP", "\0") + "/ps-restore-lstart-unknown"): continue
    # isolation (05.10): a REAL Player of another session on this machine is not part of the fixture — the sampler would
    # log it as a foreign writer (FOREIGN_WRITER flakes); fixture Players live under SHIM_TMP and stay visible
    if every and real_player.search(cmd) and os.environ.get("SHIM_TMP", "\0") not in cmd: continue
    print(" ".join(field(info, f) for f in names)); rc = 0
sys.exit(rc)
'''
# mv: pass-through; <tmp>/mv-fail-before → the rename onto …/before.txt fails (a commit that breaks halfway)
MV_SHIM = r'''#!/bin/sh
for a in "$@"; do last="$a"; done
case "$last" in */before.txt) [ -f "$SHIM_TMP/mv-fail-before" ] && { echo "mv shim: simulated failure" >&2; exit 1; } ;; esac
exec /bin/mv "$@"
'''
DF_SHIM ="#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/fixture 999999999 1 999999999'\n"
SYSCTL_SHIM = "#!/bin/sh\n[ \"$2\" = vm.swapusage ] && { echo 'total = 1.00M  used = 0.00M  free = 1.00M'; exit 0; }\nexec /usr/sbin/sysctl \"$@\"\n"


def pwhome():
    import pwd
    return os.path.realpath(pwd.getpwuid(os.getuid()).pw_dir)


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).is_file() else "MISSING"


def load_plist(p):
    with open(p, "rb") as f:
        return plistlib.load(f)


def write_exec(p, text):
    p = Path(p); p.parent.mkdir(parents=True, exist_ok=True); p.write_text(text); p.chmod(0o755)


class GuardFixture(unittest.TestCase):
    def setUp(self):
        self.assertEqual("1", os.environ.get(SANDBOX_FLAG), "run this file directly: it re-executes itself under sandbox-exec")
        self.tmpobj = tempfile.TemporaryDirectory(prefix="dd-guard-v2-")
        self.tmp = Path(self.tmpobj.name).resolve()
        self.root = self.tmp / "root"
        self.d1 = self.root / "AS" / "Deaths Demolition" / PRODUCT
        self.d2 = self.root / "AS" / BUNDLE
        self.pr = self.root / "PR"
        for d in (self.d1, self.d2, self.pr):
            d.mkdir(parents=True)
        self.p1 = self.pr / (BUNDLE + ".plist"); self.p2 = self.pr / (EDITOR_DOMAIN + ".plist")
        (self.d1 / "prototype-profile.json").write_text('{"editor": 1}\n')
        (self.d1 / "ui-settings.json").write_text('{"ui": 1}\n')
        (self.d2 / "prototype-profile.json").write_text('{"player": 1}\n')
        self.write_plist(self.p1, dict(KG_GOOD, PlayerKey="a"))
        self.write_plist(self.p2, {"EditorKey": "b"})
        self.tools = self.tmp / "tools"; self.tools.mkdir()
        for n in ("userdata-guard.sh", "userdata_guard_io.py", "userdata_attrib.py", "dd-env.sh", "slot.sh"):
            shutil.copy2(TOOLS / n, self.tools / n)
        self.guard = self.tools / "userdata-guard.sh"
        self.bin = self.tmp / "bin"
        write_exec(self.bin / "defaults", DEFAULTS_SHIM)
        write_exec(self.bin / "pgrep", PGREP_SHIM)
        write_exec(self.bin / "cp", CP_SHIM)
        write_exec(self.bin / "ps", PS_SHIM)
        write_exec(self.bin / "mv", MV_SHIM)
        write_exec(self.bin / "df", DF_SHIM)
        write_exec(self.bin / "sysctl", SYSCTL_SHIM)
        (self.tmp / "home").mkdir(); (self.tmp / "slots").mkdir()
        self.log = self.tmp / "shim.log"; self.log.write_text("")
        self.dir = self.tmp / "ug"
        self.procs = []
        self.assertFalse(str(self.root).startswith(pwhome() + "/Library"))
        self.assertEqual(str(self.bin / "defaults"), shutil.which("defaults", path=self.env()["PATH"]))

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:   # leader alive → its pid (= pgid of a start_new_session group) cannot be reused
                try:   # D8 kuyruk (test-engineer 06.10): a queued guard dies with its caller shell, no stray poller
                    os.killpg(p.pid, signal.SIGKILL)   # fixture groups only
                except (ProcessLookupError, PermissionError):
                    p.kill()   # not a group leader (shares the test's group): the process alone
            p.wait(timeout=10)
        for d in [self.dir] + list(self.tmp.glob("ug*")):
            (d / ".sampling").unlink(missing_ok=True)
        try:
            log = self.log.read_text()
            self.assertNotIn("FOREIGN", log, "a defaults call left the fixture root")
        finally:   # P5 test review NIT: the temp root goes even when the assert fails
            self.tmpobj.cleanup()

    # -- helpers ------------------------------------------------------------------------------------------------------
    def write_plist(self, path, d):
        with open(path, "wb") as f:
            plistlib.dump(d, f, fmt=plistlib.FMT_BINARY)

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("DD_", "SHIM_"))}
        e.update(HOME=str(self.tmp / "home"), TMPDIR=str(self.tmp), DD_GUARD_ROOT=str(self.root), DD_SLOT_BASE=str(self.tmp / "slots"),
                 DD_SLOT_POLL="1", DD_GUARD_WAIT="2", DD_GUARD_SLOT_WAIT="5", DD_GUARD_QUEUE_WAIT="30", PATH=f"{self.bin}:{os.environ['PATH']}",
                 SHIM_LOG=str(self.log), SHIM_PR=str(self.pr), SHIM_TMP=str(self.tmp))
        e[SANDBOX_FLAG] = "1"
        e.update(extra)
        return {k: v for k, v in e.items() if v is not None}

    def run_guard(self, *args, **env):
        r = subprocess.run(["zsh", str(self.guard), *map(str, args)], env=self.env(**env), capture_output=True, text=True,
                           timeout=120)
        return r.returncode, r.stdout, r.stderr

    def start_guard(self, *args, **env):
        p = subprocess.Popen(["zsh", str(self.guard), *map(str, args)], env=self.env(**env), stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, text=True, start_new_session=True)
        self.procs.append(p)
        return p

    def slot_entries(self):
        base = self.tmp / "slots"
        return sorted(str(p.relative_to(base)) for p in base.rglob("*") if p.is_file())

    def live_state(self):
        return {p: sha(p) for p in (self.d1 / "prototype-profile.json", self.d1 / "ui-settings.json",
                                    self.d2 / "prototype-profile.json", self.p1, self.p2)}

    def wait_file(self, p, seconds=20):
        end = time.time() + seconds
        while time.time() < end:
            if Path(p).exists():
                return True
            time.sleep(0.05)
        return False

    def holder(self, kind, tag):
        """A fixture shell that holds a slot until <tmp>/release-<tag> appears."""
        go = self.tmp / ("release-" + tag); held = self.tmp / ("held-" + tag)
        p = subprocess.Popen(["bash", "-c", f'"{self.tools}/slot.sh" acquire {kind} {tag} >/dev/null || exit 9; : > "{held}"; '
                              f'while [ ! -f "{go}" ]; do sleep 0.1; done; "{self.tools}/slot.sh" release {kind} {tag} >/dev/null'],
                             env=self.env(), start_new_session=True)
        self.procs.append(p)
        self.assertTrue(self.wait_file(held))
        return go


class SnapshotTests(GuardFixture):
    def assert_failed_clean(self):
        """INV-8: no before.txt, no .sampling, no player lock, no queue ticket."""
        self.assertFalse((self.dir / "before.txt").exists())
        self.assertFalse((self.dir / ".sampling").exists())
        self.assertFalse((self.dir / "snap").exists())
        self.assertEqual([], [e for e in self.slot_entries() if e.startswith(("player/", "queue/player/"))])

    def test_Snapshot_ArgvHatali_Cikis64DirYaratilmaz(self):
        for args in (["snapshot", self.dir, "--scope=player"], ["snapshot", self.dir, "extra"], ["snapshot", self.dir, "--scope", "x"]):
            with self.subTest(args=args):
                rc, out, err = self.run_guard(*args)
                self.assertEqual(64, rc, err)
                self.assertFalse(self.dir.exists())

    def test_Snapshot_Basarili_PencereVeOzetSatiri(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        win = json.loads((self.dir / "window.json").read_text())
        self.assertEqual(2, win["version"]); self.assertEqual("v2", win["protectedSet"]); self.assertEqual(["player"], win["windowAreas"])
        self.assertEqual("redirected", win["root"]); self.assertEqual("default", win["kgSource"])
        self.assertEqual(sha(self.guard), win["guardSha"]); self.assertEqual(sha(self.tools / "userdata_guard_io.py"), win["helperSha"])
        self.assertTrue(win["receipt"]["tag"].startswith("ug-")); self.assertTrue(win["receipt"]["owner"]["lstart"])
        last = out.strip().splitlines()[-1]
        self.assertIn("userdata-guard snapshot → ", last)
        for field in ("root=redirected", "kgSource=default", "guardSha=" + sha(self.guard)[:12], "window=" + win["id"]):
            self.assertIn(field, last)
        kg8 = hashlib.sha256(";".join("%s=%s" % kv for kv in KG_GOOD.items()).encode()).hexdigest()[:8]
        self.assertIn("kg=" + kg8, last)
        self.assertEqual((self.dir / "before.txt").read_text(),
                         (self.dir / "before-editor.txt").read_text() + (self.dir / "before-player.txt").read_text())
        self.assertFalse(list(self.dir.glob("*.tmp")))
        self.assertTrue((self.dir / "player-slot").is_file())

    def test_Snapshot_KgSapmasi_Cikis31SnapYokSlotBirakildi(self):
        self.write_plist(self.p1, dict(KG_GOOD, **{"Screenmanager Fullscreen mode": 3}))
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(31, rc, out + err)
        kg = list(self.dir.glob("failed-*/known-good-deviation.txt"))   # D5 review NIT-5: kept with the failed snapshot
        self.assertTrue(kg, out)
        self.assertIn("Screenmanager Fullscreen mode=3", kg[0].read_text())
        self.assert_failed_clean()

    def test_Snapshot_CanliPlayer_Cikis30SlotBirakildi(self):
        (self.tmp / "pgrep-player").write_text("4242 /fixture/Player\n")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "all")
        self.assertEqual(30, rc, out + err)
        self.assert_failed_clean()

    def test_Snapshot_EditorKapsamCanliPlayer_Cikis30Degil(self):
        (self.tmp / "pgrep-player").write_text("4242 /fixture/Player\n")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor")
        self.assertEqual(0, rc, out + err)
        self.assertNotIn("pgrep", self.log.read_text())   # INV-S1: editor scope never asks
        self.assertIsNone(json.loads((self.dir / "window.json").read_text())["receipt"])

    def test_Snapshot_KilitZamanAsimi_Cikis75SlotBirakildiBeforeYok(self):
        unity = self.tmp / "Unity.app/Contents/MacOS/Unity"; unity.parent.mkdir(parents=True); unity.symlink_to("/bin/sleep")
        p = subprocess.Popen([str(unity), "60"], start_new_session=True); self.procs.append(p)
        (self.d2 / "prototype-profile.json.lock").write_text("pid=%d\nname=fixture\n" % p.pid)
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(75, rc, out + err)
        self.assertIn("profile lock still held", out)
        self.assert_failed_clean()

    def test_Snapshot_UcKezDegisim_Cikis75BeforeYok(self):
        (self.tmp / "cp-mutate").write_text("")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "all")
        self.assertEqual(75, rc, out + err)
        self.assertIn("changed during the snapshot 3 times", out)
        self.assert_failed_clean()
        self.assertTrue(list(self.dir.glob("failed-*/live-at-snapshot.txt")))

    def test_Snapshot_SlotZamanAsimi_Cikis75BiletYok(self):
        go = self.holder("player", "other-session")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player", DD_GUARD_SLOT_WAIT="3")
        self.assertEqual(75, rc, out + err)
        self.assertEqual([], [e for e in self.slot_entries() if e.startswith("queue/player/")])
        self.assertFalse((self.dir / "before.txt").exists())
        go.write_text("")

    def test_Snapshot_SlotBeklerkenTerm_Cikis143BiletVeSnapYok(self):
        go = self.holder("player", "other-session")
        p = self.start_guard("snapshot", self.dir, "--scope", "player", DD_GUARD_SLOT_WAIT="60")
        self.assertTrue(self.wait_file(self.dir / "player-slot.out"))
        end = time.time() + 10
        while time.time() < end and not [e for e in self.slot_entries() if e.startswith("queue/player/")]:
            time.sleep(0.1)
        os.kill(p.pid, signal.SIGTERM)
        out, err = p.communicate(timeout=30)
        self.assertEqual(143, p.returncode, out + err)
        self.assertEqual([], [e for e in self.slot_entries() if e.startswith("queue/player/")])
        self.assertFalse((self.dir / "snap").exists()); self.assertFalse((self.dir / "before.txt").exists())
        go.write_text("")

    def test_Snapshot_KopyadaHup_FailedKlasoruSlotBirakildi(self):
        (self.tmp / "cp-slow").write_text("2")
        p = self.start_guard("snapshot", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "cp-started"))
        os.kill(p.pid, signal.SIGHUP)
        out, err = p.communicate(timeout=60)
        self.assertEqual(129, p.returncode, out + err)
        failed = list(self.dir.glob("failed-*"))
        self.assertEqual(1, len(failed), out)
        self.assertTrue((failed[0] / "snap").is_dir())
        self.assert_failed_clean()

    def test_Snapshot_CommitOncesiOldurulur_Restore64BaytDegismez(self):
        before = self.live_state()
        (self.tmp / "cp-slow").write_text("5")
        p = self.start_guard("snapshot", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "cp-started"))
        os.killpg(p.pid, signal.SIGKILL)   # the fixture guard's own group
        p.communicate(timeout=30)
        (self.dir / ".sampling").unlink(missing_ok=True)
        self.log.write_text("")
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player")
        self.assertEqual(64, rc, out + err)
        self.assertEqual(before, self.live_state())
        self.assertNotIn("defaults import", self.log.read_text())
        self.assertNotIn("defaults delete", self.log.read_text())

    def test_Snapshot_BasarisizSonraAyniDir_Cikis0(self):
        (self.tmp / "pgrep-player").write_text("4242 /fixture/Player\n")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(30, rc, out + err)
        (self.tmp / "pgrep-player").unlink()
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertTrue((self.dir / "before.txt").is_file())

    def test_Snapshot_CasefoldVaryant_Cikis65(self):
        (self.d2 / "prototype-profile.json").rename(self.d2 / "Prototype-Profile.json")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(65, rc, out + err)
        self.assertIn("case-folded name variant", out)
        self.assert_failed_clean()

    def test_Snapshot_SymlinkYol_Cikis65(self):
        real = self.tmp / "elsewhere.json"; real.write_text("{}\n")
        (self.d1 / "ui-settings.json").unlink(); (self.d1 / "ui-settings.json").symlink_to(real)
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "all")
        self.assertEqual(65, rc, out + err)
        self.assertIn("symlink", out)
        self.assert_failed_clean()

    def test_Snapshot_OkunamayanPlist_Cikis65(self):
        self.p2.write_bytes(b"not a plist")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "all")
        self.assertEqual(65, rc, out + err)
        self.assertIn("unreadable plist", out)

    def test_Snapshot_D2UiDosyalari_KopyalanirBeforePlayerda(self):
        (self.d2 / "ui-settings.json").write_text('{"player-ui": 1}\n')
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertTrue((self.dir / "snap/D2/ui-settings.json").is_file())
        self.assertIn("%s  %s" % (sha(self.d2 / "ui-settings.json"), self.d2 / "ui-settings.json"),
                      (self.dir / "before-player.txt").read_text())

    def test_Snapshot_DdGuardRootGoreliSymlinkYokGercekLibrary_Cikis64(self):
        link = self.tmp / "rootlink"; link.symlink_to(self.root)
        for root in ("relative/root", str(link), str(self.tmp / "nope"), pwhome() + "/Library"):
            with self.subTest(root=root):
                rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player", DD_GUARD_ROOT=root)
                self.assertEqual(64, rc, err)
                self.assertFalse(self.dir.exists())
        self.assertEqual("", self.log.read_text())

    def test_Snapshot_HomeSahteRootBos_Cikis64DefaultsCagrisiYok(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player", DD_GUARD_ROOT=None)
        self.assertEqual(64, rc, err)
        self.assertIn("is not the real home", err)
        self.assertEqual("", self.log.read_text())
        self.assertFalse(self.dir.exists())

    def test_Snapshot_BeforeVarSnapYok_Cikis64(self):
        for name in ("before.txt", "window.json", "snapshot-manifest.json", "sampler.json"):
            with self.subTest(name=name):
                shutil.rmtree(self.dir, ignore_errors=True); self.dir.mkdir()
                (self.dir / name).write_text("x\n")
                rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
                self.assertEqual(64, rc, err)
                self.assertNotIn("snap", os.listdir(self.dir))
        self.assertEqual([], [e for e in self.slot_entries() if e.startswith("player/")])

    def test_Snapshot_CommitBasarisiz_Cikis75BeforeYokSlotBirakildi(self):
        (self.tmp / "mv-fail-before").write_text("")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(75, rc, out + err)
        self.assertIn("commit failed", out)
        self.assert_failed_clean()
        self.assertFalse((self.dir / "before-editor.txt").exists()); self.assertFalse((self.dir / "before-player.txt").exists())
        failed = list(self.dir.glob("failed-*"))
        self.assertTrue(failed and (failed[0] / "before-player.txt").is_file())

    def test_Snapshot_KgS4SonrasiDegisir_KopyaReddedilir31(self):
        (self.tmp / "cp-kg").write_text("")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(31, rc, out + err)
        self.assertIn("copied player display values deviate", out)
        self.assert_failed_clean()

    def test_Snapshot_DirSymlink_Cikis64(self):
        real = self.tmp / "real-ug"; real.mkdir(); self.dir.symlink_to(real)
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(64, rc, err)
        self.assertEqual([], list(real.iterdir()))

    def test_Sampler_LineageSahipsizPlayerOwned0(self):
        io = self.tools / "userdata_guard_io.py"
        mark = "dd-fixture-lineage-%d" % os.getpid()
        own = subprocess.Popen(["/bin/sh", "-c", "exec -a %s-own sleep 30" % mark]); self.procs.append(own)
        # double fork: the foreign sleeper's parent exits, it is reparented to launchd (no owner ancestor)
        subprocess.run(["/bin/sh", "-c", "(exec -a %s-foreign sleep 30 &)" % mark], check=True)
        self.addCleanup(lambda: [os.kill(int(p), signal.SIGTERM) for p in subprocess.run(
            ["pgrep", "-f", "%s-foreign" % mark], capture_output=True, text=True).stdout.split()])   # our own fixture only
        time.sleep(0.5)
        self.dir.mkdir()
        subprocess.run([sys.executable, str(io), "sample", str(self.dir), mark, str(os.getpid())], check=True, env=self.env())
        rows = [l.split("\t") for l in (self.dir / "players-lineage.tsv").read_text().splitlines()]
        owned = {r[1]: r[4] for r in rows}
        self.assertEqual("1", owned.get(str(own.pid)), rows)
        self.assertIn("0", owned.values(), rows)
        self.assertTrue(all(len(r) == 5 and r[3] for r in rows))
        log = (self.dir / "players.log").read_text().splitlines()
        self.assertTrue(all("\t" in l for l in log))


class RestoreD5Tests(GuardFixture):
    """D5's restore side: the v2 player set round-trips; a window without window.json keeps the legacy set (INV-5)."""

    def test_V2Pencere_D2UiDosyasiGeriYuklenir_OzetAlanlari(self):
        (self.d2 / "ui-settings.json").write_text('{"player-ui": 1}\n')
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        win = json.loads((self.dir / "window.json").read_text())
        (self.d2 / "ui-settings.json").write_text('{"player-ui": 2}\n')
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player-ui": 1}\n', (self.d2 / "ui-settings.json").read_text())
        last = out.strip().splitlines()[-1]
        self.assertIn("verified=1", last); self.assertIn("window=" + win["id"], last); self.assertIn("root=redirected", last)

    def test_EskiPencere_PencereJsonYok_D2UiDosyasinaDokunulmaz(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        (self.dir / "window.json").unlink()   # a window an older guard took: legacy player set
        for f in ("before.txt", "before-player.txt"):
            p = self.dir / f
            p.write_text("".join(l for l in p.read_text().splitlines(True) if "/ui-settings.json" not in l or str(self.d1) in l))
        (self.d2 / "ui-settings.json").write_text('{"made-later": 1}\n')   # not recorded by the legacy window
        (self.d2 / "prototype-profile.json").write_text('{"player": 2}\n')
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"made-later": 1}\n', (self.d2 / "ui-settings.json").read_text())   # not moved to created/
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertIn("window=legacy", out.strip().splitlines()[-1])

    def test_Restore_BaskaKok_Cikis64HicbirSeyYazilmaz(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        other = self.tmp / "root2"; shutil.copytree(self.root, other)
        (other / "AS" / BUNDLE / "prototype-profile.json").write_text('{"other-root": 1}\n')
        self.log.write_text("")
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player", DD_GUARD_ROOT=str(other))
        self.assertEqual(64, rc, out + err)
        self.assertIn("another user root", err)
        self.assertEqual('{"other-root": 1}\n', (other / "AS" / BUNDLE / "prototype-profile.json").read_text())
        self.assertNotIn("defaults import", self.log.read_text()); self.assertNotIn("defaults delete", self.log.read_text())

    def test_PencereJsonProtectedSetYok_EskiKume(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        w = json.loads((self.dir / "window.json").read_text()); w.pop("protectedSet")
        (self.dir / "window.json").write_text(json.dumps(w))
        (self.d2 / "ui-settings.json").write_text('{"made-later": 1}\n')
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"made-later": 1}\n', (self.d2 / "ui-settings.json").read_text())   # legacy set: not touched

    def test_Restore_SahteHome_Cikis64HicbirSeyYazilmaz(self):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.log.write_text("")
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "player", DD_GUARD_ROOT=None)
        self.assertEqual(64, rc, err)
        self.assertEqual("", self.log.read_text())


class RestoreBase(GuardFixture):
    """helpers shared by the restore test classes (no tests of its own)."""

    def snap(self, scope="all"):
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", scope)
        self.assertEqual(0, rc, out + err)
        (self.tmp / "pgrep-count").unlink(missing_ok=True)
        return json.loads((self.dir / "window.json").read_text())

    def restore(self, scope="all", **env):
        return self.run_guard("restore", self.dir, "--scope", scope, **env)

    def ledger(self, wid):
        return self.root / "ledger" / "quarantine" / wid

    def verdict(self):
        return json.loads((self.dir / "guard-verdict.json").read_text())

    def run_changes(self):
        """what a run leaves: D1 ui changed, a new D1 file, D2 profile changed, a P1 and a P2 key."""
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        (self.d1 / "load-timing.json").write_text('{"new": 1}\n')
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')
        d = load_plist(self.p1); d["RunKey"] = 1; self.write_plist(self.p1, d)
        d = load_plist(self.p2); d["EditorKey"] = "changed"; self.write_plist(self.p2, d)

    def hook(self, script):
        write_exec(self.tmp / "pgrep-hook", "#!/bin/sh\n" + script)

    def player_changes(self):
        """only the player area changes (a player-scope restore then has no OTHER)."""
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')
        d = load_plist(self.p1); d["RunKey"] = 1; self.write_plist(self.p1, d)

    def lock_d2(self, folder=None):
        unity = self.tmp / "Unity.app/Contents/MacOS/Unity"
        if not unity.exists():
            unity.parent.mkdir(parents=True); unity.symlink_to("/bin/sleep")
        # P5 test review SHOULD-5: 900 s, not 120 — a holder that must outlive a whole restore under load (unlock_d2 /
        # tearDown end it)
        p = subprocess.Popen([str(unity), "900"], start_new_session=True); self.procs.append(p)
        (folder or self.d2).joinpath("prototype-profile.json.lock").write_text("pid=%d\nname=fixture\n" % p.pid)
        return p

    def unlock_d2(self, p, folder=None):
        p.kill(); p.wait(timeout=10); (folder or self.d2).joinpath("prototype-profile.json.lock").unlink()


class RestoreWriteTests(RestoreBase):
    """G4 D6 (design v3.1 §3 D6): the restore write path — exp / first / live per path, CAS, quarantine, rename swap."""

    def test_Restore_MutluYol_UcKapsamCreatedKarantinada(self):
        for scope in ("editor", "player", "all"):
            with self.subTest(scope=scope):
                shutil.rmtree(self.dir, ignore_errors=True)
                before = self.live_state()
                w = self.snap(scope)
                self.run_changes()
                rc, out, err = self.restore(scope)
                self.assertEqual(0 if scope == "all" else 32, rc, out + err)   # G4 D7: the other area changed too → OTHER
                last = out.strip().splitlines()[-1]
                self.assertIn("verified=1", last); self.assertIn("snapshotId=", last); self.assertIn("retry=none", last)
                state = self.live_state()
                if scope in ("editor", "all"):
                    self.assertEqual(before[self.d1 / "ui-settings.json"], state[self.d1 / "ui-settings.json"])
                    self.assertFalse((self.d1 / "load-timing.json").exists())
                    created = self.ledger(w["id"]) / "created" / "editor" / "load-timing.json.1"
                    self.assertEqual('{"new": 1}\n', created.read_text())
                    self.assertTrue((self.ledger(w["id"]) / "displaced" / "editor" / "ui-settings.json.1").is_file())
                    self.assertEqual("b", load_plist(self.p2)["EditorKey"])
                if scope in ("player", "all"):
                    self.assertEqual(before[self.d2 / "prototype-profile.json"], state[self.d2 / "prototype-profile.json"])
                    self.assertNotIn("RunKey", load_plist(self.p1))
                    self.assertTrue(list((self.ledger(w["id"]) / "displaced" / "PR").glob("p1.plist.*")))
                index = (self.root / "ledger" / "INDEX.tsv").read_text()
                self.assertIn(w["id"], index)
                self.assertEqual(str(self.root / "ledger" / "quarantine" / w["id"]) + "/", (self.dir / "quarantine.txt").read_text().strip())
                self.assertTrue(self.verdict()["verified"])
                # leave the next scope a clean start
                for f in (self.d1 / "load-timing.json",):
                    f.unlink(missing_ok=True)
                (self.d1 / "ui-settings.json").write_text('{"ui": 1}\n'); (self.d2 / "prototype-profile.json").write_text('{"player": 1}\n')
                self.write_plist(self.p1, dict(KG_GOOD, PlayerKey="a")); self.write_plist(self.p2, {"EditorKey": "b"})

    def test_Restore_ExportHatasi_DosyalarGeriCikis30(self):
        before = self.live_state(); self.snap()
        self.run_changes()
        (self.tmp / "defaults-fail-export").write_text("")
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        self.assertIn("RunKey", load_plist(self.p1))   # not written without a kept copy (INV-4)
        self.assertIn("WRITE_FAILED", self.verdict()["classes"])

    def test_Restore_DeleteImportHatasi_DigerleriGeriCikis30(self):
        before = self.live_state(); self.snap()
        self.run_changes()
        (self.tmp / "defaults-fail-delete").write_text(""); (self.tmp / "defaults-fail-import").write_text("")
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertEqual(before[self.d1 / "ui-settings.json"], sha(self.d1 / "ui-settings.json"))
        self.assertIn("WRITE_FAILED", self.verdict()["classes"])

    def test_Restore_SettleZamanAsimi_Verified0(self):
        self.snap(); self.run_changes()
        (self.tmp / "defaults-export-stale").write_text("")
        rc, out, err = self.restore("editor")
        self.assertEqual(30, rc, out + err)
        self.assertIn("verified=0", out.strip().splitlines()[-1])

    def test_Restore_BagKurallari_LiveFarkliVeyaBeforeYok_Cikis64(self):
        self.snap()
        live = self.dir / "live-at-snapshot.txt"
        live.write_text(live.read_text().replace(sha(self.d1 / "ui-settings.json"), "0" * 64))
        rc, out, err = self.restore()
        self.assertEqual(64, rc, err); self.assertIn("not bound", err)
        (self.dir / "before.txt").unlink()
        rc, out, err = self.restore()
        self.assertEqual(64, rc, err); self.assertIn("no snapshot", err)

    def test_Restore_BagKurallari_DigerAlanFarki_BilgiSatiri(self):
        self.snap("editor")
        live = self.dir / "live-at-snapshot.txt"
        live.write_text(live.read_text().replace(sha(self.d2 / "prototype-profile.json"), "0" * 64))
        rc, out, err = self.restore("editor")
        self.assertEqual(0, rc, out + err)   # C-00 05.10: only the window's own area binds
        self.assertIn("an area outside the window changed", out)

    def test_Restore_V1Izi_Cikis64Mesaj(self):
        self.snap(); (self.dir / "snapshot-manifest.json").write_text("{}")
        rc, out, err = self.restore()
        self.assertEqual(64, rc, err); self.assertIn("v1 (74f62dd6)", err)

    def test_Restore_SnapKopyaBozuk_YalnizOYolSnapcorrupt(self):
        before = self.live_state(); self.snap(); self.run_changes()
        (self.dir / "snap/D1/ui-settings.json").write_text("corrupt\n")
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"ui": 99}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        self.assertIn("SNAPCORRUPT", self.verdict()["classes"])

    def test_Restore_SnapD1Silinmis_CanliDosyaTasinmaz(self):
        self.snap(); self.run_changes()
        (self.dir / "snap/D1/ui-settings.json").unlink()
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertTrue((self.d1 / "ui-settings.json").exists())

    def test_Restore_BozukP1_YalnizP1Reddedilir(self):
        before = self.live_state(); self.snap(); self.run_changes()
        (self.dir / "snap/PR/p1.plist").write_bytes(b"broken")
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertIn("RunKey", load_plist(self.p1))
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))

    def test_Restore_Symlink_YalnizOYolReddedilir(self):
        before = self.live_state(); self.snap(); self.run_changes()
        real = self.tmp / "elsewhere.json"; real.write_text("x\n")
        (self.d1 / "ui-settings.json").unlink(); (self.d1 / "ui-settings.json").symlink_to(real)
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertTrue((self.d1 / "ui-settings.json").is_symlink()); self.assertEqual("x\n", real.read_text())
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))

    def test_Restore_AlanDiziniSymlinkOldu_Cikis30Yazimsiz(self):
        self.snap(); self.run_changes()
        moved = self.tmp / "d2-real"; self.d2.rename(moved); self.d2.symlink_to(moved)
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"player": 99}\n', (moved / "prototype-profile.json").read_text())
        self.assertIn("SYMLINK", self.verdict()["classes"])

    def test_Restore_Chmod0500D1_DigerleriDenenirCikis30(self):
        before = self.live_state(); self.snap(); self.run_changes()
        self.d1.chmod(0o500)
        try:
            rc, out, err = self.restore()
        finally:
            self.d1.chmod(0o755)
        self.assertEqual(30, rc, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        self.assertIn("WRITE_FAILED", self.verdict()["classes"])

    def test_Restore_KarantinaYazilamaz_YolYazilmaz(self):
        self.snap(); self.run_changes()
        q = self.root / "ledger" / "quarantine"   # G4 D8: ledger/ itself already holds the window record
        q.mkdir(parents=True, exist_ok=True); q.chmod(0o500)
        try:
            rc, out, err = self.restore("editor")
        finally:
            q.chmod(0o755)
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"ui": 99}\n', (self.d1 / "ui-settings.json").read_text())   # no kept copy → not written

    def test_Restore_YazimOncesiDegisim_Cikis75Yazimsiz(self):
        self.snap("player")
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')   # the area's only change: nothing else written
        # call 1 = R7 gate, call 2 = the first CAS: the user saves right there
        self.hook('[ "$1" = 2 ] && printf "%%s" "{\\"user\\": 1}" > "%s"\n' % (self.d2 / "prototype-profile.json"))
        rc, out, err = self.restore("player")
        self.assertEqual(75, rc, out + err)
        self.assertIn("retry=same-dir", out.strip().splitlines()[-1])
        self.assertEqual('{"user": 1}', (self.d2 / "prototype-profile.json").read_text())
        self.assertIn("RACED-EARLY", self.verdict()["classes"])

    def test_Restore_YazimOrtasiDegisim_Cikis30Displaced(self):
        self.snap("player"); self.run_changes()
        (self.d2 / "ui-settings.json").write_text('{"made": 1}\n')   # a second (created) path, written before the primary
        self.hook('[ "$1" = 3 ] && printf "%%s" "{\\"user\\": 1}" > "%s"\n' % (self.d2 / "prototype-profile.json"))
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user": 1}', (self.d2 / "prototype-profile.json").read_text())
        self.assertIn("RACED-LATE", self.verdict()["classes"])
        w = json.loads((self.dir / "window.json").read_text())
        self.assertTrue((self.ledger(w["id"]) / "created" / "player" / "ui-settings.json.1").is_file())   # the write before the race

    def test_Restore_YazimdaPgrepCanli_Raced(self):
        self.snap("player"); self.run_changes()
        self.hook('[ "$1" = 2 ] && echo "4242 /fixture/Player" > "%s/pgrep-player"\n' % self.tmp)
        rc, out, err = self.restore("player")
        self.assertEqual(75, rc, out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_Restore_AyniDirIkiRestore_Biri75(self):
        self.snap()
        import fcntl
        lk = open(self.dir / ".restore.lock", "a"); fcntl.lockf(lk, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            rc, out, err = self.restore()
        finally:
            lk.close()
        self.assertEqual(75, rc, out + err); self.assertTrue(out.strip().splitlines()[-1].endswith("retry=same-dir class=LOCKED"), out)
        rc, out, err = self.restore()
        self.assertEqual(0, rc, out + err)

    def test_Restore_TamamlanmisAlanKullaniciDegistirdi_Stale30Yazimsiz(self):
        self.snap(); self.run_changes()
        rc, out, err = self.restore(); self.assertEqual(0, rc, out + err)
        (self.d1 / "ui-settings.json").write_text('{"user-later": 1}\n')
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user-later": 1}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertIn("STALE", self.verdict()["classes"])

    def test_Restore_Busy75SonraTekrar_Yakinsar(self):
        before = self.live_state(); self.snap("player"); self.player_changes()   # G4 D7: player-only, no OTHER (32)
        p = self.lock_d2()
        rc, out, err = self.restore("player"); self.assertEqual(75, rc, out + err)
        self.assertIn("retry=same-dir", out.strip().splitlines()[-1])
        self.unlock_d2(p)
        rc, out, err = self.restore("player"); self.assertEqual(0, rc, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        self.assertTrue((self.dir / "after-run.txt.2").is_file())

    def test_Restore_Busy75KullaniciKaydetti_TekrarStaleKullaniciBaytiKalir(self):
        self.snap("player"); self.run_changes()
        p = self.lock_d2()
        rc, out, err = self.restore("player"); self.assertEqual(75, rc, out + err)
        self.unlock_d2(p)
        (self.d2 / "prototype-profile.json").write_text('{"user": 2}\n')
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user": 2}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_Restore_KilitBeklerkenTerm_HemenYazarInterrupted30(self):
        before = self.live_state(); self.snap(); self.run_changes()
        p = self.lock_d2()
        g = self.start_guard("restore", self.dir, "--scope", "all", DD_GUARD_WAIT="60")
        self.assertTrue(self.wait_file(self.dir / "after-run-editor.txt") or True)
        time.sleep(3)
        os.kill(g.pid, signal.SIGTERM)
        out, err = g.communicate(timeout=60)
        self.assertEqual(30, g.returncode, out + err)
        self.assertEqual(before[self.d1 / "ui-settings.json"], sha(self.d1 / "ui-settings.json"))   # editor written
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())     # player BUSY
        self.assertIn("INTERRUPTED", self.verdict()["classes"])
        self.unlock_d2(p)

    def test_Restore_YazimdaSurecGrubunaInt_YazimBiter(self):
        before = self.live_state(); self.snap("player"); self.player_changes()   # G4 D7: player-only, no OTHER (32)
        self.hook('[ "$1" = 2 ] && { : > "%s/in-write"; sleep 3; }\n' % self.tmp)
        g = self.start_guard("restore", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "in-write"))
        os.killpg(g.pid, signal.SIGINT)
        out, err = g.communicate(timeout=60)
        self.assertEqual(0, g.returncode, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))

    def test_Restore_YazimdaSigkill_TekrarYakinsar(self):
        before = self.live_state(); self.snap("player"); self.player_changes()   # G4 D7: player-only, no OTHER (32)
        self.hook('[ "$1" = 2 ] && { : > "%s/in-write"; sleep 30; }\n' % self.tmp)
        g = self.start_guard("restore", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "in-write"))
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)
        (self.tmp / "pgrep-hook").unlink()
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))

    def test_Restore_PlistDeleteSonrasiSigkill_TekrarYakinsar(self):
        self.snap("player"); self.player_changes()   # G4 D7: player-only, no OTHER (32)
        (self.tmp / "defaults-import-sleep").write_text("")
        g = self.start_guard("restore", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "import-started", 60))
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)
        (self.tmp / "defaults-import-sleep").unlink()
        self.assertNotIn("RunKey", load_plist(self.p1))   # deleted, import never ran
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)   # MUST-5: our own half-done write is finished
        d = load_plist(self.p1)
        self.assertEqual("a", d.get("PlayerKey")); self.assertNotIn("RunKey", d)

    def test_Restore_PlistYabanciDeger_Stale30DisplacedVar(self):
        w = self.snap("player"); self.run_changes()
        (self.tmp / "defaults-import-sleep").write_text("")
        g = self.start_guard("restore", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "import-started", 60))
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)
        (self.tmp / "defaults-import-sleep").unlink()
        d = load_plist(self.p1); d["UserKey"] = "user"; self.write_plist(self.p1, d)
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual("user", load_plist(self.p1).get("UserKey"))
        self.assertTrue(list((self.ledger(w["id"]) / "displaced" / "PR").glob("p1.plist.*")))

    def test_Restore_DegisiklikYok_IkiRestoreSifir(self):
        self.snap()
        for _ in range(2):
            rc, out, err = self.restore()
            self.assertEqual(0, rc, out + err)
            self.assertIn("touched=0 verified=1", out.strip().splitlines()[-1])

    def test_Restore_YalnizVolatile_SayacIceAlinmaz(self):
        self.snap("player")
        d = load_plist(self.p1); d["unity.player_session_count"] = 77; self.write_plist(self.p1, d)
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual(77, load_plist(self.p1)["unity.player_session_count"])

    def test_Restore_Kg31SonraTekrar_KullaniciKaydiStale(self):
        self.snap("player"); self.run_changes()
        good = (self.dir / "snap/PR/p1.plist").read_bytes()
        d = plistlib.loads(good); d["Screenmanager Fullscreen mode"] = 3
        (self.dir / "snap/PR/p1.plist").write_bytes(plistlib.dumps(d))
        rc, out, err = self.restore("player"); self.assertEqual(31, rc, out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())   # nothing written
        (self.dir / "snap/PR/p1.plist").write_bytes(good)   # C-00 resolves the snapshot
        (self.d2 / "prototype-profile.json").write_text('{"user": 3}\n')   # the user saved meanwhile
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user": 3}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_Karantina_IndexSatiriVeDirIsaretcisi_TmpUyarisi(self):
        w = self.snap(); self.run_changes()
        rc, out, err = self.restore()
        self.assertEqual(0, rc, out + err)
        self.assertIn("is under a temporary folder", out + err)
        rows = [l.split("\t") for l in (self.root / "ledger" / "INDEX.tsv").read_text().splitlines()]
        self.assertTrue(rows)
        for r in rows:
            self.assertEqual(7, len(r), r)
            self.assertEqual((w["id"], str(self.dir)), (r[1], r[2]))
            self.assertIn(r[5], ("created", "displaced", "tmp-sweep"))
        created = [r for r in rows if r[5] == "created"]
        self.assertEqual([str(self.d1 / "load-timing.json")], [r[3] for r in created])
        self.assertEqual(hashlib.sha256(b'{"new": 1}\n').hexdigest(), created[0][4])
        q = Path((self.dir / "quarantine.txt").read_text().strip())
        self.assertTrue((q / "created").is_dir() and (q / "displaced").is_dir())

    def test_Restore_HicbirYolda_KokDisiYazimYok(self):
        """No write outside the fixture root, proven without the user's real plist (C-00 06.10: comparing it was not
        hermetic — another session's guarded run may save it meanwhile, a false red on 06.10):
        (1) the suite's sandbox denies a write under the REAL ~/Library — the guard and its helpers inherit it. Probe: open
            an always-present real file for writing without O_CREAT and write nothing (unsandboxed it opens and changes
            nothing; sandboxed: EPERM);
        (2) a sentinel plist where a $HOME-relative domain would land (the fixture HOME) stays as it was;
        (3) every `defaults` call names the fixture root (absolute domain)."""
        probe = Path(pwhome()) / "Library" / "Preferences" / ".GlobalPreferences.plist"
        if probe.is_file():
            try:
                os.close(os.open(str(probe), os.O_WRONLY | os.O_APPEND))   # nothing is written
            except PermissionError:
                pass
            else:
                self.fail("the sandbox does not deny writes under the real ~/Library — the suite is not isolated")
        sentinel = Path(self.env()["HOME"]) / "Library" / "Preferences" / (BUNDLE + ".plist")
        sentinel.parent.mkdir(parents=True, exist_ok=True)
        sentinel.write_bytes(plistlib.dumps({"sentinel": 1}, fmt=plistlib.FMT_BINARY))
        before = sha(sentinel)
        self.snap(); self.run_changes()
        rc, out, err = self.restore()
        self.assertEqual(0, rc, out + err)
        for line in self.log.read_text().splitlines():
            if line.startswith("defaults "):
                self.assertIn(str(self.root), line)   # every defaults call names the fixture root (absolute domain)
        self.assertEqual(before, sha(sentinel))

    def test_Restore_CasRenameArasiDegisim_TakasDisplaced30(self):
        """SHOULD-3: bytes saved between the last CAS and the swap come back out of the swap and are kept (displaced, RACED)."""
        import importlib.util
        w = self.snap("player"); self.run_changes()
        p = self.lock_d2()
        rc, out, err = self.restore("player"); self.assertEqual(75, rc, out + err)   # writes the first after-run files
        self.unlock_d2(p)
        spec = importlib.util.spec_from_file_location("ugio", self.tools / "userdata_guard_io.py")
        io = importlib.util.module_from_spec(spec); spec.loader.exec_module(io)
        primary = str(self.d2 / "prototype-profile.json")
        real_live_sha, calls = io.live_sha, {"n": 0}

        def live_sha(path):
            if path != primary:
                return real_live_sha(path)
            calls["n"] += 1
            v = real_live_sha(path)
            if calls["n"] == 3:   # the CAS right before the swap: it sees "first", then the user saves
                Path(path).write_text('{"user-mid": 1}\n')
            return v
        io.live_sha = live_sha
        cfg = {"dir": str(self.dir), "area": "player", "id": w["id"], "ledger": str(self.root / "ledger"), "folder": str(self.d2),
               "names": list(FILES), "plist": str(self.p1), "domain": str(self.pr / BUNDLE), "pxName": "p1",
               "snapFolder": str(self.dir / "snap" / "D2"), "snapPlist": str(self.dir / "snap" / "PR" / "p1.plist"),
               "before": str(self.dir / "before-player.txt"), "first": str(self.dir / "after-run-player.txt"),
               "volatile": ["unity.player_session_count", "unity.player_sessionid"],
               "playerRe": "Contents/MacOS/(Deaths Demolition|DeathsDemolition)", "checkPlayer": True,
               "rootAS": str(self.root / "AS"), "rootPR": str(self.pr), "guardPid": os.getpid()}
        saved = dict(os.environ); os.environ.update(self.env())
        try:
            res = io.restore_area(cfg)
        finally:
            os.environ.clear(); os.environ.update(saved)
        self.assertEqual(3, calls["n"])
        self.assertIn("RACED-LATE", res["classes"])
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())   # the snapshot went in
        kept = [Path(q).read_text() for e in res["paths"] for q in e.get("quarantine", [])]
        self.assertIn('{"user-mid": 1}\n', kept)   # the user's bytes are kept, never lost
        self.assertIn("RACED", (self.root / "ledger" / "INDEX.tsv").read_text())

    # --- D6 reviews (data loss + concurrency, 05.10) ------------------------------------------------------------------
    def test_Inceleme_TamYazimSonrasiKullaniciPlist_Stale30(self):
        """data-loss MUST-1: a finished plist write leaves no half-done marker, so a later user save is STALE, not MUST-5."""
        self.snap(); self.run_changes()
        p = self.lock_d2(self.d1)
        rc, out, err = self.restore(); self.assertEqual(75, rc, out + err)   # editor BUSY, player written in full
        self.assertNotIn("RunKey", load_plist(self.p1))
        self.assertFalse((self.dir / ".plist-p1.writing").exists())
        self.unlock_d2(p, self.d1)
        d = load_plist(self.p1); d["RunKey"] = 1; self.write_plist(self.p1, d)   # the user sets a value the run had set
        rc, out, err = self.restore()
        self.assertEqual(30, rc, out + err)
        self.assertEqual(1, load_plist(self.p1).get("RunKey"))
        self.assertIn("STALE", self.verdict()["classes"])

    def test_Inceleme_PlistYazimOncesiDegisim_RacedKullaniciDegeriKalir(self):
        """data-loss MUST-2 / concurrency SHOULD-1: the plist CAS right before the write."""
        w = self.snap("player"); self.run_changes()
        # pgrep calls: 1 R7 gate, 2–3 the profile's two CAS, 4 the plist's Player check — the user saves right there
        self.hook('[ "$1" = 4 ] && /usr/bin/python3 -c "import plistlib,sys; p=sys.argv[1]; d=plistlib.load(open(p,\'rb\')); '
                  'd[\'PlayerKey\']=\'user\'; plistlib.dump(d,open(p,\'wb\'),fmt=plistlib.FMT_BINARY)" "%s"\n' % self.p1)
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual("user", load_plist(self.p1)["PlayerKey"])
        self.assertIn("RACED-LATE", self.verdict()["classes"])
        kept = [load_plist(f) for f in (self.ledger(w["id"]) / "displaced" / "PR").glob("p1.plist.*")]
        self.assertIn("user", [k.get("PlayerKey") for k in kept])

    def test_Inceleme_AfterRunYayiniBasarisiz_Yazimsiz_TekrardaKullaniciKaydiKalir(self):
        """data-loss SHOULD-1 / concurrency MUST-1: no committed first → no write phase, and never a guessed first later."""
        self.snap("player"); self.run_changes()
        write_exec(self.bin / "ln", '#!/bin/sh\nfor a in "$@"; do last="$a"; done\n'
                   'case "$last" in */after-run-player.txt) exit 1 ;; esac\nexec /bin/ln "$@"\n')
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertFalse((self.dir / "after-run.txt").exists())
        (self.bin / "ln").unlink()
        (self.d2 / "prototype-profile.json").write_text('{"user": 5}\n')
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user": 5}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertIn("no first to trust", out + err)

    def test_Inceleme_YetimYazimYardimcisi_Cikis75(self):
        """concurrency SHOULD-2: a write helper that outlived its SIGKILLed guard shell holds .restore-write.lock."""
        import fcntl
        self.snap(); self.run_changes()
        lk = open(self.dir / ".restore-write.lock", "a"); fcntl.lockf(lk, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            rc, out, err = self.restore()
        finally:
            lk.close()
        self.assertEqual(75, rc, out + err); self.assertIn("write helper", out)
        self.assertTrue(out.strip().splitlines()[-1].endswith("retry=same-dir class=LOCKED"), out)
        self.assertEqual('{"ui": 99}\n', (self.d1 / "ui-settings.json").read_text())
        rc, out, err = self.restore(); self.assertEqual(0, rc, out + err)

    def test_Inceleme_IlkCasYanDosya_SonraBirincilYazilir_RacedLate30(self):
        """data-loss SHOULD-2: a race before the area's first write turns 30 once a later path of the area is written."""
        self.snap("player"); self.run_changes()
        (self.d2 / "ui-settings.json").write_text('{"made": 1}\n')
        self.hook('[ "$1" = 2 ] && printf "%%s" "{\\"user\\": 7}" > "%s"\n' % (self.d2 / "ui-settings.json"))
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"user": 7}', (self.d2 / "ui-settings.json").read_text())
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertEqual(["RACED-LATE"], [c for c in self.verdict()["classes"] if c.startswith("RACED")])

    def test_Inceleme_KarantinaSlotuDoluysa_UstuneYazilmaz(self):
        """concurrency SHOULD-2: a quarantine slot taken meanwhile is never overwritten (exclusive create)."""
        w = self.snap("player"); self.player_changes()   # G4 D7: player-only, no OTHER (32)
        slot = self.ledger(w["id"]) / "displaced" / "player"; slot.mkdir(parents=True)
        # next_slot sees .1 free, another writer takes it first: the hook fires at the first CAS
        self.hook('[ "$1" = 2 ] && printf other > "%s/prototype-profile.json.1"\n' % slot)
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual("other", (slot / "prototype-profile.json.1").read_text())
        self.assertEqual('{"player": 99}\n', (slot / "prototype-profile.json.2").read_text())

    def test_TmpSweep_CanliPidDosyasi_Dokunulmaz(self):
        self.snap("editor")
        live = self.d1 / (".ddguard-x-%d.tmp" % os.getpid()); live.write_text("live\n")
        dead = self.d1 / ".ddguard-x-999999.tmp"; dead.write_text("dead\n")
        rc, out, err = self.restore("editor")
        self.assertEqual(0, rc, out + err)
        self.assertTrue(live.exists()); self.assertFalse(dead.exists())
        self.assertIn("tmp-sweep", (self.root / "ledger" / "INDEX.tsv").read_text())

    def test_Restore_WPlayerMakbuzYok_Cikis64(self):
        self.snap("player")
        w = json.loads((self.dir / "window.json").read_text()); w["receipt"] = None
        (self.dir / "window.json").write_text(json.dumps(w))
        rc, out, err = self.restore("player")
        self.assertEqual(64, rc, err); self.assertIn("SHOULD-8", err)

    def test_Restore_D2UiDosyalari_GeriYuklenir(self):
        (self.d2 / "ui-hints.json").write_text('{"hints": 1}\n')
        self.snap("player")
        (self.d2 / "ui-hints.json").write_text('{"hints": 2}\n')
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"hints": 1}\n', (self.d2 / "ui-hints.json").read_text())

    def test_Sira_VerdictDefterOzetSlot(self):
        self.snap("player")
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        lines = (self.dir / "restore.txt").read_text().splitlines()
        self.assertIn("userdata-guard restore scope=player", lines[-2]); self.assertEqual("exit=0", lines[-1])
        self.assertTrue((self.dir / "guard-verdict.json").is_file()); self.assertTrue(list(self.dir.glob("verdict-*.json")))
        self.assertEqual([], [e for e in self.slot_entries() if e.startswith("player/")])   # released after the summary


class ClassCodeTests(RestoreBase):
    """G4 D7 (design v3.1 §3 D7, §2.5): OTHER → 32, WINDOW_LOST, FOREIGN_WRITER, priority 64 > 31 > 30 > 75 > 32 > 0, class=."""

    def last(self, out):
        return out.strip().splitlines()[-1]

    def player_side_changes(self):
        (self.d2 / "prototype-profile.json").unlink()                       # removed → MISSING
        (self.d2 / "ui-settings.json").write_text('{"made": 1}\n')          # added
        d = load_plist(self.p1); d["PlayerKey"] = "z"; self.write_plist(self.p1, d)   # changed

    def test_Other_EkleSilDegistir_Cikis32Verified1Attribution(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        self.player_side_changes()
        rc, out, err = self.restore("editor")
        self.assertEqual(32, rc, out + err)
        last = self.last(out)
        self.assertIn("verified=1", last); self.assertTrue(last.endswith("class=OTHER"), last)
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())   # in scope: restored
        other = (self.dir / "out-of-scope-changed.txt").read_text()
        self.assertTrue(other.endswith("\n") and "$'" not in other, repr(other[-80:]))   # C-00 05.10 MUST: a real newline
        self.assertIn("player  MISSING  " + str(self.d2 / "prototype-profile.json"), other)
        self.assertIn(str(self.d2 / "ui-settings.json"), other)
        self.assertIn(str(self.p1), other)
        self.assertFalse((self.d2 / "prototype-profile.json").exists())                  # out of scope: never undone
        self.assertTrue((self.dir / "attribution.txt").exists())
        v = self.verdict(); self.assertEqual((32, "OTHER", True), (v["exit"], v["class"], v["verified"]))

    def test_Oncelik_OtherVeMismatch_Cikis30(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        (self.dir / "snap/D1/ui-settings.json").write_text("corrupt\n")
        self.player_side_changes()
        rc, out, err = self.restore("editor")
        self.assertEqual(30, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=SNAPCORRUPT"), self.last(out))
        self.assertIn("OTHER", self.verdict()["classes"])

    def test_Oncelik_OtherVeBusy_Cikis75(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        self.player_side_changes()
        p = self.lock_d2(self.d1)
        rc, out, err = self.restore("editor")
        self.unlock_d2(p, self.d1)
        self.assertEqual(75, rc, out + err)
        self.assertTrue(self.last(out).endswith("retry=same-dir class=BUSY"), self.last(out))

    def test_Oncelik_KgVeDiger_Cikis31(self):
        self.snap(); self.run_changes()
        d = load_plist(self.dir / "snap/PR/p1.plist"); d["Screenmanager Fullscreen mode"] = 3
        self.write_plist(self.dir / "snap/PR/p1.plist", d)
        (self.dir / "snap/D1/ui-settings.json").write_text("corrupt\n")
        rc, out, err = self.restore()
        self.assertEqual(31, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=KG_DEVIATION"), self.last(out))

    def snapshot_from_short_lived_parent(self, scope="player"):
        """the snapshot's caller (the receipt owner) is a shell that exits right after it."""
        r = subprocess.run(["zsh", "-c", 'zsh "$0" snapshot "$1" --scope "$2"; rc=$?; exit $rc', str(self.guard), str(self.dir), scope],
                           env=self.env(), capture_output=True, text=True, timeout=120)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_WindowLost_SahipSnapshottanSonraOlur_GeriYukler30TekrarSifir(self):
        before = self.live_state()
        self.snapshot_from_short_lived_parent()
        self.player_changes()
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=WINDOW_LOST"), self.last(out))
        self.assertIn("verified=1", self.last(out))
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))   # restored anyway
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)                    # reported once; the converged window is clean
        self.assertIn("WINDOW_LOST was reported", out + err)

    def test_WindowLost_ReentrantAtaCanli_Cikis0(self):
        go = self.tmp / "release-reent"; done = self.tmp / "snap-done"
        p = subprocess.Popen(["bash", "-c", f'"{self.tools}/slot.sh" acquire player reent >/dev/null || exit 9; '
                              f'zsh -c \'zsh "$0" snapshot "$1" --scope player; rc=$?; exit $rc\' "{self.guard}" "{self.dir}" > "{self.tmp}/snap.out" 2>&1; '
                              f'echo $? > "{done}"; while [ ! -f "{go}" ]; do sleep 0.1; done; "{self.tools}/slot.sh" release player reent >/dev/null'],
                             env=self.env(), start_new_session=True)
        self.procs.append(p)
        self.assertTrue(self.wait_file(done, 120)); self.assertEqual("0", done.read_text().strip(), (self.tmp / "snap.out").read_text())
        self.assertIsNotNone(json.loads((self.dir / "window.json").read_text())["receipt"]["holder"])
        self.player_changes()
        try:
            rc, out, err = self.restore("player")
        finally:
            go.write_text("")
        self.assertEqual(0, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=OK"), self.last(out))

    def lineage_row(self, owned):
        with open(self.dir / "players-lineage.tsv", "a") as f:
            f.write("2026-10-05T12:00:00Z\t4242\t1\tMon Oct  5 15:00:00 2026\t%d\n" % owned)

    def test_ForeignWriter_SahipsizPlayerVeDegisiklik_GeriYukler30Index(self):
        before = self.live_state()
        w = self.snap("player"); self.player_changes()
        self.lineage_row(0)
        rc, out, err = self.restore("player")
        self.assertEqual(30, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=FOREIGN_WRITER"), self.last(out))
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        rows = [l.split("\t") for l in (self.root / "ledger" / "INDEX.tsv").read_text().splitlines() if l.split("\t")[1] == w["id"]]
        self.assertTrue(rows); self.assertEqual({"FOREIGN_WRITER"}, {r[6] for r in rows})
        fw = self.verdict()["foreignWriter"]
        self.assertEqual([4242], [p["pid"] for p in fw["players"]]); self.assertTrue(fw["displaced"])

    def test_ForeignWriter_SahibinCocuguPlayer_Cikis0(self):
        self.snap("player"); self.player_changes()
        self.lineage_row(1)
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)
        self.assertIsNone(self.verdict()["foreignWriter"])

    def test_ForeignWriter_EditorPenceresi_SinifYokCikis0(self):
        """D7 review MUST-1: a Player writes only the player area — an editor-only window never gets FOREIGN_WRITER."""
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        self.lineage_row(0)
        rc, out, err = self.restore("editor")
        self.assertEqual(0, rc, out + err)
        self.assertTrue(self.last(out).endswith("class=OK"), self.last(out))

    def test_ForeignWriterVeBusy_Cikis30RetrySameDir(self):
        """D7 review SHOULD-3: a 30 class decides the code, but an area left BUSY still needs the retry."""
        self.snap(); self.player_changes()
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        self.lineage_row(0)
        p = self.lock_d2(self.d1)
        try:
            rc, out, err = self.restore()
        finally:
            self.unlock_d2(p, self.d1)
        self.assertEqual(30, rc, out + err)
        self.assertTrue(self.last(out).endswith("retry=same-dir class=FOREIGN_WRITER"), self.last(out))

    def test_ForeignWriter_DegisiklikYok_Cikis0(self):
        self.snap("player")
        self.lineage_row(0)
        rc, out, err = self.restore("player")
        self.assertEqual(0, rc, out + err)

    def test_Sozlesme_OzetSatiri_McGrepWpRegexKpTail(self):
        """T28: every caller parse on the real guard's output (32 and 0)."""
        import importlib.util, re as _re
        spec = importlib.util.spec_from_file_location("guard_receipt_t28", TOOLS / "qa" / "guard_receipt.py")
        gr = importlib.util.module_from_spec(spec); spec.loader.exec_module(gr)
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n'); self.player_side_changes()
        rc, out, err = self.restore("editor")
        self.assertEqual(32, rc, out + err)
        last = self.last(out)
        self.assertIn("userdata-guard restore scope=editor", last)                             # the summary is stdout's last line
        # main-ci's own grep pattern, read from main-ci.sh (drift there is caught here)
        mc = _re.search(r"grep -q '(restore scope=editor [^']*)'", (TOOLS / "main-ci.sh").read_text()).group(1)
        self.assertRegex(out, mc.replace(".*", ".*"))
        rc_pat = _re.search(r'grep -q "(restore scope=\$GUARD_SCOPE [^"]*)"', (TOOLS / "qa" / "rc.sh").read_text()).group(1)
        self.assertRegex(out, rc_pat.replace("$GUARD_SCOPE", "editor"))
        fields = gr.restore_fields(out)
        self.assertEqual(("1", "1"), (fields.get("verified"), fields.get("touched")))           # guard_receipt / WP regex
        self.assertEqual("touched=1", _re.findall(r"touched=[0-9]*", out)[-1])                  # KP / GT grep -o … | tail -1
        for key in dict(_re.findall(r"(\w+)=(\S+)", last)):
            if key not in ("touched", "verified"):
                self.assertNotIn("touched", key); self.assertNotIn("verified", key)
        self.assertTrue(_re.search(r"snapshotId=[0-9a-f]{12} retry=none class=OTHER$", last), last)
        # OTHER rows: <area>␠␠<sha|MISSING>␠␠<path>, the main-ci sed strips the prefix to the path
        for row in (self.dir / "out-of-scope-changed.txt").read_text().splitlines():
            self.assertRegex(row, r"^player  ([0-9a-f]{64}|MISSING)  /")
            self.assertTrue(_re.sub(r"^player  [^ ]*  ", "", row).startswith("/"))


class LedgerTests(RestoreBase):
    """G4 D8 (design v3.1 §3 D8, INV-7, Q4): the window ledger L/windows/<id>.json."""

    def L(self, *parts):
        return self.root.joinpath("ledger", *parts)

    def records(self, sub="windows"):
        return [json.loads(p.read_text()) for p in sorted(self.L(sub).glob("*.json"))]

    def foreign_snapshot(self, d, scope="editor", stay=True):
        """a snapshot whose caller is another process: alive afterwards (stay) or gone at once."""
        done = self.tmp / ("done-" + d.name)
        tail = "sleep 600" if stay else "exit $rc"   # 06.10: 120 s could end before the restore under load ~70 (→ 65)
        p = subprocess.Popen(["zsh", "-c", 'zsh "$0" snapshot "$1" --scope "$2" > "$3.out" 2>&1; rc=$?; '
                              'echo $rc > "$3.tmp"; mv "$3.tmp" "$3"; ' + tail,   # P5 test review SHOULD-6: atomic
                              str(self.guard), str(d), scope, str(done)], env=self.env(), start_new_session=True)
        self.procs.append(p)
        self.assertTrue(self.wait_file(done, 120))
        self.assertEqual("0", done.read_text().strip(), Path(str(done) + ".out").read_text())
        if not stay:
            p.wait(timeout=30)
        return p

    def test_Ledger_IkiEditorPenceresiAtaDisi_Ikincisi75(self):
        self.foreign_snapshot(self.tmp / "ugA")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(75, rc, out + err)
        self.assertIn("queue position 1 — ahead: %s (pid " % (self.tmp / "ugA").resolve(), out + err)
        self.assertEqual([], list(self.L("queue").glob("*.json")))   # a waiter that gives up leaves the queue …
        self.assertEqual(1, len(list(self.L("queue", "done").glob("*.exit75.json"))))   # … by a move, never a delete
        self.assertIn("class=QUEUE_TIMEOUT", out + err)
        self.assertFalse((self.dir / "before.txt").exists())
        self.assertEqual(1, len(self.records()))

    def test_Kuyruk_CagiranZombi_Cikis65PencereVeBiletYok(self):
        """technical-writer 06.10 (8z): the caller exits while its snapshot waits in the queue and its parent (this test) does
        not reap it — a zombie, kill -0 still answers for it. The guard (now a child of launchd) opens no window: 65, no
        record, no ticket left in the queue (the fixture ps shim answers nothing for a zombie, so the guard's own
        parent check decides here; with the real ps, ledger-begin's state Z does too)."""
        self.foreign_snapshot(self.tmp / "ugA")
        out, go = self.tmp / "zombie-guard.out", self.tmp / "zombie-go"
        c = subprocess.Popen(["zsh", "-c", 'zsh "$0" snapshot "$1" --scope editor > "$2" 2>&1 & '
                              'while [ ! -f "$3" ]; do sleep 0.2; done; exit 0', str(self.guard), str(self.dir), str(out), str(go)],
                             env=self.env(DD_GUARD_QUEUE_WAIT="300"), start_new_session=True)
        try:
            self.assertTrue(self.wait_until(lambda: len(list(self.L("queue").glob("*.json"))) == 1, 120))
            go.write_text("")   # the caller exits now and stays a zombie (not reaped until the finally below)
            self.assertTrue(self.wait_until(lambda: out.exists() and "FAILED exit=" in out.read_text(), 120),
                            out.read_text() if out.exists() else "-")
            text = out.read_text()
            self.assertIn("is gone — no window opened", text)
            self.assertIn("snapshot FAILED exit=65", text)
            self.assertEqual([str((self.tmp / "ugA").resolve())], [r["dir"] for r in self.records()])
            self.assertEqual([], list(self.L("queue").glob("*.json")))
        finally:
            c.wait(timeout=10)   # reaped only now

    def test_Kuyruk_S7BeklerkenCagiranOlur_Cikis65KayitAborted_SonrakiSnapshot0(self):
        """gt 06.10 N2: the record is written (S6), the guard waits for the profile lock (S7, ≤ DD_GUARD_WAIT) and its caller
        is killed — the guard opens no window for it: 65, the record goes to aborted/, the next snapshot of the area is 0
        (an open record of a dead owner refused every later snapshot with 65 before)."""
        unity = self.tmp / "Unity.app/Contents/MacOS/Unity"; unity.parent.mkdir(parents=True); unity.symlink_to("/bin/sleep")
        lk = subprocess.Popen([str(unity), "120"], start_new_session=True); self.procs.append(lk)
        lock = self.d1 / "prototype-profile.json.lock"; lock.write_text("pid=%d\nname=fixture\n" % lk.pid)
        out = self.tmp / "s7.out"
        c = subprocess.Popen(["zsh", "-c", 'zsh "$0" snapshot "$1" --scope editor > "$2" 2>&1; echo never', str(self.guard),
                              str(self.dir), str(out)], env=self.env(DD_GUARD_WAIT="60"), start_new_session=True)
        self.procs.append(c)   # '; echo never': zsh -c would exec a single command — the guard's parent is this shell
        self.assertTrue(self.wait_until(lambda: out.exists() and "profile lock held by live pid" in out.read_text(), 120),
                        out.read_text() if out.exists() else "-")
        self.assertEqual(1, len(self.records()))   # S6 wrote the record
        os.kill(c.pid, signal.SIGKILL); c.wait(timeout=10)
        lk.kill(); lk.wait(); lock.unlink()
        self.assertTrue(self.wait_until(lambda: "FAILED exit=" in out.read_text() or "snapshot → " in out.read_text(), 120), out.read_text())
        self.assertIn("is gone — no window opened for it (65)", out.read_text())
        self.assertIn("snapshot FAILED exit=65", out.read_text())
        self.assertFalse((self.dir / "before.txt").exists())
        self.assertEqual([], self.records()); self.assertEqual(1, len(self.records("aborted")))
        rc, o2, e2 = self.run_guard("snapshot", self.tmp / "ugNext", "--scope", "editor")
        self.assertEqual(0, rc, o2 + e2)

    def test_Kuyruk_DirtyRetrySameDir_SahipCanli_BekleyenBekler_IkinciRestoreSonrasiBaslar(self):
        """next slice (C-00 06.10): the open window's restore ends BUSY (75, retry=same-dir: the profile lock is held) — the
        record is dirty with retry same-dir and its owner lives on. A queued snapshot waits for it (it got 65 before);
        the second restore of the same DIR closes the window and the waiter starts."""
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a)
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        p = self.lock_d2(self.d1)
        rc, out, err = self.run_guard("restore", a, "--scope", "editor", DD_GUARD_WAIT="2")
        self.assertEqual(75, rc, out + err); self.assertIn("retry=same-dir", out.strip().splitlines()[-1])
        rec = self.records()[0]
        self.assertEqual(("dirty", "same-dir"), (rec["state"], rec.get("retry")))
        done_b = self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: "queue position 1 — ahead: %s" % a.resolve() in self.out_of(done_b), 120),
                        self.out_of(done_b))
        time.sleep(6)
        self.assertFalse(done_b.exists(), "the waiter must wait for the dirty window's second restore: " + self.out_of(done_b))
        self.unlock_d2(p, self.d1)
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertTrue(self.wait_file(done_b, 120)); self.assertEqual("0", done_b.read_text().strip(), self.out_of(done_b))

    # -- C-00 06.10 (gt P4/P5): a restore's out-of-scope areas and other live windows ------------------------------------
    def test_P5_DarRestore_KapsamDisiDegisenAlanDirty_GenisRestoreOrijinal(self):
        """gt P5: an all-window restored --scope editor left the player area 'released' with the run's bytes in it — the
        next player window took them as its base and its restore put them back. Now that area is dirty (blocked) until
        the window's own wider restore brings the user's bytes back."""
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a, scope="all")
        self.run_changes()
        rc, out, err = self.run_guard("restore", a, "--scope", "editor")
        self.assertIn(rc, (0, 32), out + err)
        recs = self.records()
        self.assertEqual(1, len(recs), "A closed — its player area (the run's bytes) released: %s" % self.records("closed"))
        rec = recs[0]
        self.assertEqual(("dirty", "finalized", "dirty"), (rec["state"], rec["areas"]["editor"], rec["areas"]["player"]))
        self.assertEqual({"player": "changed since the snapshot"}, rec["outOfScopeDirty"])
        rc, out, err = self.run_guard("snapshot", b, "--scope", "player")
        self.assertEqual(65, rc, out + err)   # the run's player bytes are nobody's base
        rc, out, err = self.run_guard("restore", a, "--scope", "all")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertEqual([], self.records()); self.assertEqual(1, len(self.records("closed")))
        rc, out, err = self.run_guard("snapshot", b, "--scope", "player")
        self.assertEqual(0, rc, out + err)
        self.assertEqual(0, self.run_guard("restore", b, "--scope", "player")[0])

    def test_P5_DarRestore_KapsamDisiDegismeyenAlanReleased_GenisRestoreSifir(self):
        """an out-of-scope area the run left as it was is released (byte-identical to the snapshot); a later wider restore
        converges with 0 (D6's re-restore contract)."""
        a = self.tmp / "ugA"
        self.foreign_snapshot(a, scope="all")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')   # the run changes the editor area only
        p = self.lock_d2(self.d1)
        rc, out, err = self.run_guard("restore", a, "--scope", "editor", DD_GUARD_WAIT="2")
        self.unlock_d2(p, self.d1)
        self.assertEqual(75, rc, out + err)
        rec = self.records()[0]
        self.assertEqual(("dirty", "released"), (rec["areas"]["editor"], rec["areas"]["player"]))
        rc, out, err = self.run_guard("restore", a, "--scope", "all")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertEqual([], self.records())

    def test_P5_RetryFinalizedAlanYeniPencerede_DegisiklikYazilmaz_DirtyAlanGeriGelir(self):
        """B-1 frees a finalized area of a dirty window; B opens there and changes it. The owner's retry restores its dirty
        area and never overwrites B's change (D6 CAS → STALE, 30) — not a 64: a finalized area is not 'still to restore',
        and it stays finalized (the window closes). B, opened after A, restores without waiting for A."""
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a, scope="all")
        self.run_changes()
        p = self.lock_d2()
        rc, out, err = self.run_guard("restore", a, "--scope", "all", DD_GUARD_WAIT="2")
        self.unlock_d2(p)
        self.assertEqual(75, rc, out + err)
        rec = self.records()[0]
        self.assertEqual(("finalized", "dirty"), (rec["areas"]["editor"], rec["areas"]["player"]))
        rc, out, err = self.run_guard("snapshot", b, "--scope", "editor")
        self.assertEqual(0, rc, out + err)                            # editor is free (finalized)
        (self.d1 / "ui-settings.json").write_text('{"ui": 77}\n')     # B's run
        rc, out, err = self.run_guard("restore", a, "--scope", "all")
        self.assertNotEqual(64, rc, out + err)
        self.assertEqual('{"ui": 77}\n', (self.d1 / "ui-settings.json").read_text())        # B's change kept
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())  # A's dirty area back
        self.assertNotIn(str(a.resolve()), [r["dir"] for r in self.records()])   # finalized is final: A closed
        rc, out, err = self.run_guard("restore", b, "--scope", "editor")
        self.assertIn(rc, (0, 32), out + err)   # 32: A put its player area back while B's (editor) window was open
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())

    def test_P5_IcIcePencereAcikken_DisRestore64_SiraylaKapanir(self):
        """a nested window still open over an area the outer restore has to write (the wrong order): its own restore would
        put the run's bytes back — 64, nothing written; inner first, then outer."""
        n = self.tmp / "ugN"
        self.snap("all")                                    # A: owner = this test process
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')   # A's run, before N's snapshot
        done_n = self.queued_snapshot(n, scope="player")     # N: a child of this process → nested, granted at once
        self.assertTrue(self.wait_file(done_n, 120)); self.assertEqual("0", done_n.read_text().strip(), self.out_of(done_n))
        rc, out, err = self.restore("all")
        self.assertEqual(64, rc, out + err); self.assertIn("holds the player area now", out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())   # nothing written
        self.assertEqual("open", [r for r in self.records() if r["dir"] == str(self.dir.resolve())][0]["state"])
        self.assertEqual(0, self.run_guard("restore", n, "--scope", "player")[0])
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_TerkEdilmisIcPencere_DisRestore64_OnceIcSonraDis_Orijinal(self):
        """P5 review MUST-1: the inner window's owner is gone and its window was never restored. The outer restore must not
        write first — restored after it, the inner window would put the outer run's bytes back."""
        n = self.tmp / "ugN"
        self.snap("all")
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')   # A's run, before N
        done_n = self.queued_snapshot(n, scope="player")
        self.assertTrue(self.wait_file(done_n, 120)); self.assertEqual("0", done_n.read_text().strip(), self.out_of(done_n))
        os.killpg(self.procs[-1].pid, signal.SIGKILL); self.procs[-1].wait(timeout=30)   # N's owner gone
        rc, out, err = self.restore("all")
        self.assertEqual(64, rc, out + err); self.assertIn("abandoned: C-00 restores it first", out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())   # nothing written
        rc, out, err = self.run_guard("restore", n, "--scope", "player")
        self.assertIn(rc, (0, 30), out + err)   # 30: WINDOW_LOST (its owner is gone), restored all the same
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_IlkDenemeDarRestore_IcPencereAcik64(self):
        """P5 review S-6: the first attempt records the run's end of EVERY window area (R8) — a narrow first restore of the
        outer window while the inner one is open would record the inner run's bytes as 'first' and its later wide restore
        would never converge (STALE). The first attempt checks every window area: 64."""
        n = self.tmp / "ugN"
        self.snap("all")
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')
        done_n = self.queued_snapshot(n, scope="player")
        self.assertTrue(self.wait_file(done_n, 120)); self.assertEqual("0", done_n.read_text().strip(), self.out_of(done_n))
        (self.d2 / "prototype-profile.json").write_text('{"player": 77}\n')   # N's run
        rc, out, err = self.restore("editor")
        self.assertEqual(64, rc, out + err); self.assertIn("holds the player area now", out + err)
        self.assertFalse((self.dir / "after-run.txt").exists())   # no 'first' recorded
        self.assertEqual(0, self.run_guard("restore", n, "--scope", "player")[0])
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_FinalizedAlan_OkGecenYol_SonraKosuBayti_Stale30Yazimsiz(self):
        """P5 review S-1: an OK path is not in written-<a>.txt. Attempt 1 records the run's end (player 99) and ends 75;
        someone puts the profile back; attempt 2 finds it OK and finalizes the area (closed). A later {"player": 99} —
        the run's bytes, but someone's later change — is never written over by a third restore: STALE 30."""
        self.snap("all")
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')
        p = self.lock_d2()
        rc, out, err = self.restore("all", DD_GUARD_WAIT="2")
        self.unlock_d2(p)
        self.assertEqual(75, rc, out + err)
        (self.d2 / "prototype-profile.json").write_text('{"player": 1}\n')
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())
        (self.d2 / "prototype-profile.json").write_text('{"player": 99}\n')
        rc, out, err = self.restore("all")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"player": 99}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertIn("never written again", (self.dir / "restore-player.json").read_text())

    def test_P5_KapsamDisiAlanCanliKilitSahibi_DegismeseDeDirty(self):
        """P5 review S-4: an out-of-scope area the run left as it was but a live lock holder may still write is not
        released — it is dirty with its reason in the record; a wider restore closes the window."""
        self.snap("all")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')   # the run changes the editor area only
        p = self.lock_d2()                                          # a live holder on the player folder
        rc, out, err = self.restore("editor")
        self.assertIsNone(p.poll(), "the lock holder ended before the restore did")   # test review SHOULD-5
        self.unlock_d2(p)
        self.assertEqual(0, rc, out + err)
        recs = self.records(); self.assertEqual(1, len(recs), self.records("closed"))
        rec = recs[0]
        self.assertEqual(("dirty", "finalized", "dirty"), (rec["state"], rec["areas"]["editor"], rec["areas"]["player"]))
        self.assertEqual({"player": "a live lock holder"}, rec["outOfScopeDirty"])
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())

    def test_P5_RestorecununBaslangicZamaniBilinmez_75HicbirSeyYazilmaz(self):
        """P5 review r2 S-2: a restore that cannot mark its record restoring (own start time unknown) would leave it open —
        a nested snapshot could be granted inside the area while it writes. 75, nothing restored; the retry restores."""
        self.snap("all"); self.run_changes()
        (self.tmp / "ps-restore-lstart-unknown").write_text("")
        rc, out, err = self.restore("all")
        (self.tmp / "ps-restore-lstart-unknown").unlink()
        self.assertEqual(75, rc, out + err); self.assertIn("own start time unknown", out + err)
        self.assertNotEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())   # nothing written
        self.assertEqual("open", self.records()[0]["state"])
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_FinalizedAlan_PlistOkYolu_SonraKosuDegeri_Stale30Yazimsiz(self):
        """P5 test review MUST-1: the plist twin of the file-path test — a finalized area's plist that an OK decision left
        out of written-<a>.txt is never written again when its live value is the run's again (A_FINAL, decide → STALE)."""
        self.snap("all")
        d = load_plist(self.p1); d["RunKey"] = 1; self.write_plist(self.p1, d)    # the run changes the player plist only
        p = self.lock_d2()
        rc, out, err = self.restore("all", DD_GUARD_WAIT="2")
        self.unlock_d2(p)
        self.assertEqual(75, rc, out + err)                                      # 'first' = RunKey 1 recorded, player BUSY
        d = load_plist(self.p1); d.pop("RunKey"); self.write_plist(self.p1, d)   # someone puts it back
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())                                     # finalized, closed
        d = load_plist(self.p1); d["RunKey"] = 1; self.write_plist(self.p1, d)   # a later change: the run's value again
        rc, out, err = self.restore("all")
        self.assertEqual(30, rc, out + err)
        self.assertEqual(1, load_plist(self.p1).get("RunKey"))                   # not written
        self.assertIn("never written again", (self.dir / "restore-player.json").read_text())

    def test_P5_KapaliDirTekrarRestore_SonraAcilanPencereDegismemisAlanda_D6Stale30_64Degil(self):
        """P5 test review SHOULD-1 (NIT-5): a closed DIR restored again while a window opened after it holds an area the run
        left as it was — D6 decides (STALE 30, nothing written), not a 64: that area is never 'still to write'."""
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a, scope="all")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')                 # the run: the editor area only
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())                                     # A closed, player released
        rc, out, err = self.run_guard("snapshot", b, "--scope", "player"); self.assertEqual(0, rc, out + err)
        (self.d2 / "prototype-profile.json").write_text('{"player": 77}\n')       # B's run
        rc, out, err = self.run_guard("restore", a, "--scope", "all")
        self.assertEqual(30, rc, out + err)
        self.assertEqual('{"player": 77}\n', (self.d2 / "prototype-profile.json").read_text())
        self.assertEqual(0, self.run_guard("restore", b, "--scope", "player")[0])
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_KapsamDisiAlanCalisanPlayer_Dirty(self):
        """P5 test review SHOULD-3: an out-of-scope area the run left as it was while a Player runs may still change — dirty,
        with the reason, not released."""
        self.snap("all")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        (self.tmp / "pgrep-player").write_text("4242 /fixture/Deaths Demolition.app/Contents/MacOS/Deaths Demolition\n")
        try:
            rc, out, err = self.restore("editor")
        finally:
            (self.tmp / "pgrep-player").unlink()
        self.assertEqual(0, rc, out + err)
        recs = self.records(); self.assertEqual(1, len(recs), self.records("closed"))
        self.assertEqual(("dirty", "dirty"), (recs[0]["state"], recs[0]["areas"]["player"]))
        self.assertEqual({"player": "a running Player"}, recs[0]["outOfScopeDirty"])
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())

    def fail_helper(self, cmd):
        real = shutil.which("python3")
        (self.bin / "python3").write_text('#!/bin/sh\ncase "$*" in *%s*) echo "Traceback: boom" >&2; exit 3 ;; esac\n'
                                          'exec "%s" "$@"\n' % (cmd, real))
        (self.bin / "python3").chmod(0o755)

    def test_P5_RestoreBeginYardimcisiCokerse_UyariR1IsaretiEskiDavranis(self):
        """P5 test review SHOULD-2 (r2 N-1): ledger-restore-begin fails — a WARNING line, the r1 restoring mark, restored as
        before (fail-open by design: a helper bug never stops a restore)."""
        self.fail_helper("ledger-restore-begin")
        self.snap("all"); self.run_changes()
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        tl = (self.dir / "timeline.txt").read_text()
        self.assertIn("WARNING userdata-guard: ledger-restore-begin failed (exit 3)", tl); self.assertIn("Traceback: boom", tl)
        self.assertTrue((self.records("closed")[0].get("restorer") or {}).get("pid"))   # the r1 mark was written
        self.assertEqual('{"player": 1}\n', (self.d2 / "prototype-profile.json").read_text())

    def test_P5_LedgerFinalizedCokerse_UyariSatiri(self):
        """P5 test review (NIT-2): ledger-finalized fails — a WARNING line (never a silent "nothing finalized")."""
        self.fail_helper("ledger-finalized")
        self.snap("all"); self.run_changes()
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)
        self.assertIn("WARNING userdata-guard: ledger-finalized failed (exit 3)", (self.dir / "timeline.txt").read_text())

    def test_Kuyruk_SayisalOlmayanBekleme_Snapshot64HicbirSeyYok_Restore_UyariVarsayilan(self):
        """06.10 reviews: DD_GUARD_QUEUE_WAIT=1h / DD_GUARD_WAIT=abc made a wait endless (the test never became true)."""
        for var, val in (("DD_GUARD_QUEUE_WAIT", "1h"), ("DD_GUARD_WAIT", "abc"), ("DD_GUARD_SLOT_WAIT", "5m"),
                         ("DD_GUARD_DIRTY_GRACE", "10m")):
            with self.subTest(var=var):
                d = self.tmp / ("ug-bad-" + var)
                rc, out, err = self.run_guard("snapshot", d, "--scope", "editor", **{var: val})
                self.assertEqual(64, rc, out + err)
                self.assertIn("%s=%s is not whole seconds — refused, nothing written (64)" % (var, val), out + err)
                self.assertFalse(d.exists())
        self.assertFalse(self.L("windows").exists() and any(self.L("windows").iterdir()))
        w = self.snap("editor")
        rc, out, err = self.run_guard("restore", self.dir, "--scope", "editor", DD_GUARD_WAIT="abc")
        self.assertEqual(0, rc, out + err)
        self.assertIn("WARNING DD_GUARD_WAIT=abc is not whole seconds — this restore uses the default", out + err)
        self.assertEqual([], self.records())

    def test_Kuyruk_KapanisAraligi_KuyruktakiBekleyen65Almaz(self):
        """06.10 reviews MUST-2 (process level): the guard's own ledger-close is held for 4 s; the record is already in
        closed/ (R12 closes and moves in one locked step), so the queued waiter starts (225d0359: 65, state=closed)."""
        real = shutil.which("python3")
        (self.bin / "python3").write_text('#!/bin/sh\ncase "$*" in *ledger-close*) sleep 4 ;; esac\nexec "%s" "$@"\n' % real)
        (self.bin / "python3").chmod(0o755)
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a)
        done_b = self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: len(list(self.L("queue").glob("*.json"))) == 1))
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.wait_file(done_b, 60))
        self.assertEqual("0", done_b.read_text().strip(), Path(str(done_b) + ".out").read_text())

    def test_Ledger_PollSirasinda_FlockTutulmaz(self):
        import fcntl
        self.foreign_snapshot(self.tmp / "ugA")
        g = self.start_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="20")
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))   # 06.10: in the wait (sleep 4 could probe too early)
        got, end = False, time.time() + 6   # ≥ one 5 s poll: the hold is one read-modify-write (ps shim calls inside)
        with open(self.L(".lock"), "a") as f:   # free within 2 s: the poll holds it only for one read-modify-write
            while not got and time.time() < end:
                try:
                    fcntl.flock(f.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB); got = True
                    fcntl.flock(f.fileno(), fcntl.LOCK_UN)
                except BlockingIOError:
                    time.sleep(0.05)
        self.assertTrue(got, "the ledger lock was held through the wait")
        out, err = g.communicate(timeout=60)
        self.assertEqual(75, g.returncode, out + err)

    def test_Ledger_TerkEdilmisOpen_Cikis65MesajAlanlari(self):
        a = self.tmp / "ugA"
        self.foreign_snapshot(a, stay=False)
        rec = self.records()[0]
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor")
        self.assertEqual(65, rc, out + err)
        for part in ("REFUSED — ledger window %s:" % rec["id"], "DIR=%s" % a.resolve(), "state=open", "owner=%d" % rec["owner"]["pid"],
                     "alive=0", 'next: "userdata-guard.sh restore %s --scope editor"' % a.resolve()):
            self.assertIn(part, out + err)

    def test_Ledger_DirtyKayit_Cikis65_TekrarRestoreSonraSifir(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        p = self.lock_d2(self.d1)
        rc, out, err = self.restore("editor"); self.assertEqual(75, rc, out + err)
        self.assertEqual("dirty", self.records()[0]["state"])
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor")
        self.assertEqual(65, rc, out + err); self.assertIn("state=dirty", out + err)
        self.unlock_d2(p, self.d1)
        rc, out, err = self.restore("editor"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records()); self.assertEqual(1, len(self.records("closed")))
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor")
        self.assertEqual(0, rc, out + err)

    def test_Ledger_IcIceAtaPenceresi_Cikis0(self):
        self.snap("editor")
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor")
        self.assertEqual(0, rc, out + err)   # the same owner (the caller itself): nested, LIFO
        self.assertEqual(2, len(self.records()))

    def test_Ledger_YazimdaSigkill_SonrakiSnapshot65TekrarKapanir(self):
        before = self.live_state()
        self.snap("player"); self.player_changes()
        self.hook('[ "$1" = 2 ] && { : > "%s/in-write"; sleep 30; }\n' % self.tmp)
        g = self.start_guard("restore", self.dir, "--scope", "player")
        self.assertTrue(self.wait_file(self.tmp / "in-write"))
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)
        (self.tmp / "pgrep-hook").unlink()
        self.assertEqual("restoring", self.records()[0]["state"])
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "player")
        self.assertEqual(65, rc, out + err); self.assertIn("state=restoring", out + err)
        rc, out, err = self.restore("player"); self.assertEqual(0, rc, out + err)
        self.assertEqual(before[self.d2 / "prototype-profile.json"], sha(self.d2 / "prototype-profile.json"))
        self.assertEqual([], self.records())

    def test_Ledger_StaleKayit_DirtyKalirClosedaGitmez(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        p = self.lock_d2(self.d1)
        rc, out, err = self.restore("editor"); self.assertEqual(75, rc, out + err)
        self.unlock_d2(p, self.d1)
        (self.d1 / "ui-settings.json").write_text('{"user": 1}\n')   # the user saved meanwhile
        rc, out, err = self.restore("editor"); self.assertEqual(30, rc, out + err)
        recs = self.records()
        self.assertEqual(("dirty", "dirty"), (recs[0]["state"], recs[0]["areas"]["editor"]))
        self.assertEqual([], self.records("closed"))

    def test_Ledger_SnapshotHatasi_KayitKalmaz(self):
        (self.tmp / "mv-fail-before").write_text("")
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor")
        self.assertEqual(75, rc, out + err)
        self.assertEqual([], self.records())
        self.assertEqual(1, len(self.records("aborted")))   # kept, never deleted

    def test_Ledger_KokDdGuardRootAltinda_HomeaYazilmaz(self):
        w = self.snap("editor")
        rc, out, err = self.restore("editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.L("closed", w["id"] + ".json").is_file())
        self.assertFalse((self.tmp / "home" / ".dd-userdata-guard").exists())
        self.assertFalse((Path(pwhome()) / ".dd-userdata-guard" / "windows" / (w["id"] + ".json")).exists())
        self.assertFalse((Path(pwhome()) / ".dd-userdata-guard" / "closed" / (w["id"] + ".json")).exists())

    def test_Ledger_ReleaseAllToPlayer_EditorReleasedKapanir(self):
        w = self.snap("all"); self.player_changes()
        rc, out, err = self.restore("player"); self.assertEqual(0, rc, out + err)
        rec = json.loads(self.L("closed", w["id"] + ".json").read_text())
        self.assertEqual({"editor": "released", "player": "finalized"}, rec["areas"])

    def test_Ledger_DarRestore_KirliAlaniKapatmaz(self):
        """D8 review MUST-1: a narrower re-restore (--scope player) never releases a dirty editor area and closes the window."""
        self.snap("all"); self.player_changes()
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        p = self.lock_d2(self.d1)
        try:
            rc, out, err = self.restore("all"); self.assertEqual(75, rc, out + err)
            rc, out, err = self.restore("player")
        finally:
            self.unlock_d2(p, self.d1)
        rec = self.records()[0]
        self.assertEqual(("dirty", "dirty"), (rec["state"], rec["areas"]["editor"]))
        self.assertEqual([], self.records("closed"))
        rc, out, err = self.restore("all"); self.assertEqual(0, rc, out + err)   # the real fix: restore it
        self.assertEqual([], self.records())

    def test_Ledger_KirliKayitBaskaAlan_Cikis0(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        p = self.lock_d2(self.d1)
        try:
            rc, out, err = self.restore("editor"); self.assertEqual(75, rc, out + err)
        finally:
            self.unlock_d2(p, self.d1)
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "player")
        self.assertEqual(0, rc, out + err)   # no overlap with the dirty editor window

    def test_Ledger_CanliRestore_Bekler75(self):
        self.snap("editor")
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        self.hook("")   # pgrep is not called for the editor area; hold the restore in its write phase via the defaults shim
        (self.tmp / "defaults-import-sleep").write_text("")
        d = load_plist(self.p2); d["EditorKey"] = "run"; self.write_plist(self.p2, d)
        g = self.start_guard("restore", self.dir, "--scope", "editor")
        self.assertTrue(self.wait_file(self.tmp / "import-started", 60))
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(75, rc, out + err); self.assertIn("restoring", out + err)
        (self.tmp / "defaults-import-sleep").unlink()
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)

    def test_P5_IcPencereCanliRestoreEdiliyor_Dis75Yazimsiz_RestorerOlunce64_SonraIkisi0(self):
        """P5 gt N-4 (C-00 07.10, option A): the outer window's restore while the nested window is being restored by a live
        guard — 75 BUSY retry=same-dir, nothing written, the outer record still open. With that restorer dead (SIGKILL in
        its write phase) the old 64 ('restore that window first'); the inner restored, the outer restores with 0."""
        before = self.live_state()
        self.snap("editor")                                                   # A, the outer window
        (self.d1 / "ui-settings.json").write_text('{"ui": 50}\n')            # A's run: B's base differs from A's
        b = self.tmp / "ugB"
        rc, out, err = self.run_guard("snapshot", b, "--scope", "editor"); self.assertEqual(0, rc, out + err)   # nested
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        d = load_plist(self.p2); d["EditorKey"] = "run"; self.write_plist(self.p2, d)
        self.hook("")
        (self.tmp / "defaults-import-sleep").write_text("")                   # hold B's restore in its write phase
        g = self.start_guard("restore", b, "--scope", "editor")
        self.assertTrue(self.wait_file(self.tmp / "import-started", 60))
        rc, out, err = self.restore("editor")
        self.assertEqual(75, rc, out + err)
        self.assertIn("is being restored now by pid", out + err)
        last = out.strip().splitlines()[-1]
        self.assertTrue(last.endswith("(75) retry=same-dir class=BUSY"), last)
        self.assertNotEqual(before[self.d1 / "ui-settings.json"], sha(self.d1 / "ui-settings.json"))   # A wrote nothing
        self.assertIn((self.d1 / "ui-settings.json").read_text(), ('{"ui": 50}\n', '{"ui": 99}\n'))   # B's base or B's run
        self.assertEqual(["open", "restoring"], sorted(r["state"] for r in self.records()))   # A open, B restoring
        os.killpg(g.pid, signal.SIGKILL); g.communicate(timeout=30)
        (self.tmp / "defaults-import-sleep").unlink()
        rc, out, err = self.restore("editor")                                 # B's restorer is dead now
        self.assertEqual(64, rc, out + err); self.assertIn("restore that window first", out + err)
        rc, out, err = self.run_guard("restore", b, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        rc, out, err = self.restore("editor"); self.assertEqual(0, rc, out + err)
        self.assertEqual(before[self.d1 / "ui-settings.json"], sha(self.d1 / "ui-settings.json"))
        self.assertEqual([], self.records())

    def test_Ledger_GercekAtaPenceresi_Cikis0(self):
        """nested: the overlapping window's owner is a GRANDPARENT of the new snapshot's caller."""
        a, done = self.tmp / "ugA", self.tmp / "inner-done"
        script = ('zsh "$0" snapshot "$1" --scope editor > "$3.a" 2>&1 || exit 7; '
                  'zsh -c \'zsh "$0" snapshot "$1" --scope editor; exit $?\' "$0" "$2" > "$3.b" 2>&1; echo $? > "$3"')
        p = subprocess.Popen(["zsh", "-c", script, str(self.guard), str(a), str(self.dir), str(done)], env=self.env(),
                             start_new_session=True)
        self.procs.append(p)
        self.assertTrue(self.wait_file(done, 120))
        self.assertEqual("0", done.read_text().strip(), Path(str(done) + ".b").read_text())
        self.assertEqual(2, len(self.records()))

    def test_Ledger_TerkEdilmisPencereCanliSlotSahibiAltinda_Kardes65(self):
        """D8 concurrency review SHOULD-1: an abandoned re-entrant window under a LIVE slot holder is not "nested" for the
        holder's next child — liveness is the window owner's (the dead snapshot caller), not the holder's."""
        go, done, done2 = self.tmp / "release-h", self.tmp / "c1-done", self.tmp / "c2-done"
        script = (f'"{self.tools}/slot.sh" acquire player h >/dev/null || exit 9; '
                  f'zsh -c \'zsh "$0" snapshot "$1" --scope player; exit $?\' "{self.guard}" "{self.dir}" > "{done}.out" 2>&1; echo $? > "{done}"; '
                  f'zsh -c \'zsh "$0" snapshot "$1" --scope player; exit $?\' "{self.guard}" "{self.tmp / "ugB"}" > "{done2}.out" 2>&1; echo $? > "{done2}"; '
                  f'while [ ! -f "{go}" ]; do sleep 0.1; done; "{self.tools}/slot.sh" release player h >/dev/null')
        p = subprocess.Popen(["bash", "-c", script], env=self.env(), start_new_session=True); self.procs.append(p)
        try:
            self.assertTrue(self.wait_file(done2, 120))
            self.assertEqual("0", done.read_text().strip(), Path(str(done) + ".out").read_text())
            out = Path(str(done2) + ".out").read_text()
            self.assertEqual("65", done2.read_text().strip(), out)
            self.assertIn("alive=0", out)
            rec = self.records()[0]
            self.assertTrue(rec["slot"]["reentrant"]); self.assertIsNotNone(rec["slot"]["holder"])
        finally:
            go.write_text("")

    def queued_snapshot(self, d, scope="editor", **env):
        """a correctly ordered caller (no unity slot) whose owner stays alive: snapshot in the background (its group is
        self.procs[-1]). 06.10: queue wait 300 s / owner 600 s — 120 s was within reach of a slow FIFO run at load ~75."""
        done = self.tmp / ("done-" + d.name)
        e = dict(DD_GUARD_QUEUE_WAIT="300"); e.update(env)
        p = subprocess.Popen(["zsh", "-c", 'zsh "$0" snapshot "$1" --scope "$3" > "$2.out" 2>&1; echo $? > "$2.tmp"; '
                              'mv "$2.tmp" "$2"; sleep 600',   # P5 test review SHOULD-6: wait_file never reads it half-written
                              str(self.guard), str(d), str(done), scope], env=self.env(**e), start_new_session=True)
        self.procs.append(p)
        return done

    def out_of(self, done):
        f = Path(str(done) + ".out")
        return f.read_text() if f.exists() else ""

    def test_Kuyruk_FIFO_IkincisiBirincininRestoreundaBaslar_UcuncuBekler(self):
        """C-00 05.10: two correctly ordered waiters queue FIFO; the first starts when the open window is restored, the
        second only when the first's window is restored."""
        a, b, c = self.tmp / "ugA", self.tmp / "ugB", self.tmp / "ugC"
        self.foreign_snapshot(a)
        done_b = self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: len(list(self.L("queue").glob("*.json"))) == 1))
        done_c = self.queued_snapshot(c)
        self.assertTrue(self.wait_until(lambda: len(list(self.L("queue").glob("*.json"))) == 2))
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.wait_file(done_b, 60)); self.assertEqual("0", done_b.read_text().strip(), self.out_of(done_b)[-800:])
        self.assertIn("waiting in the window queue — queue position 1", Path(str(done_b) + ".out").read_text())
        end = time.time() + 12   # 06.10: ≥ two of C's 5 s polls — a record of C would show at once (done_c only after S11)
        while time.time() < end:
            self.assertEqual([str(b.resolve())], [r["dir"] for r in self.records()], "the second waiter must wait for the first's window")
            self.assertFalse(done_c.exists(), "C ended early (exit %s): %s" % (done_c.read_text().strip() if done_c.exists() else "-",
                                                                               self.out_of(done_c)[-600:]))
            time.sleep(0.2)
        self.assertIn("queue position 2", Path(str(done_c) + ".out").read_text())
        rc, out, err = self.run_guard("restore", b, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.wait_file(done_c, 60)); self.assertEqual("0", done_c.read_text().strip(), self.out_of(done_c)[-800:])
        self.assertEqual([], list(self.L("queue").glob("*.json")))

    def test_Kuyruk_OlenBekleyicininBileti_SiradakiniTutmaz(self):
        """a waiting guard killed with SIGKILL (its caller shell lives on) leaves no live ticket: the next waiter proceeds."""
        a, b, c = self.tmp / "ugA", self.tmp / "ugB", self.tmp / "ugC"
        self.foreign_snapshot(a)
        self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: len(list(self.L("queue").glob("*.json"))) == 1))
        pids = subprocess.run(["pgrep", "-f", "snapshot %s --scope" % b], capture_output=True, text=True).stdout.split()
        self.assertTrue(pids)
        for pid in pids:   # only this fixture's guard (its DIR is unique to this test)
            try:
                os.kill(int(pid), signal.SIGKILL)
            except ProcessLookupError:   # 06.10: pgrep -f also lists the guard's short-lived forks ($(…), print |)
                pass
        done_c = self.queued_snapshot(c)
        self.assertTrue(self.wait_until(lambda: "queue position" in self.out_of(done_c)))
        self.assertIn("queue position 1 —", self.out_of(done_c))   # the dead waiter's ticket is not ahead
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.wait_file(done_c, 60)); self.assertEqual("0", done_c.read_text().strip(), Path(str(done_c) + ".out").read_text())

    def wait_until(self, cond, seconds=60):
        end = time.time() + seconds
        while time.time() < end:
            if cond():
                return True
            time.sleep(0.2)
        return False

    # -- D8 kuyruk boşlukları (S-01 test-engineer 06.10, WP-089 D8 kuyruk regresyon planı) ----------------------------------
    def tickets(self):
        return sorted(self.L("queue").glob("*.json"))

    def plant(self, sub, name, rec):
        p = self.L(sub, name); p.parent.mkdir(parents=True, exist_ok=True); p.write_text(json.dumps(rec)); return p

    def lstart_of(self, pid):
        """the helper's own lstart text (fixture ps shim on PATH) — what a ticket / record carries."""
        r = subprocess.run(["python3", str(self.tools / "userdata_guard_io.py"), "lstart", str(pid)], env=self.env(),
                           capture_output=True, text=True, timeout=60)
        return r.stdout.strip()

    def lock_free(self, seconds):
        import fcntl
        end = time.time() + seconds
        with open(self.L(".lock"), "a") as f:
            while time.time() < end:
                try:
                    fcntl.flock(f.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    time.sleep(0.05); continue
                fcntl.flock(f.fileno(), fcntl.LOCK_UN); return True
        return False

    def test_Kuyruk_BeklerkenIntTerm_Cikis130143BiletKayitYok(self):
        """MUST · snap_fail on a signal while queued: INT → 130, TERM → 143; the ticket leaves the queue at once (not only when
        a later waiter finds it dead), no record, no aborted record, no before.txt."""
        self.foreign_snapshot(self.tmp / "ugA")
        for sig, code in ((signal.SIGINT, 130), (signal.SIGTERM, 143)):
            with self.subTest(sig=sig.name):
                d = self.tmp / ("ugS%d" % code)
                g = self.start_guard("snapshot", d, "--scope", "editor", DD_GUARD_QUEUE_WAIT="300")
                self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
                os.kill(g.pid, sig)
                out, err = g.communicate(timeout=60)
                self.assertEqual(code, g.returncode, out + err)
                self.assertIn("snapshot FAILED exit=%d" % code, out)
                self.assertEqual([], self.tickets())
                self.assertEqual(1, len(self.records())); self.assertEqual([], self.records("aborted"))
                self.assertFalse((d / "before.txt").exists())

    def test_Kuyruk_BeklerkenSahipOlur_Cikis65BiletSilinir_RestoreSonrasiSifir(self):
        """MUST · göç 'ölü sahip': the open window's owner dies while a waiter polls → the waiter answers 65 with the next step
        and leaves the queue; that restore closes the window and the next snapshot starts at once."""
        a = self.tmp / "ugA"
        owner = self.foreign_snapshot(a)
        rec = self.records()[0]
        g = self.start_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="300")
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
        os.killpg(owner.pid, signal.SIGKILL); owner.wait(timeout=30)   # the fixture caller's own group (start_new_session)
        out, err = g.communicate(timeout=90)
        self.assertEqual(65, g.returncode, out + err)
        for part in ("REFUSED — ledger window %s:" % rec["id"], "alive=0",
                     'next: "userdata-guard.sh restore %s --scope editor"' % a.resolve()):
            self.assertIn(part, out + err)
        self.assertEqual([], self.tickets())
        self.assertFalse((self.dir / "before.txt").exists())
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertEqual([], self.records())
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor"); self.assertEqual(0, rc, out + err)

    def test_Kuyruk_IcIcePencereYabanciBekleyiciVarken_Cikis0Beklemez(self):
        """MUST · nested + queue (deadlock): a foreign waiter C is queued for the outer window A; A's own child snapshot B is
        nested (LIFO, allowed) and must not queue behind C — C waits for A, A's run waits for B: stuck until
        DD_GUARD_QUEUE_WAIT. Expected: B starts at once (0, no queue line) and C keeps its place."""
        a, b, c = self.tmp / "ugA", self.tmp / "ugB", self.tmp / "ugC"
        go, outer, inner = self.tmp / "go-inner", self.tmp / "outer-done", self.tmp / "inner-done"
        script = ('zsh "$0" snapshot "$1" --scope editor > "$3.out" 2>&1; echo $? > "$3"; '
                  'while [ ! -f "$5" ]; do sleep 0.1; done; '
                  'zsh -c \'zsh "$0" snapshot "$1" --scope editor; exit $?\' "$0" "$2" > "$4.out" 2>&1; echo $? > "$4"; sleep 600')
        p = subprocess.Popen(["zsh", "-c", script, str(self.guard), str(a), str(b), str(outer), str(inner), str(go)],
                             env=self.env(DD_GUARD_QUEUE_WAIT="20"), start_new_session=True)
        self.procs.append(p)
        self.assertTrue(self.wait_file(outer, 120)); self.assertEqual("0", outer.read_text().strip(), self.out_of(outer))
        done_c = self.queued_snapshot(c)
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
        go.write_text("")
        self.assertTrue(self.wait_file(inner, 120))
        self.assertEqual("0", inner.read_text().strip(), self.out_of(inner))
        self.assertNotIn("queue position", self.out_of(inner))
        self.assertFalse(done_c.exists(), "C still waits for the outer window")
        self.assertEqual(1, len(self.tickets()))

    def test_Kuyruk_ClosedKayitTasinmadan_YeniSnapshot65Almaz(self):
        """MUST · R12 closes in two helper calls (ledger-update → state closed, then ledger-close moves it under a second lock):
        a snapshot polling in between meets a 'closed' record in windows/ and refuses it like an abandoned one (65,
        'state=closed … alive=1 … next: restore'). Seen live 06.10 at load 75–200: a FIFO repeat (C ended early) and two
        queued waiters B that got exactly this 65 during A's real restore. Expected: after the R12 ledger-update the
        record blocks nothing (moved under the same lock, or a closed record is skipped)."""
        w = self.snap("editor")
        r = subprocess.run(["python3", str(self.tools / "userdata_guard_io.py"), "ledger-update", str(self.L()), w["id"]],
                           input='{"state": "auto", "areas": {"editor": "finalized"}}', env=self.env(), capture_output=True,
                           text=True, timeout=60)
        self.assertEqual(("closed", 0), (r.stdout.strip(), r.returncode), r.stderr)   # what the guard's R12 gets back
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(0, rc, out + err)
        self.assertNotIn("state=closed", out + err)

    def test_Kuyruk_KismiOrtusme_AllKapaninca_EditorVePlayerBekleyicileriBirlikteBaslar(self):
        """SHOULD · partial overlap: A (all) is open; B (editor) waits in the window queue, C (player) waits for the player
        slot A's owner holds (slot.sh's queue, not L/queue). After A's restore both start; C never queues behind B."""
        a, b, c = self.tmp / "ugA", self.tmp / "ugB", self.tmp / "ugC"
        self.foreign_snapshot(a, scope="all")
        done_b = self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
        done_c = self.queued_snapshot(c, scope="player", DD_GUARD_SLOT_WAIT="300")
        self.assertTrue(self.wait_until(lambda: [e for e in self.slot_entries() if e.startswith("queue/player/")]))
        self.assertEqual(1, len(self.tickets()), "C waits for the player slot, not in the window queue")
        rc, out, err = self.run_guard("restore", a, "--scope", "all"); self.assertEqual(0, rc, out + err)
        for done in (done_b, done_c):
            self.assertTrue(self.wait_file(done, 120), done.name)
            self.assertEqual("0", done.read_text().strip(), self.out_of(done))
        self.assertNotIn("queue position", self.out_of(done_c))
        self.assertEqual({("editor",), ("player",)}, {tuple(r["windowAreas"]) for r in self.records()})
        self.assertEqual([], self.tickets())

    def test_Kuyruk_PencereYokCanliBiletVar_YeniGelenOneGecmez(self):
        """SHOULD · FIFO without a blocker — the moment between a window's close and its head waiter's next poll: a live ticket
        of an earlier waiter is ahead of a newcomer even when no window is open → 75 'queue position 2 — ahead: <its DIR>
        (pid …, queued …)'; once that waiter is gone the newcomer starts. (A planted ticket: stopping a real waiter with
        SIGSTOP makes its zsh read the stopped $(…) child as exit 19 — 'ledger check failed (19)' — so not that way.)"""
        sh = subprocess.Popen(["sleep", "600"], start_new_session=True); self.procs.append(sh)
        who = {"pid": sh.pid, "lstart": self.lstart_of(sh.pid)}; self.assertTrue(who["lstart"])
        t = self.plant("queue", "%d-onceki.json" % (time.time_ns() - 10 ** 9),
                       {"id": "onceki", "dir": "/fixture/onceki", "owner": who, "waiter": who, "windowAreas": ["editor"],
                        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="1")
        self.assertEqual(75, rc, out + err)
        self.assertIn("queue position 2 — ahead: /fixture/onceki (pid %d, queued" % sh.pid, out + err)
        self.assertEqual([], self.records()); self.assertEqual([t], self.tickets())
        sh.kill(); sh.wait(timeout=10)
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(0, rc, out + err); self.assertEqual([], self.tickets())

    def test_Kuyruk_Tahmin_KapaliPencereYokUnknown_VarsaMedyanDakika(self):
        """SHOULD · the estimate: no closed window over these areas → 'estimate unknown (no closed window over these areas yet)'; with closed
        windows → their median duration minus the open window's age (a player-only window does not count)."""
        import re
        self.foreign_snapshot(self.tmp / "ugA")
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor", DD_GUARD_QUEUE_WAIT="1")
        self.assertEqual(75, rc, out + err)
        self.assertIn("estimate unknown (no closed window over these areas yet)", out + err)
        for i, (areas, minutes) in enumerate(((["editor"], 50), (["editor"], 60), (["editor", "player"], 70), (["player"], 500))):
            self.plant("closed", "20261001T0%d0000Z-fixture.json" % i,
                       {"id": "c%d" % i, "windowAreas": areas, "utc": "2026-10-01T10:00:00Z",
                        "utcUpdated": "2026-10-01T%02d:%02d:00Z" % (10 + minutes // 60, minutes % 60)})
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugC", "--scope", "editor", DD_GUARD_QUEUE_WAIT="1")
        self.assertEqual(75, rc, out + err)
        m = re.search(r"estimate ~(\d+) min", out + err)
        self.assertTrue(m, out + err)
        self.assertTrue(50 <= int(m.group(1)) <= 60, m.group(0))   # median 60 − A's age (minutes)

    def test_Kuyruk_KayitUtcPencereAcilisi_KuyrukBeklemesiSureyeGirmez(self):
        """SHOULD · a record's utc is the window's start: the queue time must not count as window time — typical_minutes
        reads utc → utcUpdated of closed records and the refusal prints age= from it, so every estimate grows by the waits."""
        import calendar
        a, b = self.tmp / "ugA", self.tmp / "ugB"
        self.foreign_snapshot(a)
        done_b = self.queued_snapshot(b)
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
        time.sleep(12)   # B queues ≥ 12 s
        t_restore = time.time()
        rc, out, err = self.run_guard("restore", a, "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertTrue(self.wait_file(done_b, 120)); self.assertEqual("0", done_b.read_text().strip(), self.out_of(done_b))
        rec = [r for r in self.records() if r["dir"] == str(b.resolve())][0]
        start = calendar.timegm(time.strptime(rec["utc"], "%Y-%m-%dT%H:%M:%SZ"))
        self.assertGreaterEqual(start, int(t_restore) - 1, "utc %s is the queue entry, not the window start" % rec["utc"])

    def test_Kuyruk_KlasorYokVeBozukBilet_YoksayilirCikis0(self):
        """SHOULD · no L/queue/ → a free snapshot creates none; a corrupt ticket (half JSON, hand edit) never blocks and is
        kept — moved to queue/done/<name>.unreadable.json, never deleted (06.10 reviews: nothing under L is deleted)."""
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugA", "--scope", "editor"); self.assertEqual(0, rc, out + err)
        self.assertFalse(self.L("queue").exists())
        rc, out, err = self.run_guard("restore", self.tmp / "ugA", "--scope", "editor"); self.assertEqual(0, rc, out + err)
        bad = self.L("queue", "1-bozuk.json"); bad.parent.mkdir(parents=True); bad.write_text('{"id": "x", "windowAreas": ["edi')
        rc, out, err = self.run_guard("snapshot", self.tmp / "ugB", "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(0, rc, out + err)
        self.assertNotIn("queue position", out + err)
        self.assertFalse(bad.exists())
        self.assertEqual(1, len(list(self.L("queue", "done").glob("1-bozuk.unreadable*.json"))))

    def test_Kuyruk_EskiBiletWaiterYokSahipOlu_SilinirCikis0(self):
        """SHOULD · a ticket without 'waiter' (2-argument ledger-begin): liveness falls back to the owner — a dead owner's
        ticket is removed by the next snapshot, which starts at once."""
        old = self.plant("queue", "1-eski.json", {"id": "eski", "dir": "/fixture/eski", "windowAreas": ["editor"],
                                                  "owner": {"pid": 999999, "lstart": "Thu Jan  1 00:00:00 1970"},
                                                  "utc": "2026-10-05T00:00:00Z"})
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(0, rc, out + err)
        self.assertFalse(old.exists())

    def test_Kuyruk_EskiBiletWaiterYokSahipCanli_SiraTutmaz(self):
        """SHOULD · a waiter-less ticket whose owner (a long-lived shell) lives on would hold every later waiter of its areas
        until that shell exits (a guard that gave up without ledger-dequeue leaves it) — S-01 06.10: no guard writes one; it
        is reaped (queue/done/…reaped.json) and never honoured."""
        sh = subprocess.Popen(["sleep", "600"], start_new_session=True); self.procs.append(sh)
        lst = self.lstart_of(sh.pid); self.assertTrue(lst)
        self.plant("queue", "1-eski.json", {"id": "eski", "dir": "/fixture/eski", "windowAreas": ["editor"],
                                            "owner": {"pid": sh.pid, "lstart": lst}, "utc": "2026-10-05T00:00:00Z"})
        rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="6")
        self.assertEqual(0, rc, out + err)

    def test_Kuyruk_SayisalOlmayanBiletAdi_BekleyenCokmez(self):
        """NIT · a live ticket whose name does not start with the ns stamp (copied by hand: 'elle-…json') makes
        int(name.split('-')[0]) raise in a waiter that holds its own ticket → 'ledger check failed (1)' → 75 at once."""
        self.foreign_snapshot(self.tmp / "ugA")
        g = self.start_guard("snapshot", self.dir, "--scope", "editor", DD_GUARD_QUEUE_WAIT="20")
        self.assertTrue(self.wait_until(lambda: len(self.tickets()) == 1))
        sh = subprocess.Popen(["sleep", "600"], start_new_session=True); self.procs.append(sh)
        who = {"pid": sh.pid, "lstart": self.lstart_of(sh.pid)}
        self.plant("queue", "elle-kopya.json", {"id": "elle", "dir": "/fixture/elle", "owner": who, "waiter": who,
                                                "windowAreas": ["editor"], "utc": "2026-10-06T00:00:00Z"})
        out, err = g.communicate(timeout=90)
        self.assertEqual(75, g.returncode, out + err)
        self.assertNotIn("ledger check failed", out + err)
        self.assertIn("still after 20s", out + err)

    def test_Goc_D7PenceresiDefterKaydiYok_YeniGuardRestore0KayitYaratmaz(self):
        """SHOULD · rollout: a window opened by the D7 guard has no ledger record; the D8 guard restores it (0, R2/R12 skipped)
        and writes no record, no closed/ entry, no ticket. (The other half — a D7 editor window is INVISIBLE to D8
        snapshots — is a rollout rule, see d8-queue-testplan-0610.md §3.)"""
        w = self.snap("editor")
        self.L("windows", w["id"] + ".json").unlink()   # what a D7 snapshot leaves behind: no ledger record
        (self.d1 / "ui-settings.json").write_text('{"ui": 99}\n')
        rc, out, err = self.restore("editor")
        self.assertEqual(0, rc, out + err)
        self.assertEqual('{"ui": 1}\n', (self.d1 / "ui-settings.json").read_text())
        self.assertEqual([], self.records()); self.assertEqual([], self.records("closed")); self.assertFalse(self.L("queue").exists())

    def test_Ledger_SnapshotMissing_Cikis64KayitDirty(self):
        w = self.snap("editor")
        (self.dir / "before.txt").unlink()
        rc, out, err = self.restore("editor")
        self.assertEqual(64, rc, err)
        self.assertIn("SNAPSHOT_MISSING — ledger window %s marked dirty" % w["id"], err)
        self.assertEqual("dirty", self.records()[0]["state"])
        self.assertIn("SNAPSHOT_MISSING — ledger window %s marked dirty" % w["id"], (self.dir / "timeline.txt").read_text())

    def plant_snapshotting(self, owner_alive):
        """a record of self.dir still 'snapshotting' (its guard gone without a trace), owner dead or alive; no snapshot."""
        if owner_alive:
            p = subprocess.Popen(["sleep", "300"]); self.procs.append(p)
        else:
            p = subprocess.Popen(["true"]); p.wait()
        lst = " ".join(subprocess.run([str(self.bin / "ps"), "-o", "lstart=", "-p", str(p.pid)], capture_output=True,
                                      text=True, env=self.env()).stdout.split())   # normalised as userdata_guard_io.lstart
        self.dir.mkdir(parents=True, exist_ok=True)
        rec = {"id": "planted", "dir": str(self.dir.resolve()), "scope": "editor", "windowAreas": ["editor"],
               "owner": {"pid": p.pid, "lstart": lst}, "state": "snapshotting", "areas": {"editor": "open"},
               "utc": "2026-10-06T00:00:00Z"}
        self.L("windows").mkdir(parents=True, exist_ok=True)
        self.L("windows", "planted.json").write_text(json.dumps(rec))

    def test_Ledger_MarkMissing_SnapshottingSahipOlu_Restore64KayitDirty(self):
        """C-00 06.10: `alive` was shadowed in userdata_guard_io.main — mark-missing crashed (UnboundLocalError) on every
        snapshotting record and the guard swallowed it; a dead owner's record stayed 'snapshotting'. Now: dirty + a line."""
        self.plant_snapshotting(owner_alive=False)
        rc, out, err = self.restore("editor")
        self.assertEqual(64, rc, out + err)
        self.assertEqual("dirty", self.records()[0]["state"])
        self.assertIn("SNAPSHOT_MISSING — ledger window planted marked dirty", (self.dir / "timeline.txt").read_text())
        self.assertTrue((self.dir / "restore.txt").read_text().splitlines()[0].endswith("— refused (64)"))   # the refusal stays last

    def test_Ledger_MarkMissing_SnapshottingSahipCanli_IsaretlenmezSatirYazilir(self):
        self.plant_snapshotting(owner_alive=True)
        rc, out, err = self.restore("editor")
        self.assertEqual(64, rc, out + err)
        self.assertEqual("snapshotting", self.records()[0]["state"])
        self.assertIn("SNAPSHOT_MISSING not marked — the ledger window of", (self.dir / "timeline.txt").read_text())

    def test_Ledger_MarkMissing_YardimciCokerse_UyariSatiri_SessizDegil(self):
        real = shutil.which("python3")
        (self.bin / "python3").write_text('#!/bin/sh\ncase "$*" in *ledger-mark-missing*) echo "Traceback: boom" >&2; exit 3 ;; esac\n'
                                          'exec "%s" "$@"\n' % real)
        (self.bin / "python3").chmod(0o755)
        self.plant_snapshotting(owner_alive=False)
        rc, out, err = self.restore("editor")
        self.assertEqual(64, rc, out + err)
        tl = (self.dir / "timeline.txt").read_text()
        self.assertIn("WARNING userdata-guard: ledger-mark-missing failed (exit 3)", tl); self.assertIn("Traceback: boom", tl)


class KilitSirasiKuyrukTests(GuardFixture):
    """C-00 05.10 (G4 D8 kuyruk): window → unity for EVERY scope. test_guard_d6a.py covers the ancestor chain (unsandboxed);
    these cover DD_SLOT_OWNER, a foreign session's unity slot and the player-ancestor exception — sandboxed (S-01 test-engineer
    06.10)."""
    LOCK = "userdata-guard: lock order — take the guard snapshot (the window; player scope: the player slot) BEFORE the Unity slot"

    def test_KilitSirasi_DdSlotOwnerUnityTutar_HerKapsam64HicbirSeyYazilmaz(self):
        """MUST · a wrapper that takes the window for its caller (DD_SLOT_OWNER) whose caller holds a unity slot → 64, every
        scope, nothing written (no DIR, no ledger, no player slot)."""
        go = self.holder("unity", "fx-unity"); owner = self.procs[-1].pid   # the holder shell owns the unity slot
        try:
            for scope in ("editor", "player", "all"):
                with self.subTest(scope=scope):
                    d = self.tmp / ("ug-own-" + scope)
                    rc, out, err = self.run_guard("snapshot", d, "--scope", scope, DD_SLOT_OWNER=str(owner))
                    self.assertEqual(64, rc, out + err)
                    self.assertIn(self.LOCK, err); self.assertIn("fix the order", err)
                    self.assertFalse(d.exists(), "nothing may be written before the refusal")
                    self.assertFalse((self.root / "ledger").exists(), "no ledger record, no queue ticket")
                    self.assertEqual([], [e for e in self.slot_entries() if e.startswith(("player/", "queue/player/"))])
        finally:
            go.write_text("")

    def test_KilitSirasi_YabanciOturumUnityTutar_EditorSnapshot0(self):
        """MUST · another session's unity slot (not our chain, no DD_SLOT_OWNER) never turns into a 64 — the rollout would
        otherwise stop every editor run while any Unity batch runs on the machine."""
        go = self.holder("unity", "fx-other")
        try:
            rc, out, err = self.run_guard("snapshot", self.dir, "--scope", "editor")
            self.assertEqual(0, rc, out + err); self.assertNotIn("lock order", out + err)
        finally:
            go.write_text("")

    def test_KilitSirasi_EditorKapsamAtaPlayerCocukUnity_Cikis64(self):
        """SHOULD · KARAR (C-00): the D6a SHOULD-1 exception (an ancestor holds the player slot → a unity slot below it is fine)
        was argued for the PLAYER slot; an editor-scope snapshot now also waits in the window queue (≤ 3600 s) with that unity
        slot held — the deadlock the 'every scope' rule removes. Expected (rule as written): 64."""
        d = self.tmp / "ug-exc"; done = self.tmp / "exc-done"; slot = self.tools / "slot.sh"
        script = (f'"{slot}" acquire player fx-p >/dev/null || exit 91; '
                  f'zsh -c \'"$0" acquire unity fx-u >/dev/null || exit 90; zsh "$1" snapshot "$2" --scope editor; rc=$?; '
                  f'"$0" release unity fx-u >/dev/null; exit $rc\' "{slot}" "{self.guard}" "{d}" > "{done}.out" 2>&1; echo $? > "{done}"; '
                  f'"{slot}" release player fx-p >/dev/null')
        p = subprocess.Popen(["bash", "-c", script], env=self.env(), start_new_session=True); self.procs.append(p)
        self.assertTrue(self.wait_file(done, 120))
        text = Path(str(done) + ".out").read_text()
        self.assertEqual("64", done.read_text().strip(), text)
        self.assertIn(self.LOCK, text)
        self.assertFalse((d / "before.txt").exists())


def sandboxed_main():
    if os.environ.get(SANDBOX_FLAG) == "1":
        unittest.main(verbosity=2)
        return
    sb = shutil.which("sandbox-exec")
    if not sb:
        print("sandbox-exec missing — the v2 guard tests refuse to run unsandboxed (RED)", file=sys.stderr)
        sys.exit(1)
    profile = ('(version 1)(allow default)(deny file-write* (subpath "%s/Library") (subpath "/private/tmp/dd-slots")'
               ' (subpath "%s/.dd-userdata-guard"))'   # G4 D8 review SHOULD-4: never the real ledger
               '(deny mach-lookup (global-name "com.apple.cfprefsd.daemon") (global-name "com.apple.cfprefsd.agent"))'
               % (pwhome(), pwhome()))
    env = dict(os.environ); env[SANDBOX_FLAG] = "1"
    os.execve(sb, [sb, "-p", profile, sys.executable, os.path.abspath(__file__), *sys.argv[1:]], env)


if __name__ == "__main__":
    sandboxed_main()
