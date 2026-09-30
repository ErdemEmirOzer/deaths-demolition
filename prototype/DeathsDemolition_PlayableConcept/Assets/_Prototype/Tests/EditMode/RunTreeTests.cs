using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 B1 · koşu içi gelişim ağacı (DEC-095): katalog, iki şeritli ızgara, yedi durum kuralı,
    /// dışlama, doruk kuralı, Ocak (kart dışı, 50+30n, ≤3/round, ≤20), Kese (+%10 tur sonu), Nişan yolu
    /// ve teklif garantisi, S-09 anlık görüntüsü, yeni dallar (ArmorBreak/Impact/Cluster) çekilişe girmez.
    /// Etkisi henüz bağlanmamış düğüm KİLİTLİ (EffectPending) kalır; kural testleri TreatAllAsLive ile koşar.
    /// </summary>
    [TestFixture]
    public sealed class RunTreeTests
    {
        private PrototypeConfig _config;
        private GoldWallet _cuzdan;
        private WeaponModifierState _rank;
        private RoundUpgradeShop _dukkan;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _cuzdan = new GoldWallet();
            _cuzdan.InitializeForRun(1);
            _rank = new WeaponModifierState();
            _dukkan = new RoundUpgradeShop
            {
                UnlockedPredicate = _ => true,
                Random = new SeededUpgradeOfferRandom(77),
                WeaponProvider = () => ClassicWeaponId.AssaultRifle
            };
            RunTreeEffects.TreatAllAsLive = false;
            RunTreeCatalog.TestSeam.Override(null);
        }

        [TearDown]
        public void TearDown()
        {
            RunTreeEffects.TreatAllAsLive = false;
            RunTreeCatalog.TestSeam.Override(null);
            Object.DestroyImmediate(_config);
        }

        private void Altin(int n) => _cuzdan.Credit(n, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());

        private RunTreeState Agac(ClassicWeaponId silah = ClassicWeaponId.AssaultRifle)
        {
            var t = new RunTreeState();
            t.Begin(1, silah, _config);
            return t;
        }

        private static int I(RunTreeState t, string id)
        {
            int i = t.Graph.IndexOf(id);
            Assert.GreaterOrEqual(i, 0, "düğüm yok: " + id);
            return i;
        }

        private RunTreeNodeStatus Durum(RunTreeState t, string id, int gold = 100000) =>
            t.Status(I(t, id), _rank, gold, out _, out _, out _);

        [Test]
        public void Katalog_Gecerli_OnSilah_KisaAd12()
        {
            RunTreeCatalogDocument doc = RunTreeCatalog.Document;
            List<string> hatalar = RunTreeCatalog.Validate(doc);
            Assert.IsEmpty(hatalar, string.Join("; ", hatalar));
            Assert.AreEqual(10, doc.weapons.Length, "WP-069: 10 silah yuvası");
            Assert.AreEqual(13, doc.branchKeystones.Length, "13 dalın her birine tek keystone");
        }

        [Test]
        public void YeniDallar_EnumSozlesme_CekiliseGirmez()
        {
            Assert.AreEqual(10, (int)UpgradeBranch.ArmorBreak);
            Assert.AreEqual(11, (int)UpgradeBranch.Impact);
            Assert.AreEqual(12, (int)UpgradeBranch.Cluster);
            Assert.AreEqual(9, (int)UpgradeBranch.RegenerationRate, "0–9 yer değiştirmez");
            Assert.AreEqual(13, WeaponModifierState.BranchCount);
            foreach (UpgradeBranch b in new[] { UpgradeBranch.ArmorBreak, UpgradeBranch.Impact, UpgradeBranch.Cluster })
            {
                Assert.IsTrue(WeaponBranchContract.IsWeaponCore(b), b + " silah çekirdeği");
                Assert.IsNotNull(_config.Economy.Find(b), b + " fiyat verisi");
                Assert.IsFalse(UpgradeCardCatalog.HasLiveConsumer(b), b + ": davranış kancası gelene kadar çekilişte yok");
            }

            for (int seed = 0; seed < 40; seed++)
            {
                var dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(seed), WeaponProvider = () => ClassicWeaponId.AssaultRifle };
                dukkan.Open(1, _config, _cuzdan, new WeaponModifierState(), 100 + seed);
                foreach (UpgradeCardOffer k in dukkan.Cards)
                {
                    Assert.Less((int)k.Branch, 10, "yeni dal kartı çekilmedi (seed " + seed + ")");
                }
            }
        }

        [Test]
        public void Izgara_IkiSerit_4x4_BenzersizHucre_CekirdekDallarRoster()
        {
            foreach (ClassicWeaponId silah in new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun })
            {
                RunTreeState t = Agac(silah);
                var hucre = new HashSet<string>();
                int silahSeridi = 0, beden = 0;
                foreach (RunTreeNode n in t.Graph.Nodes)
                {
                    Assert.That(n.Row, Is.InRange(0, RunTreeGraph.LaneRows - 1), n.Id);
                    Assert.That(n.Column, Is.InRange(0, RunTreeGraph.LaneColumns - 1), n.Id);
                    Assert.IsTrue(hucre.Add(n.Lane + ":" + n.Row + ":" + n.Column), "hücre çakışması " + n.Id);
                    Assert.LessOrEqual(n.ShortName.Length, RunTreeCatalog.ShortNameMax, n.Id);
                    if (n.Lane == RunTreeLane.Weapon) silahSeridi++; else beden++;
                }

                Assert.AreEqual(13, silahSeridi, silah + " silah şeridi");
                Assert.AreEqual(12, beden, silah + " beden şeridi");
                Assert.LessOrEqual(silahSeridi, 16, "S-09: şerit başına ≤ 16 düğüm");
                foreach (UpgradeBranch cekirdek in RunTreeGraph.CoreBranchesOf(silah))
                {
                    int i = I(t, RunTreeGraph.TierId(cekirdek));
                    Assert.AreEqual(RunTreeLane.Weapon, t.Graph[i].Lane);
                }

                Assert.AreEqual("seal_" + RunTreeCatalog.SlotFor(silah), t.Graph[t.Graph.WeaponSealIndex].Id, "WP-069 silah mührü");
            }
        }

        [Test]
        public void Doruk_M1Oncesi_YalnizPinleTeklife_Girer()
        {
            // DEC-098 K6: before mastery M1 an unpinned capstone is never offered; the pin brings it once its prerequisites are owned.
            RunTreeEffects.TreatAllAsLive = true;
            MetaRunModifiers meta = MetaRunModifiers.None;
            meta.CapstoneMasteryMet = false;
            var t = new RunTreeState();
            t.Begin(1, ClassicWeaponId.AssaultRifle, _config, meta);
            int cap = t.Graph.WeaponCapstoneIndex;
            foreach (UpgradeBranch b in RunTreeGraph.CoreBranchesOf(ClassicWeaponId.AssaultRifle)) _rank.SetTier(b, 2);
            for (int i = 0; i < t.Graph.Count; i++)
            {
                RunTreeNode n = t.Graph[i];
                if (n.Lane == RunTreeLane.Weapon && (n.Kind == RunTreeNodeKind.BranchKeystone || n.Kind == RunTreeNodeKind.WeaponSeal)) t.MarkOwned(i);
            }

            t.MarkOwned(I(t, "fork_ar_1_x"));
            Assert.IsTrue(t.PrereqMet(cap, _rank, out _), "ön koşullar tam");
            Assert.IsFalse(t.IsOfferable(cap, _rank), "M1 öncesi pinsiz teklif yok");
            t.SetPin(cap);
            Assert.IsTrue(t.IsOfferable(cap, _rank), "pin doruğu getirir");
            var kartlar = new List<int>();
            t.DrawFrontier(new SeededUpgradeOfferRandom(5), _rank, 3, kartlar);
            Assert.Contains(cap, kartlar, "Nişan garantisi çekilişte");
        }

        [Test]
        public void KademeKisaAdlari_Benzersiz_TamKelime_OnIkiAlti()
        {
            var adlar = new HashSet<string>();
            foreach (UpgradeBranch b in System.Enum.GetValues(typeof(UpgradeBranch)))
            {
                string kisa = RunTreeGraph.TierShortName(b, "ar_1", null);
                Assert.LessOrEqual(kisa.Length, RunTreeCatalog.ShortNameMax, b.ToString());
                Assert.IsTrue(adlar.Add(kisa), "tekrar eden kısa ad: " + kisa);
                foreach (string kelime in kisa.Split(' ')) Assert.GreaterOrEqual(kelime.Length, 3, "kesik kelime: " + kisa);
            }

            Assert.AreEqual("Blast", RunTreeGraph.TierShortName(UpgradeBranch.Explosion, "rl_1", null));
            RunTreeState t = Agac();
            Assert.AreEqual("Move Speed", t.Graph[t.Graph.IndexOf(RunTreeGraph.TierId(UpgradeBranch.MovementSpeed))].ShortName);
        }

        [Test]
        public void AnlikGoruntu_SatinAlinanKart_TekliftenDuser()
        {
            _dukkan.Open(1, _config, _cuzdan, _rank, 9);
            Altin(5000);
            int alinan = -1;
            for (int c = 0; c < _dukkan.Cards.Count && alinan < 0; c++) if (_dukkan.TryPurchase(c)) alinan = c;
            Assert.GreaterOrEqual(alinan, 0);
            var s = new RunTreeSnapshot();
            s.Refresh(_dukkan);
            string id = RunTreeGraph.TierId(_dukkan.Cards[alinan].Branch);
            int i = s.IndexOf(id);
            if (i >= 0) Assert.IsFalse(s.Nodes[i].InOffer, id + " satın alındı → teklif dışında");
        }

        [Test]
        public void IcerikPaketi_HerDugumunAnahtari_Var()
        {
            // planning/content/r1-strings.json (R1 içerik paketi): her düğümün "<anahtar>.name|.desc" çifti bulunur.
            string yol = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../planning/content/r1-strings.json"));
            string paket = System.IO.File.Exists(yol) ? System.IO.File.ReadAllText(yol) : null;
            var eksik = new List<string>();
            void Bak(string sahip, string anahtar)
            {
                if (string.IsNullOrEmpty(anahtar)) { eksik.Add(sahip + ": anahtar yok"); return; }
                if (paket != null && (!paket.Contains("\"" + anahtar + ".name\"") || !paket.Contains("\"" + anahtar + ".desc\""))) eksik.Add(sahip + " → " + anahtar);
            }

            foreach (ClassicWeaponId silah in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                foreach (string ozel in new[] { "sw_arc", "sw_gel" })
                {
                    RunTreeState t = Agac(silah);
                    t.OnSpecialPickedUp(ozel);
                    for (int i = 0; i < t.Graph.Count; i++) Bak(silah + "/" + t.Graph[i].Id, t.StringKeyOf(i));
                }
            }

            foreach (DD.Prototype.Profile.MetaPathNode n in DD.Prototype.Profile.MetaPathCatalog.Nodes) Bak(n.Id, n.StringKey);
            Assert.IsEmpty(eksik, string.Join("\n", eksik));
        }

        [Test]
        public void OzelBag_FiyatiVeAgirligi_KatalogdanGelir()
        {
            // WP-077 B5: the link slots are generic nodes (FlatCost 0); before the fix a Special link card cost 1 gold.
            foreach (string ozel in new[] { "sw_arc", "sw_gel" })
            {
                RunTreeState t = Agac();
                t.OnSpecialPickedUp(ozel);
                RunTreeCatalogNode b1 = RunTreeCatalog.FindLink(ozel, 1), b2 = RunTreeCatalog.FindLink(ozel, 2);
                Assert.IsNotNull(b1, ozel + " bağ I");
                Assert.IsNotNull(b2, ozel + " bağ II");
                Assert.AreEqual(b1.cost, t.Cost(t.Graph.LinkIIndex, _rank), ozel + " bağ I fiyatı katalogdan");
                Assert.AreEqual(b2.cost, t.Cost(t.Graph.LinkIIIndex, _rank), ozel + " bağ II fiyatı katalogdan");
                Assert.Greater(b1.cost, 1, "katalog fiyatı 1 değil");
            }
        }

        [Test]
        public void EtkisiBaglanmamis_Kilitli_Satilmaz_Teklife_Girmez()
        {
            // K10b: every shipped node is wired, so the rule is proven on a catalog copy whose ar_1 seal names an unwired effect.
            RunTreeCatalogDocument kopya = JsonUtility.FromJson<RunTreeCatalogDocument>(JsonUtility.ToJson(RunTreeCatalog.Document));
            foreach (RunTreeCatalogWeapon w in kopya.weapons) if (w.slot == "ar_1") w.seal.effectId = "test.unwired";
            RunTreeCatalog.TestSeam.Override(kopya);
            RunTreeState t = Agac();
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            int muhur = I(t, "seal_ar_1");
            Assert.AreEqual(RunTreeNodeStatus.Locked, t.Status(muhur, _rank, 9999, out RunTreeLockReason neden, out _, out _));
            Assert.AreEqual(RunTreeLockReason.EffectPending, neden, "davranış kancası yok → dürüst kilit");
            Assert.IsFalse(t.IsOfferable(muhur, _rank));
            Assert.IsTrue(t.IsOfferable(I(t, "ks_firerate"), _rank), "K10b: Isınma keystone'u canlı → satılır");
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum(t, "link_1"), "Special alınmadan bağ kilitli");
            Assert.AreEqual(RunTreeNodeStatus.Available, Durum(t, "purse"), "Kese canlı (B1)");
        }

        [Test]
        public void Keystone_Kavsak_Dislama_Doruk_Kurallari()
        {
            RunTreeEffects.TreatAllAsLive = true;
            RunTreeState t = Agac();

            // Keystone: bağlı dal tier ≥ 2.
            int ks = I(t, "ks_firerate");
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, t.Status(ks, _rank, 9999, out _, out int eksik, out _));
            Assert.AreEqual(I(t, "tier_FireRate"), eksik, "NEEDS: kendi dalı");
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            Assert.AreEqual(RunTreeNodeStatus.Available, t.Status(ks, _rank, 9999, out _, out _, out _));
            Assert.AreEqual(RunTreeNodeStatus.Unaffordable, t.Status(ks, _rank, 50, out _, out _, out _), "110 > 50");

            // Kavşak: şeritte ≥ 1 keystone; X alınınca Y DIŞLANMIŞ.
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, Durum(t, "fork_ar_1_x"));
            Assert.IsTrue(t.MarkOwned(ks));
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum(t, "ks_firerate"));
            Assert.AreEqual(RunTreeNodeStatus.Available, Durum(t, "fork_ar_1_x"));
            Assert.IsTrue(t.MarkOwned(I(t, "fork_ar_1_x")));
            Assert.AreEqual(RunTreeNodeStatus.Excluded, t.Status(I(t, "fork_ar_1_y"), _rank, 9999, out _, out _, out int kapatan));
            Assert.AreEqual(I(t, "fork_ar_1_x"), kapatan, "CLOSED BY X");
            Assert.IsFalse(t.IsOfferable(I(t, "fork_ar_1_y"), _rank));

            // Doruk: mühür + 2 keystone + 1 kavşak.
            int doruk = I(t, "cap_ar_1");
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, t.Status(doruk, _rank, 9999, out _, out int eksik2, out _));
            Assert.AreEqual(t.Graph.WeaponSealIndex, eksik2, "önce silah mührü");
            Assert.IsTrue(t.MarkOwned(t.Graph.WeaponSealIndex));
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, Durum(t, "cap_ar_1"), "ikinci keystone eksik");
            _rank.SetTier(UpgradeBranch.Piercing, 2);
            Assert.IsTrue(t.MarkOwned(I(t, "ks_piercing")));
            Assert.AreEqual(RunTreeNodeStatus.Available, Durum(t, "cap_ar_1"));
            Assert.AreEqual(380, t.Cost(doruk, _rank));

            // Beden şeridi bağımsız: silah keystone'ları beden kavşağını açmaz.
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, Durum(t, "fork_body_x"));
        }

        [Test]
        public void Ocak_ModifierYoksa_IslemVeYanEtkiYok()
        {
            RunTreeState t = Agac();
            Assert.IsTrue(t.MarkOwned(I(t, "ks_firerate")));
            Altin(500);
            int balance = _cuzdan.Balance, spent = _cuzdan.TotalSpent;
            int earned = _cuzdan.TotalEarned, applied = _cuzdan.AppliedTransactions;
            int previousTx = _cuzdan.NextTransactionId();
            int changes = 0, snapshots = 0;
            _cuzdan.Changed += _ => changes++;
            _cuzdan.SnapshotChanged += _ => snapshots++;

            Assert.AreEqual(RunTreeCommandResult.NotAvailable, t.TryStokeHearth(_cuzdan, null, out int tx));
            Assert.AreEqual(0, tx);
            Assert.AreEqual(balance, _cuzdan.Balance);
            Assert.AreEqual(spent, _cuzdan.TotalSpent);
            Assert.AreEqual(earned, _cuzdan.TotalEarned);
            Assert.AreEqual(applied, _cuzdan.AppliedTransactions);
            Assert.AreEqual(previousTx + 1, _cuzdan.NextTransactionId(), "reddedilen komut işlem kimliği ayırmaz");
            Assert.AreEqual(0, t.HearthStacks);
            Assert.AreEqual(0, t.HearthThisRound);
            Assert.AreEqual(-1, t.PinnedIndex);
            Assert.AreEqual(0, changes);
            Assert.AreEqual(0, snapshots);

            // The same valid prerequisite and wallet still buy the unchanged 50-gold effect with a build.
            Assert.AreEqual(RunTreeCommandResult.Done, t.TryStokeHearth(_cuzdan, _rank));
            Assert.AreEqual(balance - 50, _cuzdan.Balance);
            Assert.AreEqual(1, _rank.HearthStacks);
        }

        [Test]
        public void Ocak_KartDisi_Fiyat_TurSiniri_Tavan_Hasar()
        {
            RunTreeState t = Agac();
            Altin(8000);   // 20 ocak = Σ(50 + 30n) = 6 700
            Assert.AreEqual(RunTreeCommandResult.NotAvailable, t.TryStokeHearth(_cuzdan, _rank), "önce bir silah keystone'u");

            RunTreeEffects.TreatAllAsLive = true;
            _rank.SetTier(UpgradeBranch.Piercing, 2);
            t.MarkOwned(I(t, "ks_piercing"));
            RunTreeEffects.TreatAllAsLive = false;   // Ocak kendisi canlı; keystone sahipliği yeter

            int once = _cuzdan.Balance;
            Assert.AreEqual(RunTreeCommandResult.Done, t.TryStokeHearth(_cuzdan, _rank));
            Assert.AreEqual(RunTreeCommandResult.Done, t.TryStokeHearth(_cuzdan, _rank));
            Assert.AreEqual(RunTreeCommandResult.Done, t.TryStokeHearth(_cuzdan, _rank));
            Assert.AreEqual(once - (50 + 80 + 110), _cuzdan.Balance, "50 + 30·n");
            Assert.AreEqual(RunTreeCommandResult.LimitReached, t.TryStokeHearth(_cuzdan, _rank), "round başına ≤ 3");
            Assert.AreEqual(3, _rank.HearthStacks);
            Assert.AreEqual(1.06f, _rank.DamageMultiplier, 1e-5f, "+%2 / ocak");
            Assert.IsFalse(t.IsOfferable(t.Graph.HearthIndex, _rank), "Ocak kart değildir");

            for (int round = 0; round < 10 && t.HearthStacks < 20; round++)
            {
                t.OnRoundEnd(_cuzdan);
                for (int k = 0; k < 3 && t.HearthStacks < 20; k++) Assert.AreEqual(RunTreeCommandResult.Done, t.TryStokeHearth(_cuzdan, _rank));
            }

            t.OnRoundEnd(_cuzdan);
            Assert.AreEqual(20, t.HearthStacks);
            Assert.AreEqual(RunTreeCommandResult.LimitReached, t.TryStokeHearth(_cuzdan, _rank), "koşu tavanı 20");
            Assert.AreEqual(RunTreeNodeStatus.MaxTier, t.Status(t.Graph.HearthIndex, _rank, 99999, out _, out _, out _));
            _rank.ResetForNewRun();
            Assert.AreEqual(1f, _rank.DamageMultiplier, 1e-6f, "yeni koşu sıfırlar");
        }

        [Test]
        public void Kese_TurSonuYuzde10_YalnizSahipken()
        {
            RunTreeState t = Agac();
            Altin(100);
            Assert.AreEqual(0, t.OnRoundEnd(_cuzdan), "Kese yokken bonus yok");
            Assert.IsTrue(t.MarkOwned(t.Graph.PurseIndex));
            Altin(55);
            int once = _cuzdan.Balance;
            Assert.AreEqual(5, t.OnRoundEnd(_cuzdan), "floor(55 × 0,10)");
            Assert.AreEqual(once + 5, _cuzdan.Balance);
            Assert.AreEqual(0, t.OnRoundEnd(_cuzdan), "bonus kendisi yeni bonus doğurmaz");
            Altin(200);
            Assert.AreEqual(20, t.OnRoundEnd(_cuzdan));
        }

        [Test]
        public void Nisan_Yol_VeTeklifGarantisi()
        {
            RunTreeEffects.TreatAllAsLive = true;
            var yol = new List<int>();
            for (int seed = 0; seed < 30; seed++)
            {
                var dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(seed), WeaponProvider = () => ClassicWeaponId.AssaultRifle };
                var rank = new WeaponModifierState();
                dukkan.Open(1, _config, _cuzdan, rank, 500 + seed);
                Assert.IsTrue(dukkan.SetPin("ks_reload"));
                int hedef = dukkan.Tree.Graph.IndexOf("ks_reload");
                Assert.AreEqual(2, dukkan.Tree.PathTo(hedef, rank, yol), "yol: dal kademesi → keystone");
                Assert.AreEqual(dukkan.Tree.Graph.IndexOf("tier_ReloadSpeed"), yol[0]);
                Assert.AreEqual(hedef, yol[1]);
                Assert.AreEqual(yol[0], dukkan.Tree.PinnedOfferable(rank), "şimdi çekilebilir adım: dal kademesi");

                dukkan.Close();
                dukkan.Open(2, _config, _cuzdan, rank, 500 + seed);
                bool var = false;
                foreach (UpgradeCardOffer k in dukkan.Cards) var |= k.Branch == UpgradeBranch.ReloadSpeed;
                Assert.IsTrue(var, "Nişan: yol üstündeki ilk adım sonraki teklifte (seed " + seed + ")");
                Assert.AreEqual(3, dukkan.Cards.Count, "teklif yine üç kart");
            }

            // Doruk yolu: mühür + 2 keystone (+ dal kademeleri) + 1 kavşak + doruk.
            RunTreeState t = Agac();
            int dorukYol = t.PathTo(I(t, "cap_ar_1"), _rank, yol);
            Assert.AreEqual(I(t, "cap_ar_1"), yol[dorukYol - 1], "yol hedefle biter");
            int keystone = 0, mühür = 0, kavsak = 0;
            foreach (int i in yol)
            {
                if (t.Graph[i].Kind == RunTreeNodeKind.BranchKeystone) keystone++;
                if (t.Graph[i].Kind == RunTreeNodeKind.WeaponSeal) mühür++;
                if (t.Graph[i].Kind == RunTreeNodeKind.Fork) kavsak++;
            }

            Assert.AreEqual(2, keystone); Assert.AreEqual(1, mühür); Assert.AreEqual(1, kavsak);
        }

        [Test]
        public void Dukkan_AgaciKosuBasinaKurar_KeseyiOder_OcakKomutu()
        {
            RunTreeEffects.TreatAllAsLive = true;
            Altin(1000);
            _dukkan.Open(1, _config, _cuzdan, _rank, 9);
            Assert.IsTrue(_dukkan.Tree.IsBuilt);
            Assert.AreEqual(9, _dukkan.Tree.RunId);
            Assert.AreEqual(0, _dukkan.PurseBonusThisRound);
            _dukkan.Tree.MarkOwned(_dukkan.Tree.Graph.PurseIndex);
            _rank.SetTier(UpgradeBranch.Piercing, 2);
            _dukkan.Tree.MarkOwned(_dukkan.Tree.Graph.IndexOf("ks_piercing"));

            // Aynı round yeniden açılış: ağaç ve bonus tekrar etmez.
            _dukkan.Close();
            _dukkan.Open(1, _config, _cuzdan, _rank, 9);
            Assert.AreEqual(0, _dukkan.PurseBonusThisRound);
            Assert.IsTrue(_dukkan.Tree.IsOwned(_dukkan.Tree.Graph.PurseIndex), "aynı koşu: sahiplik korunur");

            Assert.AreEqual(RunTreeCommandResult.NotAvailable, _dukkan.TryStokeHearth(_dukkan.SessionId, _dukkan.Revision - 1), "bayat revizyon");
            int rev = _dukkan.Revision;
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryStokeHearth(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(rev + 1, _dukkan.Revision);
            Assert.AreEqual(50, _dukkan.GoldSpentThisRound);
            Assert.AreEqual(0, _dukkan.PurchasesThisRound, "Ocak kart limitini (DEC-087) kullanmaz");

            // Sonraki round: Kese öder.
            _dukkan.Close();
            Altin(100);
            _dukkan.Open(2, _config, _cuzdan, _rank, 9);
            Assert.AreEqual(10, _dukkan.PurseBonusThisRound);

            // Yeni koşu: ağaç sıfırlanır.
            _dukkan.Close();
            _dukkan.Open(1, _config, _cuzdan, new WeaponModifierState(), 10);
            Assert.AreEqual(10, _dukkan.Tree.RunId);
            Assert.IsFalse(_dukkan.Tree.IsOwned(_dukkan.Tree.Graph.PurseIndex));
            Assert.AreEqual(0, _dukkan.Tree.HearthStacks);
        }

        [Test]
        public void Anlik_Goruntu_S09_Alanlari_YeniAcilan_Yol()
        {
            RunTreeEffects.TreatAllAsLive = true;
            Altin(60);
            _dukkan.Open(1, _config, _cuzdan, _rank, 3);
            var s = new RunTreeSnapshot();
            s.Refresh(_dukkan);
            Assert.AreEqual(25, s.Count);
            int ks = s.IndexOf("ks_firerate");
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, s.Nodes[ks].Status);
            Assert.AreEqual(s.IndexOf("tier_FireRate"), s.Nodes[ks].MissingParentIndex);
            Assert.IsFalse(s.Nodes[ks].NewlyUnlocked, "koşunun ilk görüntüsünde 'yeni' yok");
            int pierce = s.IndexOf("tier_Piercing");
            Assert.AreEqual(30, s.Nodes[pierce].Cost);

            _rank.SetTier(UpgradeBranch.FireRate, 2);
            s.Refresh(_dukkan);
            Assert.AreEqual(RunTreeNodeStatus.Unaffordable, s.Nodes[ks].Status, "110 > 60");
            Assert.AreEqual(110 - 60, s.Nodes[ks].Shortfall, "N SHORT");
            Assert.IsTrue(s.Nodes[ks].NewlyUnlocked, "ön koşul yeni sağlandı → glint bir kez");
            s.Refresh(_dukkan);
            Assert.IsFalse(s.Nodes[ks].NewlyUnlocked, "yalnız bir kez");

            Assert.IsTrue(_dukkan.SetPin("cap_ar_1"));
            s.Refresh(_dukkan);
            Assert.AreEqual(s.IndexOf("cap_ar_1"), s.PinnedIndex);
            Assert.IsTrue(s.Nodes[s.PinnedIndex].IsPinned);
            Assert.Greater(s.PinPathCount, 1);
            Assert.AreEqual(s.IndexOf("cap_ar_1"), s.PinPath[s.PinPathCount - 1]);
            Assert.Greater(s.PinPathTotalCost, 380);
            Assert.IsFalse(s.UndoAvailable, "K10 açık");
            var yol = new int[RunTreeSnapshot.PathCapacity];
            Assert.AreEqual(2, s.FocusPath(_dukkan, s.IndexOf("ks_piercing"), yol));
        }
    }
}
