#!/usr/bin/env python3
"""WP-089 R3 · C-00 07.10 (decision C): a fixture that has a METHOD-level [Category("FPTP")] or [Category("Diagnostics")]
carries NO class-level [Category]; its class category (WP042 …) goes down to every test method.

Why: Unity Test Framework 1.1.33 (Library/PackageCache/com.unity.test-framework@1.1.33,
UnityEngine.TestRunner/NUnitExtensions/Filters/CategoryFilterExtended.cs) — Match() returns false for a TestMethod whose
fixture has ANY category, so a method-level category there is invisible to -testCategory, for inclusion and exclusion
alike: main-ci's '!^Diagnostics$;!^FPTP$' runs such a test (S-05 07.10: LookReference / QualityModeApplier FP/TP halves;
main-ci 14fc5bc5 lists 8 [Explicit] method-level Diagnostics tests that the exclusion let through — skipped only because
they are Explicit). A class-level category, or a method category in an uncategorized fixture, is seen.

The scan: every C# file under the Unity project's Assets/ that uses NUnit. A small C# lexer skips comments, strings
(regular, verbatim, interpolated with nested holes) and char literals, so braces and brackets inside them never move the
structure; attributes are read only where a declaration can start (directly in a namespace / type body). A class
inherits its base classes' categories and test methods (NUnit's [Category] is Inherited = true); abstract classes are not
fixtures; a nested class is its own fixture. Each file must end with its braces balanced, else the scan fails loudly.

usage: python3 tools/tests/test_category_fixture_rule.py"""
import re
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "prototype" / "DeathsDemolition_PlayableConcept" / "Assets"
RULE_CATEGORIES = ("FPTP", "Diagnostics")
TEST_ATTRS = ("Test", "UnityTest", "TestCase", "TestCaseSource", "Theory")
TYPE_KEYWORDS = ("class", "struct", "interface", "enum", "record")
RULE = ('a fixture with a method-level [Category("FPTP")] or [Category("Diagnostics")] carries no class-level [Category]: '
        'its class category goes down to every test method (C-00 07.10; UTF 1.1.33 CategoryFilterExtended ignores a '
        'method category in a categorized fixture)')


class ScanError(ValueError):
    pass


def tokens(src):
    """(kind, text, line) — kinds: id, str, punct; comments, whitespace and preprocessor lines dropped."""
    out, i, n, line = [], 0, len(src), 1

    def string_end(i):
        """index just past the string literal starting at i (prefix $ / @ / $@ / @$ included)."""
        j, interp, verb = i, False, False
        while src[j] in "$@":
            interp |= src[j] == "$"; verb |= src[j] == "@"; j += 1
        assert src[j] == '"'
        j += 1
        while j < n:
            c = src[j]
            if verb and c == '"' and j + 1 < n and src[j + 1] == '"':
                j += 2; continue
            if not verb and c == "\\":
                j += 2; continue
            if c == '"':
                return j + 1
            if interp and c == "{":
                if j + 1 < n and src[j + 1] == "{":
                    j += 2; continue
                j = hole_end(j + 1); continue
            if interp and c == "}" and j + 1 < n and src[j + 1] == "}":
                j += 2; continue
            if c == "\n" and not verb:
                raise ScanError("newline in a string literal (line %d)" % (src.count("\n", 0, i) + 1))
            j += 1
        raise ScanError("unterminated string literal (line %d)" % (src.count("\n", 0, i) + 1))

    def hole_end(j):
        """index just past the '}' closing an interpolation hole that starts at j."""
        depth = 0
        while j < n:
            c = src[j]
            if c in "$@\"" and (c == '"' or (j + 1 < n and src[j + 1] in "\"$@")):
                j = string_end(j); continue
            if c == "'":
                j = char_end(j); continue
            if c in "({[":
                depth += 1
            elif c in ")]":
                depth -= 1
            elif c == "}":
                if depth == 0:
                    return j + 1
                depth -= 1
            j += 1
        raise ScanError("unterminated interpolation hole")

    def char_end(j):
        k = j + 1
        if src[k] == "\\":
            k += 2
            while k < n and src[k] != "'":
                k += 1
        else:
            k += 1
        if k >= n or src[k] != "'":
            raise ScanError("bad char literal (line %d)" % (src.count("\n", 0, j) + 1))
        return k + 1

    at_line_start = True
    while i < n:
        c = src[i]
        if c == "\n":
            line += 1; i += 1; at_line_start = True; continue
        if c in " \t\r\f﻿":
            i += 1; continue
        if c == "#" and at_line_start:   # preprocessor directive: the whole line
            while i < n and src[i] != "\n":
                i += 1
            continue
        at_line_start = False
        if src.startswith("//", i):
            while i < n and src[i] != "\n":
                i += 1
            continue
        if src.startswith("/*", i):
            k = src.find("*/", i + 2)
            if k < 0:
                raise ScanError("unterminated comment")
            line += src.count("\n", i, k); i = k + 2; continue
        if c == '"' or (c in "$@" and i + 1 < n and (src[i + 1] == '"' or (src[i + 1] in "$@" and i + 2 < n and src[i + 2] == '"'))):
            k = string_end(i)
            out.append(("str", src[i:k], line)); line += src.count("\n", i, k); i = k; continue
        if c == "'":
            k = char_end(i); out.append(("str", src[i:k], line)); i = k; continue
        if c.isalpha() or c == "_" or (c == "@" and i + 1 < n and (src[i + 1].isalpha() or src[i + 1] == "_")):
            k = i + 1
            while k < n and (src[k].isalnum() or src[k] == "_"):
                k += 1
            out.append(("id", src[i:k].lstrip("@"), line)); i = k; continue
        if c.isdigit():
            k = i + 1
            while k < n and (src[k].isalnum() or src[k] in "._"):
                k += 1
            out.append(("num", src[i:k], line)); i = k; continue
        out.append(("punct", c, line)); i += 1
    return out


def literal(text):
    """the value of a regular / verbatim string token (no interpolation), else None."""
    if text.startswith('@"'):
        return text[2:-1].replace('""', '"')
    if text.startswith('"'):
        return bytes(text[1:-1], "utf-8").decode("unicode_escape")
    return None


def attribute_info(toks):
    """(names, categories) of one attribute section's tokens (without the brackets): names are the attribute names
    (namespace / 'Attribute' suffix stripped); categories the string arguments of Category(...); '?' for a non-literal."""
    names, cats, depth, k = [], [], 0, 0
    while k < len(toks):
        kind, text, _ = toks[k]
        if kind == "punct" and text in "([":
            depth += 1
        elif kind == "punct" and text in ")]":
            depth -= 1
        elif depth == 0 and kind == "id" and (k + 1 >= len(toks) or toks[k + 1][1] != "."):
            name = text[:-len("Attribute")] if text.endswith("Attribute") and len(text) > 9 else text
            if not (k + 1 < len(toks) and toks[k + 1][1] == ":"):   # skip a target specifier ('method:', 'assembly:')
                names.append(name)
            if name == "Category" and k + 1 < len(toks) and toks[k + 1][1] == "(":
                arg = toks[k + 2] if k + 2 < len(toks) else None
                value = literal(arg[1]) if arg and arg[0] == "str" else None
                closes = k + 3 < len(toks) and toks[k + 3][1] == ")"
                cats.append(value if value is not None and closes else "?")
        k += 1
    return names, cats


def scan(src, path="<src>"):
    """Classes of one C# source: [{name, line, path, cats, attrs, abstract, bases, tests: [(method, line, cats, attrs)]}]
    (attrs = the attribute names, e.g. Test, Explicit, Timeout)."""
    toks = tokens(src)
    classes, stack = [], []          # stack entries: ("ns" | "type" | "other", class dict or None)
    pend_attrs = []                  # [(names, cats)] since the last declaration boundary
    pend_mods = []
    i, n = 0, len(toks)
    pending_type = None              # class dict waiting for its '{'
    member_paren_seen = False

    def scope():
        return stack[-1][0] if stack else "ns"

    def boundary(k):
        return k == 0 or toks[k - 1][1] in ("{", "}", ";", "]")

    while i < n:
        kind, text, line = toks[i]
        sc = scope()
        if kind == "punct" and text == "[" and sc in ("ns", "type") and boundary(i) and pending_type is None:
            depth, j = 1, i + 1
            while j < n and depth:
                if toks[j][1] == "[":
                    depth += 1
                elif toks[j][1] == "]":
                    depth -= 1
                j += 1
            if depth:
                raise ScanError("%s: unterminated attribute section (line %d)" % (path, line))
            pend_attrs.append(attribute_info(toks[i + 1:j - 1]))
            i = j; continue
        if kind == "id" and text == "namespace" and sc == "ns":
            j = i + 1
            while j < n and toks[j][1] != "{":
                j += 1
            stack.append(("ns", None)); i = j + 1; pend_attrs, pend_mods = [], []; continue
        if kind == "id" and text in TYPE_KEYWORDS and sc in ("ns", "type") and pending_type is None \
                and i + 1 < n and toks[i + 1][0] == "id" and not (i > 0 and toks[i - 1][1] in (".", "where", ":")):
            name = toks[i + 1][1]
            cats = [c for _, cs in pend_attrs for c in cs]
            bases = []
            j = i + 2
            if j < n and toks[j][1] == "<":   # generic parameters
                d = 0
                while j < n:
                    d += {"<": 1, ">": -1}.get(toks[j][1], 0); j += 1
                    if d == 0:
                        break
            if j < n and toks[j][1] == ":":
                j += 1
                if j < n and toks[j][0] == "id":
                    k = j
                    while k + 2 < n and toks[k + 1][1] == "." and toks[k + 2][0] == "id":
                        k += 2
                    bases.append(toks[k][1])
            pending_type = {"name": name, "line": toks[i + 1][2], "path": path, "cats": cats, "kind": text,
                            "attrs": [nm for nms, _ in pend_attrs for nm in nms],
                            "abstract": "abstract" in pend_mods or "static" in pend_mods, "bases": bases, "tests": []}
            if text == "class":
                classes.append(pending_type)
            pend_attrs, pend_mods = [], []
            i += 2; continue
        if kind == "punct" and text == "{":
            if pending_type is not None:
                stack.append(("type", pending_type)); pending_type = None
            else:
                if sc == "type" and pend_attrs and not member_paren_seen:
                    pend_attrs = []              # a property / event body: its attributes are not a method's
                stack.append(("other", None))
            pend_mods, member_paren_seen = [], False
            i += 1; continue
        if kind == "punct" and text == "}":
            if not stack:
                raise ScanError("%s: unbalanced '}' (line %d)" % (path, line))
            stack.pop(); pend_attrs, pend_mods, member_paren_seen = [], [], False
            i += 1; continue
        if kind == "punct" and text == ";" and sc in ("ns", "type"):
            if pending_type is not None and pending_type["kind"] == "record":
                pending_type = None
            pend_attrs, pend_mods, member_paren_seen = [], [], False
            i += 1; continue
        if sc == "type" and pending_type is None and kind == "punct" and text == "(" and not member_paren_seen:
            member_paren_seen = True
            names = [nm for nms, _ in pend_attrs for nm in nms]
            if any(a in TEST_ATTRS for a in names):
                j = i - 1
                if j >= 0 and toks[j][1] == ">":   # generic method
                    d = 0
                    while j >= 0:
                        d += {">": 1, "<": -1}.get(toks[j][1], 0); j -= 1
                        if d == 0:
                            break
                mname = toks[j][1] if j >= 0 else "?"
                stack_type = stack[-1][1]
                stack_type["tests"].append((mname, toks[j][2], [c for _, cs in pend_attrs for c in cs], names))
            pend_attrs = []
            i += 1; continue
        if kind == "id" and sc in ("ns", "type") and pending_type is None and not member_paren_seen:
            pend_mods.append(text)
        i += 1
    if stack or pending_type is not None:
        raise ScanError("%s: braces not balanced at the end of the file (%d open)" % (path, len(stack)))
    return classes


def violations(classes):
    """[(class dict, effective class categories, [(method, line, cats)])] — fixtures breaking the rule."""
    by_name = {}
    for c in classes:
        by_name.setdefault(c["name"], []).append(c)

    def resolve(c, seen=()):
        cats, tests = list(c["cats"]), list(c["tests"])
        for b in c["bases"]:
            for base in by_name.get(b, []):
                if base is c or base["name"] in seen:
                    continue
                bc, bt = resolve(base, seen + (c["name"],))
                cats += bc; tests += bt
        return cats, tests

    out = []
    for c in classes:
        if c["abstract"]:
            continue
        cats, tests = resolve(c)
        bad = [t for t in tests if any(rc in t[2] for rc in RULE_CATEGORIES)]
        if cats and bad:
            out.append((c, sorted(set(cats)), bad))
    return out


def scan_assets(root=ASSETS):
    classes, files = [], 0
    for p in sorted(root.rglob("*.cs")):
        text = p.read_text(encoding="utf-8-sig", errors="strict")
        if "NUnit.Framework" not in text:
            continue
        files += 1
        classes += scan(text, str(p.relative_to(root)))
    return classes, files


class ScannerTests(unittest.TestCase):
    def one(self, src):
        return {c["name"]: c for c in scan(src)}

    def test_braces_and_brackets_inside_strings_chars_comments_do_not_move_the_structure(self):
        src = r'''
using NUnit.Framework;
namespace N {
    // a comment with { and [Category("FPTP")]
    /* block { [Test] */
    [TestFixture, Category("WP042")]
    public sealed class A {
        private const string S = "{ [ \" }";
        private const string V = @"x "" { [";
        private static readonly char C = '{', D = '\'', E = '[';
        private string I(int n) => $"{n:F2}}} {{ {(n > 0 ? "}" : $"{n}")} [";
        [Test, Category("FPTP")] public void T1() { var a = new int[] { 1, 2 }; var b = a[0]; }
        [Test] public void T2() { }
    }
}'''
        a = self.one(src)["A"]
        self.assertEqual(["WP042"], a["cats"])
        self.assertEqual([("T1", ["FPTP"]), ("T2", [])], [(t[0], t[2]) for t in a["tests"]])

    def test_separate_attribute_sections_and_attribute_suffix(self):
        a = self.one('namespace N { [TestFixture][CategoryAttribute("X")] class A {\n'
                     '[NUnit.Framework.Test]\n[Category("Diagnostics"), Explicit] public System.Collections.IEnumerator T() { yield return null; } } }')["A"]
        self.assertEqual(["X"], a["cats"])
        self.assertEqual([("T", ["Diagnostics"])], [(t[0], t[2]) for t in a["tests"]])

    def test_nested_class_is_its_own_fixture(self):
        cs = self.one('namespace N { [Category("WP1")] class Outer { [Test] public void O() {}\n'
                      'class Inner { [Test, Category("FPTP")] public void I() {} } } }')
        self.assertEqual(["WP1"], cs["Outer"]["cats"])
        self.assertEqual([], cs["Inner"]["cats"])
        self.assertEqual([], violations(list(cs.values())))

    def test_property_and_field_attributes_are_not_tests(self):
        a = self.one('class A { [SerializeField] int f; [Obsolete] int P { get { return 1; } }\n'
                     '[Test] public void T() {} [Category("FPTP")] int Q => 2; }')["A"]
        self.assertEqual(["T"], [t[0] for t in a["tests"]])

    def test_generic_and_expression_bodied_methods(self):
        a = self.one('class A { [Test] public void G<T>() where T : class {}\n[UnityTest, Category("FPTP")] public System.Collections.IEnumerator E() => null; }')["A"]
        self.assertEqual([("G", []), ("E", ["FPTP"])], [(t[0], t[2]) for t in a["tests"]])

    def test_preprocessor_lines_are_skipped(self):
        a = self.one('class A {\n#if UNITY_EDITOR\n[Test] public void T() {}\n#endif\n}')["A"]
        self.assertEqual(["T"], [t[0] for t in a["tests"]])

    def test_attribute_names_are_kept(self):
        a = self.one('[Explicit("x")][Category("D")] class A { [UnityTest, Timeout(5)] public System.Collections.IEnumerator T() { yield break; } }')["A"]
        self.assertEqual(["Explicit", "Category"], a["attrs"])
        self.assertEqual(["UnityTest", "Timeout"], a["tests"][0][3])

    def test_unbalanced_file_fails_loudly(self):
        with self.assertRaises(ScanError):
            scan("class A { void F() { }")

    def test_the_rule(self):
        src = '''namespace N {
    [Category("WP042")] public sealed class Blind { [Test, Category("FPTP")] public void F() {} [Test] public void T() {} }
    [Category("WP042")] public sealed class BlindDiag { [Test, Explicit, Category("Diagnostics")] public void D() {} }
    public sealed class Uncategorized { [Test, Category("FPTP")] public void F() {} }
    [Category("WP042")][Category("FPTP")] public sealed class ClassLevel { [Test] public void F() {} }
    public sealed class PushedDown { [Test, Category("WP042")] public void T() {} [Test, Category("WP042"), Category("FPTP")] public void F() {} }
    [Category("WP042")] public sealed class OtherMethodCategory { [Test, Category("Slow")] public void S() {} }
    [Category("WP1")] public abstract class Base { [Test, Category("FPTP")] public void Inherited() {} }
    public sealed class Derived : Base { [Test] public void Own() {} }
    public abstract class PlainBase { [SetUp] public void Kur() {} }
    [Category("WP1")][Category("FPTP")] public sealed class SplitFp : PlainBase { [Test] public void F() {} }
}'''
        bad = {c["name"]: (cats, [t[0] for t in tests]) for c, cats, tests in violations(scan(src))}
        self.assertEqual({"Blind": (["WP042"], ["F"]), "BlindDiag": (["WP042"], ["D"]), "Derived": (["WP1"], ["Inherited"])}, bad)


class RepoTests(unittest.TestCase):
    def test_every_fixture_follows_the_rule(self):
        if not ASSETS.is_dir():
            self.skipTest("no Assets/ in this checkout (a tools-only sparse worktree)")
        classes, files = scan_assets()
        tests = sum(len(c["tests"]) for c in classes)
        self.assertGreater(files, 50, "the scan found too few NUnit sources — is the checkout complete?")
        self.assertGreater(tests, 1000, "the scan found too few tests (%d) — the lexer lost the structure?" % tests)
        bad = violations(classes)
        report = "\n".join("  %s:%d %s class categories %s ← method-level: %s" % (
            c["path"], c["line"], c["name"], cats, ", ".join("%s %s" % (t[0], [x for x in t[2] if x in RULE_CATEGORIES]) for t in tt))
            for c, cats, tt in bad)
        self.assertEqual([], [c["name"] for c, _, _ in bad], "\nRULE: " + RULE + "\n" + report)


if __name__ == "__main__":
    unittest.main(verbosity=2)
