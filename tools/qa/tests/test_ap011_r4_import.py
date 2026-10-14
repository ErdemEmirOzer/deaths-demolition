"""WP-097 R4 (C-00 12.10): tools/audio/ap011_r4_import.py — the AP-011 v024 R4 importer, run on a synthetic v024 package.

A small package is built in a temp folder (v024/final: MANIFEST.json with the 33 contract sounds — one file each, three for
WallBuyCoins, two for TreeElementNova / TreeMiniRocket —, SHA256SUMS, 48 kHz / 24-bit / mono WAVs levelled to the R4 targets
with an independent BS.1770 written here; three loops, an 8.05 s bell, a one-shot with no quiet 100 ms window) and imported
into a temp worktree that holds the three AP011 template .meta files (as on main), a small AP011-audio-manifest.json
(indent 1, ensure_ascii False, no trailing newline, a non-ASCII row), a small AudioSoundId.cs ending in WeaponSlap = 990 and
a stand-in DD_AudioCatalog.asset. Checked: the byte copies and the SHA gates (MANIFEST, SHA256SUMS, an existing destination
is never overwritten), the .meta GUID (sha256("dd-r4-ap011:" + name)[:32], the same in any tree, only the guid line differs
from the template, the template class by loop / length) and the GUID collision check (Assets .meta files, --known-guids),
the manifest rows (schema and key order, stamps against an independent BS.1770 and an FFT 4x true peak, bus / spatial /
event, continuous, provenance), the manifest round-trip and format refusal, the enum block (insert-only, WeaponSlap = 990
stays last, conflicts stop the run), idempotence (second run: nothing written), --dry-run, the write boundary (catalog
untouched) and the command line."""
import contextlib
import difflib
import hashlib
import io
import json
import re
import shutil
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
import ap011_r4_import as imp  # noqa: E402

RATE = 48000
PROJECT = "prototype/DeathsDemolition_PlayableConcept"
AP011 = PROJECT + "/Assets/_Art/Audio/AP011"
MANIFEST = PROJECT + "/Assets/_Art/Audio/AP011-audio-manifest.json"
ENUM = PROJECT + "/Assets/_Prototype/Audio/Library/AudioSoundId.cs"
CATALOG = PROJECT + "/Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset"

# The contract, typed here from K/wp-097/r4-bind/IDS.md (independent of the tool's table): name, value, v024 id, UI?, loop.
CONTRACT = [
    ("WallBuyCoins", 1000, "wb_coins", False, False), ("WallBuyRack", 1001, "wb_rack", False, False),
    ("WallBuyDenied", 1002, "wb_denied", False, False), ("WallBuyHeavy", 1003, "wb_heavy", False, False),
    ("WallBuyPrompt", 1004, "wb_prompt", True, False), ("ModeSelectHard", 1010, "mode_hard", True, False),
    ("ModeSelectDemolition", 1011, "mode_demolition", True, False), ("TreeOpen", 1020, "tree_open", True, False),
    ("TreeStamp", 1021, "tree_stamp", True, False), ("TreeSynergy", 1022, "tree_synergy", True, False),
    ("TreeReroll", 1023, "tree_reroll", True, False), ("TreeFireIgnite", 1030, "fx_fire_ignite", False, False),
    ("TreeFireLoop", 1031, "fx_fire_loop", False, True), ("TreeIceFreeze", 1032, "fx_ice_freeze", False, False),
    ("TreeIceShatter", 1033, "fx_ice_shatter", False, False), ("TreePoisonLoop", 1034, "fx_poison", False, True),
    ("TreeThunder", 1035, "fx_thunder", False, False), ("TreeElementNova", 1036, "fx_cataclysm", False, False),
    ("TreeCrit", 1040, "fx_crit", False, False), ("TreeMiniNuke", 1041, "fx_mininuke", False, False),
    ("TreeBlackHoleLoop", 1042, "fx_blackhole_loop", False, True), ("TreeBlackHoleCollapse", 1043, "fx_blackhole_collapse", False, False),
    ("TreeDoomsdayBell", 1044, "fx_doomsday_bell", False, False), ("TreeShieldUp", 1050, "fx_shield_up", False, False),
    ("TreeShieldHit", 1051, "fx_shield_hit", False, False), ("TreeShieldBreak", 1052, "fx_shield_break", False, False),
    ("TreeLifesteal", 1053, "fx_lifesteal", False, False), ("TreeDodge", 1054, "fx_dodge", False, False),
    ("TreeUndying", 1055, "fx_undying", False, False), ("TreeRicochet", 1060, "fx_ricochet", False, False),
    ("TreeEndlessOn", 1061, "fx_endless_on", False, False), ("TreeMiniRocket", 1062, "fx_minirocket", False, False),
    ("TreeAuraUp", 1063, "fx_aura_up", False, False),
]
ROW_KEYS = ["sound", "event", "files", "loop", "lufs", "truePeakDb", "noiseFloorDb", "dcDb", "clipped", "bitDepth", "targetLufs",
            "ceilingDbtp", "continuous", "endSilenceDb", "layers", "bus", "spatial", "provenance"]

# The three template .meta files exactly as on main (only these AP011 files are needed by the tool).
META_ONESHOT = """fileFormatVersion: 2
guid: 7f61fa5799a1d4ead8221b3da8432e68
AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 0
    compressionFormat: 1
    quality: 0.8
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: 1
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""
META_LOOP = META_ONESHOT.replace("7f61fa5799a1d4ead8221b3da8432e68", "8c9f057119ede415e84597dcbddc93e4").replace("quality: 0.8", "quality: 0.7")
META_LONG = (META_ONESHOT.replace("7f61fa5799a1d4ead8221b3da8432e68", "04566616ba9194ee09b226870b44e97e").replace("quality: 0.8", "quality: 0.7")
             .replace("loadType: 0", "loadType: 2").replace("preloadAudioData: 1", "preloadAudioData: 0")
             .replace("loadInBackground: 0", "loadInBackground: 1"))

ENUM_TEXT = """namespace DD.Prototype.Audio
{
    /// <summary>
    /// A trimmed AudioSoundId: the tool only needs the enum's shape and its last member.
    /// </summary>
    public enum AudioSoundId
    {
        None = 0,

        // ---- META: not owned by the combat event contract ---------------------
        UiConfirm = 117,
        AmbBambooDrip = 571,

        WeaponTailRlRoom = 952,

        /// <summary>WP-097 K1: label only — a slapback copy of the dry Fire clip (the clip is passed explicitly, no catalog row);
        /// keeps the Reflect pool's instance count and the telemetry apart from Fire.</summary>
        WeaponSlap = 990
    }
}
"""


# ---------- independent helpers (no code shared with the tool) ----------

def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_wav24(path, x):
    q = np.clip(np.round(np.asarray(x, dtype=np.float64) * 8388608.0), -8388608, 8388607).astype("<i4")
    raw = q.view(np.uint8).reshape(-1, 4)[:, :3].tobytes()
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(3)
        w.setframerate(RATE)
        w.writeframes(raw)


def read_wav24(path):
    with wave.open(str(path)) as w:
        assert w.getsampwidth() == 3 and w.getframerate() == RATE and w.getnchannels() == 1
        raw = w.readframes(w.getnframes())
    b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
    padded = np.zeros((len(b), 4), np.uint8)
    padded[:, 1:] = b
    return (padded.view("<i4").reshape(-1) >> 8).astype(np.float64) / 8388608.0


K_SOS = np.array([[1.53512485958697, -2.69169618940638, 1.19839281085285, 1.0, -1.69065929318241, 0.73248077421585],
                  [1.0, -2.0, 1.0, 1.0, -1.99004745483398, 0.99007225036621]])


def bs1770(x):
    """Integrated loudness (mono): 400 ms blocks on a 100 ms hop from a running sum, -70 / -10 gates, short clip zero-padded."""
    n, hop = int(0.4 * RATE), int(0.1 * RATE)
    if len(x) < n:
        x = np.concatenate([x, np.zeros(n - len(x))])
    p = ss.sosfilt(K_SOS, x) ** 2
    c = np.concatenate([[0.0], np.cumsum(p)])
    starts = np.arange(0, len(p) - n + 1, hop)
    blocks = (c[starts + n] - c[starts]) / n
    loud = -0.691 + 10 * np.log10(blocks + 1e-20)
    gated = blocks[loud > -70]
    if not len(gated):
        return -120.0
    rel = -0.691 + 10 * np.log10(gated.mean()) - 10
    return float(-0.691 + 10 * np.log10(blocks[(loud > -70) & (loud > rel)].mean()))


def true_peak(x):
    return float(20 * np.log10(np.abs(ss.resample(x, 4 * len(x))).max()))   # FFT 4x, not the tool's polyphase FIR


def levelled(x, target):
    y = x * 10 ** ((target - bs1770(x)) / 20)
    assert true_peak(y) < -1.6, "fixture over the ceiling"
    return y


def ramp(n):
    return 0.5 * (1 - np.cos(np.linspace(0, np.pi, n)))


def decaying(freq, seconds=0.3, tau=0.06, silence=0.15):
    """a decaying tone, then digital silence (a 100 ms quiet window: the floor is measurable)."""
    t = np.arange(int(seconds * RATE)) / RATE
    x = np.exp(-t / tau) * np.sin(2 * np.pi * freq * t)
    x[:240] *= ramp(240)
    x[-480:] *= ramp(480)[::-1]
    return np.concatenate([x, np.zeros(int(silence * RATE))])


def steady(freq, seconds=0.21):
    """a steady tone faded over 10 ms + 10 ms of silence: no 100 ms window under -60 dBFS (continuous)."""
    t = np.arange(int(seconds * RATE)) / RATE
    x = np.sin(2 * np.pi * freq * t)
    x[:240] *= ramp(240)
    x[-480:] *= ramp(480)[::-1]
    return np.concatenate([x, np.zeros(480)])


def noise_loop(seed, seconds=0.6):
    x = ss.sosfiltfilt(ss.butter(4, [200, 6000], "bandpass", fs=RATE, output="sos"),
                       np.random.default_rng(seed).standard_normal(int(seconds * RATE)))
    x[:240] *= ramp(240)   # tapered ends: the FFT peak estimate here does not ring on a wrap-around step
    x[-240:] *= ramp(240)[::-1]
    return x


FILES = {"WallBuyCoins": 3, "TreeElementNova": 2, "TreeMiniRocket": 2}
ORIGIN = {("TreeElementNova", 1): {"kind": "synthesis", "tool": "ap011_r4_synth.py", "seed": 11, "take": "fx_cataclysm_01.wav"},
          ("TreeMiniRocket", 1): {"kind": "derived", "tool": "ap011_r4_derive.py", "seed": 12, "take": "fx_minirocket_01.wav",
                                  "source": "/x/sonniss/2024/Pole Position - The Warfare 2 Library/RPG.wav"},
          ("TreeMiniRocket", 2): {"kind": "derived", "tool": "ap011_r4_derive.py", "seed": 13, "take": "fx_minirocket_02.wav",
                                  "source": "/x/sonniss/2024/Pole Position - The Warfare 2 Library/RPG.wav"}}


def signal(i, sound, loop, k):
    if loop:
        return noise_loop(i * 10 + k)
    if sound == "TreeDoomsdayBell":
        return decaying(440.0, seconds=8.0, tau=1.2, silence=0.05)   # 8.05 s: PinImporter streams it
    if sound == "TreeLifesteal":
        return steady(700.0 + 10 * k)
    return decaying(500.0 + 37 * i + 11 * k)


def build_package(final):
    final.mkdir(parents=True)
    entries, sums = [], []
    for i, (sound, _value, sid, ui, loop) in enumerate(CONTRACT):
        target = -22.0 if ui else -20.0
        files = []
        for k in range(1, FILES.get(sound, 1) + 1):
            name = "%s_%02d.wav" % (sound, k)
            write_wav24(final / name, levelled(signal(i, sound, loop, k), target))
            back = read_wav24(final / name)
            files.append({"file": name, "sha256": sha256(final / name), "lufs": round(bs1770(back), 2), "tp_dbtp": round(true_peak(back), 2),
                          "seconds": round(len(back) / RATE, 3), "limiter_gr_db": 2.6 if (sound, k) == ("WallBuyCoins", 2) else 0.0,
                          "edits": [["cut", 0.84, 60]] if (sound, k) == ("WallBuyCoins", 1) else [],
                          # WP-113: an external single take's kind is the vendor's name in the delivery MANIFEST; any kind other than
                          # synthesis / derived is one, and the Unity manifest names it only as external (private ledger r4)
                          "origin": ORIGIN.get((sound, k), {"kind": "external-vendor", "raw": "/x/R4/%s_%d.wav" % (sid, k)}),
                          "mastered_from": "/x/v024/external/%s_%d.wav" % (sid, k)})
            sums.append("%s  %s" % (files[-1]["sha256"], name))
        entries.append({"id": sid, "sound": sound, "bus": "Interface" if ui else "SFX", "target_lufs": target, "loop": loop,
                        "files": files, "gate_note": ""})
    man = {"package": "AP-011", "version": "v024-r4", "entries": entries}
    (final / "MANIFEST.json").write_text(json.dumps(man, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    sums.append("%s  MANIFEST.json" % sha256(final / "MANIFEST.json"))
    (final / "SHA256SUMS").write_text("\n".join(sums) + "\n", encoding="utf-8")


def build_worktree(root):
    ap011 = root / AP011
    ap011.mkdir(parents=True)
    for name, text in (("ChainArc_01.wav.meta", META_ONESHOT), ("SwArcSphereLoop.wav.meta", META_LOOP),
                       ("AmbBambooGroveLoop.wav.meta", META_LONG)):
        (ap011 / name).write_text(text, encoding="utf-8")
    other = root / PROJECT / "Assets" / "Other"
    other.mkdir(parents=True)
    (other / "Thing.asset.meta").write_text("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n", encoding="utf-8")
    man = {"version": "ap011-v023", "package": "AP-011",
           "standard": {"sfxLufs": -18.0, "musicLufs": -16.0, "ambienceLufs": -20.0, "truePeakMax": -1.5, "toleranceLu": 1.5,
                        "noiseFloorMaxDb": -60.0, "bitDepth": 24},
           "entries": [{"sound": "AmbBambooDrip", "event": "scatter.bamboo_drip", "files": ["AmbBambooDrip_01.wav"], "loop": False,
                        "lufs": -24.0, "truePeakDb": -3.0, "noiseFloorDb": -120.0, "dcDb": -129.1, "clipped": 0, "bitDepth": 24,
                        "targetLufs": -24.0, "ceilingDbtp": -3.0, "continuous": False, "endSilenceDb": -240.0, "layers": "derived (v005)",
                        "bus": "Ambience", "spatial": True, "provenance": "derived offline (AP-011 v022, 0 cr) · leaf shake"},
                       {"sound": "UiConfirm", "event": "UiEvent.Confirm", "files": ["UiConfirm_01.wav"], "loop": False, "lufs": -24.23,
                        "truePeakDb": -4.0, "noiseFloorDb": -95.9, "dcDb": -125.2, "clipped": 0, "bitDepth": 24, "targetLufs": -22.0,
                        "ceilingDbtp": -4.0, "continuous": False, "endSilenceDb": -160.5, "layers": "single take", "bus": "UI",
                        "spatial": False, "provenance": "external:job-0 · hyōshigi"}]}
    (root / MANIFEST).write_text(json.dumps(man, indent=1, ensure_ascii=False), encoding="utf-8")
    (root / ENUM).parent.mkdir(parents=True)
    (root / ENUM).write_text(ENUM_TEXT, encoding="utf-8")
    (root / CATALOG).parent.mkdir(parents=True)
    (root / CATALOG).write_text("%YAML 1.1\n  _sourceHash: a38af227\n", encoding="utf-8")


def snapshot(root):
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(Path(root).rglob("*")) if p.is_file()}



# S-08 12.10: the player-centred tree sounds ride SFX-Player, 2D; other SFX are positional SFX-World; UI stays UI.
PLAYER = {"TreeShieldUp", "TreeShieldHit", "TreeShieldBreak", "TreeLifesteal", "TreeDodge", "TreeUndying", "TreeEndlessOn", "TreeAuraUp"}


def bus_of(sound, ui):
    return "UI" if ui else ("SFX-Player" if sound in PLAYER else "SFX-World")

class Ap011R4ImportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._pkg = tempfile.TemporaryDirectory()
        cls.pristine = Path(cls._pkg.name) / "final"
        build_package(cls.pristine)

    @classmethod
    def tearDownClass(cls):
        cls._pkg.cleanup()

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.src = self.tmp / "AP-011" / "v024" / "final"
        shutil.copytree(self.pristine, self.src)
        self.root = self.tmp / "wt"
        build_worktree(self.root)
        self.n_files = sum(FILES.get(c[0], 1) for c in CONTRACT)

    def tearDown(self):
        self._tmp.cleanup()

    def run_import(self, root=None, dry_run=False, known=None):
        with contextlib.redirect_stdout(io.StringIO()):
            return imp.run(root or self.root, self.src, dry_run, known)

    def assert_refused(self, pattern):
        before = snapshot(self.root)
        with self.assertRaises(imp.CheckFailed) as cm:
            self.run_import()
        self.assertTrue(any(re.search(pattern, f) for f in cm.exception.args[0]), cm.exception.args[0])
        self.assertEqual(before, snapshot(self.root), "a refused run writes nothing")
        return cm.exception.args[0]

    def manifest(self):
        return json.loads((self.root / MANIFEST).read_text(encoding="utf-8"))

    def edit_source(self, fn):
        """edit the source MANIFEST.json and keep its SHA256SUMS line true."""
        p = self.src / "MANIFEST.json"
        man = json.loads(p.read_text(encoding="utf-8"))
        fn(man)
        p.write_text(json.dumps(man, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        sums = self.src / "SHA256SUMS"
        sums.write_text(re.sub(r"^[0-9a-f]{64}  MANIFEST\.json$", sha256(p) + "  MANIFEST.json", sums.read_text(encoding="utf-8"), flags=re.M),
                        encoding="utf-8")

    def replace_source_wav(self, name, x):
        """swap one source WAV and keep its MANIFEST / SHA256SUMS lines true."""
        write_wav24(self.src / name, x)
        h = sha256(self.src / name)
        sums = self.src / "SHA256SUMS"
        sums.write_text(re.sub(r"^[0-9a-f]{64}  " + re.escape(name) + "$", h + "  " + name, sums.read_text(encoding="utf-8"), flags=re.M),
                        encoding="utf-8")

        def fix(man):
            for e in man["entries"]:
                for f in e["files"]:
                    if f["file"] == name:
                        f["sha256"] = h
                        f.pop("lufs", None)
                        f.pop("tp_dbtp", None)
        self.edit_source(fix)

    # ---- the contract table
    def test_the_tool_carries_the_ids_contract(self):
        self.assertEqual([(c[0], c[1], c[2], bus_of(c[0], c[3]), c[4]) for c in CONTRACT],
                         [(r[0], r[1], r[2], r[3], r[4]) for r in imp.R4])
        self.assertEqual(33, len({r[1] for r in imp.R4}))
        self.assertEqual("WallBuyEvents.Resolved Bought/Refilled (wp-107-wallbuy)", imp.R4[0][5])
        self.assertEqual("TreeEvents.AuraChanged (seviye artışı)", imp.R4[-1][5])

    # ---- copies and SHA gates
    def test_the_wavs_are_copied_byte_identical_with_their_meta(self):
        s = self.run_import()
        out = self.root / AP011
        for c in CONTRACT:
            for k in range(1, FILES.get(c[0], 1) + 1):
                name = "%s_%02d.wav" % (c[0], k)
                self.assertEqual(sha256(self.src / name), sha256(out / name), name)
                self.assertTrue((out / (name + ".meta")).is_file(), name)
        self.assertEqual({"new": self.n_files, "same": 0}, s["wav"])
        self.assertEqual(self.n_files, s["meta"]["new"])
        self.assertEqual({"oneshot": self.n_files - 4, "loop": 3, "long": 1}, s["meta"]["templates"])
        self.assertIn(MANIFEST, s["changed"])
        self.assertIn(ENUM, s["changed"])
        self.assertFalse(list(out.glob(".*")), "no temp file left")

    def test_a_source_file_off_its_manifest_sha_stops_the_run(self):
        write_wav24(self.src / "TreeCrit_01.wav", decaying(900.0) * 0.1)
        self.assert_refused(r"TreeCrit_01\.wav: SHA-256 .* differs from the MANIFEST")

    def test_a_sha256sums_line_off_the_file_stops_the_run(self):
        sums = self.src / "SHA256SUMS"
        sums.write_text(re.sub(r"^[0-9a-f]{64}(  TreeDodge_01\.wav)$", "0" * 64 + r"\1", sums.read_text(encoding="utf-8"), flags=re.M),
                        encoding="utf-8")
        self.assert_refused(r"TreeDodge_01\.wav: SHA-256 differs from SHA256SUMS")

    def test_an_existing_wav_with_other_bytes_is_not_overwritten(self):
        dst = self.root / AP011 / "TreeStamp_01.wav"
        write_wav24(dst, decaying(1234.0) * 0.05)
        self.assert_refused(r"TreeStamp_01\.wav: exists in the worktree with another SHA-256")

    def test_an_existing_meta_with_other_content_is_not_overwritten(self):
        (self.root / AP011 / "TreeStamp_01.wav.meta").write_text(META_ONESHOT.replace("7f61fa", "aaaaaa"), encoding="utf-8")
        self.assert_refused(r"TreeStamp_01\.wav\.meta: exists with other content")

    # ---- .meta and GUID
    def test_the_meta_guid_is_deterministic_and_only_the_guid_line_differs(self):
        self.run_import()
        other = self.tmp / "wt2"
        build_worktree(other)
        self.run_import(other)
        templates = {"oneshot": META_ONESHOT, "loop": META_LOOP, "long": META_LONG}
        guids = set()
        for c in CONTRACT:
            for k in range(1, FILES.get(c[0], 1) + 1):
                name = "%s_%02d.wav" % (c[0], k)
                text = (self.root / AP011 / (name + ".meta")).read_text(encoding="utf-8")
                self.assertEqual(text, (other / AP011 / (name + ".meta")).read_text(encoding="utf-8"), "same in any tree: " + name)
                guid = hashlib.sha256(("dd-r4-ap011:" + name).encode("utf-8")).hexdigest()[:32]
                self.assertIn("\nguid: %s\n" % guid, text)
                guids.add(guid)
                kind = "long" if c[0] == "TreeDoomsdayBell" else "loop" if c[4] else "oneshot"
                want = templates[kind].splitlines()
                got = text.splitlines()
                self.assertEqual(len(want), len(got))
                self.assertEqual([i for i, (a, b) in enumerate(zip(want, got)) if a != b], [1], name + " differs only in its guid line")
        self.assertEqual(self.n_files, len(guids))

    def test_a_guid_taken_by_another_meta_stops_the_run(self):
        taken = hashlib.sha256(b"dd-r4-ap011:TreeCrit_01.wav").hexdigest()[:32]
        (self.root / PROJECT / "Assets" / "Other" / "Clash.prefab.meta").write_text("fileFormatVersion: 2\nguid: %s\n" % taken, encoding="utf-8")
        self.assert_refused(r"TreeCrit_01\.wav: GUID %s already used by .*Clash\.prefab\.meta" % taken)

    def test_known_guids_count_as_taken(self):
        taken = hashlib.sha256(b"dd-r4-ap011:TreeAuraUp_01.wav").hexdigest()[:32]
        before = snapshot(self.root)
        with self.assertRaises(imp.CheckFailed) as cm:
            self.run_import(known={taken, "f" * 32})
        self.assertTrue(any("TreeAuraUp_01.wav: GUID" in f and "known-guids" in f for f in cm.exception.args[0]))
        self.assertEqual(before, snapshot(self.root))
        # once the import is in the tree, its own GUIDs in --known-guids (a main that carries it) are not collisions
        self.run_import()
        all_ours = {hashlib.sha256(("dd-r4-ap011:" + p.name[:-5]).encode()).hexdigest()[:32] for p in (self.root / AP011).glob("Tree*.meta")}
        self.assertEqual(0, self.run_import(known=all_ours)["meta"]["new"])

    # ---- manifest rows
    def test_the_manifest_rows_carry_the_house_stamps(self):
        self.run_import()
        text = (self.root / MANIFEST).read_text(encoding="utf-8")
        man = json.loads(text)
        self.assertEqual(json.dumps(man, indent=1, ensure_ascii=False), text, "round-trip, no trailing newline (as main)")
        self.assertIn("hyōshigi", text, "ensure_ascii False kept")
        self.assertEqual("ap011-v024", man["version"])
        names = [e["sound"] for e in man["entries"]]
        self.assertEqual(sorted(names), names)
        self.assertEqual(35, len(names))
        old = {e["sound"]: e for e in man["entries"] if e["sound"] in ("AmbBambooDrip", "UiConfirm")}
        self.assertEqual(-24.23, old["UiConfirm"]["lufs"])
        self.assertEqual("external:job-0 · hyōshigi", old["UiConfirm"]["provenance"])
        rows = {e["sound"]: e for e in man["entries"]}
        src = {e["sound"]: e for e in json.loads((self.src / "MANIFEST.json").read_text(encoding="utf-8"))["entries"]}
        for sound, _value, sid, ui, loop in CONTRACT:
            e = rows[sound]
            self.assertEqual(ROW_KEYS, list(e), sound)
            self.assertEqual([f["file"] for f in src[sound]["files"]], e["files"])
            shipped = [read_wav24(self.root / AP011 / f) for f in e["files"]]
            self.assertAlmostEqual(bs1770(shipped[0]), e["lufs"], delta=0.011, msg=sound)
            self.assertAlmostEqual(max(true_peak(x) for x in shipped), e["truePeakDb"], delta=0.05, msg=sound)
            bus = bus_of(sound, ui)
            self.assertEqual((bus, bus == "SFX-World", -22.0 if ui else -20.0), (e["bus"], e["spatial"], e["targetLufs"]), sound)
            self.assertEqual((loop, 24, src[sound].get("ceiling_dbtp", -1.5), 0), (e["loop"], e["bitDepth"], e["ceilingDbtp"], e["clipped"]), sound)
            self.assertEqual(next(r[5] for r in imp.R4 if r[0] == sound), e["event"])
            self.assertIn("AP-011 v024", e["provenance"])
            self.assertIn("v024 id " + sid, e["provenance"])
            self.assertLessEqual(e["dcDb"], -60.0)
            if not loop:
                self.assertLessEqual(e["endSilenceDb"], -70.0, sound)
        # continuous: the loops, and the one-shot that never rests under -60 dBFS in a 100 ms window
        self.assertEqual({"TreeFireLoop", "TreePoisonLoop", "TreeBlackHoleLoop", "TreeLifesteal"}, {s for s, e in rows.items() if e["continuous"]})
        self.assertIn("continuous exemption", rows["TreeLifesteal"]["provenance"])
        self.assertNotIn("continuous exemption", rows["TreeFireLoop"]["provenance"])
        self.assertLess(rows["WallBuyCoins"]["noiseFloorDb"], -60.0)
        self.assertGreater(rows["TreeLifesteal"]["noiseFloorDb"], -60.0)
        # provenance / layers from the origin, edits and limiter gain
        self.assertEqual("single take", rows["WallBuyCoins"]["layers"])
        self.assertIn("external takes ×3 (_01 _02 _03; private-ledger:r4)", rows["WallBuyCoins"]["provenance"])   # WP-113 neutral schema
        self.assertIn("edits: _01 cut at 0.84 s + 60 ms fade", rows["WallBuyCoins"]["provenance"])
        self.assertIn("TP-limited (look-ahead ≤ 6 dB): _02 2.6 dB", rows["WallBuyCoins"]["provenance"])
        self.assertEqual("_01 synth · _02 single take (external)", rows["TreeElementNova"]["layers"])   # WP-113 neutral schema
        self.assertIn("_01 synthesis (ap011_r4_synth.py seed 11)", rows["TreeElementNova"]["provenance"])
        self.assertEqual("derived: ap011_r4_derive.py (Sonniss source, seeded)", rows["TreeMiniRocket"]["layers"])
        self.assertIn("from Sonniss GameAudioGDC 'Pole Position - The Warfare 2 Library'", rows["TreeMiniRocket"]["provenance"])

    def test_a_manifest_in_another_format_is_refused(self):
        p = self.root / MANIFEST
        p.write_text(json.dumps(json.loads(p.read_text(encoding="utf-8")), indent=2, ensure_ascii=False), encoding="utf-8")
        self.assert_refused(r"does not round-trip")

    def test_a_trailing_newline_and_a_later_version_are_kept(self):
        p = self.root / MANIFEST
        man = json.loads(p.read_text(encoding="utf-8"))
        man["version"] = "ap011-v031"
        p.write_text(json.dumps(man, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
        self.run_import()
        text = p.read_text(encoding="utf-8")
        self.assertTrue(text.endswith("}\n") and not text.endswith("\n\n"))
        self.assertEqual("ap011-v031", json.loads(text)["version"])

    def test_a_stamp_off_the_source_manifest_stops_the_run(self):
        def off(man):
            man["entries"][0]["files"][0]["lufs"] += 1.0
        self.edit_source(off)
        self.assert_refused(r"WallBuyCoins_01\.wav: measured lufs")

    def test_a_row_failing_the_house_end_gate_stops_the_run(self):
        t = np.arange(int(0.3 * RATE)) / RATE
        cut = levelled(np.sin(2 * np.pi * 800 * t), -20.0)   # stops while sounding: the last 10 ms are loud
        self.replace_source_wav("TreeDodge_01.wav", cut)
        self.assert_refused(r"TreeDodge: end .* > -70")

    def test_a_contract_mismatch_in_the_source_stops_the_run(self):
        def loop_on(man):
            man["entries"][0]["loop"] = True
            man["entries"][4]["bus"] = "SFX"
        self.edit_source(loop_on)
        fails = self.assert_refused(r"WallBuyCoins: loop True, the contract says False")
        self.assertTrue(any("WallBuyPrompt: source bus SFX" in f for f in fails), fails)

    # ---- the enum
    def test_the_enum_block_is_insert_only_and_weaponslap_stays_last(self):
        self.run_import()
        new = (self.root / ENUM).read_text(encoding="utf-8")
        old_lines, new_lines = ENUM_TEXT.split("\n"), new.split("\n")
        ops = difflib.SequenceMatcher(a=old_lines, b=new_lines, autojunk=False).get_opcodes()
        self.assertEqual({"equal", "insert"}, {op[0] for op in ops}, "insert-only")
        members = re.findall(r"^\s*([A-Z][A-Za-z0-9]*)\s*=\s*(\d+)\s*(,?)\s*$", new, re.M)
        self.assertEqual(("WeaponSlap", "990", ""), members[-1], "WeaponSlap = 990 stays the last member")
        got = {n: int(v) for n, v, _ in members}
        for sound, value, sid, _ui, _loop in CONTRACT:
            self.assertEqual(value, got[sound])
        inserted = [l for op in ops if op[0] == "insert" for l in new_lines[op[3]:op[4]]]
        summaries = [l for l in inserted if l.strip().startswith("/// <summary>R4 · AP-011 v024 ")]
        self.assertEqual(33, len(summaries))
        self.assertIn("        /// <summary>R4 · AP-011 v024 wb_prompt · PromptChanged false→true</summary>", inserted)
        i = new_lines.index("        TreeAuraUp = 1063,")
        self.assertEqual(("", "        /// <summary>WP-097 K1: label only — a slapback copy of the dry Fire clip (the clip is passed explicitly, no catalog row);"),
                         (new_lines[i + 1], new_lines[i + 2]))
        for sound, value, *_ in CONTRACT:   # each member: its summary line above, a blank line below
            j = new_lines.index("        %s = %d," % (sound, value))
            self.assertTrue(new_lines[j - 1].startswith("        /// <summary>R4 · "), sound)
            self.assertEqual("", new_lines[j + 1], sound)

    def _enum_with(self, extra):
        (self.root / ENUM).write_text(ENUM_TEXT.replace("        UiConfirm = 117,\n", "        UiConfirm = 117,\n" + extra), encoding="utf-8")

    def test_an_enum_name_with_another_value_stops_the_run(self):
        self._enum_with("        TreeCrit = 1100,\n")
        self.assert_refused(r"AudioSoundId\.TreeCrit = 1100, the contract says 1040")

    def test_an_enum_value_taken_by_another_name_stops_the_run(self):
        self._enum_with("        SomethingElse = 1000,\n")
        self.assert_refused(r"AudioSoundId value 1000 is taken by SomethingElse")

    def test_a_partial_r4_set_in_the_enum_stops_the_run(self):
        self._enum_with("        WallBuyCoins = 1000,\n")
        self.assert_refused(r"carries 1 of the 33 R4 members")

    # ---- idempotence, dry run, write boundary
    def test_a_second_run_writes_nothing(self):
        first = self.run_import()
        before = snapshot(self.root)
        second = self.run_import()
        self.assertGreater(len(first["changed"]), 0)
        self.assertEqual([], second["changed"])
        self.assertEqual(({"new": 0, "same": self.n_files}, 0, False, False, 33),
                         (second["wav"], second["meta"]["new"], second["manifest"]["changed"], second["enum"]["inserted"],
                          second["manifest"]["unchanged"]))
        self.assertEqual(before, snapshot(self.root))

    def test_a_dry_run_writes_nothing_and_lists_what_would_change(self):
        before = snapshot(self.root)
        s = self.run_import(dry_run=True)
        self.assertEqual(before, snapshot(self.root))
        self.assertTrue(s["dryRun"])
        self.assertEqual([], s["changed"])
        self.assertEqual(2 * self.n_files + 2, len(s["wouldChange"]))
        self.assertEqual(33, len(s["manifest"]["added"]))

    def test_only_the_expected_paths_change_and_the_catalog_is_untouched(self):
        before = snapshot(self.root)
        self.run_import()
        after = snapshot(self.root)
        changed = {k for k in after if before.get(k) != after[k]}
        expected = {MANIFEST, ENUM} | {AP011 + "/%s_%02d.wav%s" % (c[0], k, m) for c in CONTRACT
                                       for k in range(1, FILES.get(c[0], 1) + 1) for m in ("", ".meta")}
        self.assertEqual(expected, changed)
        self.assertEqual(before[CATALOG], after[CATALOG])
        self.assertTrue(set(before) <= set(after), "nothing removed")

    # ---- the command line
    def test_the_command_line(self):
        script = str(TOOLS / "audio" / "ap011_r4_import.py")
        cmd = [sys.executable, "-B", script]
        done = subprocess.run(cmd + [str(self.root), "--src", str(self.src), "--dry-run"], capture_output=True, text=True)
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertTrue(json.loads(done.stdout)["dryRun"])
        guids = self.tmp / "guids.txt"
        guids.write_text("main:x.meta:guid: %s\n" % hashlib.sha256(b"dd-r4-ap011:TreeOpen_01.wav").hexdigest()[:32], encoding="utf-8")
        done = subprocess.run(cmd + [str(self.root), "--src", str(self.src), "--known-guids", str(guids)], capture_output=True, text=True)
        self.assertEqual(1, done.returncode)
        self.assertIn("TreeOpen_01.wav: GUID", done.stderr)
        self.assertIn("nothing written", done.stderr)
        done = subprocess.run(cmd + [str(self.root), "--src", str(self.src)], capture_output=True, text=True)
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertEqual(33, len(json.loads(done.stdout)["manifest"]["added"]))
        for argv in ([], [str(self.tmp / "nope"), "--src", str(self.src)], [str(self.root), "--src", str(self.tmp / "no-package")],
                     [str(self.root), "--src", str(self.src), "--known-guids", str(self.tmp / "missing.txt")], [str(self.root), "--bogus"]):
            self.assertEqual(64, subprocess.run(cmd + argv, capture_output=True).returncode, argv)


if __name__ == "__main__":
    unittest.main()
