"""C-00B B-02 (WP-091, C-00 05.10): tools/qa/r2_gates_lint.py — r2-gates.json ↔ r2-measure-contract.md ↔ R2Olcum.Yaz."""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import r2_gates_lint as lint  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
PLAY = lint.PROJECT + "/Assets/_Prototype/Tests/PlayMode"
EDIT = lint.PROJECT + "/Assets/_Prototype/Tests/EditMode"

GATES = {
    "schema": "dd-r2-gates-v1",
    "gates": [
        {"id": "H1", "area": "hero", "kind": "gate", "owner": "S-06", "title": "grip",
         "checks": [{"key": "maxCm", "op": "<=", "limit": 1.0}], "report": ["elbowMaxDeg"],
         "measuredBy": {"suite": "playmode", "filter": "DD.Prototype.Tests.PlayMode.HeroTests.H1_Olc", "quiet": False}},
        {"id": "U3", "area": "ui", "kind": "gate", "owner": "S-09", "title": "rotation",
         "tests": ["DD.Prototype.Tests.EditMode.ArtTests.U3_Olc"],
         "checks": [{"key": "repeats", "op": "==", "limit": 0}],
         "measuredBy": {"suite": "editmode", "filter": "DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", "quiet": False}},
        {"id": "P5", "area": "perf", "kind": "report", "owner": "S-09", "title": "load", "report": ["ratio"]},
        {"id": "H5", "area": "hero", "kind": "human", "owner": "S-06", "title": "AD"},
    ],
}

PROSE = """# R2 measurement contract

Write it with the shared writer **`R2Olcum.Yaz(gate, source, values, samples)`**
(`Assets/_Prototype/Tests/Shared/R2Olcum.cs`).

```json
{"source": "DD.Art.Tests.PlayMode.NotARealTests.Example", "values": {"fakeKey": 1}}
```

Some gates (`tests` in `r2-gates.json`, e.g. U3) also require a test case by full name.

## H1 elbow (report)

`HeroTests.H1_Olc` writes `elbowMaxDeg` next to `maxCm`. Reported until the import: then a check `elbowMaxDeg <= 165`.
H1 = grip table (S-06).

## P5 (load)

No owner test: the chain writes `r2-P5.json`. Its key `ratio` (env `DD_R2_COMMIT`).

## Human gates

H5: one review file `<E>/r2/reviews/<gate id>.json`.

## Report entries

`kind: "report"` (P5) never fail.
"""

HERO = """using System.Collections;
using System.Collections.Generic;
namespace DD.Prototype.Tests.PlayMode
{
    public sealed class HeroTests
    {
        public const string H1Name = "DD.Prototype.Tests.PlayMode.HeroTests.H1_Olc";

        [UnityTest]
        [Explicit]
        public IEnumerator H1_Olc()
        {
            yield return null;
            string note = "a { brace, and \\"quote\\" in a string";   // R2Olcum.Yaz("ZZ", "x", null, 0) in a comment
            R2Olcum.Yaz("H1", H1Name, new Dictionary<string, double> { { "maxCm", Max(1.0, 2.0) }, { "elbowMaxDeg", 150 } }, 3);
        }

        private static double Max(double a, double b) { return a > b ? a : b; }
    }
}
"""

ART = """using System.Collections.Generic;
namespace DD.Prototype.Tests.EditMode
{
    public sealed class ArtTests
    {
        [Test]
        public void U3_Olc()
        {
            var v = new Dictionary<string, double> { { "repeats", 0 } };
            R2Olcum.Yaz("U3", "DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", v, 20);
        }
    }
}
"""

SHARED = "namespace DD.Prototype.Tests { public static class R2Olcum { } }\n"


class Fixture:
    """a throw-away repo: tools/qa/r2-gates.json + r2-measure-contract.md + the C# tests (edited per test)."""

    def __init__(self, tmp):
        self.root = Path(tmp)
        self.gates = json.loads(json.dumps(GATES))
        self.prose = PROSE
        self.cs = {PLAY + "/HeroTests.cs": HERO, EDIT + "/ArtTests.cs": ART,
                   lint.PROJECT + "/Assets/_Prototype/Tests/Shared/R2Olcum.cs": SHARED}

    def gate(self, gid):
        return next(g for g in self.gates["gates"] if g["id"] == gid)

    def write(self):
        qa = self.root / "tools/qa"
        qa.mkdir(parents=True, exist_ok=True)
        (qa / "r2-gates.json").write_text(json.dumps(self.gates), encoding="utf-8")
        (qa / "r2-measure-contract.md").write_text(self.prose, encoding="utf-8")
        for rel, text in self.cs.items():
            p = self.root / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text, encoding="utf-8")

    def run(self):
        self.write()
        findings, _ = lint.run(self.root)
        return findings


def codes(findings, level=None):
    return sorted((f.code, f.gate) for f in findings if level is None or f.level == level)


class LintTestCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.fx = Fixture(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()


class CleanContractTests(LintTestCase):
    def test_an_agreeing_contract_prose_and_writers_has_no_error_or_warning(self):
        findings = self.fx.run()
        self.assertEqual([], codes(findings, "ERROR"))
        self.assertEqual([], codes(findings, "WARN"))
        info = codes(findings, "INFO")
        self.assertIn(("REPORT-ONLY", "H1"), info)        # elbowMaxDeg rides next to the check
        self.assertIn(("PLANNED-CHECK", "H1"), info)      # the prose's future elbowMaxDeg <= 165
        self.assertIn(("CHAIN-MADE", "P5"), info)         # "No owner test"

    def test_the_writer_scan_reads_consts_dictionary_variables_and_skips_comments_and_strings(self):
        self.fx.write()
        writes = lint.scan_writes(lint.load_cs(self.fx.root))
        by_gate = {w["gate"]: w for w in writes}
        self.assertEqual({"H1", "U3"}, set(by_gate))      # the commented-out ZZ call is not a writer
        self.assertEqual("DD.Prototype.Tests.PlayMode.HeroTests.H1_Olc", by_gate["H1"]["source"])
        self.assertEqual(["maxCm", "elbowMaxDeg"], by_gate["H1"]["keys"])
        self.assertTrue(by_gate["H1"]["explicit"])
        self.assertEqual(["repeats"], by_gate["U3"]["keys"])
        self.assertEqual("DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", by_gate["U3"]["test"])


class WriterTests(LintTestCase):
    def test_a_contract_key_the_test_never_writes_is_an_error(self):
        self.fx.gate("H1")["checks"].append({"key": "leftCm", "op": "<=", "limit": 1.0})
        self.assertIn(("KEY-MISSING", "H1"), codes(self.fx.run(), "ERROR"))

    def test_a_key_the_contract_does_not_list_is_an_error(self):
        self.fx.gate("H1")["report"] = []
        self.assertIn(("KEY-EXTRA", "H1"), codes(self.fx.run(), "ERROR"))

    def test_a_writer_for_an_unknown_or_a_human_gate_is_an_error(self):
        self.fx.cs[PLAY + "/HeroTests.cs"] = HERO.replace('R2Olcum.Yaz("H1"', 'R2Olcum.Yaz("H9"')
        found = codes(self.fx.run(), "ERROR")
        self.assertIn(("GATE-UNKNOWN", "H9"), found)
        self.assertIn(("NO-WRITER", "H1"), found)        # measuredBy runs a test that writes nothing for H1
        self.fx.cs[PLAY + "/HeroTests.cs"] = HERO.replace('R2Olcum.Yaz("H1"', 'R2Olcum.Yaz("H5"')
        self.assertIn(("WRITER-HUMAN", "H5"), codes(self.fx.run(), "ERROR"))

    def test_a_source_copied_from_another_test_is_an_error(self):
        self.fx.cs[EDIT + "/ArtTests.cs"] = ART.replace('"DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", v', '"DD.Prototype.Tests.EditMode.ArtTests.U2_Eski", v')
        found = codes(self.fx.run(), "ERROR")
        self.assertIn(("SOURCE-MISMATCH", "U3"), found)

    def test_an_unresolvable_source_is_an_error(self):
        self.fx.cs[PLAY + "/HeroTests.cs"] = HERO.replace("R2Olcum.Yaz(\"H1\", H1Name,", "R2Olcum.Yaz(\"H1\", Prefix + \"H1_Olc\",")
        self.assertIn(("SOURCE-UNRESOLVED", "H1"), codes(self.fx.run(), "ERROR"))

    def test_a_check_key_written_only_on_one_path_warns_and_a_null_key_is_listed(self):
        self.fx.cs[EDIT + "/ArtTests.cs"] = ART.replace(
            'var v = new Dictionary<string, double> { { "repeats", 0 } };',
            'var v = new Dictionary<string, double>();\n            if (true) v["repeats"] = 0;')
        self.assertIn(("KEY-CONDITIONAL", "U3"), codes(self.fx.run(), "WARN"))
        self.fx.cs[EDIT + "/ArtTests.cs"] = ART.replace(
            'var v = new Dictionary<string, double> { { "repeats", 0 } };\n            R2Olcum.Yaz("U3", "DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", v, 20);',
            'R2Olcum.Yaz("U3", "DD.Prototype.Tests.EditMode.ArtTests.U3_Olc", new Dictionary<string, double?> { { "repeats", null } }, 0, "not yet");')
        findings = self.fx.run()
        self.assertIn(("KEY-NULL", "U3"), codes(findings, "INFO"))
        self.assertEqual([], codes(findings, "ERROR"))


class MeasuredByTests(LintTestCase):
    def test_a_filter_that_names_no_test_is_an_error(self):
        self.fx.gate("H1")["measuredBy"]["filter"] = "DD.Prototype.Tests.PlayMode.HeroTests.H1_Yok"
        found = codes(self.fx.run(), "ERROR")
        self.assertIn(("MEASUREDBY-NOTEST", "H1"), found)
        self.assertIn(("MEASUREDBY-SOURCE", "H1"), found)

    def test_an_explicit_test_needs_its_full_name_in_the_filter(self):
        self.fx.gate("H1")["measuredBy"]["filter"] = "DD.Prototype.Tests.PlayMode.HeroTests"
        self.assertIn(("EXPLICIT-FILTER", "H1"), codes(self.fx.run(), "ERROR"))

    def test_the_suite_must_be_where_the_test_lives(self):
        self.fx.gate("U3")["measuredBy"]["suite"] = "playmode"
        self.assertIn(("MEASUREDBY-SUITE", "U3"), codes(self.fx.run(), "ERROR"))

    def test_an_explicit_writer_without_measured_by_is_never_run_by_the_chain(self):
        del self.fx.gate("H1")["measuredBy"]
        self.assertIn(("EXPLICIT-UNRUN", "H1"), codes(self.fx.run(), "WARN"))

    def test_a_gate_nobody_writes_warns_without_measured_by(self):
        self.fx.gates["gates"].append({"id": "L1", "area": "locomotion", "kind": "gate", "owner": "S-06", "title": "slide",
                                       "checks": [{"key": "avg", "op": "<=", "limit": 0.05}]})
        self.assertIn(("NO-WRITER", "L1"), codes(self.fx.run(), "WARN"))


class ToolWriterTests(LintTestCase):
    """WP-085 S3: a gate no C# test writes, whose prose section names the owner's Python writer."""

    def setUp(self):
        super().setUp()
        self.fx.gates["gates"].append({"id": "S3", "area": "sound", "kind": "gate", "owner": "S-08", "title": "dense",
                                       "checks": [{"key": "truePeakDbtp", "op": "<=", "limit": -1.0}]})
        self.fx.prose = PROSE.replace("## Human gates", "## S3 (dense)\n\nWriter: `tools/qa/s3_tool.py` (S-08), the key "
                                      "`truePeakDbtp`.\n\n## Human gates")

    def tool(self, text):
        path = self.fx.root / "tools/qa/s3_tool.py"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def test_the_named_python_writer_stands_for_the_missing_csharp_writer(self):
        self.tool('KEYS = ("truePeakDbtp",)\n')
        findings = self.fx.run()
        self.assertIn(("TOOL-WRITER", "S3"), codes(findings, "INFO"))
        self.assertNotIn(("NO-WRITER", "S3"), codes(findings))
        self.assertEqual([], codes(findings, "ERROR"))

    def test_a_named_writer_that_is_missing_or_never_names_a_key_is_an_error(self):
        self.assertIn(("TOOL-WRITER", "S3"), codes(self.fx.run(), "ERROR"))   # no such file
        self.tool('KEYS = ("truePeak",)\n')
        self.assertIn(("KEY-MISSING", "S3"), codes(self.fx.run(), "ERROR"))

    def test_without_the_writer_line_the_gate_still_warns(self):
        self.fx.prose = PROSE
        self.assertIn(("NO-WRITER", "S3"), codes(self.fx.run(), "WARN"))

    def test_measured_by_without_a_csharp_writer_stays_an_error(self):
        self.tool('KEYS = ("truePeakDbtp",)\n')
        self.fx.gate("S3")["measuredBy"] = {"suite": "playmode", "filter": "DD.Prototype.Tests.PlayMode.HeroTests.H1_Olc",
                                            "quiet": True}
        self.assertIn(("NO-WRITER", "S3"), codes(self.fx.run(), "ERROR"))


class RequiredTestTests(LintTestCase):
    def test_a_required_test_missing_from_the_repo_is_an_error(self):
        self.fx.gate("U3")["tests"] = ["DD.Prototype.Tests.EditMode.ArtTests.U3_Yok"]
        self.assertIn(("TEST-MISSING", "U3"), codes(self.fx.run(), "ERROR"))

    def test_an_explicit_required_test_no_session_names_warns(self):
        self.fx.gate("U3")["tests"] = ["DD.Prototype.Tests.PlayMode.HeroTests.H1_Olc"]
        self.assertEqual([], [c for c in codes(self.fx.run(), "WARN") if c[0] == "TEST-EXPLICIT"])  # H1's session runs it
        self.fx.gate("U3")["tests"] = ["DD.Prototype.Tests.PlayMode.HeroTests.Diger"]
        self.fx.cs[PLAY + "/HeroTests.cs"] = HERO.replace(
            "        private static double Max",
            "        [UnityTest]\n        [Explicit]\n        public IEnumerator Diger() { yield return null; }\n\n        private static double Max")
        self.assertIn(("TEST-EXPLICIT", "U3"), codes(self.fx.run(), "WARN"))


class ProseTests(LintTestCase):
    def test_the_human_and_report_lists_must_match_the_kinds(self):
        self.fx.prose = self.fx.prose.replace("H5: one review file", "H5, L6: one review file")
        self.assertIn(("PROSE-LIST", "-"), codes(self.fx.run(), "ERROR"))

    def test_an_owner_split_in_the_prose_must_match(self):
        self.fx.prose = self.fx.prose.replace("H1 = grip table (S-06)", "H1 = grip table (S-02)")
        self.assertIn(("PROSE-OWNER", "H1"), codes(self.fx.run(), "ERROR"))

    def test_a_key_or_a_limit_the_gate_does_not_have_is_an_error(self):
        self.fx.prose = self.fx.prose.replace("next to `maxCm`", "next to `maxCm` and `wristDeg`")
        self.assertIn(("PROSE-KEY", "H1"), codes(self.fx.run(), "ERROR"))
        self.fx.prose = PROSE.replace("a check `elbowMaxDeg <= 165`", "a check `maxCm <= 2.0`")
        self.assertIn(("PROSE-LIMIT", "H1"), codes(self.fx.run(), "ERROR"))

    def test_a_stale_path_or_test_name_in_the_prose_is_an_error(self):
        self.fx.prose = self.fx.prose.replace("Tests/Shared/R2Olcum.cs", "Tests/PlayMode/R2Olcum.cs")
        self.assertIn(("PROSE-PATH", "-"), codes(self.fx.run(), "ERROR"))
        self.fx.prose = PROSE.replace("`HeroTests.H1_Olc`", "`HeroTests.H1_Eski`")
        self.assertIn(("PROSE-TEST", "-"), codes(self.fx.run(), "ERROR"))

    def test_code_fences_env_names_and_artifact_names_are_not_read_as_claims(self):
        findings = self.fx.run()   # the fence names NotARealTests/fakeKey, the prose DD_R2_COMMIT and r2-P5.json
        self.assertEqual([], codes(findings, "ERROR"))


class CliTests(LintTestCase):
    def _main(self, *args):
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(io.StringIO()):
            rc = lint.main(["--repo", str(self.fx.root), *args])
        return rc, buf.getvalue()

    def test_exit_codes(self):
        self.fx.write()
        self.assertEqual(0, self._main()[0])
        del self.fx.gate("H1")["measuredBy"]   # EXPLICIT-UNRUN: a warning
        self.fx.write()
        self.assertEqual(0, self._main()[0])
        self.assertEqual(1, self._main("--strict")[0])
        self.fx.gate("H1")["report"] = []      # KEY-EXTRA: an error
        self.fx.write()
        rc, out = self._main("--json")
        self.assertEqual(1, rc)
        data = json.loads(out)
        self.assertEqual("dd-r2-gates-lint-v1", data["schema"])
        self.assertGreaterEqual(data["counts"]["ERROR"], 1)
        (self.fx.root / "tools/qa/r2-gates.json").unlink()
        self.assertEqual(2, self._main()[0])

    def test_an_invalid_json_contract_is_exit_2(self):
        self.fx.gates["schema"] = "other"
        self.fx.write()
        self.assertEqual(2, self._main()[0])


def tools_only_checkout():
    """A sparse checkout without the project's C# (a tools-only worktree): the lint has no writer to read there. A full
    checkout without them is still a failure (C-00 06.10, as test_wp078_ai_flags does)."""
    import subprocess
    if any((REPO / lint.PROJECT / "Assets").rglob("*.cs")):
        return False
    r = subprocess.run(["git", "-C", str(REPO), "config", "--get", "core.sparseCheckout"], capture_output=True, text=True)
    return r.stdout.strip() == "true"


class RealRepoTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if tools_only_checkout():
            raise unittest.SkipTest("tools-only sparse checkout (no C# under %s) — run in a full checkout" % (REPO / lint.PROJECT))

    def test_every_writer_on_this_checkout_parses(self):
        # thresholds and coverage change by reviewed commits; what must hold is that the lint can read every call
        findings, writes = lint.run(REPO)
        self.assertTrue(writes, "no R2Olcum.Yaz call found")
        unreadable = [f for f in findings if f.code in ("WRITER-UNPARSED", "WRITER-KEYS", "SOURCE-UNRESOLVED")]
        self.assertEqual([], unreadable)
        self.assertTrue(all(w["test"] for w in writes), [w for w in writes if not w["test"]])


if __name__ == "__main__":
    unittest.main()
