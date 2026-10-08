#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · tools/td-capture.sh on a throwaway fixture: copied tools in a tempdir, HOME in the tempdir
(so DD_KPROJ, the canonical guard and every output stay there), a fake tools/player-run.sh that writes what the dev-build
capture agent writes, a fake guard and a fake qa/oos_rule.py. The real Swift muxer and probe are compiled once per class.
No Unity, no Player, no real user data. usage: python3 tools/tests/test_td_capture.py"""
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import dd_paths  # noqa: E402

# fake player-run: logs argv; in stills mode writes one PNG per pose + poses.json; in video mode frames, frames.tsv,
# sync.json, summary.json and (with -ddAudioCapture) a WAV — like TdCaptureAgent. FAKE_RELEASE: answers nothing (a
# release build). FAKE_MISS: the last pose is a navmesh miss. FAKE_SLEEP: hangs (for the signal test). FAKE_CROWDSHORT:
# poses.json reports that many crowd40 poses that did not fill.
FAKE_PLAYER_RUN = r'''#!/usr/bin/env python3
import json, os, sys, time, wave, struct, math
from pathlib import Path
args = sys.argv[1:]
with open(os.environ["FAKE_LOG"], "a") as f:
    f.write(json.dumps(args) + "\n")
def flag(n):
    return args[args.index(n) + 1] if n in args else None
log = flag("-logFile")
if log:
    Path(log).write_text("" if os.environ.get("FAKE_RELEASE") else "[TdCapture] armed\n")
if os.environ.get("FAKE_SLEEP"):
    Path(os.environ["FAKE_LOG"] + ".sleeping").write_text(str(os.getpid()))
    time.sleep(60)
if os.environ.get("FAKE_RELEASE"):
    sys.exit(0)
out = Path(flag("-ddTdCapture"))
from PIL import Image
if flag("-ddTdCaptureMode") == "stills":
    st = out / "stills"; st.mkdir(parents=True, exist_ok=True)
    poses = [l.split("#")[0].split() for l in Path(flag("-ddTdPoses")).read_text().splitlines() if l.split("#")[0].strip()]
    rec = []
    for i, (k, x, z) in enumerate((p[:3] for p in poses), 1):
        miss = os.environ.get("FAKE_MISS") and i == len(poses)
        if not miss:
            Image.new("RGB", (64, 36), (i * 20, 90, 140)).save(st / ("%02d-%s.png" % (i, k)))
        rec.append({"key": k, "x": float(x), "z": float(z), "status": "navmesh-miss" if miss else "ok"})
    (st / "poses.json").write_text(json.dumps({"seed": int(flag("-ddTdSeed")), "poses": rec,
                                               "crowdShort": int(os.environ.get("FAKE_CROWDSHORT", "0"))}))
    sys.exit(3 if os.environ.get("FAKE_MISS") else 0)
v = out / "video"; fr = v / "frames"; fr.mkdir(parents=True, exist_ok=True)
rows = []
for i in range(60):
    Image.new("RGB", (64, 36), (i * 4, 40, 80)).save(fr / ("f-%06d.jpg" % i), quality=90)
    rows.append("%d\t%.6f" % (i, i / 30.0))
(v / "frames.tsv").write_text("\n".join(rows) + "\n")
wav = flag("-ddAudioCapture")
off = None
if wav and os.environ.get("FAKE_NOWAV"):   # the WAV never armed, but sync.json still names an offset → a silent MP4
    off = 0.5
elif wav:
    w = wave.open(wav, "wb"); w.setnchannels(2); w.setsampwidth(2); w.setframerate(48000)
    w.writeframes(b"".join(struct.pack("<hh", int(5000 * math.sin(n / 20.0)), 0) for n in range(48000 * 3)))
    w.close(); off = 0.5
phases = {"loadingStart": 0.4, "loadingEnd": 3.1, "menu": 3.3, "fighting": 8.0} if flag("-ddTdCaptureMode") == "boot" else None
(v / "sync.json").write_text(json.dumps({"audio": {"wavOffsetSeconds": off, "sampleRate": 48000}, "phases": phases}))
lag = float(os.environ.get("FAKE_AVDRIFT", "0"))   # the audio clock loses this much by t = 2 s
os.environ.get("FAKE_NOANCHORS") or (v / "audio-sync.tsv").write_text("# t_seconds\twav_frames_captured\n" + "".join(
    "%.6f\t%d\n" % (t, 24000 + round((t - lag * t / 2) * 48000)) for t in (0.0, 1.0, 2.0)))
(v / "summary.json").write_text(json.dumps({"framesWritten": 60, "effectiveFps": float(os.environ.get("FAKE_FPS", "29.5")),
                                         "audio": {"driftSeconds": 0.0}}))
'''

FAKE_GUARD = r'''#!/bin/zsh
echo "$1 $2 $4" >> "$FAKE_LOG.guard"
if [ "$1" = snapshot ]; then
  [ -n "${FAKE_SNAP_SLEEP:-}" ] && { echo $$ > "$FAKE_LOG.snapping"; sleep "$FAKE_SNAP_SLEEP"; }
  mkdir -p "$2"; [ -n "${FAKE_SNAP_OPEN_RC:-}" ] && { : > "$2/before.txt"; exit "$FAKE_SNAP_OPEN_RC"; }
  echo snapshot ok; exit 0; fi
rc=${FAKE_RESTORE_RC:-0}
echo "userdata-guard restore scope=$4 touched=0 verified=1"
exit $rc
'''

FAKE_OOS = r'''#!/usr/bin/env python3
import os, sys
print("fake oos_rule:", os.environ.get("FAKE_OOS", "0"))
sys.exit(int(os.environ.get("FAKE_OOS", "0")))
'''


class TdCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bins = tempfile.TemporaryDirectory(prefix="dd-td-capture-bin-")

    @classmethod
    def tearDownClass(cls):
        cls.bins.cleanup()

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-td-capture-")
        self.root = Path(self.tmp.name).resolve()
        self.home = self.root / "home"
        self.kproj = self.home / dd_paths.KADRO_REL / "context/projects/deaths-demolition"
        tools = self.root / "repo" / "tools"
        (tools / "qa").mkdir(parents=True)
        for name in ("td-capture.sh", "dd-env.sh", "td-capture-poses.txt"):
            shutil.copy2(TOOLS / name, tools / name)
        shutil.copytree(TOOLS / "td-capture", tools / "td-capture")
        self.write(tools / "player-run.sh", FAKE_PLAYER_RUN)
        self.write(tools / "qa" / "oos_rule.py", FAKE_OOS)
        self.write(self.kproj / "wp-079/driver/tools/userdata-guard.sh", FAKE_GUARD)
        self.script = tools / "td-capture.sh"
        self.app = self.root / "builds/dev/DeathsDemolition.app"; (self.app / "Contents").mkdir(parents=True)
        (self.app.parent / "build-report.json").write_text(json.dumps({"stamp": {"describe": "fixture-abc123", "commit": "abc123"}}))
        (self.app.parent / "report.json").write_text(json.dumps({"head": "abc123def", "mode": "dev"}))
        self.log = self.root / "calls.jsonl"; self.log.write_text("")

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def write(p, text):
        p.parent.mkdir(parents=True, exist_ok=True); p.write_text(text); p.chmod(0o755)

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith(("DD_", "FAKE_"))}
        e.update(HOME=str(self.home), TMPDIR=str(self.root), FAKE_LOG=str(self.log), DD_TD_CAPTURE_BIN=self.bins.name)
        # PIL may live in the REAL home's user site-packages, which a fixture HOME hides — hand its folder on explicitly
        import PIL
        site = str(Path(PIL.__file__).resolve().parents[1])
        e["PYTHONPATH"] = site + (os.pathsep + e["PYTHONPATH"] if e.get("PYTHONPATH") else "")
        e.update(extra)
        return e

    def run_cap(self, *extra, label="t1", **env):
        return subprocess.run(["zsh", str(self.script), "--app", str(self.app), "--wp", "wp-099", "--label", label,
                               *extra], capture_output=True, text=True, env=self.env(**env), timeout=300)

    def out(self, label="t1"):
        return self.kproj / "wp-099" / "capture" / label

    def calls(self):
        return [json.loads(l) for l in self.log.read_text().splitlines() if l.strip()]

    def guard_calls(self):
        p = Path(str(self.log) + ".guard")
        return p.read_text().splitlines() if p.exists() else []

    def test_TamYol_SekizKareMp4KayitVeSiraDogru(self):
        r = self.run_cap()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        o = self.out()
        self.assertEqual(8, len(list((o / "stills").glob("0*-*.png"))))
        self.assertTrue((o / "stills" / "contact.png").is_file())
        probe = json.loads((o / "video" / "probe.json").read_text())
        self.assertEqual(("avc1", "aac"), (probe["video"]["codec"], probe["audio"]["codec"]))
        self.assertAlmostEqual(2.0, probe["video"]["duration"], delta=0.05)
        rec = json.loads((o / "capture.json").read_text())
        self.assertEqual((26, 0, 0), (rec["seed"], rec["guardRestore"], rec["exit"]))
        self.assertEqual("ok", rec["videoQuality"]["verdict"]); self.assertEqual({"stills", "video"}, set(rec["videoQuality"]["loadAtLaunch"]))
        self.assertEqual((3, 0.0), (rec["videoQuality"]["avDrift"]["anchors"], rec["videoQuality"]["avDrift"]["maxAbs"]))
        self.assertEqual(("fixture-abc123", "abc123def"), (rec["build"]["stamp"]["describe"], rec["build"]["head"]))   # gt MUST-2
        self.assertNotIn("stutters", r.stderr)
        sums = (o / "SHA256SUMS").read_text()
        self.assertEqual(0.0, rec["videoQuality"]["avAlign"]["maxSecondErr"]); self.assertIn("video/audio-aligned.wav", sums)
        self.assertIn("video/td-t1.mp4", sums); self.assertIn("stills/contact.png", sums); self.assertNotIn("frames/", sums)
        stills, video = self.calls()
        for flag in ("-ddDirectToRun", "-ddTdPoses", "-ddProfilePath", "-ddUiDataRoot", "-ddTelemetryDir"):
            self.assertIn(flag, stills)
        self.assertEqual(("topdown", "1", "26", "45"), (stills[stills.index("-ddCameraMode") + 1],
                                                        stills[stills.index("-ddTdHoldSpawns") + 1], stills[stills.index("-ddTdSeed") + 1],
                                                        stills[stills.index("-ddTdCrowdWaitSeconds") + 1]))
        self.assertIn("-ddQaBot", video); self.assertNotIn("-ddQaBotRounds", video); self.assertNotIn("-ddDirectToRun", video)
        self.assertEqual("26", video[video.index("-ddQaBotSeed") + 1])
        self.assertTrue(video[video.index("-ddAudioCapture") + 1].endswith("video/audio.wav"))
        self.assertEqual([f"snapshot {o}/ug player", f"restore {o}/ug player"], self.guard_calls())

    def test_EtiketVar_Cikis64HicbirSeyKosmaz(self):
        self.out().mkdir(parents=True)
        r = self.run_cap()
        self.assertEqual(64, r.returncode, r.stderr)
        self.assertIn("never overwritten", r.stderr)
        self.assertEqual([], self.calls()); self.assertEqual([], self.guard_calls())

    def test_ReleaseBuild_Cikis3RestoreYineKosar(self):
        r = self.run_cap("--stills-only", FAKE_RELEASE="1")
        self.assertEqual(3, r.returncode, r.stdout + r.stderr)
        self.assertIn("DEV build", r.stderr)
        self.assertEqual(2, len(self.guard_calls()))

    def test_NavmeshKaybi_Cikis1(self):
        r = self.run_cap("--stills-only", FAKE_MISS="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("no NavMesh within 2 m", r.stderr)

    def test_KalabalikDolmadi_Cikis1_CrowdWaitIletilir(self):
        r = self.run_cap("--stills-only", "--crowd-wait", "35", FAKE_CROWDSHORT="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("crowdShort=1", r.stderr)
        stills = self.calls()[0]
        self.assertEqual("35", stills[stills.index("-ddTdCrowdWaitSeconds") + 1])
        self.assertEqual(8, len(list((self.out() / "stills").glob("0*-*.png"))))   # the frames are kept

    def test_DusukFps_UyariVeKayit_Cikis0(self):
        r = self.run_cap("--video-only", FAKE_FPS="8.6")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)   # the MP4 is still delivered — flagged, not failed
        self.assertIn("stutters — effective 8.6 fps < 24", r.stderr)
        self.assertEqual("low-fps", json.loads((self.out() / "capture.json").read_text())["videoQuality"]["verdict"])

    def test_AvKaymasi_UyariVeKayit_Cikis0(self):
        """gt MUST-1 (08.10): the audio clock 0.4 s behind real time by the stop → verdict av-drift, a warning, exit 0."""
        r = self.run_cap("--video-only", FAKE_AVDRIFT="0.4")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        q = json.loads((self.out() / "capture.json").read_text())["videoQuality"]
        self.assertEqual("av-drift", q["verdict"]); self.assertAlmostEqual(-0.4, q["avDrift"]["atStop"], delta=0.001)
        # aligned per anchor: each 1 s stretch is 0.2 s short → at most 0.2 s off inside it, exact at every anchor
        self.assertAlmostEqual(0.2, q["avAlign"]["maxSecondErr"], delta=0.001); self.assertAlmostEqual(0.2, q["avError"], delta=0.001)
        self.assertIn("sound and picture are up to 0.200 s apart", r.stderr)
        r = self.run_cap("--video-only", label="t2", FAKE_AVDRIFT="0.4", FAKE_FPS="8.6")
        self.assertEqual("low-fps+av-drift", json.loads((self.out("t2") / "capture.json").read_text())["videoQuality"]["verdict"])

    def test_DusmanlarAcik_HoldSpawns0(self):
        r = self.run_cap("--stills-only", "--with-enemies")
        self.assertEqual(0, r.returncode, r.stderr)
        stills = self.calls()[0]
        self.assertEqual("0", stills[stills.index("-ddTdHoldSpawns") + 1])

    def test_Guard32_OosKuraliKabul_Cikis0_Ret_Cikis32(self):
        r = self.run_cap("--stills-only", FAKE_RESTORE_RC="32", FAKE_OOS="0")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        r = self.run_cap("--stills-only", label="t2", FAKE_RESTORE_RC="32", FAKE_OOS="1")
        self.assertEqual(32, r.returncode, r.stdout + r.stderr)
        self.assertIn("NOT accepted", r.stderr)

    def test_SinyalLansmanSirasinda_RestoreKosar_Cikis143(self):
        p = subprocess.Popen(["zsh", str(self.script), "--app", str(self.app), "--wp", "wp-099", "--label", "t1", "--stills-only"],
                             env=self.env(FAKE_SLEEP="1"), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        end = time.time() + 30
        while not Path(str(self.log) + ".sleeping").exists() and time.time() < end:
            time.sleep(0.1)
        os.kill(p.pid, signal.SIGTERM)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        self.assertEqual("restore", self.guard_calls()[-1].split()[0])

    def test_SinyalSnapshotKuyruktayken_HicbirSeyBaslamaz_Cikis143(self):
        """gt SHOULD-2 (08.10): a TERM while the snapshot waits (the real guard: in the window queue, ≤ 1 h) stops it at
        once — no launch, no restore (no window opened), exit 143."""
        p = subprocess.Popen(["zsh", str(self.script), "--app", str(self.app), "--wp", "wp-099", "--label", "t1", "--stills-only"],
                             env=self.env(FAKE_SNAP_SLEEP="30"), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        end = time.time() + 30
        while not Path(str(self.log) + ".snapping").exists() and time.time() < end:
            time.sleep(0.1)
        t0 = time.time(); os.kill(p.pid, signal.SIGTERM)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        self.assertLess(time.time() - t0, 10)   # not after the snapshot's 30 s
        gpid = int(Path(str(self.log) + ".snapping").read_text())
        end = time.time() + 10
        while time.time() < end:
            try:
                os.kill(gpid, 0); time.sleep(0.1)
            except ProcessLookupError:
                break
        else:
            os.kill(gpid, signal.SIGKILL); self.fail("the snapshot was left running — it could open a window nobody restores")
        self.assertEqual([], self.calls()); self.assertEqual(["snapshot"], [l.split()[0] for l in self.guard_calls()])

    def test_SnapshotHataAmaPencereAcik_RestoreKosar_Cikis1(self):
        r = self.run_cap("--stills-only", FAKE_SNAP_OPEN_RC="75")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("its window is open (before.txt)", r.stderr)
        self.assertEqual([], self.calls()); self.assertEqual(["snapshot", "restore"], [l.split()[0] for l in self.guard_calls()])

    def test_HudKapali_IkiLansmanaIletilir_VarsayilanAcik(self):
        r = self.run_cap("--hud", "off")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        stills, video = self.calls()
        self.assertEqual(("off", "off"), (stills[stills.index("-ddTdHud") + 1], video[video.index("-ddTdHud") + 1]))
        r = self.run_cap("--stills-only", label="t2")
        self.assertEqual("on", self.calls()[-1][self.calls()[-1].index("-ddTdHud") + 1])

    def test_VideoBoot_ModBoot_AsamalarCaptureJsonda(self):
        """C-00 08.10 (S-10 08.10-2): the loading screen / menu logo clip — mode boot, phases into capture.json."""
        self.assertEqual(64, self.run_cap("--video-only", "--video-from", "boot", "--video-seconds", "20").returncode)
        r = self.run_cap("--video-only", "--video-from", "boot", "--video-seconds", "30")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        video = self.calls()[0]
        self.assertEqual("boot", video[video.index("-ddTdCaptureMode") + 1])
        self.assertNotIn("-ddQaBot", video); self.assertNotIn("-ddDirectToRun", video)   # the agent drives menu → run
        rec = json.loads((self.out() / "capture.json").read_text())
        self.assertEqual({"loadingStart": 0.4, "loadingEnd": 3.1, "menu": 3.3, "fighting": 8.0}, rec["video"]["phases"])

    def test_AnkrajYok_TekOfsetleSesYineVar(self):
        """no audio-sync.tsv (an older agent): align_audio exits 2 → the single wavOffsetSeconds, still with sound."""
        r = self.run_cap("--video-only", FAKE_NOANCHORS="1")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        rec = json.loads((self.out() / "capture.json").read_text())
        self.assertIsNone(rec["videoQuality"]["avAlign"]); self.assertEqual("aac", rec["video"]["probe"]["audio"]["codec"])
        self.assertIn("audio not aligned per anchor", (self.out() / "logs" / "timeline.txt").read_text())

    def test_SessizMp4_Cikis1(self):
        """C-00 08.10: the MP4 must carry the game sound — no AAC track → exit 1 (the MP4 is kept for a look)."""
        r = self.run_cap("--video-only", FAKE_NOWAV="1")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn("the MP4 has no audio track", r.stderr)
        self.assertIsNone(json.loads((self.out() / "video" / "probe.json").read_text())["audio"])

    def test_KotuArguman_Cikis64(self):
        for extra in (["--video-seconds", "5"], ["--size", "big"], ["--seed", "x"], ["--nope"],
                      ["--crowd-wait", "0"], ["--crowd-wait", "121"], ["--hud", "none"], ["--video-from", "menu"]):
            with self.subTest(extra=extra):
                self.assertEqual(64, self.run_cap(*extra).returncode)


if __name__ == "__main__":
    unittest.main()
