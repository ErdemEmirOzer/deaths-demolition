#!/usr/bin/env python3
"""WP-112 (C-00 12.10, the user: no developer text or prototype trace in the release build) · source gates, no Unity: the code
below must sit in a branch a release player (no DEVELOPMENT_BUILD, no UNITY_EDITOR, no DD_PERF_PROBE) never compiles.
The release player compile itself: tools/compile-check.sh <ref> --player release. usage: python3 tools/tests/test_release_presentable.py"""
import re, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype"
RELEASE = {"DEVELOPMENT_BUILD": False, "UNITY_EDITOR": False, "DD_PERF_PROBE": False, "ENABLE_INPUT_SYSTEM": True}


def active(cond):
    expr = re.sub(r"[A-Z_][A-Z0-9_]*", lambda m: str(RELEASE.get(m.group(0), False)), cond)
    return eval(expr.replace("||", " or ").replace("&&", " and ").replace("!", " not "), {})


def release_lines(path):
    """The lines a release compile keeps (#if / #elif / #else / #endif followed; no #define in these files)."""
    keep, stack = [], []   # stack of [this branch active, any earlier branch taken]
    for line in path.read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if s.startswith("#if "):
            on = all(f[0] for f in stack) and active(s[4:]); stack.append([on, on]); continue
        if s.startswith("#elif "):
            on = not stack[-1][1] and active(s[6:]); stack[-1] = [on, stack[-1][1] or on]; continue
        if s.startswith("#else"):
            stack[-1] = [not stack[-1][1], True]; continue
        if s.startswith("#endif"):
            stack.pop(); continue
        if all(f[0] for f in stack):
            keep.append(line)
    return "\n".join(keep)


class ReleasePresentableTests(unittest.TestCase):
    def test_F1Paneli_ReleaseteAcilmaz(self):
        # S-09's f3123ab9 (WP-112): the panel opens only when Debug.isDebugBuild (a runtime gate, kept as it is on main) -
        # Awake starts it hidden and Update hides it before the F1 / Gamepad Select toggle is read
        src = (ROOT / "Core/Debug/PrototypeDebugPanel.cs").read_text(encoding="utf-8")
        self.assertIn("_visible = _startVisible && UnityEngine.Debug.isDebugBuild;", src)
        upd = src[src.index("private void Update()"):]
        self.assertLess(upd.index("!UnityEngine.Debug.isDebugBuild"), upd.index("TogglePressedThisFrame()"))

    def test_KayitliDirectToRun_ReleaseteOkunmazYazilmaz(self):
        rel = release_lines(ROOT / "Core/Boot/PrototypeBootFlags.cs")
        self.assertNotIn("PlayerPrefs.GetInt(DirectToRunPrefKey", rel)
        self.assertNotIn("PlayerPrefs.SetInt(DirectToRunPrefKey", rel)
        self.assertNotIn("config.System.DirectToRun", rel)
        self.assertIn("HasCommandLineFlag(DirectToRunArg)", rel, "the process-local smoke flag stays (tools/smoke.sh)")

    def test_DunyaBayraklari_ReleaseteYok_ProbedaVar(self):
        src = ROOT / "World/WorldSource.cs"
        rel = release_lines(src)
        for gone in ("Arg(CommandLineFlag)", "Arg(SceneFlag)", "GetEnvironmentVariable(EnvironmentVariable)",
                     "GetEnvironmentVariable(SceneEnvironmentVariable)"):
            self.assertNotIn(gone, rel)
        RELEASE["DD_PERF_PROBE"] = True
        try:
            self.assertIn("Arg(CommandLineFlag)", release_lines(src), "release-probe measurements keep the world flags")
        finally:
            RELEASE["DD_PERF_PROBE"] = False


if __name__ == "__main__":
    unittest.main(verbosity=2)
