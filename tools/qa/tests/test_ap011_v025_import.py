"""WP-101 R4 (C-00 12.10): tools/audio/ap011_v025_import.py — real-take impact variations + the sr_1 layer, on a synthetic package.

A temp worktree (test_ap011_r4_import.build_worktree: the AP011 template .meta files, a small manifest, the enum) gains an
ImpactV3StoneLight row with three existing files; a temp v025/final holds two new Stone files and one ImpactSniperCrack file
(MANIFEST.json in the v025 shape, SHA256SUMS, 48 kHz / 24-bit / mono). Checked: byte copies + the .meta (GUID
sha256("dd-v025-ap011:" + name)[:32], only the guid line differs from the one-shot template), the Stone row (new files after
the v018 ones, lufs / noise floor stay file 1's, worst-of true peak, provenance tag once, version), the new sniper row (the 18
fields in order, target -22, SFX-World, spatial), idempotence, and the refusals (other bytes at the destination, a SHA off
SHA256SUMS) with nothing written."""
import contextlib
import hashlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / "audio"))
import test_ap011_r4_import as base  # noqa: E402  (fixtures: worktree, 24-bit writer, BS.1770 levelling)
import ap011_v025_import as imp  # noqa: E402

STONE = "ImpactV3StoneLight"


def tone(freq, seed):
    x = base.decaying(freq)
    return x + 1e-4 * np.random.default_rng(seed).standard_normal(len(x))


def build_package(final):
    final.mkdir(parents=True)
    spec = [(f"{STONE}_04.wav", STONE, 900.0, -20.0), (f"{STONE}_05.wav", STONE, 1100.0, -20.0),
            ("ImpactSniperCrack_01.wav", "ImpactSniperCrack", 2400.0, -22.0)]
    files = []
    for i, (name, sound, freq, target) in enumerate(spec):
        base.write_wav24(final / name, base.levelled(tone(freq, i), target))
        files.append({"file": name, "sound": sound, "sources": [f"2020/PMSFX - Shattering Bricks/take_{i}.wav"],
                      "sha256": base.sha256(final / name)})
    (final / "MANIFEST.json").write_text(json.dumps({"package": "AP-011", "version": "v025", "files": files}, indent=1), encoding="utf-8")
    (final / "SHA256SUMS").write_text("".join(f"{f['sha256']}  {f['file']}\n" for f in files), encoding="utf-8")


def add_stone_row(root):
    ap011 = root / base.AP011
    names = [f"{STONE}_0{i}.wav" for i in (1, 2, 3)]
    for i, n in enumerate(names):
        base.write_wav24(ap011 / n, base.levelled(tone(600.0 + 100 * i, 10 + i), -20.0))
    man_path = root / base.MANIFEST
    man = json.loads(man_path.read_text(encoding="utf-8"))
    man["entries"].append({"sound": STONE, "event": "ProjectileEvents.Impact Stone light", "files": names, "loop": False, "lufs": -20.0,
                           "truePeakDb": -1.5, "noiseFloorDb": -120.0, "dcDb": -124.2, "clipped": 0, "bitDepth": 24, "targetLufs": -20.0,
                           "ceilingDbtp": -1.5, "continuous": False, "endSilenceDb": -240.0, "layers": "derived (v005)",
                           "bus": "SFX-World", "spatial": True, "provenance": "derived offline (AP-011 v018)"})
    man["entries"].sort(key=lambda e: e["sound"])
    man_path.write_text(json.dumps(man, indent=1, ensure_ascii=False), encoding="utf-8")


class Ap011V025ImportTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        t = Path(self._tmp.name)
        self.src = t / "v025" / "final"
        build_package(self.src)
        self.root = t / "wt"
        base.build_worktree(self.root)
        add_stone_row(self.root)

    def tearDown(self):
        self._tmp.cleanup()

    def run_tool(self, *extra):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            rc = imp.main([str(self.root), "--src", str(self.src), *extra])
        return rc, out.getvalue()

    def rows(self):
        return {e["sound"]: e for e in json.loads((self.root / base.MANIFEST).read_text(encoding="utf-8"))["entries"]}

    def test_copies_and_metas_with_the_deterministic_guid(self):
        self.assertEqual(0, self.run_tool()[0])
        ap011 = self.root / base.AP011
        template = (ap011 / "ChainArc_01.wav.meta").read_text(encoding="utf-8")
        for f in json.loads((self.src / "MANIFEST.json").read_text())["files"]:
            self.assertEqual(f["sha256"], base.sha256(ap011 / f["file"]))
            meta = (ap011 / (f["file"] + ".meta")).read_text(encoding="utf-8")
            guid = hashlib.sha256(("dd-v025-ap011:" + f["file"]).encode()).hexdigest()[:32]
            self.assertIn("guid: " + guid, meta)
            diff = [(a, b) for a, b in zip(template.splitlines(), meta.splitlines()) if a != b]
            self.assertEqual(1, len(diff), "only the guid line differs")

    def test_the_stone_row_keeps_file_one_and_appends(self):
        before = self.rows()[STONE]
        self.assertEqual(0, self.run_tool()[0])
        row = self.rows()[STONE]
        self.assertEqual(before["files"] + [f"{STONE}_04.wav", f"{STONE}_05.wav"], row["files"])
        self.assertEqual(before["lufs"], row["lufs"], "lufs is file 1's stamp")
        self.assertEqual(before["noiseFloorDb"], row["noiseFloorDb"])
        self.assertLessEqual(row["truePeakDb"], -1.5)
        self.assertEqual(1, row["provenance"].count(imp.TAG))
        self.assertEqual(list(base.FIELDS) if hasattr(base, "FIELDS") else list(imp.r4.FIELDS), list(row.keys()))
        self.assertEqual("ap011-v025", json.loads((self.root / base.MANIFEST).read_text())["version"])

    def test_the_sniper_row_is_new_and_complete(self):
        self.assertEqual(0, self.run_tool()[0])
        row = self.rows()[imp.SNIPER]
        self.assertEqual(list(imp.r4.FIELDS), list(row.keys()))
        self.assertEqual(["ImpactSniperCrack_01.wav"], row["files"])
        self.assertEqual(-22.0, row["targetLufs"])
        self.assertEqual("SFX-World", row["bus"])
        self.assertTrue(row["spatial"])
        self.assertAlmostEqual(-22.0, row["lufs"], delta=0.6)

    def test_a_second_run_writes_nothing(self):
        self.assertEqual(0, self.run_tool()[0])
        snap = base.snapshot(self.root)
        rc, out = self.run_tool()
        self.assertEqual(0, rc)
        self.assertEqual([], json.loads(out)["changed"])
        self.assertEqual(snap, base.snapshot(self.root))

    def test_other_bytes_at_the_destination_are_never_overwritten(self):
        dst = self.root / base.AP011 / f"{STONE}_04.wav"
        shutil.copy(self.root / base.AP011 / f"{STONE}_01.wav", dst)
        snap = base.snapshot(self.root)
        self.assertEqual(1, self.run_tool()[0])
        self.assertEqual(snap, base.snapshot(self.root), "a refused run writes nothing")

    def test_a_sha_off_sha256sums_stops_the_run(self):
        sums = self.src / "SHA256SUMS"
        sums.write_text(sums.read_text().replace(sums.read_text()[:8], "00000000", 1))
        snap = base.snapshot(self.root)
        self.assertEqual(1, self.run_tool()[0])
        self.assertEqual(snap, base.snapshot(self.root))

    def test_a_dry_run_writes_nothing(self):
        snap = base.snapshot(self.root)
        rc, out = self.run_tool("--dry-run")
        self.assertEqual(0, rc)
        self.assertTrue(json.loads(out)["wouldChange"])
        self.assertEqual(snap, base.snapshot(self.root))


if __name__ == "__main__":
    unittest.main()
