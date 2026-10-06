#!/usr/bin/env python3
"""WP-079G D6a (C-00 30.09) · tools/userdata-guard.sh
(1) lock order player → unity: `snapshot … --scope player|all` refuses (64, nothing written, no player slot) while the
    calling shell chain holds a `unity` slot (tools/slot.sh holds); editor scope is unaffected;
(2) `restore <dir>` records its final line + exit=<n> in <dir>/restore.txt on every exit path and keeps <dir>/timeline.txt
    whatever DD_TIMELINE says.
Fixture only: copied tools in a tempdir, DD_GUARD_ROOT and DD_SLOT_BASE in the tempdir, HOME in the tempdir, a Player
pattern that never matches a real Player. No Unity, no Player, never the real user data or the real slot directory.
usage: python3 tools/tests/test_guard_d6a.py"""
import os, plistlib, shutil, subprocess, tempfile, time, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
BUNDLE = "com.Deaths-Demolition.Deaths-Demolition---Playable-Concept"
PRODUCT = "Deaths Demolition - Playable Concept"
LOCK_LINE = "userdata-guard: lock order — take the guard snapshot (the window; player scope: the player slot) BEFORE the Unity slot"

# Holds a real unity slot (owner = this shell) and runs the guard from it: the guard is a child of the slot holder.
HOLDER = """#!/bin/zsh
"$DD_SLOT_SH" acquire unity dd-fixture-u > /dev/null || exit 90
zsh "$DD_GUARD_SH" snapshot "$1" --scope "$2"; rc=$?
"$DD_SLOT_SH" release unity dd-fixture-u > /dev/null
exit $rc
"""


class GuardD6aTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-guard-d6a-")
        self.root = Path(self.tmp.name).resolve()
        self.tools = self.root / "tools"; self.tools.mkdir()
        for name in ("userdata-guard.sh", "dd-env.sh", "userdata_attrib.py", "slot.sh", "userdata_guard_io.py"):
            shutil.copy2(TOOLS / name, self.tools / name)
        # G4 D5: the guard's Player pattern is fixed (DD_GUARD_PLAYER_RE is gone) — a pgrep shim answers "no Player" for it,
        # so a real Player running on this machine never reaches these tests
        self.bin = self.root / "bin"; self.bin.mkdir()
        (self.bin / "pgrep").write_text('#!/bin/sh\nfor a in "$@"; do case "$a" in *"Contents/MacOS/(Deaths Demolition"*) exit 1 ;; esac; done\n'
                                       'exec /usr/bin/pgrep "$@"\n')
        (self.bin / "pgrep").chmod(0o755)
        self.slots = self.root / "slots"
        (self.root / "limit").write_text("3\n")
        (self.root / "home").mkdir()
        self.groot = self.root / "groot"
        (self.groot / "AS/Deaths Demolition" / PRODUCT).mkdir(parents=True)
        (self.groot / "AS" / BUNDLE).mkdir(parents=True)
        (self.groot / "PR").mkdir(parents=True)
        (self.groot / "AS/Deaths Demolition" / PRODUCT / "ui-settings.json").write_text('{"a": 1}\n')
        self.holder = self.root / "dd-fixture-holder.zsh"
        self.holder.write_text(HOLDER); self.holder.chmod(0o755)
        self.bg = []

    def tearDown(self):
        for p in self.bg:
            if p.poll() is None:
                p.kill()          # only fixture processes this test started
            p.wait(timeout=10)
        for d in self.root.glob("ug*"):
            self.stop_sampler(d)
        self.tmp.cleanup()

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        e.update(HOME=str(self.root / "home"), TMPDIR=str(self.root), DD_GUARD_ROOT=str(self.groot), DD_GUARD_WAIT="1",
                 PATH=f"{self.root / 'bin'}:{os.environ['PATH']}",
                 DD_SLOT_BASE=str(self.slots), DD_SLOT_LIMIT_FILE=str(self.root / "limit"), DD_SLOT_POLL="1",
                 DD_SLOT_SH=str(self.tools / "slot.sh"), DD_GUARD_SH=str(self.tools / "userdata-guard.sh"))
        e.update(extra)
        return {k: v for k, v in e.items() if v is not None}

    def guard(self, *args, **env):
        r = subprocess.run(["zsh", str(self.tools / "userdata-guard.sh"), *map(str, args)], env=self.env(**env),
                           capture_output=True, text=True, timeout=120)
        return r.returncode, r.stdout + r.stderr

    def stop_sampler(self, d):
        (d / ".sampling").unlink(missing_ok=True)   # the snapshot's Player sampler ends within one 5 s tick
        end = time.time() + 15
        while time.time() < end and subprocess.run(["pgrep", "-f", f"userdata-guard.sh snapshot {d} "],
                                                   capture_output=True).returncode == 0:
            time.sleep(0.5)

    def slot_files(self, kind):
        d = self.slots / kind
        return sorted(p.name for p in d.iterdir()) if d.is_dir() else []

    def under_unity(self, target, scope):
        r = subprocess.run([str(self.holder), str(target), scope], env=self.env(), capture_output=True, text=True, timeout=120)
        return r.returncode, r.stdout + r.stderr

    def record(self, target):
        return (target / "restore.txt").read_text().splitlines()

    # --- (1) lock order ---------------------------------------------------------------------------------------------
    def test_KilitSirasi_UnitySlotuTutulurkenPlayerSnapshot_64SatirDizinYokPlayerSlotuYok(self):
        for scope in ("player", "all"):
            target = self.root / f"ug-{scope}"
            rc, text = self.under_unity(target, scope)
            self.assertEqual(rc, 64, text)
            self.assertIn(LOCK_LINE, text)
            self.assertFalse(target.exists(), f"{scope}: nothing may be written before the refusal")
            self.assertEqual(self.slot_files("player"), [], "no player slot may be taken")
            self.assertEqual(self.slot_files("queue/player"), [])
        self.assertEqual(self.slot_files("unity"), [], "the fixture released its unity slot")

    def test_KilitSirasi_AtaPlayerTutuyorCocukUnity_SiraDogru_SnapshotDevamEder(self):
        # D6a review SHOULD-1: player (ancestor) → unity (child) is the right order — the snapshot must not refuse.
        script = self.root / "dd-fixture-player-then-unity.zsh"
        script.write_text('#!/bin/zsh\n"$DD_SLOT_SH" acquire player dd-fixture-p > /dev/null || exit 91\n'
                          'zsh -c \'"$DD_SLOT_SH" acquire unity dd-fixture-u > /dev/null || exit 90; '
                          'zsh "$DD_GUARD_SH" snapshot "$1" --scope player; rc=$?; "$DD_SLOT_SH" release unity dd-fixture-u > /dev/null; exit $rc\' zsh "$1"\n'
                          'rc=$?; "$DD_SLOT_SH" release player dd-fixture-p > /dev/null; exit $rc\n')
        script.chmod(0o755)
        target = self.root / "ug-order-ok"
        r = subprocess.run([str(script), str(target)], env=self.env(), capture_output=True, text=True, timeout=120)
        self.assertNotIn("lock order", r.stdout + r.stderr)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.stop_sampler(target)

    def test_KilitSirasi_AtaPlayerCocukUnity_EditorVeAllKapsami_Cikis64(self):
        # G4 D8 queue reviews (06.10): the ancestor-player exception is --scope player only — an editor/all snapshot also
        # waits for the editor area (the player slot does not guard it) and would queue holding the unity slot
        script = self.root / "dd-fixture-player-then-unity-wide.zsh"
        script.write_text('#!/bin/zsh\n"$DD_SLOT_SH" acquire player dd-fixture-p > /dev/null || exit 91\n'
                          'zsh -c \'"$DD_SLOT_SH" acquire unity dd-fixture-u > /dev/null || exit 90; '
                          'zsh "$DD_GUARD_SH" snapshot "$1" --scope "$2"; rc=$?; "$DD_SLOT_SH" release unity dd-fixture-u > /dev/null; exit $rc\' zsh "$1" "$2"\n'
                          'rc=$?; "$DD_SLOT_SH" release player dd-fixture-p > /dev/null; exit $rc\n')
        script.chmod(0o755)
        for scope in ("editor", "all"):
            with self.subTest(scope=scope):
                target = self.root / f"ug-wide-{scope}"
                r = subprocess.run([str(script), str(target), scope], env=self.env(), capture_output=True, text=True, timeout=120)
                self.assertEqual(r.returncode, 64, r.stdout + r.stderr)
                self.assertIn(LOCK_LINE, r.stdout + r.stderr)
                self.assertFalse(target.exists(), "nothing may be written before the refusal")
        self.assertEqual(self.slot_files("unity"), [], "the fixture released its unity slot")
        self.assertEqual(self.slot_files("player"), [], "the fixture released its player slot")

    def test_KilitSirasi_BosOnceVarOlanDizin_IcindeHicbirSeyOlusmaz(self):
        target = self.root / "ug-pre"; target.mkdir()
        rc, text = self.under_unity(target, "player")
        self.assertEqual(rc, 64, text)
        self.assertEqual(list(target.iterdir()), [])

    def test_KilitSirasi_EditorKapsamiUnityTutulurken_Cikis64SirayiDuzelt(self):
        # C-00 05.10 (G4 D8 queue): window → unity for every scope — an editor-scope snapshot under a unity slot is refused
        target = self.root / "ug-editor"
        rc, text = self.under_unity(target, "editor")
        self.assertEqual(rc, 64, text)
        self.assertIn(LOCK_LINE, text)
        self.assertIn("fix the order", text)
        self.assertFalse(target.exists(), "nothing may be written before the refusal")
        self.assertEqual(self.slot_files("player"), [], "no player slot may be taken")

    def test_KilitSirasi_UnitySlotuYok_PlayerSnapshotDevamEder_RestorePlayerSlotunuBirakir(self):
        target = self.root / "ug-free"
        rc, text = self.guard("snapshot", target, "--scope", "player")
        self.assertEqual(rc, 0, text)
        self.assertEqual(len(self.slot_files("player")), 1, "the snapshot holds the player slot until restore")
        rc, text = self.guard("restore", target, "--scope", "player")
        self.assertEqual(rc, 0, text)
        self.assertEqual(self.slot_files("player"), [])

    def test_SlotHolds_SahipZinciri_0YabanciPid_1HicbirSeyYazmaz(self):
        slot = str(self.tools / "slot.sh")
        script = (f'"{slot}" acquire unity dd-fixture-h >/dev/null || exit 90\n'
                  f'( "{slot}" holds unity $$; echo "own=$?" )\n'   # a subshell of the owner: ancestor chain
                  f'"{slot}" holds unity 1; echo "init=$?"\n'
                  f'"{slot}" holds player $$; echo "player=$?"\n'
                  f'"{slot}" release unity dd-fixture-h >/dev/null\n'
                  f'"{slot}" holds unity $$; echo "after=$?"\n:\n')
        r = subprocess.run(["zsh", "-c", script], env=self.env(), capture_output=True, text=True, timeout=60)
        self.assertIn("own=0", r.stdout, r.stdout + r.stderr)
        self.assertIn("SLOT unity held by pid", r.stdout)
        for want in ("init=1", "player=1", "after=1"):
            self.assertIn(want, r.stdout, r.stdout + r.stderr)
        fresh = self.root / "slots-untouched"
        r = subprocess.run([slot, "holds", "unity"], env=self.env(DD_SLOT_BASE=str(fresh)), capture_output=True, text=True)
        self.assertEqual(r.returncode, 1)
        self.assertFalse(fresh.exists(), "holds is read-only: no slot directories created")

    # --- (2) uniform restore record ---------------------------------------------------------------------------------
    def assert_restore_record(self, target, scope, code, want):
        lines = self.record(target)
        self.assertEqual(lines[-1], f"exit={code}")
        self.assertIn(want, lines[-2])
        return lines

    def test_RestoreKaydi_DDTimelineBaskaYerde_RestoreTxtVeDizinTimeline(self):
        target = self.root / "ug-tl"; other = self.root / "other-timeline.txt"
        rc, text = self.guard("snapshot", target, "--scope", "editor", DD_TIMELINE=str(other))
        self.assertEqual(rc, 0, text)
        rc, text = self.guard("restore", target, "--scope", "editor", DD_TIMELINE=str(other))
        self.assertEqual(rc, 0, text)
        summary = [l for l in text.splitlines() if "userdata-guard restore scope=editor" in l][-1]
        self.assert_restore_record(target, "editor", 0, "userdata-guard restore scope=editor touched=0 verified=1")
        self.assertEqual(self.record(target)[-2], summary, "restore.txt carries the summary line unchanged")
        own = (target / "timeline.txt").read_text()
        self.assertIn(summary, own)
        self.assertIn("userdata-guard snapshot →", own)
        self.assertIn(summary, other.read_text(), "the caller's DD_TIMELINE still gets the lines")

    def test_RestoreKaydi_DDTimelineBos_DizinTimelineZamanCizelgesidir(self):
        target = self.root / "ug-empty"
        rc, text = self.guard("snapshot", target, "--scope", "editor", DD_TIMELINE="")
        self.assertEqual(rc, 0, text)
        rc, text = self.guard("restore", target, "--scope", "editor", DD_TIMELINE="")
        self.assertEqual(rc, 0, text)
        self.assert_restore_record(target, "editor", 0, "verified=1")
        tl = (target / "timeline.txt").read_text()
        self.assertEqual(tl.count("userdata-guard restore scope=editor"), 1, "one copy, not two")

    def test_RestoreKaydi_SnapshotYok_64Kaydedilir(self):
        target = self.root / "ug-none"
        rc, text = self.guard("restore", target, "--scope", "editor")
        self.assertEqual(rc, 64, text)
        self.assert_restore_record(target, "editor", 64, f"userdata-guard: no snapshot in {target}")

    def test_RestoreKaydi_BilinenIyiSapma_31KaydedilirPlayerSlotuBirakilir(self):
        target = self.root / "ug-kg"
        rc, text = self.guard("snapshot", target, "--scope", "player")
        self.assertEqual(rc, 0, text)
        with open(target / "snap/PR/p1.plist", "wb") as f:
            plistlib.dump({"Screenmanager Fullscreen mode": 3}, f)
        rc, text = self.guard("restore", target, "--scope", "player")
        self.assertEqual(rc, 31, text)
        # G4 D6 §2.6: the summary line is the LAST line on every restore exit (31 included); the refusal comes before it
        self.assertIn("REFUSED", text)
        self.assert_restore_record(target, "player", 31, "verified=0")
        self.assertEqual(self.slot_files("player"), [])

    def test_RestoreKaydi_KilitTutuluyor_75KaydedilirTekrarEkler(self):
        target = self.root / "ug-busy"
        rc, text = self.guard("snapshot", target, "--scope", "editor")
        self.assertEqual(rc, 0, text)
        # G4 D5 S7: only a live Player or Unity counts as a lock holder (by its command, not the recorded name) — a Unity-path
        # stand-in (sleep behind a Unity.app path; no -batchmode / -projectPath, so no Unity counter sees it)
        holder = self.root / "Unity.app/Contents/MacOS/Unity"; holder.parent.mkdir(parents=True); holder.symlink_to("/bin/sleep")
        h = subprocess.Popen([str(holder), "60"]); self.bg.append(h); time.sleep(0.3)
        lock = self.groot / "AS/Deaths Demolition" / PRODUCT / "prototype-profile.json.lock"
        lock.write_text(f"pid={h.pid}\nname={holder}\n")
        rc, text = self.guard("restore", target, "--scope", "editor")
        self.assertEqual(rc, 75, text)
        self.assertIn("NOT restored", text)   # G4 D6: the summary line (retry=same-dir) is the last line
        self.assert_restore_record(target, "editor", 75, "retry=same-dir")
        h.kill(); h.wait(timeout=10); lock.unlink()
        rc, text = self.guard("restore", target, "--scope", "editor")
        self.assertEqual(rc, 0, text)
        lines = self.assert_restore_record(target, "editor", 0, "verified=1")
        self.assertEqual([l for l in lines if l.startswith("exit=")], ["exit=75", "exit=0"], "one record per restore, appended")


if __name__ == "__main__":
    unittest.main(verbosity=2)
