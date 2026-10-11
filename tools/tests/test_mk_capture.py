#!/usr/bin/env python3
"""WP-110 (S-01 mk-capture, C-00 11.10) · tools/mk-capture.sh + tools/mk-capture/*.py on a throwaway fixture: copied tools in a
tempdir, HOME in the tempdir (DD_KPROJ, the canonical guard and every output stay there), a fake tools/player-run.sh that writes
what MkCaptureAgent writes (a small 216×384 master so the real Swift muxer stays fast), a fake guard and a fake qa/oos_rule.py.
The real tdmux / mp4probe are compiled once per class. No Unity, no Player, no real user data.
usage: python3 tools/tests/test_mk_capture.py"""
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import wave
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / "tests"))
import dd_paths  # noqa: E402
from test_td_capture import FAKE_GUARD, FAKE_OOS  # noqa: E402

# R4 rule 1: the snapshot's summary names the known-good source (FAKE_KGSRC=default: the user's values were not checked)
FAKE_GUARD_MK = FAKE_GUARD.replace("echo snapshot ok; exit 0; fi", 'echo "snapshot ok root=real kgSource=${FAKE_KGSRC:-env}"; exit 0; fi')
assert FAKE_GUARD_MK != FAKE_GUARD

FORMATS = "216x384,216x288,216x270,216x216"

# fake player-run: logs argv; writes the session MkCaptureAgent writes: summary.json, sync.json, audio-sync.tsv, the WAV and
# clips/01 (kept, 2 s of frames at session t 10..12, frames/ and/or frames-hud/ per -ddMkHud, one still) + clips/02 (not
# kept). FAKE_RELEASE: answers nothing. FAKE_AGENT_ERROR: error.json only. FAKE_BADFRAMES: clip 01's JPEGs are not images.
FAKE_PLAYER_RUN = r'''#!/usr/bin/env python3
import json, os, sys, wave, struct, math
from pathlib import Path
args = sys.argv[1:]
with open(os.environ["FAKE_LOG"], "a") as f:
    f.write(json.dumps(args) + "\n")
def flag(n):
    return args[args.index(n) + 1] if n in args else None
log = flag("-logFile")
if log:
    Path(log).write_text("" if os.environ.get("FAKE_RELEASE") else "[MkCapture] armed\n")
if os.environ.get("FAKE_RELEASE"):
    sys.exit(0)
out = Path(flag("-ddMkCapture"))
if os.environ.get("FAKE_AGENT_ERROR"):
    (out / "error.json").write_text(json.dumps({"wp": "WP-110", "stage": "scene-ready", "error": "camera mode is FirstPerson", "exitCode": 2}))
    sys.exit(2)
from PIL import Image
hud = flag("-ddMkHud")
streams = {"off": ["frames"], "on": ["frames-hud"], "both": ["frames", "frames-hud"]}[hud]
wav = flag("-ddAudioCapture")
w = wave.open(wav, "wb"); w.setnchannels(2); w.setsampwidth(2); w.setframerate(48000)
w.writeframes(b"".join(struct.pack("<hh", int(4000 * math.sin(n / 30.0)), 0) for n in range(48000 * 15)))
w.close()
(out / "audio-sync.tsv").write_text("# t_seconds\twav_frames_captured\n" + "".join("%.6f\t%d\n" % (t, 24000 + t * 48000) for t in range(15)))
(out / "sync.json").write_text(json.dumps({"wp": "WP-110", "audio": {"wav": wav, "sampleRate": 48000, "anchorT": 0.0,
                                                                     "anchorFrames": 24000, "wavOffsetSeconds": 0.5}}))
for n, kept in ((1, True), (2, False)):
    c = out / "clips" / ("%02d" % n)
    rows = []
    for s in streams:
        (c / s).mkdir(parents=True, exist_ok=True)
    for i in range(60):
        idx = 300 * n + i
        t = 10.0 * n + i / 30.0
        rows.append("%d\t%.6f" % (idx, t))
        for s in streams:
            p = c / s / ("f-%06d.jpg" % idx)
            if os.environ.get("FAKE_BADFRAMES") and n == 1:
                p.write_bytes(b"not a jpeg")
            else:
                Image.new("RGB", (216, 384), (i * 4, 60 if s == "frames" else 160, 90)).save(p, quality=90)
    (c / "frames.tsv").write_text("\n".join(rows) + "\n")
    for s in ("stills", "stills-hud"):
        if (s == "stills" and "frames" in streams) or (s == "stills-hud" and "frames-hud" in streams):
            (c / s).mkdir(exist_ok=True)
            Image.new("RGB", (216, 384), (200, 120, 40)).save(c / s / "s1-t10.35.png")
    (c / "clip.json").write_text(json.dumps({"wp": "WP-110", "clip": n, "kept": kept, "triggerKind": "Explosion", "trigger": 10.0 * n + 0.5,
        "peak": 10.0 * n + 1.0, "round": 14, "weapon": flag("-ddQaBotWeapon") or "AssaultRifle", "killsInClip": 7, "maxGroupKills": 6, "maxChainKills": 7,
        "crowdWithin20mAtTrigger": 22, "aliveAtTrigger": 31, "camera": flag("-ddMkCamera"), "lookOffsetAtTrigger": [0.4, 2.1],
        "hud": hud, "effectiveFps": 29.6, "stills": ["s1-t10.35.png"], "events": []}))
tree = flag("-ddTreePreset")
applied = tree if tree and not os.environ.get("FAKE_TREE_UNKNOWN") else None
(out / "summary.json").write_text(json.dumps({"wp": "WP-110", "stopReason": "duration", "durationSeconds": 25.0, "master": [216, 384],
    "hud": hud, "clipsKept": 1, "clipsDropped": 1, "events": {"total": 40}, "frames": {"sessionFps": 29.7}, "ring": {"peakMB": 12.0},
    "tree": {"asked": tree, "preset": applied, "v2": bool(applied), "nodesOwned": 18 if applied else 0, "presetNodes": 18 if applied else 0,
             "presetNodesOwned": 18 if applied else 0}}))
'''

SEGMENTS = """# name weapon start rounds life specials camera hud seconds clips music seed [threshold [mode [tree]]]
a1   Rl1  14  30  immortal  1  G  both  600  8  off  26  12  demolition  barrage_r15
c1   -    1   -   mortal    0  O  off   120  -  on   73
"""


class MkCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bins = tempfile.TemporaryDirectory(prefix="dd-mk-capture-bin-")

    @classmethod
    def tearDownClass(cls):
        cls.bins.cleanup()

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-mk-capture-")
        self.root = Path(self.tmp.name).resolve()
        self.home = self.root / "home"
        self.kproj = self.home / dd_paths.KADRO_REL / "context/projects/deaths-demolition"
        tools = self.root / "repo" / "tools"
        (tools / "qa").mkdir(parents=True)
        for name in ("mk-capture.sh", "dd-env.sh", "dd_paths.py"):   # dd_paths: the R4 FINAL gate reads K/wp-110/FINAL-SHA.txt
            shutil.copy2(TOOLS / name, tools / name)
        shutil.copytree(TOOLS / "mk-capture", tools / "mk-capture")
        shutil.copytree(TOOLS / "td-capture", tools / "td-capture")
        self.write(tools / "player-run.sh", FAKE_PLAYER_RUN)
        self.write(tools / "qa" / "oos_rule.py", FAKE_OOS)
        self.write(self.kproj / "wp-079/driver/tools/userdata-guard.sh", FAKE_GUARD_MK)
        self.script = tools / "mk-capture.sh"
        self.app = self.root / "builds/dev/DeathsDemolition.app"; (self.app / "Contents").mkdir(parents=True)
        (self.app.parent / "build-report.json").write_text(json.dumps({"stamp": {"describe": "fixture-abc123", "commit": "abc123"}}))
        (self.app.parent / "report.json").write_text(json.dumps({"head": "abc123def", "mode": "dev"}))
        self.segs = self.root / "segments.txt"; self.segs.write_text(SEGMENTS)
        self.log = self.root / "calls.jsonl"; self.log.write_text("")

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def write(p, text):
        p.parent.mkdir(parents=True, exist_ok=True); p.write_text(text); p.chmod(0o755)

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("DD_", "FAKE_"))}
        e.update(HOME=str(self.home), TMPDIR=str(self.root), FAKE_LOG=str(self.log), DD_TD_CAPTURE_BIN=self.bins.name, DD_MK_MIN_FREE_GB="1",
                 DD_R4_FINAL_SHA="abc123def",   # C-00 12.10: the fixture build IS the R4 FINAL commit (report.json head)
                 DD_GUARD_KNOWN_GOOD="Screenmanager Fullscreen mode=1;Screenmanager Resolution Width=1496")
        import PIL
        site = str(Path(PIL.__file__).resolve().parents[1])
        e["PYTHONPATH"] = site + (os.pathsep + e["PYTHONPATH"] if e.get("PYTHONPATH") else "")
        e.update(extra)
        return {k: v for k, v in e.items() if v is not None}   # None: leave the variable out

    def run_mk(self, *extra, label="m1", segs=None, **env):
        return subprocess.run(["zsh", str(self.script), "--app", str(self.app), "--wp", "wp-110", "--label", label,
                               "--segments", str(segs or self.segs), "--formats", FORMATS, *extra],
                              capture_output=True, text=True, env=self.env(**env), timeout=600)

    def out(self, label="m1"):
        return self.kproj / "wp-110" / "capture" / label

    def calls(self):
        return [json.loads(l) for l in self.log.read_text().splitlines() if l.strip()]

    def guard_calls(self):
        p = Path(str(self.log) + ".guard")
        return p.read_text().splitlines() if p.exists() else []

    @staticmethod
    def arg(call, flag):
        return call[call.index(flag) + 1] if flag in call else None

    def test_TamYol_IkiSegment_DortFormatSesliMp4_FotoKirpim_KareleriSiler(self):
        r = self.run_mk()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()], "one guard window around both segments")
        a1, c1 = self.calls()
        # the segment table → the bot and the agent
        self.assertEqual(("Rl1", "14", "30", "1", "1"), (self.arg(a1, "-ddQaBotWeapon"), self.arg(a1, "-ddQaBotStartRound"),
                                                         self.arg(a1, "-ddQaBotRounds"), self.arg(a1, "-ddQaBotInvulnerable"), self.arg(a1, "-ddQaBotSpecial")))
        self.assertEqual(("G", "both", "600", "8", "1", "12"), (self.arg(a1, "-ddMkCamera"), self.arg(a1, "-ddMkHud"), self.arg(a1, "-ddMkSeconds"),
                                                                self.arg(a1, "-ddMkClips"), self.arg(a1, "-ddMkMusicOff"), self.arg(a1, "-ddMkThreshold")))
        self.assertNotIn("-ddQaBotWeapon", c1); self.assertNotIn("-ddQaBotInvulnerable", c1, "mortal: the bot can die")
        self.assertNotIn("-ddQaBotSpecial", c1)
        self.assertEqual(("demolition", "normal"), (self.arg(a1, "-ddDifficulty"), self.arg(c1, "-ddDifficulty")),
                         "WP-102: the mode column, and normal passed explicitly when the column is absent")
        self.assertEqual("barrage_r15", self.arg(a1, "-ddTreePreset")); self.assertNotIn("-ddTreePreset", c1, "WP-104: no column, no preset")
        self.assertEqual(("O", "off", "0", "12", "10", "73", "30"), (self.arg(c1, "-ddMkCamera"), self.arg(c1, "-ddMkHud"), self.arg(c1, "-ddMkMusicOff"),
                                                                     self.arg(c1, "-ddMkClips"), self.arg(c1, "-ddMkThreshold"), self.arg(c1, "-ddQaBotSeed"),
                                                                     self.arg(c1, "-ddQaBotRounds")), "'-' columns take the defaults")
        for call in (a1, c1):   # the sandbox and the window
            for flag in ("-ddProfilePath", "-ddUiDataRoot", "-ddTelemetryDir", "-ddAudioCapture"):
                self.assertIsNotNone(self.arg(call, flag), flag)
            self.assertEqual(("0", "topdown"), (self.arg(call, "-screen-fullscreen"), self.arg(call, "-ddCameraMode")))
        # a1 (hud both): 4 formats × 2 streams, every MP4 sized and with AAC sound; clip 02 (not kept) produced nothing
        clips = json.loads((self.out() / "a1" / "clips.json").read_text())
        self.assertEqual([1], [c["clip"] for c in clips["clips"]])
        c = clips["clips"][0]
        self.assertTrue(c["ok"], c["notes"]); self.assertEqual(8, len(c["videos"]))
        for v in c["videos"]:
            m = re.search(r"-(\d+)x(\d+)(-hud)?\.mp4$", v["file"])
            w, h = int(m.group(1)), int(m.group(2))
            self.assertEqual((w, h), (v["probe"]["video"]["width"], v["probe"]["video"]["height"]), v["file"])
            self.assertEqual("aac", v["probe"]["audio"]["codec"], v["file"])
        self.assertEqual(4, sum(v["file"].endswith("-hud.mp4") for v in c["videos"]))
        self.assertEqual(("Explosion", 14, "Rl1", 7, 22, "G"), (c["triggerKind"], c["round"], c["weapon"], c["killsInClip"],
                                                                c["crowdWithin20mAtTrigger"], c["camera"]))
        self.assertEqual((6, 7), (c["maxGroupKills"], c["maxChainKills"]), "S-11 12.10: the blast's and the chain's body counts")
        self.assertIsNotNone(c["avError"]); self.assertLessEqual(c["avError"], 0.1)
        self.assertEqual({"weapon": "Rl1", "start": "14", "life": "immortal", "specials": "1", "music": "off", "seed": "26", "mode": "demolition", "tree": "barrage_r15"}, clips["meta"])
        # stills: PNG + JPEG per format, HUD-less and HUD
        self.assertEqual(16, len(c["stills"]))
        self.assertTrue((self.out() / "a1" / "stills" / "a1-01-s1-t10.35-216x216-hud.jpg").exists())
        # raw frames gone once every MP4 probed; clip.json and the alignment record stay
        clip_dir = self.out() / "a1" / "clips" / "01"
        self.assertFalse((clip_dir / "frames").exists()); self.assertFalse((clip_dir / "frames-hud").exists())
        self.assertFalse(any(clip_dir.glob("work*")))
        self.assertTrue((clip_dir / "clip.json").exists()); self.assertTrue((clip_dir / "align.json").exists())
        self.assertTrue(c["framesRemoved"])
        # c1 (hud off): 4 HUD-less MP4s
        c1rec = json.loads((self.out() / "c1" / "clips.json").read_text())["clips"][0]
        self.assertEqual(4, len(c1rec["videos"])); self.assertFalse(any(v["file"].endswith("-hud.mp4") for v in c1rec["videos"]))
        # the record
        rec = json.loads((self.out() / "capture.json").read_text())
        self.assertEqual(["a1", "c1"], [s["segment"] for s in rec["segments"]]); self.assertEqual(0, rec["guardRestore"])
        self.assertEqual(12, sum(1 for f in rec["files"] if f.endswith(".mp4")))
        self.assertFalse(any("/frames" in f or f.endswith(".wav") for f in rec["files"]), "raw frames / WAV are not deliverables")
        sums = (self.out() / "SHA256SUMS").read_text().splitlines()
        self.assertEqual(len(rec["files"]), len(sums))

    def test_AgacOnAyari_Uygulanmadiysa_Cikis1(self):
        """WP-104: the game ignores an unknown -ddTreePreset; summary.json tree says what applied — the segment fails, honestly."""
        r = self.run_mk(FAKE_TREE_UNKNOWN="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("a1: the tree preset did not apply — asked barrage_r15, applied None", r.stderr)
        self.assertNotIn("c1: the tree preset", r.stderr, "a segment without a preset is not checked")

    def test_R4FinalKapisi_YalnizFinalBuildi_KuruKosuMuaf(self):
        """C-00 12.10 (the user: the Instagram pictures are R4 as finished): a shoot only on the R4 FINAL PASS build — the
        build's commit against --final-sha / DD_R4_FINAL_SHA / K/wp-110/FINAL-SHA.txt; a dry* label is exempt and recorded dry."""
        r = self.run_mk(label="s1", DD_R4_FINAL_SHA=None)
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertIn("R4 FINAL SHA is unknown", r.stderr)
        r = self.run_mk(label="s2", DD_R4_FINAL_SHA="fff0000")
        self.assertEqual(64, r.returncode); self.assertIn("the build is abc123def", r.stderr)
        self.assertEqual(64, self.run_mk("--final-sha", "zz", label="s3").returncode, "not hex")
        self.assertEqual([], self.calls()); self.assertEqual([], self.guard_calls(), "refused before anything opened")
        (self.kproj / "wp-110").mkdir(parents=True, exist_ok=True)
        (self.kproj / "wp-110" / "FINAL-SHA.txt").write_text("abc123d R4 FINAL PASS (C-00)\n")
        r = self.run_mk(label="s4", DD_R4_FINAL_SHA=None)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        cap = json.loads((self.out("s4") / "capture.json").read_text())
        self.assertEqual((False, "abc123d", "abc123def"), (cap["dry"], cap["finalGate"]["finalSha"], cap["finalGate"]["buildHead"]))
        self.assertTrue(cap["finalGate"]["finalShaSource"].endswith("FINAL-SHA.txt"))
        self.assertEqual(64, self.run_mk("--final-sha", "fff0000", label="s5", DD_R4_FINAL_SHA=None).returncode, "the flag wins over the file")
        r = self.run_mk(label="dry-9", DD_R4_FINAL_SHA="fff0000")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertTrue(json.loads((self.out("dry-9") / "capture.json").read_text())["dry"], "a dry run is exempt and recorded dry")

    def test_KareleriSakla_HicbirSeySilinmez(self):
        r = self.run_mk("--keep-frames")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual(60, len(list((self.out() / "a1" / "clips" / "01" / "frames-hud").glob("f-*.jpg"))))

    def test_MuxBasarisiz_Cikis1_KarelerKalir(self):
        r = self.run_mk(FAKE_BADFRAMES="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("a clip did not mux", r.stderr)
        self.assertTrue(any((self.out() / "a1" / "clips" / "01" / "frames").glob("f-*.jpg")), "a failed clip keeps its frames for a retry")
        self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_KotuSegmentTablosu_Cikis64_HicbirSeyKosmaz(self):
        bad_tables = (
            "a1 Rl1 14 30 immortal 1 G both 600 8 off\n",                  # 11 columns
            "A1 Rl1 14 30 immortal 1 G both 600 8 off 26\n",               # name case
            "a1 Rl1 0 30 immortal 1 G both 600 8 off 26\n",                # start 0
            "a1 Rl1 14 30 forever 1 G both 600 8 off 26\n",                # life
            "a1 Rl1 14 30 immortal 1 X both 600 8 off 26\n",               # camera
            "a1 Rl1 14 30 immortal 1 G all 600 8 off 26\n",                # hud
            "a1 Rl1 14 30 immortal 1 G both 30 8 off 26\n",                # seconds < 60
            "a1 Rl1 14 30 immortal 1 G both 600 8 loud 26\n",              # music
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26\na1 - 1 - mortal 0 O off 120 - on 73\n",   # duplicate
            "a1 Rl1 14 30 immortal 1 G both 600 0 off 26\n",                # clips 0 (gt NOTE)
            "a1 Rl1 14 30 immortal 1 G both 600 61 off 26\n",               # clips 61
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 99999999999\n",       # seed above int32
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26 0\n",              # threshold 0
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26 250\n",            # threshold 250
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26 12 easy\n",        # mode (WP-102)
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26 12 hard Bad-Tree\n",   # tree preset name (WP-104)
            "a1 Rl1 14 30 immortal 1 G both 600 8 off 26 12 hard shot_r10 x\n",   # 16 columns
            "# only a comment\n",
        )
        for i, table in enumerate(bad_tables):
            with self.subTest(table=table):
                p = self.root / ("bad%d.txt" % i); p.write_text(table)
                self.assertEqual(64, self.run_mk(segs=p, label="b%d" % i).returncode)
        self.assertEqual([], self.calls()); self.assertEqual([], self.guard_calls())
        for extra in (["--window", "big"], ["--formats", "1080"], ["--nope"]):
            with self.subTest(extra=extra):
                self.assertEqual(64, self.run_mk(*extra, label="x").returncode)

    def test_KgEnvYok_Cikis64_HicbirSeyKosmaz(self):
        """R4 rule 1: no DD_GUARD_KNOWN_GOOD → refused before the guard."""
        r = self.run_mk(DD_GUARD_KNOWN_GOOD="")
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertIn("DD_GUARD_KNOWN_GOOD", r.stderr)
        self.assertEqual([], self.calls()); self.assertEqual([], self.guard_calls())

    def test_SnapshotKgDefault_Cikis64_HicbirSegmentYok_RestoreKosar(self):
        r = self.run_mk(FAKE_KGSRC="default")
        self.assertEqual(64, r.returncode, r.stdout + r.stderr); self.assertIn("kgSource=env", r.stderr)
        self.assertEqual([], self.calls()); self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_SnapshotHataPencereAcik_HicbirSegmentKosmaz_Cikis1(self):
        """gt S5 (11.10): the snapshot failed but left its window open — restore it, launch nothing (td-capture parity)."""
        r = self.run_mk(FAKE_SNAP_OPEN_RC="31")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr); self.assertIn("nothing launched", r.stderr)
        self.assertEqual([], self.calls()); self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_HamButce_BosDiskleSinirli_AjanaGecer_1GBAltindaDurur(self):
        """gt M1 (11.10): every segment's raw budget = min(--raw-gb, free disk − the floor), handed to the agent."""
        # a fixed free disk (40 GB) through a fake df on PATH: the real one moves while other sessions write (12.10: the
        # disk fell from 28 to 27 GB between this test's reading and the script's, and the disk stop fired first)
        free = 40
        fake = self.root / "fakebin"
        self.write(fake / "df", "#!/bin/sh\necho 'Filesystem 1024-blocks Used Available Capacity Mounted'\n"
                                "echo \"fake 999999999 1 %d 1%% /\"\n" % (free * 1048576 + 512))
        path = "%s%s%s" % (fake, os.pathsep, os.environ.get("PATH", ""))
        run = self.run_mk
        self.run_mk = lambda *a, **k: run(*a, **dict(k, PATH=path))
        r = self.run_mk("--raw-gb", "3", "--ring-mb", "256", "--hud-safe")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        a1 = self.calls()[0]
        self.assertEqual("1", self.arg(a1, "-ddMkHudSafe"), "S-11: the HUD inside the Reels safe rectangle")
        self.assertEqual(str(min(3, free - 1)), self.arg(a1, "-ddMkRawGB")); self.assertEqual("256", self.arg(a1, "-ddMkRingMB"))
        r = self.run_mk(label="m2", DD_MK_MIN_FREE_GB=str(free - 2))   # 2 GB above the floor
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual("2", self.arg(self.calls()[2], "-ddMkRawGB"))
        self.assertEqual("0", self.arg(self.calls()[2], "-ddMkHudSafe"), "off by default")
        r = self.run_mk(label="m3", DD_MK_MIN_FREE_GB=str(free))   # 0 GB above the floor: nothing launches
        self.assertEqual(74, r.returncode, r.stdout + r.stderr); self.assertIn("raw budget", r.stderr)
        self.assertEqual(4, len(self.calls()))
        r = self.run_mk(label="m4", DD_MK_MIN_FREE_GB=str(free + 1))   # below the floor itself: the disk stop
        self.assertEqual(74, r.returncode, r.stdout + r.stderr); self.assertIn("GB free before segment", r.stderr)
        self.assertEqual(4, len(self.calls()))
        for bad in (["--raw-gb", "0"], ["--ring-mb", "8"]):
            with self.subTest(bad=bad):
                self.assertEqual(64, self.run_mk(*bad, label="x").returncode)

    def test_DusurulenKlipKareleri_Silinir_FazlaFormatKaydedilir(self):
        r = self.run_mk("--formats", FORMATS + ",1080x1920")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertFalse((self.out() / "a1" / "clips" / "02" / "frames").exists(), "gt NOTE: a dropped clip's raw frames go too")
        c = json.loads((self.out() / "a1" / "clips.json").read_text())["clips"][0]
        self.assertEqual(["1080x1920"], c["skippedFormats"])

    def test_EtiketVar_Cikis64(self):
        self.out().mkdir(parents=True)
        self.assertEqual(64, self.run_mk().returncode)
        self.assertEqual([], self.calls())

    def test_ReleaseBuild_Cikis3_RestoreYineKosar_IkinciSegmentAcilmaz(self):
        r = self.run_mk(FAKE_RELEASE="1")
        self.assertEqual(3, r.returncode, r.stdout + r.stderr)
        self.assertIn("DEV build", r.stderr)
        self.assertEqual(1, len(self.calls()), "a release build: the next segment is not tried")
        self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_AjanReddetti_Cikis1_NedenYazilir(self):
        r = self.run_mk(FAKE_AGENT_ERROR="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("the capture agent refused", r.stderr); self.assertIn("FirstPerson", r.stderr)

    def test_DiskAz_Cikis74_HicbirSegmentAcilmaz_RestoreKosar(self):
        r = self.run_mk(DD_MK_MIN_FREE_GB="999999")
        self.assertEqual(74, r.returncode, r.stdout + r.stderr)   # gt NOTE: 75 is the guard's
        self.assertEqual([], self.calls())
        self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_Guard32_OosKabul_Cikis0_Ret_Cikis32(self):
        self.assertEqual(0, self.run_mk(FAKE_RESTORE_RC="32", FAKE_OOS="0").returncode)
        self.assertEqual(32, self.run_mk(label="m2", FAKE_RESTORE_RC="32", FAKE_OOS="1").returncode)


class MkClipsUnitTests(unittest.TestCase):
    """mk_clips.py pieces without a muxer."""

    def setUp(self):
        sys.path.insert(0, str(TOOLS / "mk-capture"))
        import mk_clips
        self.m = mk_clips

    def test_Formatlar_CiftVeWxH(self):
        self.assertEqual([(1080, 1920), (1080, 1080)], self.m.parse_formats("1080x1920,1080x1080"))
        for bad in ("1080", "1081x1920", "axb"):
            with self.subTest(bad=bad):
                with self.assertRaises(ValueError):
                    self.m.parse_formats(bad)

    def test_AvHatasi_YalnizKlibeDegenSaniyeler(self):
        """dry-2 11.10: clip 01 reported 0.136 from the margin anchor after its last frame (t' 22.01 > 21.71)."""
        align = {"perSecond": [[-1.4, 0.5], [-0.4, 0.02], [0.6, 0.03], [10.0, 0.024], [21.0, 0.05], [22.013, 0.136]]}
        self.assertEqual(0.05, self.m.clip_av_error(align, 21.706))
        self.assertEqual(0.136, self.m.clip_av_error(align, 22.5))
        self.assertEqual(0.0, self.m.clip_av_error({}, 10.0))

    def test_MerkezKirpma(self):
        self.assertEqual((0, 285, 1080, 1635), self.m.crop_box(1080, 1920, 1080, 1350))
        self.assertEqual((0, 420, 1080, 1500), self.m.crop_box(1080, 1920, 1080, 1080))

    def test_IsMasasi_KlipZamaninaGoreKaydirilir(self):
        with tempfile.TemporaryDirectory() as d:
            seg = Path(d); clip = seg / "clips" / "01"; (clip / "frames").mkdir(parents=True)
            for idx in (5, 6):
                (clip / "frames" / ("f-%06d.jpg" % idx)).write_bytes(b"x")
            (clip / "frames.tsv").write_text("5\t20.000000\n6\t20.033000\n7\t20.066000\n")   # 7 has no file: left out
            (seg / "audio-sync.tsv").write_text("# h\n10.000000\t480000\n19.000000\t960000\n20.000000\t1008000\n21.000000\t1056000\n30.000000\t1488000\n")
            (seg / "sync.json").write_text(json.dumps({"audio": {"anchorT": 0.0, "anchorFrames": 24000, "wavOffsetSeconds": 0.5}}))
            w = wave.open(str(seg / "audio.wav"), "wb"); w.setnchannels(1); w.setsampwidth(2); w.setframerate(48000); w.writeframes(b"\0\0" * 10); w.close()
            audio = self.m.lay_work(seg, clip, "frames", 20.0, clip / "work", 20.033)
            self.assertEqual(["5\t0.000000", "6\t0.033000"], (clip / "work" / "frames.tsv").read_text().splitlines())
            anchors = [l.split("\t")[0] for l in (clip / "work" / "audio-sync.tsv").read_text().splitlines() if not l.startswith("#")]
            self.assertEqual(["-1.000000", "0.000000", "1.000000"], anchors, "only the clip's anchors ± 1.5 s (dry-1 11.10)")
            self.assertEqual((-20.0, 20.5), (audio["anchorT"], audio["wavOffsetSeconds"]))
            self.assertTrue((clip / "work" / "audio.wav").is_symlink())
            self.assertEqual(0, audio["anchorsDropped"], "rows without a realtime column are kept")

    def test_DuraksamaSatiri_KendiSaatindenKopuk_Atilir(self):
        """dry-3 12.10: the session's stop row said t = the stop time, its realtime / dsp / frames were read 2.4 s later
        (the raw-budget drain); kept, it made clip 03's avError 1.52. A row whose t is off its own realtime is dropped."""
        with tempfile.TemporaryDirectory() as d:
            seg = Path(d); clip = seg / "clips" / "03"; (clip / "frames").mkdir(parents=True)
            (clip / "frames" / "f-000001.jpg").write_bytes(b"x")
            (clip / "frames.tsv").write_text("1\t68.000000\n")
            (seg / "audio-sync.tsv").write_text("# h\n" + "".join("%.6f\t%d\t%.6f\t%.6f\t0\n" % (t, 48000 * t, t + 16.7, t + 16.29) for t in (67.0, 68.0, 69.0))
                                                + "69.899038\t4018176\t88.128000\t87.713506\t0\n")
            (seg / "sync.json").write_text(json.dumps({"audio": {"anchorT": 0.0}}))
            audio = self.m.lay_work(seg, clip, "frames", 68.0, clip / "work", 69.9)
            anchors = [l.split("\t")[0] for l in (clip / "work" / "audio-sync.tsv").read_text().splitlines() if not l.startswith("#")]
            self.assertEqual(["-1.000000", "0.000000", "1.000000"], anchors, "the stop row (t 69.899, realtime 87.71) is dropped")
            self.assertEqual(1, audio["anchorsDropped"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
