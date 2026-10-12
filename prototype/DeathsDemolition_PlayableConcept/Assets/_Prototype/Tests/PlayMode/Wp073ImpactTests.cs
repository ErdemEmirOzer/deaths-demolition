using System.Collections;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 B2 · impacts (direction §1.4): the surface × calibre debris counts (light ×0.7 / heavy ×1.6, tier ×0.4 / ×1.3),
    /// the impact concurrency cap, the grazing ricochet streak, the impact light rule, the calibre hole size and the wet-stone
    /// rule on the legacy dressing, the exit spray of a pierced body, the head-mist cap, and 0 B per impact on the calling thread.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073ImpactTests
    {
        private static readonly Vector3 P = new Vector3(0f, 1f, 30f);

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
        }

        private static ImpactSystem System()
        {
            VfxDressing vfx = HitFeedbackDirector.Instance.Vfx;
            return vfx.IsReady ? ImpactSystem.Instance : ImpactSystem.Ensure(null);
        }

        private static ProjectileImpactInfo World(SurfaceKind surface, WeaponCaliber calibre, Vector3 point, Vector3 normal, Vector3 dir, ProjectileKind kind = ProjectileKind.Bullet)
        {
            return new ProjectileImpactInfo(1, 0, ClassicWeaponId.AssaultRifle, kind, calibre, point, normal, dir, surface, 200f, false, false, false, true, 0);
        }

        private static ProjectileImpactInfo Body(WeaponCaliber calibre, Vector3 point, Vector3 dir, bool armour, bool stopped)
        {
            return new ProjectileImpactInfo(1, 0, ClassicWeaponId.AssaultRifle, ProjectileKind.Bullet, calibre, point, -dir, dir,
                armour ? SurfaceKind.Armor : SurfaceKind.Flesh, 200f, true, armour, false, stopped, 0);
        }

        [UnityTest]
        public IEnumerator Matris_YuzeyKalibreKademe_Sayilari()
        {
            ImpactSystem s = System();
            Assert.IsNotNull(s, "ImpactSystem kurulmadı");
            yield return null;

            // Stone medium: 8 spray + 4 chips (debris), 2 sparks. Head-on (no ricochet).
            int d0 = s.DebrisEmitted, s0 = s.SparksEmitted;
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(12, s.DebrisEmitted - d0, "taş orta: 8 serpinti + 4 kırıntı");
            Assert.AreEqual(2, s.SparksEmitted - s0, "taş orta: 2 kıvılcım");

            // Light ×0.7 → 6 + 3, 1 spark; heavy ×1.6 → 13 + 6, 3 sparks.
            d0 = s.DebrisEmitted; s0 = s.SparksEmitted;
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Light, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(9, s.DebrisEmitted - d0, "hafif ×0,7");
            Assert.AreEqual(1, s.SparksEmitted - s0);
            d0 = s.DebrisEmitted; s0 = s.SparksEmitted;
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Heavy, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(19, s.DebrisEmitted - d0, "ağır ×1,6");
            Assert.AreEqual(3, s.SparksEmitted - s0);

            // Wood: 8 splinters + 2 lacquer; metal: 10 sparks; dirt: 8 mud; water: 6 spray; paper: nothing extra.
            d0 = s.DebrisEmitted; s0 = s.SparksEmitted;
            s.Play(World(SurfaceKind.Wood, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(10, s.DebrisEmitted - d0, "ahşap: 8 kıymık + 2 lake");
            s.Play(World(SurfaceKind.Metal, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(10, s.SparksEmitted - s0, "metal: 10 kıvılcım");
            d0 = s.DebrisEmitted;
            s.Play(World(SurfaceKind.Dirt, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            s.Play(World(SurfaceKind.Water, WeaponCaliber.Medium, P, Vector3.up, Vector3.down));
            s.Play(World(SurfaceKind.Paper, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(14, s.DebrisEmitted - d0, "toprak 8 çamur + su 6 serpinti, kâğıt ek yok");

            // Performance ×0.4: stone medium → 3 + 2, 1 spark (at least one of each layer).
            VfxTier.Override = VfxTierLevel.Performance;
            yield return null;
            d0 = s.DebrisEmitted; s0 = s.SparksEmitted;
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(5, s.DebrisEmitted - d0, "Performans ×0,4");
            Assert.AreEqual(1, s.SparksEmitted - s0);

            // Rockets / Special orbs are not this layer's (B3 / B6).
            d0 = s.DebrisEmitted; int impacts = s.Impacts;
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Explosive, P, Vector3.back, Vector3.forward, ProjectileKind.Rocket));
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward, ProjectileKind.Orb));
            Assert.AreEqual(impacts, s.Impacts, "roket / küre darbe katmanına girmez");
            Assert.AreEqual(d0, s.DebrisEmitted);
            yield return null;
            Assert.Greater(s.DebrisAlive, 0, "enkaz parçacıkları canlı");
        }

        [UnityTest]
        public IEnumerator Tavan_Sekme_Isik()
        {
            ImpactSystem s = System();
            yield return new WaitForSeconds(ImpactSystem.ConcurrentWindowSeconds + 0.05f);   // an empty window

            // High: 40 impacts per 0.4 s, the rest skipped (the legacy card / hole still play).
            int impacts = s.Impacts, skipped = s.Skipped;
            for (int i = 0; i < 60; i++) s.Play(World(SurfaceKind.Default, WeaponCaliber.Light, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(40, s.Impacts - impacts, "Yüksek kademe tavanı 40");
            Assert.AreEqual(20, s.Skipped - skipped);

            yield return new WaitForSeconds(ImpactSystem.ConcurrentWindowSeconds + 0.05f);
            // Grazing (≈ 5°) on metal: the bounce streak.
            int ric = s.Ricochets;
            Vector3 graze = new Vector3(0f, -0.09f, 1f).normalized;
            s.Play(World(SurfaceKind.Metal, WeaponCaliber.Medium, P, Vector3.up, graze));
            Assert.AreEqual(ric + 1, s.Ricochets, "sıyırma → sekme izi");
            s.Play(World(SurfaceKind.Metal, WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(ric + 1, s.Ricochets, "dik vuruşta sekme yok");

            // Light: High heavy only on metal / armour; Cinematic every calibre; Performance never; never on stone.
            Assert.IsTrue(ImpactSystem.WantsLight(SurfaceKind.Metal, WeaponCaliber.Heavy, VfxTierLevel.High));
            Assert.IsFalse(ImpactSystem.WantsLight(SurfaceKind.Metal, WeaponCaliber.Medium, VfxTierLevel.High));
            Assert.IsTrue(ImpactSystem.WantsLight(SurfaceKind.Armor, WeaponCaliber.Light, VfxTierLevel.Cinematic));
            Assert.IsFalse(ImpactSystem.WantsLight(SurfaceKind.Metal, WeaponCaliber.Heavy, VfxTierLevel.Performance));
            Assert.IsFalse(ImpactSystem.WantsLight(SurfaceKind.Stone, WeaponCaliber.Heavy, VfxTierLevel.Cinematic));
            int lights = s.LightsAsked;
            s.Play(World(SurfaceKind.Metal, WeaponCaliber.Heavy, P, Vector3.back, Vector3.forward));
            Assert.AreEqual(lights + 1, s.LightsAsked, "ağır metal darbe ışığı");
            yield return null;
            Assert.Greater(MuzzleSystem.Instance.ImpactLights, 0);
        }

        [UnityTest]
        public IEnumerator Delik_Kalibreyle_IslakTasTozsuz_CikisSpreyi_KafaSisi()
        {
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            VfxDressing vfx = fx.Vfx;
            if (!vfx.IsReady) Assert.Ignore("AP-010 kataloğu yok");
            ImpactSystem s = ImpactSystem.Instance;
            Assert.IsNotNull(s, "Configure ImpactSystem'i kurmalı");
            yield return new WaitForSeconds(ImpactSystem.ConcurrentWindowSeconds + 0.05f);

            // Light round on stone: hole 0.06 m, the dust card gone (wet world); heavy: 0.14 m and the card stays.
            Vector3 a = P + Vector3.right * 3f, b = P + Vector3.left * 3f;
            vfx.PlaySurfaceImpact(SurfaceKind.Stone, a, Vector3.back, 1f);
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Light, a, Vector3.back, Vector3.forward));
            vfx.PlaySurfaceImpact(SurfaceKind.Stone, b, Vector3.back, 1f);
            s.Play(World(SurfaceKind.Stone, WeaponCaliber.Heavy, b, Vector3.back, Vector3.forward));
            // WP-101 (AP-041): an imported AP-041 hole sheet carries its spall (Ap041Vfx.SpalledHole); the AP-010 hole is the calibre size
            bool spall = VfxLibrary.TryGetPlayable(VfxIds.Ap041DecalStone01, out _);
            Assert.AreEqual(spall ? Ap041Vfx.SpalledHole(0.06f) : 0.06f, NearestDecalSize(vfx, a), 1e-3f, "hafif kalibre deliği 0,06 m (+ AP-041 kırığı)");
            Assert.AreEqual(spall ? Ap041Vfx.SpalledHole(0.14f) : 0.14f, NearestDecalSize(vfx, b), 1e-3f, "ağır kalibre deliği 0,14 m (+ AP-041 kırığı, ≤ 0,28)");
            // WP-101 (R4, AP-041): the stone card is the AP-041 sheet once it is imported - the wet-stone rule is the same.
            string stoneCard = vfx.ImpactCardId(SurfaceKind.Stone);
            Assert.AreEqual(0, CardsAt(vfx, a, stoneCard), "ıslak taş: hafif mermide toz kartı yok");
            Assert.AreEqual(1, CardsAt(vfx, b, stoneCard), "ağır mermide taş kartı kalır");

            // A pierced body sprays out of the back and stains the wall behind it.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer = PrototypeLayers.Environment;
            wall.transform.position = P + new Vector3(0f, 0f, 1.5f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();
            try
            {
                int sprays = vfx.Blood.ExitSprays;
                s.Play(Body(WeaponCaliber.Medium, P, Vector3.forward, false, false));
                Assert.AreEqual(sprays + 1, vfx.Blood.ExitSprays, "delip geçen mermi çıkış spreyi");
                s.Play(Body(WeaponCaliber.Medium, P, Vector3.forward, false, true));
                Assert.AreEqual(sprays + 1, vfx.Blood.ExitSprays, "gövdede duran mermi sprey yapmaz");
            }
            finally
            {
                Object.Destroy(wall);
            }

            // Head mist: at most 2 at once on High.
            int mists = vfx.Blood.Mists;
            for (int i = 0; i < 3; i++) vfx.Blood.PlayHeadMist(P + Vector3.up * i);
            Assert.AreEqual(mists + 2, vfx.Blood.Mists, "kırmızı sis tavanı 2");
        }

        [UnityTest]
        public IEnumerator Darbe_GcSifir_IsParcacigiKaydedici()
        {
            ImpactSystem s = System();
            yield return new WaitForSeconds(ImpactSystem.ConcurrentWindowSeconds + 0.05f);
            for (int i = 0; i < 8; i++) s.Play(World((SurfaceKind)(i % 9), WeaponCaliber.Medium, P, Vector3.back, Vector3.forward));   // warm
            yield return new WaitForSeconds(ImpactSystem.ConcurrentWindowSeconds + 0.05f);

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() =>
            {
                for (int i = 0; i < 36; i++)
                {
                    s.Play(World((SurfaceKind)(i % 9), (WeaponCaliber)(i % 3), P, Vector3.back, Vector3.forward));
                }

                s.Play(World(SurfaceKind.Metal, WeaponCaliber.Heavy, P, Vector3.up, new Vector3(0f, -0.09f, 1f).normalized));
                for (int i = 0; i < 20; i++) s.Play(World(SurfaceKind.Stone, WeaponCaliber.Light, P, Vector3.back, Vector3.forward));   // over the cap: skipped path
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "darbe katmanı yönetilen bellek ayırdı (bu iş parçacığı; yem " + (bait != null ? bait.Length : 0) + " B)");
        }

        [UnityTest]
        public IEnumerator Atlas_HucreEslemesi_CamurKoyuKahve_TasAcikGri()
        {
            // The debris cell per emit (texture-sheet start frame, normalised) must land on its row: a mud particle renders
            // dark brown, a slate chip light grey — a wrong mapping draws both from one cell.
            ImpactSystem s = System();
            var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var camGo = new GameObject("ImpactAtlasCamera");
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 300f, -2f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                cam.targetTexture = rt;
                cam.fieldOfView = 30f;
                Color mud = default, stone = default;
                for (int pass = 0; pass < 2; pass++)
                {
                    s.ClearParticles();
                    s.ProbeEmit(pass == 0 ? ImpactSystem.ProbeCellMud : ImpactSystem.ProbeCellStone, new Vector3(0f, 300f, 2f), 3f);
                    yield return null;
                    yield return null;
                    Color c = Centre(rt);
                    if (pass == 0) mud = c; else stone = c;
                }

                s.ClearParticles();
                Debug.Log("[DD-TEST] wp073.atlas mud=" + mud + " stone=" + stone);
                Assert.Less(Luma(mud) + 0.12f, Luma(stone), "çamur hücresi taş kırıntısından koyu olmalı (hücre eşlemesi)");
                Assert.Greater(mud.r, mud.b + 0.02f, "çamur kahverengi (r > b)");
                Assert.Less(Luma(mud), 0.45f, "çamur koyu (arka plan 0,5 griden koyu)");
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Centre(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(rt.width / 2 - 4, rt.height / 2 - 4, 8, 8), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color sum = Color.black;
            Color[] px = tex.GetPixels();
            foreach (Color c in px) sum += c;
            Object.Destroy(tex);
            return sum / px.Length;
        }

        private static float NearestDecalSize(VfxDressing vfx, Vector3 at)
        {
            float best = float.MaxValue, size = -1f;
            foreach (DecalEffect d in vfx.DecalPool.Spawned)
            {
                if (d == null) continue;
                float dist = (d.transform.position - at).sqrMagnitude;
                if (dist < best) { best = dist; size = d.CurrentSize; }
            }

            return size;
        }

        private static int CardsAt(VfxDressing vfx, Vector3 at, string id)
        {
            int n = 0;
            foreach (FlipbookEffect f in vfx.FlipbookPool.Spawned)
            {
                if (f != null && f.Row != null && f.Row.Id == id && (f.transform.position - at).sqrMagnitude < 0.25f) n++;
            }

            return n;
        }
    }
}
