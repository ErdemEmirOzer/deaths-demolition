using System.IO;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · seçim hafızası sözleşmesi (DEC-037 / DEC-039), süreç sınırı simülasyonuyla.
    ///
    /// Oturum içi hafıza statik alanlardır; "uygulama yeniden açıldı" =
    /// RuntimeInitializeOnLoadMethod(SubsystemRegistration) ResetStatics zinciri. Bu test o
    /// zinciri reflection ile çağırır: seçimler varsayılana döner, kalıcı profil (diskten)
    /// korunur. Gerçek süreç sınırı kanıtı LifecycleProbe'dur (dev build); bu onun birim
    /// düzeyi nöbetçisidir.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class SelectionMemoryTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-editmode-wp005-selection-profile.json");

        [SetUp]
        public void SetUp()
        {
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
            ProfileService.ResetProfile();
        }

        [TearDown]
        public void TearDown()
        {
            Bosalt();
            ProfileStore.SetPathOverride(null);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            WeaponRegistry.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            if (File.Exists(TestPath))
            {
                File.Delete(TestPath);
            }
        }

        private static void Bosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private static void ResetStatics(System.Type tip)
        {
            MethodInfo m = tip.GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, tip.Name + ".ResetStatics bulunamadı — relaunch simülasyonu kırık");
            m.Invoke(null, null);
        }

        [Test]
        public void UcSkin_HepsiAcik_KozmetikVarsayilan()
        {
            Assert.AreEqual(3, PlayerSkins.Count);
            Assert.AreEqual(3, System.Enum.GetValues(typeof(PlayerSkin)).Length);
            Assert.AreEqual(PlayerSkin.IvoryCrimson, PlayerSkins.Default);

            for (int i = 0; i < PlayerSkins.Count; i++)
            {
                PlayerSkins.Palette p = PlayerSkins.Get((PlayerSkin)i);
                Assert.IsFalse(string.IsNullOrEmpty(p.DisplayName), "skin " + i + " adı boş");
            }

            // Skin'in kilit/fiyat/rarity kavramı yok: palette yalnız ad ve üç renk taşır.
            FieldInfo[] alanlar = typeof(PlayerSkins.Palette).GetFields(BindingFlags.Public | BindingFlags.Instance);
            CollectionAssert.AreEquivalent(new[] { "DisplayName", "Primary", "Secondary", "Accent" },
                System.Array.ConvertAll(alanlar, f => f.Name));
        }

        [Test]
        public void SecimStatikleri_TekSahip()
        {
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AutoSmg;
            Assert.AreEqual(ClassicWeaponId.AutoSmg, WeaponRegistry.SelectedWeapon, "flow, registry'ye yönlendirir");
            WeaponRegistry.SelectedWeapon = ClassicWeaponId.CombatShotgun;
            Assert.AreEqual(ClassicWeaponId.CombatShotgun, PrototypeUiFlow.SelectedWeapon);

            PrototypeUiFlow.SelectedSkin = PlayerSkin.MossCream;
            Assert.AreEqual(PlayerSkin.MossCream, PrototypeUiFlow.SelectedSkin, "oturum içinde hatırlanır");
        }

        [Test]
        public void RelaunchSimulasyonu_VarsayilanaDoner_ProfilKalir()
        {
            // Profili L2'ye çıkar (AutoSmg açılır), sonra seçim yap.
            ProfileService.ApplyRun(new RunSummary
            {
                ReachedRound = 2, CompletedRounds = 1, CompletedRoundXp = 100, TotalXpEarned = 100,
                TotalKills = 3, StandardKills = 3, KillXp = 3, EndReason = RunEndReason.PlayerDeath
            });
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg));

            PrototypeUiFlow.SelectedSkin = PlayerSkin.MossCream;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AutoSmg;
            Assert.IsNotNull(ProfileService.LastReport);

            // "Uygulama kapandı ve yeniden açıldı": statikler sıfırlanır, disk kalır.
            ResetStatics(typeof(PrototypeUiFlow));
            ResetStatics(typeof(WeaponRegistry));
            ResetStatics(typeof(ProfileService));
            ResetStatics(typeof(ProfileStore));
            ResetStatics(typeof(UiNavigation));

            // ProfileStore.ResetStatics override'ı da siler (gerçek süreçte override zaten yoktur);
            // test dosyasına geri yönlendir — gerçek profile dokunulmaz.
            ProfileStore.SetPathOverride(TestPath);

            Assert.AreEqual(PlayerSkins.Default, PrototypeUiFlow.SelectedSkin, "DEC-039: skin varsayılana döner");
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, PrototypeUiFlow.SelectedWeapon, "DEC-039: silah varsayılana döner");
            Assert.IsNull(ProfileService.LastReport, "önceki koşu raporu taşınmaz");
            Assert.AreEqual(UiContext.None, UiNavigation.Base, "navigation temiz başlar");

            ProfileData yeniden = ProfileService.Current;
            Assert.AreEqual(2, yeniden.ProfileLevel, "kalıcı profil diskten geri gelir");
            Assert.AreEqual(3, yeniden.TotalKills);
            Assert.AreEqual(2, yeniden.HighestRound);
            Assert.AreEqual(1, yeniden.RunsPlayed);
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "unlock kalıcıdır");
            Assert.IsTrue(yeniden.Achievements.Contains("first_blood"), "başarım kalıcıdır");

            Debug.Log("[DD-TEST] wp005.relaunch skin=" + PrototypeUiFlow.SelectedSkin + " weapon=" +
                      PrototypeUiFlow.SelectedWeapon + " level=" + yeniden.ProfileLevel);
        }

        [Test]
        public void SkinDegisimi_ProfilVeSilahiEtkilemez()
        {
            string once = File.ReadAllText(TestPath);
            ClassicWeaponId silah = PrototypeUiFlow.SelectedWeapon;

            PrototypeUiFlow.SelectedSkin = PlayerSkin.IndigoBlack;
            PrototypeUiFlow.SelectedSkin = PlayerSkin.MossCream;

            Assert.AreEqual(silah, PrototypeUiFlow.SelectedWeapon);
            Assert.AreEqual(once, File.ReadAllText(TestPath), "skin seçimi profil dosyasına yazılmaz");
        }
    }
}
