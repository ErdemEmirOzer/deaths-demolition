using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · Special Weapon PlayMode testlerinin ortak kurulumu (KUR-B / KUR-C).
    ///
    /// Run sahnesini doğrudan yükler, düşman spawn'ını durdurur, sürülebilir girdiyi takar,
    /// config'in bir KOPYASINI etkinleştirir (test alanları bu kopyada ayarlanır; teardown eski
    /// config'i geri koyar) ve round köprüsünü test sağlayıcılarıyla sürer. Hiçbir sayı ürün
    /// değeri değildir; spawn kararı config'e (ihtimal 1, pencere tek nokta) ve köprüye bağlanır.
    /// </summary>
    internal sealed class SpecialWeaponSahne
    {
        /// <summary>Test parametresi: kurulum ve faz beklemeleri için gerçek-zaman tavanı.</summary>
        public const float KurulumZamanAsimi = 30f;
        public const float FazZamanAsimi = 25f;

        public PlayerRig Rig;
        public SavasTestGirdisi Girdi;
        public PrototypeConfig Config;
        public SpecialWeaponDirector Director;
        public WeaponController Controller;

        public int Tur = 1;
        public float Ilerleme;

        private IInputSource _oncekiGirdi;
        private PrototypeConfig _eskiConfig;
        private readonly List<GameObject> _kapatilanNoktalar = new List<GameObject>();
        private readonly List<GameObject> _testNoktalari = new List<GameObject>();
        private readonly List<ZombieEnemy> _dogurulan = new List<ZombieEnemy>();

        public SpecialWeaponConfigSection Special => Config.Weapons.Special;

        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            _oncekiGirdi = InputSourceRegistry.Current;
            _eskiConfig = PrototypeConfig.Active;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            yield return Bekle(() => UnityEngine.Object.FindObjectOfType<SpecialWeaponDirector>() != null, KurulumZamanAsimi, "SpecialWeaponDirector yok.");

            Rig = PlayerRegistry.Primary;
            Controller = WeaponRegistry.ActiveController;
            Director = UnityEngine.Object.FindObjectOfType<SpecialWeaponDirector>();
            Rig.Health.DiagnosisInvulnerable = true;

            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            EnemyFactory.ClearAll();
            yield return null;

            Girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(Girdi);
            Rig.Motor.SetInputSource(null);
            Rig.Aim.SetInputSource(null);

            // Config kopyası: test alanları burada değişir, sevkiyat asset'i dokunulmaz kalır.
            Config = UnityEngine.Object.Instantiate(_eskiConfig);
            PrototypeConfig.SetActive(Config);

            // Köprü: authoritative sağlayıcı yerine test sağlayıcısı (RunDirector'ın :870-874 dikişi,
            // sahne yeniden yüklenince RunDirector geri kurar).
            Tur = RunDirector.Instance.Round;
            Ilerleme = 0f;
            SpecialWeaponRoundBridge.CurrentRoundProvider = () => Tur;
            SpecialWeaponRoundBridge.RoundProgressProvider = () => Ilerleme;
            yield return null;

            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, Director.State, "Kurulumda director Idle değil.");
            Assert.AreEqual(0, AktifPickupSayisi(), "Kurulumda yerde pickup var.");
        }

        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);

            if (Girdi != null)
            {
                Girdi.Yon = Vector2.zero;
                Girdi.AtesBasili = false;
                Girdi.AtesBasti = false;
                Girdi.DoldurBasti = false;
            }

            for (int i = 0; i < _testNoktalari.Count; i++)
            {
                if (_testNoktalari[i] != null)
                {
                    UnityEngine.Object.Destroy(_testNoktalari[i]);
                }
            }

            _testNoktalari.Clear();

            for (int i = 0; i < _kapatilanNoktalar.Count; i++)
            {
                if (_kapatilanNoktalar[i] != null)
                {
                    _kapatilanNoktalar[i].SetActive(true);
                }
            }

            _kapatilanNoktalar.Clear();

            if (Director != null)
            {
                Director.SetRandomSource(null);
                Director.SetDropValidator(null);
                Director.MaxSpawnsPerRound = SpecialWeaponDirector.MaxSpawnsPerRoundDefault;
            }

            InputSourceRegistry.SetCurrent(_oncekiGirdi);
            EnemyFactory.ClearAll();
            _dogurulan.Clear();

            if (_eskiConfig != null)
            {
                PrototypeConfig.SetActive(_eskiConfig);
            }

            if (Config != null)
            {
                UnityEngine.Object.Destroy(Config);
                Config = null;
            }

            Rig = null;
            Girdi = null;
            Director = null;
            Controller = null;
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        /// <summary>Sevkiyat config'ini geri koyar (ikinci koşu sevkiyat ihtimaliyle başlasın diye); kopya teardown'da yok edilir.</summary>
        public void ConfigGeriYukle()
        {
            if (_eskiConfig != null && PrototypeConfig.Active != _eskiConfig)
            {
                PrototypeConfig.SetActive(_eskiConfig);
            }
        }

        // ------------------------------------------------------------------ spawn sürme

        /// <summary>
        /// Spawn kararını deterministik yapar: ihtimal 1, pencere tek nokta (mark = pencere),
        /// ikinci spawn kapalı. Kopya config üzerinde; sayılar test parametresidir.
        /// </summary>
        public void SpawnKesinlestir(float pencere, bool ikinciSpawn = false)
        {
            SpecialWeaponConfigSection s = Special;
            s.FirstEligibleRound = 1;
            s.BaseSpawnChance = 1f;
            s.MaxSpawnChance = 1f;
            s.SpawnChanceGrowthPerRound = 0f;
            s.SpawnWindowMin = pencere;
            s.SpawnWindowMax = pencere;
            s.SecondSpawnFromRound = ikinciSpawn ? 1 : int.MaxValue;
            s.SecondSpawnChance = ikinciSpawn ? 1f : 0f;
            Config.NotifyChanged();
        }

        /// <summary>Yazılı noktaları kapatır (statik listeden çıkarlar); teardown geri açar.</summary>
        public void NoktalariKapat()
        {
            IReadOnlyList<SpecialWeaponSpawnPoint> hepsi = SpecialWeaponSpawnPoint.All;
            var kopya = new List<SpecialWeaponSpawnPoint>(hepsi);
            for (int i = 0; i < kopya.Count; i++)
            {
                if (kopya[i] != null && kopya[i].gameObject.activeSelf)
                {
                    kopya[i].gameObject.SetActive(false);
                    _kapatilanNoktalar.Add(kopya[i].gameObject);
                }
            }
        }

        /// <summary>Verilen konuma bir test noktası ekler (OnEnable ile statik listeye girer).</summary>
        public SpecialWeaponSpawnPoint TestNoktasi(Vector3 konum)
        {
            var go = new GameObject("TestSpecialWeaponSpawnPoint");
            go.transform.position = konum;
            _testNoktalari.Add(go);
            return go.AddComponent<SpecialWeaponSpawnPoint>();
        }

        /// <summary>Yazılı noktaları kapatır, oyuncunun yanına tek bir test noktası koyar (3. geçiş mesafe kuralını gevşetir — KS-8, HEAD davranışı).</summary>
        public SpecialWeaponSpawnPoint TekTestNoktasi(Vector3 konum)
        {
            NoktalariKapat();
            return TestNoktasi(konum);
        }

        /// <summary>Round başlangıcını bildirir (schedule roll'u) ve ilerlemeyi mark'a taşır; OnGround'a kadar bekler.</summary>
        public IEnumerator SpawnEt(float pencere)
        {
            SpawnKesinlestir(pencere);
            Ilerleme = 0f;
            SpecialWeaponRoundBridge.NotifyRoundStarted(Tur);
            yield return null;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, Director.State, "Schedule roll'u ihtimal 1 ile Scheduled üretmedi.");
            Assert.AreEqual(pencere, Director.ScheduledProgress, 0.0001f, "Tek noktalı pencere mark'ı vermedi.");
            Ilerleme = pencere;
            yield return Bekle(() => Director.State == SpecialWeaponDirector.SpecialWeaponState.OnGround, FazZamanAsimi, "Special Weapon yere düşmedi.");

            // Silah seçimi üretim RNG'siyle (ağırlıklı) yapılır; hangi prototip silahın koştuğu
            // kanıt olarak log'a yazılır (Kagura/Ofuda/Tide yollarından hangisi kapsandı).
            Debug.Log("[DD-TEST] wp007.weapon spawned=" + WeaponEvents.Special.WeaponId + " test=" + TestContext.CurrentContext.Test.Name);
        }

        public SpecialWeaponPickup CanliPickup()
        {
            SpecialWeaponPickup[] hepsi = UnityEngine.Object.FindObjectsOfType<SpecialWeaponPickup>();
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].IsSpawned)
                {
                    return hepsi[i];
                }
            }

            return null;
        }

        public static int AktifPickupSayisi()
        {
            SpecialWeaponPickup[] hepsi = UnityEngine.Object.FindObjectsOfType<SpecialWeaponPickup>();
            int n = 0;
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].IsSpawned)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Oyuncuyu pickup'ın üstüne ışınlar; alma bir sonraki Update'te olur.</summary>
        public void PickupaGit(SpecialWeaponPickup pickup)
        {
            Rig.Motor.Teleport(pickup.transform.position);
        }

        /// <summary>Oyuncuyu pickup'tan uzağa (alma yarıçapının dışına) ışınlar.</summary>
        public void PickuptanUzaklas(SpecialWeaponPickup pickup, float mesafe)
        {
            Vector3 p = pickup.transform.position + Vector3.right * mesafe;
            p.y = Rig.transform.position.y;
            Rig.Motor.Teleport(p);
        }

        /// <summary>Oyuncudan uzakta, düz zeminde, oyuncuya bakan bir Standard doğurur (biletsiz test yolu).</summary>
        public ZombieEnemy Dogur(Vector3 konum)
        {
            Vector3 d = Rig.transform.position - konum;
            d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, konum, bakis);
            Assert.IsNotNull(z, "Zombi doğmadı (havuz/tavan).");
            _dogurulan.Add(z);
            return z;
        }

        public Vector3 Zemin(float xOffset, float zOffset)
        {
            Vector3 p = Rig.transform.position + new Vector3(xOffset, 0f, zOffset);
            p.y = Rig.transform.position.y;
            return p;
        }

        public static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
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

        public static IEnumerator Kareler(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// HUD haber banner'ının durumu (metin + kalan süre; `RunHudScreen` özel alanları,
        /// yansımayla). Alan adı değişirse test sessizce geçmez, burada kırmızı yanar.
        /// Doğal bitiş testi pozitif kontroldür (banner GÖRÜNÜR), ölüm testi kilittir.
        /// </summary>
        public static void HudBanner(out string metin, out float kalan)
        {
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");
            System.Reflection.FieldInfo ft = typeof(RunHudScreen).GetField("_bannerText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.FieldInfo fr = typeof(RunHudScreen).GetField("_bannerRemaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(ft, "RunHudScreen._bannerText alanı yok (test kancası eskidi).");
            Assert.IsNotNull(fr, "RunHudScreen._bannerRemaining alanı yok (test kancası eskidi).");
            var text = (TMPro.TMP_Text)ft.GetValue(hud);
            metin = text != null ? text.text : string.Empty;
            kalan = (float)fr.GetValue(hud);
        }
    }

    /// <summary>
    /// Special Weapon olay/ses/hasar sayacı. Yalnız dinler; hiçbir davranışı değiştirmez.
    /// </summary>
    internal sealed class SpecialOlaySayaci : IDisposable
    {
        public int Spawned, GroundExpired, PickedUp, FinalSeconds, Ended;
        public int Scheduled, Cancelled, Cleared;
        public float SonMark = -1f;
        public readonly List<SpecialWeaponCancelInfo> Iptaller = new List<SpecialWeaponCancelInfo>(4);
        public int ChangedOnGround, ChangedActive, ChangedNone;
        public readonly List<SpecialWeaponPhase> FazSirasi = new List<SpecialWeaponPhase>(16);

        public readonly Dictionary<CombatAudioEvent, int> Cue = new Dictionary<CombatAudioEvent, int>();
        public readonly Dictionary<CombatAudioEvent, int> LoopBaslat = new Dictionary<CombatAudioEvent, int>();
        public readonly Dictionary<CombatAudioEvent, int> LoopDurdur = new Dictionary<CombatAudioEvent, int>();

        public readonly List<EnemyDamageInfo> Damaged;
        public readonly Dictionary<int, int> DamagedByLife = new Dictionary<int, int>();
        public readonly List<EnemyDeathInfo> Died = new List<EnemyDeathInfo>();
        public int Dressed;

        private readonly Action<Vector3, float> _spawned;
        private readonly Action _groundExpired;
        private readonly Action<float> _pickedUp;
        private readonly Action<float> _finalSeconds;
        private readonly Action _ended;
        private readonly Action<SpecialWeaponHudState> _changed;
        private readonly Action<float> _scheduled;
        private readonly Action<SpecialWeaponCancelInfo> _cancelled;
        private readonly Action _cleared;
        private readonly Action<CombatAudioCue> _cue;
        private readonly Action<CombatAudioCue> _cueSuppressed;
        private readonly Action<CombatAudioEvent> _loopStopped;
        private readonly Action<EnemyDamageInfo> _damaged;
        private readonly Action<EnemyDeathInfo> _died;
        private readonly Action<EnemyHitFeedbackInfo, HitFeedbackTier> _dressed;

        public SpecialOlaySayaci(int kapasite = 32)
        {
            Damaged = new List<EnemyDamageInfo>(kapasite);
            _spawned = (p, d) => Spawned++;
            _groundExpired = () => GroundExpired++;
            _pickedUp = d => PickedUp++;
            _finalSeconds = r => FinalSeconds++;
            _ended = () => Ended++;
            _changed = s =>
            {
                FazSirasi.Add(s.Phase);
                switch (s.Phase)
                {
                    case SpecialWeaponPhase.OnGround: ChangedOnGround++; break;
                    case SpecialWeaponPhase.Active: ChangedActive++; break;
                    default: ChangedNone++; break;
                }
            };
            _scheduled = m => { Scheduled++; SonMark = m; };
            _cancelled = i => { Cancelled++; Iptaller.Add(i); };
            _cleared = () => Cleared++;
            _cue = c => Say(c.IsLoop ? LoopBaslat : Cue, c.Event);
            _cueSuppressed = c => Say(c.IsLoop ? LoopBaslat : Cue, c.Event);
            _loopStopped = e => Say(LoopDurdur, e);
            _damaged = i =>
            {
                Damaged.Add(i);
                DamagedByLife.TryGetValue(i.LifeId, out int n);
                DamagedByLife[i.LifeId] = n + 1;
            };
            _died = i => Died.Add(i);
            _dressed = (i, t) => Dressed++;

            WeaponEvents.SpecialWeaponSpawned += _spawned;
            WeaponEvents.SpecialWeaponGroundExpired += _groundExpired;
            WeaponEvents.SpecialWeaponPickedUp += _pickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds += _finalSeconds;
            WeaponEvents.SpecialWeaponEnded += _ended;
            WeaponEvents.SpecialChanged += _changed;
            WeaponEvents.SpecialWeaponScheduled += _scheduled;
            WeaponEvents.SpecialWeaponCancelled += _cancelled;
            WeaponEvents.SpecialWeaponCleared += _cleared;
            AudioEvents.Cue += _cue;
            AudioEvents.CueSuppressed += _cueSuppressed;
            AudioEvents.LoopStopped += _loopStopped;
            DamageEvents.EnemyDamaged += _damaged;
            DamageEvents.EnemyDied += _died;
            HitFeedbackDirector.EnemyHitDressed += _dressed;
        }

        public void Dispose()
        {
            WeaponEvents.SpecialWeaponSpawned -= _spawned;
            WeaponEvents.SpecialWeaponGroundExpired -= _groundExpired;
            WeaponEvents.SpecialWeaponPickedUp -= _pickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds -= _finalSeconds;
            WeaponEvents.SpecialWeaponEnded -= _ended;
            WeaponEvents.SpecialChanged -= _changed;
            WeaponEvents.SpecialWeaponScheduled -= _scheduled;
            WeaponEvents.SpecialWeaponCancelled -= _cancelled;
            WeaponEvents.SpecialWeaponCleared -= _cleared;
            AudioEvents.Cue -= _cue;
            AudioEvents.CueSuppressed -= _cueSuppressed;
            AudioEvents.LoopStopped -= _loopStopped;
            DamageEvents.EnemyDamaged -= _damaged;
            DamageEvents.EnemyDied -= _died;
            HitFeedbackDirector.EnemyHitDressed -= _dressed;
        }

        public int CueSayisi(CombatAudioEvent e) => Cue.TryGetValue(e, out int n) ? n : 0;
        public int LoopBaslatSayisi(CombatAudioEvent e) => LoopBaslat.TryGetValue(e, out int n) ? n : 0;
        public int LoopDurdurSayisi(CombatAudioEvent e) => LoopDurdur.TryGetValue(e, out int n) ? n : 0;
        public int DamagedFor(int lifeId) => DamagedByLife.TryGetValue(lifeId, out int n) ? n : 0;

        public string Ozet()
        {
            return "spawned=" + Spawned + " groundExpired=" + GroundExpired + " pickedUp=" + PickedUp + " finalSeconds=" + FinalSeconds +
                   " ended=" + Ended + " scheduled=" + Scheduled + " cancelled=" + Cancelled + " cleared=" + Cleared +
                   " changed(onGround/active/none)=" + ChangedOnGround + "/" + ChangedActive + "/" + ChangedNone;
        }

        private static void Say(Dictionary<CombatAudioEvent, int> d, CombatAudioEvent e)
        {
            d.TryGetValue(e, out int n);
            d[e] = n + 1;
        }
    }
}
