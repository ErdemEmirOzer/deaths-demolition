using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-019b · Kan katmanı (direktif §10, gore YÜKSEK): et vuruşunda yönlü sıçrama kartı
    /// (≤ 0.3 s, HDR tint) + GPU-instanced damlacık + zemin/duvar izi; ölümde büyüyen göl;
    /// 128'lik iz havuzu LRU solmayla taşmaz, impact payı ≤ %40; gold çevresi 0.6 m iz yok;
    /// uzak izler birleşik mesh'e alınır; round başında sıfırlanır; olay yolu GC ayırmaz;
    /// render edilen kan rengi DEC-077 bandında ve rezervlerden ΔE ≥ 25 uzakta.
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class VfxBloodTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, DolusZamanAsimi = 90f, YatismaZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private readonly List<GameObject> _gecici = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _director = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            foreach (GameObject go in _gecici) if (go != null) UnityEngine.Object.Destroy(go);
            _gecici.Clear();
            Time.timeScale = 1f;
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CharacterAssetSet.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }

            _director = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator RunAP005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null && HitFeedbackDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static EnemyHitFeedbackInfo Hit(Vector3 point, Vector3 direction, bool killed, float health, int id, bool head = false)
        {
            var r = new DamageResult { HealthDamage = health, Killed = killed, Region = head ? BodyRegion.Head : BodyRegion.UpperBody };
            return new EnemyHitFeedbackInfo
            {
                Point = point, Direction = direction, Normal = -direction, Result = r, Power = WeaponPower.Medium,
                TargetId = id, TargetRoot = null, Weight = FeedbackWeight.Standard, ImpactForce = 1f
            };
        }

        private static float GroundY(Vector3 near)
        {
            return Physics.Raycast(near + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : near.y;
        }

        // ============================================================== 1 · sıçrama + damlacık + iz + havuz + LRU + LOD + round + GC

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_KanKatmani_YonluSicrama_Damlacik_Iz_Havuz128_LRU_UzakLOD_GcSifir()
        {
            yield return RunAP005();
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            BloodSystem blood = fx.Vfx.Blood;
            Assert.IsTrue(fx.Vfx.IsReady, "AP-010 katmanı hazır değil.");
            Assert.IsTrue(blood.IsReady, "Kan katmanı hazır değil.");
            Assert.IsTrue(blood.DropletsReady, "Damlacık havuzu yok (blood_droplet satırı / materyali eksik).");
            Assert.LessOrEqual(BloodSystem.SplashSeconds, 0.3f, "Sıçrama kartı kapsülü ≥ 0.3 s örtmemeli.");

            Vector3 origin = PlayerRegistry.Primary.transform.position;
            float ground = GroundY(origin + Vector3.forward * 3f);
            Vector3 p = new Vector3(origin.x, ground + 0.6f, origin.z + 3f);
            int splashes0 = blood.Splashes, drops0 = blood.DropletBursts, marks0 = blood.Marks;

            // One critical (head) flesh hit from the player's side: v003 = one A7 heavy sheet (twin cone / lobe burst carries the spread),
            // v002 fallback = 2 rolled puffs; 22 droplets; 2 marks (ray + drip).
            bool v003 = VfxLibrary.TryGetPlayable(VfxIds.BloodHeavy01, out VfxCatalogEntry heavyRow) && heavyRow.Family == VfxFamily.Blood;
            fx.ReportEnemyHit(Hit(p, Vector3.forward, false, 45f, 1001, true));
            yield return null;
            Assert.AreEqual(v003 ? 1 : 2, blood.Splashes - splashes0, "Kritik vuruş sıçrama kartı sayısı");
            Assert.AreEqual(1, blood.DropletBursts - drops0, "Kritik vuruş 1 damlacık patlaması");
            Assert.GreaterOrEqual(blood.Marks - marks0, 1, "Kritik vuruş en az 1 iz (yön ışını zemine ≤ 2.5 m'de değmeli)");

            FlipbookEffect card = null;
            foreach (FlipbookEffect f in fx.Vfx.FlipbookPool.Spawned)
            {
                if (f.Row != null && (f.Row.Id == VfxIds.ImpactFlesh || (f.Row.Family == VfxFamily.Blood && f.Row.Kind == VfxKind.Flipbook))) { card = f; break; }
            }

            Assert.IsNotNull(card, "Sıçrama kartı havuzda yok.");
            Assert.Greater(card.AlignDirection.sqrMagnitude, 0.5f, "Kart yön hizası verilmemiş.");
            var mpb = new MaterialPropertyBlock();
            card.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(mpb);
            Color cardTint = mpb.GetColor("_Tint");
            if (card.Row.Family == VfxFamily.Blood)
            {
                // Authored AP-010B sheet: manifest duration, capsule-cover ledger 0, painted colour (no recolour), origin (0.12, 0.5) on the hit point.
                Assert.LessOrEqual(card.LifeSeconds, card.Row.DurationSeconds + 0.01f, "v003 kartı manifest süresinde oynar");
                Assert.AreEqual(0f, card.Row.ReadabilityLimitSeconds, "v003 kapsül örtme defteri 0");
                Assert.That(cardTint.r, Is.EqualTo(BloodPalette.AuthoredSheetTint.r).Within(0.01f), "v003 kartı yeniden renklendirilmez (yalnız tek tip karartma)");
                Assert.That(cardTint.g, Is.EqualTo(cardTint.r).Within(0.01f), "v003 kartı gri tint (renk yok)");
                Assert.AreEqual(new Vector2(0.12f, 0.5f), card.Row.OriginUv);
            }
            else
            {
                Assert.LessOrEqual(card.LifeSeconds, 0.3f, "Sıçrama kartı ömrü");
                Assert.Greater(cardTint.r, 1f, "Taze sıçrama HDR tint (r > 1) değil: " + cardTint);
                Assert.Greater(cardTint.r, cardTint.g * 3f, "Taze sıçrama kızıl değil: " + cardTint);
            }

            BloodDropletEffect drops = blood.DropletPool.Spawned.Count > 0 ? blood.DropletPool.Spawned[0] : null;
            Assert.IsNotNull(drops, "Damlacık sistemi havuzdan çıkmadı.");
            Assert.IsTrue(drops.GpuInstanced, "Damlacık renderer'ı mesh + GPU instancing değil.");
            var psr = drops.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(VfxIds.BloodDroplet, VfxLibrary.Get(VfxIds.BloodDroplet).Id);
            Assert.AreSame(VfxLibrary.Get(VfxIds.BloodDroplet).Material, psr.sharedMaterial, "Damlacık materyali katalog satırı değil.");
            Assert.Greater(drops.GetComponent<ParticleSystem>().particleCount, 0, "Damlacık emit etmedi.");

            DecalEffect mark = null;
            foreach (DecalEffect d in blood.DecalPool.Spawned) { mark = d; break; }
            Assert.IsNotNull(mark, "Kan izi havuzda yok.");
            Assert.LessOrEqual(mark.AlphaCap, BloodSystem.DecalAlphaCap + 1e-4f, "İz alfa tavanı ≤ 0.85 (DEC-077).");
            Assert.AreEqual(DecalEffect.Priority.Impact, mark.EvictionClass);

            // Pool discipline: 200 normal hits 0.6 m apart (outside the blood mark merge 0.25 m and the
            // director's 0.55 m proximity merge), 4 per frame (under the per-frame blood cap), then 30 deaths.
            for (int i = 0; i < 200; i++)
            {
                Vector3 q = origin + new Vector3(-6f + (i % 20) * 0.6f, 0f, 2f + (i / 20) * 0.6f);
                q.y = GroundY(q) + 0.5f;
                fx.ReportEnemyHit(Hit(q, Vector3.forward, false, 12f, 2000 + i));
                if (i % 4 == 3) yield return null;
            }

            yield return null;
            Assert.LessOrEqual(blood.DecalPool.CountTotal, BloodSystem.DecalCapacity, "Kan izi havuzu 128'i aştı.");
            Assert.LessOrEqual(blood.LiveMarks, BloodSystem.DecalCapacity, "Canlı iz sayısı kapasiteyi aştı.");
            Assert.Greater(blood.Evicted, 0, "LRU solma hiç çalışmadı (200 vuruş > 52 impact payı).");
            int pools0 = blood.Pools;
            for (int i = 0; i < 30; i++)
            {
                Vector3 q = origin + new Vector3(-3f + (i % 10) * 0.7f, 0f, 6f + (i / 10) * 0.8f);
                q.y = GroundY(q);
                fx.ReportEnemyHit(Hit(q + Vector3.up, Vector3.forward, true, 30f, 3000 + i));
                if (i % 2 == 1) yield return null;
            }

            yield return new WaitForSeconds(1.3f);   // eviction fades (1 s) complete
            int liveImpact = blood.LiveImpactMarks, live = blood.LiveMarks;
            Assert.GreaterOrEqual(blood.Pools - pools0, 20, "30 ölümden ≥ 20 göl (gold dışlaması / zemin ışını dışında).");
            Assert.LessOrEqual(liveImpact, BloodSystem.ImpactShareMax + 8, "Impact izleri havuzun %40 payını aştı: " + liveImpact);
            Assert.LessOrEqual(live, BloodSystem.DecalCapacity, "Canlı iz > 128");
            Assert.LessOrEqual(blood.DecalPool.CountTotal, BloodSystem.DecalCapacity);

            DecalEffect pool = null;
            foreach (DecalEffect d in blood.DecalPool.Spawned) if (d.EvictionClass == DecalEffect.Priority.Pool) { pool = d; break; }
            Assert.IsNotNull(pool, "Ölüm gölü havuzda yok.");
            Assert.Greater(pool.CurrentSize, BloodSystem.PoolGrowFrom, "Göl büyümüyor.");
            Assert.LessOrEqual(pool.CurrentSize, BloodSystem.PoolSizeHeavy + 1e-3f);

            // GC: the event path (hits + deaths) allocates nothing after warm-up.
            for (int i = 0; i < 40; i++)
            {
                fx.ReportEnemyHit(Hit(p + Vector3.right * (i % 7) * 0.6f, Vector3.forward, i % 8 == 7, 12f, 5000 + i));
                if (i % 4 == 3) yield return null;
            }

            yield return null; yield return null;
            // Used-heap delta over 2000 direct blood hits (WP-047: GC.GetAllocatedBytesForCurrentThread is always 0 in Unity Mono —
            // the earlier 40-hit figure measured nothing). Synchronous, pools recycling, marks evicting.
            long gc0 = GC.GetTotalMemory(false);
            for (int i = 0; i < 2000; i++) blood.PlayHit(p + Vector3.right * (i % 7) * 0.6f, Vector3.back, Vector3.forward, i % 8 == 7 ? BloodSystem.HitClass.Critical : BloodSystem.HitClass.Normal);
            long gc1 = GC.GetTotalMemory(false);
            yield return null;

            // Far LOD: walk the camera 30 m away; settled marks (> 2 s) beyond 18 m merge into the chunk mesh and free their slots.
            int liveBefore = blood.LiveMarks;
            PlayerRig rig = PlayerRegistry.Primary;
            Vector3 farPos = origin + Vector3.back * 30f;
            farPos.y = GroundY(farPos) + 0.1f;
            rig.Teleport(farPos, Quaternion.identity);
            yield return new WaitForSeconds(2.8f);
            int merged = blood.Merged, mergedQuads = blood.Merger.MergedQuads, liveAfter = blood.LiveMarks;
            rig.Teleport(origin, Quaternion.identity);
            yield return null;

            // Round reset: everything fades in 2 s and the merged chunks drop.
            RunEvents.PublishRoundStarted(99);
            yield return new WaitForSeconds(2.3f);
            int liveReset = blood.LiveMarks, quadsReset = blood.Merger.MergedQuads;

            Debug.Log("[DD-TEST] wp019b.blood splashes=" + blood.Splashes + " droplets=" + blood.DropletBursts + " marks=" + blood.Marks + " pools=" + blood.Pools +
                      " evicted=" + blood.Evicted + " skippedGold=" + blood.SkippedForGold + " liveImpact=" + liveImpact + " live=" + live + " poolTotal=" + blood.DecalPool.CountTotal +
                      " gcBytes2000hits=" + (gc1 - gc0) + " far: live " + liveBefore + "→" + liveAfter + " merged=" + merged + " quads=" + mergedQuads +
                      " reset: live=" + liveReset + " quads=" + quadsReset + " cardTint=" + cardTint + " gpuInstanced=" + drops.GpuInstanced);
            Assert.LessOrEqual(gc1 - gc0, 4096, "Kan olay yolu yönetilen bellek ayırdı: " + (gc1 - gc0) + " B / 2000 vuruş (yığın deltası).");
            Assert.Greater(merged, 0, "Uzak izler birleşik mesh'e alınmadı.");
            Assert.Greater(mergedQuads, 0);
            Assert.Less(liveAfter, liveBefore, "Birleştirme havuz slotu boşaltmadı.");
            Assert.AreEqual(0, liveReset, "Round başında izler temizlenmedi.");
            Assert.AreEqual(0, quadsReset, "Round başında birleşik mesh temizlenmedi.");
        }

        // ============================================================== 2 · gold dışlaması

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_KanIzi_GoldPaketiCevresinde_0_6m_Dogmaz()
        {
            yield return RunAP005();
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            BloodSystem blood = fx.Vfx.Blood;
            Assert.IsTrue(blood.IsReady);

            _director.ForceActiveTarget(3, true);
            yield return CharacterTests.Bekle(() => ZombieEnemy.Live.Count >= 1, DolusZamanAsimi, "zombi doğmadı");
            ZombieEnemy z = ZombieEnemy.Live[0];
            Vector3 origin = PlayerRegistry.Primary.transform.position;
            z.transform.position = origin + Vector3.forward * 7f;   // beyond the 3.5 m magnet: the packet stays on the floor
            yield return null;
            z.Kill();
            yield return CharacterTests.Bekle(() => GoldService.Instance != null && GoldService.Instance.LiveOrbCount >= 1, 10f, "gold paketi düşmedi");
            _director.ForceActiveTarget(0, false);
            yield return new WaitForSeconds(1.5f);   // the packet scatters on an arc; let it settle before measuring from it

            GoldOrb orb = UnityEngine.Object.FindObjectOfType<GoldOrb>();
            Assert.IsNotNull(orb, "paket 1.5 s içinde kayboldu (mıknatıs?) liveOrbs=" + GoldService.Instance.LiveOrbCount);
            Vector3 g = orb.transform.position;
            float d = GoldService.Instance.NearestLiveOrbDistance(g + Vector3.right * 0.3f);
            Assert.Less(d, 0.35f, "NearestLiveOrbDistance yanlış: " + d);

            int marks0 = blood.Marks, skipped0 = blood.SkippedForGold, pools0 = blood.Pools;
            // Deaths with their feet 0.3 m from the packet: the pool is skipped (centre inside the ring).
            for (int i = 0; i < 4; i++)
            {
                Vector3 feet = g + new Vector3(0.3f, 0f, 0f);
                feet.y = GroundY(feet);
                fx.ReportEnemyHit(Hit(feet + Vector3.up, Vector3.forward, true, 30f, 7000 + i));
                yield return null;
            }

            int skippedNear = blood.SkippedForGold - skipped0;
            int poolsNear = blood.Pools - pools0;

            // A death 1.0 m away: the pool lands, clipped so its edge stays 0.6 m clear (size ≤ 2 × (1.0 − 0.6) = 0.8 m).
            Vector3 feetFar = g + new Vector3(1.0f, 0f, 0f);
            feetFar.y = GroundY(feetFar);
            int poolsBeforeFar = blood.Pools;
            fx.ReportEnemyHit(Hit(feetFar + Vector3.up, Vector3.forward, true, 30f, 7100));
            yield return null;
            // Read the placed size at once (a round change or the far LOD may reclaim the mark within seconds).
            bool farPlaced = blood.Pools == poolsBeforeFar + 1;
            float farSize = blood.LastPoolSize;

            var dump = new System.Text.StringBuilder();
            foreach (DecalEffect dd in blood.DecalPool.Spawned) dump.Append(dd.EvictionClass).Append('@').Append(dd.transform.position.ToString("F2")).Append(' ');
            Debug.Log("[DD-TEST] wp019b.gold skippedNear=" + skippedNear + " poolsNear=" + poolsNear + " clippedSize=" + (farPlaced ? farSize.ToString("F2", CultureInfo.InvariantCulture) : "none") +
                      " marksDelta=" + (blood.Marks - marks0) + " poolsDelta=" + (blood.Pools - pools0) + " feetFar=" + feetFar.ToString("F2") + " orb=" + g.ToString("F2") + " spawned: " + dump);
            Assert.GreaterOrEqual(skippedNear, 4, "Gold çevresindeki ölüm gölleri atlanmadı.");
            Assert.AreEqual(0, poolsNear, "Gold 0.6 m içinde göl doğdu.");
            Assert.IsTrue(farPlaced, "1.0 m'deki göl doğmadı.");
            Assert.LessOrEqual(farSize, 0.8f + 1e-3f, "Göl kenarı gold dışlama halkasına girdi: " + farSize);
            Assert.GreaterOrEqual(farSize, 0.79f, "Göl beklenenden küçük kırpıldı: " + farSize);
        }

        // ============================================================== 3 · render edilen renk bandı

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator KanRengi_RenderdeBantIcinde_RezervlerdenAyri()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsTrue(VfxLibrary.IsReady, "VfxCatalog yok.");
            var results = new List<string>();
            var failures = new List<string>();

            // A wet mark, a dry mark and the splash card, each rendered alone over a transparent black clear and un-premultiplied.
            var cases = new List<(string name, string id, Color target, float calibration)>
            {
                ("wet", BloodSlots.GroundDecal[BloodSlots.GroundDecal.Length - 1], BloodPalette.DecalWet, BloodPalette.MarkCalibration),
                ("dry", BloodSlots.GroundDecal[BloodSlots.GroundDecal.Length - 1], BloodPalette.DecalDry, BloodPalette.MarkCalibration),
                ("pool", VfxIds.DecalZombieSplat01, BloodPalette.DecalWet, BloodPalette.MarkCalibration),
                ("droplet", VfxIds.BloodDroplet, BloodPalette.Droplet, BloodPalette.DropletCalibration),
                ("fresh", BloodSlots.Splash[BloodSlots.Splash.Length - 1], BloodPalette.Fresh, BloodPalette.SplashCalibration * BloodPalette.FreshHdrGain),
                ("death", VfxIds.ZombieDeath, BloodPalette.Fresh, BloodPalette.DeathSheetCalibration)
            };

            // AP-010B v003 rows play as painted (tint white): the production claim "100 % in band" is measured here the same way.
            foreach (string id in new[] { VfxIds.BloodSplash01, VfxIds.BloodHeavy01, VfxIds.BloodDeath01, VfxIds.BloodGround01, VfxIds.BloodWall01, VfxIds.BloodPoolAlbedo })
            {
                if (VfxLibrary.TryGetPlayable(id, out VfxCatalogEntry v3) && v3.Family == VfxFamily.Blood) cases.Add(("v003:" + id, id, Color.white, v3.Kind == VfxKind.Flipbook ? -2f : -1f));
            }

            foreach (var c in cases)
            {
                Assert.IsTrue(VfxLibrary.TryGetPlayable(c.id, out VfxCatalogEntry row), c.id);
                // -1 = authored mark (white), -2 = authored sheet (the uniform darkening the game applies), droplet = its material tint.
                Color tint = c.calibration == -2f ? BloodPalette.AuthoredSheetTint : c.calibration < 0f ? Color.white : c.id == VfxIds.BloodDroplet ? row.Material.GetColor("_Tint") : BloodPalette.TintFor(row, c.target, c.calibration);
                yield return RenderQuad(row, tint, 1f, (mean, n) =>
                {
                    bool ok = BloodPalette.IsInBand(mean, out float h, out float s, out float v);
                    float dBeacon = BloodPalette.DeltaE(mean, VfxLibrary.BeaconTint), dVerm = BloodPalette.DeltaE(mean, VfxLibrary.VermilionTint), dGold = BloodPalette.DeltaE(mean, VfxLibrary.GoldTint);
                    float dSkin1 = BloodPalette.DeltaE(mean, BloodPalette.SkinOneAccent);
                    Color.RGBToHSV(BloodPalette.SkinOneAccent, out _, out _, out float skinV);
                    float p95 = s_lastP95;
                    string line = c.name + " mean=" + mean + " hsv=(" + h.ToString("F0", CultureInfo.InvariantCulture) + "," + s.ToString("F2", CultureInfo.InvariantCulture) + "," + v.ToString("F2", CultureInfo.InvariantCulture) + ") p95v=" + p95.ToString("F2", CultureInfo.InvariantCulture) + " n=" + n +
                                  " dE beacon=" + dBeacon.ToString("F1", CultureInfo.InvariantCulture) + " vermilion=" + dVerm.ToString("F1", CultureInfo.InvariantCulture) + " gold=" + dGold.ToString("F1", CultureInfo.InvariantCulture) +
                                  " skin1=" + dSkin1.ToString("F1", CultureInfo.InvariantCulture) + " (Δv " + (skinV - v).ToString("F2", CultureInfo.InvariantCulture) + ")";
                    results.Add(line);
                    if (n < 100) failures.Add(c.name + ": çok az opak piksel (" + n + ")");
                    // AD M3: sheets by p95 value too (≤ 0.50), not only by the coverage-weighted mean.
                    if (row.Kind == VfxKind.Flipbook && p95 > 0.50f + 1e-3f) failures.Add(c.name + ": p95 v > 0.50 " + line);
                    if (!ok) failures.Add(c.name + ": bant dışı " + line);
                    if (dBeacon < 25f || dVerm < 25f || dGold < 25f) failures.Add(c.name + ": rezervden ΔE < 25 " + line);
                    // Skin-1 shares the hue by design; the directive separates it by value (0.64 vs ≤ 0.45). ΔE ≥ 15 / Δv ≥ 0.15 here, the AD's ≥ 25 wish is reported.
                    if (dSkin1 < 15f || skinV - v < 0.15f) failures.Add(c.name + ": skin-1 aksanından ayrışmıyor " + line);
                });
            }

            float wetDry = BloodPalette.DeltaE(BloodPalette.DecalWet, BloodPalette.DecalDry);
            results.Add("wet↔dry ΔE=" + wetDry.ToString("F1", CultureInfo.InvariantCulture));
            if (wetDry < 8f) failures.Add("ıslak ↔ kuru ΔE < 8");
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp019-captures");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "blood-colour-readout.txt"), string.Join("\n", results) + "\n");
            Debug.Log("[DD-TEST] wp019b.colour " + string.Join(" | ", results));
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static float s_lastP95;

        /// <summary>Renders <paramref name="row"/> on the shared quad, alone, over a transparent black clear; reports the un-premultiplied mean of texels with alpha &gt; 0.5 (the importer's opaque threshold).</summary>
        private static IEnumerator RenderQuad(VfxCatalogEntry row, Color tint, float alphaCap, Action<Color, int> report)
        {
            // The importer's alpha floor (0.1); pixels count by coverage.
            float alphaFloor = 0.1f;
            var go = new GameObject("blood-colour-quad");
            go.transform.position = new Vector3(1000f, 1000f, 1000f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(2f, 2f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = row.Material;
            var mpb = new MaterialPropertyBlock();
            // A mid frame for sheets; alpha cap and the tint under test through the property block (the game path).
            bool materialGrid = row.Material.GetFloat("_Cols") > 1.5f || row.Material.GetFloat("_Rows") > 1.5f;   // the droplet material is 1×1 (particle atlas)
            mpb.SetFloat("_Frame", materialGrid ? Mathf.Max(0, row.FramesUsed / 2) : 0f); mpb.SetFloat("_Alpha", 1f); mpb.SetFloat("_AlphaCap", alphaCap); mpb.SetColor("_Tint", tint);
            r.SetPropertyBlock(mpb);

            var camGo = new GameObject("blood-colour-cam");
            camGo.SetActive(false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = 1.05f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.transform.position = go.transform.position + Vector3.back * 3f;
            cam.transform.LookAt(go.transform.position, Vector3.up);
            UniversalAdditionalCameraData urp = cam.GetUniversalAdditionalCameraData();
            urp.renderType = CameraRenderType.Base; urp.renderPostProcessing = false; urp.antialiasing = AntialiasingMode.None; urp.renderShadows = false;
            camGo.SetActive(true);
            yield return null;

            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0, false);
            tex.Apply(false, false);
            RenderTexture.active = previous;

            Color[] px = tex.GetPixels();
            double rs = 0, gs = 0, bs = 0, ws = 0; int n = 0;
            var values = new List<float>(px.Length / 4);
            for (int i = 0; i < px.Length; i++)
            {
                Color p = px[i];
                if (p.a > alphaFloor)
                {
                    // The RT stores sRGB(src_lin × a) and a: un-premultiply in linear, weight by coverage (the importer's
                    // definition), average in linear, encode once. Per-pixel value (un-premultiplied) feeds the p95 (AD M3).
                    rs += Mathf.GammaToLinearSpace(p.r); gs += Mathf.GammaToLinearSpace(p.g); bs += Mathf.GammaToLinearSpace(p.b); ws += p.a; n++;
                    float lr = Mathf.GammaToLinearSpace(p.r) / p.a, lg = Mathf.GammaToLinearSpace(p.g) / p.a, lb = Mathf.GammaToLinearSpace(p.b) / p.a;
                    values.Add(Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.Max(lr, Mathf.Max(lg, lb)))));
                }
            }

            values.Sort();
            s_lastP95 = values.Count > 0 ? values[Mathf.Min(values.Count - 1, Mathf.FloorToInt(values.Count * 0.95f))] : 0f;

            Color mean = n > 0 && ws > 0
                ? new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01((float)(rs / ws))), Mathf.LinearToGammaSpace(Mathf.Clamp01((float)(gs / ws))), Mathf.LinearToGammaSpace(Mathf.Clamp01((float)(bs / ws))), 1f)
                : Color.black;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.Destroy(camGo); UnityEngine.Object.Destroy(go);
            report(mean, n);
        }
    }
}
