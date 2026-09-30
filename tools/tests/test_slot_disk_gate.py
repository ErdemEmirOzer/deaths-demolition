#!/usr/bin/env python3
"""C-00 01.10 · slot.sh disk gate: a unity|player acquire runs dd_disk_gate (dd-env.sh) the moment it gets the slot; not
ready → the slot is released, the ticket keeps its place, a "disk-gate wait" line, retry after DD_SLOT_DISK_POLL (60 s);
DD_SLOT_DISK_WAIT (3600 s) of disk waiting → 75. blender/smoke and re-entrant acquires are not gated; no dd-env.sh → 64.
Fixture only: copied slot.sh + dd-env.sh in a tempdir, fake df/sysctl first on PATH (values from a state file), a temp
DD_SLOT_BASE and HOME — no real disk state, no real slot directory. usage: python3 tools/tests/test_slot_disk_gate.py"""
import os, shutil, signal, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
LOW, OK = 1000, 20 * 1048576   # KiB free: below / above 15 GiB


def write_exec(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    path.chmod(0o755)


def wait_for(predicate, seconds=20.0):
    end = time.time() + seconds
    while time.time() < end:
        if predicate():
            return True
        time.sleep(0.1)
    return False


class SlotDiskGateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-slot-disk-")
        self.root = Path(self.tmp.name).resolve()
        self.tools = self.root / "repo/tools"
        self.tools.mkdir(parents=True)
        for name in ("slot.sh", "dd-env.sh"):
            shutil.copy2(TOOLS / name, self.tools / name)
        self.state = self.root / "free-kib"
        self.state.write_text(str(OK))
        fake = self.root / "bin"
        write_exec(fake / "df", f"#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho \"/dev/x 999 1 $(cat {self.state})\"\n")
        write_exec(fake / "sysctl", "#!/bin/sh\necho 'total = 16384.00M  used = 100.00M  free = 1.00M  (encrypted)'\n")
        (self.root / "home").mkdir()
        self.base = self.root / "slots"
        self.env = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        self.env.update(HOME=str(self.root / "home"), TMPDIR=str(self.root), DD_SLOT_BASE=str(self.base), DD_SLOT_POLL="1",
                        DD_SLOT_DISK_POLL="1", PATH=f"{fake}:{os.environ['PATH']}")
        self.procs = []

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:
                os.killpg(p.pid, signal.SIGKILL)   # only fixture shells started here
            p.communicate(timeout=10)
        self.tmp.cleanup()

    def slot(self, *args, **env):
        return subprocess.run(["bash", str(self.tools / "slot.sh"), *args], env=dict(self.env, **env), capture_output=True,
                              text=True, timeout=60)

    def start(self, script, **env):
        """A holder shell: acquires, then stays alive (the lock is bound to its pid) until `done` appears."""
        p = subprocess.Popen(["bash", "-c", script], env=dict(self.env, **env), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, start_new_session=True)
        self.procs.append(p)
        return p

    def holder(self, kind, tag, **env):
        flag = self.root / f"{tag}.rc"
        done = self.root / f"{tag}.done"
        p = self.start(f'"{self.tools}/slot.sh" acquire {kind} {tag}; echo $? > "{flag}"; '
                       f'while [ ! -f "{done}" ]; do sleep 0.1; done', **env)
        return p, flag, done

    def locks(self, kind):
        return sorted(f.name for f in (self.base / kind).glob("*")) if (self.base / kind).exists() else []

    def tickets(self, kind):
        return sorted(f.name for f in (self.base / "queue" / kind).glob("*")) if (self.base / "queue" / kind).exists() else []

    def test_DiskYeterli_SlotAlinir_BeklemeYok(self):
        for kind in ("unity", "player"):
            with self.subTest(kind=kind):
                p, flag, done = self.holder(kind, f"ok-{kind}")
                self.assertTrue(wait_for(flag.exists))
                self.assertEqual("0", flag.read_text().strip())
                self.assertEqual(1, len(self.locks(kind)))
                done.write_text("")
                out, _ = p.communicate(timeout=20)
                self.assertNotIn("disk-gate wait", out)

    def test_DiskDusuk_SlotBirakilirBiletKalir_SonraAlinir(self):
        self.state.write_text(str(LOW))
        p, flag, done = self.holder("player", "waiter")
        self.assertTrue(wait_for(lambda: len(self.tickets("player")) == 1 and not flag.exists(), 5))
        time.sleep(2.5)                                   # at least two gate rounds
        self.assertEqual([], self.locks("player"))        # the slot is not held while waiting
        self.assertEqual(1, len(self.tickets("player")))  # the ticket keeps its place
        self.assertFalse(flag.exists())
        self.state.write_text(str(OK))
        self.assertTrue(wait_for(flag.exists))
        self.assertEqual("0", flag.read_text().strip())
        self.assertEqual(1, len(self.locks("player")))
        self.assertEqual([], self.tickets("player"))
        done.write_text("")
        out, err = p.communicate(timeout=20)
        self.assertIn("SLOT player disk-gate wait (waiter", out)
        self.assertIn("free disk 1000 KiB < 15728640 (15 GiB)", out)
        self.assertIn("slot released, retry in 1s", out)
        self.assertIn("disk-gate wait", err)              # live, too
        self.assertRegex(out, r"SLOT player#1 -> waiter .*disk-gate wait \d+ s\)")

    def test_DiskBeklemeSuresiDolar_Cikis75SlotVeBiletYok(self):
        self.state.write_text(str(LOW))
        t = time.time()
        r = self.slot("acquire", "unity", "late", DD_SLOT_DISK_WAIT="2")
        self.assertEqual(75, r.returncode, r.stdout + r.stderr)
        self.assertLess(time.time() - t, 30)
        self.assertIn("SLOT unity disk-gate TIMEOUT after", r.stderr)
        self.assertIn("DD_SLOT_DISK_WAIT=2", r.stderr)
        self.assertEqual([], self.locks("unity") + self.tickets("unity"))

    def test_BlenderVeSmoke_KapiyaBakmaz(self):
        self.state.write_text(str(LOW))
        for kind in ("blender", "smoke"):
            with self.subTest(kind=kind):
                p, flag, done = self.holder(kind, f"free-{kind}")
                self.assertTrue(wait_for(flag.exists))
                self.assertEqual("0", flag.read_text().strip())
                done.write_text("")
                out, _ = p.communicate(timeout=20)
                self.assertNotIn("disk-gate", out)

    def test_YenidenGiris_AtasiTutuyor_KapiyaBakmaz(self):
        go = self.root / "go"
        rc = self.root / "child.rc"
        p = self.start(f'"{self.tools}/slot.sh" acquire player outer >/dev/null || exit 9; : > "{self.root}/held"; '
                       f'while [ ! -f "{go}" ]; do sleep 0.1; done; '
                       f'bash -c \'"{self.tools}/slot.sh" acquire player inner\'; echo $? > "{rc}"')
        self.assertTrue(wait_for((self.root / "held").exists))
        self.state.write_text(str(LOW))                   # the disk drops while the outer shell holds the slot
        go.write_text("")
        self.assertTrue(wait_for(rc.exists))
        self.assertEqual("0", rc.read_text().strip())
        out, _ = p.communicate(timeout=20)
        self.assertIn("SLOT player reentrant -> inner", out)
        self.assertNotIn("disk-gate", out)

    def test_DdEnvYok_Cikis64HicbirSeyTutulmaz(self):
        (self.tools / "dd-env.sh").unlink()               # and HOME has no wp-079 driver
        r = self.slot("acquire", "player", "noenv")
        self.assertEqual(64, r.returncode, r.stdout + r.stderr)
        self.assertIn("disk gate unavailable", r.stderr)
        self.assertEqual([], self.locks("player") + self.tickets("player"))

    def test_DdEnvYok_DriverKopyasiKullanilir(self):
        (self.tools / "dd-env.sh").unlink()
        driver = self.root / "home/Desktop/KADRO/context/projects/deaths-demolition/wp-079/driver/tools/dd-env.sh"
        driver.parent.mkdir(parents=True)
        shutil.copy2(TOOLS / "dd-env.sh", driver)
        self.state.write_text(str(LOW))
        r = self.slot("acquire", "player", "drv", DD_SLOT_DISK_WAIT="1")
        self.assertEqual(75, r.returncode, r.stdout + r.stderr)   # the driver's gate ran (and refused the low disk)
        self.assertIn("free disk 1000 KiB", r.stderr)

    def test_KuyrukSirasi_DiskBekleyenOndeKalir(self):
        self.state.write_text(str(LOW))
        a, a_flag, a_done = self.holder("player", "first")
        self.assertTrue(wait_for(lambda: len(self.tickets("player")) == 1))
        time.sleep(1.1)                                   # distinct ticket second
        b, b_flag, b_done = self.holder("player", "second")
        self.assertTrue(wait_for(lambda: len(self.tickets("player")) == 2))
        self.state.write_text(str(OK))
        self.assertTrue(wait_for(a_flag.exists))
        time.sleep(2.5)
        self.assertFalse(b_flag.exists())                 # player limit 1: the later ticket still waits
        self.assertEqual(1, len(self.tickets("player")))
        self.assertIn(".first.", self.locks("player")[0])
        a_done.write_text("")
        a.communicate(timeout=20)
        self.assertTrue(wait_for(b_flag.exists))
        b_done.write_text("")
        b.communicate(timeout=20)

    def test_DiskBeklerkenINT_BiletSilinir130(self):
        self.state.write_text(str(LOW))
        p = subprocess.Popen(["bash", str(self.tools / "slot.sh"), "acquire", "unity", "intr"], env=dict(self.env, DD_SLOT_DISK_POLL="30"),
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
        self.procs.append(p)
        self.assertTrue(wait_for(lambda: len(self.tickets("unity")) == 1))
        time.sleep(1.0)                                   # inside the 30 s disk sleep
        os.kill(p.pid, signal.SIGINT)
        p.wait(timeout=10)
        self.assertEqual(130, p.returncode)
        self.assertEqual([], self.locks("unity") + self.tickets("unity"))

    def test_DdSlotAcquire_TimelineDiskGateWaitSatiri(self):
        self.state.write_text(str(LOW))
        timeline = self.root / "timeline.txt"
        r = subprocess.run(["zsh", "-c", 'source "$1"; DD_TIMELINE="$2"; DD_SLOT_TAG=tl; dd_slot_acquire player', "zsh",
                            str(self.tools / "dd-env.sh"), str(timeline)], env=dict(self.env, DD_SLOT_DISK_WAIT="1"),
                           capture_output=True, text=True, timeout=60)
        self.assertEqual(75, r.returncode, r.stdout + r.stderr)
        text = timeline.read_text()
        self.assertIn("disk-gate wait", text)
        self.assertIn("slot: acquire player failed (slot.sh exit 75)", text)

    def test_EsikTekKaynak_SlotShSayiTasimaz(self):
        text = (TOOLS / "slot.sh").read_text()
        self.assertIn("dd_disk_gate", text)
        self.assertNotIn("15728640", text)
        self.assertNotIn("12288", text)


if __name__ == "__main__":
    unittest.main(verbosity=2)
