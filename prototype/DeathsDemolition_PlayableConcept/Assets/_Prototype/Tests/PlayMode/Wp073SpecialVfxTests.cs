using System;
using System.Collections;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 B6 · the two Specials' VFX (direction §2.1 / §2.2 / §4 / §5): Raiju — hop arcs, branches and the tier caps
    /// (8 / 8 / 6 main, 32 / 24 / 12 branches), charge / discharge / orb / fizzle, the electrocuted body (joint arcs, head
    /// smoke, Lichtenberg burn), the rim envelope rule; Namekuji — the puddle life table (spread / live / drying / fade),
    /// FieldEnded, the visual cap, the blob read from the projectile state, impact fan + wall runs, slide smears (S-03 beat
    /// and the player), the §4.1 player guard; the model glow state tables; the old Kagura / Ofuda / Tide dressing is gone
    /// and the bus reaches the new systems; and 0 B per event + frame on the calling thread (WP-059D pattern with a
    /// positive control). No run scene: the systems are driven directly, like Wp073ImpactTests.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073SpecialVfxTests
    {
        private static readonly Vector3 P = new Vector3(0f, 1f, 30f);
        private GameObject _ground;
        private GameObject _body;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
            MuzzleSystem.Ensure();
            ImpactSystem.Ensure(HitFeedbackDirector.Instance != null ? HitFeedbackDirector.Instance.Vfx : null);
            ArcSystem.Ensure().Clear();
            GelSystem.Ensure().Clear();
            SpecialWeaponGlow.Ensure();
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
            GelSystem.PlayerOverride = null;
            ProjectileSystem.Clear();
            if (ArcSystem.Instance != null) ArcSystem.Instance.Clear();
            if (GelSystem.Instance != null) GelSystem.Instance.Clear();
            if (_ground != null) Object.Destroy(_ground);
            if (_body != null) Object.Destroy(_body);
        }

        private GameObject Ground()
        {
            if (_ground == null)
            {
                _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);   // Default layer: in both systems' floor mask
                _ground.transform.position = new Vector3(P.x, 0f, P.z);
                _ground.transform.localScale = Vector3.one * 3f;
                Physics.SyncTransforms();   // the collider was registered at the origin before the move
            }

            return _ground;
        }

        private static SpecialUsageEvent Hop(int index, Vector3 from, Vector3 to, in DamageTarget target = default, float seconds = 1.2f)
        {
            return new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Hit, "sw_arc_orb", from, to - from, 6f, seconds, index, target, 1f, 1f, 0);
        }

        private static SpecialUsageEvent Raiju(SpecialUsageKind kind, Vector3 at, float seconds = 0f, int count = 0)
        {
            return new SpecialUsageEvent(SpecialWeaponId.Raiju, kind, "sw_arc_orb", at, Vector3.forward, 0f, seconds, count);
        }

        private static SpecialUsageEvent Gel(SpecialUsageKind kind, Vector3 at, Vector3 dir, float radius = 0f, float seconds = 0f, int count = 0)
        {
            return new SpecialUsageEvent(SpecialWeaponId.Namekuji, kind, "sw_gel_puddle", at, dir, radius, seconds, count);
        }

        private static void Spawn(ProjectileKind kind, int special, Vector3 at)
        {
            var shot = new ShotRequest { Origin = at, Direction = Vector3.forward, Damage = 0f, Range = 30f, DrawTracer = false };
            var launch = new ProjectileLaunch
            {
                WeaponId = (ClassicWeaponId)(100 + special), Kind = kind, Caliber = WeaponCaliber.Heavy, Speed = kind == ProjectileKind.Orb ? 14f : 22f,
                Gravity = kind == ProjectileKind.Gel ? 12f : 0f, MaxRange = 30f, MaxLifetime = 3f, SweepRadius = 0.1f,
                FalloffStart = 30f, FalloffEnd = 30f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f, HeadMultiplier = 1f, ArmorMultiplier = 1f
            };
            ProjectileSystem.Spawn(shot, launch);
        }

        // ------------------------------------------------------------------ Raiju

        [UnityTest]
        public IEnumerator Raiju_Sicrama_Ark_Dal_KademeTavanlari()
        {
            ArcSystem arc = ArcSystem.Instance;
            Assert.IsTrue(arc.Ready, "DD/VFX Ribbon / Gel / Puddle kütüphanede yok (Rebuild Shader Library)");
            int chains = arc.ChainArcs;
            for (int i = 0; i < 3; i++) arc.OnUsage(Hop(i, P + Vector3.right * (3f * i), P + Vector3.right * (3f * i + 3f)));
            Assert.AreEqual(chains + 3, arc.ChainArcs, "her sıçrama bir ana ark");
            Assert.AreEqual(3, arc.LiveMainArcs);
            arc.Frame(Time.time);   // a whole frame now: branches live 40 ms, a slow batch frame would miss them
            Assert.Greater(arc.CoreInstances, 3 * 14, "ana ark 16 nokta = 15 çekirdek parçası");
            Assert.Greater(arc.HaloInstances, 0, "hale katmanı çizilmedi");
            Assert.Greater(arc.LiveBranches, 0, "sıçramada dal yok");
            Assert.LessOrEqual(arc.LiveBranches, ArcSystem.BranchCap);

            // High: 8 main arcs at once — a ninth hop retires the oldest (a new hop always shows).
            for (int i = 0; i < 12; i++) arc.OnUsage(Hop(i, P, P + Vector3.forward * 4f));
            Assert.AreEqual(8, arc.LiveMainArcs, "Yüksek ana ark tavanı 8");
            yield return new WaitForSeconds(ArcSystem.HopVisibleSeconds + 0.1f);
            Assert.AreEqual(0, arc.LiveMainArcs, "sıçrama arkı 0,12 s görünür");

            // Performance: 6 main arcs, 12 branches, 2 branches per re-sample, no joint arcs.
            VfxTier.Override = VfxTierLevel.Performance;
            yield return null;
            Assert.AreEqual(6, ArcSystem.MainArcCap);
            Assert.AreEqual(12, ArcSystem.BranchCap);
            Assert.AreEqual(2, ArcSystem.BranchesPerHop);
            Assert.IsFalse(ArcSystem.JointArcs);
            for (int i = 0; i < 10; i++) arc.OnUsage(Hop(i, P, P + Vector3.forward * 5f));
            Assert.AreEqual(6, arc.LiveMainArcs, "Performans ana ark tavanı 6");
            yield return null;
            Assert.LessOrEqual(arc.LiveBranches, 12, "Performans dal tavanı 12");

            VfxTier.Override = VfxTierLevel.Cinematic;
            Assert.AreEqual(32, ArcSystem.BranchCap);
            Assert.AreEqual(4, ArcSystem.BranchesPerHop);
            Assert.IsTrue(ArcSystem.FeetRing);
        }

        [UnityTest]
        public IEnumerator Raiju_Sarj_Desarj_Kure_Sonme()
        {
            ArcSystem arc = ArcSystem.Instance;
            Ground();
            arc.OnUsage(Raiju(SpecialUsageKind.ChargeStarted, Vector3.zero, 0.25f));
            Assert.IsTrue(arc.Charging, "ChargeStarted → şarj");
            arc.OnUsage(Raiju(SpecialUsageKind.ChargeCancelled, Vector3.zero));
            Assert.IsFalse(arc.Charging, "ChargeCancelled → şarj biter");

            int discharges = arc.Discharges;
            arc.OnUsage(Raiju(SpecialUsageKind.Fired, P));
            Assert.AreEqual(discharges + 1, arc.Discharges);
            Assert.GreaterOrEqual(arc.LiveMainArcs, 3, "halka deşarjı: 3 elektrot arası 3 ark");

            // The orb is read from the projectile state (no event per frame).
            Spawn(ProjectileKind.Orb, 0, P + Vector3.up);
            yield return null;
            Assert.GreaterOrEqual(arc.OrbsDrawn, 1, "küre çizilmedi (ProjectileSystem Kind Orb)");
            ProjectileSystem.Clear();
            yield return null;
            Assert.AreEqual(0, arc.OrbsDrawn);

            // Fizzle (FieldEnded Count 0, Seconds 0.3): crackle + 6 sparks + a 0.4 m burn on the floor below.
            int fizzles = arc.Fizzles, burns = arc.BurnsPlaced, sparks = arc.SparksAsked;
            arc.OnUsage(Raiju(SpecialUsageKind.FieldEnded, new Vector3(P.x, 1.2f, P.z), 0.3f, 0));
            Assert.AreEqual(fizzles + 1, arc.Fizzles);
            Assert.AreEqual(burns + 1, arc.BurnsPlaced, "sönmede zemin yanığı");
            Assert.AreEqual(sparks + 6, arc.SparksAsked);

            // Count 1 = the orb reached a body: no fizzle.
            arc.OnUsage(Raiju(SpecialUsageKind.FieldEnded, P, 0f, 1));
            Assert.AreEqual(fizzles + 1, arc.Fizzles, "gövdeye ulaşan küre sönme sayılmaz");
        }

        [UnityTest]
        public IEnumerator Raiju_Carpilma_Eklem_Duman_Yanik()
        {
            ArcSystem arc = ArcSystem.Instance;
            _body = new GameObject("wp073b6_body");
            _body.transform.position = new Vector3(P.x, 0f, P.z);
            var target = new DamageTarget { Root = _body.transform, Id = _body.GetInstanceID(), Height = 1.8f, Weight = FeedbackWeight.Standard };
            int shocks = arc.Shocks, burns = arc.BurnsPlaced, sparks = arc.SparksAsked;
            arc.OnUsage(Hop(0, P + Vector3.back * 3f, P + Vector3.up * 0.2f, target, 1.2f));
            Assert.AreEqual(shocks + 1, arc.Shocks);
            Assert.AreEqual(1, arc.LiveShocks);
            Assert.AreEqual(burns + 1, arc.BurnsPlaced, "çarpılan gövdenin altında Lichtenberg yanığı");
            Assert.AreEqual(sparks + 12, arc.SparksAsked, "uçta 12 kıvılcım");

            // The same body again: extends, never a second record.
            arc.OnUsage(Hop(1, P + Vector3.left * 3f, P + Vector3.up * 0.2f, target, 1.2f));
            Assert.AreEqual(1, arc.LiveShocks, "aynı gövde ikinci kayıt açmaz");

            arc.Step(Time.time);    // joint arcs live 60 ms: step now rather than wait for a (slow) frame
            int main = arc.LiveMainArcs;
            Assert.Greater(arc.LiveArcs, main, "Yüksek: eklem arkları (baş–göğüs–leğen–eller)");
            yield return new WaitForSeconds(0.75f);
            Assert.Greater(arc.SmokePuffs, 0, "0,6 s sonra baştan duman");
            yield return new WaitForSeconds(1.6f);
            Assert.AreEqual(0, arc.LiveShocks, "titreme + duman bitti, kayıt kapandı");
            Assert.AreEqual(arc.BurnsPlaced - burns, arc.LiveBurns, "yanıklar 10 s kalır (iki sıçrama = iki yanık)");
        }

        [Test]
        public void Carpilma_Rim_Zarfi_FlasKurali()
        {
            // Full for min(0.5 s, 42 %), 20 Hz only for the first 0.4 s, then 8 Hz (A4.6 / §4.7); reduce flashes: steady.
            Vector4 on = ZombieSkinFx.ShockColour(0.01f, 1.2f, false);
            Vector4 off = ZombieSkinFx.ShockColour(0.03f, 1.2f, false);    // 0.03 × 20 = 0.6 → the low half
            Assert.Greater(on.x, off.x * 2f, "20 Hz kare dalga");
            Assert.LessOrEqual(ZombieSkinFx.FastFlickerSeconds, 0.4f, "> 12 Hz yalnız ≤ 0,4 s");
            // After 0.4 s the square wave is 8 Hz: 0.45 × 8 = 3.6 → low half, 0.51 × 8 = 4.08 → high half.
            Assert.Greater(ZombieSkinFx.ShockColour(0.51f, 1.2f, false).x, ZombieSkinFx.ShockColour(0.45f, 1.2f, false).x, "8 Hz evre");
            Assert.AreEqual(Vector4.zero, ZombieSkinFx.ShockColour(1.2f, 1.2f, false), "titreme sonunda sıfır");
            Vector4 r1 = ZombieSkinFx.ShockColour(0.01f, 1.2f, true), r2 = ZombieSkinFx.ShockColour(0.03f, 1.2f, true);
            Assert.AreEqual(r1.x, r2.x, 1e-5f, "flaş azaltma: titreşimsiz");
            Assert.Greater(ZombieSkinFx.ShockColour(0.2f, 1.2f, true).x, ZombieSkinFx.ShockColour(1.0f, 1.2f, true).x, "0,5 s tam, sonra söner");

            Assert.Greater(ZombieSkinFx.GelColour(0f).x, 0f);
            Assert.AreEqual(Vector4.zero, ZombieSkinFx.GelColour(ZombieSkinFx.GelFilmSeconds), "jel filmi 6 s");
        }

        // ------------------------------------------------------------------ Namekuji

        [Test]
        public void Birikinti_YasamTablosu_Yayilma_Kuruma()
        {
            Assert.AreEqual(GelSystem.PuddlePhase.Spreading, GelSystem.PhaseAt(0.05f, 20f));
            Assert.AreEqual(GelSystem.PuddlePhase.Live, GelSystem.PhaseAt(0.5f, 10f));
            Assert.AreEqual(GelSystem.PuddlePhase.Drying, GelSystem.PhaseAt(0.85f, 3f));
            Assert.AreEqual(GelSystem.PuddlePhase.Fading, GelSystem.PhaseAt(0.95f, 1f), "son 2 s solma");
            Assert.AreEqual(0.6f, GelSystem.SpreadScale(0f), 1e-4f, "yayılma 0,6'dan");
            Assert.AreEqual(1f, GelSystem.SpreadScale(0.1f), 1e-4f, "%10'da tam");
            Assert.Greater(GelSystem.SpreadScale(0.05f), 0.8f, "ease-out");
        }

        [UnityTest]
        public IEnumerator Birikinti_Dogar_Biter_GorselTavan()
        {
            GelSystem gel = GelSystem.Instance;
            Assert.IsTrue(gel.Ready, "DD/VFX Puddle kütüphanede yok");
            int started = gel.PuddlesStarted;
            gel.OnUsage(Gel(SpecialUsageKind.FieldSpawned, new Vector3(P.x, 0f, P.z), Vector3.up, 1.6f, 25f, 90001));
            Assert.AreEqual(started + 1, gel.PuddlesStarted);
            Assert.AreEqual(1, gel.LivePuddles);
            yield return null;
            Assert.AreEqual(1, gel.PuddlesDrawn, "birikinti çizilmedi");

            // A wall contact's normal: the puddle still lies flat (S-02 found the floor below).
            gel.OnUsage(Gel(SpecialUsageKind.FieldSpawned, new Vector3(P.x + 3f, 0f, P.z), Vector3.back, 0.9f, 8f, 90002));
            gel.OnUsage(Gel(SpecialUsageKind.FieldEnded, Vector3.zero, Vector3.up, 0f, 0f, 90001));
            Assert.AreEqual(2, gel.LivePuddles, "FieldEnded → 0,5 s solma, hemen silinmez");
            yield return new WaitForSeconds(GelSystem.EndFadeSeconds + 0.1f);
            Assert.AreEqual(1, gel.LivePuddles, "FieldEnded sonrası birikinti kalktı");

            for (int i = 0; i < 20; i++) gel.OnUsage(Gel(SpecialUsageKind.FieldSpawned, new Vector3(P.x + i, 0f, P.z), Vector3.up, 1.6f, 25f, 90100 + i));
            Assert.AreEqual(GelSystem.MaxPuddles, gel.LivePuddles, "görsel tavan (kayıt defteri 12 + solanlar)");

            // A different Special's field is not a puddle.
            gel.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.FieldSpawned, "sw_arc_orb", P, Vector3.forward, 0.5f, 3f, 0));
            Assert.AreEqual(GelSystem.MaxPuddles, gel.LivePuddles);
        }

        [UnityTest]
        public IEnumerator Jel_Damla_Sap_Duvar_Kayma()
        {
            GelSystem gel = GelSystem.Instance;
            Ground();

            Spawn(ProjectileKind.Gel, 1, P + Vector3.up);
            yield return null;
            Assert.GreaterOrEqual(gel.BlobsDrawn, 1, "jel damlası çizilmedi (ProjectileSystem Kind Gel)");
            ProjectileSystem.Clear();

            int drops = gel.DropsEmitted, runs = gel.Runs;
            gel.PlayImpact(Time.time, new Vector3(P.x, 0f, P.z), Vector3.up);
            Assert.AreEqual(drops + 8, gel.DropsEmitted, "şap: 8 damla yelpazesi");
            Assert.AreEqual(runs, gel.Runs, "zeminde akıntı yok");
            gel.PlayImpact(Time.time, P + Vector3.up, Vector3.back);
            Assert.AreEqual(runs + 3, gel.Runs, "duvarda 3 akıntı");

            // S-03 slide beat: two 0.04 m smears + 4 droplets (+ the body's film / drips when it is a zombie).
            _body = new GameObject("wp073b6_slider");
            _body.transform.position = new Vector3(P.x, 0f, P.z);
            int smears = gel.SmearsEmitted;
            drops = gel.DropsEmitted;
            gel.OnBeat(new EnemyCombatEventInfo(EnemyCombatEventKind.SpecialSlide, _body.transform, EnemyTier.Standard, 1, _body.transform.position,
                Vector3.forward, ArmorSlot.None, true, 0.9f));
            Assert.AreEqual(smears + 2, gel.SmearsEmitted, "zombi kayması: 2 sürtme izi");
            Assert.AreEqual(drops + 4, gel.DropsEmitted, "zombi kayması: 4 damlacık");

            // The player slides for 0.5 s: two parallel smears every 0.1 s while moving.
            GelSystem.PlayerOverride = _body.transform;
            smears = gel.SmearsEmitted;
            gel.OnUsage(Gel(SpecialUsageKind.PlayerSlideStarted, _body.transform.position, Vector3.forward));
            Assert.IsTrue(gel.PlayerSliding);
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f)
            {
                _body.transform.position += Vector3.forward * (4f * Time.deltaTime);
                yield return null;
            }

            gel.OnUsage(Gel(SpecialUsageKind.PlayerSlideEnded, _body.transform.position, Vector3.forward, 0f, 0.3f));
            Assert.IsFalse(gel.PlayerSliding);
            Assert.GreaterOrEqual(gel.SmearsEmitted - smears, 4, "oyuncu kayması: paralel izler");

            // Direct hit on a body: drips 2 / s for 3 s.
            drops = gel.DropsEmitted;
            var target = new DamageTarget { Root = _body.transform, Id = 7, Height = 1.8f };
            gel.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.Hit, "sw_gel_blob", P, Vector3.forward, 1.6f, 0f, 0, target, 1f, 1f, 0));
            yield return new WaitForSeconds(1.1f);
            Assert.GreaterOrEqual(gel.DropsEmitted - drops, 2, "gövdeden damlama");
        }

        [Test]
        public void OyuncuKorumasi_KameraIsini()
        {
            Vector3 cam = new Vector3(0f, 10f, -10f), chest = new Vector3(0f, 1.3f, 0f);
            Assert.AreEqual(0.5f, GelSystem.GuardFactor(cam, chest, Vector3.Lerp(cam, chest, 0.7f), 0.2f), "kamera–göğüs ışınında α × 0,5");
            Assert.AreEqual(1f, GelSystem.GuardFactor(cam, chest, Vector3.Lerp(cam, chest, 0.7f) + Vector3.right * 3f, 0.2f), "yanda: örtmez");
            Assert.AreEqual(1f, GelSystem.GuardFactor(cam, chest, chest + (chest - cam).normalized * 1f, 0.2f), "oyuncunun arkasında: örtmez");
        }

        // ------------------------------------------------------------------ model glow

        [Test]
        public void SilahParlamasi_DurumTablolari()
        {
            // P-07 AP-021 state table (core / rim / nozzle).
            Assert.AreEqual(new Vector3(0.6f, 0.5f, 0.3f), SpecialWeaponGlow.GelIntensities(SpecialWeaponGlow.GelState.Idle));
            Assert.AreEqual(new Vector3(1.5f, 1.0f, 1.0f), SpecialWeaponGlow.GelIntensities(SpecialWeaponGlow.GelState.Charged));
            Assert.AreEqual(new Vector3(4.5f, 3.0f, 3.0f), SpecialWeaponGlow.GelIntensities(SpecialWeaponGlow.GelState.Fire));
            Assert.AreEqual(new Vector3(0.15f, 0.12f, 0f), SpecialWeaponGlow.GelIntensities(SpecialWeaponGlow.GelState.Empty));
            Assert.AreEqual(SpecialWeaponGlow.GelState.Empty, SpecialWeaponGlow.GelStateOf(0, false, false, 5f));
            Assert.AreEqual(SpecialWeaponGlow.GelState.Empty, SpecialWeaponGlow.GelStateOf(4, false, true, 5f), "hazne değişimi = boş");
            Assert.AreEqual(SpecialWeaponGlow.GelState.Fire, SpecialWeaponGlow.GelStateOf(4, false, false, 0.1f));
            Assert.AreEqual(SpecialWeaponGlow.GelState.Charged, SpecialWeaponGlow.GelStateOf(4, true, false, 1f));
            Assert.AreEqual(SpecialWeaponGlow.GelState.Idle, SpecialWeaponGlow.GelStateOf(4, false, false, 1f));

            // Raiju: each cell is a third of the magazine; an empty cell is dark; the new cassette warms over 0.4 s.
            Assert.AreEqual(2f, SpecialWeaponGlow.RaijuCell(2, 3, 3, false, 0f));
            Assert.AreEqual(2f, SpecialWeaponGlow.RaijuCell(0, 1, 3, false, 0f));
            Assert.AreEqual(0f, SpecialWeaponGlow.RaijuCell(1, 1, 3, false, 0f));
            Assert.AreEqual(0f, SpecialWeaponGlow.RaijuCell(0, 0, 3, true, 1.0f), "kaset çıkarken sönük");
            Assert.AreEqual(1f, SpecialWeaponGlow.RaijuCell(0, 0, 3, true, 0.2f), 1e-4f, "yeni kaset ısınıyor");

            // Namekuji canister: 8 steps of the magazine.
            Assert.AreEqual(8, SpecialWeaponGlow.GelLevelStep(8, 8));
            Assert.AreEqual(4, SpecialWeaponGlow.GelLevelStep(4, 8));
            Assert.AreEqual(1, SpecialWeaponGlow.GelLevelStep(1, 8));
            Assert.AreEqual(0, SpecialWeaponGlow.GelLevelStep(0, 8));
            Assert.AreEqual(3, SpecialWeaponGlow.GelLevelStep(5, 16));
        }

        // ------------------------------------------------------------------ the old dressing is gone

        [Test]
        public void EskiSpecialVfx_Silindi_OlayYeniSistemlere()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Type dressing = typeof(VfxDressing);
            foreach (string m in new[] { "PlayKaguraRing", "PlayKaguraHit", "PlayOfudaCollapse", "PlayOfudaDart", "OfudaBurstAt", "PlayTideWell", "PlayTideImplode", "PlayTidePull", "PlayChainArc" })
            {
                Assert.IsNull(dressing.GetMethod(m, All), "VfxDressing." + m + " hâlâ var");
            }

            Assert.IsNull(dressing.GetProperty("SwSheets", All), "VfxDressing.SwSheets hâlâ var");
            Type director = typeof(HitFeedbackDirector);
            foreach (string m in new[] { "ReportShockRing", "ReportSeal", "ReportSealLink", "ReportOfudaNet", "ReportOfudaTrail" })
            {
                Assert.IsNull(director.GetMethod(m, All), "HitFeedbackDirector." + m + " hâlâ var");
            }

            foreach (string p in new[] { "TideColor", "KaguraColor", "OfudaColor" }) Assert.IsNull(director.GetProperty(p, All), p);
            Assembly runtime = dressing.Assembly;
            foreach (string t in new[] { "DD.Prototype.Feedback.Vfx.SpecialWeaponSheets", "DD.Prototype.Feedback.ShockRingEffect", "DD.Prototype.Feedback.SealGlyphEffect", "DD.Prototype.Feedback.VortexEffect" })
            {
                Assert.IsNull(runtime.GetType(t), t + " hâlâ derleniyor");
            }

            Type binder = typeof(VfxEventBinder);
            Assert.IsNull(binder.GetProperty("ChainArcs", All), "bağlayıcının ara eşlemesi kalktı");
            Assert.IsNull(binder.GetMethod("OnSpecialUsage", All), "bağlayıcı Special olaylarını dinlemez (ArcSystem / GelSystem dinler)");

            // The bus reaches the new systems (not the binder).
            ArcSystem arc = ArcSystem.Instance;
            GelSystem gel = GelSystem.Instance;
            arc.Rearm();
            gel.Rearm();
            int chains = arc.ChainArcs, puddles = gel.PuddlesStarted;
            SpecialWeaponUsageEvents.Publish(Hop(0, P, P + Vector3.forward * 3f));
            SpecialWeaponUsageEvents.Publish(Gel(SpecialUsageKind.FieldSpawned, new Vector3(P.x, 0f, P.z), Vector3.up, 1.6f, 25f, 91000));
            Assert.AreEqual(chains + 1, arc.ChainArcs, "Raiju Hit → ArcSystem");
            Assert.AreEqual(puddles + 1, gel.PuddlesStarted, "Namekuji FieldSpawned → GelSystem");
        }

        // ------------------------------------------------------------------ 0 B

        [UnityTest]
        public IEnumerator SpecialVfx_GcSifir_IsParcacigiKaydedici()
        {
            ArcSystem arc = ArcSystem.Instance;
            GelSystem gel = GelSystem.Instance;
            Ground();
            _body = new GameObject("wp073b6_gc_body");
            _body.transform.position = new Vector3(P.x, 0f, P.z);
            var target = new DamageTarget { Root = _body.transform, Id = 11, Height = 1.8f };
            var slide = new EnemyCombatEventInfo(EnemyCombatEventKind.SpecialSlide, _body.transform, EnemyTier.Standard, 1, _body.transform.position,
                Vector3.forward, ArmorSlot.None, true, 0.9f);
            Vector3 floor = new Vector3(P.x, 0f, P.z);

            // Handlers are called directly (the bus has other subscribers — audio, HUD — that are not this layer's).
            void Rep(int rep)
            {
                float now = Time.time;
                for (int i = 0; i < 8; i++) arc.OnUsage(Hop(i, P + Vector3.right * i, P + Vector3.right * (i + 2), target, 1.2f));
                arc.OnUsage(Raiju(SpecialUsageKind.ChargeStarted, Vector3.zero, 0.25f));
                arc.OnUsage(Raiju(SpecialUsageKind.ChargeCancelled, Vector3.zero));
                arc.OnUsage(Raiju(SpecialUsageKind.Fired, P));
                arc.OnUsage(Raiju(SpecialUsageKind.FieldEnded, P, 0.3f, 0));
                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.SelfShock, "sw_arc_orb", P, Vector3.forward * 2f, 2.5f, 0.5f, 10));
                for (int i = 0; i < 4; i++)
                {
                    gel.OnUsage(Gel(SpecialUsageKind.FieldSpawned, floor + Vector3.right * i, Vector3.up, 1.6f, 25f, 92000 + rep * 4 + i));
                    gel.OnUsage(Gel(SpecialUsageKind.FieldEnded, floor, Vector3.up, 0f, 0f, 92000 + rep * 4 + i));
                }

                gel.OnUsage(Gel(SpecialUsageKind.Fired, P, Vector3.forward));
                gel.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.Hit, "sw_gel_blob", P, Vector3.forward, 1.6f, 0f, 0, target, 1f, 1f, 0));
                gel.OnUsage(Gel(SpecialUsageKind.Liquefied, floor, Vector3.forward));
                gel.PlayImpact(now, floor, Vector3.up);
                gel.PlayImpact(now, P, Vector3.back);
                gel.OnBeat(slide);
                arc.Frame(now);
                gel.Frame(now, 0.016f);
            }

            // Warm: three reps + real frames (the pools wrap, the property-block arrays take their length).
            for (int rep = 0; rep < 3; rep++) Rep(rep);
            yield return null;
            yield return null;

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() =>
            {
                for (int rep = 3; rep < 103; rep++) Rep(rep);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "Special VFX yönetilen bellek ayırdı (bu iş parçacığı; yem " + (bait != null ? bait.Length : 0) + " B; 100 × (8 sıçrama + şarj + deşarj + sönme + 4 birikinti + şap + kayma + kare))");
        }
    }
}
