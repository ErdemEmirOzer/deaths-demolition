#!/usr/bin/env python3
"""WP-079G (C-00 30.09) · tools/ci-owners.tsv: every rule compiles, known fixtures map to their owners, and no test
fixture in the repo falls through to the C-00 catch-all (run before adding suites to CI). usage: python3 tools/tests/test_ci_owners.py"""
import re, subprocess, unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parent


def rules():
    out = []
    for line in (TOOLS / "ci-owners.tsv").read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        rx, owner = (line.split("\t") + ["", ""])[:2]
        out.append((re.compile(rx), owner))
    return out


def owner(name, rs):
    return next(o for r, o in rs if r.search(name))


class CiOwnersTests(unittest.TestCase):
    def test_Sahipler_BilinenFiksturler_DogruOturuma(self):
        rs = rules()
        expected = {"NodeCardTests": "S-04", "RunTreePinActionRegressionTests": "S-09", "RunTreeTests": "S-04",
                    "Wp073FireVfxTests": "S-07", "QualityTiersTests": "S-05", "V6AiRoundTests": "S-03",
                    "CameraModeServiceTests": "S-01", "BootFirstSceneHandOffTests": "S-01", "EnemyDamagesPlayerTests": "S-02",
                    "AnimationTests": "S-06", "AudioSliceEditModeTests": "S-08", "QaRunBotTests": "S-10"}
        for name, own in expected.items():
            self.assertEqual(owner(name, rs), own, name)

    def test_Sahipler_DepodakiHicbirFikstur_CatchAlldaKalmaz(self):
        rs = rules()
        files = subprocess.run(["git", "-C", str(REPO), "ls-files", "prototype/*Tests/*.cs", "prototype/*Qa/Tests/*.cs"],
                               capture_output=True, text=True, check=True).stdout.split()
        unmapped = set()
        for f in files:
            src = subprocess.run(["git", "-C", str(REPO), "show", "HEAD:" + f], capture_output=True, text=True).stdout   # sparse worktrees too
            for m in re.finditer(r"\bclass\s+(\w+Tests?)\b", src):
                if owner(m.group(1), rs) == "C-00":
                    unmapped.add(m.group(1))
        self.assertEqual(sorted(unmapped), [], "add an owner row in tools/ci-owners.tsv")


if __name__ == "__main__":
    unittest.main(verbosity=2)
