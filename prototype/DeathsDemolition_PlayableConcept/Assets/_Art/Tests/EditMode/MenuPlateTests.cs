using System;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 6 · The engine-captured MainMenu / loading backdrop (Look B, World_AP005, MenuPlateCaptureTests): three
    /// 2560×1440 layers + the lantern-breath mask + the manifest under Resources/MenuPlates, imported by their own rules
    /// (ArtImportContract.ApplyMenuPlate), and T10 (AD MUST 5): TextSecondary over the left third under the SurfaceBase scrim
    /// α 0.55 ≥ 4.5:1 on 99 % of it — with the lantern breath at its peak, at rest and at both parallax extremes (4 / 8 / 12 px);
    /// the breath mask never reaches into that third.
    /// </summary>
    public sealed class MenuPlateTests
    {
        [Serializable]
        private sealed class Layer { public string resource; public string band; public bool alpha; public int parallax_px; }

        [Serializable]
        private sealed class Scrim { public string token; public float alpha; }

        [Serializable]
        private sealed class T10 { public float threshold; public float p1; public float below_pct; }

        [Serializable]
        private sealed class Mask { public string resource; public float scale; public float period_s; }

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public int[] size;
            public Layer[] layers;
            public Mask lantern_mask;
            public Scrim scrim;
            public T10 t10;
        }

        private static string PathOf(string name) => ArtImportContract.MenuPlateRoot + "/" + name + ".png";

        private static Manifest Read()
        {
            string path = ArtImportContract.MenuPlateRoot + "/menu_plates.json";
            Assert.IsTrue(File.Exists(path), "manifest yok: " + path);
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }

        private static Texture2D Source(string name)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(PathOf(name))), name + " PNG okunamadı");
            return t;
        }

        [Test]
        public void MenuPlakalari_UcKatmanMaskeVeManifest_KendiIceAktarimKurallariyla()
        {
            Manifest m = Read();
            Assert.AreEqual("dd.menu-plates/1", m.schema);
            CollectionAssert.AreEqual(new[] { 2560, 1440 }, m.size);
            Assert.AreEqual(3, m.layers.Length, "uzak / orta / yakın");
            CollectionAssert.AreEqual(new[] { 4, 8, 12 }, Array.ConvertAll(m.layers, l => l.parallax_px), "parallax 4/8/12 px (§6.1)");
            Assert.AreEqual(MenuPlateMetrics.ScrimAlpha, m.scrim.alpha, 1e-4f, "scrim α 0,55 sabit (AD MUST 5)");
            Assert.AreEqual("SurfaceBase", m.scrim.token);
            Assert.AreEqual(2.4f, m.lantern_mask.period_s, 1e-4f);

            foreach (string name in new[] { ArtImportContract.MenuPlateFar, ArtImportContract.MenuPlateMid, ArtImportContract.MenuPlateNear, ArtImportContract.MenuPlateLanternMask })
            {
                string path = PathOf(name);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path + " içe aktarılmamış");
                Assert.IsFalse(ArtImportContract.ApplyMenuPlate(importer, path), path + ": içe aktarma kuralları uygulanmamış (yeniden içe aktar)");
                TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(ArtImportContract.StandalonePlatform);
                Assert.AreEqual(ArtImportContract.MenuPlateFormat(name), st.format, path);
                Assert.IsFalse(importer.streamingMipmaps, path + ": UI mip akışı yok");
                var tex = Resources.Load<Texture2D>("MenuPlates/" + name);
                Assert.IsNotNull(tex, "Resources.Load(MenuPlates/" + name + ")");
                bool mask = name == ArtImportContract.MenuPlateLanternMask;
                Assert.AreEqual(mask ? 1280 : 2560, tex.width, path + " genişlik (küçültülmez)");
                Assert.AreEqual(mask ? 720 : 1440, tex.height, path + " yükseklik");
            }

            // The mid / near layers are cut-outs, not full frames and not empty.
            foreach (string name in new[] { ArtImportContract.MenuPlateMid, ArtImportContract.MenuPlateNear })
            {
                Color32[] px = Source(name).GetPixels32();
                int covered = 0;
                foreach (Color32 c in px) if (c.a > 127) covered++;
                float share = (float)covered / px.Length;
                Assert.That(share, Is.InRange(0.02f, 0.85f), name + " kaplama " + share.ToString("P1"));
            }
        }

        [Test]
        public void MenuPlakasi_T10_SolUcteBir_TextSecondary45_ScrimAltinda_FenerNefesiTepedeVeParallaxUclarinda()
        {
            Manifest m = Read();
            Texture2D far = Source(ArtImportContract.MenuPlateFar), mid = Source(ArtImportContract.MenuPlateMid), near = Source(ArtImportContract.MenuPlateNear);
            Texture2D mask = Source(ArtImportContract.MenuPlateLanternMask);
            Color32[] mp = mask.GetPixels32();
            int clip = MenuPlateMetrics.GlowClipX * mask.width / 2560, leaked = 0;
            for (int y = 0; y < mask.height; y++)
            for (int x = 0; x < clip; x++) if (mp[y * mask.width + x].r > 0) leaked++;
            Assert.AreEqual(0, leaked, "fener maskesi metin-güvenli üçte bire taşıyor (AD)");
            foreach (int k in new[] { 0, -1, 1 })
            {
                Texture2D rest = MenuPlateMetrics.Composite(far, mid, near, 4 * k, 8 * k, 12 * k);
                Texture2D comp = MenuPlateMetrics.ApplyGlow(rest, mask, 1f);
                UnityEngine.Object.DestroyImmediate(rest);
                (float p1, float min, float below) = MenuPlateMetrics.TextSafe(comp);
                Debug.Log("[DD-TEST] wp068.t10 offset=" + k + " p1=" + p1.ToString("F2") + " min=" + min.ToString("F2") + " below=" + below.ToString("F2") + "%");
                Assert.GreaterOrEqual(p1, MenuPlateMetrics.Threshold, "T10 (parallax " + k + "): sol üçte birin %99'u TextSecondary ≥ 4,5:1 değil (p1 " + p1.ToString("F2") + ", altında %" + below.ToString("F2") + ")");
                UnityEngine.Object.DestroyImmediate(comp);
            }

            Assert.GreaterOrEqual(m.t10.p1, MenuPlateMetrics.Threshold, "manifest T10 kaydı");
        }
    }
}
