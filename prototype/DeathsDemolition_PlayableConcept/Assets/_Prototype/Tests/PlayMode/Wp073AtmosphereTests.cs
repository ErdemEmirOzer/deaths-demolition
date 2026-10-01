using System;
using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rendering;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 B5 · atmosphere v3: the tier table against the contract (QUALITY-TIERS-V2 §4.2, direction §5, DEC-100), the procedural
    /// atlas, the world scan (sakura crown, bamboo clump, pond disc, roof eaves, S-05 shaft cards), live counts and draws per tier on a
    /// synthetic world, the near field never inside 0.6 m of the lens, the legacy WP-013 drift silenced but playing, the shaft globals,
    /// and 0 B for 100 frames of <see cref="AtmosphereSystem.Step"/> on the calling thread (positive control first).
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073AtmosphereTests
    {
        private GameObject _world;
        private GameObject _camGo;
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.High;
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
            EnemyTargeting.Clear();
            CameraModeService.Request(CameraMode.TopDown, true);
            if (AtmosphereSystem.Active != null) Object.Destroy(AtmosphereSystem.Active.gameObject);
            if (_world != null) Object.Destroy(_world);
            if (_camGo != null) Object.Destroy(_camGo);
            if (_player != null) Object.Destroy(_player);
            Shader.SetGlobalVector("_DD_VL_Density", Vector4.zero);   // the DEC-102 field off again (as VolumetricLight.OnDisable)
            Shader.SetGlobalFloat("_DD_VL_RegionCount", 0f);
        }

        // DEC-102 (S-05 205ef275): while the volumetric field is live its regions are the mote anchors and the cards are off; with
        // the field off (Performance) the cards come back. The field is published as globals (DD.Art references this assembly).
        [UnityTest]
        public IEnumerator Dec102_AlanCanliyken_BolgelerCapa_KartlarKapali()
        {
            BuildWorld(3);
            AtmosphereSystem sys = Attach();
            yield return null;   // Start → Rescan
            Assert.AreEqual(AtmosphereSystem.MaxVolumeRegions, sys.AnchorCount(AtmosphereSystem.Source.Volume), "8 bölge yeri ayrıldı");

            var regions = new Vector4[AtmosphereSystem.MaxVolumeRegions];
            var shapes = new Vector4[AtmosphereSystem.MaxVolumeRegions];
            regions[0] = new Vector4(-8f, 2f, 6f, 6f);  shapes[0] = new Vector4(3f, 0.02f, 0f, 0f);   // a grove
            regions[1] = new Vector4(8f, 2f, -6f, 5f);  shapes[1] = new Vector4(3f, 0.01f, 0f, 0f);   // a pond
            Shader.SetGlobalVectorArray("_DD_VL_Regions", regions);
            Shader.SetGlobalVectorArray("_DD_VL_RegionShape", shapes);
            Shader.SetGlobalFloat("_DD_VL_RegionCount", 2f);
            Shader.SetGlobalVector("_DD_VL_Density", new Vector4(0.0025f, 0.18f, 0f, 1f));
            Assert.IsTrue(AtmosphereSystem.FieldLive);

            for (int i = 0; i < 60; i++) sys.Step(1f / 30f);
            yield return null;
            Assert.AreEqual(2, sys.VolumeRegions, "iki bölge okundu");
            Assert.AreEqual(2, sys.InRangeCount(AtmosphereSystem.Source.Volume), "alan canlı: bölgeler menzilde");
            Assert.AreEqual(0, sys.InRangeCount(AtmosphereSystem.Source.Shaft), "alan canlı: kartlar kapalı");
            Assert.Greater(sys.LiveCount(AtmosphereSystem.Layer.Motes), 0);

            // AD B5 MUST 2: two populations — the Pollen frame is 4–5 cm pollen, the placed dust 2–3 cm (the near field keeps its
            // finer motes on the Mote frame).
            ParticleSystem motes = sys.SystemOf(AtmosphereSystem.Layer.Motes);
            var grains = new ParticleSystem.Particle[motes.particleCount];
            int n = motes.GetParticles(grains), pollen = 0, pollenInRange = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 s3 = grains[i].startSize3D;
                if (Mathf.RoundToInt(s3.z) != AtmosphereAtlas.Pollen) continue;
                pollen++;
                if (s3.y >= 0.0399f && s3.y <= 0.0501f) pollenInRange++;
            }

            Assert.Greater(pollen, 0, "polen yok");
            Assert.AreEqual(pollen, pollenInRange, "polen 4–5 cm");

            Shader.SetGlobalVector("_DD_VL_Density", Vector4.zero);   // Performance: the field is off
            for (int i = 0; i < 30; i++) sys.Step(1f / 30f);
            Assert.AreEqual(0, sys.InRangeCount(AtmosphereSystem.Source.Volume), "alan kapalı: bölge yok");
            Assert.AreEqual(3, sys.InRangeCount(AtmosphereSystem.Source.Shaft), "alan kapalı: kartlar döndü");
        }

        [Test]
        public void KademeTablosu_SozlesmeVeCizimSayisi()
        {
            Assert.AreEqual(600, AtmosphereSystem.TotalTarget(VfxTierLevel.Performance), "Performans 600 canlı (§5)");
            Assert.AreEqual(1800, AtmosphereSystem.TotalTarget(VfxTierLevel.High), 40, "Yüksek ≈ 1 800 canlı (§5 · 0,60 ms)");
            Assert.GreaterOrEqual(AtmosphereSystem.TotalTarget(VfxTierLevel.Cinematic), 3000, "Sinematik ≥ r1 3 000 (DEC-100 cömertleştirir)");
            Assert.AreEqual(4, AtmosphereSystem.DrawsFor(VfxTierLevel.Performance));
            Assert.AreEqual(5, AtmosphereSystem.DrawsFor(VfxTierLevel.High));
            Assert.AreEqual(5, AtmosphereSystem.DrawsFor(VfxTierLevel.Cinematic), "≤ 6 draw (§3)");
            foreach (VfxTierLevel t in new[] { VfxTierLevel.Performance, VfxTierLevel.High, VfxTierLevel.Cinematic })
                Assert.Less(AtmosphereSystem.NearShare(t), AtmosphereSystem.TargetLive(AtmosphereSystem.Layer.Motes, t), "yakın alan tane payının içinde");
            Assert.AreEqual(0, AtmosphereSystem.TargetLive(AtmosphereSystem.Layer.Drips, VfxTierLevel.Performance), "Performans: damla yok");
        }

        [Test]
        public void Atlas_SekizHucre_MerkezDoluKenarBos()
        {
            for (int f = 0; f < 8; f++)
            {
                AtmosphereAtlas.Shade(f, f == AtmosphereAtlas.LeafSliver || f == AtmosphereAtlas.Drip ? 0.2f : 0f, f == AtmosphereAtlas.PetalYoung || f == AtmosphereAtlas.PetalAged ? 0.2f : 0f, out _, out float centre);
                AtmosphereAtlas.Shade(f, 0.98f, 0.98f, out _, out float corner);
                Assert.Greater(centre, 0.3f, "hücre " + f + " merkezi boş");
                Assert.Less(corner, 0.05f, "hücre " + f + " köşesi dolu (mip komşuya taşar)");
            }

            Texture2D tex = AtmosphereAtlas.Build();
            try
            {
                Assert.AreEqual(512, tex.width);
                Assert.AreEqual(256, tex.height);
                Assert.Greater(tex.mipmapCount, 1, "mip zinciri yok: uzak taneler titrer");
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        [Test]
        public void Shader_KitapliktanGelir_BulutCerezSozlesmesiniOkur()
        {
            Assert.IsTrue(PrototypeShaders.IsReady, "shader kitaplığı eksik");
            Assert.AreEqual(AtmosphereSystem.ShaderName, PrototypeShaders.VfxAtmo.name);
            Assert.IsTrue(PrototypeShaders.VfxAtmo.isSupported);
        }

        [UnityTest]
        public IEnumerator Dunya_Taranir_KademeBasinaCanliVeCizim()
        {
            BuildWorld(3);
            AtmosphereSystem sys = Attach();
            Assert.IsTrue(sys.Ready, "DD/VFX Atmo hazır değil");
            yield return null;   // Start → Rescan

            Assert.AreEqual(2, sys.AnchorCount(AtmosphereSystem.Source.Sakura), "iki sakura tacı");
            Assert.AreEqual(1, sys.AnchorCount(AtmosphereSystem.Source.Bamboo), "bir bambu öbeği");
            Assert.AreEqual(1, sys.AnchorCount(AtmosphereSystem.Source.BambooTip));
            Assert.AreEqual(1, sys.AnchorCount(AtmosphereSystem.Source.Water), "gölet diski");
            Assert.AreEqual(4, sys.AnchorCount(AtmosphereSystem.Source.Eave), "çatı saçağı dört kenar");
            Assert.AreEqual(3, sys.AnchorCount(AtmosphereSystem.Source.Shaft), "üç huzme kartı");

            foreach (VfxTierLevel tier in new[] { VfxTierLevel.Performance, VfxTierLevel.High, VfxTierLevel.Cinematic })
            {
                VfxTier.Override = tier;
                for (int i = 0; i < 90; i++) sys.Step(1f / 30f);   // tier applied, prefill, 3 s of turnover
                yield return null;

                int draws = 0;
                for (int l = 0; l < AtmosphereSystem.LayerCount; l++)
                {
                    var layer = (AtmosphereSystem.Layer)l;
                    int live = sys.LiveCount(layer), target = AtmosphereSystem.TargetLive(layer, tier);
                    Assert.LessOrEqual(live, sys.CapOf(layer), tier + " " + layer + " tavanı aştı");
                    if (target > 0) Assert.GreaterOrEqual(live, (int)(target * 0.7f), tier + " " + layer + " hedefin %70'inin altında (" + live + "/" + target + ")");
                    if (live > 0) draws++;
                }

                Assert.LessOrEqual(draws, AtmosphereSystem.DrawsFor(tier), tier + " draw sayısı");
                Debug.Log("[DD-TEST] wp073.b5 tier=" + tier + " live=" + sys.TotalLive + " draws=" + draws + " shafts=" + sys.ActiveShafts);
            }

            Assert.AreEqual(sys.ActiveShafts, (int)Shader.GetGlobalFloat("_DD_AtmoShaftCount"), "huzme globali");
            Assert.AreEqual(3, sys.ActiveShafts);
        }

        [UnityTest]
        public IEnumerator TekTane_EkranKaplamasi_KucukKalir()
        {
            // One 3 cm mote 5 m ahead must cover a few pixels, never the frame (B5 capture haze regression).
            _world = new GameObject("AtmoCoverageWorld");
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            _camGo = new GameObject("AtmoCoverageCamera");
            Camera cam = _camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 400f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 60f;
            cam.targetTexture = rt;
            AtmosphereSystem sys = Attach();
            // Full sun, whatever cookie the last world left bound (DEC-102's broken-cloud cookie put this point in a cloud shadow:
            // steam, α floored in the shade, then read 0 px): the test measures the pixel clamp, not the weather.
            Texture cookieWas = Shader.GetGlobalTexture("_DD_CloudCookie");
            Vector4 uWas = Shader.GetGlobalVector("_DD_CloudCookieU"), vWas = Shader.GetGlobalVector("_DD_CloudCookieV");
            Shader.SetGlobalTexture("_DD_CloudCookie", Texture2D.whiteTexture);
            Shader.SetGlobalVector("_DD_CloudCookieU", Vector4.zero);
            Shader.SetGlobalVector("_DD_CloudCookieV", Vector4.zero);
            yield return null;
            // The system itself stays out of the measurement (its open-air field emits round the focus — here the camera).
            sys.enabled = false;
            try
            {
                var counts = new int[1];
                foreach (AtmosphereSystem.Layer layer in new[] { AtmosphereSystem.Layer.Motes, AtmosphereSystem.Layer.Petals, AtmosphereSystem.Layer.Steam })
                {
                    ParticleSystem ps = sys.SystemOf(layer);
                    Assert.IsNotNull(ps, layer + " katmanı yok");
                    // Shade floors to 1 and soft particles off: α independent of the sun's shadow map / cookie and of the depth texture
                    // an earlier test left bound (this off-screen camera renders none) — the steam puff read 0–504 px across runs.
                    Material mat = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    Vector4 lightWas = mat.GetVector("_AtmoLight"), twinkleWas = mat.GetVector("_AtmoTwinkle"), pxWas = mat.GetVector("_AtmoPx");
                    bool softWas = mat.IsKeywordEnabled("_DD_ATMO_SOFT");
                    mat.SetVector("_AtmoLight", new Vector4(lightWas.x, lightWas.y, lightWas.z, 1f));
                    mat.SetVector("_AtmoTwinkle", new Vector4(twinkleWas.x, twinkleWas.y, twinkleWas.z, 1f));
                    mat.DisableKeyword("_DD_ATMO_SOFT");
                    float size = layer == AtmosphereSystem.Layer.Steam ? 0.8f : layer == AtmosphereSystem.Layer.Petals ? 0.12f : 0.03f;
                    try
                    {
                        yield return RenderOne(sys, ps, size, rt, counts);
                        int lit = counts[0];
                        // Expected footprint at 5 m on a 256 px, 60° frame: ≈ 44 px per metre → mote ≈ 1.5 px, petal ≈ 5 px, steam ≈ 35 px across.
                        float expect = size * 44.3f;
                        Debug.Log("[DD-TEST] wp073.b5 coverage " + layer + " lit=" + lit + " expected≈" + (expect * expect).ToString("F0") + " px");
                        Assert.Less(lit, Mathf.Max(64f, expect * expect * 4f), layer + ": tek tane " + lit + " piksel kapladı (ekranı örtüyor)");
                        if (layer == AtmosphereSystem.Layer.Steam)
                        {
                            // A / B under the same light: the steam's own cap vs the grains' 24 px cap. The puff keeps its world size, so
                            // capping it at 24 px must shrink it (a 35 px puff → 24 px ≈ half the area).
                            mat.SetVector("_AtmoPx", new Vector4(pxWas.x, 24f, pxWas.z, pxWas.w));
                            yield return RenderOne(sys, ps, size, rt, counts);
                            Debug.Log("[DD-TEST] wp073.b5 coverage Steam capped24 lit=" + counts[0]);
                            Assert.Greater(lit, 0, "buhar pufu çizilmedi");
                            Assert.Greater(lit, counts[0] * 1.4f, "buhar pufu dünya boyunu korumalı (24 px tane tavanı uygulanmaz): " + lit + " / 24 px ile " + counts[0]);
                        }
                    }
                    finally
                    {
                        mat.SetVector("_AtmoLight", lightWas);
                        mat.SetVector("_AtmoTwinkle", twinkleWas);
                        mat.SetVector("_AtmoPx", pxWas);
                        if (softWas) mat.EnableKeyword("_DD_ATMO_SOFT");
                    }
                }
            }
            finally
            {
                sys.enabled = true;
                if (cookieWas != null) Shader.SetGlobalTexture("_DD_CloudCookie", cookieWas);
                Shader.SetGlobalVector("_DD_CloudCookieU", uWas);
                Shader.SetGlobalVector("_DD_CloudCookieV", vWas);
                sys.CameraOverride = null;
                rt.Release();
                Object.Destroy(rt);
            }
        }

        // One grain of the layer 5 m ahead of the coverage camera, alone on screen; counts[0] = pixels brighter than 4/255.
        private static IEnumerator RenderOne(AtmosphereSystem sys, ParticleSystem ps, float size, RenderTexture rt, int[] counts)
        {
            for (int i = 0; i < AtmosphereSystem.LayerCount; i++) { ParticleSystem other = sys.SystemOf((AtmosphereSystem.Layer)i); if (other != null) other.Clear(); }
            var ep = new ParticleSystem.EmitParams { position = new Vector3(0f, 400f, 5f), velocity = Vector3.zero, startLifetime = 5f,
                startSize3D = new Vector3(size, size, 0f), startColor = new Color(1f, 1f, 1f, 0.3f), rotation = 0f };
            ps.Emit(ep, 1);
            // Game time, not frames: steam and petals fade in over their first tenths of a second, and batch-mode frames can be
            // a few ms each (two frames read 0 px in some runs, ~190 in others).
            float t0 = Time.time;
            while (Time.time - t0 < 0.8f) yield return null;   // past the 12 % fade-in of a 5 s life
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            int lit = 0;
            foreach (Color32 c in tex.GetPixels32()) if (c.r > 4 || c.g > 4 || c.b > 4) lit++;
            Object.Destroy(tex);
            counts[0] = lit;
        }

        [UnityTest]
        public IEnumerator YakinAlan_UcuncuSahis_KameraDibindeTaneYok()
        {
            BuildWorld(0);
            CameraModeService.Request(CameraMode.ThirdPerson, true);
            AtmosphereSystem sys = Attach();
            yield return null;
            VfxTier.Override = VfxTierLevel.Cinematic;
            for (int i = 0; i < 60; i++) sys.Step(1f / 30f);

            ParticleSystem motes = sys.SystemOf(AtmosphereSystem.Layer.Motes);
            var buffer = new ParticleSystem.Particle[motes.particleCount];   // test-side allocation, outside any measurement
            int n = motes.GetParticles(buffer);
            Vector3 cam = _camGo.transform.position;
            int near = 0, lens = 0;
            for (int i = 0; i < n; i++)
            {
                float d = Vector3.Distance(buffer[i].position, cam);
                if (d < AtmosphereSystem.CameraClear - 0.05f) lens++;
                if (d < 8f) near++;
            }

            // The near field never births inside 0.6 m; only an open-air field grain can drift there (the shader zeroes it < 0.35 m).
            Assert.LessOrEqual(lens, Mathf.Max(2, n / 500), "merceğe 0,6 m'den yakın tane: " + lens + "/" + n);
            Assert.Greater(near, 200, "TP yakın alanı boş (göz hizası taneleri)");
        }

        [UnityTest]
        public IEnumerator EskiDrift_SusarAmaOynar_SistemGidinceDoner()
        {
            BuildWorld(0);
            var legacyGo = new GameObject("DD_Pollen_Global");
            legacyGo.SetActive(false);
            legacyGo.transform.SetParent(_world.transform, false);
            legacyGo.AddComponent<ParticleSystem>();
            AtmosphereDrift drift = legacyGo.AddComponent<AtmosphereDrift>();
            legacyGo.SetActive(true);
            yield return null;
            float authored = drift.System.emission.rateOverTime.constant;
            Assert.Greater(authored, 0f);

            AtmosphereSystem sys = Attach();
            yield return null;
            Assert.AreEqual(0f, drift.System.emission.rateOverTime.constant, 1e-4f, "eski drift susmadı (çift tane)");
            Assert.IsTrue(drift.System.isPlaying, "WP-013 bütçe testi isPlaying bekler");

            Object.Destroy(sys.gameObject);
            yield return null;
            Assert.AreEqual(authored, drift.System.emission.rateOverTime.constant, 1e-4f, "sistem gidince eski drift geri gelmeli");
        }

        [UnityTest]
        public IEnumerator YuzKare_SifirAyirma_IsParcacigiFiltreli()
        {
            BuildWorld(3);
            CameraModeService.Request(CameraMode.ThirdPerson, true);
            AtmosphereSystem sys = Attach();
            yield return null;
            VfxTier.Override = VfxTierLevel.Cinematic;
            for (int i = 0; i < 60; i++) sys.Step(1f / 30f);   // warm: tier, prefill, weights, shafts, every code path once
            yield return null;

            // Positive control: the test framework's GC.Alloc recorder is filtered to this thread and must see a 256 KB bait
            // (VfxWave3Tests / WP-051C pattern; GC.GetAllocatedBytesForCurrentThread is always 0 in Unity Mono).
            object[] keep = null;
            Assert.That(() => { keep = new object[4000]; for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64]; },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "kayıtçı 256 KB yemi görmedi — ölçüm geçersiz");
            GC.KeepAlive(keep);

            Assert.That(() =>
            {
                for (int f = 0; f < 100; f++) sys.Step(1f / 30f);   // includes two weight refreshes and ~1 500 emits
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Atmosfer adımı yönetilen bellek ayırdı (bu iş parçacığında GC.Alloc örneği / 100 kare).");
        }

        // ------------------------------------------------------------------ fixture

        private AtmosphereSystem Attach()
        {
            AtmosphereSystem sys = AtmosphereSystem.Attach(_world.transform);
            sys.ScanRoot = _world.transform;   // C-00 01.10 A1 triage: count only this synthetic world (a suite may leave V6 loaded)
            if (sys.Ready) sys.Rescan();       // an Active left by an earlier world may have scanned already (Start rescans again: same result)
            sys.CameraOverride = _camGo.GetComponent<Camera>();
            sys.FocusOverride = Vector3.zero;
            sys.Seed(12345u);
            return sys;
        }

        /// <summary>
        /// A 60 m slab, two sakura crowns, one bamboo clump, a pond disc, a roofed box with CameraOccluderClass Roof, a player, a TP
        /// camera and <paramref name="shafts"/> shaft cards under "Shafts (C4)" (unit quads, x = width, y = length).
        /// </summary>
        private void BuildWorld(int shafts)
        {
            _world = new GameObject("WP073-B5 World");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(_world.transform, false);
            ground.transform.localScale = new Vector3(60f, 0.2f, 60f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);

            Tree("SM_sakura_v2_V02", new Vector3(6f, 0f, 4f), 5f);
            Tree("SM_sakura_v2_V03", new Vector3(-6f, 0f, 8f), 4.5f);
            Tree("SM_bamboo_v2_V01", new Vector3(-10f, 0f, -6f), 7f);

            var pond = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pond.name = "Pond0_Water";
            Object.Destroy(pond.GetComponent<Collider>());
            pond.transform.SetParent(_world.transform, false);
            pond.transform.position = new Vector3(10f, 0.02f, -8f);
            pond.transform.localScale = new Vector3(8f, 0.01f, 5f);

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "R9_Roof";
            roof.transform.SetParent(_world.transform, false);
            roof.transform.position = new Vector3(0f, 3.5f, 14f);
            roof.transform.localScale = new Vector3(8f, 0.5f, 5f);
            roof.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Roof;

            if (shafts > 0)
            {
                var group = new GameObject(AtmosphereSystem.ShaftGroupName);
                group.transform.SetParent(_world.transform, false);
                for (int i = 0; i < shafts; i++)
                {
                    var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.Destroy(card.GetComponent<Collider>());
                    card.name = "Shaft_" + i.ToString("00") + "_Test";
                    card.transform.SetParent(group.transform, false);
                    card.transform.position = new Vector3(-4f + 4f * i, 9f, 2f);
                    card.transform.localScale = new Vector3(2f, 10f, 10f);
                }
            }

            _player = new GameObject("WP073-B5 Player");
            _player.transform.position = Vector3.zero;
            EnemyTargeting.SetPlayer(_player.transform);

            _camGo = new GameObject("WP073-B5 Camera");
            _camGo.transform.position = new Vector3(0f, 1.7f, -3.5f);
            _camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            _camGo.AddComponent<Camera>();
            Physics.SyncTransforms();
        }

        private void Tree(string name, Vector3 at, float height)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_world.transform, false);
            root.transform.position = at;
            root.AddComponent<LODGroup>();
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(crown.GetComponent<Collider>());
            crown.name = name + "_LOD0";
            crown.transform.SetParent(root.transform, false);
            crown.transform.localPosition = new Vector3(0f, height * 0.6f, 0f);
            crown.transform.localScale = new Vector3(height * 0.8f, height * 0.6f, height * 0.8f);
        }
    }
}
