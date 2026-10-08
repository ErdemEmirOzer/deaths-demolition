"""WP-097 K2 (S-08 07.10): tools/audio/ap029_import.py — the AP-029 importer, run on a synthetic AP-029 export.

A small export is built in a temp folder (fire / space / special / foley, each with MANIFEST.json + SHA256SUMS, 24-bit PCM) and
imported into a temp worktree root that holds AP-011 / AP-027 Unity manifests and an AudioSoundId.cs. Checked: files copied to
Assets/_Art/Audio/AP029, the AP011-schema entries (sound names per part, event / bus / spatial / loop from the superseded AP-011
row for a special sound, stereo tail channels, durations, house LUFS of file 1 and true peak measured on the shipped file), the
foley naming (Casing_ / Link_ / CasingLink_ alias / MagDrop_), the part without a MANIFEST.json skipped, the merge by part, the
import fade only where a gate needs it (never on a loop; a tailExempt sound only on the absolute end gate), idempotence (copied 0,
manifest byte-identical), the SHA asserts (SHA256SUMS and perFile) and the CLI exit codes."""
import contextlib
import hashlib
import io
import json
import sys
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np

TOOLS = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(TOOLS / "audio"))
import ap029_import as imp  # noqa: E402

RATE = 48000
AUDIO = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Audio"
SOUND_IDS = ["WeaponAr1Fire", "WeaponTailArEcho", "SwArcFire", "SwArcSphereLoop", "SwArcCharge",
             "CasingAr1Metal", "LinkLmg1Grass", "MagDropRifleStoneSlate"]


def write_wav24(path, x):
    x = np.atleast_2d(np.asarray(x, dtype=np.float64).T).T
    q = np.clip(np.round(x * 8388608.0), -8388608, 8388607).astype("<i4")
    raw = q.reshape(-1).view(np.uint8).reshape(-1, 4)[:, :3].tobytes()
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(x.shape[1]); w.setsampwidth(3); w.setframerate(RATE); w.writeframes(raw)


def read_wav24(path):
    with wave.open(str(path)) as w:
        ch, raw = w.getnchannels(), w.readframes(w.getnframes())
    b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
    padded = np.zeros((len(b), 4), np.uint8); padded[:, 1:] = b
    return (padded.view("<i4").reshape(-1) >> 8).astype(np.float64).reshape(-1, ch) / 8388608.0


def sha256(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def burst(seconds, amp=0.3, seed=0, tail_silence=0.03, stereo=False, cut=False):
    """Decaying noise burst; tail_silence of digital zeros at the end (cut=True: stops while sounding)."""
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    env = np.exp(-np.arange(n) / (0.08 * RATE))
    if cut: env = np.full(n, 0.5)
    ch = 2 if stereo else 1
    x = amp * rng.uniform(-1, 1, (n, ch)) * env[:, None]
    if not cut: x = np.vstack([x, np.zeros((int(tail_silence * RATE), ch))])
    return x


def build_part(root, part, files, entries, extra=None):
    d = root / part
    sums = []
    for name, x in files.items():
        write_wav24(d / name, x)
        sums.append(sha256(d / name) + "  " + name)
    man = {"package": "AP-029", "version": "v001-" + part, "entries": entries}
    if extra: man.update(extra)
    (d / "MANIFEST.json").write_text(json.dumps(man, indent=1), encoding="utf-8")
    (d / "SHA256SUMS").write_text("\n".join(sums) + "\n", encoding="utf-8")
    return d


class Ap029ImportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        base = Path(self.tmp.name)
        self.exp = base / "exports" / "v001"
        self.wt = base / "wt"
        a = self.wt / AUDIO
        a.mkdir(parents=True)
        (a / "AP011-audio-manifest.json").write_text(json.dumps({"version": "t", "entries": [
            {"sound": "SwArcFire", "event": "SwArcFire", "files": ["SwArcFire_01.wav", "SwArcFire_02.wav"], "loop": False, "bus": "SFX-Player", "spatial": False},
            {"sound": "SwArcSphereLoop", "event": "SwArcSphereFlight", "files": ["SwArcSphereLoop.wav"], "loop": True, "bus": "SFX-World", "spatial": True},
            {"sound": "SwArcCharge", "event": "SwArcChargeStart", "files": ["SwArcCharge_01.wav"], "loop": False, "bus": "SFX-Player", "spatial": False},
        ]}), encoding="utf-8")
        (a / "AP027-audio-manifest.json").write_text(json.dumps({"version": "t", "entries": [
            {"sound": "WeaponAr1Fire", "weapon": "ar1", "files": ["Weapon_ar1_Fire_01.wav", "Weapon_ar1_Fire_02.wav"]}]}), encoding="utf-8")
        lib = self.wt / "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Library"
        lib.mkdir(parents=True)
        (lib / "AudioSoundId.cs").write_text("enum AudioSoundId {\n" + "".join("        %s = %d,\n" % (s, 1 + i) for i, s in enumerate(SOUND_IDS)) + "}\n", encoding="utf-8")

        fire = {"Weapon_ar1_Fire_01.wav": burst(0.4, 0.2, 1), "Weapon_ar1_Fire_02.wav": burst(0.4, 0.2, 2)}
        build_part(self.exp, "fire", fire, [{"sound": "WeaponAr1Fire", "weapon": "ar1", "event": "ClassicWeaponFire", "files": list(fire),
                                             "loop": False, "targetLufs": -18.0, "ceilingDbtp": -1.0, "bus": "SFX-Player", "spatial": True}])
        space = {"WeaponTailArEcho_01.wav": burst(2.2, 0.1, 3, stereo=True)}
        d = build_part(self.exp, "space", space, [{"sound": "WeaponTailArEcho", "kind": "Echo", "files": list(space), "ceilingDbtp": -3.0}])
        self.space_sha = sha256(d / "WeaponTailArEcho_01.wav")
        special = {"SwArcFire_01.wav": burst(0.5, 0.4, 4), "SwArcFire_02.wav": burst(0.5, 0.4, 5, cut=True),
                   "SwArcSphereLoop.wav": burst(1.0, 0.2, 6, cut=True), "SwArcCharge_01.wav": burst(0.5, 0.3, 7, cut=True)}
        build_part(self.exp, "special", special, [
            {"sound": "SwArcFire", "files": ["SwArcFire_01.wav", "SwArcFire_02.wav"], "loop": False, "ceilingDbtp": -1.0},
            {"sound": "SwArcSphereLoop", "files": ["SwArcSphereLoop.wav"], "loop": True, "ceilingDbtp": -1.0},
            {"sound": "SwArcCharge", "files": ["SwArcCharge_01.wav"], "loop": False, "ceilingDbtp": -1.0}],
            {"tailExempt": {"SwArcCharge": "ends abruptly by design"}})

    def tearDown(self):
        self.tmp.cleanup()

    def add_foley(self):
        foley = {"Casing_ar1_Metal_01.wav": burst(0.3, 0.2, 8), "CasingLink_lmg1_Grass_01.wav": burst(0.3, 0.05, 9),
                 "MagDrop_Rifle_StoneSlate_01.wav": burst(0.5, 0.3, 10)}
        build_part(self.exp, "foley", foley, [
            {"sound": "CasingAr1Metal", "files": ["Casing_ar1_Metal_01.wav"], "targetLufs": -24.0, "ceilingDbtp": -1.0, "bus": "SFX-World", "spatial": True},
            {"sound": "CasingLinkLmg1Grass", "files": ["CasingLink_lmg1_Grass_01.wav"], "targetLufs": -30.0, "ceilingDbtp": -1.0},
            {"sound": "MagDropRifleStoneSlate", "files": ["MagDrop_Rifle_StoneSlate_01.wav"], "targetLufs": -22.0, "ceilingDbtp": -1.0}])

    def run_tool(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = imp.main([str(self.wt), "--ap029", str(self.exp), *args])
        return code, out.getvalue()

    def manifest(self):
        return json.loads((self.wt / AUDIO / "AP029-audio-manifest.json").read_text(encoding="utf-8"))

    def test_import_without_foley_skips_it_and_writes_the_schema(self):
        code, out = self.run_tool()
        self.assertEqual(0, code, out)
        summary = json.loads(out[:out.rindex("}") + 1])
        self.assertEqual(["foley"], summary["skipped"])
        self.assertEqual([], summary["warnings"])
        e = {x["sound"]: x for x in self.manifest()["entries"]}
        self.assertEqual({"WeaponAr1Fire", "WeaponTailArEcho", "SwArcFire", "SwArcSphereLoop", "SwArcCharge"}, set(e))
        for k in ("sound", "event", "files", "loop", "lufs", "codexLufs", "truePeakDb", "bitDepth", "targetLufs", "ceilingDbtp",
                  "noiseFloorDb", "dcDb", "clipped", "endSilenceDb", "continuous", "layers", "bus", "spatial", "provenance", "part", "durationsS"):
            self.assertIn(k, e["WeaponAr1Fire"], k)
        self.assertTrue(e["WeaponAr1Fire"]["provenance"].startswith("AP-029 v001 fire"))
        self.assertEqual(-18.0, e["WeaponAr1Fire"]["targetLufs"])
        # special: event / bus / spatial / loop from the AP-011 row it supersedes
        self.assertEqual(("SwArcFire", "SFX-Player", False, False), (e["SwArcFire"]["event"], e["SwArcFire"]["bus"], e["SwArcFire"]["spatial"], e["SwArcFire"]["loop"]))
        self.assertTrue(e["SwArcSphereLoop"]["loop"] and e["SwArcSphereLoop"]["continuous"])
        self.assertEqual(2, e["WeaponTailArEcho"]["channels"])
        self.assertTrue(e["WeaponTailArEcho"]["continuous"])
        self.assertAlmostEqual(2.23, e["WeaponTailArEcho"]["durationsS"][0], places=3)   # 2.2 s body + 30 ms of zeros
        # untouched file: a byte copy; measured stamps match the shipped file
        dst = self.wt / AUDIO / "AP029" / "WeaponTailArEcho_01.wav"
        self.assertEqual(self.space_sha, sha256(dst))
        x = read_wav24(self.wt / AUDIO / "AP029" / "Weapon_ar1_Fire_01.wav")
        self.assertAlmostEqual(imp.a27.house_lufs_x(x, RATE), e["WeaponAr1Fire"]["lufs"], places=1)
        self.assertLessEqual(e["WeaponAr1Fire"]["truePeakDb"], 0.0)

    def test_fade_only_where_a_gate_needs_it(self):
        self.assertEqual(0, self.run_tool()[0])
        e = {x["sound"]: x for x in self.manifest()["entries"]}
        self.assertEqual(["SwArcFire_02.wav"], e["SwArcFire"]["importFade"], "the cut file is faded, the decayed one copied")
        self.assertEqual([], e["SwArcSphereLoop"]["importFade"], "a loop is never faded")
        self.assertEqual(["SwArcCharge_01.wav"], e["SwArcCharge"]["importFade"], "tailExempt: the absolute end gate (−70 dBFS) still applies")
        self.assertIn("import fade", e["SwArcFire"]["provenance"])
        y = read_wav24(self.wt / AUDIO / "AP029" / "SwArcFire_02.wav")
        self.assertEqual(int(0.5 * RATE) + int(0.02 * RATE), len(y), "body + 20 ms of silence")
        self.assertTrue(np.all(y[-int(0.02 * RATE):] == 0))
        self.assertLessEqual(e["SwArcFire"]["endSilenceDb"], -70.0)
        loop = read_wav24(self.wt / AUDIO / "AP029" / "SwArcSphereLoop.wav")
        self.assertEqual(int(1.0 * RATE), len(loop), "loop byte-length kept")

    def test_foley_names_alias_and_merge_by_part_and_idempotence(self):
        self.assertEqual(0, self.run_tool()[0])
        self.add_foley()
        code, out = self.run_tool("--parts", "foley")
        self.assertEqual(0, code, out)
        e = {x["sound"]: x for x in self.manifest()["entries"]}
        self.assertIn("WeaponAr1Fire", e, "the other parts' entries are kept")
        self.assertIn("LinkLmg1Grass", e, "CasingLink_ → Link<W><Surface>")
        self.assertNotIn("CasingLinkLmg1Grass", e)
        self.assertEqual("WeaponMechanism.MagazineDropped", e["MagDropRifleStoneSlate"]["event"])
        self.assertEqual("WeaponMechanism.CasingEjected", e["CasingAr1Metal"]["event"])
        self.assertEqual("SFX-World", e["LinkLmg1Grass"]["bus"])
        self.assertEqual("foley", e["CasingAr1Metal"]["part"])
        before = (self.wt / AUDIO / "AP029-audio-manifest.json").read_bytes()
        code, out = self.run_tool()
        self.assertEqual(0, code)
        self.assertEqual(0, json.loads(out[:out.rindex("}") + 1])["copied"], "second run copies nothing")
        self.assertEqual(before, (self.wt / AUDIO / "AP029-audio-manifest.json").read_bytes(), "manifest byte-identical")

    def test_magdrop_stamp_file_is_the_median_rr(self):
        foley = {"MagDrop_Rifle_StoneSlate_01.wav": burst(0.5, 0.03, 11), "MagDrop_Rifle_StoneSlate_02.wav": burst(0.5, 0.3, 12),
                 "MagDrop_Rifle_StoneSlate_03.wav": burst(0.5, 0.1, 13)}
        build_part(self.exp, "foley", foley, [{"sound": "MagDropRifleStoneSlate", "files": list(foley), "targetLufs": -22.0, "ceilingDbtp": -1.0}])
        new, summary = imp.run(self.wt, ["foley"], self.exp)
        e = new[0]
        self.assertEqual("MagDrop_Rifle_StoneSlate_03.wav", e["files"][0], "the median-loudness RR is file 1 (MagazineDropAudio reads its stamp)")
        self.assertEqual(sorted(foley), sorted(e["files"]))
        x = read_wav24(self.wt / AUDIO / "AP029" / "MagDrop_Rifle_StoneSlate_03.wav")
        self.assertAlmostEqual(imp.a27.house_lufs_x(x, RATE), e["lufs"], places=1)
        self.assertIn("median-loudness", e["provenance"])

    def test_foley_name_mismatch_asserts(self):
        self.add_foley()
        m = json.loads((self.exp / "foley" / "MANIFEST.json").read_text())
        m["entries"][0]["sound"] = "CasingAr1Mat"
        (self.exp / "foley" / "MANIFEST.json").write_text(json.dumps(m))
        with self.assertRaises(AssertionError):
            imp.run(self.wt, ["foley"], self.exp)

    def test_sha_mismatch_asserts_and_manifest_not_written(self):
        p = self.exp / "space" / "WeaponTailArEcho_01.wav"
        write_wav24(p, burst(2.2, 0.1, 99, stereo=True))
        with self.assertRaises(AssertionError):
            imp.run(self.wt, ["space"], self.exp)
        self.assertFalse((self.wt / AUDIO / "AP029-audio-manifest.json").exists())

    def test_perfile_sha_checked(self):
        m = json.loads((self.exp / "space" / "MANIFEST.json").read_text())
        m["entries"][0]["perFile"] = [{"file": "WeaponTailArEcho_01.wav", "sha256": "0" * 64}]
        (self.exp / "space" / "MANIFEST.json").write_text(json.dumps(m))
        with self.assertRaises(AssertionError):
            imp.run(self.wt, ["space"], self.exp)

    def test_cli_exit_codes(self):
        self.assertEqual(64, self.run_tool("--parts", "nope")[0])
        self.assertEqual(64, self.run_tool("--parts", "foley")[0], "only a part without MANIFEST.json → nothing to import")
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(64, imp.main([str(self.wt / "yok")]))

    def test_unknown_sound_id_is_reported(self):
        lib = self.wt / "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Library/AudioSoundId.cs"
        lib.write_text("enum AudioSoundId {\n        WeaponAr1Fire = 1,\n}\n", encoding="utf-8")
        new, summary = imp.run(self.wt, ["space"], self.exp)
        self.assertTrue(any("WeaponTailArEcho" in w and "AudioSoundId" in w for w in summary["warnings"]), summary["warnings"])


if __name__ == "__main__":
    unittest.main()
