using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-075 C2 · The cloud shadow (direction Ek A §4, gate K7): the AP-023 cookie's statistics on the ground
    /// (240 m tile), its import, and the projection contract — the published rows equal URP 14's own main-light cookie
    /// mapping (LightCookieManager.SetupMainLight), the tile is 240 m on the ground along both axes, a point in the air
    /// reads the ground at the end of its sun ray, and the drift moves the pattern toward 52°.
    /// </summary>
    public sealed class CloudShadowTests
    {
        private const float Tile = WorldAtmosphere.CloudShadow.TileMeters;
        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            CloudShadowDriver.ForcedPhase = null;
            CloudShadowDriver.ClearGlobals();
            foreach (Object o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        [Test]
        public void Cookie_K7_AydinlikPay_KenarGecisi_EnKucukYama14m_Import()
        {
            var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _made.Add(png);
            Assert.IsTrue(png.LoadImage(File.ReadAllBytes(LookV3Builder.CloudCookiePath)), "cookie PNG");
            Assert.AreEqual(1024, png.width);
            Assert.AreEqual(1024, png.height);
            int n = png.width;
            Color32[] px = png.GetPixels32();
            var v = new float[n * n];
            double sum = 0;
            float min = 1f;
            int lit = 0;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = px[i].r / 255f;
                sum += v[i];
                min = Mathf.Min(min, v[i]);
                if (v[i] >= 0.8f) lit++;
            }

            float mean = (float)(sum / v.Length), litShare = lit / (float)v.Length, mPerPx = Tile / n;

            // Edge transition: across the 0.40 → 0.80 ramp, width = 0.40 / |gradient| on the mid-ramp pixels (torus).
            var widths = new List<float>();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float c = v[y * n + x];
                if (c <= 0.45f || c >= 0.75f) continue;
                float gx = (v[y * n + (x + 1) % n] - v[y * n + (x + n - 1) % n]) / (2f * mPerPx);
                float gy = (v[(y + 1) % n * n + x] - v[(y + n - 1) % n * n + x]) / (2f * mPerPx);
                float g = Mathf.Sqrt(gx * gx + gy * gy);
                if (g > 1e-6f) widths.Add(0.40f / g);
            }

            widths.Sort();
            float edge = widths[widths.Count / 2];
            float minSun = SmallestPatch(v, n, true) * mPerPx, minShadow = SmallestPatch(v, n, false) * mPerPx;
            Debug.Log("[DD-TEST] wp075.c2.k7 mean " + mean.ToString("F3") + " min " + min.ToString("F2") + " lit " + litShare.ToString("P1") + " edge " + edge.ToString("F1") + " m, smallest sun " + minSun.ToString("F1") + " m / shadow " + minShadow.ToString("F1") + " m");
            Assert.That(litShare, Is.InRange(0.35f, 0.45f), "aydınlık pay (≥ 0,8) %35–45");
            Assert.That(mean, Is.InRange(0.58f, 0.68f), "ortalama ≈ 0,62");
            Assert.That(min, Is.InRange(0.38f, 0.42f), "bulut gölgesi 0,40");
            Assert.That(edge, Is.InRange(5f, 9f), "kenar geçişi 5–9 m (medyan)");
            Assert.GreaterOrEqual(minSun, 14f, "en küçük güneş yaması ≥ 14 m");
            Assert.GreaterOrEqual(minShadow, 14f, "en küçük gölge yaması ≥ 14 m");

            var importer = (TextureImporter)AssetImporter.GetAtPath(LookV3Builder.CloudCookiePath);
            Assert.AreEqual(TextureImporterType.SingleChannel, importer.textureType);
            Assert.IsFalse(importer.sRGBTexture, "doğrusal çarpan (sRGB kapalı)");
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapMode);
            Assert.IsTrue(importer.mipmapEnabled);
            Assert.IsFalse(importer.streamingMipmaps, "cookie'yi değerlendiren renderer yok");
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            Assert.IsTrue(standalone.overridden);
            Assert.AreEqual(TextureImporterFormat.BC4, standalone.format);
            var tex = Resources.Load<Texture2D>(WorldAtmosphere.CloudShadow.CookieResourceName);
            Assert.IsNotNull(tex, "Resources/" + WorldAtmosphere.CloudShadow.CookieResourceName);
            Assert.AreEqual(1024, tex.width);
            Assert.IsTrue(GraphicsFormatUtility.GetComponentCount(tex.graphicsFormat) == 1, "tek kanal (URP Red cookie): " + tex.graphicsFormat);
        }

        [Test]
        public void Izdusum_UrpCookieIleAyni_Zemin240m_HavadaIsinUcu_Suruklenme52()
        {
            var go = new GameObject("Sun (test)");
            _made.Add(go);
            go.transform.SetPositionAndRotation(new Vector3(3f, 20f, -7f), Quaternion.Euler(WorldAtmosphere.SunEuler));
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            var cookie = Resources.Load<Texture2D>(WorldAtmosphere.CloudShadow.CookieResourceName);
            Assert.IsNotNull(cookie);

            CloudShadowDriver.ForcedPhase = 0f;
            CloudShadowDriver driver = CloudShadowDriver.Attach(light, cookie);
            Assert.AreSame(cookie, light.cookie, "güneşin cookie'si");
            var data = light.GetComponent<UniversalAdditionalLightData>();
            Assert.IsNotNull(data);
            Assert.AreEqual(Tile, data.lightCookieSize.x, 1e-3f);
            Assert.AreEqual(Tile * Mathf.Sin(WorldAtmosphere.SunEuler.x * Mathf.Deg2Rad), data.lightCookieSize.y, 1e-2f, "ışık uzayı y = 240 · sin(yükseklik)");

            // 1) The published rows are URP's mapping (read back from the URP light data, not from the driver).
            var probes = new[] { Vector3.zero, new Vector3(41f, 0f, -27f), new Vector3(-55f, 3.5f, 52f), new Vector3(12f, 9f, 80f) };
            foreach (Vector3 p in probes)
                Assert.Less((driver.CookieUV(p) - UrpUV(go.transform, data, p)).magnitude, 1e-4f, "URP cookie uv " + p);
            Assert.AreEqual(driver.RowU, Shader.GetGlobalVector(CloudShadowDriver.CookieUId));
            Assert.AreEqual(driver.RowV, Shader.GetGlobalVector(CloudShadowDriver.CookieVId));
            Assert.AreSame(cookie, Shader.GetGlobalTexture(CloudShadowDriver.CookieId));
            Vector4 shaft = Shader.GetGlobalVector(CloudShadowDriver.ShaftDirId);
            Assert.Less(Vector3.Distance(go.transform.forward, shaft), 1e-4f, "_DD_ShaftDir = ışığın gittiği yön");

            // 2) One tile is 240 m on the ground along the light's right and along the sun azimuth.
            Vector3 right = go.transform.right, f = go.transform.forward;
            Vector3 along = new Vector3(f.x, 0f, f.z).normalized;
            Vector3 g0 = new Vector3(-8f, 0f, 13f);
            AssertWhole(driver.CookieUV(g0 + right * Tile) - driver.CookieUV(g0), new Vector2(1f, 0f), "sağ eksen 240 m");
            AssertWhole(driver.CookieUV(g0 + along * Tile) - driver.CookieUV(g0), new Vector2(0f, 1f), "güneş azimutu 240 m");
            Assert.AreEqual(52f, Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg, 0.01f, "ışık KD'ye (52°) gider, güneş 232°'den");

            // 3) A point in the air reads the ground at the end of its sun ray (shaft cards, particles).
            var air = new Vector3(6f, 5f, -4f);
            Vector3 hit = air + f * (air.y / -f.y);
            Assert.Less((driver.CookieUV(air) - driver.CookieUV(hit)).magnitude, 1e-4f);

            // 4) Drift: a quarter phase moves the pattern 60 m toward 52°.
            Vector2 before = driver.CookieUV(g0 - CloudShadowDriver.DriftDirection * (0.25f * Tile));
            CloudShadowDriver.ForcedPhase = 0.25f;
            driver.Apply();
            Vector2 after = driver.CookieUV(g0);
            Vector2 d = after - before;
            Assert.Less(new Vector2(d.x - Mathf.Round(d.x), d.y - Mathf.Round(d.y)).magnitude, 1e-3f, "¼ faz = 60 m KD kayma");
            Assert.Less((driver.CookieUV(g0) - UrpUV(go.transform, data, g0)).magnitude, 1e-4f, "kaymada da URP ile aynı");

            // 5) No driver: white cookie, everything reads lit.
            CloudShadowDriver.ClearGlobals();
            Assert.AreSame(Texture2D.whiteTexture, Shader.GetGlobalTexture(CloudShadowDriver.CookieId));
        }

        [Test]
        public void DunyaShaderlari_LightCookiesAnahtari_Hlsl()
        {
            foreach (string s in new[] { "DD_GroundBlend", "DD_Foliage_Wind", "DD_Water_v2", "DD_TierLit" })
                StringAssert.Contains("multi_compile_fragment _ _LIGHT_COOKIES", File.ReadAllText(ArtImportContract.ArtRoot + "/Shaders/" + s + ".shader"), s + ": bulut gölgesi bu yüzeye düşmez");
            string hlsl = File.ReadAllText(ArtImportContract.ArtRoot + "/Shaders/DD_CloudCookie.hlsl");
            foreach (string token in new[] { "_DD_CloudCookieU", "_DD_CloudCookieV", "_DD_ShaftDir", "DD_CloudSunLit01" }) StringAssert.Contains(token, hlsl);
        }

        /// <summary>URP 14 LightCookieManager.SetupMainLight + ComputeLightCookieUVDirectional, reproduced.</summary>
        private static Vector2 UrpUV(Transform light, UniversalAdditionalLightData data, Vector3 p)
        {
            Vector2 scale = new Vector2(1f / data.lightCookieSize.x, 1f / data.lightCookieSize.y);
            Matrix4x4 uvTransform = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1f));
            uvTransform.SetColumn(3, new Vector4(-data.lightCookieOffset.x * scale.x, -data.lightCookieOffset.y * scale.y, 0f, 1f));
            Matrix4x4 m = Matrix4x4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, -0.5f, 0.5f) * uvTransform * Matrix4x4.TRS(light.position, light.rotation, Vector3.one).inverse;
            Vector3 ls = m.MultiplyPoint(p);
            return new Vector2(ls.x * 0.5f + 0.5f, ls.y * 0.5f + 0.5f);
        }

        private static void AssertWhole(Vector2 delta, Vector2 expected, string what)
        {
            Assert.Less((new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) - expected).magnitude, 1e-3f, what + ": " + delta.ToString("F4"));
        }

        /// <summary>Smallest connected patch (sun: ≥ 0.62, shadow: &lt; 0.62) on the torus, as an equivalent diameter in pixels.</summary>
        private static float SmallestPatch(float[] v, int n, bool sun)
        {
            var seen = new bool[v.Length];
            var queue = new int[v.Length];
            float smallest = float.MaxValue;
            for (int s = 0; s < v.Length; s++)
            {
                if (seen[s] || (v[s] >= 0.62f) != sun) continue;
                int head = 0, tail = 0;
                queue[tail++] = s;
                seen[s] = true;
                while (head < tail)
                {
                    int i = queue[head++], x = i % n, y = i / n;
                    int[] next = { y * n + (x + 1) % n, y * n + (x + n - 1) % n, (y + 1) % n * n + x, (y + n - 1) % n * n + x };
                    foreach (int j in next)
                    {
                        if (seen[j] || (v[j] >= 0.62f) != sun) continue;
                        seen[j] = true;
                        queue[tail++] = j;
                    }
                }

                smallest = Mathf.Min(smallest, 2f * Mathf.Sqrt(tail / Mathf.PI));
            }

            return smallest;
        }
    }
}
