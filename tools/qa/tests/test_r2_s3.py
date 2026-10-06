"""WP-085 R2 S3 (S-08 06.10): tools/qa/r2_s3.py — the Dense-40 loudness/peak writer, on synthetic recordings."""
import contextlib
import io
import json
import math
import os
import sys
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import r2_gates  # noqa: E402
import r2_s3  # noqa: E402

RATE = 48000
COMMIT = "a" * 40
OFFSET = 3.2   # dsp time - capture seconds
TAKES_END = 1791248420.0   # 2026-10-06T01:00:20Z: every take ends here (the 40 s listening takes began 00:59:40), its two host
                           # samples at 01:00:00 / 01:00:05 cover that span within 30 s


def write_wav(path, data):
    pcm = (np.clip(data, -1.0, 32767 / 32768) * 32768).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(data.shape[1])
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def write_markers(path, seconds, alive=40, dense_from=1.0, dropped=0):
    lines = ["# seconds\trealtime\tkind\tdetail", "0.000\t1.000\tcapture\trate 48000 dsp 1024x4 speakers Stereo"]
    for s in range(int(seconds)):
        a = alive if s >= dense_from else 2
        lines.append(f"{s:.3f}\t{s + 1:.3f}\tdensity\tphase Fighting snapshot Run alive {a} tiers 0/0/0/0 voices 4/0 "
                     f"loops 0/0 actions 0 dsp {s + OFFSET:.6f}")
    if dropped is not None:
        lines.append(f"{seconds:.3f}\t{seconds + 1:.3f}\tcapture\tend frames {int(seconds * RATE)} dropped {dropped} channels 2")
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_telemetry(path, fire_times=(), delivered=None, explosions=0, overflow=0):
    delivered = len(fire_times) if delivered is None else delivered
    lines = ["# wp-065 audio playback telemetry v1", "# fixed_ring_capacity\t65536", f"# ring_overflow\t{overflow}",
             "# combat_event\tdelivered\tsuppressed", f"ClassicWeaponFire\t{delivered}\t0", f"Explosion\t{explosions}\t0",
             "# sequence\tframe\trealtime\tdsp_time\toutcome\tpool\tsound\tvictim\tcombat_event\tactive\tcapacity"]
    for i, t in enumerate(fire_times):
        lines.append(f"{2 * i}\t{i}\t{t:.5f}\t{t + OFFSET:.6f}\tRequested\tWorld\tWeaponAr1Fire\tNone\t-\t4\t16")
        lines.append(f"{2 * i + 1}\t{i}\t{t:.5f}\t{t + OFFSET:.6f}\tStarted\tWorld\tWeaponAr1Fire\tNone\t-\t4\t16")
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def dense_take(folder, mode="tp", seconds=8.0, shot_amp=0.25, alive=40, dropped=0, camera=None, stamped=True,
               commit=COMMIT, fires=True, complete=True, bed=0.002):
    """a Dense-40 run folder: a -54 dBFS noise bed with 30 ms 1 kHz shots every 0.5 s from 1.2 s, and its sidecars."""
    folder = Path(folder)
    (folder / "bot").mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(7)
    n = int(seconds * RATE)
    data = rng.normal(0.0, bed, (n, 2))   # default: a bed far under the -30 dBFS shot floor, so an unheard shot reads as one
    times = list(np.arange(1.2, seconds - 0.5, 0.5))
    burst = shot_amp * np.sin(2 * np.pi * 1000 * np.arange(int(0.03 * RATE)) / RATE)
    for t in times:
        a = int(t * RATE)
        data[a:a + burst.size, :] += burst[:, None]
    write_wav(folder / "mix.wav", data)
    write_markers(str(folder / "mix.wav") + ".markers.txt", seconds, alive=alive, dropped=dropped)
    # the bot delivers more ClassicWeaponFire cues than Fire voices start (R1: 80 delivered, 26 started)
    write_telemetry(str(folder / "mix.wav") + ".telemetry.tsv", times if fires else (), delivered=60 if fires else 0)
    info = {"stamped": stamped, "describe": "wp-085-r2", "commit": commit[:12] if stamped else "", "mode": "dev"}
    (folder / "bot" / "qa-report.json").write_text(json.dumps({"buildInfo": json.dumps(info), "load1Before": "",
                                                               "load1After": ""}), encoding="utf-8")
    (folder / "bot" / "bot-summary.json").write_text(json.dumps({"cameraMode": camera or r2_s3.CAMERA[mode],
                                                                 "meanFrameMs": 16.6}), encoding="utf-8")
    log = "[AudioDense40Evidence] dense checkpoint reached\n"
    (folder / "player.log").write_text(log + ("[AudioDense40Evidence] capture window complete\n" if complete else ""),
                                       encoding="utf-8")
    return folder


def a9_render(path, level_dbfs=-8.5, seconds=12.0, dropped=0, fires=19, explosions=6):
    """a 100 Hz square wave: |x| is constant, so the limiter's detector settles on its level exactly."""
    n = int(seconds * RATE)
    amp = 10 ** (level_dbfs / 20)
    square = amp * np.sign(np.sin(2 * np.pi * 100 * (np.arange(n) + 0.5) / RATE))
    write_wav(path, np.repeat(square[:, None], 2, axis=1))
    write_markers(str(path) + ".markers.txt", seconds, alive=6, dropped=dropped)
    write_telemetry(str(path) + ".telemetry.tsv", delivered=fires, explosions=explosions)
    return path


def listening_take(folder, name, w_sigma=0.1, r_sigma=0.05, shots=300):
    """A B C W R x2 in 4 s blocks: W and R independent noises, A = their sum (the stems split the mix exactly)."""
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(11)
    block = int(4.0 * RATE)
    order = "ABCWRABCWR"
    parts, rows = [], ["frame,variant"]
    for i, v in enumerate(order):
        w = rng.normal(0.0, w_sigma, (block, 2)) if w_sigma else np.zeros((block, 2))
        r = rng.normal(0.0, r_sigma, (block, 2))
        parts.append({"W": w, "R": r}.get(v, w + r))
        rows.append(f"{i * block},{v}")
    wav = folder / f"{name}_take.wav"
    write_wav(wav, np.concatenate(parts))
    (folder / f"{name}_blocks.csv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    write_markers(str(wav) + ".markers.txt", 40.0, alive=6)
    write_telemetry(str(wav) + ".telemetry.tsv", delivered=shots)
    return wav


def nunit(path, a9="Passed", listening="Passed"):
    cases = "".join(f'<test-case fullname="{name}" result="{result}"/>'
                    for name, result in ((r2_s3.TESTS["a9"], a9), (r2_s3.TESTS["listening"], listening)) if result)
    Path(path).write_text(f'<test-run result="Passed">{cases}</test-run>', encoding="utf-8")
    return path


class Rig:
    """one complete, quiet, passing set of S3 inputs in a temp folder; tests break one piece at a time."""

    def __init__(self, root):
        self.root = Path(root)
        self.dense = {m: dense_take(self.root / m, m) for m in ("tp", "fp")}
        self.a9 = a9_render(self.root / "a9-stack.wav")
        self.listen = self.root / "listening"
        listening_take(self.listen, "1_ustten")
        listening_take(self.listen, "2_birinci-sahis")
        self.xml = nunit(self.root / "pm.xml")
        self.load = self.root / "load.txt"
        self.load.write_text("2026-10-06T01:00:00Z { 2.10 2.40 2.30 }\n2026-10-06T01:00:05Z { 3.05 2.50 2.30 }\n",
                             encoding="utf-8")
        self.host = self.root / "cpu.txt"
        self.host.write_text("2026-10-06T01:00:00Z CPU usage: 41.0% user, 30.0% sys, 29.0% idle  · foreign 0\n"
                             "2026-10-06T01:00:05Z CPU usage: 60.0% user, 38.0% sys, 2.0% idle  · foreign 0\n", encoding="utf-8")
        self.out = self.root / "r2" / "r2-S3.json"
        self.takes_end = TAKES_END
        self.listening_end = None

    def end_takes(self, epoch):
        """every take's WAV written (its capture ended) at `epoch` on the next run — the span the host samples must cover."""
        self.takes_end = epoch

    def end_listening(self, epoch):
        """the listening takes end at `epoch`, the Dense-40 and A9 takes stay at end_takes (a window's later step)."""
        self.listening_end = epoch

    def stamp(self):
        for wav in self.root.rglob("*.wav"):   # takes a test rewrote too: the window's time, not the test's
            end = self.listening_end if self.listening_end is not None and self.listen in wav.parents else self.takes_end
            os.utime(wav, (end, end))

    def argv(self, *extra, load=True, host=True):
        args = [a for m, d in self.dense.items() for a in ("--dense", f"{m}={d}")]
        args += ["--a9", str(self.a9), "--listening", str(self.listen), "--xml", str(self.xml), "--dense-seconds", "10",
                 "--commit", COMMIT, "--out", str(self.out)]
        if load:
            args += ["--load", str(self.load)]
        if host:
            args += ["--host", str(self.host)]
        return args + list(extra)

    def run(self, *extra, load=True, host=True):
        self.stamp()
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(io.StringIO()):
            rc = r2_s3.main(self.argv(*extra, load=load, host=host))
        return rc, buf.getvalue()


class RigTestCase(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory()

    @classmethod
    def tearDownClass(cls):
        cls._tmp.cleanup()

    def setUp(self):
        self.dir = tempfile.mkdtemp(dir=self._tmp.name)
        self.rig = Rig(self.dir)


class WritesTests(RigTestCase):
    def test_a_complete_quiet_run_writes_the_contract_artifact_and_the_judge_passes_it(self):
        rc, out = self.rig.run()
        self.assertEqual(0, rc, out)
        data = json.loads(self.rig.out.read_text(encoding="utf-8"))
        self.assertEqual(("dd-r2-measure-v1", "S3", COMMIT), (data["schema"], data["gate"], data["commit"]))
        self.assertEqual(set(r2_s3.KEYS), set(data["values"]))
        self.assertTrue(all(isinstance(v, float) and math.isfinite(v) for v in data["values"].values()))
        self.assertEqual(5, data["samples"])          # tp + fp + A9 + two listening takes
        self.assertNotIn("note", data)
        gate = next(g for g in r2_gates.load_contract((Path(r2_s3.__file__).parent / "r2-gates.json").read_text(
            encoding="utf-8"))["gates"] if g["id"] == "S3")
        self.assertEqual("PASS", r2_gates.evaluate_gate(gate, COMMIT, measure=data)["status"])

    def test_a_value_outside_its_check_is_still_written_and_the_exit_says_fail(self):
        listening_take(self.rig.listen, "2_birinci-sahis", w_sigma=0.05, r_sigma=0.1)   # W 6 dB under R
        rc, out = self.rig.run()
        self.assertEqual(2, rc, out)
        data = json.loads(self.rig.out.read_text(encoding="utf-8"))
        self.assertAlmostEqual(-6.02, data["values"]["weaponMarginLu"], delta=0.3)
        self.assertIn("S3 FAIL", out)

    def test_a_repeated_camera_run_counts_and_the_worst_wins(self):
        rc, _ = self.rig.run()
        quiet = json.loads(self.rig.out.read_text(encoding="utf-8"))["values"]["truePeakDbtp"]
        louder = dense_take(self.rig.root / "tp2", "tp", shot_amp=0.6)
        rc, out = self.rig.run("--dense", f"tp={louder}")
        self.assertEqual(0, rc, out)
        data = json.loads(self.rig.out.read_text(encoding="utf-8"))
        self.assertEqual(6, data["samples"])
        self.assertIn("tp#2", data["takes"]["dense"])
        self.assertAlmostEqual(20 * math.log10(0.6), data["values"]["truePeakDbtp"], delta=0.1)
        self.assertGreater(data["values"]["truePeakDbtp"], quiet + 6)

    def test_commit_and_out_come_from_the_r2_environment_and_a_short_commit_is_usage(self):
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(64, r2_s3.main(["--commit", "abc123", "--out", str(self.rig.out)]))
        args = [a for a in self.rig.argv() if a not in ("--commit", COMMIT, "--out", str(self.rig.out))]
        self.rig.stamp()
        env = {"DD_R2_COMMIT": COMMIT, "DD_R2_MEASURE_DIR": str(self.rig.root / "env-r2")}
        saved = {k: r2_s3.os.environ.get(k) for k in env}
        try:
            r2_s3.os.environ.update(env)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, r2_s3.main(args))
        finally:
            for k, v in saved.items():
                if v is None:
                    r2_s3.os.environ.pop(k, None)
                else:
                    r2_s3.os.environ[k] = v
        self.assertEqual(COMMIT, json.loads((self.rig.root / "env-r2" / "r2-S3.json").read_text(encoding="utf-8"))["commit"])


class MeasureTests(unittest.TestCase):
    def test_the_true_peak_finds_the_peak_between_samples(self):
        # fs/8 sine, 22.5° off the sample grid: the samples top out 0.69 dB under the waveform's -6.02 dBFS crest
        n = RATE
        amp = 0.5
        x = amp * np.sin(2 * np.pi * (RATE / 8) * np.arange(n) / RATE + np.pi / 8)
        data = np.repeat(x[:, None], 2, axis=1)
        sample = 20 * math.log10(np.max(np.abs(data)))
        fir = 20 * math.log10(r2_s3.true_peak_fir(data))
        self.assertAlmostEqual(-6.71, sample, delta=0.02)
        self.assertAlmostEqual(-6.02, fir, delta=0.1)

    def test_the_momentary_maximum_of_a_stereo_1khz_sine_reads_its_level(self):
        # EBU Tech 3341 case 1: a 1 kHz sine at -23 dBFS in both channels reads -23 LUFS
        amp = 10 ** (-23 / 20)
        x = amp * np.sin(2 * np.pi * 1000 * np.arange(3 * RATE) / RATE)
        energy = r2_s3.acr.k_energy(np.repeat(x[:, None], 2, axis=1).astype(np.float32), RATE)
        lufs, _ = r2_s3.momentary_max(energy, RATE, r2_s3.M_HOP_S)
        self.assertAlmostEqual(-23.0, lufs, delta=0.1)

    def test_the_finer_hop_never_reads_lower_than_the_r1_grid(self):
        rng = np.random.default_rng(3)
        x = rng.normal(0.0, 0.05, (4 * RATE, 2))
        x[int(1.37 * RATE):int(1.52 * RATE)] *= 8   # a 150 ms burst off the 100 ms grid
        energy = r2_s3.acr.k_energy(x.astype(np.float32), RATE)
        self.assertGreaterEqual(r2_s3.momentary_max(energy, RATE, r2_s3.M_HOP_S)[0],
                                r2_s3.momentary_max(energy, RATE, r2_s3.R1_HOP_S)[0] - 1e-9)

    def test_the_limiter_gain_reduction_is_the_envelope_above_minus_8(self):
        with tempfile.TemporaryDirectory() as tmp:
            for level, gr in ((-6.0, 2.0), (-7.0, 1.0), (-8.5, 0.0)):
                detail, findings = r2_s3.measure_a9(a9_render(Path(tmp) / f"a9{level}.wav", level))
                self.assertEqual([], findings)
                self.assertAlmostEqual(gr, detail["preLimiterGrDb"], delta=0.02, msg=f"{level} dBFS")


class ThresholdTests(unittest.TestCase):
    """the artifact is judged by r2_gates with the contract's numbers: the limits themselves pass."""

    def judge(self, **changes):
        values = {"truePeakDbtp": -3.0, "momentaryMaxLufs": -15.0, "preLimiterGrDb": 0.0, "weaponMarginLu": 2.0}
        values.update(changes)
        return r2_s3.judge(r2_s3.artifact(values, {}, 5, COMMIT, []))["status"]

    def test_every_limit_is_inclusive(self):
        self.assertEqual("PASS", self.judge())
        for key, at, past in (("truePeakDbtp", -1.0, -0.999), ("momentaryMaxLufs", -13.0, -12.999),
                              ("preLimiterGrDb", 1.0, 1.001), ("weaponMarginLu", 1.0, 0.999)):
            self.assertEqual("PASS", self.judge(**{key: at}), key)
            self.assertEqual("FAIL", self.judge(**{key: past}), key)


class SourceTests(RigTestCase):
    """the source must be in the recording: one missing piece and nothing is written (exit 1, S3 BLOCKED)."""

    def assert_blocked(self, needle):
        rc, out = self.rig.run()
        self.assertEqual(1, rc, out)
        self.assertIn(needle, out)
        self.assertFalse(self.rig.out.exists())
        self.assertFalse(r2_s3.kirli_path(self.rig.out).exists())

    def test_fewer_than_40_alive(self):
        dense_take(self.rig.dense["tp"], "tp", alive=30)
        self.assert_blocked("max alive 30")

    def test_no_weapon_shot_in_the_recording(self):
        dense_take(self.rig.dense["fp"], "fp", fires=False)
        self.assert_blocked("ClassicWeaponFire delivered 0")

    def test_a_dense_mix_without_a_6_db_shot_rise_is_written_c00_exception(self):
        # C-00 06.10 (S3 gt N-1): a continuous Dense-40 bed keeps the shot under SABİT-KURALLAR 5.3's +6 dB rise; the
        # source rule is shots delivered + Fire voices started + shot-window peak >= -30 dBFS — the rise is only reported.
        dense_take(self.rig.dense["tp"], "tp", shot_amp=0.25, bed=0.08)
        rc, out = self.rig.run()
        self.assertEqual(0, rc, out)
        data = json.loads(self.rig.out.read_text(encoding="utf-8"))
        rise = data["takes"]["dense"]["tp"]["shotRiseDbMedian"]
        self.assertIsNotNone(rise)
        self.assertLess(rise, 6.0, "the fixture must sit under the 5.3 rise for this test to mean anything")
        self.assertGreaterEqual(data["takes"]["dense"]["tp"]["shotPeakMedianDbfs"], r2_s3.SHOT_PEAK_FLOOR_DBFS)
        self.assertIn("5.3 exception (C-00 06.10)", data["sourceRule"])

    def test_shots_that_are_not_heard(self):
        dense_take(self.rig.dense["fp"], "fp", shot_amp=0.0)
        self.assert_blocked("shot-window peak median")

    def test_dropped_capture_samples(self):
        dense_take(self.rig.dense["tp"], "tp", dropped=480)
        self.assert_blocked("capture dropped samples 480")

    def test_a_take_that_is_not_the_camera_it_claims(self):
        dense_take(self.rig.dense["tp"], "tp", camera="TopDown")
        self.assert_blocked("cameraMode 'TopDown'")

    def test_a_missing_camera_take(self):
        del self.rig.dense["fp"]
        self.assert_blocked("dense fp: no capture given")

    def test_an_unstamped_or_other_commit_build(self):
        dense_take(self.rig.dense["tp"], "tp", stamped=False)
        self.assert_blocked("unstamped")
        dense_take(self.rig.dense["tp"], "tp", commit="b" * 40)
        self.assert_blocked("is not " + COMMIT)

    def test_a_dense_run_that_never_completed_its_window(self):
        dense_take(self.rig.dense["fp"], "fp", complete=False)
        self.assert_blocked("capture window complete")

    def test_a_clipped_or_empty_a9_render(self):
        a9_render(self.rig.a9, level_dbfs=0.0)
        self.assert_blocked("clipped")
        a9_render(self.rig.a9, fires=0)
        self.assert_blocked("the stack needs")

    def test_a_silent_weapon_stem(self):
        listening_take(self.rig.listen, "1_ustten", w_sigma=0.0)
        self.assert_blocked("listening topdown")

    def test_the_named_tests_must_have_passed(self):
        nunit(self.rig.xml, a9="Failed")
        self.assert_blocked("A9_EnYuksekYigin_LimiterOncesiZarf_Olcum is Failed")
        nunit(self.rig.xml, listening=None)
        self.assert_blocked("DinlemePaketi_AB_Uret is not in the given XML")

    def test_a_dry_run_measures_and_never_writes(self):
        rc, out = self.rig.run("--dry-run")
        self.assertEqual(0, rc, out)
        self.assertIn("dry run: nothing written", out)
        self.assertFalse(self.rig.out.exists())


class KirliTests(RigTestCase):
    """SABİT-KURALLAR 5.8: a dirty run is ranking information — r2-S3.KIRLI.json with the note, never r2-S3.json."""

    def assert_kirli(self, rc, out, reason):
        self.assertEqual(3, rc, out)
        self.assertFalse(self.rig.out.exists())
        data = json.loads(r2_s3.kirli_path(self.rig.out).read_text(encoding="utf-8"))
        self.assertTrue(data["kirli"])
        self.assertTrue(data["note"].startswith("KİRLİ: "), data["note"])
        self.assertIn(reason, data["note"])
        self.assertEqual(set(r2_s3.KEYS), set(data["values"]))

    def test_a_foreign_heavy_process_during_the_window(self):
        with self.rig.host.open("a", encoding="utf-8") as f:
            f.write("2026-10-06T01:00:10Z CPU usage: 50.0% user, 30.0% sys, 20.0% idle  · foreign 1\n")
        self.assert_kirli(*self.rig.run(), "a foreign Unity / Player / Blender / ffmpeg in 1 of 3 host samples")

    def test_a_foreign_line_without_cpu_idle_still_counts_gt_b2(self):
        # an empty `top` leaves "<utc>  · foreign 2 · own 1": the criterion is the foreign process, idle is only reported
        with self.rig.host.open("a", encoding="utf-8") as f:
            f.write("2026-10-06T01:00:10Z  · foreign 2 · own 1\n")
        self.assert_kirli(*self.rig.run(), "a foreign Unity / Player / Blender / ffmpeg in 1 of 3 host samples")

    def test_host_samples_that_stop_before_the_takes_end_gt_b1(self):
        # the sampler died at 01:00:05 and the takes ran on to 01:05:00: a foreign process after it would go unseen
        self.rig.end_takes(TAKES_END + 280)
        self.assert_kirli(*self.rig.run(), "the last host sample 2026-10-06T01:00:05Z is 295 s before the last take ended 2026-10-06T01:05:00Z")

    def test_samples_that_start_after_the_takes_began_gt_b1(self):
        self.rig.end_takes(TAKES_END - 120)   # the takes ran 00:57:40-00:58:20, the sampler started at 01:00:00
        self.assert_kirli(*self.rig.run(), "the first host sample 2026-10-06T01:00:00Z is 140 s after the first take began")

    def test_a_gap_in_the_host_samples_gt_b1(self):
        self.rig.end_takes(TAKES_END + 100)   # 01:02:00: samples at 01:00:00, 01:00:05, then 01:02:10
        with self.rig.host.open("a", encoding="utf-8") as f:
            f.write("2026-10-06T01:02:10Z CPU usage: 50.0% user, 30.0% sys, 20.0% idle  · foreign 0\n")
        self.assert_kirli(*self.rig.run(), "a 125 s gap in the host samples after 2026-10-06T01:00:05Z")

    def test_the_span_ends_with_the_last_take_not_the_first_gt_b3(self):
        # Dense-40 and A9 inside the samples, the listening takes 2:40 later: the sampler stopped before the window's last step
        self.rig.end_listening(TAKES_END + 160)
        self.assert_kirli(*self.rig.run(), "the last host sample 2026-10-06T01:00:05Z is 175 s before the last take ended 2026-10-06T01:03:00Z")

    def test_a_sampler_that_starts_inside_the_window_gt_b3(self):
        # Dense-40 and A9 1:40 earlier than the listening takes: the first sample (01:00:00) lies between the first take's
        # start + 30 s and the last take's end — the window's first steps went unsampled
        self.rig.end_takes(TAKES_END - 100)
        self.rig.end_listening(TAKES_END)
        self.assert_kirli(*self.rig.run(), "the first host sample 2026-10-06T01:00:00Z is 92 s after the first take began 2026-10-06T00:58:28Z")   # A9: 12 s

    def test_samples_every_few_seconds_cover_the_takes(self):
        rc, out = self.rig.run()
        self.assertEqual(0, rc, out)
        host = json.loads(self.rig.out.read_text(encoding="utf-8"))["takes"]["host"]
        self.assertEqual(("2026-10-06T01:00:00Z", "2026-10-06T01:00:05Z"), (host["firstSample"], host["lastSample"]))

    def test_no_host_log_is_not_a_quiet_machine(self):
        self.assert_kirli(*self.rig.run(host=False), "no host log")

    def test_a_high_load_average_or_a_busy_cpu_alone_is_not_kirli_c00(self):
        # Karar C-00 06.10: the load average counts iCloud I/O waits on this machine; CPU idle is reported only
        # (pencere-p4: valid captures at idle p10 0.4 %). With no foreign process sampled the run writes the gate file.
        self.rig.load.write_text("2026-10-06T01:00:00Z { 31.50 30.0 29.0 }\n2026-10-06T01:00:05Z { 54.10 40.0 30.0 }\n",
                                 encoding="utf-8")
        rc, out = self.rig.run()
        self.assertEqual(0, rc, out)
        data = json.loads(self.rig.out.read_text(encoding="utf-8"))
        self.assertNotIn("note", data)
        host = data["takes"]["host"]
        self.assertEqual((2, 0, 54.1), (host["samples"], host["foreignSamples"], host["load1Max"]))
        self.assertAlmostEqual(4.7, host["cpuIdleP10"], delta=0.01)
        self.assertFalse(r2_s3.kirli_path(self.rig.out).exists())

    def test_c00_marks_a_run_dirty(self):
        self.assert_kirli(*self.rig.run("--kirli", "Blender rendered during the run"), "Blender rendered during the run")

    def test_the_kirli_file_sits_next_to_the_gate_file(self):
        self.assertEqual("r2-S3.KIRLI.json", r2_s3.kirli_path(Path("/e/r2/r2-S3.json")).name)


if __name__ == "__main__":
    unittest.main()
