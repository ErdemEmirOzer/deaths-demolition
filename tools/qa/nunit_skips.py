"""C-00 30.09 Y6 (K/c-00/TANI-TESTI-POLITIKASI-2026-09-30.md): the one skip rule for every NUnit gate.

A skipped test case is allowed only when all three hold: result Skipped, Explicit (NUnit `label` or `runstate`), and
Category "Diagnostics" on the case or an enclosing suite. An `Assert.Ignore` opt-in (label Ignored) is an unexpected
skip → red. No fullname lists and no '!' filters; the only name-based exception is the caller's visible legacy set.
"""
import xml.etree.ElementTree as ET

DIAGNOSTICS_CATEGORY = "Diagnostics"


def _categories(node):
    props = node.find("properties")
    if props is None:
        return set()
    return {p.get("value") for p in props.findall("property") if p.get("name") == "Category" and p.get("value")}


def skipped_cases_from_root(root):
    """[{name, label, runstate, categories}] for every Skipped test case; categories include the enclosing suites'."""
    found = []

    def walk(node, inherited):
        cats = inherited | _categories(node)
        if node.tag == "test-case":
            if (node.get("result") or "").lower() == "skipped":
                found.append({"name": node.get("fullname") or node.get("name") or "unknown", "label": node.get("label") or "",
                              "runstate": node.get("runstate") or "", "categories": sorted(cats)})
            return
        for child in node:
            walk(child, cats)

    walk(root, set())
    return found


def skipped_cases(path):
    try:
        return skipped_cases_from_root(ET.parse(path).getroot())
    except Exception:
        return []


def is_diagnostic_explicit(case):
    """Skipped ∧ Explicit ∧ Diagnostics — all three."""
    explicit = case["label"].lower() == "explicit" or case["runstate"].lower() == "explicit"
    return explicit and DIAGNOSTICS_CATEGORY in case["categories"]


def classify(cases, legacy=frozenset()):
    """(allowedDiagnostic, legacyExceptions, unexpected) sorted fullname lists; unexpected names carry their label."""
    allowed, legacy_hits, unexpected = [], [], []
    for case in cases:
        if is_diagnostic_explicit(case):
            allowed.append(case["name"])
        elif case["name"] in legacy:
            legacy_hits.append(case["name"])
        else:
            unexpected.append(case["name"] + " [" + (case["label"] or case["runstate"] or "skipped") + "]")
    return sorted(allowed), sorted(legacy_hits), sorted(unexpected)


def classify_skips(path, legacy=frozenset()):
    return classify(skipped_cases(path), legacy)
