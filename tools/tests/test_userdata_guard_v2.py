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
            if p.poll() is None:
                p.kill()   # fixture processes only
            p.wait(timeout=10)
        for d in [self.dir] + list(self.tmp.glob("ug*")):
            (d / ".sampling").unlink(missing_ok=True)
        log = self.log.read_text()
        self.assertNotIn("FOREIGN", log, "a defaults call left the fixture root")
        self.tmpobj.cleanup()

    # -- helpers ------------------------------------------------------------------------------------------------------
    def write_plist(self, path, d):
        with open(path, "wb") as f:
            plistlib.dump(d, f, fmt=plistlib.FMT_BINARY)

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("DD_", "SHIM_"))}
        e.update(HOME=str(self.tmp / "home"), TMPDIR=str(self.tmp), DD_GUARD_ROOT=str(self.root), DD_SLOT_BASE=str(self.tmp / "slots"),
                 DD_SLOT_POLL="1", DD_GUARD_WAIT="2", DD_GUARD_SLOT_WAIT="5", PATH=f"{self.bin}:{os.environ['PATH']}",
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
        p = subprocess.Popen([str(unity), "120"], start_new_session=True); self.procs.append(p)
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
        (self.root / "ledger").mkdir(); (self.root / "ledger").chmod(0o500)
        try:
            rc, out, err = self.restore("editor")
        finally:
            (self.root / "ledger").chmod(0o755)
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
        real = Path(pwhome()) / "Library" / "Preferences" / (BUNDLE + ".plist")
        before = sha(real)
        self.snap(); self.run_changes()
        rc, out, err = self.restore()
        self.assertEqual(0, rc, out + err)
        for line in self.log.read_text().splitlines():
            if line.startswith("defaults "):
                self.assertIn(str(self.root), line)   # every defaults call names the fixture root (absolute domain)
        self.assertEqual(before, sha(real))

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


def sandboxed_main():
    if os.environ.get(SANDBOX_FLAG) == "1":
        unittest.main(verbosity=2)
        return
    sb = shutil.which("sandbox-exec")
    if not sb:
        print("sandbox-exec missing — the v2 guard tests refuse to run unsandboxed (RED)", file=sys.stderr)
        sys.exit(1)
    profile = ('(version 1)(allow default)(deny file-write* (subpath "%s/Library") (subpath "/private/tmp/dd-slots"))'
               '(deny mach-lookup (global-name "com.apple.cfprefsd.daemon") (global-name "com.apple.cfprefsd.agent"))' % pwhome())
    env = dict(os.environ); env[SANDBOX_FLAG] = "1"
    os.execve(sb, [sb, "-p", profile, sys.executable, os.path.abspath(__file__), *sys.argv[1:]], env)


if __name__ == "__main__":
    sandboxed_main()
