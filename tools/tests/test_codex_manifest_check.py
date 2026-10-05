#!/usr/bin/env python3
"""WP-089 (C-00 05.10) · tools/codex_manifest_check.py on synthetic packages: real binary FBX 7.4 (GlobalSettings),
PNG and 24-bit WAV files written here; one good package PASSes with no warning, each rule has a failing variant.
usage: python3 tools/tests/test_codex_manifest_check.py"""
import hashlib, json, math, shutil, struct, subprocess, sys, tempfile, unittest, zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import codex_manifest_check as cmc  # noqa: E402

V = "v001"
PKG = "AP-024"


# --- file writers -----------------------------------------------------------------------------------------------------
def fbx_bytes(version=7400, up_axis=1):
    def props(ps):
        out = b""
        for p in ps:
            if isinstance(p, str):
                b = p.encode()
                out += b"S" + struct.pack("<I", len(b)) + b
            elif isinstance(p, float):
                out += b"D" + struct.pack("<d", p)
            else:
                out += b"I" + struct.pack("<i", p)
        return out

    def node(name, ps, children, start):
        pb, nb = props(ps), name.encode()
        head = 13 + len(nb)
        kids = b""
        for c in children:
            kids += node(*c, start + head + len(pb) + len(kids))
        if children:
            kids += b"\x00" * 13
        end = start + head + len(pb) + len(kids)
        return struct.pack("<III", end, len(ps), len(pb)) + bytes([len(nb)]) + nb + pb + kids

    gs = ("GlobalSettings", [], [("Version", [1000], []),
                                 ("Properties70", [], [("P", ["UpAxis", "int", "Integer", "", up_axis], []),
                                                       ("P", ["UnitScaleFactor", "double", "Number", "", 100.0], [])])])
    body = node(*gs, 27)
    return cmc.FBX_MAGIC + struct.pack("<I", version) + body + b"\x00" * 13


def png_bytes(w, h, ctype=2):
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[ctype]
    raw = (b"\x00" + b"\x40" * (w * channels)) * h

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ctype, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 1)) + chunk(b"IEND", b""))


def wav_bytes(rate=48000, bits=24, seconds=0.1, peak_db=-6.0, lead_ms=0.0, ch=2):
    amp = 10 ** (peak_db / 20.0)
    lead = int(rate * lead_ms / 1000)
    n = int(rate * seconds)
    width = bits // 8
    frames = bytearray()
    full = (1 << (bits - 1)) - 1
    for i in range(lead + n):
        v = 0.0 if i < lead else amp * math.sin(2 * math.pi * 997 * (i - lead) / rate + 0.3)
        s = int(round(v * full))
        frames += s.to_bytes(width, "little", signed=True) * ch
    fmt = struct.pack("<HHIIHH", 1, ch, rate, rate * ch * width, ch * width, bits)
    data = b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", len(frames)) + bytes(frames)
    return b"RIFF" + struct.pack("<I", 4 + len(data)) + b"WAVE" + data


# --- package builder --------------------------------------------------------------------------------------------------
class Pkg:
    def __init__(self, root):
        self.dir = Path(root) / PKG
        self.items = []
        (self.dir / "exports" / V).mkdir(parents=True)
        (self.dir / "manifest").mkdir()
        (self.dir / "03-sources").mkdir()
        (self.dir / "04-tools").mkdir()
        (self.dir / "04-tools" / "build.py").write_text("# fixture\n")
        (self.dir / "validation").mkdir()
        (self.dir / "validation" / "audio-measurements.csv").write_text("file,lufs\n")
        (self.dir / "03-sources" / "LICENSES.csv").write_text(
            "dosya,kaynak url,lisans,tarih,kullanım\nimpact.wav,https://example.org/a,CC0,2026-10-05,layer\n")
        self.manifest = {"package": PKG, "version": V, "date": "2026-10-05", "producer": "codex/gpt-5",
                         "licenses": "03-sources/LICENSES.csv", "notes": "fixture"}

    def add(self, rel, data, kind, role="r", listed=True, **extra):
        p = self.dir / "exports" / V / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        if listed:
            item = {"path": "exports/%s/%s" % (V, rel), "sha256": hashlib.sha256(data).hexdigest(), "type": kind,
                    "role": role, "specs": {"fixture": True}, "source": "04-tools/build.py"}
            item.update(extra)
            self.items.append(item)
        return p

    def good(self):
        for lod in (0, 1, 2):
            self.add("Hero/Hero_Body_LOD%d.fbx" % lod, fbx_bytes(), "fbx")
        self.add("Hero/COL_Hero_Body.fbx", fbx_bytes(), "fbx")
        self.add("Anim/Hero_Walk.fbx", fbx_bytes(), "anim")
        (self.dir / "exports" / V / "anim-events.json").write_text('{"Hero_Walk": [{"frame": 3, "event": "foot_l"}]}')
        self.add("Tex/Hero_Body_BaseColor.png", png_bytes(2048, 2048, 2), "texture")
        self.add("Tex/Hero_Body_ORM.png", png_bytes(2048, 2048, 2), "texture")
        self.add("Audio/Weapon_Ar1_Body_01.wav", wav_bytes(), "audio")
        self.add("UI/icon_reload.png", png_bytes(128, 128, 6), "ui")
        return self

    def write(self, sums=True, manifest=None):
        m = dict(self.manifest, items=self.items) if manifest is None else manifest
        (self.dir / "manifest" / ("AP024-manifest-%s.json" % V)).write_text(json.dumps(m, indent=1))
        if sums:
            lines = ["%s  %s" % (it["sha256"], it["path"]) for it in self.items]
            (self.dir / "manifest" / ("SHA256SUMS-" + V)).write_text("\n".join(lines) + "\n")
        return self.dir


class CodexManifestCheckTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-codex-check-")
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def pkg(self):
        return Pkg(self.root).good()

    def run_check(self, pkg_dir, *args):
        rep, version = cmc.check(pkg_dir, *args)
        return rep

    def assert_error(self, rep, needle):
        self.assertTrue(any(needle in e["message"] or needle in e["path"] for e in rep.errors),
                        "no error with %r in %s" % (needle, rep.errors))

    def test_IyiPaket_PASS_UyariYok(self):
        rep = self.run_check(self.pkg().write())
        self.assertEqual([], rep.errors)
        self.assertEqual([], rep.warnings)

    def test_Cli_Cikis0_Json_Strict_64(self):
        d = self.pkg().write()
        r = subprocess.run([sys.executable, str(TOOLS / "codex_manifest_check.py"), str(d), "--json"], capture_output=True, text=True)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual("PASS", json.loads(r.stdout)["result"])
        self.assertEqual(64, cmc.main([str(self.root / "nope")]))
        self.assertEqual(64, cmc.main([str(d), "--version", "1"]))
        p = Pkg(self.root / "w").good()
        p.add("UI/hud_frame.png", png_bytes(64, 64, 6), "ui")          # a warning only
        d2 = p.write()
        self.assertEqual(0, cmc.main([str(d2)]))
        self.assertEqual(1, cmc.main([str(d2), "--strict"]))

    def test_SonSurumVarsayilan(self):
        p = self.pkg(); p.write()
        (p.dir / "exports" / "v000").mkdir()
        self.assertEqual(V, cmc.check(p.dir)[1])

    # --- manifest + SHA256SUMS ----------------------------------------------------------------------------------------
    def test_ManifestAlanlari(self):
        p = self.pkg()
        for key, bad, needle in (("package", "AP-099", "package"), ("version", "v002", "version"),
                                 ("date", "05.10.2026", "date"), ("producer", "", "producer")):
            with self.subTest(key=key):
                m = dict(p.manifest, items=p.items); m[key] = bad
                self.assert_error(self.run_check(p.write(manifest=m)), needle)
        self.assert_error(self.run_check(p.write(manifest=dict(p.manifest, items=[]))), "non-empty")
        (p.dir / "manifest" / ("AP024-manifest-%s.json" % V)).write_text("{not json")
        self.assert_error(self.run_check(p.dir), "not valid JSON")

    def test_Ogeler_ShaYolTipRol(self):
        p = self.pkg()
        p.items[0]["sha256"] = "0" * 64
        p.items[1]["path"] = "exports/%s/../escape.fbx" % V
        p.items[2]["type"] = "mesh"
        p.items[3]["role"] = ""
        p.items.append(dict(p.items[4]))
        rep = self.run_check(p.write())
        for needle in ("sha256", "must be relative", "type 'mesh'", "role missing", "listed twice"):
            self.assert_error(rep, needle)

    def test_ListelenmemisDosya_Hata(self):
        p = self.pkg()
        p.add("Tex/Stray_Mask.png", png_bytes(2048, 2048, 0), "texture", listed=False)
        self.assert_error(self.run_check(p.write()), "not in the manifest")

    def test_Sha256Sums_YokVeyaFarkli(self):
        p = self.pkg()
        self.assert_error(self.run_check(p.write(sums=False)), "SHA256SUMS missing")
        d = p.write()
        f = d / "manifest" / ("SHA256SUMS-" + V)
        lines = f.read_text().splitlines()
        f.write_text("\n".join([("f" * 64) + lines[0][64:]] + lines[2:]) + "\n")
        rep = self.run_check(d)
        self.assert_error(rep, "hash differs")
        self.assert_error(rep, "not in SHA256SUMS")

    # --- FBX / anim ---------------------------------------------------------------------------------------------------
    def test_Fbx_IkiliSurumAdLod(self):
        p = Pkg(self.root).good()
        p.items = [it for it in p.items if "LOD2" not in it["path"]]
        (p.dir / "exports" / V / "Hero" / "Hero_Body_LOD2.fbx").unlink()
        p.add("Prop/Crate_Lid_LOD0.fbx", b"; FBX 7.4.0 project file (ASCII)\n", "fbx")
        p.add("Prop/Barrel_LOD0.fbx", fbx_bytes(), "fbx")
        p.add("Prop/Lamp_Top_LOD0.fbx", fbx_bytes(version=7500), "fbx")
        p.add("Prop/Lamp_Top_LOD1.fbx", fbx_bytes(up_axis=2), "fbx")
        rep = self.run_check(p.write())
        for needle in ("not a binary FBX", "<Asset>_<Part>_LOD<n>", "need 7400", "LOD set incomplete"):
            self.assert_error(rep, needle)
        self.assertTrue(any("UpAxis=2" in w["message"] for w in rep.warnings), rep.warnings)

    def test_Fbx_GlobalSettingsOkunur(self):
        f = self.root / "x.fbx"; f.write_bytes(fbx_bytes(up_axis=1))
        self.assertEqual({"UpAxis": 1, "UnitScaleFactor": 100.0}, cmc.fbx_global_settings(f))

    def test_Anim_OlayDosyasiZorunlu(self):
        p = self.pkg()
        (p.dir / "exports" / V / "anim-events.json").unlink()
        self.assert_error(self.run_check(p.write()), "anim-events.json")
        (p.dir / "exports" / V / "anim-events.json").write_text("{broken")
        self.assert_error(self.run_check(p.dir), "not valid JSON")

    # --- texture / audio / ui -----------------------------------------------------------------------------------------
    def test_Doku_SonekBoyutKanal(self):
        p = self.pkg()
        p.add("Tex/Hero_Body_Albedo.png", png_bytes(2048, 2048, 2), "texture")
        p.add("Tex/Hero_Arm_BaseColor.png", png_bytes(1000, 1000, 2), "texture")
        p.add("Tex/Hero_Leg_Normal.png", png_bytes(2048, 2048, 0), "texture")
        p.add("Tex/Hero_Leg_Mask.png", png_bytes(1024, 1024, 0), "texture")
        rep = self.run_check(p.write())
        for needle in ("suffix must be", "not power-of-two", "must be RGB/RGBA"):
            self.assert_error(rep, needle)
        self.assertTrue(any("1024x1024" in w["message"] for w in rep.warnings))

    def test_Ses_HizBitTepeSessizlikAd(self):
        p = self.pkg()
        p.add("Audio/Weapon_Ar1_Tail_02.wav", wav_bytes(rate=44100), "audio")
        p.add("Audio/Weapon_Ar1_Mech_03.wav", wav_bytes(bits=16), "audio")
        p.add("Audio/Weapon_Ar1_Hot_04.wav", wav_bytes(peak_db=-0.2), "audio")
        p.add("Audio/Weapon_Ar1_Late_05.wav", wav_bytes(lead_ms=50), "audio")
        p.add("Audio/ar1-body.wav", wav_bytes(), "audio")
        rep = self.run_check(p.write())
        for needle in ("need 48000", "need 24", "dBTP", "silence lead", "<Category>_<Weapon>"):
            self.assert_error(rep, needle)

    def test_Ses_GercekTepeOrneklerArasi(self):
        # a full-scale tone at fs/4 sampled at ±45°: sample peak −3 dBFS, true peak 0 dBTP
        import numpy as np
        x = np.sin(2 * np.pi * 0.25 * np.arange(4800) + np.pi / 4).reshape(-1, 1)
        self.assertLess(20 * np.log10(np.max(np.abs(x))), -2.9)
        self.assertGreater(cmc.true_peak_dbtp(x), -0.2)

    def test_Ui_SeffaflikIkonBoyut(self):
        p = self.pkg()
        p.add("UI/icon_ammo.png", png_bytes(128, 128, 2), "ui")
        p.add("UI/icon_grenade.png", png_bytes(100, 100, 6), "ui")
        p.add("UI/icon_Bad-Name.png", png_bytes(64, 64, 6), "ui")
        rep = self.run_check(p.write())
        for needle in ("transparent", "need square", "lowercase icon_"):
            self.assert_error(rep, needle)

    # --- licenses / ledger --------------------------------------------------------------------------------------------
    def test_Lisans_YokKolonBelirsiz(self):
        p = self.pkg()
        lic = p.dir / "03-sources" / "LICENSES.csv"
        lic.write_text("dosya,kaynak url,lisans,tarih,kullanım\nx.wav,https://e.org,belirsiz,2026-10-05,layer\n")
        self.assert_error(self.run_check(p.write()), "not usable")
        lic.write_text("dosya,lisans\nx.wav,CC0\n")
        self.assert_error(self.run_check(p.dir), "columns missing")
        lic.unlink()
        self.assert_error(self.run_check(p.dir), "LICENSES.csv missing")

    def test_Higgsfield_DefterZorunlu(self):
        p = self.pkg()
        (p.dir / "03-higgsfield").mkdir()
        self.assert_error(self.run_check(p.write()), "ledger is missing")
        (p.dir / "03-higgsfield" / "ledger.csv").write_text("tarih,model,adet,kredi,çıktı\n2026-10-05,img,4,0,02-concept/o1.png\n")
        self.assertEqual([], self.run_check(p.dir).errors)
        (p.dir / "03-higgsfield" / "ledger.csv").write_text("tarih,model\n")
        self.assert_error(self.run_check(p.dir), "columns missing")


if __name__ == "__main__":
    unittest.main(verbosity=2)
