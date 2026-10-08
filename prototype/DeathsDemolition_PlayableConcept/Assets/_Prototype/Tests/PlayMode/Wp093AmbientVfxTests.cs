using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Navigation;
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
    /// WP-093 Faz 1b (LOOK-BIBLE §5) · the lantern light follows its flame card's flicker (one sample, same pass: light ±12 %, card α
    /// ±10 %, card scale ±8 %; reduce-flashes scales them) and lives under an active holder, with one halo per flame; warm sparks
    /// inside the tier caps and ≤ 3 per lantern; smoke only from roofs over a room (outside the v6 bible sources), drifting with the
    /// wind, silent while the player stands under that roof or the column nears the camera → player ray; pond / puddle rings inside
    /// their caps (life ≤ 1.8 s, α ≤ 0.35, never at the player); an atmosphere drip that lands in a pond rings it; and 0 B per step for
    /// the ambient system and the lanterns (positive control first). Synthetic worlds; ScanRoot isolates them.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093AmbientVfxTests
    {
        private GameObject _world;
        private GameObject _camGo;
        private GameObject _player;
        private float _flashWas;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
            _flashWas = GameFeelSettings.FlashScale;
            GameFeelSettings.FlashScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
            GameFeelSettings.FlashScale = _flashWas;
            EnemyTargeting.Clear();
            WorldWind.ApplyCalm();
            FeedbackCameraRef.Set(null);
            if (AmbientVfxSystem.Active != null) Object.Destroy(AmbientVfxSystem.Active.gameObject);
            if (AtmosphereSystem.Active != null) Object.Destroy(AtmosphereSystem.Active.gameObject);
            if (_world != null) Object.Destroy(_world);
            if (_camGo != null) Object.Destroy(_camGo);
            if (_player != null) Object.Destroy(_player);
        }

        // ------------------------------------------------------------------ lanterns

        [UnityTest]
        public IEnumerator Fener_IsikYogunlugu_KartTitremesiyleAyniSinyal()
        {
            LanternFlames lanterns = Lanterns();
            BuildLanterns(3);
            yield return null;
            Assert.GreaterOrEqual(lanterns.Scan(), 3, "üç fener bulunmalı");
            Assert.IsNotNull(lanterns.Holder);
            Assert.IsTrue(lanterns.Holder.gameObject.activeInHierarchy, "ışıklar etkin bir köke bağlı olmalı (şablon kökü pasif: ışık hiç çizilmiyordu)");

            float t = Time.time;
            int checkedPairs = 0;
            float minK = float.MaxValue, maxK = float.MinValue;
            var light = new List<float>(256);
            var card = new List<float>(256);
            for (int step = 0; step < 160; step++)
            {
                t += 1.02f / LanternFlames.FlickerHz;   // just past the 20 Hz gate: every tick is a flicker pass
                lanterns.Tick(t);
                for (int slot = 0; slot < LanternFlames.MaxLanternLights; slot++)
                {
                    int a = lanterns.LightAnchorOf(slot);
                    if (a < 0) continue;
                    Light l = lanterns.LightOf(slot);
                    FlipbookEffect f = lanterns.FlameOf(a);
                    if (l == null || f == null || !f.IsSpawned) continue;
                    Assert.IsTrue(l.isActiveAndEnabled, "fener ışığı sahnede etkin değil");
                    float s = lanterns.SignalOf(a);
                    Assert.AreEqual(LanternFlames.FlickerSignal(a, t), s, 1e-4f, "sinyal bu geçişin zamanından");
                    Assert.AreEqual(LanternFlames.LightIntensityFor(s, 1f), l.intensity, 1e-4f, "ışık yoğunluğu kartın sinyalinden");
                    Assert.AreEqual(LanternFlames.CardAlphaFor(s, 1f), f.AlphaCap, 1e-4f, "kart alfası aynı sinyalden");
                    Assert.AreEqual(LanternFlames.FlameScaleFor(s, 1f), f.transform.localScale.x, 1e-4f, "alev ölçeği aynı örnekten (§5 ±%8)");
                    float k = l.intensity / LanternFlames.LanternLightIntensity - 1f;
                    minK = Mathf.Min(minK, k);
                    maxK = Mathf.Max(maxK, k);
                    light.Add(l.intensity);
                    card.Add(f.AlphaCap);
                    checkedPairs++;
                }
            }

            Assert.Greater(checkedPairs, 80, "ışık–kart çifti ölçülemedi");
            Assert.GreaterOrEqual(lanterns.HalosLive, 3, "fener başına bir hale (§5)");
            Assert.GreaterOrEqual(minK, -LanternFlames.LightFlickerAmplitude - 1e-4f);
            Assert.LessOrEqual(maxK, LanternFlames.LightFlickerAmplitude + 1e-4f);
            Assert.Greater(maxK - minK, 0.04f, "ışık titremiyor (sabit ışık)");
            Assert.Greater(Correlation(light, card), 0.999f, "ışık ve kart aynı yönde titremeli");

            // Reduce flashes (S-09 accessibility, FlashScale 0.4): both swings shrink with it.
            GameFeelSettings.FlashScale = 0.4f;
            float maxReduced = 0f;
            for (int step = 0; step < 40; step++)
            {
                t += 1.02f / LanternFlames.FlickerHz;   // just past the 20 Hz gate: every tick is a flicker pass
                lanterns.Tick(t);
                for (int slot = 0; slot < LanternFlames.MaxLanternLights; slot++)
                {
                    Light l = lanterns.LightOf(slot);
                    if (lanterns.LightAnchorOf(slot) < 0 || l == null) continue;
                    maxReduced = Mathf.Max(maxReduced, Mathf.Abs(l.intensity / LanternFlames.LanternLightIntensity - 1f));
                }
            }

            Assert.LessOrEqual(maxReduced, LanternFlames.LightFlickerAmplitude * 0.4f + 1e-4f, "flaş azaltma ışık titremesini kısmalı");
            Debug.Log("[DD-TEST] wp093.lantern pairs=" + checkedPairs + " swing=" + minK.ToString("F3") + ".." + maxK.ToString("F3") + " reduced=" + maxReduced.ToString("F3"));

            Object.Destroy(_world);
            _world = null;
            yield return null;
            lanterns.Scan();   // the session's own lanterns back
        }

        [UnityTest]
        public IEnumerator Fener_Kivilcim_KademeTavani_LambaBasinaUc_SifirAyirma()
        {
            LanternFlames lanterns = Lanterns();
            BuildLanterns(12);
            for (int i = 0; i <= LanternFlames.SettleFrames; i++) yield return null;   // no settle scan left pending to land in the 0 B block
            Assert.GreaterOrEqual(lanterns.Scan(), 12);
            Assert.IsNotNull(lanterns.SparkSystem, "kıvılcım sistemi kurulmadı (DD/VFX Flipbook)");

            float t = Time.time;
            foreach (VfxTierLevel tier in new[] { VfxTierLevel.Performance, VfxTierLevel.High, VfxTierLevel.Cinematic })
            {
                VfxTier.Override = tier;
                int emitted0 = lanterns.SparksEmitted;
                for (int i = 0; i < 600; i++)
                {
                    t += 1f / 60f;
                    lanterns.Tick(t);
                    Assert.LessOrEqual(lanterns.SparksLive, LanternFlames.SparkCap(tier), tier + " kıvılcım tavanı");
                    for (int a = 0; a < lanterns.Placed; a++) Assert.LessOrEqual(lanterns.SparksOf(a, t), LanternFlames.MaxSparksPerLantern, "fener başına ≤ 3");
                }

                if (tier == VfxTierLevel.Performance) Assert.AreEqual(0, lanterns.SparksLive, "Performans: kıvılcım yok");
                else Assert.Greater(lanterns.SparksEmitted - emitted0, 0, tier + ": kıvılcım doğmadı");
                Debug.Log("[DD-TEST] wp093.sparks tier=" + tier + " live=" + lanterns.SparksLive + " emitted=" + (lanterns.SparksEmitted - emitted0));
            }

            // The look: 2–4 cm, warm amber (r > g > b), dim (α ≤ 0.55 — additive, rgb × α).
            ParticleSystem ps = lanterns.SparkSystem;
            var buffer = new ParticleSystem.Particle[ps.particleCount];   // test-side, outside any measurement
            int n = ps.GetParticles(buffer);
            Assert.Greater(n, 0);
            for (int i = 0; i < n; i++)
            {
                Color32 c = buffer[i].startColor;
                Assert.That(buffer[i].startSize, Is.InRange(LanternFlames.SparkSizeMin - 1e-4f, LanternFlames.SparkSizeMax + 1e-4f));
                Assert.Greater(c.r, c.g);
                Assert.Greater(c.g, c.b);
                Assert.LessOrEqual(c.a / 255f, LanternFlames.SparkAlpha + 0.01f);
            }

            // 0 B: 100 lantern ticks with sparks, flicker and the light pass on the calling thread.
            for (int i = 0; i < 30; i++) { t += 1f / 60f; lanterns.Tick(t); }
            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++) { t += 1f / 60f; lanterns.Tick(t); }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "fener titreme + ışık + kıvılcım 0 B / 100 kare");
            Assert.IsNotNull(bait);

            Object.Destroy(_world);
            _world = null;
            yield return null;
            lanterns.Scan();
        }

        [UnityTest]
        public IEnumerator Ap030_SoketYerlesimi_PisirilmisAileKarakterIsigi_ParlamaAyniOrnek()
        {
            LanternFlames lanterns = Lanterns();
            BuildLanterns(1);   // one lantern_kasuga (no sockets: the heuristic, outdoor, full mask)
            var made = new List<Object>();
            try
            {
                // An AP-030 andon: SOCKET_Flame / SOCKET_Light children, a two-slot body whose second slot is the glow material.
                var lamp = new GameObject("SM_andon_V01");
                lamp.transform.SetParent(_world.transform, false);
                lamp.transform.position = new Vector3(502f, 0f, 500f);
                lamp.AddComponent<SurfaceTag>().Family = "andon";
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(lamp.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                body.transform.localScale = new Vector3(0.35f, 0.6f, 0.35f);
                MeshFilter mf = body.GetComponent<MeshFilter>();
                Mesh mesh = Object.Instantiate(mf.sharedMesh);
                made.Add(mesh);
                int[] tris = mesh.triangles;
                int half = tris.Length / 2 / 3 * 3;
                var a0 = new int[half];
                var a1 = new int[tris.Length - half];
                Array.Copy(tris, 0, a0, 0, half);
                Array.Copy(tris, half, a1, 0, a1.Length);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(a0, 0);
                mesh.SetTriangles(a1, 1);
                mf.sharedMesh = mesh;
                var paper = new Material(PrototypeShaders.Lit) { name = "AP030_M_andon_paper_v001" };
                var glow = new Material(PrototypeShaders.Lit) { name = "AP030_M_andon_glow_v001" };
                glow.SetColor("_EmissionColor", new Color(1.2f, 0.7f, 0.35f, 1f));
                made.Add(paper);
                made.Add(glow);
                body.GetComponent<MeshRenderer>().sharedMaterials = new[] { paper, glow };
                var flameSocket = new GameObject(LanternFlames.FlameSocketName).transform;
                flameSocket.SetParent(lamp.transform, false);
                flameSocket.localPosition = new Vector3(0f, 0.42f, 0f);
                var lightSocket = new GameObject(LanternFlames.LightSocketName).transform;
                lightSocket.SetParent(lamp.transform, false);
                lightSocket.localPosition = new Vector3(0f, 0.36f, 0.04f);
                yield return null;

                Assert.GreaterOrEqual(lanterns.Scan(), 2);
                int andon = -1, kasuga = -1;
                for (int i = 0; i < lanterns.AnchorCount; i++)
                {
                    if ((lanterns.FlamePointOf(i) - flameSocket.position).sqrMagnitude < 1e-6f) andon = i;
                    else if ((lanterns.FlamePointOf(i) - new Vector3(500f, 0.7f, 500f)).sqrMagnitude < 0.25f) kasuga = i;
                }

                Assert.GreaterOrEqual(andon, 0, "alev SOCKET_Flame'de değil");
                Assert.GreaterOrEqual(kasuga, 0, "soketsiz fener sezgisel yerinde değil");
                Assert.Less((lanterns.LightPointOf(andon) - lightSocket.position).magnitude, 1e-3f, "ışık merkezi SOCKET_Light'ta değil");
                Assert.IsTrue(lanterns.IsBaked(andon), "andon pişirilmiş aile");
                Assert.IsFalse(lanterns.IsBaked(kasuga), "kasuga açık hava feneri");
                int glowSlot = -1;
                for (int k = 0; k < lanterns.GlowSlots; k++) if (lanterns.GlowAnchorOf(k) == andon) glowSlot = k;
                Assert.GreaterOrEqual(glowSlot, 0, "AP030_M_andon_glow_v001 yuvası bulunmadı");
                Assert.AreEqual(1, lanterns.GlowMaterialIndexOf(glowSlot), "yalnız parlama yuvası");

                float t = Time.time;
                var block = new MaterialPropertyBlock();
                int checkedAndon = 0, checkedKasuga = 0;
                for (int step = 0; step < 60; step++)
                {
                    t += 1.02f / LanternFlames.FlickerHz;
                    lanterns.Tick(t);
                    FlipbookEffect card = lanterns.FlameOf(andon);
                    Assert.IsNotNull(card);
                    Assert.Less((card.transform.position - flameSocket.position).magnitude, LanternFlames.SwayMetres + 0.005f, "alev kartı sokette (+ salınım)");

                    float s = lanterns.SignalOf(andon);
                    Renderer r = lanterns.GlowRendererOf(glowSlot);
                    r.GetPropertyBlock(block, 1);
                    Color e = block.GetColor(LanternFlames.GlowEmissionProperty);
                    Color expect = LanternFlames.GlowEmissionFor(lanterns.GlowBaseOf(glowSlot), s, 1f);
                    Assert.AreEqual(expect.r, e.r, 1e-4f, "parlama yuvası aynı titreme örneğinden");
                    Assert.AreEqual(expect.b, e.b, 1e-4f);
                    r.GetPropertyBlock(block, 0);
                    Assert.IsTrue(block.isEmpty, "gövde yuvasına dokunulmamalı");

                    for (int slot = 0; slot < LanternFlames.MaxLanternLights; slot++)
                    {
                        int a = lanterns.LightAnchorOf(slot);
                        Light l = lanterns.LightOf(slot);
                        if (a < 0 || l == null) continue;
                        uint? layers = LanternFlames.RenderingLayersOf(l);
                        Assert.IsTrue(layers.HasValue, "URP ışık verisi yok (ışık katmanı yazılamaz)");
                        if (a == andon)
                        {
                            Assert.Less((l.transform.position - lightSocket.position).magnitude, LanternFlames.SwayMetres * 0.6f + 0.005f, "ışık SOCKET_Light'ta");
                            Assert.AreEqual(LanternFlames.CharactersOnlyRenderingLayers, layers.Value, "pişirilmiş aile: yalnız Characters katmanı");
                            Assert.AreEqual((int)LanternFlames.CharactersOnlyRenderingLayers, l.renderingLayerMask);
                            // Glow and light: the same sample (both swing ±12 %).
                            float kLight = l.intensity / LanternFlames.LanternLightIntensity - 1f;
                            float kGlow = e.r / lanterns.GlowBaseOf(glowSlot).r - 1f;
                            Assert.AreEqual(kLight, kGlow, 1e-3f, "parlama ve ışık aynı örnek");
                            checkedAndon++;
                        }
                        else if (a == kasuga)
                        {
                            Assert.AreEqual(LanternFlames.FullRenderingLayers, layers.Value, "açık hava feneri tam maske");
                            checkedKasuga++;
                        }
                    }
                }

                Assert.Greater(checkedAndon, 10, "andon ışığı atanmadı");
                Assert.Greater(checkedKasuga, 10, "kasuga ışığı atanmadı");
            }
            finally
            {
                Object.Destroy(_world);
                _world = null;
                foreach (Object o in made) Object.Destroy(o);
            }

            yield return null;
            lanterns.Scan();
        }

        // ------------------------------------------------------------------ smoke and rings

        [UnityTest]
        public IEnumerator Dunya_Taranir_DumanYalnizOdaliCatidan_HalkalarTavanda()
        {
            BuildWorld(false);
            AmbientVfxSystem sys = Attach();
            Assume.That(sys != null && sys.Ready, "DD/VFX Atmo / Flipbook yok");
            yield return null;

            Assert.AreEqual(1, sys.Ponds, "bir gölet");
            Assert.AreEqual(1, sys.SmokeSourceCount, "yalnız altında oda olan çatı tüter");
            Assert.Greater(sys.PuddlePoints, 0, "meydan birikintisi noktası yok");
            for (int i = 0; i < sys.PuddlePoints; i++)
            {
                Vector3 p = sys.PuddlePoint(i);
                Assert.IsTrue(sys.InPuddle(p.x, p.z), "birikinti noktası birikintide değil");
                Vector4 r = AmbientVfxSystem.PlazaRects[0];
                Assert.IsTrue(p.x > r.x && p.x < r.z && p.z > r.y && p.z < r.w, "birikinti noktası meydanın dışında");
            }

            Assert.LessOrEqual(sys.MaterialOf(AmbientVfxSystem.Kind.Smoke).GetFloat("_AtmoAlpha"), 0.12f + 1e-4f, "duman α tavanı ≤ 0,12");
            Assert.LessOrEqual(sys.MaterialOf(AmbientVfxSystem.Kind.Rings).GetFloat("_AlphaCap"), 0.35f + 1e-4f, "halka α tavanı ≤ 0,35");

            foreach (VfxTierLevel tier in new[] { VfxTierLevel.Performance, VfxTierLevel.High, VfxTierLevel.Cinematic })
            {
                VfxTier.Override = tier;
                for (int i = 0; i < 150; i++) sys.Step(1f / 30f);
                Assert.AreEqual(AmbientVfxSystem.SmokeCap(tier), sys.CapOf(AmbientVfxSystem.Kind.Smoke));
                Assert.LessOrEqual(sys.LiveCount(AmbientVfxSystem.Kind.Smoke), AmbientVfxSystem.SmokeCap(tier), tier + " duman tavanı");
                Assert.LessOrEqual(sys.LiveCount(AmbientVfxSystem.Kind.Rings), AmbientVfxSystem.RingCap(tier), tier + " halka tavanı");
                Assert.AreEqual(1, sys.ActiveSmokeSources, tier + ": tüten çatı");
                Assert.Greater(sys.LiveCount(AmbientVfxSystem.Kind.Smoke), 0, tier + ": duman yok");
                Debug.Log("[DD-TEST] wp093.ambient tier=" + tier + " smoke=" + sys.LiveCount(AmbientVfxSystem.Kind.Smoke) + " rings=" + sys.LiveCount(AmbientVfxSystem.Kind.Rings) +
                          " pond=" + sys.Rings(AmbientVfxSystem.RingCause.PondDrop) + " puddle=" + sys.Rings(AmbientVfxSystem.RingCause.PuddleDrop) +
                          " puddlePoints=" + sys.PuddlePoints + " underEaves=" + sys.PuddlesUnderEaves);
                Assert.LessOrEqual(sys.LivePuddleRings, AmbientVfxSystem.MaxPuddleRings, "§5: karede ≤ 12 birikinti halkası");
            }

            Assert.Greater(sys.Rings(AmbientVfxSystem.RingCause.PondDrop), 0, "gölet damlası halkası yok");
            Assert.Greater(sys.Rings(AmbientVfxSystem.RingCause.PuddleDrop), 0, "birikinti damlası halkası yok");

            // Rings: life ≤ 1.8 s (§5 pond), α ≤ 0.35, on the surface, never within 1 m of the player.
            ParticleSystem rings = sys.SystemOf(AmbientVfxSystem.Kind.Rings);
            var buffer = new ParticleSystem.Particle[Mathf.Max(1, rings.particleCount)];
            int n = rings.GetParticles(buffer);
            Assert.Greater(n, 0);
            for (int i = 0; i < n; i++)
            {
                Assert.LessOrEqual(buffer[i].startLifetime, AmbientVfxSystem.RingLifeMax + 1e-4f);
                Assert.LessOrEqual(buffer[i].startColor.a / 255f, AmbientVfxSystem.RingAlphaCap + 0.01f);
                Vector3 p = buffer[i].position;
                float dx = p.x - _player.transform.position.x, dz = p.z - _player.transform.position.z;
                Assert.GreaterOrEqual(dx * dx + dz * dz, AmbientVfxSystem.RingPlayerClear * AmbientVfxSystem.RingPlayerClear - 1e-3f, "oyuncunun dibinde halka");
                Assert.Less(p.y, 0.1f, "halka yüzeyde değil");
            }

            // Smoke: from the roof over the room (ridge 4.25 m), α 0.06–0.10 (cap 0.12), gone by 7 m over the floor.
            ParticleSystem smoke = sys.SystemOf(AmbientVfxSystem.Kind.Smoke);
            var wisps = new ParticleSystem.Particle[smoke.particleCount];
            n = smoke.GetParticles(wisps);
            for (int i = 0; i < n; i++)
            {
                Assert.LessOrEqual(wisps[i].startColor.a / 255f, AmbientVfxSystem.SmokeAlphaMax + 0.01f);
                Assert.LessOrEqual(wisps[i].position.y + wisps[i].velocity.y * wisps[i].startLifetime, AmbientVfxSystem.SmokeCeiling + 0.5f, "duman 7 m'yi aşıyor");
                Assert.Greater(wisps[i].position.y, 3.9f, "duman çatı sırtının altında");
                Assert.Less(Mathf.Abs(wisps[i].position.x - -8f), 6f, "duman odasız çatıdan çıktı");
            }
        }

        [UnityTest]
        public IEnumerator Duman_RuzgarlaSuruklenir_KameraIsinindaVeCatiAltindaSusar()
        {
            BuildWorld(false);
            AmbientVfxSystem sys = Attach();
            Assume.That(sys != null && sys.Ready);
            yield return null;
            VfxTier.Override = VfxTierLevel.Cinematic;
            WorldWind.Apply(Vector3.forward, 1f, 0f);   // +z, full strength (no Update runs inside this loop: nothing overwrites it)
            sys.Step(1f / 30f);
            sys.SystemOf(AmbientVfxSystem.Kind.Smoke).Clear();
            for (int i = 0; i < 60; i++) sys.Step(1f / 30f);

            ParticleSystem smoke = sys.SystemOf(AmbientVfxSystem.Kind.Smoke);
            var wisps = new ParticleSystem.Particle[smoke.particleCount];
            int n = smoke.GetParticles(wisps);
            Assert.Greater(n, 0);
            float vz = 0f, vx = 0f;
            for (int i = 0; i < n; i++) { vz += wisps[i].velocity.z; vx += wisps[i].velocity.x; }
            vz /= n;
            vx /= n;
            // A small start drift with the wind (the WindZone keeps pushing as it climbs: §5 "bends after 2 m").
            Assert.Greater(vz, 0.04f, "duman rüzgârla sürüklenmiyor (ort. vz " + vz + ")");
            Assert.Less(Mathf.Abs(vx), 0.03f);

            // §5: the column (ridge → 7 m) comes within 4 m of the camera → player ray: that source stops at the next refresh.
            _player.transform.position = new Vector3(-8f, 0f, -1f);
            _camGo.transform.position = new Vector3(-8f, 15f, 14f);
            sys.FocusOverride = _player.transform.position;
            for (int i = 0; i < 20; i++) sys.Step(1f / 30f);
            Assert.Less(AmbientVfxSystem.ColumnNearRay(sys.SmokeSourcePoint(0), 7f, _camGo.transform.position, _player.transform.position + Vector3.up * 1.1f), AmbientVfxSystem.SmokeRayClear);
            Assert.AreEqual(0, sys.ActiveSmokeSources, "kamera → oyuncu ışınına 4 m'den yakın duman");
            _camGo.transform.position = new Vector3(0f, 15f, -13f);

            // The player walks under the smoking roof (TD cut-away): that roof stops at the next refresh.
            _player.transform.position = new Vector3(-8f, 0f, 8f);
            sys.FocusOverride = _player.transform.position;
            for (int i = 0; i < 20; i++) sys.Step(1f / 30f);
            Assert.AreEqual(0, sys.ActiveSmokeSources, "oyuncu çatı altındayken duman sürüyor");
        }

        [UnityTest]
        public IEnumerator Damla_SudaHalka_KuruZemindeYok_PerformanstaOlayYok()
        {
            BuildWorld(false);
            AmbientVfxSystem sys = Attach();
            Assume.That(sys != null && sys.Ready);
            yield return null;
            Vector3 far = new Vector3(500f, 0f, 500f);

            VfxTier.Override = VfxTierLevel.Performance;
            for (int i = 0; i < 30; i++) sys.Step(1f / 30f);
            Assert.IsFalse(sys.NotifyLanding(new Vector3(10f, 0f, -8f), false, far), "Performans: olay halkası yok");

            VfxTier.Override = VfxTierLevel.High;
            for (int i = 0; i < 30; i++) sys.Step(1f / 30f);   // the event bucket fills (4 / s)
            int drips = sys.Rings(AmbientVfxSystem.RingCause.Drip);
            Assert.IsTrue(sys.NotifyLanding(new Vector3(10.5f, 0f, -8.2f), false, far), "gölete düşen damla halka açmalı");
            Assert.AreEqual(drips + 1, sys.Rings(AmbientVfxSystem.RingCause.Drip));
            Assert.IsFalse(sys.NotifyLanding(new Vector3(25f, 0f, -25f), false, far), "kuru toprak: halka yok");
            Vector3 puddle = sys.PuddlePoint(0);
            Assert.IsTrue(sys.NotifyLanding(puddle, false, far), "birikintiye düşen damla halka açmalı");
            Assert.IsFalse(sys.NotifyLanding(puddle, false, puddle), "oyuncunun dibinde halka yok");
        }

        [UnityTest]
        public IEnumerator Atmosfer_SacakDamlasi_GoleteDuser_Halka()
        {
            BuildWorld(true);
            AtmosphereSystem atmo = AtmosphereSystem.Attach(_world.transform);
            Assume.That(atmo != null && atmo.Ready, "DD/VFX Atmo yok");
            atmo.ScanRoot = _world.transform;
            atmo.Rescan();
            atmo.CameraOverride = _camGo.GetComponent<Camera>();
            atmo.FocusOverride = Vector3.zero;
            atmo.Seed(777u);
            AmbientVfxSystem sys = Attach();
            Assume.That(sys != null && sys.Ready);
            yield return null;
            Assert.GreaterOrEqual(atmo.AnchorCount(AtmosphereSystem.Source.Eave), 8, "iki çatı, sekiz saçak");

            VfxTier.Override = VfxTierLevel.Cinematic;
            int writes0 = atmo.DripLandingWrites;
            int dripRings0 = sys.Rings(AmbientVfxSystem.RingCause.Drip);
            // Deterministic, not raced against frame time: everything runs inside this one frame on a fixed dt and a fixed seed, so no
            // particle ever dies on its own — the drip layer would sit at its cap after the prefill and log only ≈ 19 drips (pm3). Every
            // 15 steps (0.5 s, about half a drip's 0.9 s fall) the drip and ring layers are cleared, as the simulation would retire them,
            // so emission keeps running. Each new log entry is read the step it is written; the ones over Pond1 that have landed by the
            // end are the expected rings.
            const float dt = 1f / 30f;
            int read = writes0, onPond = 0, landedOnPond = 0;
            var pondLandings = new List<float>(256);
            for (int i = 0; i < 180; i++)
            {
                if (i % 15 == 14)
                {
                    atmo.SystemOf(AtmosphereSystem.Layer.Drips).Clear();
                    sys.SystemOf(AmbientVfxSystem.Kind.Rings).Clear();
                }

                atmo.Step(dt);
                for (; read < atmo.DripLandingWrites; read++)
                {
                    Vector4 l = atmo.DripLanding(read);
                    Assert.That(l.w - atmo.Clock, Is.InRange(0f, 1.3f), "iniş zamanı damlanın ömrü içinde");
                    Assert.AreEqual(0f, l.y, 0.05f, "iniş yüksekliği saçak altındaki zemin");
                    float dx = (l.x - 8f) / 3.4f, dz = (l.z - 11.5f) / 1.4f;   // Pond1 ellipse (semi 3.5 × 1.5) less the 0.1 m inset
                    if (dx * dx + dz * dz <= 1f) { onPond++; pondLandings.Add(l.w); }
                }

                sys.Step(dt);
            }

            foreach (float w in pondLandings) if (w <= atmo.Clock) landedOnPond++;
            int writes = atmo.DripLandingWrites - writes0, dripRings = sys.Rings(AmbientVfxSystem.RingCause.Drip) - dripRings0;
            Debug.Log("[DD-TEST] wp093.drips writes=" + writes + " onPond=" + onPond + " landedOnPond=" + landedOnPond + " dripRings=" + dripRings +
                      " leafRings=" + sys.Rings(AmbientVfxSystem.RingCause.Leaf));
            Assert.Greater(writes, 100, "damla iniş kaydı yok (" + writes + ")");
            Assert.Greater(landedOnPond, 0, "kurgu: Gate saçağının damlası Pond1'e düşmeli");
            Assert.Greater(dripRings, 0, "saçaktan göle düşen damla halka açmadı (" + landedOnPond + " iniş)");
            // Only the event bucket (Cinematic 8 / s, burst 4; shared with puddle drips, which count as Drip too) holds a ring back.
            Assert.GreaterOrEqual(dripRings, Mathf.Min(landedOnPond, 8), "göle inen damlaların çoğu halka açmalı");
        }

        [UnityTest]
        public IEnumerator YuzKare_SifirAyirma_IsParcacigiFiltreli()
        {
            BuildWorld(true);
            AtmosphereSystem atmo = AtmosphereSystem.Attach(_world.transform);
            Assume.That(atmo != null && atmo.Ready);
            atmo.ScanRoot = _world.transform;
            atmo.Rescan();
            atmo.CameraOverride = _camGo.GetComponent<Camera>();
            atmo.FocusOverride = Vector3.zero;
            AmbientVfxSystem sys = Attach();
            Assume.That(sys != null && sys.Ready);
            yield return null;
            VfxTier.Override = VfxTierLevel.Cinematic;
            for (int i = 0; i < 90; i++) { atmo.Step(1f / 30f); sys.Step(1f / 30f); }   // warm: tier, ranges, every path once (smoke, drops, drips, leaf sampling)
            yield return null;

            object[] keep = null;
            Assert.That(() => { keep = new object[4000]; for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64]; },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "kayıtçı 256 KB yemi görmedi — ölçüm geçersiz");
            GC.KeepAlive(keep);

            Assert.That(() =>
            {
                for (int f = 0; f < 100; f++) { atmo.Step(1f / 30f); sys.Step(1f / 30f); }   // includes 6 range refreshes and 13 leaf samples
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Ortam VFX adımı yönetilen bellek ayırdı (100 kare).");
        }

        // ------------------------------------------------------------------ fixture

        private static LanternFlames Lanterns()
        {
            HitFeedbackDirector director = HitFeedbackDirector.Instance;
            Assume.That(director != null && director.Vfx != null && director.Vfx.IsReady, "AP-010 kataloğu / geri bildirim yönetmeni yok");
            Assume.That(VfxLibrary.TryGetPlayable(VfxIds.LanternFlame, out _), "lantern_flame satırı yok");
            return director.Vfx.Lanterns;
        }

        /// <summary><paramref name="count"/> stone lanterns (SurfaceTag lantern_kasuga, 1.4 m body) 1.5 m apart near (500, 0, 500), a camera 8 m off.</summary>
        private void BuildLanterns(int count)
        {
            EnemyTargeting.Clear();
            _world = new GameObject("WP093 Lantern World");
            for (int i = 0; i < count; i++)
            {
                var root = new GameObject("lantern_kasuga_" + i);
                root.transform.SetParent(_world.transform, false);
                root.transform.position = new Vector3(500f + (i % 4) * 1.5f, 0f, 500f + (i / 4) * 1.5f);
                root.AddComponent<SurfaceTag>().Family = "lantern_kasuga";
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                body.transform.localScale = new Vector3(0.5f, 1.4f, 0.5f);
            }

            _camGo = new GameObject("WP093 Lantern Camera");
            _camGo.transform.position = new Vector3(502f, 6f, 494f);
            _camGo.transform.LookAt(new Vector3(502f, 0.7f, 501f));
            FeedbackCameraRef.Set(_camGo.AddComponent<Camera>());
        }

        private AmbientVfxSystem Attach()
        {
            AmbientVfxSystem sys = AmbientVfxSystem.Attach(_world.transform);
            if (sys == null) return null;
            sys.ScanRoot = _world.transform;
            if (sys.Ready) sys.Rescan();   // an Active left by an earlier world may have scanned already (Start rescans: same result)
            sys.CameraOverride = _camGo.GetComponent<Camera>();
            sys.FocusOverride = Vector3.zero;
            sys.Seed(4242u);
            return sys;
        }

        /// <summary>
        /// A 70 m ground slab; the wet plaza (Plaza_Slate over PlazaRects[0]); a pond disc (10, −8) 8 × 5 m; a roof over a Room
        /// (−8, 4, 8) and one without (8, 4, 14) — CameraOccluderClass Roof on a box collider; the player at the origin; a TD camera
        /// (49°). <paramref name="eavePond"/>: a second pond under the room-less roof's south eave and a sakura crown over it.
        /// </summary>
        private void BuildWorld(bool eavePond)
        {
            _world = new GameObject("WP093 Ambient World");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(_world.transform, false);
            ground.transform.localScale = new Vector3(70f, 0.2f, 70f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);

            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plaza.name = AtmosphereSystem.PlazaName;
            Object.DestroyImmediate(plaza.GetComponent<Collider>());
            plaza.transform.SetParent(_world.transform, false);
            plaza.transform.position = new Vector3(0f, 0.006f, 2.85f);
            plaza.transform.localScale = new Vector3(32f, 0.012f, 33.7f);

            Pond("Pond0_Water", new Vector3(10f, 0.02f, -8f), new Vector3(8f, 0.01f, 5f));

            Roof("Hall_Roof", new Vector3(-8f, 4f, 8f), new Vector3(8f, 0.5f, 6f));
            var room = new GameObject("Hall_Room");
            room.transform.SetParent(_world.transform, false);
            room.transform.position = new Vector3(-8f, 0f, 8f);
            room.AddComponent<NavRegionMarker>().Configure("wp093-room", NavRegionKind.Room, new Vector3(6f, 3f, 4f), null, 1f, 0, false, Vector3.zero, "wp093-room");

            Roof("Gate_Roof", new Vector3(8f, 4f, 14f), new Vector3(8f, 0.5f, 5f));

            if (eavePond)
            {
                Pond("Pond1_Water", new Vector3(8f, 0.02f, 11.5f), new Vector3(7f, 0.01f, 3f));
                var tree = new GameObject("SM_sakura_v2_V09");
                tree.transform.SetParent(_world.transform, false);
                tree.transform.position = new Vector3(10f, 0f, -8f);
                tree.AddComponent<LODGroup>();
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(crown.GetComponent<Collider>());
                crown.transform.SetParent(tree.transform, false);
                crown.transform.localPosition = new Vector3(0f, 3f, 0f);
                crown.transform.localScale = new Vector3(4f, 3f, 4f);
            }

            _player = new GameObject("WP093 Player");
            _player.transform.position = Vector3.zero;
            EnemyTargeting.SetPlayer(_player.transform);

            _camGo = new GameObject("WP093 Camera");
            _camGo.transform.position = new Vector3(0f, 15f, -13f);
            _camGo.transform.rotation = Quaternion.Euler(49f, 0f, 0f);
            _camGo.AddComponent<Camera>();
            CameraModeService.Request(CameraMode.TopDown, true);
            Physics.SyncTransforms();
        }

        // Colliders go with DestroyImmediate: the scans (eave raycasts) run in this same frame, and a deferred Destroy left the pond
        // cylinder's CapsuleCollider alive — scaled (7, 0.01, 3) it is a 3.5 m-radius sphere whose top (y 3.52) caught the Gate roof's
        // south-eave ray (eave y 3.75), so AddEave saw a 0.13 m drop and skipped that edge (7 eaves, PM run 1).
        private void Pond(string name, Vector3 at, Vector3 scale)
        {
            var pond = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pond.name = name;
            Object.DestroyImmediate(pond.GetComponent<Collider>());
            pond.transform.SetParent(_world.transform, false);
            pond.transform.position = at;
            pond.transform.localScale = scale;
        }

        private void Roof(string name, Vector3 at, Vector3 scale)
        {
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);   // keeps its BoxCollider: the v6 RoofOccluder shape
            roof.name = name;
            roof.transform.SetParent(_world.transform, false);
            roof.transform.position = at;
            roof.transform.localScale = scale;
            roof.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Roof;
        }

        private static float Correlation(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n;
            mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sab += da * db;
                saa += da * da;
                sbb += db * db;
            }

            return (float)(sab / Math.Sqrt(Math.Max(saa * sbb, 1e-20)));
        }
    }
}
