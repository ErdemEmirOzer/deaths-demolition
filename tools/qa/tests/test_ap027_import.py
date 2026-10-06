"""WP-085 B5 (C-00 06.10): tools/audio/ap027_import.py — the AP-027 importer, run on a synthetic AP-027 package.

A small package is built in a temp folder (manifest/AP027-manifest-v002.json, exports/v002/<w>/*.wav as 24-bit PCM, the
v003-recut / v003b-recut / v003d-recut overlay folders with their MANIFEST.json and superseded_sha256 chain) and imported
into a temp worktree root. Checked: the files and manifest entries it writes, truePeakDb / codexLufs / lufs measured on
the shipped files (against an independent BS.1770 written here, and an analytic inter-sample peak), the overlay chain and
its SHA asserts, idempotence (second run: copied 0, manifest byte-identical), the fades log only when asked and nothing
written outside the worktree root."""
import contextlib
import hashlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np
import scipy.signal as ss

TOOLS = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(TOOLS / "audio"))
import ap027_import as imp  # noqa: E402

RATE = 48000
AUDIO = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Audio"
TP_PEAK_DB = -3.0   # the inter-sample file: fs/4 at 45° — samples at -6.02 dBFS, the waveform's peak at -3.0


# ---------- independent helpers (no code shared with the tool) ----------

def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_wav24(path, x):
    """x: (n, ch) floats in [-1, 1) → 24-bit little-endian PCM."""
    x = np.atleast_2d(np.asarray(x, dtype=np.float64).T).T
    q = np.clip(np.round(x * 8388608.0), -8388608, 8388607).astype("<i4")
    raw = q.reshape(-1).view(np.uint8).reshape(-1, 4)[:, :3].tobytes()
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(x.shape[1])
        w.setsampwidth(3)
        w.setframerate(RATE)
        w.writeframes(raw)


def read_wav24(path):
    with wave.open(str(path)) as w:
        assert w.getsampwidth() == 3 and w.getframerate() == RATE
        ch, raw = w.getnchannels(), w.readframes(w.getnframes())
    b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
    padded = np.zeros((len(b), 4), np.uint8)
    padded[:, 1:] = b   # the 24 bits in the top of an int32: the arithmetic shift keeps the sign
    return (padded.view("<i4").reshape(-1) >> 8).astype(np.float64).reshape(-1, ch) / 8388608.0


# BS.1770-4 K-weighting at 48 kHz (the recommendation's published coefficients) as second-order sections.
K_SOS = np.array([[1.53512485958697, -2.69169618940638, 1.19839281085285, 1.0, -1.69065929318241, 0.73248077421585],
                  [1.0, -2.0, 1.0, 1.0, -1.99004745483398, 0.99007225036621]])


def bs1770(x):
    """Integrated loudness, channel powers summed: 400 ms blocks on a 100 ms hop from a running sum, -70 LUFS absolute and
    -10 LU relative gates; a clip shorter than one block is zero-padded."""
    x = np.atleast_2d(x.T).T
    n, hop = int(0.4 * RATE), int(0.1 * RATE)
    if len(x) < n:
        x = np.vstack([x, np.zeros((n - len(x), x.shape[1]))])
    p = (ss.sosfilt(K_SOS, x, axis=0) ** 2).sum(axis=1)
    c = np.concatenate([[0.0], np.cumsum(p)])
    starts = np.arange(0, len(p) - n + 1, hop)
    blocks = (c[starts + n] - c[starts]) / n
    loud = -0.691 + 10 * np.log10(blocks + 1e-20)
    gated = blocks[loud > -70]
    if not len(gated):
        return -120.0
    rel = -0.691 + 10 * np.log10(gated.mean()) - 10
    return float(-0.691 + 10 * np.log10(blocks[(loud > -70) & (loud > rel)].mean()))


def onset(x, ms):
    k = int(ms / 1000 * RATE)
    x = x.copy()
    x[:k] *= (0.5 * (1 - np.cos(np.linspace(0, np.pi, k))))[:, None]
    return x


def burst(seconds=0.5, freq=1000.0, amp=0.5, tau=0.03, cut=None):
    """a decaying tone that reaches digital silence (cut: stops while sounding at that time)."""
    t = np.arange(int(seconds * RATE)) / RATE
    x = onset((amp * np.exp(-t / tau) * np.sin(2 * np.pi * freq * t))[:, None], 5)
    return x[:int(cut * RATE)] if cut else x


def intersample(seconds=0.5):
    """fs/4 at 45°: every sample at 0.707 of the peak; stops while sounding (the importer fades it)."""
    n = np.arange(int(seconds * RATE))
    return onset((10 ** (TP_PEAK_DB / 20) * np.sin(np.pi * n / 2 + np.pi / 4))[:, None], 20)


def stereo_tail(seconds=0.8, amp=0.1, seed=3):
    """decorrelated L/R noise below 8 kHz, decaying to silence (a hall tail)."""
    t = np.arange(int(seconds * RATE)) / RATE
    noise = ss.sosfiltfilt(ss.butter(4, 8000, fs=RATE, output="sos"), np.random.default_rng(seed).standard_normal((len(t), 2)), axis=0)
    return onset(amp * noise * np.exp(-t / 0.08)[:, None], 2)


# ---------- the synthetic AP-027 package ----------

def build_package(ap):
    """v002 + three overlay folders; returns {name: sha256 of what should ship from the source side}."""
    ap = Path(ap)
    v2 = ap / "exports" / "v002"
    sources = {
        "ar1/Weapon_ar1_Fire_01.wav": burst(),
        "ar1/Weapon_ar1_Fire_02.wav": burst(cut=0.05),          # cut while sounding; replaced by the overlay chain
        "ar1/Weapon_ar1_Fire_03.wav": intersample(),
        "ar1/Weapon_ar1_Mechanical_01.wav": burst(amp=0.2, freq=2000.0, tau=0.01, cut=0.03),
        "ar1/Weapon_ar1_TailHall_01.wav": stereo_tail(),
        "ar1/Weapon_ar1_Body_01.wav": burst(freq=150.0),          # production stem: not imported
        "ar1/Weapon_ar1_Cock_01.wav": burst(freq=3000.0),         # slice 2: not imported
        "ar1/Casing_ar1_Dirt_01.wav": burst(freq=4000.0, amp=0.3, tau=0.01),
        "ar1/Casing_ar1_Dirt_02.wav": burst(freq=4000.0, amp=0.3, tau=0.01) + 0.01,   # -40 dBFS DC: import high-pass
        "ar1/Casing_ar1_Gravel_01.wav": burst(freq=4000.0, tau=0.01),   # no SurfaceKind: not imported
        "lmg2/Weapon_lmg2_Fire_01.wav": burst(freq=600.0),        # POOL_EXCLUDE
        "lmg2/Weapon_lmg2_Fire_02.wav": burst(freq=600.0, amp=0.4),
        "sg1/Weapon_sg1_Fire_01.wav": burst(freq=300.0),          # not asked for in most runs
    }
    items = []
    for rel, x in sources.items():
        write_wav24(v2 / rel, x)
        w, name = rel.split("/")
        role = name.rsplit("_", 1)[0]
        items.append({"path": "exports/v002/" + rel, "sha256": sha256(v2 / rel), "type": "audio", "role": role,
                      "specs": {"sample_rate_hz": RATE, "bit_depth": 24, "channels": x.shape[1],
                                # the v002 estimates the importer must NOT copy (it measures the shipped file)
                                "true_peak_db_est": 5.0, "lufs_gated_est": -99.0},
                      "source_license_ids": ["SRC9" + str(len(items) % 2)],
                      "source_authenticity": {"caliber_surface_proxy": role.startswith("Casing")}})
    (ap / "manifest").mkdir(parents=True, exist_ok=True)
    (ap / "manifest" / "AP027-manifest-v002.json").write_text(json.dumps({"package": "AP-027", "items": items}, indent=1),
                                                              encoding="utf-8")

    def overlay(folder, entries):
        d = ap / "exports" / folder
        rows = []
        for rel, x, base_key, base in entries:
            if x is not None:
                write_wav24(d / rel, x)
            rows.append({"file": rel, base_key: base, "sha256": sha256(d / rel) if x is not None else base})
        (d / "MANIFEST.json").write_text(json.dumps({"package": "AP-027", "version": folder, "items": rows}, indent=1),
                                         encoding="utf-8")
        return {r["file"]: r["sha256"] for r in rows}

    fire2_v2 = sha256(v2 / "ar1/Weapon_ar1_Fire_02.wav")
    mech_v2 = sha256(v2 / "ar1/Weapon_ar1_Mechanical_01.wav")
    a = overlay("v003-recut", [("ar1/Weapon_ar1_Fire_02.wav", burst(amp=0.45), "source_v002_sha256", fire2_v2),
                               ("ar1/Weapon_ar1_Mechanical_01.wav", burst(amp=0.2, freq=2000.0, tau=0.01), "source_v002_sha256", mech_v2)])
    b = overlay("v003b-recut", [("ar1/Weapon_ar1_Fire_02.wav", burst(amp=0.4), "superseded_sha256", a["ar1/Weapon_ar1_Fire_02.wav"]),
                                # OVERLAY_EXCLUDE: v003b never touches ar1 Mechanical
                                ("ar1/Weapon_ar1_Mechanical_01.wav", burst(amp=0.1, freq=2000.0, tau=0.01), "superseded_sha256",
                                 a["ar1/Weapon_ar1_Mechanical_01.wav"])])
    # v003d: ar1 Mechanical restored as v003-recut's copy over a dropped v003c — same content already in place, skipped
    d = ap / "exports" / "v003d-recut"
    write_wav24(d / "ar1/Weapon_ar1_Mechanical_01.wav", burst(amp=0.2, freq=2000.0, tau=0.01))
    (d / "MANIFEST.json").write_text(json.dumps({"items": [{"file": "ar1/Weapon_ar1_Mechanical_01.wav", "superseded_sha256": "c" * 64,
                                                            "sha256": a["ar1/Weapon_ar1_Mechanical_01.wav"]}]}), encoding="utf-8")
    return {"fire2_v003b": b["ar1/Weapon_ar1_Fire_02.wav"], "mech_v003": a["ar1/Weapon_ar1_Mechanical_01.wav"]}


def entry(man, sound):
    found = [e for e in man["entries"] if e["sound"] == sound]
    assert len(found) == 1, (sound, [e["sound"] for e in man["entries"]])
    return found[0]


class Ap027ImportTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.ap = self.tmp / "AP-027"
        self.shas = build_package(self.ap)
        self.root = self.tmp / "wp" / "worktree" / "deaths-demolition"   # the script's root.parent.parent/evidence lived here
        self.root.mkdir(parents=True)
        self.audio = self.root / AUDIO
        self.out = self.audio / "AP027"

    def tearDown(self):
        self._tmp.cleanup()

    def run_import(self, weapons=("ar1",), fades_log=None):
        with contextlib.redirect_stdout(io.StringIO()):
            return imp.run(self.root, list(weapons), self.ap, fades_log)

    def manifest(self):
        return json.loads((self.audio / "AP027-audio-manifest.json").read_text(encoding="utf-8"))

    # (a) files and manifest entries
    def test_the_runtime_layers_are_imported_and_listed(self):
        summary = self.run_import(("ar1", "lmg2"))
        self.assertEqual(sorted(p.name for p in self.out.iterdir()), sorted([
            "Weapon_ar1_Fire_01.wav", "Weapon_ar1_Fire_02.wav", "Weapon_ar1_Fire_03.wav", "Weapon_ar1_Mechanical_01.wav",
            "Weapon_ar1_TailHall_01.wav", "Casing_ar1_Dirt_01.wav", "Casing_ar1_Dirt_02.wav", "Weapon_lmg2_Fire_02.wav"]))
        man = self.manifest()
        self.assertEqual(["CasingAr1Dirt", "WeaponAr1Fire", "WeaponAr1Mechanical", "WeaponAr1TailHall", "WeaponLmg2Fire"],
                         [e["sound"] for e in man["entries"]])
        self.assertEqual("ap027-v002-unity-ar1-lmg2", man["version"])
        self.assertEqual("K/asset-production/AP-027/manifest/AP027-manifest-v002.json ("
                         + sha256(self.ap / "manifest/AP027-manifest-v002.json")[:12] + ")", man["source_manifest"])
        fire = entry(man, "WeaponAr1Fire")
        self.assertEqual(["Weapon_ar1_Fire_01.wav", "Weapon_ar1_Fire_02.wav", "Weapon_ar1_Fire_03.wav"], fire["files"])
        self.assertEqual(("ar1", "ClassicWeaponFire", "SFX-Player", 24, -1.0, 20),
                         (fire["weapon"], fire["event"], fire["bus"], fire["bitDepth"], fire["ceilingDbtp"], fire["noiseFloorWindowMs"]))
        self.assertEqual(["Weapon_lmg2_Fire_02.wav"], entry(man, "WeaponLmg2Fire")["files"])   # Fire_01 out of the pool
        tail = entry(man, "WeaponAr1TailHall")
        self.assertEqual(("ClassicWeaponRelease", "Weapon_ar1_TailHall · stereo", True), (tail["event"], tail["layers"], tail["continuous"]))
        casing = entry(man, "CasingAr1Dirt")
        self.assertEqual(("WeaponMechanism.CasingEjected", "SFX-World"), (casing["event"], casing["bus"]))
        self.assertIn("proxy surface/caliber", casing["provenance"])
        self.assertEqual(["Casing_ar1_Dirt_02.wav"], casing["importDcRemoved"])
        self.assertIn("import 15 Hz high-pass + DC out on Casing_ar1_Dirt_02.wav", casing["provenance"])
        self.assertIn("bolt-return", entry(man, "WeaponAr1Mechanical")["noiseFloorException"])
        self.assertIn("Weapon_ar1_Fire_03.wav", fire["importFade"])   # stopped while sounding
        self.assertNotIn("Weapon_ar1_Fire_01.wav", fire["importFade"])
        # a file that needs no import treatment ships as Codex delivered it
        self.assertEqual(sha256(self.ap / "exports/v002/ar1/Weapon_ar1_Fire_01.wav"), sha256(self.out / "Weapon_ar1_Fire_01.wav"))
        self.assertEqual((5, 8, 8), (summary["entries"], summary["files"], summary["copied"]))

    # (b) measured on the shipped files
    def test_true_peak_and_loudness_are_measured_on_the_shipped_files(self):
        self.run_import()
        man = self.manifest()
        for e in man["entries"]:
            shipped = [read_wav24(self.out / f) for f in e["files"]]
            tps = [20 * np.log10(np.abs(ss.resample(x, 4 * len(x), axis=0)).max()) for x in shipped]   # FFT 4x, not the tool's FIR
            self.assertAlmostEqual(max(tps), e["truePeakDb"], delta=0.05, msg=e["sound"])
            self.assertAlmostEqual(np.mean([bs1770(x) for x in shipped]), e["codexLufs"], delta=0.011, msg=e["sound"])
            self.assertAlmostEqual(bs1770(shipped[0].mean(axis=1)), e["lufs"], delta=0.011, msg=e["sound"])
            self.assertEqual(e["lufs"], e["targetLufs"])
            self.assertNotEqual(5.0, e["truePeakDb"])   # never the v002 estimate
            self.assertNotEqual(-99.0, e["codexLufs"])
        fire = entry(man, "WeaponAr1Fire")
        # the inter-sample file wins: the true peak, ~3 dB over its sample peak (a sample-peak meter would read -6.0)
        self.assertAlmostEqual(TP_PEAK_DB, fire["truePeakDb"], delta=0.05)
        self.assertLess(20 * np.log10(np.abs(read_wav24(self.out / "Weapon_ar1_Fire_03.wav")).max()), TP_PEAK_DB - 2.9)
        # stereo: Codex's channel sum is above the house mono mean for a decorrelated tail
        tail = entry(man, "WeaponAr1TailHall")
        self.assertGreater(tail["codexLufs"] - tail["lufs"], 2.0)

    # (c) the overlay chain and its SHA asserts
    def test_the_overlay_chain_wins_and_is_named(self):
        self.run_import()
        man = self.manifest()
        self.assertEqual(self.shas["fire2_v003b"], sha256(self.out / "Weapon_ar1_Fire_02.wav"))   # v002 → v003 → v003b
        fire = entry(man, "WeaponAr1Fire")
        self.assertEqual(["Weapon_ar1_Fire_02.wav v003-recut", "Weapon_ar1_Fire_02.wav v003b-recut"], fire["overlay"])
        self.assertIn("AP-027 v003b-recut on 1 file(s)", fire["provenance"])
        self.assertNotIn("v003-recut on", fire["provenance"])   # counted once, under its last overlay
        # ar1 Mechanical: v003-recut wins, v003b excluded by name, v003d's identical copy skipped
        self.assertEqual(self.shas["mech_v003"], sha256(self.out / "Weapon_ar1_Mechanical_01.wav"))
        self.assertEqual(["Weapon_ar1_Mechanical_01.wav v003-recut"], entry(man, "WeaponAr1Mechanical")["overlay"])

    def _edit_overlay(self, folder, **fields):
        path = self.ap / "exports" / folder / "MANIFEST.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        doc["items"][0].update(fields)
        path.write_text(json.dumps(doc), encoding="utf-8")

    def test_an_overlay_on_another_base_stops_the_import(self):
        self._edit_overlay("v003b-recut", superseded_sha256="0" * 64)
        with self.assertRaisesRegex(AssertionError, "overlay base mismatch.*v003b-recut.*Weapon_ar1_Fire_02"):
            self.run_import()
        self.assertFalse((self.audio / "AP027-audio-manifest.json").exists())

    def test_an_overlay_file_that_is_not_its_manifest_sha_stops_the_import(self):
        write_wav24(self.ap / "exports/v003-recut/ar1/Weapon_ar1_Fire_02.wav", burst(amp=0.44))
        with self.assertRaisesRegex(AssertionError, "overlay SHA mismatch.*v003-recut.*Weapon_ar1_Fire_02"):
            self.run_import()
        self.assertFalse((self.audio / "AP027-audio-manifest.json").exists())

    def test_a_v002_file_that_is_not_its_manifest_sha_stops_the_import(self):
        write_wav24(self.ap / "exports/v002/ar1/Weapon_ar1_Fire_01.wav", burst(amp=0.49))
        with self.assertRaisesRegex(AssertionError, "SHA mismatch.*Weapon_ar1_Fire_01"):
            self.run_import()

    # (d) idempotence and the merge
    def test_a_second_run_copies_nothing_and_writes_the_same_bytes(self):
        first = self.run_import()
        before = {p.name: p.read_bytes() for p in self.audio.rglob("*") if p.is_file()}
        second = self.run_import()
        self.assertGreater(first["copied"], 0)
        self.assertEqual(0, second["copied"])
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.audio.rglob("*") if p.is_file()})
        self.assertFalse(list(self.out.glob("*.tmp.wav")))

    def test_other_weapons_entries_are_kept_and_the_weapons_own_replaced(self):
        self.run_import(("sg1",))
        sg1 = entry(self.manifest(), "WeaponSg1Fire")
        man = self.manifest()
        man["entries"].append(dict(sg1, sound="WeaponAr1Fire", weapon="ar1", files=["stale.wav"]))
        (self.audio / "AP027-audio-manifest.json").write_text(json.dumps(man), encoding="utf-8")
        self.run_import(("ar1",))
        man = self.manifest()
        self.assertEqual(sg1, entry(man, "WeaponSg1Fire"))
        self.assertNotIn("stale.wav", entry(man, "WeaponAr1Fire")["files"])
        self.assertEqual("ap027-v002-unity-ar1-sg1", man["version"])

    # the fades record and the write boundary
    def test_nothing_is_written_outside_the_root_without_a_fades_log(self):
        outside = sorted(p for p in self.tmp.rglob("*") if self.root not in p.parents and p != self.root)
        self.run_import()
        self.assertEqual(outside, sorted(p for p in self.tmp.rglob("*") if self.root not in p.parents and p != self.root))
        self.assertFalse((self.root.parent.parent / "evidence").exists())

    def test_the_fades_log_is_written_when_asked_and_merged(self):
        log = self.tmp / "evidence" / "fades.tsv"
        log.parent.mkdir()
        log.write_text("file\tcodex_sha12\timported_sha12\nOther_01.wav\taaaaaaaaaaaa\tbbbbbbbbbbbb\n"
                       "Weapon_ar1_Fire_03.wav\told\told\n", encoding="utf-8")
        self.run_import(fades_log=log)
        rows = [l.split("\t") for l in log.read_text(encoding="utf-8").splitlines()]
        self.assertEqual(["file", "codex_sha12", "imported_sha12"], rows[0])
        self.assertIn(["Other_01.wav", "aaaaaaaaaaaa", "bbbbbbbbbbbb"], rows)
        fire3 = [r for r in rows if r[0] == "Weapon_ar1_Fire_03.wav"]
        self.assertEqual([["Weapon_ar1_Fire_03.wav", sha256(self.ap / "exports/v002/ar1/Weapon_ar1_Fire_03.wav")[:12],
                           sha256(self.out / "Weapon_ar1_Fire_03.wav")[:12]]], fire3)
        faded = set(sum((e["importFade"] for e in self.manifest()["entries"]), []))
        self.assertEqual(faded, {r[0] for r in rows[1:]} - {"Other_01.wav"})

    # the command line
    def test_the_command_line(self):
        script = str(TOOLS / "audio" / "ap027_import.py")
        log = self.tmp / "cli-fades.tsv"
        done = subprocess.run([sys.executable, script, str(self.root), "ar1", "--ap027", str(self.ap), "sg1",
                               "--fades-log", str(log)], capture_output=True, text=True)
        self.assertEqual(0, done.returncode, done.stderr)
        head, _, rest = done.stdout.partition("\n}\n")
        summary = json.loads(head + "\n}")
        self.assertEqual((["ar1", "sg1"], 5, 8), (summary["weapons"], summary["entries"], summary["files"]))
        self.assertIn("WeaponAr1Fire 3 ", rest)
        self.assertTrue(log.exists())
        for argv in ([script], [script, str(self.root)], [script, str(self.root / "nope"), "ar1", "--ap027", str(self.ap)],
                     [script, str(self.root), "ar1", "--ap027", str(self.tmp / "no-package")]):
            self.assertEqual(64, subprocess.run([sys.executable] + argv, capture_output=True).returncode, argv)


if __name__ == "__main__":
    unittest.main()
