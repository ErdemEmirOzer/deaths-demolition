#!/usr/bin/env python3
"""C-00 12.10 · the runtime files of DD.Prototype.Qa (Diagnostics/Qa/*.cs, not Editor/ or Tests/) are development-only: each
starts with `#if DEVELOPMENT_BUILD || UNITY_EDITOR` and ends with `#endif`, like QaRunBot / QaArgs / QaVirtualGamepad. Two files
without the guard broke every release and release-probe build of main in one night (QaCrowdWalkScenario 5ad4b583 → fixed
15ba88b5, QaTreeBot a0046369 → fixed c494dcd4): this static check catches the next one in a second, without Unity.
usage: python3 tools/tests/test_qa_dev_guard.py"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
QA = REPO / "prototype" / "DeathsDemolition_PlayableConcept" / "Assets" / "_Prototype" / "Diagnostics" / "Qa"
GUARD = re.compile(r"^#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR\b")


def unguarded(folder):
    """[(file, reason)] of the folder's own .cs files (not sub-folders) that are not wholly development-only."""
    out = []
    for p in sorted(folder.glob("*.cs")):
        lines = [l for l in p.read_text(encoding="utf-8-sig", errors="replace").splitlines() if l.strip()]
        if not lines or not GUARD.match(lines[0].strip()):
            out.append((p.name, "the first line is not `#if DEVELOPMENT_BUILD || UNITY_EDITOR`"))
        elif not lines[-1].strip().startswith("#endif"):
            out.append((p.name, "the last line is not `#endif`"))
    return out


class QaDevGuardTests(unittest.TestCase):
    def test_QaKlasoru_HerCalismaDosyasiYalnizDevVeEditor(self):
        self.assertTrue(QA.is_dir(), QA)
        self.assertGreaterEqual(len(list(QA.glob("*.cs"))), 5, "the folder holds the QA runtime files")
        self.assertEqual([], unguarded(QA), "a DD.Prototype.Qa runtime file without the dev guard breaks the release builds")

    def test_Kontrol_KorumasizDosyayiYakalar(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            (d / "Ok.cs").write_text("#if DEVELOPMENT_BUILD || UNITY_EDITOR\nusing System;\nclass A {}\n#endif\n")
            (d / "Bare.cs").write_text("using System;\nclass B { void F() { QaArgs.Arg(\"x\"); } }\n")
            (d / "Open.cs").write_text("#if DEVELOPMENT_BUILD || UNITY_EDITOR\nclass C {}\n")
            self.assertEqual(["Bare.cs", "Open.cs"], [n for n, _ in unguarded(d)])


if __name__ == "__main__":
    unittest.main(verbosity=2)
