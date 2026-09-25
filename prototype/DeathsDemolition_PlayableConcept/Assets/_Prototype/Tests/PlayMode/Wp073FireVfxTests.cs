using System.Collections;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 B1 · fire VFX backbone: tracer length per class (Δ clamp(v × 0.020 s, 1.2, 8 m), sniper × 0.025 ≤ 10 m),
    /// the every-N tracer rhythm with a glint on every round, the round travelling at its class speed and expiring past
    /// its impact, a real draw into a target, the Performance tier dropping glow and glint, the sustained-fire muzzle
    /// light (one light for a held SMG, capped lights for single shots), the tier casing pool, 0 B per 25-round frame.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073FireVfxTests
    {
        private GameObject _camGo;
        private RenderTexture _rt;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
            TracerRibbonRenderer.Instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TracerRibbonRenderer.Instance.Clear();
            VfxTier.Override = null;
            GameFeelSettings.FlashScale = 1f;
            if (_camGo != null) Object.Destroy(_camGo);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
        }

        [Test]
        public void IzBoyu_SinifaGore_VeHerNRitmi()
        {
            Assert.AreEqual(5.2f, VfxWeaponProfiles.Ar1.TracerLength, 1e-3f, "ar_1 260 m/s → 5,2 m");
            Assert.AreEqual(3.4f, VfxWeaponProfiles.Smg1.TracerLength, 1e-3f, "smg_1 170 → 3,4");
            Assert.AreEqual(6.4f, VfxWeaponProfiles.Ar2.TracerLength, 1e-3f);
            Assert.AreEqual(10f, VfxWeaponProfiles.Sr1.TracerLength, 1e-3f, "sr_1 × 0,025 s, ≤ 10 m");
            Assert.AreEqual(1.2f, new VfxWeaponProfile { Speed = 20f }.TracerLength, 1e-3f, "alt sınır 1,2 m");
            foreach (VfxWeaponProfile p in VfxWeaponProfiles.All) Assert.AreSame(p, VfxWeaponProfiles.BySlot(p.Slot));

            TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
            Assert.IsTrue(r.Ready, "DD/VFX Ribbon yok ya da desteklenmiyor");
            int tracers0 = r.EmittedTracers, emitted0 = r.Emitted;
            for (int i = 0; i < 9; i++) r.Emit(Vector3.zero, new Vector3(0f, 0f, 30f), VfxWeaponProfiles.Ar1, false, false, false);
            Assert.AreEqual(3, r.EmittedTracers - tracers0, "ar_1: her 3. mermi tam iz");
            Assert.AreEqual(9, r.Emitted - emitted0, "her mermi bir şerit (parıltı her mermide)");
        }

        [UnityTest]
        public IEnumerator Projectile_DurumdanCizilir_AraCizgiKapali_DuvardaKuyrukYetisir()
        {
            // WP-069 ADR-069-1: the ribbon reads ProjectileSystem state (no event per frame); a stopped round's tail runs in.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "WP073-Wall";
            wall.layer = PrototypeLayers.Environment;
            wall.transform.position = new Vector3(0f, 1f, 12f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();
            try
            {
                TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
                Assert.IsTrue(r.Ready);
                var shot = new ShotRequest
                {
                    Origin = new Vector3(0f, 1f, 0f), Direction = Vector3.forward, Damage = 1f, Range = 40f,
                    ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium, PierceDamageFalloff = 1f
                };
                var launch = new ProjectileLaunch
                {
                    WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Caliber = WeaponCaliber.Medium,
                    Speed = 60f, Gravity = 0f, MaxRange = 40f, MaxLifetime = 5f,
                    FalloffStart = 40f, FalloffEnd = 40f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
                };
                ProjectileSystem.Clear();
                for (int i = 0; i < 3; i++) ProjectileSystem.Spawn(shot, launch);   // ar_1: one of three is the full tracer
                ProjectileSystem.Step(0.05f);                                        // 3 m out, all in flight
                yield return null;
                Assert.AreEqual(3, ProjectileSystem.LiveCount, "mermiler uçuşta");
                Assert.IsFalse(ProjectileDriver.DrawInterimTrails, "WP-069 geçici çizgi kapalı olmalı (şerit hazır)");
                Assert.AreEqual(3, r.DrawnGlint, "her mermide parıltı");
                Assert.AreEqual(2, r.DrawnCore, "ar_1: üçte bir tam iz (çekirdek + ≥ 6 px baş noktası, AD B1 MUST 7)");

                int tailOuts = r.TailOuts;
                ProjectileSystem.Step(0.3f);                                         // into the wall
                Assert.AreEqual(0, ProjectileSystem.LiveCount, "duvarda durdu");
                Assert.AreEqual(tailOuts + 3, r.TailOuts, "duran her merminin kuyruğu içeri koşar");
                yield return null;
                Assert.Greater(r.LiveCount, 0, "kuyruk yetişme şeridi çizimde");
            }
            finally
            {
                ProjectileSystem.Clear();
                Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator Mermi_SinifHizindaUcar_CarpmaSonrasiBiter()
        {
            TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
            r.Emit(Vector3.zero, new Vector3(0f, 0f, 20f), VfxWeaponProfiles.Ar1, false, false, false);
            yield return null;
            Assert.AreEqual(1, r.LiveCount, "uçuşta");
            // 20 m at 260 m/s + a 5.2 m tail running in: gone well inside 0.2 s.
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(0, r.LiveCount, "çarpmadan sonra kuyruk yetişince biter");

            // Shotgun pellets: glint bars that die out by 6 m even on a 40 m shot.
            for (int i = 0; i < 8; i++) r.Emit(Vector3.zero, new Vector3(i * 0.1f, 0f, 40f), VfxWeaponProfiles.Sg1, true, i == 0, false);
            yield return null;
            Assert.AreEqual(8, r.LiveCount);
            yield return new WaitForSeconds(0.1f);   // 120 m/s → 12 m travelled: past the 6 m fade
            Assert.AreEqual(0, r.LiveCount, "pellet çubukları 6 m'de söner");
        }

        [UnityTest]
        public IEnumerator Serit_HedefeCizilir_PerformanstaParilti_VeGlowYok()
        {
            _rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            _rt.Create();
            _camGo = new GameObject("RibbonTestCamera");
            Camera cam = _camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -2f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = _rt;
            cam.fieldOfView = 60f;

            TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
            // A full tracer crossing the view left → right at 6 m, slow so it stays on screen.
            var slow = new VfxWeaponProfile { Slot = "test", Speed = 20f, CoreWidth = 0.08f, GlowWidth = 0.2f, TracerEveryN = 1,
                HeadColor = new Color(4f, 3f, 2f), TailColor = new Color(2f, 1f, 0.5f), MaxLength = 8f, LengthSeconds = 0.3f };
            r.Emit(new Vector3(-3f, 0f, 6f), new Vector3(3f, 0f, 6f), slow, false, false, false);
            yield return null;
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(r.DrawnCore, 0, "çekirdek çizilmedi");
            Assert.Greater(r.DrawnGlow, 0, "Yüksek'te glow çizilir");
            Assert.Greater(r.DrawnGlint, 0, "Yüksek'te parıltı her mermide");
            yield return null;

            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            int lit = 0;
            foreach (Color32 c in tex.GetPixels32()) if (c.r > 40) lit++;
            Object.Destroy(tex);
            Debug.Log("[DD-TEST] wp073.ribbon lit=" + lit);
            Assert.Greater(lit, 30, "şerit hedefe çizilmedi (instancing / shader?)");

            r.Clear();
            VfxTier.Override = VfxTierLevel.Performance;
            r.Emit(new Vector3(-3f, 0f, 6f), new Vector3(3f, 0f, 6f), slow, false, false, false);
            yield return null;
            Assert.Greater(r.DrawnCore, 0, "Performans'ta iz kalır");
            Assert.AreEqual(0, r.DrawnGlow, "Performans'ta glow yok");
            Assert.AreEqual(0, r.DrawnGlint, "Performans'ta parıltı yok");
        }

        [UnityTest]
        public IEnumerator Namlu_SurekliAtesTekIsik_TekAtislarKademeTavani()
        {
            MuzzleSystem m = MuzzleSystem.Ensure();
            var muzzleGo = new GameObject("TestMuzzle");
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    m.Play(muzzleGo.transform, VfxWeaponProfiles.Smg1, false);
                    yield return null;
                }

                Assert.AreEqual(1, m.LitCount, "SMG sürekli ateş: tek ışık yanık kalır (strobe değil)");

                yield return new WaitForSeconds(0.6f);
                Assert.AreEqual(0, m.LitCount, "tetik bırakılınca söner");

                var others = new GameObject[6];
                for (int i = 0; i < 6; i++)
                {
                    others[i] = new GameObject("M" + i);
                    m.Play(others[i].transform, VfxWeaponProfiles.Ar1, false);
                }

                Assert.AreEqual(4, m.LitCount, "Yüksek: en fazla 4 eşzamanlı namlu ışığı");
                foreach (GameObject g in others) Object.Destroy(g);
                Assert.AreEqual(48, VfxDressing.CasingCapacity, "Yüksek kovan havuzu 48");
                VfxTier.Override = VfxTierLevel.Cinematic;
                Assert.AreEqual(128, VfxDressing.CasingCapacity, "DEC-100: Sinematik kovan havuzu 128");
            }
            finally
            {
                Object.Destroy(muzzleGo);
            }
        }

        [UnityTest]
        public IEnumerator YirmiBesMermiKaresi_SifirAyirma()
        {
            TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
            for (int i = 0; i < 30; i++) r.Emit(Vector3.zero, new Vector3(i, 0f, 40f), VfxWeaponProfiles.Ar1, false, false, false);
            yield return null;
            yield return null;

            UnityEngine.Profiling.Profiler.enabled = true;
            // HotPathAllocationContractTests lesson: under the bootstrapper's 60 fps cap LastValue lags a frame (the
            // positive control read 130 B after the flow suites) — uncap for the measurement, restore after.
            int frameRateWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    yield return null;
                    yield return null;
                    var bait = new object[64];
                    for (int i = 0; i < bait.Length; i++) bait[i] = new byte[1024];
                    long control = 0;
                    for (int f = 0; f < 3; f++)
                    {
                        yield return null;
                        control = System.Math.Max(control, bytes.LastValue);
                    }

                    System.GC.KeepAlive(bait);
                    Assert.Greater(control, 60 * 1024, "pozitif kontrol: kayıtçı ayırmayı görmeli");

                    long best = long.MaxValue;
                    for (int pair = 0; pair < 4; pair++)
                    {
                        for (int i = 0; i < 25; i++) r.Emit(Vector3.zero, new Vector3(i * 0.2f, 0f, 30f), i % 2 == 0 ? VfxWeaponProfiles.Ar1 : VfxWeaponProfiles.Sg1, i % 2 == 1, i == 1, false);
                        yield return null;
                        long burst = bytes.LastValue;
                        yield return null;
                        long quiet = bytes.LastValue;
                        best = System.Math.Min(best, burst - quiet);
                    }

                    Assert.LessOrEqual(best, 0, "25 mermilik kare kontrol karesinden fazla ayırdı: +" + best + " B");
                }
            }
            finally
            {
                Application.targetFrameRate = frameRateWas;
            }
        }
    }
}
