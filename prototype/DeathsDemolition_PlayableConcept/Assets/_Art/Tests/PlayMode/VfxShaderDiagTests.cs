using System.Collections;
using System.IO;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-019 · Shader smoke: a catalog material on the shared quad must change pixels (the
    /// Metal constant-buffer packing bug that made every sheet sample its transparent border
    /// was caught this way; DD_VfxFlipbook.shader keeps an all-float UnityPerMaterial block).
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class VfxShaderDiagTests
    {
        [UnityTest]
        public IEnumerator FlipbookShader_KatalogMateryali_PikselCizer()
        {
            LogAssert.ignoreFailingMessages = true;
            var light = new GameObject("diag-light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 3f;
            yield return null; yield return null;
            var pose = new ArtVisualRegression.CameraPose("diag", new Vector3(0f, 3f, -6f), new Vector3(0f, 1f, 0f), 42f);
            Texture2D a = ArtVisualRegression.Capture(pose, 640, 360, false);
            long empty = Sum(a); Object.DestroyImmediate(a);

            var go = new GameObject("diag-quad");
            go.transform.position = new Vector3(0f, 1f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            var r = go.AddComponent<MeshRenderer>();
            long ring = 0, scorch = 0, impact = 0;
            foreach (string id in new[] { VfxIds.BeaconRing, VfxIds.DecalScorch, VfxIds.ImpactStone })
            {
                Assert.IsTrue(VfxLibrary.TryGetPlayable(id, out VfxCatalogEntry row), id);
                r.sharedMaterial = row.Material;
                var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Frame", row.FrameCells > 1 ? 2f : 0f); mpb.SetFloat("_Alpha", 1f); mpb.SetFloat("_AlphaCap", 1f); mpb.SetColor("_Tint", Color.white); r.SetPropertyBlock(mpb);
                yield return null;
                Texture2D t = ArtVisualRegression.Capture(pose, 640, 360, false);
                long s = Sum(t); Object.DestroyImmediate(t);
                if (id == VfxIds.BeaconRing) ring = s; else if (id == VfxIds.DecalScorch) scorch = s; else impact = s;
            }

            Debug.Log("[DD-TEST] wp019.shadersmoke empty=" + empty + " ring=" + ring + " scorch=" + scorch + " impactFrame2=" + impact);
            Assert.AreNotEqual(empty, ring, "Beacon ring (additive) hiç piksel değiştirmedi.");
            Assert.AreNotEqual(ring, scorch, "Ring ve scorch aynı kareyi verdi: doku bağlı değil (varsayılan beyaz).");
            Assert.AreNotEqual(empty, scorch, "Scorch dekalı (alpha) hiç piksel değiştirmedi.");
            Assert.AreNotEqual(empty, impact, "Impact sheet karesi hiç piksel değiştirmedi.");
            Object.Destroy(go); Object.Destroy(floor); Object.Destroy(light.gameObject);
        }

        private static long Sum(Texture2D t)
        {
            long s = 0; foreach (Color32 p in t.GetPixels32()) s += p.r + p.g + p.b; return s;
        }
    }
}
