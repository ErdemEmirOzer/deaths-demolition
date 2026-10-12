using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = NUnit.Framework.Is;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-101 (R4, AP-041) · the premultiplied path drawn and the dressing that uses it:
    ///  - DD/VFX Flipbook <c>_Premultiplied 1</c> composites a PM texel as T.rgb + bg × (1 − T.a) (no second × a, no a = 1),
    ///    a fade scales rgb and a together, the cap rescales both, a = 0 leaves the background; 0 keeps the straight path.
    ///  - top-down, every class plays its AP-041 TD card (three variants in turn; long axis = §1.1 FlashLength, height = the
    ///    manifest [w, h], pivot on the socket), the launcher keeps fb_muzzle, the smoke card is throttled;
    ///  - impacts play the AP-041 sheet of their surface group, stone / wood / metal holes cycle four variants, dirt keeps the
    ///    AP-010 mark, the calibre still sizes the hole and the light round still drops the stone card;
    ///  - 0 B on the calling thread for 60 TD flashes and 120 impacts after warm-up.
    /// The dressing tests are ignored when the AP-041 set is not imported (the shader test always runs).
    /// </summary>
    [TestFixture]
    [Category("WP101")]
    public sealed class Wp101Ap041DressingTests
    {
        private static readonly Vector3 P = new Vector3(0f, 1f, 60f);
        private readonly List<Object> _temp = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);   // review: never silently skipped after a TP/FP test
            VfxTier.Override = VfxTierLevel.High;
            GameFeelSettings.FlashScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            VfxDressing.ProfileOverrideForTests = null;
            VfxTier.Override = null;
            GameFeelSettings.FlashScale = 1f;
            foreach (Object o in _temp) if (o != null) Object.Destroy(o);
            _temp.Clear();
        }

        private static VfxDressing Dressing(bool muzzle, bool impacts)
        {
            VfxDressing vfx = HitFeedbackDirector.Instance.Vfx;
            if (!vfx.IsReady) Assert.Ignore("VFX kataloğu yok");
            if (muzzle && !vfx.Ap041MuzzleReady(Ap041MuzzleClass.Rifle)) Assert.Ignore("AP-041 namlu satırları içe alınmamış");
            if (impacts && !vfx.Ap041ImpactsReady) Assert.Ignore("AP-041 isabet satırları içe alınmamış");
            if (CameraModeService.Current != CameraMode.TopDown) Assert.Ignore("kamera TD değil: " + CameraModeService.Current);
            return vfx;
        }

        private Transform Socket()
        {
            var go = new GameObject("Wp101Muzzle");
            go.transform.SetPositionAndRotation(P, Quaternion.LookRotation(Vector3.right));
            _temp.Add(go);
            return go.transform;
        }

        private static FlipbookEffect CardOn(VfxDressing vfx, Transform parent, string id)
        {
            FlipbookEffect hit = null;
            foreach (FlipbookEffect f in vfx.FlipbookPool.Spawned)
                if (f != null && f.Row != null && f.Row.Id == id && f.transform.parent == parent) hit = f;
            return hit;
        }

        // ------------------------------------------------------------------ shader

        [UnityTest]
        public IEnumerator Shader_Premultiplied_KompozitDogru_StraightYoluAyri()
        {
            LogAssert.ignoreFailingMessages = true;   // sonra-r1: an aim/HUD system logs "Screen position out of view frustum" for the 768² test camera
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var camGo = new GameObject("Wp101PmCamera");
            _temp.Add(camGo);
            Camera cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 500f, -5f), Quaternion.identity);
            cam.orthographic = true;
            cam.orthographicSize = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            Color bg = new Color(0.5f, 0.5f, 0.5f, 1f);   // sRGB grey
            cam.backgroundColor = bg;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;

            Shader shader = Shader.Find("DD/VFX Flipbook");
            Assert.IsNotNull(shader);
            // A PM texel: linear white × coverage 128/255, stored sRGB (188) — and a transparent one (0, 0, 0, 0).
            Texture2D half = Solid(new Color32(188, 188, 188, 128));
            Texture2D clear = Solid(new Color32(0, 0, 0, 0));
            var quad = new GameObject("Wp101PmQuad");
            _temp.Add(quad);
            quad.transform.SetPositionAndRotation(new Vector3(0f, 500f, 0f), Quaternion.identity);
            quad.transform.localScale = new Vector3(4f, 4f, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            MeshRenderer r = quad.AddComponent<MeshRenderer>();
            var m = new Material(shader);
            _temp.Add(m);
            r.sharedMaterial = m;

            float tA = 128f / 255f, tRgb = Mathf.GammaToLinearSpace(188f / 255f), b = Mathf.GammaToLinearSpace(0.5f);
            float got;
            try
            {
                Pm(m, half, 1f, 1f, true);
                yield return Frames();
                got = Read(rt);
                Assert.AreEqual(tRgb + b * (1f - tA), got, 0.02f, "PM: T.rgb + bg × (1 − a)");

                Pm(m, half, 0.5f, 1f, true);
                yield return Frames();
                got = Read(rt);
                Assert.AreEqual(0.5f * tRgb + b * (1f - 0.5f * tA), got, 0.02f, "PM fade 0,5: rgb ve a birlikte");

                Pm(m, half, 1f, 0.25f, true);
                yield return Frames();
                got = Read(rt);
                Assert.AreEqual(tRgb * (0.25f / tA) + b * (1f - 0.25f), got, 0.02f, "PM cap 0,25: rgb newA / oldA ile");

                Pm(m, clear, 1f, 1f, true);
                yield return Frames();
                got = Read(rt);
                Assert.AreEqual(b, got, 0.01f, "a = 0 arka planı değiştirmez (a = 1 hatası yok)");

                Pm(m, half, 1f, 1f, false);
                yield return Frames();
                got = Read(rt);
                float straight = tRgb * tA + b * (1f - tA);
                Assert.AreEqual(straight, got, 0.02f, "_Premultiplied 0: bugünkü straight-alpha yolu birebir");
                Assert.Greater(Mathf.Abs((tRgb + b * (1f - tA)) - straight), 0.08f, "iki yol ayırt edilebilir olmalı");
            }
            finally
            {
                RenderSettings.fog = fog;
                cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(half);
                Object.Destroy(clear);
            }
        }

        private static void Pm(Material m, Texture2D tex, float alpha, float cap, bool premultiplied)
        {
            m.SetTexture("_Sheet", tex);
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_Cols", 1f); m.SetFloat("_Rows", 1f); m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", alpha); m.SetFloat("_AlphaCap", cap); m.SetFloat("_UseEmissive", 0f); m.SetFloat("_ScrollV", 0f);
            m.SetFloat("_Premultiplied", premultiplied ? 1f : 0f);
            m.SetFloat("_SrcBlend", premultiplied ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", 0f); m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = 3000;
        }

        private static Texture2D Solid(Color32 c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static IEnumerator Frames()
        {
            yield return null;
            yield return null;
        }

        /// <summary>Centre texel's green channel, sRGB → linear.</summary>
        private static float Read(RenderTexture rt)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
            t.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            float g = Mathf.GammaToLinearSpace(t.GetPixel(0, 0).g);
            Object.Destroy(t);
            return g;
        }

        // ------------------------------------------------------------------ muzzle

        [UnityTest]
        public IEnumerator TdNamlu_SinifKarti_UcluVaryant_BoyVePivot_RoketatarAp010()
        {
            yield return null;
            VfxDressing vfx = Dressing(true, false);
            Transform socket = Socket();
            var cases = new[]
            {
                (VfxWeaponProfiles.Ar1, Ap041MuzzleClass.Rifle), (VfxWeaponProfiles.Smg1, Ap041MuzzleClass.Pistol), (VfxWeaponProfiles.Sg1, Ap041MuzzleClass.Pellet),
                (VfxWeaponProfiles.Lmg1, Ap041MuzzleClass.Lmg), (VfxWeaponProfiles.Sr1, Ap041MuzzleClass.Sniper)
            };

            foreach ((VfxWeaponProfile profile, Ap041MuzzleClass cls) in cases)
            {
                VfxDressing.ProfileOverrideForTests = profile;
                var seen = new HashSet<string>();
                string first = null;
                for (int shot = 0; shot < 4; shot++)
                {
                    int plays = vfx.Ap041MuzzlePlays;
                    Assert.IsTrue(vfx.PlayMuzzle(socket, 1f), profile.Slot);
                    Assert.AreEqual(plays + 1, vfx.Ap041MuzzlePlays, profile.Slot + ": TD AP-041 kartı oynamadı");
                    string id = vfx.LastMuzzleRowId;
                    Assert.AreEqual(Ap041Vfx.MuzzleId(cls, vfx.LastAp041MuzzleVariant), id, profile.Slot + ": sınıf kartı");
                    if (shot == 0) first = id;
                    if (shot < 3) Assert.IsTrue(seen.Add(id), profile.Slot + ": varyant tekrarı (3'lü döngü)");
                    else Assert.AreEqual(first, id, profile.Slot + ": 4. atış ilk varyanta döner");

                    FlipbookEffect card = CardOn(vfx, socket, id);
                    Assert.IsNotNull(card, profile.Slot + ": kart soketin çocuğu değil");
                    Transform q = card.transform.GetChild(0);
                    float ratio = card.Row.WorldHeightMetres / card.Row.WorldSizeMetres;
                    Assert.AreEqual(profile.FlashLength, Mathf.Abs(q.localScale.x), 1e-4f, profile.Slot + ": uzun eksen §1.1 boyu");
                    Assert.AreEqual(profile.FlashLength * ratio, q.localScale.y, 1e-4f, profile.Slot + ": yükseklik manifest [w, h]");
                    Assert.AreEqual((0.5f - card.Row.OriginUv.x) * q.localScale.x, q.localPosition.x, 1e-4f, profile.Slot + ": pivot u 0,11 sokette (gerilme yok)");
                    Assert.AreEqual(Mathf.Max(VfxDressing.Ap041MinFlashSeconds, card.Row.DurationSeconds * profile.FlashDurationScale), vfx.LastMuzzleLifeSeconds, 1e-4f, profile.Slot + ": ömür");
                    Assert.IsTrue(card.Row.Premultiplied, id);
                }
            }

            Assert.Less(Life(vfx, VfxWeaponProfiles.Smg1, socket), Life(vfx, VfxWeaponProfiles.Ar1, socket) * 0.75f, "SMG flaşı kısa olan kalır");

            VfxDressing.ProfileOverrideForTests = VfxWeaponProfiles.Rl1;
            int before = vfx.Ap041MuzzlePlays;
            vfx.PlayMuzzle(socket, 1f);
            Assert.AreEqual(before, vfx.Ap041MuzzlePlays, "roketatar AP-041 kartı oynatmaz");
            Assert.AreEqual(VfxIds.Muzzle, vfx.LastMuzzleRowId, "roketatar AP-010 fb_muzzle + geri tepme");
        }

        private static float Life(VfxDressing vfx, VfxWeaponProfile p, Transform socket)
        {
            VfxDressing.ProfileOverrideForTests = p;
            vfx.PlayMuzzle(socket, 1f);
            return vfx.LastMuzzleLifeSeconds;
        }

        [UnityTest]
        public IEnumerator TdDuman_KartAraligi_ParcacikDumaniKapali()
        {
            yield return null;
            VfxDressing vfx = Dressing(true, false);
            Transform socket = Socket();
            VfxDressing.ProfileOverrideForTests = VfxWeaponProfiles.Ar1;
            yield return new WaitForSeconds(VfxDressing.Ap041SmokeInterval + 0.05f);
            int cards = vfx.Ap041SmokeCards;
            int puffs = MuzzleSystem.Instance != null ? MuzzleSystem.Instance.SmokeAlive : 0;
            vfx.PlayMuzzle(socket, 1f);
            vfx.PlayMuzzle(socket, 1f);
            vfx.PlayMuzzle(socket, 1f);
            Assert.AreEqual(cards + 1, vfx.Ap041SmokeCards, "aralık içinde tek duman kartı");
            yield return null;
            if (MuzzleSystem.Instance != null) Assert.LessOrEqual(MuzzleSystem.Instance.SmokeAlive, puffs, "TD'de AP-041 duman kartı parçacık dumanının yerini alır");
            yield return new WaitForSeconds(VfxDressing.Ap041SmokeInterval + 0.05f);
            vfx.PlayMuzzle(socket, 1f);
            Assert.AreEqual(cards + 2, vfx.Ap041SmokeCards, "aralıktan sonra yeni kart");
        }

        // ------------------------------------------------------------------ impacts / holes

        [UnityTest]
        public IEnumerator Isabet_YuzeyKarti_DelikDongusu_KalibreVeIslakTas()
        {
            yield return null;
            VfxDressing vfx = Dressing(false, true);
            SurfaceKind[] surfaces = { SurfaceKind.Stone, SurfaceKind.Wood, SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Default };
            for (int i = 0; i < surfaces.Length; i++)
            {
                Vector3 at = P + Vector3.right * (i * 4f);
                vfx.PlaySurfaceImpact(surfaces[i], at, Vector3.up, 1f);
                string want = Ap041Vfx.ImpactIdFor(surfaces[i]);
                Assert.AreEqual(want, vfx.ImpactCardId(surfaces[i]));
                Assert.AreEqual(1, CardsNear(vfx, at, want), surfaces[i] + ": AP-041 isabet kartı");
                DecalEffect d = DecalNear(vfx, at);
                Assert.IsNotNull(d, surfaces[i] + ": iz yok");
                if (surfaces[i] == SurfaceKind.Dirt) Assert.AreEqual(VfxIds.DecalImpactEarth, d.Row.Id, "toprak AP-010 izinde kalır");
                else Assert.IsTrue(d.Row.Premultiplied && d.Row.Id.StartsWith("decal_ap041_", System.StringComparison.Ordinal), surfaces[i] + ": AP-041 delik " + d.Row.Id);
            }

            // Four stone holes in a row: four variants, the fifth wraps.
            var ids = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = P + new Vector3(i * 2f, 0f, 8f);
                vfx.PlaySurfaceImpact(SurfaceKind.Stone, at, Vector3.up, 1f);
                ids.Add(DecalNear(vfx, at).Row.Id);
            }

            Assert.AreEqual(4, new HashSet<string>(ids.GetRange(0, 4)).Count, "taş deliği 4 varyant sırayla");
            Assert.AreEqual(ids[0], ids[4], "5. delik ilk varyanta döner");

            // Calibre and the wet-stone rule apply to the AP-041 card and hole as before.
            Vector3 a = P + new Vector3(0f, 0f, 16f), b = P + new Vector3(4f, 0f, 16f);
            vfx.PlaySurfaceImpact(SurfaceKind.Stone, a, Vector3.up, 1f);
            Assert.IsTrue(vfx.RefineSurfaceImpact(a, SurfaceKind.Stone, WeaponCaliber.Light));
            vfx.PlaySurfaceImpact(SurfaceKind.Stone, b, Vector3.up, 1f);
            Assert.IsTrue(vfx.RefineSurfaceImpact(b, SurfaceKind.Stone, WeaponCaliber.Heavy));
            Assert.AreEqual(0, CardsNear(vfx, a, VfxIds.Ap041ImpactStone), "ıslak taş: hafif mermide kart düşer");
            Assert.AreEqual(1, CardsNear(vfx, b, VfxIds.Ap041ImpactStone), "ağır mermide kart kalır");
            Assert.AreEqual(Ap041Vfx.SpalledHole(ImpactSystem.HoleSize(WeaponCaliber.Light)), DecalNear(vfx, a).CurrentSize, 1e-4f, "the hole with its spall");
            Assert.AreEqual(Ap041Vfx.SpalledHole(ImpactSystem.HoleSize(WeaponCaliber.Heavy)), DecalNear(vfx, b).CurrentSize, 1e-4f, "capped at the sheet size");
        }

        private static int CardsNear(VfxDressing vfx, Vector3 at, string id)
        {
            int n = 0;
            foreach (FlipbookEffect f in vfx.FlipbookPool.Spawned)
                if (f != null && f.Row != null && f.Row.Id == id && (f.transform.position - at).sqrMagnitude < 0.25f) n++;
            return n;
        }

        private static DecalEffect DecalNear(VfxDressing vfx, Vector3 at)
        {
            DecalEffect best = null;
            float bestD = 0.25f;
            foreach (DecalEffect d in vfx.DecalPool.Spawned)
            {
                if (d == null || d.Row == null) continue;
                float dist = (d.transform.position - at).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = d; }
            }

            return best;
        }

        // ------------------------------------------------------------------ 0 B

        [UnityTest]
        public IEnumerator SicakYol_TdNamluVeIsabet_GcSifir()
        {
            yield return null;
            VfxDressing vfx = Dressing(true, true);
            Transform socket = Socket();
            VfxDressing.ProfileOverrideForTests = VfxWeaponProfiles.Lmg1;
            SurfaceKind[] surfaces = { SurfaceKind.Stone, SurfaceKind.Wood, SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Flesh, SurfaceKind.Default };

            void Rep()
            {
                for (int i = 0; i < 20; i++) vfx.PlayMuzzle(socket, 1f);
                for (int i = 0; i < 40; i++) vfx.PlaySurfaceImpact(surfaces[i % surfaces.Length], P + Vector3.right * (i % 7) * 0.3f, Vector3.up, 1f);
            }

            // the pools wrap, every row's material is bound once - and every decal instance has played once (High: 128 holes,
            // ≈ 33 per Rep; sonra-r1 warmed 3 Reps and the measured Reps hit first-time decals)
            for (int rep = 0; rep < 8; rep++) Rep();
            yield return null;
            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol");
            Assert.That(() => { Rep(); Rep(); Rep(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "AP-041 TD namlu / isabet / delik yolu yönetilen bellek ayırdı (yem " + (bait != null ? bait.Length : 0) + " B)");
        }
    }
}
