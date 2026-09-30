"""WP-078 K4 (C-00 30.09): wp078_report.AI_FLAGS must equal the AI argv flags the game's C# source parses.

Heuristic (source scan, no Unity):
  * Every Player argv flag in this project is a "-dd<Name>" C# string literal compared against
    Environment.GetCommandLineArgs() — directly (AttackStyles.ReadFlag: args[i] == "-ddAiV3") or through a
    `const string X = "-dd…"` / helper call such as QaArgs.Arg(X). A "-dd…" literal in non-test runtime source is
    therefore treated as a parsed flag; comments are stripped first so a doc mention is not a flag.
  * "AI scope" = the folders that own enemy AI, spawning, crowd/navigation and ragdolls: _Prototype/Enemies,
    _Prototype/Spawn, _Prototype/Navigation, _Prototype/Characters (RagdollProxyPool — S-06 -ddRagdoll lands here),
    plus any runtime file whose name says AI/crowd/ragdoll/corpse/threat/spawn. Editor/ and Tests/ folders are
    excluded (batchmode tools and test doubles are not Player flags).
  * A literal found in AI scope must be in AI_FLAGS or in NON_AI_FLAGS (explicit, reviewed allowlist).
  * Every AI_FLAGS entry must be a parsed literal somewhere in non-test source, in a file that reads argv.
  * None of the six specification-only flags (wp078_report.AI_FLAGS_NOT_PASS) may be parsed anywhere: when one
    lands on main it must move into AI_FLAGS, which grows the A3 Player smoke matrix by two runs.
"""
import importlib.util
import re
import unittest
from pathlib import Path

QA = Path(__file__).resolve().parents[1]
REPO = QA.parents[1]
ASSETS = REPO / "prototype" / "DeathsDemolition_PlayableConcept" / "Assets"
AI_DIRS = ("_Prototype/Enemies", "_Prototype/Spawn", "_Prototype/Navigation", "_Prototype/Characters")
AI_NAME = re.compile(r"(?:^Ai[A-Z0-9]|Crowd|Ragdoll|Corpse|Threat|Spawn)")
FLAG = re.compile(r'"(-dd[A-Za-z0-9]+)"')
ARGV_READER = re.compile(r"GetCommandLineArgs|QaArgs\.|\bArg\s*\(|\bHasFlag\s*\(")
# Reviewed non-AI flags that live in AI-scope folders. Extend only with a C-00 note.
NON_AI_FLAGS = {
    "-ddCharacters": "CharacterAssetSet: Procedural|Skinned art source A/B (WP-016), not AI behaviour",
}


def load_report():
    spec = importlib.util.spec_from_file_location("wp078_report_flags", QA / "wp078_report.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def strip_comments(text):
    """Remove // and /* */ comments but keep string literals (a "//" inside a string is not a comment)."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def runtime_sources():
    for path in sorted(ASSETS.rglob("*.cs")):
        parts = path.relative_to(ASSETS).parts
        if "Tests" in parts or "Editor" in parts:
            continue
        yield path


def in_ai_scope(path):
    relative = path.relative_to(ASSETS).as_posix()
    return any(relative.startswith(folder + "/") for folder in AI_DIRS) or bool(AI_NAME.search(path.stem))


class AiFlagSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = load_report()
        cls.flags = {}          # flag -> [(path, reads argv)]
        for path in runtime_sources():
            code = strip_comments(path.read_text(encoding="utf-8", errors="replace"))
            reads = bool(ARGV_READER.search(code))
            for flag in set(FLAG.findall(code)):
                cls.flags.setdefault(flag, []).append((path, reads))

    def test_sources_present(self):
        self.assertTrue(ASSETS.is_dir(), ASSETS)
        self.assertTrue((ASSETS / "_Prototype" / "Characters" / "RagdollProxyPool.cs").is_file(),
                        "RagdollProxyPool moved — update AI_DIRS so -ddRagdoll stays in scope")
        self.assertGreater(len(self.flags), 10, "the -dd literal scan found almost nothing; the heuristic is broken")

    def test_every_listed_flag_is_parsed(self):
        for flag in self.report.AI_FLAGS:
            with self.subTest(flag=flag):
                self.assertIn(flag, self.flags, flag + " is in AI_FLAGS but no C# source parses it")
                self.assertTrue(any(reads for _, reads in self.flags[flag]), flag + " literal is never next to an argv read")

    def test_no_unlisted_ai_flag(self):
        allowed = set(self.report.AI_FLAGS) | set(NON_AI_FLAGS)
        unlisted = sorted({(flag, path.relative_to(ASSETS).as_posix()) for flag, rows in self.flags.items()
                           for path, _ in rows if in_ai_scope(path) and flag not in allowed})
        self.assertEqual(unlisted, [], "new AI argv flag(s) in C#: add them to wp078_report.AI_FLAGS (the A3 Player smoke "
                                       "matrix grows by two runs each) or, if not AI, to NON_AI_FLAGS with a C-00 note")

    def test_specification_only_flags_not_parsed(self):
        landed = sorted(flag for flag in self.report.AI_FLAGS_NOT_PASS if flag in self.flags)
        self.assertEqual(landed, [], "specification flag(s) now parsed on this source: move them from AI_FLAGS_NOT_PASS "
                                     "to AI_FLAGS (C-00 30.09 K4)")
        self.assertFalse(set(self.report.AI_FLAGS) & set(self.report.AI_FLAGS_NOT_PASS))

    def test_heuristic_self_check(self):
        sample = 'const string A = "-ddRagdoll"; // "-ddCommented"\n/* "-ddBlock" */ var s = "a//b"; if (x == "-ddAiV3") {}'
        self.assertEqual(sorted(FLAG.findall(strip_comments(sample))), ["-ddAiV3", "-ddRagdoll"])
        self.assertTrue(in_ai_scope(ASSETS / "_Prototype" / "Characters" / "RagdollProxyPool.cs"))
        self.assertTrue(in_ai_scope(ASSETS / "_Prototype" / "UI" / "CrowdDebug.cs"))
        self.assertFalse(in_ai_scope(ASSETS / "_Prototype" / "Core" / "Input" / "AimAssist.cs"))


if __name__ == "__main__":
    unittest.main()
