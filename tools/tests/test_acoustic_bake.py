#!/usr/bin/env python3
"""WP-099 R3 (C-00 10.10) · tools/qa/acoustic_bake.py — the one "is the acoustic map baked from its json?" check
(integrate.sh gate 4c, main-ci's preflight). Throwaway files and git repos only. usage: python3 tools/tests/test_acoustic_bake.py"""
import hashlib
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS / "qa"))
import acoustic_bake as ab  # noqa: E402

J = b'{"schema": "acoustic-v6", "spaces": [{"id": "R1_MainHall", "enclosure": "Hall"}]}\n'


def h16(b):
    return hashlib.sha256(b).hexdigest()[:16]


def amap(h):
    return ("%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Name: DD_AcousticSpaces\n  _sourceHash: " + h
            + "\n  _worldScene: World_V6_Greybox\n").encode()


class VerdictTests(unittest.TestCase):
    def test_Pisirilmis_Gecer(self):
        self.assertEqual((True, "DD_AcousticSpaces.asset baked from acoustic-spaces-v6.json (%s)" % h16(J)),
                         ab.verdict(J, amap(h16(J))))

    def test_Bayat_IkiHashVeDuzeltmeyiYazar(self):
        ok, why = ab.verdict(J, amap("b1a15d1fc40d9fa5"))
        self.assertFalse(ok)
        self.assertEqual("DD_AcousticSpaces.asset _sourceHash b1a15d1fc40d9fa5 != sha256(acoustic-spaces-v6.json)[:16] %s - %s"
                         % (h16(J), ab.FIX), why)

    def test_JsonYok_PisirilecekBirSeyYok(self):
        self.assertTrue(ab.verdict(None, None)[0])
        self.assertTrue(ab.verdict(None, amap("x"))[0])

    def test_JsonVarHaritaYok_Kirmizi(self):
        ok, why = ab.verdict(J, None)
        self.assertFalse(ok); self.assertIn("without DD_AcousticSpaces.asset", why)

    def test_Utf8Bom_AtilarakHashlenir_CrlfKorunur(self):
        self.assertTrue(ab.verdict(b"\xef\xbb\xbf" + J, amap(h16(J)))[0])
        crlf = J.replace(b"\n", b"\r\n")
        self.assertTrue(ab.verdict(crlf, amap(h16(crlf)))[0])
        self.assertFalse(ab.verdict(crlf, amap(h16(J)))[0])

    def test_Utf16Bom_VeGecersizUtf8_KodlamaMesaji(self):
        """gate 4c gt NOTE-1: the C# would decode these differently; the reason names the encoding, not a re-bake."""
        for raw in (b"\xff\xfe" + J.decode().encode("utf-16-le"), b'{"x": "\xff"}\n'):
            ok, why = ab.verdict(raw, amap(h16(raw)))
            self.assertFalse(ok); self.assertIn("save it as UTF-8", why)

    def test_BosSourceHash_SonrakiSatiriOkumaz(self):
        """gate 4c gt NOTE-2: `_sourceHash:` empty — '(none)', never the next line's key."""
        m = b"MonoBehaviour:\n  _sourceHash: \n  _worldScene: World_V6_Greybox\n"
        self.assertEqual("(none)", ab.map_hash(m))
        self.assertIn("_sourceHash (none) !=", ab.verdict(J, m)[1])


class GitModeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-acoustic-")
        self.repo = Path(self.tmp.name)
        self.env = dict(os.environ, GIT_CONFIG_NOSYSTEM="1", GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t",
                        GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t", HOME=self.tmp.name)
        self.git("init", "-q", "-b", "main")

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *a):
        return subprocess.run(["git", "-C", str(self.repo), *a], env=self.env, check=True, capture_output=True, text=True).stdout

    def put(self, rel, data):
        f = self.repo / rel; f.parent.mkdir(parents=True, exist_ok=True); f.write_bytes(data)

    def cli(self, *a):
        r = subprocess.run([sys.executable, str(TOOLS / "qa" / "acoustic_bake.py"), *a], env=self.env, capture_output=True,
                           text=True)
        return r.returncode, r.stdout + r.stderr

    def test_Tree_CommitBazinda(self):
        self.put("README", b"x\n"); self.git("add", "."); self.git("commit", "-q", "-m", "no pair")
        self.assertEqual(0, self.cli("tree", str(self.repo), "HEAD")[0])            # no json at all
        self.put(ab.JSON_PATH, J); self.put(ab.MAP_PATH, amap("0" * 16)); self.git("add", "."); self.git("commit", "-q", "-m", "stale")
        rc, out = self.cli("tree", str(self.repo), "HEAD")
        self.assertEqual(1, rc, out); self.assertIn("_sourceHash 0000000000000000 !=", out)
        self.put(ab.MAP_PATH, amap(h16(J))); self.git("commit", "-qam", "baked")
        self.assertEqual(0, self.cli("tree", str(self.repo), "HEAD")[0])
        self.assertEqual(1, self.cli("tree", str(self.repo), "HEAD~1")[0])

    def test_Index_SahnelenmisCifti(self):
        self.put(ab.JSON_PATH, J); self.put(ab.MAP_PATH, amap(h16(J))); self.git("add", "."); self.git("commit", "-q", "-m", "baked")
        self.assertEqual(0, self.cli("index", str(self.repo))[0])
        self.put(ab.JSON_PATH, J + b" "); self.git("add", ab.JSON_PATH)                 # staged json, the map not re-baked
        self.assertEqual(1, self.cli("index", str(self.repo))[0])

    def test_Kullanim_VeBilinmeyenRev_Cikis2(self):
        self.assertEqual(2, self.cli()[0])
        self.assertEqual(2, self.cli("tree", str(self.repo), "nope")[0])


class ContractTests(unittest.TestCase):
    """gate 4c gt NOTE-4: the paths and the hash method are AudioCatalogBuilder's — a change there must show up here."""

    def builder_source(self):
        rel = "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Editor/AudioCatalogBuilder.cs"
        f = TOOLS.parent / rel
        if f.is_file():
            return f.read_text(encoding="utf-8")
        r = subprocess.run(["git", "-C", str(TOOLS.parent), "show", "HEAD:" + rel], capture_output=True, text=True)
        if r.returncode != 0:
            self.skipTest("AudioCatalogBuilder.cs not readable here")
        return r.stdout

    def test_YollarVeHashYontemi_AudioCatalogBuilderIleAyni(self):
        cs = self.builder_source()
        src = re.search(r'AcousticSourcePath = "([^"]+)"', cs).group(1)
        dst = re.search(r'AcousticMapPath = "([^"]+)"', cs).group(1)
        self.assertEqual(ab.JSON_PATH, "prototype/DeathsDemolition_PlayableConcept/" + src)
        self.assertEqual(ab.MAP_PATH, "prototype/DeathsDemolition_PlayableConcept/" + dst)
        body = cs[cs.index("public static string BuildAcousticSpaces()"):]
        for needle in ("File.ReadAllText(AcousticSourcePath)", "SHA256.Create()", "Encoding.UTF8.GetBytes(text)",
                       ".Substring(0, 16).ToLowerInvariant()", "existing.SourceHash == hash"):
            self.assertIn(needle, body[:1500], needle)


if __name__ == "__main__":
    unittest.main(verbosity=2)
