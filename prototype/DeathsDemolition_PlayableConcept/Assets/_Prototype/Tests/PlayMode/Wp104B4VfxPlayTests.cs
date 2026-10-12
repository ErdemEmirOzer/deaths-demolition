using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B4-1 (S-07) · the run tree's VFX in play.
    /// <list type="bullet">
    /// <item>Per-round style (TreeRuntime.SetTestStats, the TD record path, DrawTd's real instance data): Shot V + fire → the core at its
    /// WP-101 width (AD-B4 §1, ceiling −11.1 px), the halo + 20 px with no ceiling and V's HDR, the trail × 1.45, fire palette, no amber
    /// rim, the element halo covering the ground; neutral → WP-101's buffers to the value; S-02's SizeScale
    /// thickens; extra volleys are body streaks, the SMG rhythm counts main rounds, extras never hold more than 32 of the 48 records
    /// and past it merge into their pull's main record (×1.25); the marked chaos round (a real projectile with
    /// RunTreeHooks.TreeRoundMarkedChaos) turns violet with a sumi core; the whole style path allocates nothing.</item>
    /// <item>Procs (need the imported AP-041 tree rows — ArtAp041TreeIntake.Run; they fail loudly without them, that is the B4 gate):
    /// K6 B5 + the death's chain blast a frame later → B8, B5 + B8 ≤ 8, one nuke, no heat glint under Reduce Flashes, a tree blast's
    /// ExplosionSource.Tree draws nothing in ExplosionDirector, B8 at its damage area × the tier's event scale (AD-B4 M10) with the
    /// III ink ring's washi edge under it (B4-2 S3), S-04's zones (FireGround → GroundFireSystem as a tree zone with its #E0702A ember
    /// underdraw (M8, α .225) + B1 scorch, WP-073's own zone unchanged; VenomCloud for Seconds over its lifted underdraw (B4-2 S4)),
    /// the leech thread from the victim (Point, S-04 B5b) over its jade underdraw (M7), the ensō once per synergy per run in the
    /// ceremony slot, 0 B.</item>
    /// <item>The aura (B11 ring / colour / sparks and the ring's sumi underdraw (M6), III's light inner line (B4-2 S2), K7 with the B14
    /// dome; ShieldBreak / ShieldUp / ShieldHit) and the tree preview clock (timeScale 0: a diorama tracer and card move, a world card
    /// stays frozen).</item>
    /// <item>S-06's add-on glow: a real WeaponAttachMount (ar_1, DD_WeaponAttach) — E4 fire III and E5 V take 0.6 / 1.5 on their
    /// M_AP040_Glow slot through a MaterialPropertyBlock, × FlashScale, black while hidden, the electric capsule counted slotless,
    /// the per-frame poll 0 B (ignored while DD_WeaponAttach is not built).</item>
    /// </list>
    /// TD only; synthetic positions far from any scene (O); the tracer fixture is the WP-093 pattern (own instance, the global one parked).
    /// </summary>
    [TestFixture, Category("WP104"), Category("TD")]
    public sealed class Wp104B4VfxPlayTests
    {
        private static readonly Vector3 O = new Vector3(1400f, 0f, 1400f);
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<KeyValuePair<FieldInfo, object>> _savedDelegates = new List<KeyValuePair<FieldInfo, object>>();
        private GameObject _root;
        private TracerRibbonRenderer _tracer, _previousTracer;
        private bool _previousTracerEnabled, _previousInterimTrails, _hasTracerFixture;
        private CameraMode _previousMode;
        private float _previousFlash, _previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("WP104 B4 test root");
            _previousMode = CameraModeService.Current;
            _previousFlash = GameFeelSettings.FlashScale;
            _previousTimeScale = Time.timeScale;
            SetModeState(CameraMode.TopDown);
            GameFeelSettings.FlashScale = 1f;
            VfxClock.ResetForTests();
            TracerStyle.ResetForTests();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TreeVfxPreview.End();
            VfxClock.ResetForTests();
            TreeRuntime.ClearTestStats();
            TracerStyle.ResetForTests();
            ProjectileSystem.Clear();
            TreeVfxBinder binder = TreeVfxBinder.Instance;
            if (binder != null)
            {
                binder.Clear();
                binder.ResetCountersForTests();
            }

            if (_hasTracerFixture)
            {
                typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields).SetValue(null, _previousTracer);
                if (_previousTracer != null) _previousTracer.enabled = _previousTracerEnabled;
                ProjectileDriver.DrawInterimTrails = _previousInterimTrails;
            }

            _tracer = _previousTracer = null;
            _hasTracerFixture = false;
            if (_root != null) Object.Destroy(_root);
            for (int i = _savedDelegates.Count - 1; i >= 0; i--) _savedDelegates[i].Key.SetValue(null, _savedDelegates[i].Value);
            _savedDelegates.Clear();
            if (binder != null) binder.Rearm();
            GameFeelSettings.FlashScale = _previousFlash;
            Time.timeScale = _previousTimeScale;
            SetModeState(_previousMode);
            yield return null;
        }

        // ------------------------------------------------------------------ tracer style per round (no catalog needed)

        [Test]
        public void TdIz_KosuStili_AtisVAtes_CekirdekHaleRenkTavan_NotrdeWp101Birebir()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            VfxWeaponProfile ar = VfxWeaponProfiles.For(ClassicWeaponId.AssaultRifle);
            float core = TracerRibbonRenderer.TdCoreWidth(ar.Class);

            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, 5);
            s.SetBranchTier(TreeBranch.Element, 1);
            s.Elements = TreeElement.Fire;
            s.DominantElement = TreeElement.Fire;
            TreeRuntime.SetTestStats(s);
            int id = 2000;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(id, ClassicWeaponId.AssaultRifle, O)), Is.True);
            tracer.StopTdProjectile(id, O + Vector3.right * 3f);   // a drawable streak: head on the impact, tail running in
            tracer.TickTd(.01f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(core).Within(1e-5f), "AD-B4 §1: çekirdek kademe kanalı değil (Atış V'te de WP-101 eni)");
            Assert.That(TracerBuffer(tracer, "_extra")[0].z, Is.EqualTo(-TracerStyle.CoreQuadCapPx).Within(1e-4f), "parlak çekirdek ≤ 8 px (quad ≤ 11.1 px, 1080p)");
            Assert.That(TracerBuffer(tracer, "_extra")[0].w, Is.EqualTo(-core).Within(1e-6f), "tavanın tabanı: WP-101 eni (m), hiç incelmez");
            Vector4 hc = TracerBuffer(tracer, "_headColor")[0];
            Color fireHead = TracerStyle.CoreHead(TracerStyle.FireLook, Color.black);
            Assert.That(hc.x, Is.EqualTo(fireHead.r).Within(1e-4f));
            Assert.That(hc.z, Is.EqualTo(fireHead.b).Within(1e-4f), "ateş çekirdeği #FFE2B0 (K1: bir mermi bir renk)");
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(core * TracerRibbonRenderer.TdHaloScale).Within(1e-5f), "hale metre eni: WP-101 halesi");
            Assert.That(TracerBuffer(tracer, "_extra")[0], Is.EqualTo(new Vector4(0f, -TreeVfxLadder.HaloStepPx(5), 0f, 0f)), "AD-B4 §1: hale tavansız, V = WP-101 + 20 px (_Extra.y)");
            Assert.That(TracerBuffer(tracer, "_tailColor")[0].w, Is.EqualTo(TracerStyle.ElementHaloCover), "M5: element halesi zemini örter");
            Assert.That(RecordField<float>(tracer, id, "TrailScale"), Is.EqualTo(TreeVfxLadder.TrailScale(5)).Within(1e-5f), "iz boyu: Atış V ×1,45");
            Assert.That(TracerBuffer(tracer, "_span")[0].z, Is.EqualTo(0f), "element halesi kehribar kenar almaz (K1: karışım yok)");
            Color fireHalo = TracerStyle.Halo(TracerStyle.FireLook, Color.black) * (TracerRibbonRenderer.TdHaloHdr * TracerStyle.HaloHdr(TreeVfxLadder.HaloHdrV, 1f));   // S1: V ×1.5
            Assert.That(TracerBuffer(tracer, "_headColor")[0].y, Is.EqualTo(fireHalo.g).Within(1e-4f), "hale #FF5A1F");

            // neutral: WP-101's buffers to the value (the Wp093 / Wp101 expectations)
            tracer.Clear();
            TreeRuntime.ClearTestStats();
            int id2 = 3000;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(id2, ClassicWeaponId.AssaultRifle, O)), Is.True);
            tracer.StopTdProjectile(id2, O + Vector3.right * 3f);
            tracer.TickTd(.01f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(core).Within(1e-6f), "nötr: WP-101 genişliği");
            Assert.That(TracerBuffer(tracer, "_extra")[0], Is.EqualTo(new Vector4(TracerRibbonRenderer.TdCoreMinPx, 0f, 0f, 0f)), "nötr: WP-101 _extra birebir");
            Vector4 neutral = TracerBuffer(tracer, "_headColor")[0];
            Assert.That(neutral.x, Is.EqualTo(TracerRibbonRenderer.TdCoreHead.r).Within(1e-5f));
            Assert.That(neutral.y, Is.EqualTo(TracerRibbonRenderer.TdCoreHead.g).Within(1e-5f));
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_span")[0].z, Is.EqualTo(-TracerRibbonRenderer.TdHaloEdgeShare), "nötr hale kehribar kenarını korur");
            Assert.That(TracerBuffer(tracer, "_extra")[0], Is.EqualTo(Vector4.zero), "nötr hale: tavan yok, adım yok");
            Assert.That(TracerBuffer(tracer, "_tailColor")[0].w, Is.Zero, "nötr WP-101 halesi zemini örtmez");
            Assert.That(InvokeInk(tracer), Is.Zero, "işaretli mermi yok: mürekkep çizimi yok");
        }

        [Test]
        public void TdIz_SpecialEskiAtisi_SentetikKayit_OnizlemeDisindaStilAlmaz()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, 5);
            s.Elements = TreeElement.Venom;
            s.DominantElement = TreeElement.Venom;
            TreeRuntime.SetTestStats(s);
            VfxWeaponProfile ar = VfxWeaponProfiles.For(ClassicWeaponId.AssaultRifle);
            Assert.That(tracer.Emit(O, O + Vector3.forward * 30f, ar, false, false, false), Is.True);   // a Special's legacy hitscan shot
            tracer.TickTd(.05f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(TracerRibbonRenderer.TdCoreWidth(ar.Class)).Within(1e-6f), "ağaç Special atışını boyamaz");
            Assert.That(TracerBuffer(tracer, "_extra")[0].z, Is.EqualTo(0f));
            Assert.That(TracerBuffer(tracer, "_headColor")[0].x, Is.EqualTo(TracerRibbonRenderer.TdCoreHead.r).Within(1e-5f));
        }

        [Test]
        public void TdIz_SizeScale_EkVoleyGovde_SmgRitmiAnaMermide_48KayitBirlesme()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            TreeRuntime.SetTestStats(TreeStats.Neutral);

            // S-02 B2 SizeScale (Big Bore × a merged shot) on a neutral tree: thicker, the ceiling opens
            Assert.That(tracer.RecordTdProjectile(TdSpawn(100, ClassicWeaponId.AssaultRifle, O, 0, 1.25f)), Is.True);
            tracer.StopTdProjectile(100, O + Vector3.right * 3f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle) * 1.25f).Within(1e-5f));
            Assert.That(TracerBuffer(tracer, "_extra")[0].z, Is.LessThan(0f), "boyut büyüyünce tavan açılır");
            tracer.Clear();

            // SMG (every 2nd main round a tracer) with one extra volley per pull: the extras are body streaks and the main rounds
            // keep T B T B (the rhythm id skips every extra; WP-101's ProjectileId rhythm would give T T T)
            VfxWeaponProfile smg = VfxWeaponProfiles.For(ClassicWeaponId.AutoSmg);
            Assert.That(TracerRibbonRenderer.TdTracerEveryN(smg.Class), Is.EqualTo(2));
            var mains = new List<bool>();
            int pid = 200;
            for (int pull = 0; pull < 4; pull++)
            {
                Assert.That(tracer.RecordTdProjectile(TdSpawn(pid, ClassicWeaponId.AutoSmg, O + Vector3.forward * pull, 0, 1f)), Is.True);
                mains.Add(RecordField<bool>(tracer, pid, "Tracer"));
                pid++;
                Assert.That(tracer.RecordTdProjectile(TdSpawn(pid, ClassicWeaponId.AutoSmg, O + Vector3.forward * pull, 1, 1f)), Is.True);
                Assert.That(RecordField<bool>(tracer, pid, "Tracer"), Is.False, "ek voley mermisi gövde izi (S-02)");
                Assert.That(RecordField<bool>(tracer, pid, "Extra"), Is.True);
                pid++;
            }

            Assert.That(mains, Is.EqualTo(new[] { mains[0], !mains[0], mains[0], !mains[0] }), "SMG ritmi yalnız ana mermide (VolleyIndex 0)");
            tracer.Clear();

            // the 48 records: 8 pulls × (1 main + 4 extras) = 40, then a 9th pull's extras find the extras' 32 taken → they merge
            pid = 1000;
            for (int pull = 0; pull < 8; pull++)
            {
                Assert.That(tracer.RecordTdProjectile(TdSpawn(pid++, ClassicWeaponId.AssaultRifle, O + Vector3.back * pull, 0, 1f)), Is.True);
                for (int v = 1; v <= 4; v++) Assert.That(tracer.RecordTdProjectile(TdSpawn(pid++, ClassicWeaponId.AssaultRifle, O + Vector3.back * pull, v, 1f)), Is.True);
            }

            Assert.That(tracer.LiveTdTracers, Is.EqualTo(40));
            int main9 = pid;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(pid++, ClassicWeaponId.AssaultRifle, O + Vector3.left * 9f, 0, 1f)), Is.True, "ana mermi her zaman yer bulur");
            for (int v = 1; v <= 4; v++) Assert.That(tracer.RecordTdProjectile(TdSpawn(pid++, ClassicWeaponId.AssaultRifle, O + Vector3.left * 9f, v, 1f)), Is.False, "32 ek kayıt dolu: birleşir");
            Assert.That(tracer.TdVolleyMerged, Is.EqualTo(4));
            Assert.That(tracer.TdVolleyDropped, Is.Zero);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(41), "kayıt tavanı 48 aşılmaz");
            Assert.That(tracer.LiveTdVolleyRecords, Is.EqualTo(TracerStyle.VolleyRecordsMax));
            Assert.That(RecordField<int>(tracer, main9, "Merged"), Is.EqualTo(4));
            Assert.That(RecordField<float>(tracer, main9, "Core"), Is.EqualTo(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle) * TreeHooks.MergedSizeMax).Within(1e-5f),
                "birleşen ana mermi S-02 kuralıyla kalınlaşır (√(1 + n) ≤ 1.25)");
        }

        [Test]
        public void TdIz_IsaretliKaosMermisi_MorKenar_SumiCekirdek()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            ProjectileSystem.Clear();
            var shot = new ShotRequest
            {
                Origin = O + Vector3.up * 50f, Direction = Vector3.right, Damage = 1f, Range = 200f, PierceDamageFalloff = 1f,
                TreeFlags = RunTreeHooks.TreeRoundMarkedChaos   // S-02 B2: TreeVolleyPlan.Marked → the pull's main projectile
            };
            var launch = new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Speed = 100f, MaxRange = 200f, MaxLifetime = 10f,
                FalloffStart = 200f, FalloffEnd = 200f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
            Assert.That(ProjectileSystem.Spawn(in shot, in launch), Is.GreaterThanOrEqualTo(0));
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(1), "ProjectileEvents.Spawned → TD kaydı");
            int gen = ProjectileSystem.Get(0).Generation;
            Assert.That(RecordField<int>(tracer, gen, "Look"), Is.EqualTo(TracerStyle.MarkedLook), "K3 doğuşta: ilk TickTd'den önce isabet etse de mor");
            ProjectileSystem.Step(.05f);   // 5 m of flight: a drawable streak
            tracer.TickTd(.05f);
            Assert.That(RecordField<int>(tracer, gen, "Look"), Is.EqualTo(TracerStyle.MarkedLook), "K3: işaretli mermi");
            Assert.That(RecordField<bool>(tracer, gen, "Tracer"), Is.True, "işaretli mermi tam iz");

            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Color edge = TreeVfxLadder.Linear(TreeVfxLadder.ChaosEdge) * TracerStyle.MarkedEdgeHdr;
            Vector4 core = TracerBuffer(tracer, "_headColor")[0];
            Assert.That(core.z, Is.EqualTo(edge.b).Within(1e-4f), "eklenen çekirdek mor (#9B5CFF)");
            Assert.That(TracerBuffer(tracer, "_extra")[0].z, Is.LessThan(0f), "işaretli mermi tavanlı");
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_headColor")[0].z, Is.EqualTo(TreeVfxLadder.Linear(TreeVfxLadder.ChaosEdge).b * TracerRibbonRenderer.TdHaloHdr).Within(1e-4f), "mor kenar halesi");
            Assert.That(TracerBuffer(tracer, "_span")[0].z, Is.EqualTo(0f), "kehribar kenar yok");
            Assert.That(TracerBuffer(tracer, "_extra")[0].y, Is.EqualTo(-TreeVfxLadder.HaloStepPx(TreeVfxLadder.TierMax)), "M4: kademeden bağımsız V halesi (WP-101 + 20 px)");
            Assert.That(TracerBuffer(tracer, "_tailColor")[0].w, Is.EqualTo(TracerStyle.ElementHaloCover), "işaretli hale zemini örter (açık taşta okunur)");
            Assert.That(InvokeInk(tracer), Is.EqualTo(1), "sumi çekirdek (Transparent+11)");
            Assert.That(tracer.DrawnInk, Is.EqualTo(1));
            Vector4 ink = TracerBuffer(tracer, "_headColor")[0];
            Assert.That(Mathf.Max(ink.x, Mathf.Max(ink.y, ink.z)), Is.LessThan(.01f), "#12100F");
            Assert.That(ink.w, Is.EqualTo(TracerStyle.InkAlpha).Within(1e-5f));
            float coreWidth = RecordField<float>(tracer, gen, "Core");
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(coreWidth * TracerStyle.InkWidthShare).Within(1e-5f), "mor kenar iki yanda kalır");
            Assert.That(TracerBuffer(tracer, "_extra")[0].x, Is.EqualTo(TracerStyle.InkMinPx));
            Assert.That(TracerBuffer(tracer, "_extra")[0].z, Is.LessThan(0f), "mürekkep çekirdek tavanlı");
            Assert.That(TracerBuffer(tracer, "_extra")[0].w, Is.EqualTo(-RecordField<float>(tracer, gen, "Base") * TracerStyle.InkWidthShare).Within(1e-6f));
            ProjectileSystem.Clear();
        }

        [Test]
        public void TdIz_StilYolu_0B()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, 4);
            s.SetBranchTier(TreeBranch.Element, 3);
            s.Elements = TreeElement.Shock;
            s.DominantElement = TreeElement.Shock;
            TreeRuntime.SetTestStats(s);
            var draw = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), tracer, typeof(TracerRibbonRenderer).GetMethod("DrawTd", Private));
            var ink = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), tracer, typeof(TracerRibbonRenderer).GetMethod("DrawTdInk", Private));
            ProjectileSpawnInfo main = TdSpawn(7000, ClassicWeaponId.AssaultRifle, O, 0, 1.1f);
            ProjectileSpawnInfo extra = TdSpawn(7001, ClassicWeaponId.AssaultRifle, O, 1, 1.1f);
            // warm: the style cache, the arrays, the property block
            tracer.RecordTdProjectile(in main);
            tracer.RecordTdProjectile(in extra);
            tracer.StopTdProjectile(main.ProjectileId, main.Position + Vector3.right * 3f);
            tracer.StopTdProjectile(extra.ProjectileId, extra.Position + Vector3.right * 3f);
            tracer.TickTd(.01f);
            draw(0); draw(2); ink();
            tracer.Clear();
            int id = 8000;
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    ProjectileSpawnInfo m = TdSpawn(id++, ClassicWeaponId.AssaultRifle, O + Vector3.forward * i, 0, 1.1f);
                    ProjectileSpawnInfo e = TdSpawn(id++, ClassicWeaponId.AssaultRifle, O + Vector3.forward * i, 1, 1.1f);
                    tracer.RecordTdProjectile(in m);
                    tracer.RecordTdProjectile(in e);
                    tracer.StopTdProjectile(m.ProjectileId, m.Position + Vector3.right * 3f);   // drawable: the layers write and draw
                    tracer.StopTdProjectile(e.ProjectileId, e.Position + Vector3.right * 3f);
                    tracer.TickTd(.004f);
                    draw(0); draw(1); draw(2); ink();
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "stilli TD iz yolu ayırmasız");
        }

        /// <summary>
        /// WP-104 B4-2 B9 (S-07 F): a record that keeps a path (S-02 sh_cap's seeker: TickTd copies ProjectileSystem.TryGetPath; here
        /// SetTdPathForTests on a TD record of its own) lays its streak along it — consecutive DD/VFX Ribbon instances, the same width,
        /// colours and ceiling, _Span = (its tail / head distance along the streak, the rim z, −the streak's length) so the head bead and
        /// the tail run once over the whole bent streak; its length is the kept path up to 10 m (S-07 12.10 decision (a):
        /// TreeSeekTrail.SeekLength, the Shot ladder's ceiling); the halo and a body streak (an extra volley: what a real seeker is) ride
        /// the same path; on impact the tail runs in along it; a straight round's instance stays byte-identical beside it; 0 B.
        /// </summary>
        [Test]
        public void TdIz_B9_KivrilanArayici_YolBoyuncaSegmentler_DuzMermiBirebir_0B()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            TreeRuntime.SetTestStats(TreeStats.Neutral);

            // a straight round first (record 0): its instance alone, then beside a bending one
            int duz = 9200, arayan = 9201;
            Vector3 duzO = O + Vector3.forward * 4f;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(duz, ClassicWeaponId.AssaultRifle, duzO)), Is.True);
            tracer.StopTdProjectile(duz, duzO + Vector3.right * 3f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            string[] buffers = { "_head", "_tail", "_headColor", "_tailColor", "_span", "_extra" };
            var yalniz = new Vector4[buffers.Length];
            for (int b = 0; b < buffers.Length; b++) yalniz[b] = TracerBuffer(tracer, buffers[b])[0];

            // the bending round (record 1): spawned at O, turned 30° at P1 3 m on, stopped 1 m past it — a 4 m kept path
            Vector3 p1 = O + Vector3.right * 3f, turned = Quaternion.Euler(0f, -30f, 0f) * Vector3.right, head = p1 + turned;
            const float yol = 4f;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(arayan, ClassicWeaponId.AssaultRifle, O)), Is.True);
            tracer.StopTdProjectile(arayan, head);
            var path = new[] { O, p1 };
            Assert.That(tracer.SetTdPathForTests(arayan, path, path.Length), Is.True);
            Assert.That(RecordField<int>(tracer, arayan, "SeekPoints"), Is.EqualTo(2));
            Assert.That(tracer.LiveTdSeekPaths, Is.EqualTo(1));
            int bent0 = tracer.TdBentInstances;

            Assert.That(DrawTd(tracer, 0), Is.EqualTo(3), "düz mermi 1 + kıvrılan iz 2 segment (bütün yol: kıvrımdan namluya)");
            for (int b = 0; b < buffers.Length; b++) Assert.That(TracerBuffer(tracer, buffers[b])[0], Is.EqualTo(yalniz[b]), "düz mermi birebir: " + buffers[b]);
            Vector4[] h = TracerBuffer(tracer, "_head"), t = TracerBuffer(tracer, "_tail"), s = TracerBuffer(tracer, "_span");
            Assert.That(((Vector3)h[1] - head).magnitude, Is.LessThan(1e-4f), "baş mermide");
            Assert.That(((Vector3)t[1] - p1).magnitude, Is.LessThan(1e-4f), "ilk segment kıvrımda biter");
            Assert.That(((Vector3)h[2] - p1).magnitude, Is.LessThan(1e-4f), "segmentler ardışık");
            Assert.That(((Vector3)t[2] - O).magnitude, Is.LessThan(1e-4f), "karar (a): iz = tutulan yol (≤ 10 m), namluya kadar");
            Assert.That(s[1].x, Is.EqualTo(yol - 1f).Within(1e-4f));
            Assert.That(s[1].y, Is.EqualTo(yol).Within(1e-4f), "baş: izin tüm boyu");
            Assert.That(s[2].x, Is.EqualTo(0f).Within(1e-4f), "kuyruk ucu t = 0");
            Assert.That(s[2].y, Is.EqualTo(s[1].x).Within(1e-6f), "t kesintisiz");
            Assert.That(s[1].w, Is.EqualTo(-yol).Within(1e-4f), "DD/VFX Ribbon: _Span.w < 0 = bütün iz boyunca t");
            Assert.That(s[2].w, Is.EqualTo(-yol).Within(1e-4f));
            Assert.That(t[1].w, Is.EqualTo(TracerRibbonRenderer.TdTracerBead / yol).Within(1e-5f), "baş boncuğu bütün izin payı");
            Assert.That(t[2].w, Is.EqualTo(t[1].w));
            Assert.That(h[1].w, Is.EqualTo(h[2].w), "aynı en");
            Assert.That(TracerBuffer(tracer, "_headColor")[1], Is.EqualTo(TracerBuffer(tracer, "_headColor")[2]), "aynı renk");
            Assert.That(TracerBuffer(tracer, "_extra")[1], Is.EqualTo(TracerBuffer(tracer, "_extra")[2]), "aynı taban / tavan");
            Assert.That(tracer.TdBentInstances - bent0, Is.EqualTo(1), "kayıt başına ek instance: segment − 1");

            // the halo rides the same path, its amber rim (z) kept
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(3));
            Assert.That(TracerBuffer(tracer, "_span")[1].z, Is.EqualTo(-TracerRibbonRenderer.TdHaloEdgeShare), "nötr hale kenarı");
            Assert.That(TracerBuffer(tracer, "_span")[2].w, Is.EqualTo(-yol).Within(1e-4f));

            // on impact the tail runs in along the path: half the tail-out, half the streak (2 m: still past the turn)
            tracer.TickTd(TracerRibbonRenderer.TdTailOutSeconds * .5f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(3));
            Assert.That(TracerBuffer(tracer, "_span")[1].w, Is.EqualTo(-yol * .5f).Within(1e-4f), "kuyruk yol boyunca içeri akar");
            Assert.That(((Vector3)TracerBuffer(tracer, "_tail")[2] - (p1 + Vector3.left)).magnitude, Is.LessThan(1e-4f), "kıvrımdan 1 m geri");

            // a body streak (an extra volley: what a real seeker is) 0.3 m past the turn: its whole 3.3 m path, bent
            tracer.Clear();
            int govde = 9300;
            Vector3 head2 = p1 + turned * .3f;
            Assert.That(tracer.RecordTdProjectile(TdSpawn(govde, ClassicWeaponId.AssaultRifle, O, 1, 1f)), Is.True);
            tracer.StopTdProjectile(govde, head2);
            Assert.That(tracer.SetTdPathForTests(govde, path, path.Length), Is.True);
            Assert.That(DrawTd(tracer, 0), Is.Zero, "ek voley gövde izidir (çekirdek yok)");
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(2), "gövde izi kıvrımı geçer");
            Assert.That(((Vector3)TracerBuffer(tracer, "_tail")[1] - O).magnitude, Is.LessThan(1e-4f), "gövde izi de bütün yol");
            Assert.That(TracerBuffer(tracer, "_span")[0].w, Is.EqualTo(-3.3f).Within(1e-4f));
            Assert.That(TracerBuffer(tracer, "_tail")[0].w, Is.Zero, "gövde izinde boncuk yok");

            // a kept path longer than 10 m: its newest 10 m (TreeVfxLadder.TrailMaxMetres, the Shot ladder's ceiling)
            tracer.Clear();
            int uzun = 9310;
            Vector3 p20 = O + Vector3.right * 20f, head3 = p20 + turned;
            var farPath = new[] { O, p20 };
            Assert.That(tracer.RecordTdProjectile(TdSpawn(uzun, ClassicWeaponId.AssaultRifle, O)), Is.True);
            tracer.StopTdProjectile(uzun, head3);
            Assert.That(tracer.SetTdPathForTests(uzun, farPath, farPath.Length), Is.True);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(2));
            Assert.That(TracerBuffer(tracer, "_span")[0].w, Is.EqualTo(-TreeVfxLadder.TrailMaxMetres).Within(1e-4f), "en çok 10 m");
            Assert.That(((Vector3)TracerBuffer(tracer, "_tail")[1] - (O + Vector3.right * 11f)).magnitude, Is.LessThan(1e-3f), "1 m dönüş + 9 m geri");

            // 0 B: the path read-in and the lay-out allocate nothing
            var draw = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), tracer, typeof(TracerRibbonRenderer).GetMethod("DrawTd", Private));
            var ink = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), tracer, typeof(TracerRibbonRenderer).GetMethod("DrawTdInk", Private));
            draw(0); draw(1); draw(2); ink();
            tracer.Clear();
            int id = 9400;
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    ProjectileSpawnInfo e = TdSpawn(id++, ClassicWeaponId.AssaultRifle, O, 1, 1f);
                    tracer.RecordTdProjectile(in e);
                    tracer.StopTdProjectile(e.ProjectileId, head2);
                    tracer.SetTdPathForTests(e.ProjectileId, path, path.Length);
                    tracer.TickTd(.004f);
                    draw(0); draw(1); draw(2); ink();
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "kıvrılan iz yolu ayırmasız");
        }

        /// <summary>
        /// AD A5 on the TD camera's scale (≈ 95–100 px/m, 1080p; the Wp101TdIzZeminTests set-up without post-processing, so the core's
        /// bright part is the pixels whose brightest channel saturates, ≥ 254): an AR tracer at Shot V + fire draws a bright core of
        /// 3–8 px (the quad clamped at max(11.1 px, its WP-101 width)), and the sniper's — already ≈ 11 px in WP-101 — neither grows
        /// nor shrinks (the ceiling's floor is its own width in metres). Writes the numbers to the log (and a5.json to DD_TD_A5_DIR).
        /// </summary>
        [UnityTest]
        public IEnumerator A5_AktifCekirdek_TdKamerada_En8px_SniperIncelmez()
        {
            LogAssert.ignoreFailingMessages = true;
            const int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp104A5" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var camGo = new GameObject("WP104-A5-camera");
            camGo.transform.SetParent(_root.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            try
            {
                cam.transform.SetPositionAndRotation(O + new Vector3(0f, 20f, 0f), Quaternion.Euler(90f, 0f, 0f));
                cam.allowHDR = true;
                cam.fieldOfView = 31.7f;   // 1080 / (2 · 20 · tan 15.85°) ≈ 95 px/m
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.targetTexture = rt;
                cam.enabled = false;
                yield return null;

                TracerRibbonRenderer tracer = AttachTdTracer();
                float pxPerM = (Px(cam, O + new Vector3(1f, 1f, 0f), W, H) - Px(cam, O + Vector3.up, W, H)).magnitude;
                Assert.That(pxPerM, Is.InRange(85f, 105f), "oyunun TD ölçeği");

                TreeStats v = TreeStats.Neutral;
                v.SetBranchTier(TreeBranch.Shot, 5);
                v.SetBranchTier(TreeBranch.Element, 5);
                v.Elements = TreeElement.Fire;
                v.DominantElement = TreeElement.Fire;
                int[] styled = Measure(cam, rt, read, tracer, v, 9001, W, H);
                yield return null;   // a new frame: the queued draws are gone
                int[] neutral = Measure(cam, rt, read, tracer, TreeStats.Neutral, 9101, W, H);
                Debug.Log("[DD-TEST] wp104b4.a5 pxPerM=" + pxPerM.ToString("0.#") + " ar V/I=" + styled[0] + "/" + neutral[0] + " sr V/I=" + styled[1] + "/" + neutral[1]);
                string dir = Environment.GetEnvironmentVariable("DD_TD_A5_DIR");
                if (!string.IsNullOrEmpty(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a5.json"), "{\"pxPerMetre\": " + pxPerM.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                        + ", \"arV\": " + styled[0] + ", \"arI\": " + neutral[0] + ", \"srV\": " + styled[1] + ", \"srI\": " + neutral[1] + "}\n");
                }

                Assert.That(styled[0], Is.InRange(3, (int)TracerStyle.CoreCapPx), "A5: AR çekirdeği V'te ≤ 8 px (parlak, eşikli)");
                Assert.That(neutral[0], Is.GreaterThanOrEqualTo(3), "A5 alt sınır (nötr)");
                Assert.That(styled[0], Is.GreaterThanOrEqualTo(neutral[0]), "kademe çekirdeği daraltmaz");
                Assert.That(Mathf.Abs(styled[1] - neutral[1]), Is.LessThanOrEqualTo(1), "sniper: WP-101 eni korunur (ne büyür ne incelir)");
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(read);
            }
        }

        /// <summary>An AR (z −2) and a sniper (z +2) tracer in <paramref name="stats"/>'s look, layer 0 only, one render: the bright
        /// width (brightest channel ≥ 254: white-hot, saturated) across each streak 0.1 m behind its head.</summary>
        private static int[] Measure(Camera cam, RenderTexture rt, Texture2D read, TracerRibbonRenderer tracer, TreeStats stats, int id, int w, int h)
        {
            tracer.Clear();
            TreeRuntime.SetTestStats(in stats);
            Vector3 arHead = O + new Vector3(0f, 1f, -2f), srHead = O + new Vector3(0f, 1f, 2f);
            Assert.That(tracer.RecordTdProjectile(TdSpawn(id, ClassicWeaponId.AssaultRifle, arHead + Vector3.left * 4f)), Is.True);
            Assert.That(tracer.RecordTdProjectile(TdSpawn(id + 1, ClassicWeaponId.Sr1, srHead + Vector3.left * 4f)), Is.True);
            tracer.StopTdProjectile(id, arHead);
            tracer.StopTdProjectile(id + 1, srHead);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(2));
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            Color32[] px = read.GetPixels32();
            return new[] { Bright(cam, px, arHead + Vector3.left * .1f, w, h), Bright(cam, px, srHead + Vector3.left * .1f, w, h) };
        }

        private static int Bright(Camera cam, Color32[] px, Vector3 at, int w, int h)
        {
            Vector2 c = Px(cam, at, w, h);
            int n = 0;
            for (int dy = -30; dy <= 30; dy++)
            {
                int x = (int)c.x, y = (int)c.y + dy;
                if (y < 0 || y >= h || x < 0 || x >= w) continue;
                Color32 k = px[y * w + x];
                if (Mathf.Max(k.r, Mathf.Max(k.g, k.b)) >= 254) n++;
            }

            return n;
        }

        private static Vector2 Px(Camera cam, Vector3 world, int w, int h)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            return new Vector2(v.x * w, v.y * h);
        }

        // ------------------------------------------------------------------ procs (need the imported tree rows)

        [UnityTest]
        public IEnumerator Proc_PatlayanMermi_K6_Kapak_TekNukleer_AgacKaynagiCizilmez_0B()
        {
            TreeVfxBinder binder = Binder();
            yield return null;
            Vector3 a = O + Vector3.right * 10f;
            TreeEvents.RaiseProc(Info(TreeProcKind.ExplosiveRound, a, 2f, 3));
            Assert.That(binder.PlayedByKind(TreeProcKind.ExplosiveRound), Is.EqualTo(1));
            Assert.That(binder.LiveBlasts, Is.EqualTo(1), "B5");
            Assert.That(binder.LiveSumi, Is.EqualTo(1), "III: mürekkep şok halkası");

            yield return null;   // S-04's queue: the killed body's chain blast is presented the next frame (generation 1)
            TreeProcInfo chain = Info(TreeProcKind.ChainBlast, a + Vector3.right * .3f, 3f, 2);
            chain.Depth = 1;
            TreeEvents.RaiseProc(chain);
            Assert.That(binder.Replaced, Is.EqualTo(1), "K6: aynı isabette B5 + B8 (bir kare sonra) → yalnız B8");
            Assert.That(binder.LiveBlasts, Is.EqualTo(1));
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B8Chain1), "kuşak 1 = ilk B8 varyantı, tam boy (Depth − 1)");

            binder.Clear();
            yield return null;
            for (int i = 0; i < 12; i++) TreeEvents.RaiseProc(Info(TreeProcKind.ExplosiveRound, O + new Vector3(i * 8f, 0f, 40f), 2f, 3));
            Assert.That(binder.LiveBlasts, Is.LessThanOrEqualTo(8), "A9: B5 + B8 ≤ 8");
            for (int f = 0; f < 4; f++)
            {
                yield return null;
                Assert.That(binder.LiveBlasts, Is.LessThanOrEqualTo(8));
            }

            binder.Clear();
            yield return null;   // a frame whose spawn budget the queue did not spend
            for (int i = 0; i < 3; i++) TreeEvents.RaiseProc(Info(TreeProcKind.MiniNuke, O + new Vector3(0f, 0f, 80f + i * 20f), 5f, 3));
            Assert.That(binder.LiveNukes, Is.EqualTo(1), "A9: ekranda tek nükleer");

            // S-02 B2: a tree blast through Explode(…, ExplosionSource.Tree) is its proc's only — ExplosionDirector draws nothing
            ExplosionDirector director = ExplosionDirector.Instance;
            Assert.That(director, Is.Not.Null);
            int presented = director.Presentations, tree = director.TreePresentations;
            Assert.That(director.Play(O + Vector3.forward * 160f, 3f, -1, ExplosionSource.Tree), Is.True, "true: HitFeedbackDirector'ün BurstEffect yedeği de çizmez");
            Assert.That(director.TreePresentations, Is.EqualTo(tree + 1));
            Assert.That(director.Presentations, Is.EqualTo(presented), "kart / halka / ışık / yanık yok");

            // 0 B on the proc path (pool warm, rows resolved)
            binder.Clear();
            yield return null;
            yield return null;
            TreeProcInfo crit = Info(TreeProcKind.Crit, O + Vector3.forward * 120f, 0f, 2);
            TreeProcInfo blast = Info(TreeProcKind.ExplosiveRound, O + Vector3.forward * 140f, 2f, 3);
            TreeEvents.RaiseProc(crit);
            TreeEvents.RaiseProc(blast);
            binder.Clear();
            Assert.That(() =>
            {
                TreeEvents.RaiseProc(crit);
                TreeEvents.RaiseProc(blast);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ağaç proc yolu ayırmasız");
        }

        [UnityTest]
        public IEnumerator ReduceFlashes_NukleerIsiParlamasiYok()
        {
            TreeVfxBinder binder = Binder();
            yield return null;
            GameFeelSettings.FlashScale = .4f;
            TreeEvents.RaiseProc(Info(TreeProcKind.MiniNuke, O + Vector3.left * 30f, 5f, 3));
            Assert.That(binder.LiveNukes, Is.EqualTo(1), "mantar dumanı kalır");
            Assert.That(binder.LiveGlints, Is.EqualTo(0), "AD §2: ısı parlaması kalkar");
        }

        /// <summary>AD-B4 M10: B8 shows its damage area as B5 does (S-04's ChainBlast Radius is the sphere its queue damages) — the first
        /// generation's card is 2R × the tier's event scale (R 3 m, III: 5.4 m; the sheet's ring ≈ 0.9 of it = 4.3–5.4 m), × 0.85 per
        /// later generation, and the III sumi ring follows the card (× 1.2 within the T3 clamp) instead of twice the old 1.8 m card.</summary>
        [UnityTest]
        public IEnumerator Proc_ZincirPatlama_B8HasarAlaniBoyu_KusakBasina085()
        {
            TreeVfxBinder binder = Binder();
            yield return null;
            float authored = binder.Row(TreeVfxRow.B8Chain1).WorldSizeMetres;
            Assert.That(TreeVfxLadder.ChainCardSize(3f, 3, 0, authored), Is.EqualTo(2f * 3f * .9f).Within(1e-4f), "R 3 m, III: 2R × 0.9 = 5.4 m");
            Assert.That(TreeVfxLadder.ChainCardSize(0f, 3, 0, authored), Is.EqualTo(authored * .9f).Within(1e-4f), "yarıçapsız: yazılı boy × merdiven");

            // tier II has no sumi layer: the B8 card is the proc's last spawn
            TreeProcInfo chain = Info(TreeProcKind.ChainBlast, O + new Vector3(-60f, 1f, 0f), 3f, 2);
            chain.Depth = 1;   // S-04: a kill's first chain generation
            Assert.That(binder.Play(in chain), Is.True);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B8Chain1));
            Assert.That(binder.LastSpawned.transform.GetChild(0).localScale.x, Is.EqualTo(2f * 3f * TreeVfxLadder.ImpactScale(2)).Within(1e-3f), "kuşak 1: 2R × 0.75");
            chain.Point += Vector3.forward * 20f;
            chain.Depth = 2;
            binder.Play(in chain);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B8Chain2));
            Assert.That(binder.LastSpawned.transform.GetChild(0).localScale.x, Is.EqualTo(2f * 3f * TreeVfxLadder.ImpactScale(2) * .85f).Within(1e-3f), "kuşak 2: × 0.85");

            // III: the ink-shock ring follows the 5.4 m card (B5's rule)
            binder.Clear();
            yield return null;
            TreeProcInfo third = Info(TreeProcKind.ChainBlast, O + new Vector3(-60f, 1f, 40f), 3f, 3);
            third.Depth = 1;
            binder.Play(in third);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.T3InkShock), "III: sumi katmanı");
            float t3 = binder.Row(TreeVfxRow.T3InkShock).WorldSizeMetres;
            Assert.That(binder.LastSpawned.transform.GetChild(0).localScale.x, Is.EqualTo(Mathf.Clamp(5.4f * 1.2f, t3 * .5f, t3 * 1.5f)).Within(1e-3f), "sumi halkası kartı izler");
            Assert.That(binder.LiveBlasts, Is.EqualTo(1));

            // AD-B4 S3 (B4-2): the ink ring's washi edge — its own procedural rim row on the ink's card, frames and life, 5 mm under it
            FlipbookEffect ink = binder.LastSpawned, rim = binder.LastWashi;
            Assert.That(binder.WashiRow, Is.Not.Null, "S3: washi kenarı satırı (T3 malzemesinin kopyası + prosedürel doku)");
            Assert.That(rim, Is.Not.Null, "S3: mürekkep halkasının washi kenarı");
            Assert.That(rim, Is.Not.SameAs(ink));
            Assert.That(rim.Row, Is.SameAs(binder.WashiRow));
            Assert.That(rim.Row.Material, Is.Not.SameAs(ink.Row.Material), "kendi dokusu: koyu ön-çarpımlı T3 tint ile açılmaz");
            Assert.That(rim.Row.Material.shader, Is.SameAs(ink.Row.Material.shader), "aynı DD/VFX Flipbook yolu");
            Assert.That(rim.Row.FramesUsed, Is.EqualTo(ink.Row.FramesUsed));
            Assert.That(rim.Row.Fps, Is.EqualTo(ink.Row.Fps).Within(1e-5f));
            Assert.That(rim.LifeSeconds, Is.EqualTo(ink.LifeSeconds).Within(1e-4f), "halkayla aynı ömür ve kare");
            Assert.That(rim.transform.GetChild(0).localScale.x, Is.EqualTo(ink.transform.GetChild(0).localScale.x).Within(1e-4f), "halkanın kartı");
            Assert.That(rim.AlphaCap, Is.EqualTo(TreeVfxLadder.InkWashiAlpha).Within(1e-5f), "α ≤ 0.40");
            Assert.That(rim.transform.position.y, Is.LessThan(ink.transform.position.y), "halkanın altında: önce çizilir");
            var rimBlock = new MaterialPropertyBlock();
            rim.transform.GetChild(0).GetComponent<MeshRenderer>().GetPropertyBlock(rimBlock);
            Color cream = TreeVfxLadder.InkWashiTint;
            cream.a = TreeVfxLadder.InkWashiAlpha;
            var creamProbe = new MaterialPropertyBlock();
            creamProbe.SetColor("_Tint", cream);   // the runtime row's own tint is white
            Assert.That(rimBlock.GetColor("_Tint").g, Is.EqualTo(creamProbe.GetColor("_Tint").g).Within(1e-3f), "washi kreması #F3EEE3");
            Assert.That(InkWashiSheet.RimAlpha(7, InkWashiSheet.OuterHalfMax(7) - .03f), Is.EqualTo(0f), "mürekkep bandının içinde kenar yok");
            Assert.That(InkWashiSheet.RimAlpha(7, InkWashiSheet.Outer10(7)), Is.GreaterThan(.5f), "mürekkebin dış kenarında");
            Assert.That(InkWashiSheet.RimAlpha(15, InkWashiSheet.Outer10(15)), Is.LessThan(.05f), "mürekkep sönünce kenar da söner");
        }

        [UnityTest]
        public IEnumerator Aura_Seviye2_HalkaSatiriRenkBoy_KalkandaKivilcimYarim()
        {
            TreeVfxBinder binder = Binder();
            var mannequin = new GameObject("WP104 aura anchor");
            mannequin.transform.SetParent(_root.transform, false);
            mannequin.transform.position = O + Vector3.back * 30f;
            TreeStats s = TreeStats.Neutral;
            s.AuraLevel = 2;
            TreeVfxPreview.Begin(in s, mannequin.transform);
            yield return null;
            yield return null;
            TreeAuraVfx aura = binder.Aura;
            Assert.That(aura.Level, Is.EqualTo(2));
            Assert.That(aura.RingVisible, Is.True);
            Assert.That(aura.RingRow, Is.SameAs(binder.Row(TreeVfxRow.B11Ring2)), "aura II halkası");
            Assert.That(aura.RingScale, Is.EqualTo(binder.Row(TreeVfxRow.B11Ring2).WorldSizeMetres).Within(1e-4f), "sayfanın yazılı boyu = r 1.3 m");
            Assert.That(aura.SparkAlphaCap, Is.EqualTo(1f));
            Assert.That(aura.SmokeVisible, Is.False, "sumi dumanı yalnız III");

            // AD-B4 M6: the ring once more under itself — the same sheet and frame, sumi, α ≤ 0.35, × 1.04, drawn first
            Assert.That(aura.UnderVisible, Is.True, "M6: halkanın sumi alt çizimi");
            Assert.That(aura.UnderRow, Is.SameAs(aura.RingRow), "aynı doku");
            Assert.That(aura.UnderRenderer.sharedMaterial, Is.SameAs(aura.RingRenderer.sharedMaterial), "aynı malzeme (yeni varlık yok)");
            Assert.That(aura.UnderFrame, Is.EqualTo(aura.RingFrame), "aynı kare");
            Assert.That(aura.UnderScale, Is.EqualTo(aura.RingScale * TreeVfxLadder.AuraUnderScale).Within(1e-4f), "× 1.04");
            var under = new MaterialPropertyBlock();
            aura.UnderRenderer.GetPropertyBlock(under);
            Color ink = under.GetColor("_Tint");
            Assert.That(Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b)), Is.LessThan(.08f), "sumi #12100F");
            Assert.That(ink.a, Is.EqualTo(TreeVfxLadder.AuraUnderCoverage).Within(1e-5f), "örtü ölçeği (shader fade)");
            Assert.That(under.GetFloat("_AlphaCap"), Is.EqualTo(TreeVfxLadder.AuraUnderAlpha).Within(1e-5f), "α ≤ 0.35");
            Assert.That(aura.UnderRenderer.bounds.center.y, Is.LessThan(aura.RingRenderer.bounds.center.y), "halkanın altında: üstteki her kameradan uzak, önce çizilir");
            Assert.That(aura.LineVisible, Is.False, "S2: açık iç çizgi yalnız III");
            var late = (Action)Delegate.CreateDelegate(typeof(Action), aura, typeof(TreeAuraVfx).GetMethod("LateUpdate", Private));
            late();
            Assert.That(() => { for (int i = 0; i < 50; i++) late(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "aura + alt çizim kare yolu ayırmasız");

            s.ShieldShare = .25f;
            TreeVfxPreview.SetStats(in s);
            yield return null;
            Assert.That(aura.DomeVisible, Is.True, "B14 kubbe");
            Assert.That(aura.SparkAlphaCap, Is.EqualTo(TreeVfxLadder.AuraSparkDamp), "K7: kalkan açıkken kıvılcım %50");
            GameFeelSettings.FlashScale = .4f;
            yield return null;
            Assert.That(aura.SparkAlphaCap, Is.EqualTo(TreeVfxLadder.AuraSparkDamp * TreeVfxLadder.AuraSparkDamp).Within(1e-5f), "RF × kalkan");

            // S-04 12.10: ShieldHit 26 = the B14 wave, ShieldBreak 16 hides the dome, ShieldUp 25 brings it back (no timer)
            Vector3 hero = mannequin.transform.position;
            Assert.That(binder.Play(Info(TreeProcKind.ShieldHit, hero, 0f, 2)), Is.True);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B14Hit), "emilen vuruş dalgası");
            binder.Play(Info(TreeProcKind.ShieldBreak, hero, 0f, 2));
            yield return null;
            Assert.That(aura.DomeVisible, Is.False, "kırılınca kubbe gider");
            for (int i = 0; i < 30; i++) yield return null;
            Assert.That(aura.DomeVisible, Is.False, "zamanlayıcı yok: ShieldUp gelene dek kapalı");
            binder.Play(Info(TreeProcKind.ShieldUp, hero, 0f, 2));
            yield return null;
            Assert.That(aura.DomeVisible, Is.True, "ShieldUp: kubbe geri");

            s.AuraLevel = 3;
            TreeVfxPreview.SetStats(in s);
            yield return null;
            Assert.That(aura.Level, Is.EqualTo(3));
            Assert.That(aura.RingRow, Is.SameAs(binder.Row(TreeVfxRow.B11Ring3)), "aura III halkası");
            Assert.That(aura.SmokeVisible, Is.True, "III: sumi dumanı");
            Assert.That(aura.UnderRow, Is.SameAs(aura.RingRow), "alt çizim seviyeyle döner");

            // AD-B4 S2 (B4-2): III's light inner line — a procedural annulus on its own row, #FF605C α 0.9, still, over the ring
            Assert.That(aura.LineVisible, Is.True, "S2: III açık iç çizgi");
            Assert.That(aura.LineRow, Is.Not.Null);
            Assert.That(aura.LineRenderer.sharedMaterial, Is.Not.SameAs(aura.RingRenderer.sharedMaterial), "kendi dokusu (halka sayfası değil)");
            Assert.That(aura.LineRenderer.sharedMaterial.shader, Is.SameAs(aura.RingRenderer.sharedMaterial.shader), "halkanın shader yolu");
            Assert.That(aura.LineScale, Is.EqualTo(TreeAuraVfx.LineQuadSize(3)).Within(1e-4f));
            Assert.That(TreeAuraVfx.LineQuadSize(3) * .5f * TreeAuraVfx.LineTexRadius,
                Is.EqualTo(TreeVfxLadder.AuraRadius(3) * TreeVfxLadder.AuraLineRadiusShare).Within(1e-4f), "bandın iç kenarında (r ≈ 1.31 m)");
            var lineBlock = new MaterialPropertyBlock();
            aura.LineRenderer.GetPropertyBlock(lineBlock);
            Assert.That(lineBlock.GetFloat("_AlphaCap"), Is.EqualTo(TreeVfxLadder.AuraLineAlpha).Within(1e-5f), "α 0.9");
            Assert.That(lineBlock.GetFloat("_Frame"), Is.EqualTo(0f), "durağan çizgi (tek kare)");
            Color coral = TreeVfxLadder.AuraLineTint;
            coral.a = TreeVfxLadder.AuraLineAlpha;
            var coralProbe = new MaterialPropertyBlock();
            coralProbe.SetColor("_Tint", coral);
            Assert.That(lineBlock.GetColor("_Tint").g, Is.EqualTo(coralProbe.GetColor("_Tint").g).Within(1e-3f), "#FF605C");
            Assert.That(aura.LineRenderer.bounds.center.y, Is.GreaterThan(aura.RingRenderer.bounds.center.y), "halkanın üstünde, kıvılcımların altında");
            Assert.That(() => { for (int i = 0; i < 50; i++) late(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "III + iç çizgi kare yolu ayırmasız");

            s.AuraLevel = 0;
            TreeVfxPreview.SetStats(in s);
            yield return null;
            Assert.That(aura.RingVisible || aura.UnderVisible || aura.LineVisible, Is.False, "aura yok: halka, alt çizim, iç çizgi yok");
        }

        [UnityTest]
        public IEnumerator Sinerji_KosudaBirKez_TorenSlotuBirTane()
        {
            TreeVfxBinder binder = Binder();
            var mannequin = new GameObject("WP104 ensō anchor");
            mannequin.transform.SetParent(_root.transform, false);
            mannequin.transform.position = O + Vector3.forward * 60f;
            TreeVfxPreview.Begin(TreeStats.Neutral, mannequin.transform);
            yield return null;
            TreeProcInfo first = Info(TreeProcKind.SynergyFirstTrigger, mannequin.transform.position, 0f, 1);
            first.Synergy = TreeSynergy.FrozenGravity;
            binder.Play(in first);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.X8Enso));
            Assert.That(binder.LiveCeremonies, Is.EqualTo(1));
            binder.Play(in first);
            Assert.That(binder.CeremoniesPlayed, Is.EqualTo(1), "aynı sinerji koşuda bir kez");
            binder.Play(Info(TreeProcKind.UndyingOath, mannequin.transform.position, 0f, 5));
            Assert.That(binder.CeremoniesDropped, Is.EqualTo(1), "ekran başına tek tören: sinerji kazanır");
        }

        [UnityTest]
        public IEnumerator Zon_AtesZemini_AlevBeslenir_YanikSeconds_ZehirBulutuSeconds()
        {
            TreeVfxBinder binder = Binder();
            GroundFireSystem flames = GroundFireSystem.Instance;
            Assert.That(flames, Is.Not.Null, "GroundFireSystem (VfxDressing.Configure)");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(_root.transform, false);
            ground.transform.position = O + new Vector3(0f, -.5f, -60f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            yield return null;
            Physics.SyncTransforms();

            int started = flames.ZonesStarted;
            TreeProcInfo fire = Info(TreeProcKind.FireGround, O + Vector3.back * 60f, 2.5f, 2);
            fire.Element = TreeElement.Fire;
            fire.Damage = 12f;
            fire.Seconds = 3f;   // S-04 §5: the zone's life; its ticks raise nothing
            Assert.That(binder.Play(in fire), Is.True, "B1 yanık izi");
            Assert.That(flames.ZonesStarted, Is.EqualTo(started + 1), "alevi GroundFireSystem çizer (proc'tan beslenir)");
            Assert.That(binder.FireZonesFed, Is.EqualTo(1));

            // AD-B4 M8: the tree zone's ember underdraw — its char disc in #E0702A, α ≤ 0.30, between the char and the glow
            // the ignition frame's LateUpdate sees intensity 0 (no particle); the next one sets it, and the coroutine resumes after
            // the frame's Update — two frames
            yield return null;
            yield return null;
            Assert.That(flames.TreeUnderLive, Is.EqualTo(1), "M8: ağaç zonunda kor alt çizimi");
            var particles = new ParticleSystem.Particle[4];
            Assert.That(flames.TreeUnderSystem.GetParticles(particles), Is.EqualTo(1));
            Color32 ember = particles[0].startColor;
            Color32 want = TreeVfxLadder.FireUnderTint;
            Assert.That(ember.r, Is.EqualTo(want.r).Within(1));
            Assert.That(ember.g, Is.EqualTo(want.g).Within(1), "#E0702A");
            Assert.That(ember.b, Is.EqualTo(want.b).Within(1));
            Assert.That(ember.a / 255f, Is.LessThanOrEqualTo(.30f + .5f / 255f), "α ≤ 0.30");
            Assert.That(ember.a / 255f, Is.GreaterThan(0f));
            Assert.That(TreeVfxLadder.FireUnderAlpha, Is.EqualTo(.21f).Within(1e-6f), "M8 (C-00 12.10): alt çizim α .21 (.225 b42-r1'de açık taşı geçirmedi)");
            Assert.That(particles[0].startSize, Is.EqualTo(2.5f * .87f * 2f).Within(1e-3f), "çarın diski (boy)");
            float charFudge = flames.SystemOf(3).GetComponent<ParticleSystemRenderer>().sortingFudge;
            float glowFudge = flames.SystemOf(2).GetComponent<ParticleSystemRenderer>().sortingFudge;
            float underFudge = flames.TreeUnderSystem.GetComponent<ParticleSystemRenderer>().sortingFudge;
            Assert.That(underFudge, Is.LessThan(charFudge).And.GreaterThan(glowFudge), "çarın üstünde, kor ışımasının altında");

            // WP-073's own zone (RunTreeGroundFire: a positive id) draws as before — no underdraw
            flames.OnIgnited(new GroundFireInfo(99001, O + new Vector3(-8f, 0f, -60f), 2f, 1f, 3f, 3f));
            yield return null;
            Assert.That(flames.TreeUnderLive, Is.EqualTo(1), "WP-073 zonu değişmez: alt çizim yalnız ağaç zonunda");
            Assert.That(() => { flames.Step(Time.time, .016f); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "alt çizim karesi ayırmasız");

            TreeProcInfo cloud = Info(TreeProcKind.VenomCloud, O + new Vector3(4f, 0f, -60f), 2f, 2);
            cloud.Element = TreeElement.Venom;
            cloud.Seconds = 4f;
            binder.Play(in cloud);
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B4Cloud));
            Assert.That(binder.LastSpawned.LifeSeconds, Is.EqualTo(4f).Within(1e-4f), "bulut zonun ömrü kadar döner");
            Assert.That(binder.LiveClouds, Is.EqualTo(1));

            // AD-B4 S4 (B4-2): the cloud's underdraw — the same sheet, card and life 1 cm under it, the sheet lifted to the venom core
            FlipbookEffect cloudFx = binder.LastSpawned, cloudUnder = binder.LastCloudUnder;
            Assert.That(cloudUnder, Is.Not.Null, "S4: bulutun alt çizimi");
            Assert.That(cloudUnder, Is.Not.SameAs(cloudFx));
            Assert.That(cloudUnder.Row, Is.SameAs(cloudFx.Row), "aynı sayfa (yeni varlık yok)");
            Assert.That(cloudUnder.LifeSeconds, Is.EqualTo(cloudFx.LifeSeconds).Within(1e-4f), "aynı ömür ve kare");
            Assert.That(cloudUnder.transform.GetChild(0).localScale.x, Is.EqualTo(cloudFx.transform.GetChild(0).localScale.x).Within(1e-4f), "aynı kart");
            Assert.That(cloudUnder.AlphaCap, Is.EqualTo(TreeVfxLadder.VenomUnderAlpha).Within(1e-5f), "α ≤ 0.30");
            Assert.That(cloudUnder.transform.position.y, Is.LessThan(cloudFx.transform.position.y), "bulutun altında: önce çizilir");
            Color lifted = TreeVfxLadder.HdrTint(TreeVfxLadder.VenomUnderTint, TreeVfxLadder.VenomUnderGain, TreeVfxLadder.VenomUnderCoverage);
            Assert.That(Mathf.GammaToLinearSpace(lifted.g), Is.EqualTo(TreeVfxLadder.VenomUnderTint.linear.g * TreeVfxLadder.VenomUnderGain).Within(.02f),
                "zehir çekirdeği #E8FFB0 × 4 (doğrusal)");
            Color manifestTint = cloudFx.Row.DefaultTint;
            manifestTint.a = 1f;
            var liftProbe = new MaterialPropertyBlock();
            liftProbe.SetColor("_Tint", binder.VenomUnderTint * manifestTint);   // FlipbookEffect.Play's tint × the row's own
            var underBlock = new MaterialPropertyBlock();
            cloudUnder.transform.GetChild(0).GetComponent<MeshRenderer>().GetPropertyBlock(underBlock);
            Color underTint = underBlock.GetColor("_Tint");
            Assert.That(underTint.g, Is.EqualTo(liftProbe.GetColor("_Tint").g).Within(1e-3f), "HDR zehir tonu bloğa geçer");
            Assert.That(underTint.a, Is.EqualTo(TreeVfxLadder.VenomUnderCoverage).Within(1e-4f), "örtü ölçeği × 2 (shader fade)");
            flames.Clear();
        }

        [UnityTest]
        public IEnumerator CanEmme_PointKurban_IplikKahramana_NoktasizIplikYok()
        {
            TreeVfxBinder binder = Binder();
            var mannequin = new GameObject("WP104 leech anchor");
            mannequin.transform.SetParent(_root.transform, false);
            mannequin.transform.position = O + Vector3.right * 90f;
            TreeVfxPreview.Begin(TreeStats.Neutral, mannequin.transform);
            yield return null;

            Vector3 hero = mannequin.transform.position;
            Assert.That(binder.Play(Info(TreeProcKind.Leech, hero + Vector3.forward * 5f, 0f, 2)), Is.True, "S-04 B5b: Point = kurban");
            Assert.That(binder.LastSpawnedRow, Is.EqualTo(TreeVfxRow.B15Thread), "iplik kurban → kahraman");
            Assert.That(binder.LastSpawned.LifeSeconds, Is.EqualTo(TreeVfxBinder.LeechSeconds).Within(1e-4f));
            Assert.That(binder.LeechUnresolved, Is.EqualTo(0));

            // AD-B4 M7: the jade underdraw — the same ribbon (row, length, flow), thicker, α 0.5, behind the thread for the camera
            FlipbookEffect thread = binder.LastSpawned, under = binder.LastUnderlay;
            Assert.That(under, Is.Not.Null, "M7: yeşim alt çizim");
            Assert.That(under, Is.Not.SameAs(thread));
            Assert.That(under.Row, Is.SameAs(thread.Row), "aynı ribbon");
            Assert.That(under.LifeSeconds, Is.EqualTo(TreeVfxBinder.LeechSeconds).Within(1e-4f), "iplikle aynı ömür");
            Assert.That(under.AlphaCap, Is.EqualTo(TreeVfxLadder.LeechUnderAlpha).Within(1e-5f), "α 0.5");
            Assert.That(Vector3.Angle(under.AlignDirection, thread.AlignDirection), Is.LessThan(.01f), "aynı akış (kurban → kahraman)");
            Transform threadQuad = thread.transform.GetChild(0), underQuad = under.transform.GetChild(0);
            Assert.That(underQuad.localScale.x, Is.EqualTo(threadQuad.localScale.x).Within(1e-4f), "aynı boy");
            Assert.That(underQuad.localScale.y / threadQuad.localScale.y, Is.EqualTo(TreeVfxLadder.LeechUnderThickness).Within(1e-3f), "iplik bandından kalın");
            var block = new MaterialPropertyBlock();
            underQuad.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Color tint = block.GetColor("_Tint");
            Color given = TreeVfxLadder.LeechUnderTint, manifest = thread.Row.DefaultTint;
            given.a = TreeVfxLadder.LeechUnderCoverage;
            manifest.a = 1f;
            var probe = new MaterialPropertyBlock();
            probe.SetColor("_Tint", given * manifest);   // FlipbookEffect.Play's tint × the row's own, through the block's colour space
            Color jade = probe.GetColor("_Tint");
            Assert.That(tint.r, Is.EqualTo(jade.r).Within(1e-3f));
            Assert.That(tint.g, Is.EqualTo(jade.g).Within(1e-3f), "yeşim #4F8A63");
            Assert.That(tint.b, Is.EqualTo(jade.b).Within(1e-3f));
            Assert.That(tint.a, Is.EqualTo(TreeVfxLadder.LeechUnderCoverage).Within(1e-4f), "örtü ölçeği (shader fade)");
            if (FeedbackCameraRef.TryGetPosition(out Vector3 eye))
            {
                Assert.That((under.transform.position - eye).magnitude, Is.GreaterThan((thread.transform.position - eye).magnitude), "kameradan uzak: önce çizilir");
            }

            Assert.That(binder.Play(Info(TreeProcKind.Leech, default, 0f, 2)), Is.False, "noktasız an: iplik yok");
            Assert.That(binder.Play(Info(TreeProcKind.Leech, hero, 0f, 2)), Is.False, "kahramanın ayağındaki kurban: iplik yok");
            Assert.That(binder.LeechUnresolved, Is.EqualTo(2));

            // the thread + its underdraw allocate nothing (pool warm, rows resolved)
            TreeProcInfo leech = Info(TreeProcKind.Leech, hero + Vector3.left * 5f, 0f, 2);
            binder.Clear();
            Assert.That(() => { binder.Play(in leech); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "iplik + alt çizim ayırmasız");
        }

        // ------------------------------------------------------------------ the tree preview clock

        [UnityTest]
        public IEnumerator Onizleme_TimeScale0_DioramaIzVeKartAkar_DunyaKartiDonuk()
        {
            TreeVfxBinder binder = Binder();
            TracerRibbonRenderer tracer = TracerRibbonRenderer.Instance;
            Assert.That(tracer != null && tracer.Ready, Is.True);
            yield return null;
            binder.Play(Info(TreeProcKind.Crit, O + Vector3.right * 50f, 0f, 2));
            FlipbookEffect world = binder.LastSpawned;
            Assert.That(world, Is.Not.Null);
            Time.timeScale = 0f;
            yield return null;
            int worldFrame = world.CurrentFrame;

            var mannequin = new GameObject("WP104 diorama anchor");
            mannequin.transform.SetParent(_root.transform, false);
            mannequin.transform.position = O + Vector3.down * 500f;
            TreeStats after = TreeStats.Neutral;
            after.SetBranchTier(TreeBranch.Shot, 3);
            TreeVfxPreview.Begin(in after, mannequin.transform);
            VfxWeaponProfile ar = VfxWeaponProfiles.For(ClassicWeaponId.AssaultRifle);
            Vector3 muzzle = mannequin.transform.position + Vector3.up * 1.3f;
            Assert.That(tracer.Emit(muzzle, muzzle + Vector3.forward * 40f, ar, false, false, false), Is.True);
            binder.Play(Info(TreeProcKind.Crit, muzzle + Vector3.forward * 10f, 0f, 2));
            FlipbookEffect diorama = binder.LastSpawned;
            float start = Time.realtimeSinceStartup, travel = 0f, width = 0f;
            int frames = 0, dioramaFrame = 0;
            while (Time.realtimeSinceStartup - start < .25f && frames < 120)
            {
                frames++;
                yield return null;
                travel = Mathf.Max(travel, TracerHeadTravel(tracer, muzzle, out float w));   // the record dies at its 40 m target: keep the best
                width = Mathf.Max(width, w);
                dioramaFrame = Mathf.Max(dioramaFrame, diorama.CurrentFrame);
            }

            Assert.That(dioramaFrame, Is.GreaterThan(0), "önizleme kartı ölçeksiz saatle akar");
            Assert.That(world.CurrentFrame, Is.EqualTo(worldFrame), "önizlemeden önce doğan dünya kartı donuk kalır (kendi saati)");
            Assert.That(travel, Is.GreaterThan(1f), "diorama izi timeScale 0'da ilerler");
            Assert.That(width, Is.EqualTo(TreeVfxLadder.HaloStepPx(3)).Within(1e-4f), "önizleme 'sonra' yapısının izini doğurur (Atış III: hale +10 px)");
            TreeVfxPreview.End();
            Assert.That(VfxClock.Offset, Is.GreaterThan(0f), "önizlemenin ilerlettiği süre Now'da kalır");
        }

        // ------------------------------------------------------------------ S-06 add-on glow (needs DD_WeaponAttach)

        [UnityTest]
        public IEnumerator EkIsimasi_E4AtesIII_E5V_FlashScale_Gizli_ElektrikYuvasiz_0B()
        {
            WeaponAttachLibrary.ResetCache();
            WeaponAttachLibrary lib = WeaponAttachLibrary.Current;
            if (lib == null) Assert.Ignore("DD_WeaponAttach henüz kurulmadı (Resources/" + WeaponAttachLibrary.ResourcePath + "): S-06 ArtAp040WeaponAttachBuilder");
            WeaponAttachLibrary.Row row = lib.Find(ClassicWeaponId.AssaultRifle);
            Assert.That(row, Is.Not.Null, "ar_1 satırı");
            SaveAndClearDelegates(typeof(WeaponAttachEvents));   // only this test's glow listens
            var glowGo = new GameObject("WP104 glow");
            glowGo.transform.SetParent(_root.transform, false);
            TreeAttachGlow glow = glowGo.AddComponent<TreeAttachGlow>();
            var model = new GameObject("WP104 ar_1 model root");
            model.transform.SetParent(_root.transform, false);
            model.transform.position = O + Vector3.up * 30f;
            WeaponAttachMount mount = WeaponAttachMount.Ensure(model.transform, row);
            Assert.That(mount, Is.Not.Null);

            var set = new WeaponAttachSet { Side = TreeElement.Fire, SideTier = 3, Ring = 1, RingTier = 5 };
            mount.Apply(in set, lib);   // S-06 raises WeaponAttachEvents.Changed → the ladder
            Assert.That(glow.Mount, Is.SameAs(mount));
            Assert.That(glow.GlowRenderers, Is.GreaterThan(0), "E4 ateş + E5: M_AP040_Glow yuvası (S-06)");
            ExpectGlow(mount.SideAttachment, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, 3, 1f, true), "E4 ateş III = 1.4 (AD-B4 M9)");
            ExpectGlow(mount.RingAttachment, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Ring, 5, 1f, true), "E5 V = 2.0 HDR (AD-B4 M9)");
            Assert.That(glow.SideEmission.r, Is.EqualTo(1.4f).Within(1e-5f), "M9: E4 III 1.4 (bloom eşiği 1.25 üstü)");
            Assert.That(glow.RingEmission.r, Is.EqualTo(2f).Within(1e-5f), "M9: E5 V 2.0");
            Assert.That(glow.EmissionOffRenderers, Is.EqualTo(0), "boyanan ışıma yuvasında _EMISSION açık (M_AP040_Glow)");

            GameFeelSettings.FlashScale = .4f;
            yield return null;   // the poll repaints (no event for a setting)
            ExpectGlow(mount.SideAttachment, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, 3, .4f, true), "Reduce Flashes × FlashScale");
            ExpectGlow(mount.RingAttachment, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Ring, 5, .4f, true), "V bloom eşiğinin altına iner");
            Assert.That(glow.RingEmission.r, Is.LessThan(TreeVfxLadder.BloomThreshold), "RF 0,4: E5 V 0,8 — bloom yok (AD M9: kabul)");
            GameFeelSettings.FlashScale = 1f;

            mount.SetShown(false);   // S-06: third person hides the add-ons (no event)
            yield return null;
            ExpectGlow(mount.SideAttachment, Color.black, "gizliyken kapalı");
            mount.SetShown(true);
            yield return null;
            ExpectGlow(mount.SideAttachment, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, 3, 1f, true), "TD'ye dönünce geri");

            // the per-frame poll with nothing changed: 0 B
            var late = (Action)Delegate.CreateDelegate(typeof(Action), glow, typeof(TreeAttachGlow).GetMethod("LateUpdate", Private));
            late();
            int applies = glow.Applies;
            Assert.That(() => { for (int i = 0; i < 100; i++) late(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ışıma yoklaması ayırmasız");
            Assert.That(glow.Applies, Is.EqualTo(applies), "değişiklik yokken boyama yok");

            // element_capsule_V03 (electric): no glow slot in AP-040 v001 — counted, left dark
            var shock = new WeaponAttachSet { Side = TreeElement.Shock, SideTier = 4, Ring = 1, RingTier = 5 };
            mount.Apply(in shock, lib);
            yield return null;
            Assert.That(glow.SlotlessRenderers, Is.GreaterThan(0), "V03 elektrik kapsülünde ışıma yuvası yok (AP-040 notu)");
            Debug.Log("[DD-TEST] wp104b4.glow glowRenderers=" + glow.GlowRenderers + " slotless=" + glow.SlotlessRenderers + " applies=" + glow.Applies);
        }

        // ------------------------------------------------------------------ helpers

        private static void ExpectGlow(GameObject addOn, Color emission, string because)
        {
            Assert.That(addOn, Is.Not.Null, because + ": ek yok");
            var probe = new MaterialPropertyBlock();
            probe.SetColor("_EmissionColor", emission);
            Color want = probe.GetColor("_EmissionColor");   // the block's own colour-space round trip
            var scratch = new MaterialPropertyBlock();
            int painted = 0;
            foreach (Renderer r in addOn.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats.Length <= TreeAttachGlow.GlowSlot || !TreeAttachGlow.IsGlowMaterial(mats[TreeAttachGlow.GlowSlot])) continue;
                Color have = TreeAttachGlow.EmissionOf(r, scratch);
                Assert.That(have.r, Is.EqualTo(want.r).Within(1e-3f), because + " (" + r.name + ")");
                Assert.That(have.g, Is.EqualTo(want.g).Within(1e-3f), because + " (" + r.name + ")");
                painted++;
            }

            Assert.That(painted, Is.GreaterThan(0), because + ": ışıma yuvalı renderer yok");
        }

        private TreeVfxBinder Binder()
        {
            VfxDressing vfx = HitFeedbackDirector.Instance.Vfx;
            TreeVfxBinder binder = TreeVfxBinder.Instance != null ? TreeVfxBinder.Instance : TreeVfxBinder.Ensure(vfx);
            Assert.That(binder, Is.Not.Null, "TreeVfxBinder yok (VfxDressing.Configure)");
            Assert.That(binder.RowsReady, Is.True, "AP-041 ağaç satırları katalogda yok (" + binder.RowsResolved + "/" + (int)TreeVfxRow.Count + "): ArtAp041TreeIntake.Run");
            SaveAndClearDelegates(typeof(TreeEvents));   // only the binder listens: no S-08 audio, no telemetry
            binder.Rearm();
            binder.Clear();
            binder.ResetCountersForTests();
            return binder;
        }

        private static TreeProcInfo Info(TreeProcKind kind, Vector3 point, float radius, int tier) =>
            new TreeProcInfo { Kind = kind, Point = point, Radius = radius, Tier = tier, Weapon = ClassicWeaponId.AssaultRifle };

        private TracerRibbonRenderer AttachTdTracer()
        {
            _hasTracerFixture = true;
            FieldInfo instance = typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields);
            _previousTracer = (TracerRibbonRenderer)instance.GetValue(null);
            _previousInterimTrails = ProjectileDriver.DrawInterimTrails;
            if (_previousTracer != null)
            {
                _previousTracerEnabled = _previousTracer.enabled;
                _previousTracer.enabled = false;
            }

            SaveAndClearDelegates(typeof(ProjectileEvents));
            instance.SetValue(null, null);   // avoid Awake's duplicate-owner Destroy path
            var owner = new GameObject("WP104 TD tracer fixture");
            owner.transform.SetParent(_root.transform, false);
            _tracer = owner.AddComponent<TracerRibbonRenderer>();
            _tracer.enabled = false;   // TickTd alone advances this deterministic fixture
            Assert.That(_tracer.Ready, Is.True, "real DD/VFX Ribbon shader must be present");
            return _tracer;
        }

        private static ProjectileSpawnInfo TdSpawn(int id, ClassicWeaponId weapon, Vector3 origin, int volley = 0, float size = 1f) =>
            new ProjectileSpawnInfo(id, int.MaxValue, weapon, ProjectileKind.Bullet, WeaponCaliber.Medium, origin, Vector3.right, 100f, 0, volley, size);

        private static int DrawTd(TracerRibbonRenderer tracer, int layer) =>
            (int)typeof(TracerRibbonRenderer).GetMethod("DrawTd", Private).Invoke(tracer, new object[] { layer });

        private static int InvokeInk(TracerRibbonRenderer tracer) =>
            (int)typeof(TracerRibbonRenderer).GetMethod("DrawTdInk", Private).Invoke(tracer, null);

        private static Vector4[] TracerBuffer(TracerRibbonRenderer tracer, string name) =>
            (Vector4[])typeof(TracerRibbonRenderer).GetField(name, Private).GetValue(tracer);

        /// <summary>A field of the live TD record with this id (reflection on the private records).</summary>
        private static T RecordField<T>(TracerRibbonRenderer tracer, int id, string field)
        {
            var rounds = (Array)typeof(TracerRibbonRenderer).GetField("_tdRounds", Private).GetValue(tracer);
            foreach (object r in rounds)
            {
                Type t = r.GetType();
                if ((bool)t.GetField("Alive").GetValue(r) && (int)t.GetField("Id").GetValue(r) == id) return (T)t.GetField(field).GetValue(r);
            }

            Assert.Fail("no live TD record " + id);
            return default;
        }

        /// <summary>The farthest a live synthetic TD record's head got from <paramref name="origin"/>, and its halo's tier step (px).</summary>
        private static float TracerHeadTravel(TracerRibbonRenderer tracer, Vector3 origin, out float haloPx)
        {
            var rounds = (Array)typeof(TracerRibbonRenderer).GetField("_tdRounds", Private).GetValue(tracer);
            float best = 0f;
            haloPx = 0f;
            foreach (object r in rounds)
            {
                Type t = r.GetType();
                if (!(bool)t.GetField("Alive").GetValue(r) || (int)t.GetField("Id").GetValue(r) >= 0) continue;
                best = Mathf.Max(best, Vector3.Distance((Vector3)t.GetField("Head").GetValue(r), origin));
                haloPx = Mathf.Max(haloPx, (float)t.GetField("HaloPx").GetValue(r));
            }

            return best;
        }

        private void SaveAndClearDelegates(Type type)
        {
            foreach (EventInfo signal in type.GetEvents(StaticFields))
            {
                FieldInfo field = type.GetField(signal.Name, StaticFields);
                Assert.That(field, Is.Not.Null, "event backing field: " + signal.Name);
                _savedDelegates.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(null, null);
            }
        }

        private static void SetModeState(CameraMode mode) =>
            typeof(CameraModeService).GetField("s_current", StaticFields).SetValue(null, mode);
    }
}
