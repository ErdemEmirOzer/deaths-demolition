"""C-00 30.09 Y6: tools/qa/nunit_skips.py — the one skip rule (Skipped ∧ Explicit ∧ Diagnostics) for every NUnit gate."""
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import nunit_skips as ns  # noqa: E402

XML = """<test-run result="Passed" skipped="4">
  <test-suite type="TestFixture" fullname="A.Fixture"><properties><property name="Category" value="Diagnostics"/></properties>
    <test-case fullname="A.Fixture.OnFixture" result="Skipped" runstate="Explicit"/>
  </test-suite>
  <test-suite type="TestFixture" fullname="B.Fixture">
    <test-case fullname="B.Fixture.OnCase" result="Skipped" label="Explicit" runstate="Runnable">
      <properties><property name="Category" value="Diagnostics"/></properties></test-case>
    <test-case fullname="B.Fixture.IgnoreOptIn" result="Skipped" label="Ignored" runstate="Runnable">
      <properties><property name="Category" value="Diagnostics"/></properties></test-case>
    <test-case fullname="B.Fixture.ExplicitSlow" result="Skipped" label="Explicit" runstate="Explicit">
      <properties><property name="Category" value="Slow"/></properties></test-case>
    <test-case fullname="B.Fixture.Ran" result="Passed" runstate="Runnable"/>
  </test-suite>
</test-run>"""


class NunitSkipsTests(unittest.TestCase):
    def test_only_explicit_diagnostics_is_allowed(self):
        allowed, legacy, unexpected = ns.classify(ns.skipped_cases_from_root(ET.fromstring(XML)))
        self.assertEqual(["A.Fixture.OnFixture", "B.Fixture.OnCase"], allowed)
        self.assertEqual([], legacy)
        self.assertEqual(["B.Fixture.ExplicitSlow [Explicit]", "B.Fixture.IgnoreOptIn [Ignored]"], unexpected)

    def test_legacy_names_are_reported_separately(self):
        allowed, legacy, unexpected = ns.classify(ns.skipped_cases_from_root(ET.fromstring(XML)), {"B.Fixture.IgnoreOptIn"})
        self.assertEqual(["B.Fixture.IgnoreOptIn"], legacy)
        self.assertEqual(["B.Fixture.ExplicitSlow [Explicit]"], unexpected)

    def test_unreadable_file_has_no_cases(self):
        self.assertEqual([], ns.skipped_cases("/nonexistent/editmode.xml"))


if __name__ == "__main__":
    unittest.main()
