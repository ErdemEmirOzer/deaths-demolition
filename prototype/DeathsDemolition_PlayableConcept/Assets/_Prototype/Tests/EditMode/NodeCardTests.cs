using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 K10a · düğüm kartları (DEC-095 K1 hibrit, direction §2.5): bayrak kapalıyken klasik dal kartları; açıkken
    /// teklif ağaç cephesinden en çok 3 düğüm, kavşak çifti tek kart (ikinci seçenek AltNodeIndex), Nişan adımı garanti;
    /// kademe olmayan düğüm alımı; "son alımı geri al" (kart / kademe / Ocak, tam iade gelir değil, seçim hakkı geri
    /// gelir, reroll dönmez, tek adım, son 3 s kapısı, yeni oturum temizler); Yeniden Dövme (R5+, koşuda 1, ortak
    /// fiyatı); düğüm reroll'u (pin kartı kalır); şans koruması sayacı; cüzdan iadesi; S-09 anlık görüntü alanları.
    /// </summary>
    [TestFixture]
    public sealed class NodeCardTests
    {
        private PrototypeConfig _config;
        private GoldWallet _cuzdan;
        private WeaponModifierState _rank;
        private RoundUpgradeShop _dukkan;
        private const int Kosu = 9;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _cuzdan = new GoldWallet();
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(20000, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            _rank = new WeaponModifierState();
            _dukkan = new RoundUpgradeShop
            {
                UnlockedPredicate = _ => true,
                Random = new SeededUpgradeOfferRandom(41),
                WeaponProvider = () => ClassicWeaponId.AssaultRifle,
                NodeCardsOverride = true
            };
            RunTreeEffects.TreatAllAsLive = true;
            RunTreeCatalog.TestSeam.Override(null);
        }

        [TearDown]
        public void TearDown()
        {
            RunTreeEffects.TreatAllAsLive = false;
            Object.DestroyImmediate(_config);
        }

        private void Ac(int round) => _dukkan.Open(round, _config, _cuzdan, _rank, Kosu);

        private int Kart(string nodeId)
        {
            IReadOnlyList<UpgradeCardOffer> cards = _dukkan.Cards;
            for (int c = 0; c < cards.Count; c++)
            {
                if (cards[c].NodeId == nodeId) return c;
                if (cards[c].AltNodeIndex >= 0 && _dukkan.Tree.Graph[cards[c].AltNodeIndex].Id == nodeId) return c;
            }

            return -1;
        }

        private int I(string id) => _dukkan.Tree.Graph.IndexOf(id);

        private void PinleAc(int round, string nodeId)
        {
            if (!_dukkan.Tree.IsBuilt) Ac(round);
            _dukkan.SetPin(nodeId);
            _dukkan.DiscardOffer();
            Ac(round);
        }

        private bool Al(string nodeId, bool alt = false)
        {
            int c = Kart(nodeId);
            Assert.GreaterOrEqual(c, 0, nodeId + " teklifte");
            return _dukkan.TryPurchase(c, _dukkan.SessionId, _dukkan.Revision, alt);
        }

        [Test]
        public void BayrakKapali_KlasikDalKartlari()
        {
            _dukkan.NodeCardsOverride = false;
            Ac(1);
            Assert.AreEqual(UpgradeOfferPolicy.CardsPerOffer, _dukkan.Cards.Count);
            foreach (UpgradeCardOffer c in _dukkan.Cards) Assert.IsFalse(c.IsNode);
        }

        [Test]
        public void DugumTeklifi_UcFarkliDugum_KavsakCiftiTekKart_PinGaranti()
        {
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(1, "ks_firerate");
            IReadOnlyList<UpgradeCardOffer> cards = _dukkan.Cards;
            Assert.LessOrEqual(cards.Count, 3);
            var gorulen = new HashSet<int>();
            foreach (UpgradeCardOffer c in cards)
            {
                Assert.IsTrue(c.IsNode && c.IsValid);
                Assert.IsTrue(gorulen.Add(c.NodeIndex), "tekrar eden düğüm");
                if (c.AltNodeIndex >= 0) Assert.IsTrue(gorulen.Add(c.AltNodeIndex), "kavşak çifti iki kart olamaz");
            }

            Assert.GreaterOrEqual(Kart("ks_firerate"), 0, "Nişan adımı teklifte");

            // Kavşaklar açılınca (keystone sahibi) çift tek kartta gelir.
            Assert.IsTrue(Al("ks_firerate"));
            PinleAc(2, "fork_ar_1_x");
            int f = Kart("fork_ar_1_x");
            Assert.GreaterOrEqual(f, 0);
            UpgradeCardOffer kavsak = _dukkan.Cards[f];
            Assert.AreEqual(RunTreeNodeKind.Fork, kavsak.NodeKind);
            Assert.AreEqual(I("fork_ar_1_y"), kavsak.NodeIndex == I("fork_ar_1_x") ? kavsak.AltNodeIndex : kavsak.NodeIndex);
        }

        [Test]
        public void KavsakKarti_IkinciSecenek_OrtagiKapatir()
        {
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(1, "ks_firerate");
            Assert.IsTrue(Al("ks_firerate"));
            PinleAc(2, "fork_ar_1_x");
            int f = Kart("fork_ar_1_x");
            bool altIsY = _dukkan.Cards[f].AltNodeIndex == I("fork_ar_1_y");
            Assert.IsTrue(_dukkan.TryPurchase(f, _dukkan.SessionId, _dukkan.Revision, altIsY), "Y seçildi");
            Assert.IsTrue(_dukkan.Tree.IsOwned(I("fork_ar_1_y")));
            Assert.IsFalse(_dukkan.Tree.IsOwned(I("fork_ar_1_x")));
            Assert.AreEqual(RunTreeNodeStatus.Excluded, _dukkan.Tree.Status(I("fork_ar_1_x"), _rank, 99999, out _, out _, out _));
        }

        [Test]
        public void GeriAl_KeystoneKarti_TamIade_GelirDegil_SecimHakkiDoner_RerollDonmez()
        {
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(1, "ks_firerate");
            int once = _cuzdan.Balance, kazanc = _cuzdan.TotalEarned;
            Assert.IsTrue(Al("ks_firerate"));
            Assert.IsTrue(_dukkan.Tree.IsOwned(I("ks_firerate")));
            Assert.AreEqual(1, _dukkan.PurchasesThisRound);
            Assert.IsTrue(_dukkan.UndoAvailable);
            int iade = _dukkan.UndoRefund;
            Assert.Greater(iade, 0);

            bool geriAlindi = false;
            _dukkan.PurchaseUndone += _ => geriAlindi = true;
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision));
            Assert.IsTrue(geriAlindi);
            Assert.AreEqual(once, _cuzdan.Balance, "tam iade");
            Assert.AreEqual(kazanc, _cuzdan.TotalEarned, "iade gelir sayılmaz (Kese şişmez)");
            Assert.IsFalse(_dukkan.Tree.IsOwned(I("ks_firerate")));
            Assert.AreEqual(0, _dukkan.PurchasesThisRound);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement);
            Assert.AreEqual(RerollAvailability.LockedByPurchase, _dukkan.RerollAvailability, "reroll hakkı geri gelmez (DEC-050)");
            Assert.AreEqual(RunTreeCommandResult.NothingToUndo, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision), "tek adım");
            Assert.IsTrue(Al("ks_firerate"), "aynı kart yeniden alınır");
        }

        [Test]
        public void GeriAl_KademeKarti_ve_Ocak()
        {
            PinleAc(1, "tier_FireRate");
            Assert.IsTrue(Al("tier_FireRate"));
            Assert.AreEqual(1, _rank.GetTier(UpgradeBranch.FireRate));
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(0, _rank.GetTier(UpgradeBranch.FireRate), "kademe geri");

            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(2, "ks_firerate");
            Assert.IsTrue(Al("ks_firerate"));
            int once = _cuzdan.Balance;
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryStokeHearth(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(1, _dukkan.Tree.HearthStacks);
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision), "son alım Ocak");
            Assert.AreEqual(0, _dukkan.Tree.HearthStacks);
            Assert.AreEqual(1f, _rank.DamageMultiplier, 1e-5f);
            Assert.AreEqual(once, _cuzdan.Balance);
            Assert.IsTrue(_dukkan.Tree.IsOwned(I("ks_firerate")), "yalnız son alım geri alınır");
        }

        [Test]
        public void GeriAl_SonUcSaniyeKapali_YeniOturumTemizler()
        {
            PinleAc(1, "tier_FireRate");
            Assert.IsTrue(Al("tier_FireRate"));
            _dukkan.CloseUndo();
            Assert.IsFalse(_dukkan.UndoAvailable);
            Assert.AreEqual(RunTreeCommandResult.UndoClosed, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision));
            Ac(2);
            Assert.AreEqual(RunTreeCommandResult.NothingToUndo, _dukkan.TryUndoLast(_dukkan.SessionId, _dukkan.Revision), "önceki round'un alımı geri alınmaz");
        }

        [Test]
        public void YenidenDovme_R5Ten_KosudaBir_OrtakFiyati()
        {
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(1, "ks_firerate");
            Assert.IsTrue(Al("ks_firerate"));
            PinleAc(2, "fork_ar_1_x");
            int f = Kart("fork_ar_1_x");
            bool altIsX = _dukkan.Cards[f].AltNodeIndex == I("fork_ar_1_x");
            Assert.IsTrue(_dukkan.TryPurchase(f, _dukkan.SessionId, _dukkan.Revision, altIsX));
            Assert.IsTrue(_dukkan.Tree.IsOwned(I("fork_ar_1_x")));

            Ac(4);
            Assert.AreEqual(-1, _dukkan.ReforgeCost(I("fork_ar_1_x")));
            Assert.AreEqual(RunTreeCommandResult.TooEarly, _dukkan.TryReforge("fork_ar_1_x", _dukkan.SessionId, _dukkan.Revision));

            Ac(5);
            int fiyat = _dukkan.ReforgeCost(I("fork_ar_1_x"));
            Assert.AreEqual(_dukkan.Tree.Cost(I("fork_ar_1_y"), _rank), fiyat, "yeni yolun fiyatı");
            int once = _cuzdan.Balance;
            Assert.AreEqual(RunTreeCommandResult.Done, _dukkan.TryReforge("fork_ar_1_x", _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(once - fiyat, _cuzdan.Balance, "iade yok");
            Assert.IsTrue(_dukkan.Tree.IsOwned(I("fork_ar_1_y")));
            Assert.IsFalse(_dukkan.Tree.IsOwned(I("fork_ar_1_x")));
            Assert.AreEqual(RunTreeCommandResult.AlreadyUsed, _dukkan.TryReforge("fork_ar_1_y", _dukkan.SessionId, _dukkan.Revision));
            Assert.IsFalse(_dukkan.UndoAvailable, "Yeniden Dövme geri alınmaz");
        }

        [Test]
        public void DugumReroll_PinKartiKalir_DigerleriDegisir()
        {
            PinleAc(1, "tier_FireRate");
            int pin = Kart("tier_FireRate");
            var once = new List<int>();
            foreach (UpgradeCardOffer c in _dukkan.Cards) once.Add(c.NodeIndex);
            int altin = _cuzdan.Balance;
            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.Less(_cuzdan.Balance, altin, "reroll ücreti alındı");
            Assert.AreEqual(I("tier_FireRate"), _dukkan.Cards[pin].NodeIndex, "Nişan kartı kalır");
            int degisen = 0;
            for (int c = 0; c < _dukkan.Cards.Count; c++) if (_dukkan.Cards[c].NodeIndex != once[c]) degisen++;
            Assert.GreaterOrEqual(degisen, 1);
        }

        [Test]
        public void EtkiSahipligi_BaglarSpecialUzerindenCozulur()
        {
            var t = new RunTreeState();
            t.Begin(1, ClassicWeaponId.AssaultRifle, _config, MetaRunModifiers.None);
            Assert.IsFalse(t.OwnsEffect(RunTreeEffects.LinkArcConductor));
            t.OnSpecialPickedUp("sw_arc");
            Assert.IsTrue(t.MarkOwned(t.Graph.LinkIIndex));
            Assert.IsTrue(t.OwnsEffect(RunTreeEffects.LinkArcConductor), "Raiju ile Bağ I = İletken");
            Assert.IsFalse(t.OwnsEffect(RunTreeEffects.LinkGelSlickFloor));
            Assert.IsFalse(RunTreeEffects.Owned(RunTreeEffects.LinkArcConductor), "yönetmen yokken false");
        }

        [Test]
        public void SansKorumasi_GorulmeyenKeystoneSayaci()
        {
            var t = new RunTreeState();
            t.Begin(1, ClassicWeaponId.AssaultRifle, _config, MetaRunModifiers.None);
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            int ks = t.Graph.IndexOf("ks_firerate");
            var bos = new List<int>();
            for (int r = 0; r < 3; r++) t.RecordOffer(bos, _rank);
            Assert.AreEqual(3, t.UnseenRounds(ks), "3 teklif dışında → ağırlık ×2");
            t.RecordOffer(new List<int> { ks }, _rank);
            Assert.AreEqual(0, t.UnseenRounds(ks), "gösterilince sıfırlanır");
        }

        [Test]
        public void Cuzdan_Iade_HarcamayiGeriAlir_TekSefer()
        {
            var w = new GoldWallet();
            w.InitializeForRun(3);
            w.Credit(100, GoldSource.EnemyPickup, w.NextTransactionId());
            int tx = w.NextTransactionId();
            Assert.AreEqual(GoldTransactionResult.Applied, w.TrySpend(40, GoldSource.UpgradePurchase, tx));
            Assert.AreEqual(GoldTransactionResult.Applied, w.TryRefund(tx));
            Assert.AreEqual(100, w.Balance);
            Assert.AreEqual(0, w.TotalSpent);
            Assert.AreEqual(0, w.SpentOnUpgrades);
            Assert.AreEqual(100, w.TotalEarned);
            Assert.AreEqual(GoldTransactionResult.UnknownTransaction, w.TryRefund(tx));
            Assert.AreEqual(GoldTransactionResult.UnknownTransaction, w.TryRefund(12345));
        }

        [Test]
        public void AnlikGoruntu_GeriAl_Dovme_Ikon_Kapatir()
        {
            _rank.SetTier(UpgradeBranch.FireRate, 2);
            PinleAc(1, "ks_firerate");
            Assert.IsTrue(Al("ks_firerate"));
            var s = new RunTreeSnapshot();
            s.Refresh(_dukkan);
            Assert.IsTrue(s.UndoAvailable);
            Assert.Greater(s.UndoRefund, 0);
            Assert.IsFalse(s.ReforgeAvailable);
            ref RunTreeNodeView ks = ref s.Nodes[s.IndexOf("ks_firerate")];
            Assert.AreEqual("Upgrade.FireRate", ks.IconKey);
            ref RunTreeNodeView fx = ref s.Nodes[s.IndexOf("fork_ar_1_x")];
            Assert.AreEqual(s.IndexOf("fork_ar_1_y"), fx.ClosesIndex, "closes X");
            Assert.AreEqual("Weapon.ar_1", fx.IconKey);
            Assert.AreEqual("Tree.Hearth", s.Nodes[s.HearthIndex].IconKey);
            Assert.AreEqual(-1, fx.ReforgeCost);
        }
    }
}
