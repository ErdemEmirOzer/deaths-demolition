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
if cmd == "export":
    d = load()
    if d is None: sys.exit(1)
    sys.stdout.buffer.write(plistlib.dumps(d))
elif cmd == "import":
    with open(args[2], "rb") as f: new = plistlib.load(f)
    d = load() or {}; d.update(new); save(d)
elif cmd == "delete":
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
rc = 1
for pid in pids or []:
    info = bsdinfo(pid)
    if info is None: continue
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
