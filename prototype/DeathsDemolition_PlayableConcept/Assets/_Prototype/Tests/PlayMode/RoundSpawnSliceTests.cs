using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-003 · M1 ROUND &amp; DİNAMİK SPAWN nöbetçisi — Run sahnesinde, gerçek zincirle, gerçek Metal.
    /// REQ-006 · REQ-010 · REQ-012 · REQ-019 · REQ-065 · REQ-066 · REQ-069 · REQ-072.
    ///
    /// Hiçbir test rastgele dünya koordinatı üretmez, hiçbir test sayı kilitlemez:
    /// eşikler R1 formül değerlerinden (havuz 12, hedef 9, tavan 40) ve config
    /// aralıklarından türetilir; sabit tazelik nöbetçisi sapmada uyarır. Ölümler gerçek
    /// ölüm yolundan (ZombieEnemy.Kill → Die → sink) gelir; director'ın yaşayan sayısı,
    /// ZombieEnemy.Live (cesetleri de içerir) ile çapraz kontrol edilir.
    ///
    /// ZAMAN: gerçek kare. Refill aralığı 0.45 s × tik başına 2 spawn → 9 yaşayana
    /// T_ideal = ceil(9/2)·0.45 ≈ 2.25 s; üst sınır (nokta gevşetmeleri, ilk path cache)
    /// için 12 s bekleme kullanılır. Sayılar PrototypeOnly — test sabiti.
    /// </summary>
    [TestFixture]
    public sealed class RoundSpawnSliceTests
    {
        private const int R1Havuz = 12;
        private const int R1Hedef = 9;
        private const int Tavan = 40;
        private const float RefillAraligi = 0.45f;
        private const int TikBasinaSpawn = 2;
        private const float MaxUseful = 62f;
        private const float KurulumZamanAsimi = 40f;
        private const float DolusZamanAsimi = 12f;

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private float _sinir = MaxUseful;

        // ============================================================== kurulum

        private IEnumerator RunSahnesiniHazirla()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;

            Assert.IsFalse(_rig.Health.DiagnosisInvulnerable, "Oyuncu tanı dokunulmazlığıyla açıldı.");
            SabitTazeligiDogrula();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private static void SabitTazeligiDogrula()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "PrototypeConfig.Active yok.");
            Assert.AreEqual(R1Havuz, config.TotalEnemyPool(1), "R1 havuz sabiti sevkiyat config'inden saptı; türetmeyi yenile.");
            Assert.AreEqual(R1Hedef, config.TargetActiveEnemies(1), "R1 hedef sabiti saptı; türetmeyi yenile.");
            Assert.AreEqual(Tavan, config.AbsoluteSafetyCeiling, "Tavan sabiti saptı; türetmeyi yenile.");
            Assert.AreEqual(RefillAraligi, config.Rounds.SpawnRefillInterval, 0.0001f, "Refill aralığı saptı; türetmeyi yenile.");
            Assert.AreEqual(TikBasinaSpawn, config.Rounds.MaxSpawnsPerRefillTick, "Tik başına spawn saptı; türetmeyi yenile.");
            Assert.AreEqual(MaxUseful, config.Spawn.MaxUsefulDistance, 0.0001f, "MaxUsefulDistance saptı; türetmeyi yenile.");
        }

        /// <summary>Her karede geçerli olması gereken değişmezler. Ceset dahil gövde ≤ tavan.</summary>
        private void DegismezleriDogrula(string nokta)
        {
            RoundPopulationSnapshot p = _director.Population;
            Assert.LessOrEqual(p.Occupied, p.Ceiling, nokta + ": gövde sayısı tavanı aştı (" + p.Occupied + "/" + p.Ceiling + ").");
            Assert.LessOrEqual(p.Alive, p.Occupied, nokta + ": yaşayan gövdeden fazla.");
            Assert.LessOrEqual(p.TargetActive, p.Ceiling, nokta + ": hedef tavanı aştı.");
            Assert.LessOrEqual(ZombieEnemy.Live.Count, Tavan, nokta + ": sahnedeki zombi gövdesi tavanı aştı (" + ZombieEnemy.Live.Count + ").");
            Assert.GreaterOrEqual(p.Unspawned, 0, nokta + ": havuz negatif.");
        }

        private static int DirectorYasayanZombi()
        {
            int n = 0;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket > 0) n++;
            }

            return n;
        }

        private static ZombieEnemy BirYasayanDirectorZombisi()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket > 0) return live[i];
            }

            return null;
        }

        private static int TumYasayanlariOldur()
        {
            int n = 0;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != null && live[i].IsAlive)
                {
                    live[i].Kill();
                    n++;
                }
            }

            return n;
        }

        private IEnumerator YeniRound()
        {
            RunFlowClock.ClearHolds();
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(1);
            Assert.AreEqual(0, ZombieEnemy.Live.Count, "BeginRound eski gövdeleri temizlemedi.");
            Assert.AreEqual(0, _director.Population.SpawnedTotal);
            yield return null;
            Assert.AreEqual(RunPhase.Fighting, _run.Phase);
            Assert.AreEqual(RoundState.Active, _director.State, "BeginRound sonrası state Active olmalı.");

            // Yeni round'un ilk refill tiki ilk pozitif karede gelir (kesintisiz geliş): en
            // fazla bir tik, ve sahnedeki her gövde bu round'un.
            RoundPopulationSnapshot p = _director.Population;
            Assert.LessOrEqual(p.SpawnedTotal, TikBasinaSpawn, "İlk karede birden fazla refill tiki.");
            Assert.AreEqual(p.SpawnedTotal, ZombieEnemy.Live.Count, "Sahnede bu round'a ait olmayan gövde var.");
            Assert.AreEqual(p.Alive, p.Occupied);
        }

        // ============================================================== WP-061K

        /// <summary>
        /// WP-061K (S-10 soak 61-3): the round's last living zombie falls out of the world. The net despawns it —
        /// the round completes as for a kill, but the body pays NOTHING (no gold packet, no round kill) and the
        /// removal is counted once.
        /// </summary>
        [UnityTest]
        [Category("WP061K")]
        [Timeout(300000)]
        public IEnumerator P8_DunyaDisinaDusenSonZombi_RoundBiter_OdulYok()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();
            ZombieEnemy.ResetOutOfWorldStatics();
            GoldService gold = GoldService.Instance;
            Assert.IsNotNull(gold, "GoldService yok");

            int temizlendi = 0;
            Action<int> t = r => temizlendi++;
            _director.RoundCleared += t;
            try
            {
                // drain the pool, keeping exactly one living body
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && !(_director.Population.Unspawned == 0 && DirectorYasayanZombi() == 1))
                {
                    IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                    int alive = DirectorYasayanZombi();
                    for (int i = live.Count - 1; i >= 0 && alive > 1; i--)
                        if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket > 0) { live[i].Kill(); alive--; }
                    yield return null;
                }
                Assert.AreEqual(0, _director.Population.Unspawned, "havuz tükenmedi");
                ZombieEnemy son = BirYasayanDirectorZombisi();
                Assert.IsNotNull(son, "son gövde yok");
                for (int i = 0; i < 20; i++) yield return null;   // let the survivor stand on the mesh (last on-mesh height)

                int paket0 = gold.PacketsSpawned, kill0 = _run.RoundKills, temiz0 = temizlendi;
                var cc = son.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                son.transform.position += Vector3.down * 20f;   // off the edge: 20 m under its last on-mesh point
                if (cc != null) cc.enabled = true;

                yield return Bekle(() => !son.IsAlive || son.SpawnTicket <= 0, 3f, "düşen gövde kaldırılmadı");
                yield return Bekle(() => temizlendi > temiz0, 5f, "round tamamlanmadı (son yaşayan düştü)");
                Assert.AreEqual(1, ZombieEnemy.OutOfWorldRemovals, "tek kaldırma");
                Assert.AreEqual(paket0, gold.PacketsSpawned, "düşen gövde altın paketi düşürmedi");
                Assert.AreEqual(kill0, _run.RoundKills, "düşen gövde öldürme sayılmadı");
                Debug.Log("[DD-TEST] wp061k.fell removals=" + ZombieEnemy.OutOfWorldRemovals + " packets " + paket0 + "→" + gold.PacketsSpawned + " kills " + kill0 + "→" + _run.RoundKills + " state=" + _director.State);
            }
            finally { _director.RoundCleared -= t; }
        }

        /// <summary>
        /// WP-061L (S-10 stability seed 26): the round's last living zombie is held against the ring (on the NavMesh,
        /// moving nowhere). The stuck watchdog tries one repath + side step, then removes it — the round completes,
        /// no gold packet, no kill; a body that keeps closing on the player is never touched.
        /// </summary>
        [UnityTest]
        [Category("WP061L")]
        [Timeout(300000)]
        public IEnumerator P9_HalkayaYapisanSonZombi_KurtarmaSonraKaldirma_RoundBiter_OdulYok()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();
            ZombieEnemy.ResetOutOfWorldStatics();
            ZombieEnemy.StuckWindowOverride = 1.5f;
            GoldService gold = GoldService.Instance;
            int temizlendi = 0;
            Action<int> t = r => temizlendi++;
            _director.RoundCleared += t;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && !(_director.Population.Unspawned == 0 && DirectorYasayanZombi() == 1))
                {
                    IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                    int alive = DirectorYasayanZombi();
                    for (int i = live.Count - 1; i >= 0 && alive > 1; i--)
                        if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket > 0) { live[i].Kill(); alive--; }
                    yield return null;
                }
                Assert.AreEqual(0, _director.Population.Unspawned, "havuz tükenmedi");
                ZombieEnemy son = BirYasayanDirectorZombisi();
                Assert.IsNotNull(son);

                // hold it far from the player (≥ 12 m), pinned in place each frame — on the mesh, making no progress
                Vector3 pin = son.transform.position;
                Vector3 away = pin - _rig.transform.position; away.y = 0f;
                if (away.magnitude < 12f) { pin = _rig.transform.position + (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.right) * 14f; pin.y = son.transform.position.y; }
                var cc = son.GetComponent<CharacterController>();
                int paket0 = gold.PacketsSpawned, kill0 = _run.RoundKills, temiz0 = temizlendi;
                float tPin = Time.realtimeSinceStartup;
                while (son.IsAlive && son.SpawnTicket > 0 && Time.realtimeSinceStartup - tPin < 10f)
                {
                    if (cc != null) cc.enabled = false;
                    son.transform.position = pin;
                    if (cc != null) cc.enabled = true;
                    yield return null;
                }
                Assert.IsFalse(son.IsAlive && son.SpawnTicket > 0, "takılı gövde kaldırılmadı");
                Assert.AreEqual(1, ZombieEnemy.StuckRecoveries, "önce bir kurtarma denendi");
                Assert.AreEqual(1, ZombieEnemy.StuckRemovals);
                yield return Bekle(() => temizlendi > temiz0, 5f, "round tamamlanmadı (takılı son gövde)");
                Assert.AreEqual(paket0, gold.PacketsSpawned, "ödül yok (altın)");
                Assert.AreEqual(kill0, _run.RoundKills, "ödül yok (öldürme)");
                Debug.Log("[DD-TEST] wp061l.stuck recoveries=" + ZombieEnemy.StuckRecoveries + " removals=" + ZombieEnemy.StuckRemovals + " packets " + paket0 + "→" + gold.PacketsSpawned + " state=" + _director.State);
            }
            finally { _director.RoundCleared -= t; ZombieEnemy.StuckWindowOverride = 0f; }
        }

        /// <summary>WP-061L guard: a crowd that keeps chasing the player (real round, short window) is never removed by the watchdog.</summary>
        [UnityTest]
        [Category("WP061L")]
        [Timeout(300000)]
        public IEnumerator P10_OyuncuyuKovalayanKalabalik_TakilmaSayilmaz()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();
            ZombieEnemy.ResetOutOfWorldStatics();
            ZombieEnemy.StuckWindowOverride = 1.5f;
            _rig.Health.DiagnosisInvulnerable = true;   // the fixture's TearDown clears it
            try
            {
                yield return Bekle(() => _director.ActiveCount >= 6, DolusZamanAsimi, "kalabalık doğmadı");
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 8f) yield return null;   // the crowd closes in and fights (invulnerable player)
                Assert.AreEqual(0, ZombieEnemy.StuckRemovals, "kovalayan/savaşan gövde kaldırıldı");
                Debug.Log("[DD-TEST] wp061l.chase alive=" + _director.ActiveCount + " recoveries=" + ZombieEnemy.StuckRecoveries + " removals=" + ZombieEnemy.StuckRemovals);
            }
            finally { ZombieEnemy.StuckWindowOverride = 0f; }
        }

        /// <summary>
        /// WP-084 (C-00 06.10, S-05 r4 finding) · MEASUREMENT before the fix: a stationary, invulnerable player and the real director's crowd
        /// (round 10, active target 32, the round's own pool) for 60 s with the shipped stuck window (9 s). Every stuck removal is logged:
        /// the body's distance to the player, bodies within 3 m of the player (the front ring) and in the cone between the body and the
        /// player, whether it stood in the game camera's view, and its visible pixels in the game camera at that instant (the frame with
        /// and without the body). Then the director's spawns after the first removal, the alive count and the pool. REPORTED
        /// ([DD-OBS] wp084.watchdog); no assert — the guard after the fix is a separate test.
        /// </summary>
        [UnityTest]
        [Explicit("WP-084 watchdog measurement: run by full name")]
        [Category("Diagnostics")]
        [Category("WP061L")]
        [Timeout(300000)]
        public IEnumerator P11_Olcum_SabitOyuncuyaYigilanKalabalik_ArkaSiraKaldirmalari()
        {
            yield return RunSahnesiniHazirla();
            RunFlowClock.ClearHolds();
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(10);
            yield return null;
            ZombieEnemy.ResetOutOfWorldStatics();
            _rig.Health.DiagnosisInvulnerable = true;   // the fixture's TearDown clears it
            _director.ForceActiveTarget(32, false);
            Camera cam = DD.Prototype.CameraSystem.PrototypeCameraRig.Active.Camera;
            var rows = new List<string>();
            int spawnsAfterFirst = 0, visibleRemovals = 0, inView = 0, maxAlive = 0, removals = 0;
            bool first = false;
            Action<EnemySpawnContext> onSpawn = c => { if (first) spawnsAfterFirst++; };
            Action<EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, DD.Prototype.Damage.ZombieBodyState> onRemove = (tier, where, reason, state) =>
            {
                if (reason != ZombieEnemy.OutOfWorldReason.Stuck) return;
                first = true;
                removals++;
                ZombieEnemy body = NearestLive(where);
                Vector3 player = _rig.transform.position;
                float d = Flat(where, player);
                int ring = 0, cone = 0;
                foreach (ZombieEnemy o in ZombieEnemy.Live)
                {
                    if (o == null || o == body || !o.IsAlive) continue;
                    float od = Flat(o.transform.position, player);
                    if (od <= 3f) ring++;
                    Vector3 toO = o.transform.position - where, toP = player - where; toO.y = 0f; toP.y = 0f;
                    if (od < d && toO.sqrMagnitude > 1e-4f && Vector3.Angle(toO, toP) < 35f) cone++;
                }

                Vector3 vp = cam.WorldToViewportPoint(where + Vector3.up);
                bool view = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                int px = body != null ? ChangedPixels(cam, body) : -1;
                if (view) inView++;
                if (px > 0) visibleRemovals++;
                rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} d={1:0.0} ring3m={2} cone={3} inView={4} visiblePx={5} state={6}", tier, d, ring, cone, view, px, state));
            };
            _director.EnemySpawned += onSpawn;
            ZombieEnemy.OutOfWorldRemoved += onRemove;
            int unspawned0 = 0;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f)
                {
                    maxAlive = Math.Max(maxAlive, _director.ActiveCount);
                    if (!first) unspawned0 = _director.Population.Unspawned;
                    yield return null;
                }
            }
            finally
            {
                _director.EnemySpawned -= onSpawn;
                ZombieEnemy.OutOfWorldRemoved -= onRemove;
                _director.ForceActiveTarget(0, false);
            }

            Debug.Log("[DD-OBS] wp084.watchdog removals=" + removals + " recoveries=" + ZombieEnemy.StuckRecoveries + " inView=" + inView + " visible(px>0)=" + visibleRemovals
                      + " maxAlive=" + maxAlive + " spawnsAfterFirstRemoval=" + spawnsAfterFirst + " unspawned(before first)=" + unspawned0 + " unspawned(end)=" + _director.Population.Unspawned
                      + " alive(end)=" + _director.ActiveCount + " | " + string.Join(" | ", rows));
        }

        /// <summary>
        /// WP-084 (C-00 06.10) guard: 32 bodies of the real director round a standing, invulnerable player (round 10) with a short stuck window
        /// (1.5 s) for 20 s — the crowd's back row waits (ZombieEnemy.StuckCrowdRadius / StuckCrowdAhead) and the watchdog removes none.
        /// Before the exemption (r9a, shipped window): 7 removals at 2.8–3.2 m, every one on screen. P9 keeps the stranded body's removal.
        /// </summary>
        [UnityTest]
        [Category("WP061L")]
        [Timeout(300000)]
        public IEnumerator P12_SabitOyuncuyaYigilanKalabalik_ArkaSiraBekler_KaldirmaYok()
        {
            yield return RunSahnesiniHazirla();
            RunFlowClock.ClearHolds();
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(10);
            yield return null;
            ZombieEnemy.ResetOutOfWorldStatics();
            ZombieEnemy.StuckWindowOverride = 1.5f;
            _rig.Health.DiagnosisInvulnerable = true;   // the fixture's TearDown clears it
            _director.ForceActiveTarget(32, false);
            int maxAlive = 0, maxRing = 0;
            Action<EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, ZombieBodyState> removed = (tier, where, reason, state) =>
            {
                if (reason != ZombieEnemy.OutOfWorldReason.Stuck) return;
                ZombieEnemy body = NearestLive(where);
                Debug.Log("[DD-TEST] wp096.p12.removed body=" + (body != null ? body.GetInstanceID() : 0)
                          + " tier=" + tier + " position=" + where.ToString("F3")
                          + " target=" + _rig.transform.position.ToString("F3")
                          + " distance=" + Flat(where, _rig.transform.position).ToString("F3")
                          + " realSpeed=" + (body != null ? body.RealGroundSpeed.ToString("F3") : "unknown") + " state=" + state);
            };
            ZombieEnemy.OutOfWorldRemoved += removed;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 20f)
                {
                    maxAlive = Math.Max(maxAlive, _director.ActiveCount);
                    int ring = 0;
                    foreach (ZombieEnemy z in ZombieEnemy.Live) if (z != null && z.IsAlive && Flat(z.transform.position, _rig.transform.position) <= 6.5f) ring++;
                    maxRing = Math.Max(maxRing, ring);
                    yield return null;
                }

                Debug.Log("[DD-TEST] wp084.watchdog.guard maxAlive=" + maxAlive + " maxWithin6.5m=" + maxRing + " recoveries=" + ZombieEnemy.StuckRecoveries + " removals=" + ZombieEnemy.StuckRemovals);
                // a back row exists (the front ring round a player holds ~8–10): r9b 06.10 read 19 within 6.5 m in 20 s
                Assert.GreaterOrEqual(maxRing, 15, "kaynak yok: oyuncunun çevresinde arka sıralı kalabalık oluşmadı");
                Assert.AreEqual(0, ZombieEnemy.StuckRemovals, "kalabalıkta bekleyen gövde takılma sayılıp kaldırıldı");
            }
            finally
            {
                ZombieEnemy.OutOfWorldRemoved -= removed;
                ZombieEnemy.StuckWindowOverride = 0f;
                _director.ForceActiveTarget(0, false);
            }
        }

        private static ZombieEnemy NearestLive(Vector3 where)
        {
            ZombieEnemy best = null;
            float bd = float.MaxValue;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsAlive) continue;
                float d = (z.transform.position - where).sqrMagnitude;
                if (d < bd) { bd = d; best = z; }
            }

            return bd < 0.25f ? best : null;
        }

        private static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>Pixels of the game camera's frame (960×540) that change when <paramref name="body"/> is hidden at this instant — what vanishes on screen.</summary>
        private static int ChangedPixels(Camera cam, ZombieEnemy body)
        {
            var hidden = new List<Renderer>();
            Color32[] with = Frame(cam);
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; hidden.Add(r); }
            Color32[] without;
            try { without = Frame(cam); }
            finally { foreach (Renderer r in hidden) r.enabled = true; }
            int n = 0;
            for (int i = 0; i < with.Length; i++)
            {
                Color32 a = with[i], b = without[i];
                if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 12) n++;
            }

            return n;
        }

        private static Color32[] Frame(Camera cam)
        {
            var rt = new RenderTexture(960, 540, 24) { name = "WP-084 watchdog frame", antiAliasing = 1 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            RenderTexture target = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0, false);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                cam.targetTexture = target;
                RenderTexture.active = prev;
                rt.Release();
            }
        }

        // ============================================================== P1

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P1_Round1BaslarOyuncuMerkezdeHavuzDogru()
        {
            yield return RunSahnesiniHazirla();

            Assert.AreEqual(1, _run.Round, "Koşu 1. turdan başlamalı (REQ-010).");
            Assert.AreEqual(1, _director.Round);
            Assert.IsTrue(_director.State == RoundState.Active || _director.State == RoundState.PoolDepleted,
                "Round 1 akmıyor: state=" + _director.State);

            RoundPopulationSnapshot p = _director.Population;
            Assert.AreEqual(R1Havuz, p.PoolTotal, "Round 1 havuzu formül değeri değil.");
            Assert.AreEqual(R1Hedef, p.TargetActive);
            Assert.AreEqual(Tavan, p.Ceiling);
            Assert.AreEqual(p.PoolTotal, p.Unspawned + p.SpawnedTotal, "Havuz muhasebesi: unspawned + spawned ≠ toplam.");
            Assert.AreEqual(R1Havuz, _run.RoundPoolTotal, "RunDirector havuz toplamı director ile aynı olmalı.");
            Assert.AreEqual(1, RunEvents.Current.Round, "HUD anlık görüntüsü tur 1 göstermiyor.");

            // REQ-010: merkez başlangıç.
            Vector3 konum = _rig.transform.position;
            Vector3 fark = konum - WorldLayout.PlayerStart;
            fark.y = 0f;
            Assert.Less(fark.magnitude, 2.5f, "Oyuncu merkez başlangıç noktasında değil: " + konum + " (beklenen " + WorldLayout.PlayerStart + ")");
            Assert.AreEqual(RingRegion.Center, RingRegionMap.Classify(konum), "Oyuncu başlangıcı Center bölgesinde değil.");

            Debug.Log("[DD-TEST] wp003.p1 round=1 pool=" + p.PoolTotal + " target=" + p.TargetActive +
                      " ceiling=" + p.Ceiling + " playerPos=" + konum + " region=" + RingRegionMap.Classify(konum));
        }

        // ============================================================== P2

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P2_DusmanYalnizAuthoringNoktalarindanDogar_HedefeTamamlanir_TavanAsilmaz()
        {
            yield return RunSahnesiniHazirla();

            // Abonelik yeni round'dan ÖNCE: ilk refill tiki BeginRound'un ilk karesinde gelir.
            var baglamlar = new List<EnemySpawnContext>(32);
            Action<EnemySpawnContext> kayit = c => baglamlar.Add(c);
            _director.EnemySpawned += kayit;
            yield return YeniRound();

            int enYuksekYasayan = 0;
            int enYuksekGovde = 0;
            float t0 = Time.realtimeSinceStartup;

            try
            {
                while (Time.realtimeSinceStartup - t0 < DolusZamanAsimi && _director.ActiveCount < R1Hedef)
                {
                    DegismezleriDogrula("doluş");
                    enYuksekYasayan = Mathf.Max(enYuksekYasayan, _director.ActiveCount);
                    enYuksekGovde = Mathf.Max(enYuksekGovde, _director.OccupiedCount);
                    Assert.LessOrEqual(_director.ActiveCount, R1Hedef, "Yaşayan sayısı hedefi aştı.");
                    yield return null;
                }
            }
            finally
            {
                _director.EnemySpawned -= kayit;
            }

            float dolusSuresi = Time.realtimeSinceStartup - t0;
            Assert.AreEqual(R1Hedef, _director.ActiveCount,
                "Hedef aktif sayıya " + DolusZamanAsimi + " s içinde ulaşılmadı: " + _director.DescribeFlow());
            Assert.AreEqual(DirectorYasayanZombi(), _director.ActiveCount, "Director yaşayan sayısı ZombieEnemy.Live ile uyuşmuyor.");

            // REQ-065: her spawn kayıtlı bir noktadan, nokta etrafındaki sınırlı saçılım içinde.
            Assert.GreaterOrEqual(baglamlar.Count, R1Hedef, "Spawn olayları eksik.");
            float tolerans = _director.SpawnScatterRadius + 2f;   // saçılım + NavMesh projeksiyon yarıçapı (ScatterAround)
            IReadOnlyList<SpawnPointRuntime> noktalar = SpawnPointRegistry.Points;
            var kullanilan = new HashSet<string>();

            for (int i = 0; i < baglamlar.Count; i++)
            {
                EnemySpawnContext c = baglamlar[i];
                Assert.IsTrue(c.PointIndex >= 0 && c.PointIndex < noktalar.Count, "Spawn bağlamı kayıtlı olmayan nokta indeksi taşıyor: " + c.PointIndex);
                SpawnPointRuntime nokta = noktalar[c.PointIndex];
                Assert.AreEqual(nokta.Id, c.PointId);
                Assert.IsTrue(nokta.HasMarker, "Nokta haritadaki bir SpawnPointMarker'a bağlı değil: " + nokta.Id);
                Assert.IsTrue(nokta.Enabled);
                Assert.Greater(c.Ticket, 0, "Bilet 0: yaşam döngüsü sözleşmesi verilmedi.");
                Assert.IsNotNull(c.Lifecycle);

                Vector3 fark = c.Position - nokta.NavPosition;
                fark.y = 0f;
                Assert.LessOrEqual(fark.magnitude, tolerans,
                    "Spawn konumu " + c.PointId + " noktasından " + fark.magnitude.ToString("F2") + " m uzakta (izin " + tolerans + " m).");
                kullanilan.Add(c.PointId);
            }

            Assert.LessOrEqual(enYuksekGovde, Tavan);
            Debug.Log("[DD-TEST] wp003.p2 fill=" + dolusSuresi.ToString("F2") + "s spawns=" + baglamlar.Count +
                      " alive=" + _director.ActiveCount + " bodies=" + _director.OccupiedCount + " points=" + string.Join(",", kullanilan) +
                      " | " + _director.DescribeFlow());
        }

        // ============================================================== P3

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P3_ZorlanmisYukteTeknikTavanAsilmaz()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();
            _rig.Health.DiagnosisInvulnerable = true;   // ölçüm oyuncu ölümüyle kesilmesin (tanı)

            int enYuksekGovde = 0;
            int enYuksekLive = 0;
            int kare = 0;

            _director.ForceActiveTarget(Tavan, true);
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 8f)
                {
                    DegismezleriDogrula("zorlama");
                    enYuksekGovde = Mathf.Max(enYuksekGovde, _director.OccupiedCount);
                    enYuksekLive = Mathf.Max(enYuksekLive, ZombieEnemy.Live.Count);
                    kare++;
                    yield return null;
                }
            }
            finally
            {
                _director.ForceActiveTarget(0, false);
                _rig.Health.DiagnosisInvulnerable = false;
            }

            Assert.LessOrEqual(enYuksekGovde, Tavan, "Teknik tavan aşıldı: " + enYuksekGovde);
            Assert.LessOrEqual(enYuksekLive, Tavan, "Sahnedeki gövde sayısı tavanı aştı: " + enYuksekLive);
            Assert.GreaterOrEqual(enYuksekGovde, Mathf.RoundToInt(Tavan * 0.75f),
                "Zorlama tavana yaklaşmadı (" + enYuksekGovde + "); test tavanı gerçekten sınamıyor. " + _director.DescribeFlow());

            Debug.Log("[DD-TEST] wp003.p3 frames=" + kare + " maxBodies=" + enYuksekGovde + "/" + Tavan + " maxLive=" + enYuksekLive + " | " + _director.DescribeFlow());
        }

        // ============================================================== P4

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P4_OlumYenidenDoldurur_HavuzBitinceDurur_SonOlumdeRoundBirKezBiter_CesetSayilmaz()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();

            int tamamlandi = 0;
            int havuzTukendi = 0;
            int runEventsTamamlandi = 0;
            var durumlar = new List<RoundState>();
            Action<int> t1 = r => tamamlandi++;
            Action<int> t2 = r => havuzTukendi++;
            Action<int> t3 = r => runEventsTamamlandi++;
            Action<RoundState> t4 = s => durumlar.Add(s);
            _director.RoundCleared += t1;
            _director.RoundPoolExhausted += t2;
            RunEvents.RoundCompleted += t3;
            _director.RoundStateChanged += t4;

            try
            {
                yield return Bekle(() => _director.ActiveCount >= 3, DolusZamanAsimi, "En az 3 yaşayan düşman doğmadı.");

                // ---- ölüm anında yaşayan düşer, ceset gövde olarak kalır
                int yasayanOnce = _director.ActiveCount;
                int govdeOnce = _director.OccupiedCount;
                int liveOnce = ZombieEnemy.Live.Count;
                int spawnedOnce = _director.Population.SpawnedTotal;
                int killedOnce = _director.Population.Killed;

                ZombieEnemy kurban = BirYasayanDirectorZombisi();
                Assert.IsNotNull(kurban);
                kurban.Kill();

                Assert.IsFalse(kurban.IsAlive);
                Assert.AreEqual(yasayanOnce - 1, _director.ActiveCount, "Ölüm anında yaşayan sayısı düşmedi (ceset yaşayan sayıldı).");
                Assert.AreEqual(govdeOnce, _director.OccupiedCount, "Ceset despawn olmadan gövde sayısı düşmemeli.");
                Assert.AreEqual(liveOnce, ZombieEnemy.Live.Count, "Ceset ZombieEnemy.Live'da kalmalı (animasyon sürüyor).");
                Assert.AreEqual(killedOnce + 1, _director.Population.Killed);
                Assert.AreEqual(_run.RoundKills, _director.Population.Killed, "RunDirector kill sayısı director ile uyuşmuyor.");

                // ---- havuz kaldıysa yenisi gelir (REQ-019)
                Assert.Greater(_director.RemainingInPool, 0);
                float t0 = Time.realtimeSinceStartup;
                float ustSinir = RefillAraligi * 4f + 1f;
                yield return Bekle(() => _director.Population.SpawnedTotal > spawnedOnce, ustSinir,
                    "Ölümden sonra havuz doluyken yeni düşman gelmedi: " + _director.DescribeFlow());
                Debug.Log("[DD-TEST] wp003.p4 replenish=" + (Time.realtimeSinceStartup - t0).ToString("F2") + "s");

                // ---- havuzu ölümle tüket
                int spawnedHavuzBitince = -1;
                int tamamlanmaKaresi = -1;
                int kare = 0;
                float baslangic = Time.realtimeSinceStartup;

                while (Time.realtimeSinceStartup - baslangic < 90f)
                {
                    DegismezleriDogrula("tüketme");
                    RoundPopulationSnapshot p = _director.Population;

                    if (p.Unspawned == 0)
                    {
                        if (spawnedHavuzBitince < 0)
                        {
                            spawnedHavuzBitince = p.SpawnedTotal;
                        }

                        Assert.AreEqual(spawnedHavuzBitince, p.SpawnedTotal, "Havuz bittikten sonra yeni düşman üretildi.");
                    }

                    if (tamamlandi > 0)
                    {
                        tamamlanmaKaresi = kare;
                        break;
                    }

                    TumYasayanlariOldur();
                    kare++;
                    yield return null;
                }

                Assert.AreEqual(1, tamamlandi, "RoundCleared tam bir kez yayınlanmalı (ölçülen " + tamamlandi + ").");
                Assert.AreEqual(1, havuzTukendi, "RoundPoolExhausted tam bir kez yayınlanmalı.");
                Assert.AreEqual(1, runEventsTamamlandi, "RunEvents.RoundCompleted tam bir kez yayınlanmalı.");
                Assert.AreEqual(RoundState.Completed, _director.State);
                Assert.AreEqual(RunPhase.RoundComplete, _run.Phase, "RunDirector RoundComplete fazına geçmedi.");
                Assert.AreEqual(R1Havuz, _director.Population.Killed, "Öldürülen sayısı havuz toplamına eşit olmalı.");
                Assert.AreEqual(R1Havuz, _director.Population.SpawnedTotal);
                Assert.AreEqual(0, _director.Population.Unspawned);
                Assert.AreEqual(0, _director.ActiveCount);
                Assert.AreEqual(1, _director.LastCompletedRound.Round);
                Assert.AreEqual(R1Havuz, _director.LastCompletedRound.Killed);
                Assert.AreEqual(RoundState.Completed, _director.LastCompletedRound.State);

                // Tamamlanma cesetleri BEKLEMEDİ: son ölümde yerde en az bir ceset var.
                Assert.Greater(ZombieEnemy.Live.Count, 0, "Tamamlanma anında sahnede ceset yok — bitiş ceset despawn'ına kadar gecikmiş olabilir.");
                Assert.Greater(_director.OccupiedCount, 0, "Tamamlanma anında gövde sayısı sıfır — ceset gövde olarak sayılmalı.");

                CollectionAssert.AreEqual(
                    new[] { RoundState.PoolDepleted, RoundState.Completing, RoundState.Completed },
                    durumlar, "State sırası: " + string.Join(" → ", durumlar));

                // Tamamlanma sonrası fazladan yayın yok (birkaç kare daha).
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(1, tamamlandi);
                Assert.AreEqual(1, runEventsTamamlandi);

                Debug.Log("[DD-TEST] wp003.p4 completion frame=" + tamamlanmaKaresi + " corpses@completion=" + ZombieEnemy.Live.Count +
                          " | " + _director.DescribeFlow());
            }
            finally
            {
                _director.RoundCleared -= t1;
                _director.RoundPoolExhausted -= t2;
                RunEvents.RoundCompleted -= t3;
                _director.RoundStateChanged -= t4;
                RunFlowClock.ClearHolds();
            }
        }

        // ============================================================== P5

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P5_PauseSpawnDurdurur_AcKapaSayaclariBozmaz()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();

            yield return Bekle(() => _director.ActiveCount >= 2, DolusZamanAsimi, "En az 2 düşman doğmadı.");

            int spawnedOnce = _director.Population.SpawnedTotal;
            int tamamlandi = 0;
            Action<int> t1 = r => tamamlandi++;
            _director.RoundCleared += t1;

            try
            {
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.IsTrue(RunFlowClock.IsFrozen);
                Assert.AreEqual(0f, Time.timeScale, 0.0001f);

                RoundPopulationSnapshot donmus = _director.Population;
                var konumlar = new Dictionary<ZombieEnemy, Vector3>();
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++) konumlar[live[i]] = live[i].transform.position;

                float t0 = Time.realtimeSinceStartup;
                int kare = 0;
                while (Time.realtimeSinceStartup - t0 < 2f)
                {
                    Assert.AreEqual(0f, Time.deltaTime, 0.0001f, "Pause'ta deltaTime sıfır değil.");
                    RoundPopulationSnapshot p = _director.Population;
                    Assert.AreEqual(donmus.SpawnedTotal, p.SpawnedTotal, "Pause'ta spawn oluştu.");
                    Assert.AreEqual(donmus.Alive, p.Alive, "Pause'ta yaşayan sayısı değişti.");
                    Assert.AreEqual(donmus.Occupied, p.Occupied, "Pause'ta gövde sayısı değişti.");
                    Assert.AreEqual(donmus.Unspawned, p.Unspawned, "Pause'ta havuz değişti.");
                    Assert.AreEqual(donmus.Killed, p.Killed);
                    Assert.AreEqual(donmus.State, p.State);
                    kare++;
                    yield return null;
                }

                foreach (KeyValuePair<ZombieEnemy, Vector3> kv in konumlar)
                {
                    if (kv.Key == null) continue;
                    Assert.Less((kv.Key.transform.position - kv.Value).magnitude, 0.001f, "Pause'ta düşman hareket etti.");
                }

                Debug.Log("[DD-TEST] wp003.p5 pauseFrames=" + kare + " spawned=" + donmus.SpawnedTotal + " alive=" + donmus.Alive);

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.IsFalse(RunFlowClock.IsFrozen);

                // Açıldıktan hemen sonra biriken tik patlaması yok: en fazla bir tik (≤ TikBasinaSpawn spawn).
                RoundPopulationSnapshot sonrasi = _director.Population;
                Assert.LessOrEqual(sonrasi.SpawnedTotal - donmus.SpawnedTotal, TikBasinaSpawn,
                    "Pause sonrası biriken spawn patlaması: " + (sonrasi.SpawnedTotal - donmus.SpawnedTotal));
                Assert.AreEqual(sonrasi.PoolTotal, sonrasi.Unspawned + sonrasi.SpawnedTotal, "Havuz muhasebesi bozuldu.");
                Assert.AreEqual(DirectorYasayanZombi(), _director.ActiveCount, "Yaşayan sayısı sahneyle uyuşmuyor.");
                DegismezleriDogrula("pause sonrası");

                // Akış devam eder: hedefin altındaysa yeni spawn gelir.
                if (_director.ActiveCount < R1Hedef && _director.RemainingInPool > 0)
                {
                    yield return Bekle(() => _director.Population.SpawnedTotal > sonrasi.SpawnedTotal, RefillAraligi * 4f + 1f,
                        "Pause sonrası spawn akışı devam etmedi.");
                }

                Assert.AreEqual(0, tamamlandi);
                Assert.GreaterOrEqual(_director.Population.SpawnedTotal, spawnedOnce);
            }
            finally
            {
                _director.RoundCleared -= t1;
                RunFlowClock.ClearHolds();
            }
        }

        // ============================================================== P6

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P6_OyuncuOlunceSpawnDurur_GecikmisCallbackSizmaz_YeniKosuTemizVeTekDirector()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();

            yield return Bekle(() => _director.ActiveCount >= 2, DolusZamanAsimi, "En az 2 düşman doğmadı.");

            int tamamlandi = 0;
            int runTamamlandi = 0;
            Action<int> t1 = r => tamamlandi++;
            Action<int> t3 = r => runTamamlandi++;
            _director.RoundCleared += t1;
            RunEvents.RoundCompleted += t3;

            EnemySpawnDirector eskiDirector = _director;
            try
            {
                _rig.Health.ApplyDamage(_rig.Health.Max * 10f, _rig.transform.position + Vector3.forward, "wp-003-test");
                yield return Bekle(() => _run.Phase == RunPhase.Dead, 5f, "Koşu Dead fazına geçmedi.");

                Assert.AreEqual(RoundState.Aborted, _director.State, "Oyuncu ölümünde round Aborted olmalı.");
                Assert.IsFalse(_director.IsRoundActive);

                int spawnedOlumde = _director.Population.SpawnedTotal;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 2f)
                {
                    Assert.AreEqual(spawnedOlumde, _director.Population.SpawnedTotal, "Oyuncu ölümünden sonra spawn üretildi.");
                    Assert.AreEqual(RoundState.Aborted, _director.State);
                    yield return null;
                }

                // Gecikmiş ölümler: kalan düşmanlar ölse de round tamamlanmaz.
                TumYasayanlariOldur();
                for (int i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(0, tamamlandi, "Koşu bittikten sonra RoundCleared yayınlandı.");
                Assert.AreEqual(0, runTamamlandi, "Koşu bittikten sonra RunEvents.RoundCompleted yayınlandı.");
                Assert.AreEqual(RoundState.Aborted, _director.State);
                Assert.AreEqual(RunPhase.Dead, _run.Phase);
                Debug.Log("[DD-TEST] wp003.p6 dead: " + _director.DescribeFlow());
            }
            finally
            {
                _director.RoundCleared -= t1;
                RunEvents.RoundCompleted -= t3;
            }

            // ---- yeni koşu: sahne yeniden yüklenir (Results → Selection → Run akışının Run ucu)
            int runTamamlandi2 = 0;
            Action<int> t5 = r => runTamamlandi2++;
            RunEvents.RoundCompleted += t5;
            try
            {
                SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
                yield return null;
                yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null && RunDirector.Instance != _run,
                    KurulumZamanAsimi, "Yeni Run sahnesi / yeni RunDirector oluşmadı.");

                EnemySpawnDirector[] directorlar = UnityEngine.Object.FindObjectsOfType<EnemySpawnDirector>();
                Assert.AreEqual(1, directorlar.Length, "Sahne yeniden yüklenince EnemySpawnDirector sayısı 1 değil: " + directorlar.Length);
                Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<RunDirector>().Length, "RunDirector sayısı 1 değil.");
                Assert.AreNotSame(eskiDirector, EnemySpawnDirector.Instance, "Eski director yeni sahnede yaşıyor.");
                Assert.AreSame(directorlar[0], EnemySpawnDirector.Instance);

                yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Yeni koşu Fighting'e girmedi.");
                _run = RunDirector.Instance;
                _director = EnemySpawnDirector.Instance;

                RoundPopulationSnapshot p = _director.Population;
                Assert.AreEqual(1, _run.Round, "Yeni koşu 1. turdan başlamalı.");
                Assert.AreEqual(1, p.Round);
                Assert.AreEqual(0, p.Killed, "Yeni koşu önceki koşunun kill sayısını taşıdı.");
                Assert.AreEqual(0, _run.RoundKills);
                Assert.AreEqual(R1Havuz, p.PoolTotal);
                Assert.AreEqual(p.PoolTotal, p.Unspawned + p.SpawnedTotal);
                Assert.AreEqual(p.Alive, p.Occupied, "Yeni koşu başında ceset var: önceki koşunun gövdeleri taşındı.");
                Assert.AreEqual(p.Alive, DirectorYasayanZombi(), "Yeni koşuda director dışı / eski koşudan zombi var.");

                int eskiKosudanZombi = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null && (live[i].SpawnTicket <= 0 || live[i].IsDying)) eskiKosudanZombi++;
                }

                Assert.AreEqual(0, eskiKosudanZombi, "Yeni koşuda eski koşudan kalan/biletsiz zombi var.");

                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual(0, runTamamlandi2, "Önceki koşunun gecikmiş completion'ı yeni koşuya sızdı.");
                Assert.IsTrue(_run.Phase == RunPhase.Fighting, "Yeni koşu fazı bozuldu: " + _run.Phase);

                Debug.Log("[DD-TEST] wp003.p6 newRun: " + _director.DescribeFlow() + " directors=" + directorlar.Length);
            }
            finally
            {
                RunEvents.RoundCompleted -= t5;
            }
        }

        // ============================================================== P7

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P7_CokUzakNoktaKullanilmaz_OyuncuHareketEdinceDagilimDegisir()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();
            _rig.Health.DiagnosisInvulnerable = true;

            // WP-076 (afabd44f): V6 varsayılan dünya — tur başı profil v6, sınır 75 m (AP005 zorlamalı yolda config'in 62 m'si)
            Assert.IsTrue(SpawnWorldProfile.IsV6, "varsayılan dünya V6 değil");
            _sinir = SpawnWorldProfile.IsV6 ? SpawnWorldProfile.V6MaxUseful : MaxUseful;
            var rapor = new StringBuilder("[DD-TEST] wp003.p7 sınır=" + _sinir + "m\n");

            // Konum A: kuzeydoğu (taş yol köşesi yakını). Konum B: güneybatı (bambu girişi
            // kavşağı yakını). İkisi de halka üzerinde yürünebilir zemin; her birinde karşı
            // taraftaki girişlerin yol mesafesi etkin MaxUsefulDistance'ı aşar (V6: 75 m; A'dan
            // SW_TeaHouse / W_Bamboo*, B'den NE_Corner / N_RearEast / E_Garden* düz çizgide 76–95 m).
            // WP-030: A, AP-005 dünyasında (WP-014) (20,16)'dan en uzak nokta 61,7 m kalıyordu (kapı
            // sınanmıyor — WP-018 tabanında da FAIL); köşeye taşındı: SW girişleri > 62 m.
            Vector3 konumA = new Vector3(25f, 0.1f, 21f);
            Vector3 konumB = new Vector3(-26f, 0.1f, -21f);

            Dictionary<string, float> agirlikA = null;
            Dictionary<string, float> agirlikB = null;
            HashSet<string> uzakA = null;
            HashSet<string> uzakB = null;
            Dictionary<string, int> spawnA = null;
            Dictionary<string, int> spawnB = null;

            try
            {
                yield return OlcumKonumunda(konumA, rapor, "A", v => agirlikA = v, u => uzakA = u, h => spawnA = h);
                yield return OlcumKonumunda(konumB, rapor, "B", v => agirlikB = v, u => uzakB = u, h => spawnB = h);
            }
            finally
            {
                _director.ForceActiveTarget(0, false);
                _rig.Health.DiagnosisInvulnerable = false;
                Debug.Log(rapor.ToString());
            }

            // REQ-066: çok uzak nokta sıfır ağırlık alır ve kullanılmaz.
            Assert.Greater(uzakA.Count, 0, "A konumunda hiç 'too far' nokta yok; test uzaklık kapısını sınamıyor.\n" + rapor);
            Assert.Greater(uzakB.Count, 0, "B konumunda hiç 'too far' nokta yok.\n" + rapor);
            foreach (string id in uzakA) Assert.IsFalse(spawnA.ContainsKey(id), "A: çok uzak nokta kullanıldı: " + id + "\n" + rapor);
            foreach (string id in uzakB) Assert.IsFalse(spawnB.ContainsKey(id), "B: çok uzak nokta kullanıldı: " + id + "\n" + rapor);

            // Oyuncu hareket edince dağılım değişir: uzak kümeleri ve ağırlık vektörü farklı.
            Assert.IsFalse(uzakA.SetEquals(uzakB), "A ve B konumlarında aynı noktalar uzak: konum ağırlığı etkilemiyor.\n" + rapor);
            int farkli = 0;
            foreach (KeyValuePair<string, float> kv in agirlikA)
            {
                if (Mathf.Abs(kv.Value - agirlikB[kv.Key]) > 0.001f) farkli++;
            }

            Assert.Greater(farkli, agirlikA.Count / 2, "Ağırlık vektörü iki konumda büyük ölçüde aynı kaldı.\n" + rapor);
            Assert.IsFalse(new HashSet<string>(spawnA.Keys).SetEquals(spawnB.Keys),
                "İki konumda tamamen aynı nokta kümesi kullanıldı; dağılım değişmedi.\n" + rapor);
            Assert.Greater(spawnA.Count, 0);
            Assert.Greater(spawnB.Count, 0);
        }

        private IEnumerator OlcumKonumunda(Vector3 konum, StringBuilder rapor, string etiket,
            Action<Dictionary<string, float>> agirlik, Action<HashSet<string>> uzak, Action<Dictionary<string, int>> histogram)
        {
            _director.ForceActiveTarget(0, false);
            _director.ClearAllEnemies();
            _rig.Teleport(konum, Quaternion.identity);
            yield return null;
            yield return null;   // crowd oyuncu durumu (NavigationSystemBehaviour) yeni konumu görsün

            Vector3 gercek = _rig.transform.position;
            Assert.Less(Vector3.Distance(new Vector3(gercek.x, 0f, gercek.z), new Vector3(konum.x, 0f, konum.z)), 1.5f,
                etiket + ": oyuncu istenen konuma taşınamadı (" + gercek + ").");

            _director.RefreshPointsNow();

            // Ölçüm: yüksek talep (zorlama) altında spawn histogramı; her spawn'dan sonra
            // ağırlıklar kaydedilir. Cesetler/ yoğunluk histogramı etkilemesin diye
            // örnekleme kısa tutulur ve gövdeler periyodik temizlenir.
            var hist = new Dictionary<string, int>();
            Action<EnemySpawnContext> kayit = c =>
            {
                hist.TryGetValue(c.PointId, out int n);
                hist[c.PointId] = n + 1;
            };

            _director.EnemySpawned += kayit;
            _director.ForceActiveTarget(Tavan, true);
            try
            {
                float t0 = Time.realtimeSinceStartup;
                int temizlikSayaci = 0;
                while (Time.realtimeSinceStartup - t0 < 4f)
                {
                    DegismezleriDogrula(etiket);
                    if (++temizlikSayaci % 20 == 0)
                    {
                        _director.ClearAllEnemies();
                    }

                    yield return null;
                }
            }
            finally
            {
                _director.EnemySpawned -= kayit;
                _director.ForceActiveTarget(0, false);
                _director.ClearAllEnemies();
            }

            // Son değerlendirmenin ağırlıkları ve "too far" kümesi.
            _director.RefreshPointsNow();
            var agirliklar = new Dictionary<string, float>();
            var uzakKume = new HashSet<string>();
            IReadOnlyList<SpawnPointRuntime> noktalar = SpawnPointRegistry.Points;
            rapor.AppendLine("  konum " + etiket + " = " + gercek + " region=" + RingRegionMap.Classify(gercek));
            for (int i = 0; i < noktalar.Count; i++)
            {
                SpawnPointRuntime p = noktalar[i];
                agirliklar[p.Id] = p.LastWeight;
                bool cokUzak = p.PathDistance > _sinir;
                if (cokUzak)
                {
                    uzakKume.Add(p.Id);
                    Assert.AreEqual(0f, p.LastWeight, etiket + ": çok uzak nokta sıfır ağırlık almadı: " + p.Id + " path=" + p.PathDistance);
                }

                hist.TryGetValue(p.Id, out int n);
                rapor.AppendLine(string.Format("    {0,-22} path={1,6:0.0}m {2,-8} w={3,6:0.000} spawns={4} {5}",
                    p.Id, p.PathDistance, p.PathStatus, p.LastWeight, n, p.LastWeight > 0f ? "" : "[" + p.LastRejectReason + "]"));
            }

            agirlik(agirliklar);
            uzak(uzakKume);
            histogram(hist);
        }

        // ============================================================== P8 (gözlem, kapı değil)

        [UnityTest]
        [Category("WP003")]
        [Timeout(300000)]
        public IEnumerator P8_PerformansVeReq123Gozlemi_RoundYukuAltinda()
        {
            yield return RunSahnesiniHazirla();
            yield return YeniRound();

            var kareMs = new List<float>(1500);
            var spawnKareMs = new List<float>(64);
            int gc0 = GC.CollectionCount(0);
            long mono0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var gcAlloc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long gcAllocToplam = 0;
            int gcAllocOrnek = 0;
            long spawnKareAllocToplam = 0;
            int spawnKareSayisi = 0;
            int spawnBuKare = 0;
            Action<EnemySpawnContext> kayit = c => spawnBuKare++;
            _director.EnemySpawned += kayit;

            float canBaslangic = _rig.Health.Current;
            float enDusukCan = canBaslangic;
            int hasarOlayi = 0;
            Action<PlayerDamageInfo> hasar = h => hasarOlayi++;
            PlayerHealth.AnyDamaged += hasar;

            int enYuksekYasayan = 0;
            float baslangic = Time.realtimeSinceStartup;
            float oncekiKare = baslangic;
            bool oldu = false;

            try
            {
                // Doğal round akışı: hedef 9 yaşayan, oyuncu merkezde hareketsiz ve ateş etmiyor
                // (hayatta kalma kapasitesi gözlemi — REQ-072 tempo / WP-002 §27 REQ-123 notu).
                // 25 s: girişler merkeze 30+ m, Standard 2.7 m/s → ilk temas ~12 s'de; pencere
                // sürünün gelişini ve ilk saldırı dalgalarını kapsar.
                while (Time.realtimeSinceStartup - baslangic < 25f)
                {
                    spawnBuKare = 0;
                    yield return null;
                    float simdi = Time.realtimeSinceStartup;
                    float ms = (simdi - oncekiKare) * 1000f;
                    oncekiKare = simdi;
                    kareMs.Add(ms);
                    if (gcAlloc.Valid)
                    {
                        gcAllocToplam += gcAlloc.LastValue;
                        gcAllocOrnek++;
                        if (spawnBuKare > 0)
                        {
                            spawnKareAllocToplam += gcAlloc.LastValue;
                        }
                    }

                    if (spawnBuKare > 0)
                    {
                        spawnKareMs.Add(ms);
                        spawnKareSayisi++;
                    }

                    enYuksekYasayan = Mathf.Max(enYuksekYasayan, _director.ActiveCount);
                    enDusukCan = Mathf.Min(enDusukCan, _rig.Health.Current);
                    if (_run.Phase == RunPhase.Dead)
                    {
                        oldu = true;
                        break;
                    }
                }
            }
            finally
            {
                gcAlloc.Dispose();
                _director.EnemySpawned -= kayit;
                PlayerHealth.AnyDamaged -= hasar;
            }

            int gc1 = GC.CollectionCount(0);
            long mono1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            kareMs.Sort();
            spawnKareMs.Sort();
            float p50 = kareMs[kareMs.Count / 2];
            float p95 = kareMs[Mathf.Min(kareMs.Count - 1, Mathf.RoundToInt(kareMs.Count * 0.95f))];
            float max = kareMs[kareMs.Count - 1];
            float spawnP50 = spawnKareMs.Count > 0 ? spawnKareMs[spawnKareMs.Count / 2] : -1f;
            float spawnMax = spawnKareMs.Count > 0 ? spawnKareMs[spawnKareMs.Count - 1] : -1f;
            float sure = Time.realtimeSinceStartup - baslangic;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-OBS] wp003.p8 round yükü gözlemi (kapı değil; Editor batchmode, gerçek kare)");
            rapor.AppendLine("  kare: n=" + kareMs.Count + " p50=" + p50.ToString("F2") + " ms p95=" + p95.ToString("F2") + " ms max=" + max.ToString("F2") + " ms");
            rapor.AppendLine("  spawn kareleri: n=" + spawnKareSayisi + " p50=" + spawnP50.ToString("F2") + " ms max=" + spawnMax.ToString("F2") +
                             " ms gc-alloc/spawn-kare avg=" + (spawnKareSayisi > 0 ? spawnKareAllocToplam / spawnKareSayisi : -1) + " B");
            rapor.AppendLine("  GC: collections(gen0)=" + (gc1 - gc0) + " mono heap delta=" + ((mono1 - mono0) / 1024) + " KB" +
                             " gc-alloc/frame avg=" + (gcAllocOrnek > 0 ? gcAllocToplam / gcAllocOrnek : -1) + " B (" + (gcAlloc.Valid ? "recorder" : "recorder yok") + ")");
            rapor.AppendLine("  nüfus: " + _director.DescribeFlow() + " maxAlive=" + enYuksekYasayan + " crowd=" + CrowdService.MemberCount);
            rapor.AppendLine("  REQ-072/REQ-123 gözlemi: oyuncu hareketsiz+ateşsiz " + sure.ToString("F1") + " s; can " + canBaslangic.ToString("F0") + " → min " +
                             enDusukCan.ToString("F0") + " → son " + _rig.Health.Current.ToString("F0") + "; hasar olayı=" + hasarOlayi + "; öldü=" + oldu);
            Debug.Log(rapor.ToString());

            Assert.Greater(kareMs.Count, 60, "Ölçüm penceresinde 60'tan az kare; ölçüm boş.");
            Assert.Greater(enYuksekYasayan, 0, "Ölçüm süresince hiç düşman doğmadı.");

            // Oyuncu öldüyse meta akış (Boot'tan kalan PrototypeUiFlow) 1.6 s sonra Results'a
            // geçer; sonraki test Run sahnesini yüklemeden önce bu geçişin bitmesi beklenir,
            // yoksa gecikmiş geçiş bir sonraki testin sahnesini değiştirir (test izolasyonu).
            if (oldu)
            {
                yield return AkisiYatistir();
            }
        }

        /// <summary>Bekleyen Results geçişini (varsa) tüketir: en fazla 3 s, hata üretmez.</summary>
        private static IEnumerator AkisiYatistir()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f && SceneManager.GetActiveScene().name != PrototypeScenes.Results)
            {
                yield return null;
            }

            yield return null;
            Debug.Log("[DD-TEST] wp003 akış yatıştı: scene=" + SceneManager.GetActiveScene().name);
        }

        // ============================================================== yardımcılar

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye)
                {
                    Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }
    }
}
