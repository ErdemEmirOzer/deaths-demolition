using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · ortak yaşam döngüsü ölçüm araçları (yalnız test assembly'si).
    ///
    /// Statik event abone sayıları derleyicinin ürettiği backing field üzerinden okunur
    /// (field-like event: alan adı = event adı). Alan bulunamazsa test yalancı yeşil
    /// olamaz: ölçüm kırık demektir ve FAIL verilir.
    /// </summary>
    internal static class LifecycleTestKit
    {
        public const float SahneZamanAsimi = 20f;
        public const float FazZamanAsimi = 25f;

        /// <summary>Zincir boyunca izlenen statik event'ler (tip, event adı).</summary>
        public static readonly (Type Tip, string Ad)[] IzlenenEventler =
        {
            (typeof(RunEvents), "RunStarted"),
            (typeof(RunEvents), "RoundStarted"),
            (typeof(RunEvents), "RoundCompleted"),
            (typeof(RunEvents), "RoundPoolExhausted"),
            (typeof(RunEvents), "GoldChanged"),
            (typeof(RunEvents), "PhaseChanged"),
            (typeof(RunEvents), "CountdownTick"),
            (typeof(RunEvents), "RunEnded"),
            (typeof(RunFlowClock), "FrozenChanged"),
            (typeof(ProfileService), "Changed"),
            (typeof(InputSourceRegistry), "CurrentChanged"),
            (typeof(WeaponRegistry), "ActiveChanged"),
            (typeof(PlayerHealth), "AnyPlayerDied"),
            (typeof(PlayerHealth), "AnyHealthChanged"),
            (typeof(PlayerHealth), "AnyDamaged"),
            // WP-006: damage/armour event bus and the single feedback owner's hook, so a
            // subscription that multiplies across runs is measured here too.
            (typeof(DD.Prototype.Damage.DamageEvents), "ArmorBroken"),
            (typeof(DD.Prototype.Damage.DamageEvents), "EnemyDamaged"),
            (typeof(DD.Prototype.Damage.DamageEvents), "EnemyDied"),
            (typeof(DD.Prototype.Damage.DamageEvents), "PlayerAttacked"),
            (typeof(DD.Prototype.Feedback.HitFeedbackDirector), "EnemyHitDressed"),
            // WP-007: weapon / Special Weapon event bus and the round bridge, so a Special
            // Weapon subscription that multiplies across runs (director, HUD) is measured too.
            (typeof(DD.Prototype.Weapons.WeaponEvents), "WeaponEquipped"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "AmmoChanged"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "ReloadStarted"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "ReloadCompleted"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "ReloadInterrupted"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "DryFire"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "ClassicChanged"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponSpawned"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponGroundExpired"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponPickedUp"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponFinalSeconds"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponEnded"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialChanged"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponScheduled"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponCancelled"),
            (typeof(DD.Prototype.Weapons.WeaponEvents), "SpecialWeaponCleared"),
            (typeof(DD.Prototype.Weapons.SpecialWeaponRoundBridge), "RoundStarted"),
            (typeof(DD.Prototype.Weapons.SpecialWeaponRoundBridge), "RoundEnded"),
            (typeof(DD.Prototype.Weapons.SpecialWeaponRoundBridge), "RunStarted"),
            (typeof(DD.Prototype.Weapons.SpecialWeaponRoundBridge), "RunEnded"),
        };

        public static int AboneSayisi(Type tip, string eventAdi)
        {
            FieldInfo alan = tip.GetField(eventAdi,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(alan, tip.Name + "." + eventAdi +
                " backing field bulunamadı — abone ölçümü kırık; test yalancı yeşil olamaz.");
            var d = alan.GetValue(null) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        /// <summary>
        /// WP-059: bir statik event'in abonelerinin hedef tipleri ("Tip" ya da statik/lambda için
        /// "Tip::Metot"). Bilinen süreç-boyu tüketiciler beyaz listesiyle karşılaştırılır — liste dışı
        /// her abone bir sızıntıdır.
        /// </summary>
        public static List<string> AboneHedefleri(Type tip, string eventAdi)
        {
            FieldInfo alan = tip.GetField(eventAdi, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(alan, tip.Name + "." + eventAdi + " backing field bulunamadı.");
            var sonuc = new List<string>();
            var d = alan.GetValue(null) as Delegate;
            if (d == null) return sonuc;
            foreach (Delegate h in d.GetInvocationList())
            {
                Type sahip = h.Target != null ? h.Target.GetType() : h.Method.DeclaringType;
                // A lambda / closure lives in a compiler class nested under its owner.
                while (sahip != null && sahip.IsNested && sahip.Name.StartsWith("<")) sahip = sahip.DeclaringType;
                sonuc.Add(sahip != null ? sahip.Name : "?");
            }

            return sonuc;
        }

        /// <summary>Tüm izlenen event'lerin abone sayıları; anahtar "Tip.Event".</summary>
        public static Dictionary<string, int> AboneAnlikGoruntu()
        {
            var sonuc = new Dictionary<string, int>();
            for (int i = 0; i < IzlenenEventler.Length; i++)
            {
                (Type tip, string ad) = IzlenenEventler[i];
                sonuc[tip.Name + "." + ad] = AboneSayisi(tip, ad);
            }

            return sonuc;
        }

        public static string Yazdir(string etiket, Dictionary<string, int> g)
        {
            var sb = new StringBuilder("[DD-TEST] subscribers." + etiket + " ");
            foreach (KeyValuePair<string, int> kv in g)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            }

            string s = sb.ToString();
            Debug.Log(s);
            return s;
        }

        /// <summary>a − b farkını döndürür (her anahtar için).</summary>
        public static Dictionary<string, int> Fark(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            var f = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> kv in a)
            {
                f[kv.Key] = kv.Value - (b.TryGetValue(kv.Key, out int v) ? v : 0);
            }

            return f;
        }

        public static void AboneleriKarsilastir(string aciklama, Dictionary<string, int> beklenen, Dictionary<string, int> gozlenen)
        {
            var hatalar = new List<string>();
            foreach (KeyValuePair<string, int> kv in beklenen)
            {
                int g = gozlenen.TryGetValue(kv.Key, out int v) ? v : -1;
                if (g != kv.Value)
                {
                    hatalar.Add(kv.Key + ": beklenen " + kv.Value + ", gözlenen " + g);
                }
            }

            Assert.IsTrue(hatalar.Count == 0,
                aciklama + " — abone sayıları eşleşmiyor (birikme/sızıntı):\n  " + string.Join("\n  ", hatalar));
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

        public static IEnumerator SahneyiBekle(string ad)
        {
            yield return Bekle(
                () => SceneManager.GetActiveScene().name == ad && SceneManager.GetActiveScene().isLoaded,
                SahneZamanAsimi,
                "'" + ad + "' sahnesi etkin hale gelmedi. Şu an: '" + SceneManager.GetActiveScene().name + "'");
        }

        /// <summary>Boot'u yükler ve Main Menu'yü bekler.</summary>
        public static IEnumerator BootTanMainMenuye()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
        }

        /// <summary>Run sahnesi yüklendikten sonra koşunun Fighting fazına girmesini bekler.</summary>
        public static IEnumerator KosuHazirOlsun()
        {
            yield return Bekle(() => RunDirector.Instance != null, FazZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, FazZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, FazZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, FazZamanAsimi,
                "Koşu Fighting fazına girmedi.");
        }

        /// <summary>Simülasyonun çözülmüş olduğunu iki bağımsız ölçümle doğrular.</summary>
        public static IEnumerator SimulasyonSerbestOlmali(string nokta)
        {
            Assert.AreEqual(0, RunFlowClock.HoldCount, nokta + ": hold kaldı (" + RunFlowClock.DescribeHolds() + ")");
            Assert.IsFalse(RunFlowClock.IsFrozen, nokta + ": IsFrozen true");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, nokta + ": timeScale 1 değil");
            yield return null;
            Assert.Greater(Time.deltaTime, 0f, nokta + ": deltaTime 0 — dünya donmuş");
            Debug.Log("[DD-TEST] clock." + nokta + " holds=0 timeScale=" + Time.timeScale + " dt=" + Time.deltaTime);
        }

        public static int AktifSayi<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindObjectsOfType<T>().Length;
        }
    }
}
