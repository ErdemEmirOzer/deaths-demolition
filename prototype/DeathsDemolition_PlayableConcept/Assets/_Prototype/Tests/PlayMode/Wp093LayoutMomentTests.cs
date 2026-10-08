using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rendering;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-093 × WP-092 (S-05 LAMP-CONTRACT, layout-data.json v002 moments) on synthetic fixtures — the scan finds the layout by name, so a
    /// fixture under a "Layout v5 (WP-092)" root is the real contract. A: lamp kinds by name (unlit → nothing, emissive → no light, pool
    /// first in the ≤ 6 pool with the full mask, baked → Characters only); tōrō bob / drift / yaw inside their bounds, ringing the pond,
    /// one streak each; chōchin pendulum within ±amp, phased along the wind, paper glow on the same sample. B: moment mist flat under its
    /// ceiling (α ≤ 0.12), a gust lifts ≤ 30 petals (≤ 50 live), door shaft α ≤ 0.12 + motes, the M5 curtain ≤ 40 drops / ≤ 12 rings,
    /// M4 footstep rings ≤ 16. C: shoji emission from the nearest lamp's sample, v ≤ 0.78. And 0 B for the three systems together.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093LayoutMomentTests
    {
        private static readonly Vector3 O = new Vector3(600f, 0f, 600f);
        private GameObject _world;
        private GameObject _camGo;
        private GameObject _player;
        private Transform _clusters;
        private readonly List<Object> _assets = new List<Object>();
        private LanternFlames _lanterns;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
            GameFeelSettings.FlashScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
            WorldMoments.Override = null;
            WorldWind.ApplyCalm();
            EnemyTargeting.Clear();
            if (_lanterns != null)
            {
                _lanterns.Clear();   // movers back to rest, material swaps undone
                _lanterns.ScanRoot = null;
            }

            if (AmbientVfxSystem.Active != null) Object.Destroy(AmbientVfxSystem.Active.gameObject);
            if (_world != null) Object.Destroy(_world);
            if (_camGo != null) Object.Destroy(_camGo);
            if (_player != null) Object.Destroy(_player);
            foreach (Object o in _assets) if (o != null) Object.Destroy(o);
            _assets.Clear();
            FeedbackCameraRef.Set(null);
        }

        // ------------------------------------------------------------------ A · lamp kinds and the pool

        [UnityTest]
        public IEnumerator Sozlesme_LambaTurleri_HavuzOnceligi_IsikMaskeleri()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            // Six baked lamps 2–4 m round the player, one pool lamp 10 m off (farthest), one emissive at 1 m (nearest), one unlit.
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Lamp(_clusters, "Lamp_T_B" + i + "_baked", O + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (2f + i * 0.3f), "SM_andon_V01_LOD0", 0.5f);
            }

            Lamp(_clusters, "Lamp_T_P1_pool", O + new Vector3(10f, 0f, 0f), "SM_lantern_oribe_V01_LOD0", 1.2f);
            Lamp(_clusters, "Lamp_T_E1_emissive", O + new Vector3(0f, 0f, 1f), "SM_wall_sconce_V01_LOD0", 0.4f);
            Lamp(_clusters, "Lamp_T_U1_unlit", O + new Vector3(-1f, 0f, 0f), "SM_kerosene_lamp_V01_LOD0", 0.5f);
            yield return null;

            Assert.AreEqual(8, _lanterns.Scan(), "6 baked + 1 pool + 1 emissive (unlit: nothing)");
            int pool = Find("T_P1"), emissive = Find("T_E1");
            Assert.AreEqual(-1, Find("T_U1"), "unlit lamba alev almamalı");
            Assert.AreEqual(LampKind.Pool, _lanterns.KindOf(pool));
            Assert.AreEqual(0, _lanterns.LightModeOf(emissive), "emissive: ışık yok");
            Assert.IsFalse(_lanterns.HasHalo(emissive), "emissive (anı dışı): hale yok");
            Assert.AreEqual(0, _lanterns.MaxSparksOf(emissive));

            float t = Time.time;
            for (int k = 0; k < 15; k++) { t += 1.02f / LanternFlames.FlickerHz; _lanterns.Tick(t); }
            bool poolLit = false;
            int bakedLit = 0;
            for (int slot = 0; slot < LanternFlames.MaxLanternLights; slot++)
            {
                int a = _lanterns.LightAnchorOf(slot);
                if (a < 0) continue;
                Assert.AreNotEqual(emissive, a, "emissive lamba ışık aldı");
                uint? layers = LanternFlames.RenderingLayersOf(_lanterns.LightOf(slot));
                if (a == pool)
                {
                    poolLit = true;
                    Assert.AreEqual(LanternFlames.PoolLampsFullMask ? LanternFlames.FullRenderingLayers : LanternFlames.CharactersOnlyRenderingLayers, layers, "pool lamba maskesi");
                }
                else
                {
                    Assert.AreEqual(LampKind.Baked, _lanterns.KindOf(a));
                    Assert.AreEqual(LanternFlames.CharactersOnlyRenderingLayers, layers, "baked: yalnız Characters");
                    bakedLit++;
                }
            }

            Assert.IsTrue(poolLit, "pool lamba (en uzak) öncelikle ışık almalı");
            Assert.AreEqual(LanternFlames.MaxLanternLights - 1, bakedLit, "kalan slotlar en yakın baked lambalara");
        }

        // ------------------------------------------------------------------ A · tōrō

        [UnityTest]
        public IEnumerator Toro_SalinirSurukleninir_HalkaVeSerit()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            Pond(O + new Vector3(0f, -0.02f, 4f), new Vector3(8f, 0.01f, 6f));
            var group = new GameObject(WorldMoments.ToroGroupName).transform;
            group.SetParent(_clusters, false);
            var toro = new List<Transform>();
            for (int i = 0; i < 2; i++)
            {
                GameObject g = Lamp(group, "M8_P0" + (i + 1) + "_andon", O + new Vector3(-1f + 2f * i, -0.02f, 4f), "SM_andon_V01_LOD0", 0.45f);
                g.transform.localScale = Vector3.one * 0.6f;
                toro.Add(g.transform);
            }

            AmbientVfxSystem amb = Ambient();
            yield return null;
            Assert.AreEqual(2, _lanterns.Scan());
            Assert.AreEqual(2, _lanterns.ToroCount);
            Assert.AreEqual(0, _lanterns.MotionBlockedByStaticBatch);
            amb.Step(1f / 30f);   // the ring tier

            var basePos = new Vector3[2];
            var baseRot = new Quaternion[2];
            for (int i = 0; i < 2; i++) { basePos[i] = toro[i].position; baseRot[i] = toro[i].rotation; }
            for (int i = 0; i < 2; i++)
            {
                int a = Find(toro[i].name);
                Assert.AreEqual(LampKind.Toro, _lanterns.KindOf(a));
                Assert.AreEqual(0, _lanterns.LightModeOf(a), "tōrō lamba değil: ışık yok");
                Assert.AreEqual(2, _lanterns.StreakKindOf(a), "tōrō başına sahte yansıma şeridi");
            }

            Assert.AreEqual(2, _lanterns.StreaksLive);
            float t = Time.time, maxXZ = 0f, maxY = 0f, maxYaw = 0f, moved = 0f;
            for (int k = 0; k < 400; k++)   // ≈ 20 s
            {
                t += 1.02f / LanternFlames.FlickerHz;
                _lanterns.Tick(t);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 d = toro[i].position - basePos[i];
                    float xz = new Vector2(d.x, d.z).magnitude;
                    maxXZ = Mathf.Max(maxXZ, xz);
                    maxY = Mathf.Max(maxY, Mathf.Abs(d.y));
                    maxYaw = Mathf.Max(maxYaw, Quaternion.Angle(baseRot[i], toro[i].rotation));
                    moved = Mathf.Max(moved, xz);
                }
            }

            Debug.Log("[DD-TEST] wp093.toro drift=" + maxXZ.ToString("F3") + " bob=" + maxY.ToString("F3") + " yaw=" + maxYaw.ToString("F2") + " rings=" + _lanterns.ToroRings);
            Assert.LessOrEqual(maxXZ, 0.4f + 1e-3f, "sürüklenme r ≤ 0,4 m");
            Assert.LessOrEqual(maxY, LanternFlames.ToroBobMetres + 1e-3f, "salınım ±1 cm");
            Assert.LessOrEqual(maxYaw, LanternFlames.ToroYawDegrees + 0.1f, "yaw ±5°");
            Assert.Greater(moved, 0.05f, "tōrō yerinden kımıldamadı");
            Assert.GreaterOrEqual(_lanterns.ToroRings, 4, "her 3–5 s bir halka (20 s, 2 tōrō)");
            Assert.Greater(amb.Rings(AmbientVfxSystem.RingCause.Toro), 0);
        }

        // ------------------------------------------------------------------ A · pendulum + paper glow (M7-like)

        [UnityTest]
        public IEnumerator Sarkac_ChochinRuzgarFazli_KagitParlamaAyniOrnek()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            WorldMoments.Override = new[] { new MomentSpec { Id = "T7", Center = new Vector2(O.x, O.z), Radius = 10f, SwingDegrees = 5f, HaloEmissive = true } };
            var paper = new Material(PrototypeShaders.Lit) { name = "M_ap023_lantern_paper" };
            _assets.Add(paper);
            GameObject a = Lamp(_clusters, "Lamp_T_C1_emissive", O + new Vector3(-2f, 2.5f, 0f), "SM_lantern_chochin_V01_LOD0", 0.4f, paper);
            GameObject b = Lamp(_clusters, "Lamp_T_C2_emissive", O + new Vector3(2f, 2.5f, 0f), "SM_lantern_chochin_V01_LOD0", 0.4f, paper);
            WorldWind.Apply(Vector3.right, 1f, 0f);   // swing along +x; b is 4 m downwind of a
            yield return null;

            Assert.AreEqual(2, _lanterns.Scan());
            int ia = Find("T_C1");
            Assert.AreEqual(2, _lanterns.Movers, "asılı iki chōchin sallanır");
            Assert.AreEqual(5f, _lanterns.SwingDegreesOf(ia), 1e-4f, "anının genliği ±5°");
            Assert.IsTrue(_lanterns.HasHalo(ia), "M7: emissive fenere hale");
            Renderer body = a.GetComponentInChildren<Renderer>();
            Assert.IsTrue(body.sharedMaterial.IsKeywordEnabled("_EMISSION"), "kağıt yuvası _EMISSION'lı çalışma kopyasına geçmeli");
            int glow = -1;
            for (int k = 0; k < _lanterns.GlowSlots; k++) if (_lanterns.GlowAnchorOf(k) == ia) glow = k;
            Assert.GreaterOrEqual(glow, 0, "chōchin kağıdı parlama yuvası değil");

            Quaternion ra = a.transform.rotation, rb = b.transform.rotation;
            float t = Time.time, maxA = 0f, maxDiff = 0f;
            var block = new MaterialPropertyBlock();
            for (int k = 0; k < 200; k++)
            {
                t += 1.02f / LanternFlames.FlickerHz;
                _lanterns.Tick(t);
                float angA = Quaternion.Angle(ra, a.transform.rotation), angB = Quaternion.Angle(rb, b.transform.rotation);
                maxA = Mathf.Max(maxA, angA);
                maxDiff = Mathf.Max(maxDiff, Mathf.Abs(angA - angB));
                body.GetPropertyBlock(block, _lanterns.GlowMaterialIndexOf(glow));
                Color e = block.GetColor(LanternFlames.GlowEmissionProperty), expect = LanternFlames.GlowEmissionFor(LanternFlames.PaperGlow, _lanterns.SignalOf(ia), 1f);
                Assert.AreEqual(expect.r, e.r, 1e-4f, "kağıt parlaması aynı titreme örneğinden");
                FlipbookEffect card = _lanterns.FlameOf(ia);
                Assert.Less((card.transform.position - _lanterns.MovedFlamePointOf(ia)).magnitude, LanternFlames.SwayMetres + 0.005f, "alev feneriyle sallanır");
            }

            Debug.Log("[DD-TEST] wp093.pendulum max=" + maxA.ToString("F2") + " phaseDiff=" + maxDiff.ToString("F2"));
            Assert.LessOrEqual(maxA, 5f + 0.05f, "±5° aşıldı");
            Assert.Greater(maxA, 2f, "sarkaç sallanmıyor (rüzgâr tam)");
            Assert.Greater(maxDiff, 0.5f, "faz rüzgâr boyunca konuma bağlı olmalı (aynı fazda sallanıyor)");

            _lanterns.Clear();
            Assert.AreEqual(ra, a.transform.rotation, "Clear feneri dinlenme pozuna döndürmeli");
            Assert.AreSame(paper, body.sharedMaterial, "Clear kağıt malzemesini geri koymalı");
        }

        // ------------------------------------------------------------------ C · shoji

        [UnityTest]
        public IEnumerator Shoji_YakinLambaninOrnegiyle_Isir_V078()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            WorldMoments.Override = new[] { new MomentSpec { Id = "T6", Center = new Vector2(O.x, O.z), Radius = 8f, Shoji = true } };
            Lamp(_clusters, "Lamp_T_A1_baked", O, "SM_andon_V01_LOD0", 0.5f);
            var washi = new Material(PrototypeShaders.Lit) { name = "M_ap023_washi" };
            _assets.Add(washi);
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.name = "R4_Shoji";
            panel.transform.SetParent(_world.transform, false);
            panel.transform.position = O + new Vector3(0f, 1f, 2f);
            panel.transform.localScale = new Vector3(2f, 1.8f, 0.05f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = washi;
            var far = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 9 m away: no lamp within 5 m
            Object.DestroyImmediate(far.GetComponent<Collider>());
            far.transform.SetParent(_world.transform, false);
            far.transform.position = O + new Vector3(7.5f, 1f, 0f);
            far.GetComponent<MeshRenderer>().sharedMaterial = washi;
            yield return null;

            Assert.AreEqual(1, _lanterns.Scan());
            Assert.AreEqual(1, _lanterns.ShojiSlots, "lambaya ≤ 5 m tek shoji paneli");
            Assert.LessOrEqual(LanternFlames.ShojiEmission.maxColorComponent * (1f + LanternFlames.LightFlickerAmplitude), 0.78f + 1e-4f, "v ≤ 0,78");
            int k = -1;
            for (int g = 0; g < _lanterns.GlowSlots; g++) if (_lanterns.GlowIsShoji(g)) k = g;
            Renderer r = _lanterns.GlowRendererOf(k);
            Assert.AreSame(panel.GetComponent<MeshRenderer>(), r);
            var block = new MaterialPropertyBlock();
            float t = Time.time;
            for (int step = 0; step < 40; step++)
            {
                t += 1.02f / LanternFlames.FlickerHz;
                _lanterns.Tick(t);
                r.GetPropertyBlock(block, 0);
                Color e = block.GetColor(LanternFlames.GlowEmissionProperty);
                Color expect = LanternFlames.GlowEmissionFor(LanternFlames.ShojiEmission, _lanterns.SignalOf(_lanterns.GlowAnchorOf(k)), 1f);
                Assert.AreEqual(expect.r, e.r, 1e-4f, "shoji yakın lambanın örneğiyle");
                Assert.LessOrEqual(e.maxColorComponent, 0.78f + 1e-4f, "shoji v ≤ 0,78");
                Assert.Greater(e.r, e.b, "sıcak #E8C89A");
            }
        }

        // ------------------------------------------------------------------ B · moments

        [UnityTest]
        public IEnumerator Anlar_Sis_Cicek_Kapi_Perde_AdimHalkasi_Tavanlar()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            var specs = new[]
            {
                new MomentSpec { Id = "T1", Center = new Vector2(O.x - 30f, O.z), Radius = 8f, Mist = MomentMist.Lane, MistColour = new Color(0.79f, 0.82f, 0.84f), MistAlpha = 0.10f, MistTop = 0.3f, MistDrift = new Vector3(0.12f, 0f, 0f) },
                new MomentSpec { Id = "T2", Center = new Vector2(O.x + 30f, O.z), Radius = 8f, Hanafubuki = true },
                new MomentSpec { Id = "T3", Center = new Vector2(O.x, O.z + 30f), Radius = 7f, Mist = MomentMist.Pocket, MistRect = new Vector4(O.x + 2f, O.z + 31f, O.x + 6f, O.z + 33f), MistColour = new Color(0.6f, 0.65f, 0.7f), MistAlpha = 0.08f, MistTop = 0.3f,
                    Door = true, DoorPoint = new Vector3(O.x, 0.25f, O.z + 30f), DoorDir = Vector3.right, DoorWidth = 2f, ShaftLength = 3.5f, Drips = MomentDrips.Wedge, DripRate = 2f },
                new MomentSpec { Id = "T4", Center = new Vector2(O.x, O.z), Radius = 8f, FootstepRings = true },
                new MomentSpec { Id = "T5", Center = new Vector2(O.x, O.z - 30f), Radius = 10f, Drips = MomentDrips.Curtain, DripRadius = 2.5f, DripRate = 14f },
            };
            WorldMoments.Override = specs;
            Lamp(_clusters, "Lamp_T_K1_baked", new Vector3(O.x - 3f, 3.4f, O.z - 30f), "SM_kerosene_lamp_V01_LOD0", 0.5f);
            Lamp(_clusters, "Lamp_T_K2_baked", new Vector3(O.x + 3f, 3.4f, O.z - 30f), "SM_kerosene_lamp_V01_LOD0", 0.5f);
            AmbientVfxSystem amb = Ambient();
            yield return null;
            Assert.AreEqual(2, _lanterns.Scan());
            MomentVfxSystem mom = MomentVfxSystem.Active;
            Assert.IsNotNull(mom, "MomentVfxSystem AmbientVfxSystem ile gelmeli");
            Assume.That(mom.Ready);
            mom.FocusOverride = O;
            mom.Rescan();
            Assert.AreEqual(2, mom.LampsIn(4), "T5'in iki lambası");
            Assert.IsNotNull(mom.ShaftOf(0), "kapı ışık kartı");

            VfxTier.Override = VfxTierLevel.Cinematic;
            amb.Step(1f / 30f);
            mom.TriggerGust();
            int gustPetals0 = mom.GustPetals;
            for (int i = 0; i < 120; i++) { mom.Step(1f / 30f); amb.Step(1f / 30f); }
            Assert.LessOrEqual(mom.GustPetals - gustPetals0, MomentVfxSystem.MaxPetalsPerGust, "bir rüzgâr cephesi ≤ 30 yaprak");
            Assert.Greater(mom.GustPetals - gustPetals0, 0, "hanafubuki yaprak kaldırmadı");
            mom.TriggerGust();
            for (int i = 0; i < 90; i++) { mom.Step(1f / 30f); amb.Step(1f / 30f); }

            foreach (MomentVfxSystem.Layer l in new[] { MomentVfxSystem.Layer.Mist, MomentVfxSystem.Layer.Petals, MomentVfxSystem.Layer.Motes, MomentVfxSystem.Layer.Drips })
                Assert.LessOrEqual(mom.LiveCount(l), MomentVfxSystem.CapFor(l, VfxTierLevel.Cinematic), l + " tavanı");
            Assert.LessOrEqual(mom.LiveCount(MomentVfxSystem.Layer.Petals), 50, "karede ≤ 50 yaprak");
            Assert.Greater(mom.LiveCount(MomentVfxSystem.Layer.Mist), 0, "sis yok");
            Assert.Greater(mom.LiveCount(MomentVfxSystem.Layer.Motes), 0, "kapı zerresi yok");

            // Mist: flat, under its ceiling, α ≤ 0.12.
            ParticleSystem mist = mom.SystemOf(MomentVfxSystem.Layer.Mist);
            var buf = new ParticleSystem.Particle[mist.particleCount];
            int n = mist.GetParticles(buf);
            for (int i = 0; i < n; i++)
            {
                Assert.LessOrEqual(buf[i].position.y, 0.3f + 1e-3f, "sis 0,3 m tavanını aştı");
                Assert.GreaterOrEqual(buf[i].position.y, 0f);
                Assert.LessOrEqual(buf[i].startColor.a / 255f, MomentVfxSystem.MistAlphaCap + 0.01f);
            }

            Assert.AreEqual(ParticleSystemRenderMode.HorizontalBillboard, mist.GetComponent<ParticleSystemRenderer>().renderMode, "yer sisi yatık kart");
            Assert.LessOrEqual(mom.MaterialOf(MomentVfxSystem.Layer.Mist).GetFloat("_AtmoAlpha"), 0.12f + 1e-4f);

            // Door shaft: vertex α × the lamp's +12 % stays ≤ 0.12.
            Mesh shaft = mom.ShaftOf(0).GetComponent<MeshFilter>().sharedMesh;
            foreach (Color c in shaft.colors) Assert.LessOrEqual(c.a * (1f + LanternFlames.LightFlickerAmplitude), MomentVfxSystem.ShaftAlphaCap + 1e-4f, "kapı kartı α ≤ 0,12");

            // M5 curtain and its rings.
            Assert.Greater(mom.CurtainDrops, 0, "aydınlık saçak perdesi yok");
            Assert.LessOrEqual(mom.LiveCurtainDrops, MomentVfxSystem.MaxCurtainDrops, "perde ≤ 40 damla");
            Assert.LessOrEqual(mom.LiveCurtainRings, MomentVfxSystem.MaxCurtainRings, "perde halkası ≤ 12");
            Assert.Greater(mom.CurtainRings, 0, "perde damlası yere halka açmadı");

            // M4 footsteps: ≤ 16 live, on the moment only.
            amb.SystemOf(AmbientVfxSystem.Kind.Rings).Clear();
            float now = Time.time + 100f;
            int accepted = 0;
            for (int i = 0; i < 30; i++) if (mom.NotifyFootstep(O + new Vector3(Mathf.Cos(i) * 3f, 0f, Mathf.Sin(i) * 3f), now)) accepted++;
            Assert.AreEqual(MomentVfxSystem.MaxFootstepRings, accepted, "aynı anda ≤ 16 adım halkası");
            Assert.IsFalse(mom.NotifyFootstep(O + new Vector3(0f, 0f, 60f), now + 5f), "anı dışında adım halkası yok");
            Assert.IsTrue(mom.NotifyFootstep(O, now + FootstepClear()), "ömür dolunca yeni halka");

            ParticleSystem drips = mom.SystemOf(MomentVfxSystem.Layer.Drips);
            var dbuf = new ParticleSystem.Particle[Mathf.Max(1, drips.particleCount)];
            n = drips.GetParticles(dbuf);
            for (int i = 0; i < n; i++) Assert.LessOrEqual(dbuf[i].startColor.a / 255f, MomentVfxSystem.DripAlpha + 0.01f, "damla α ≤ 0,25");
            Debug.Log("[DD-TEST] wp093.moments mist=" + mom.LiveCount(MomentVfxSystem.Layer.Mist) + " petals=" + mom.LiveCount(MomentVfxSystem.Layer.Petals) + " motes=" + mom.LiveCount(MomentVfxSystem.Layer.Motes) +
                      " drips=" + mom.LiveCount(MomentVfxSystem.Layer.Drips) + " curtain=" + mom.CurtainDrops + "/" + mom.CurtainRings + " gusts=" + mom.Gusts);
        }

        private static float FootstepClear() => MomentVfxSystem.FootstepRingLife + 0.01f;

        // ------------------------------------------------------------------ 0 B

        [UnityTest]
        public IEnumerator UcSistem_YuzKare_SifirAyirma()
        {
            _lanterns = Lanterns();
            Fixture(new Vector3(0f, 0f, 0f));
            WorldMoments.Override = new[]
            {
                new MomentSpec { Id = "Z1", Center = new Vector2(O.x, O.z), Radius = 12f, SwingDegrees = 4f, HaloEmissive = true, Shoji = true, FootstepRings = true,
                    Mist = MomentMist.Water, MistColour = Color.white, MistAlpha = 0.08f, MistTop = 0.4f, Hanafubuki = true,
                    Door = true, DoorPoint = O + new Vector3(4f, 0f, 0f), DoorDir = Vector3.right, DoorWidth = 2f, ShaftLength = 3f, Drips = MomentDrips.Curtain, DripRadius = 2.5f, DripRate = 14f },
            };
            Pond(O + new Vector3(0f, -0.02f, 4f), new Vector3(8f, 0.01f, 6f));
            var paper = new Material(PrototypeShaders.Lit) { name = "M_ap023_lantern_paper" };
            var washi = new Material(PrototypeShaders.Lit) { name = "M_ap023_washi" };
            _assets.Add(paper);
            _assets.Add(washi);
            Lamp(_clusters, "Lamp_Z_C1_emissive", O + new Vector3(-2f, 2.5f, 0f), "SM_lantern_chochin_V01_LOD0", 0.4f, paper);
            Lamp(_clusters, "Lamp_Z_A1_baked", O + new Vector3(1f, 0f, -1f), "SM_andon_V01_LOD0", 0.5f);
            Lamp(_clusters, "Lamp_Z_P1_pool", O + new Vector3(3f, 0f, -2f), "SM_lantern_oribe_V01_LOD0", 1.2f);
            var group = new GameObject(WorldMoments.ToroGroupName).transform;
            group.SetParent(_clusters, false);
            Lamp(group, "M8_P01_andon", O + new Vector3(0f, -0.02f, 4f), "SM_andon_V01_LOD0", 0.45f).transform.localScale = Vector3.one * 0.6f;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(_world.transform, false);
            panel.transform.position = O + new Vector3(1f, 1f, -3f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = washi;
            WorldWind.Apply(Vector3.right, 0.7f, 0f);
            AmbientVfxSystem amb = Ambient();
            yield return null;
            Assert.AreEqual(4, _lanterns.Scan());
            MomentVfxSystem mom = MomentVfxSystem.Active;
            Assume.That(mom != null && mom.Ready);
            mom.FocusOverride = O;
            mom.Rescan();
            VfxTier.Override = VfxTierLevel.Cinematic;
            float t = Time.time;
            void Frames(int count)
            {
                for (int f = 0; f < count; f++)
                {
                    t += 1f / 30f;
                    _lanterns.Tick(t);
                    amb.Step(1f / 30f);
                    mom.Step(1f / 30f);
                    if (f % 7 == 0) mom.NotifyFootstep(O + new Vector3(f % 5, 0f, 0f), t);
                }
            }

            Frames(120);   // warm: tiers, light pass + masks, glow / shoji blocks, motion, rings, gust, curtain landings
            mom.TriggerGust();
            yield return null;
            Frames(20);

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() => Frames(100), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "fener + ortam + anı yolu 100 kare ayırdı");
            Assert.IsNotNull(bait);
            GC.KeepAlive(bait);
        }

        // ------------------------------------------------------------------ fixture

        private static LanternFlames Lanterns()
        {
            HitFeedbackDirector director = HitFeedbackDirector.Instance;
            Assume.That(director != null && director.Vfx != null && director.Vfx.IsReady, "AP-010 kataloğu / geri bildirim yönetmeni yok");
            Assume.That(VfxLibrary.TryGetPlayable(VfxIds.LanternFlame, out _), "lantern_flame satırı yok");
            return director.Vfx.Lanterns;
        }

        private int Find(string id)
        {
            for (int i = 0; i < _lanterns.AnchorCount; i++) if (_lanterns.LampIdOf(i) == id) return i;
            return -1;
        }

        /// <summary>World root → "Layout v5 (WP-092)" → Clusters; the player at O, a TD camera, a ground slab; the lantern scan isolated to it.</summary>
        private void Fixture(Vector3 unused)
        {
            _world = new GameObject("WP093 Layout Fixture");
            var layout = new GameObject(WorldMoments.LayoutRootName).transform;
            layout.SetParent(_world.transform, false);
            _clusters = new GameObject("Clusters").transform;
            _clusters.SetParent(layout, false);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(_world.transform, false);
            ground.transform.position = O + new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(120f, 0.2f, 120f);
            _player = new GameObject("WP093 Layout Player");
            _player.transform.position = O;
            EnemyTargeting.SetPlayer(_player.transform);
            _camGo = new GameObject("WP093 Layout Camera");
            _camGo.transform.position = O + new Vector3(0f, 15f, -13f);
            _camGo.transform.rotation = Quaternion.Euler(49f, 0f, 0f);
            FeedbackCameraRef.Set(_camGo.AddComponent<Camera>());
            CameraModeService.Request(CameraMode.TopDown, true);
            _lanterns.ScanRoot = _world.transform;
            Physics.SyncTransforms();
        }

        private GameObject Lamp(Transform parent, string name, Vector3 at, string bodyName, float height, Material material = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = at;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = bodyName;
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(0.3f, height, 0.3f);
            if (material != null) body.GetComponent<MeshRenderer>().sharedMaterial = material;
            return root;
        }

        private void Pond(Vector3 at, Vector3 scale)
        {
            var pond = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pond.GetComponent<Collider>());
            pond.name = "Pond9_Water";
            pond.transform.SetParent(_world.transform, false);
            pond.transform.position = at;
            pond.transform.localScale = scale;
        }

        private AmbientVfxSystem Ambient()
        {
            AmbientVfxSystem sys = AmbientVfxSystem.Attach(_world.transform);
            Assume.That(sys != null && sys.Ready, "DD/VFX Atmo / Flipbook yok");
            sys.ScanRoot = _world.transform;
            sys.Rescan();
            sys.CameraOverride = _camGo.GetComponent<Camera>();
            sys.FocusOverride = O;
            sys.Seed(99u);
            return sys;
        }
    }
}
