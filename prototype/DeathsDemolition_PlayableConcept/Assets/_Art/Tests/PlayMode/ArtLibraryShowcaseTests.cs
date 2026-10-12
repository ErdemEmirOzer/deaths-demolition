using System;
using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace DD.Art.Tests
{
    /// <summary>
    /// WP-012 · The library showcase renders: every variation prefab is instanced, the
    /// reference pose captures to a non-black, non-magenta frame (a missing shader or
    /// texture would paint magenta), and the capture is written for the art director and
    /// the visual-regression reference (<c>DD_ART_CAPTURE_DIR</c>). Editor PlayMode only:
    /// the scene is deliberately outside the build settings.
    /// </summary>
    [TestFixture]
    [Category("WP012")]
    public sealed class ArtLibraryShowcaseTests
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Vitrin_169Ornek_ReferansPozYakalama_SiyahVeMagentaDegil()
        {
#if UNITY_EDITOR
            var parameters = new LoadSceneParameters(LoadSceneMode.Single);
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(ArtLibraryPaths.ShowcaseScene, parameters);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(scene.isLoaded, "Vitrin sahnesi yüklenmedi: " + ArtLibraryPaths.ShowcaseScene);

            // Per shelf (ArtLibraryShowcaseBuilder: one root per package, one instance per index item with a prefab).
            int ornek = 0, ap023 = 0, ap030 = 0, ap040 = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("SM_", StringComparison.Ordinal) && t.GetComponent<LODGroup>() != null && t.parent == root.transform)
                    {
                        ornek++;
                        if (root.name == "AP-023") ap023++;
                        if (root.name == "AP-030") ap030++;   // WP-092 C: the AP-030 kit is held to its index like AP-023
                        if (root.name == "AP-040") ap040++;   // WP-108: the AP-040 world kit too
                    }
                }
            }

            // B5a (8f412b5d) put AP-023 on the shelf, and every AP-023 manifest moves its count: the whole shelf is held to the
            // library indexes, and the frozen packages keep their fixed 175 so a lost prefab there still fails by number.
            int dizin = 0;
            foreach (string ap in ArtLibraryPaths.Packages)
            {
                var index = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset(ap));
                Assert.IsNotNull(index, "kütüphane dizini yok: " + ap);
                foreach (ArtLibraryAsset.Item item in index.items)
                    if (item != null && item.prefab != null) dizin++;
            }

            Assert.AreEqual(175, ornek - ap023 - ap030 - ap040, "12 + 42 + 53 + 12 + 6 + 50 (AP-017 Faz 1+2+2b, WP-038L v003) varyasyon örneği, AP-023, AP-030 ve AP-040 hariç");   // name kept (169 → 175)
            Assert.AreEqual(dizin, ornek, "vitrin = kütüphane dizinlerindeki her varyasyon (AP-023 " + ap023 + ")");

            for (int i = 0; i < 10; i++) yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Application.temporaryCachePath, "wp012-captures");
            }

            Directory.CreateDirectory(dir);
            // Rows: AP-002 z 0..110 (one column), AP-003 z 130..180 (≤ 8 columns), AP-003B z 200..300 (≤ 6 columns).
            var pose = new ArtVisualRegression.CameraPose("showcase_overview", new Vector3(-30f, 30f, 60f), new Vector3(30f, 0f, 150f), 42f);
            var poses = new[]
            {
                pose,
                new ArtVisualRegression.CameraPose("showcase_ap002", new Vector3(14f, 9f, -14f), new Vector3(0f, 1.5f, 40f), 42f),
                new ArtVisualRegression.CameraPose("showcase_ap003", new Vector3(-6f, 6f, 120f), new Vector3(24f, 1f, 158f), 42f),
                new ArtVisualRegression.CameraPose("showcase_ap003b", new Vector3(-6f, 6f, 190f), new Vector3(24f, 1f, 250f), 42f)
            };

            foreach (ArtVisualRegression.CameraPose p in poses)
            {
                string yol = Path.Combine(dir, p.name + "-" + Width + "x" + Height + ".png");
                ArtVisualRegression.CaptureToPng(p, Width, Height, yol);
                yield return null;
                Assert.IsTrue(File.Exists(yol), "Yakalama yazılmadı: " + yol);

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(yol));
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                long sum = 0;
                int magenta = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    sum += px[i].r + px[i].g + px[i].b;
                    if (px[i].r > 200 && px[i].b > 200 && px[i].g < 60) magenta++;
                }

                double mean = sum / (3.0 * px.Length);
                double magentaFraction = (double)magenta / px.Length;
                Debug.Log("[DD-TEST] wp012.showcase.capture " + p.name + " mean=" + mean.ToString("F1", CultureInfo.InvariantCulture) +
                          " magenta=" + (magentaFraction * 100.0).ToString("F3", CultureInfo.InvariantCulture) + "% -> " + yol);
                Assert.Greater(mean, 1.0, p.name + ": kare siyah (render olmadı?).");
                Assert.Less(magentaFraction, 0.001, p.name + ": magenta pikseller (eksik shader/doku?).");
            }
#else
            Assert.Ignore("Editor PlayMode only.");
            yield break;
#endif
        }
    }
}
