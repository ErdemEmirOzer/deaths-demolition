using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-008 · teklif/reroll politikasının saf yarısı (DEC-047 / DEC-049 / DEC-050):
    /// üç farklı kart, 1–2 slot değişimi, küçük havuzda döngüsüz hata, enjekte RNG ile
    /// deterministik tekrar. Sahnesiz, config'siz (yalnız UpgradeBranchSettings listeleri).
    /// </summary>
    [TestFixture]
    public sealed class UpgradeOfferPolicyTests
    {
        private static UpgradeBranchSettings Dal(UpgradeBranch branch, float weight = 1f, int maxTier = 3)
        {
            return new UpgradeBranchSettings { Branch = branch, DisplayName = branch.ToString(), OfferWeight = weight, MaxTier = maxTier };
        }

        private static List<UpgradeBranchSettings> Havuz(params UpgradeBranch[] branches)
        {
            var list = new List<UpgradeBranchSettings>();
            for (int i = 0; i < branches.Length; i++) list.Add(Dal(branches[i]));
            return list;
        }

        private static List<float> Agirliklar(List<UpgradeBranchSettings> havuz)
        {
            var w = new List<float>();
            for (int i = 0; i < havuz.Count; i++) w.Add(havuz[i].OfferWeight);
            return w;
        }

        private static readonly UpgradeBranch[] On =
        {
            UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Explosion, UpgradeBranch.Chain, UpgradeBranch.FireRate,
            UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed, UpgradeBranch.MaximumHealth, UpgradeBranch.RegenerationDelay, UpgradeBranch.RegenerationRate
        };

        // ------------------------------------------------------------------ eligibility

        [Test]
        public void Uygunluk_Siniflandirma_FiyatKriterDegil()
        {
            UpgradeBranchSettings d = Dal(UpgradeBranch.Piercing, 1f, 3);
            Assert.AreEqual(UpgradeEligibility.Eligible, UpgradeOfferPolicy.Classify(d, 0, true, true));
            Assert.AreEqual(UpgradeEligibility.Eligible, UpgradeOfferPolicy.Classify(d, 2, true, true), "Tavanın altında uygun.");
            Assert.AreEqual(UpgradeEligibility.Maxed, UpgradeOfferPolicy.Classify(d, 3, true, true));
            Assert.AreEqual(UpgradeEligibility.Locked, UpgradeOfferPolicy.Classify(d, 0, true, false));
            Assert.AreEqual(UpgradeEligibility.NoLiveConsumer, UpgradeOfferPolicy.Classify(d, 0, false, true));
            Assert.AreEqual(UpgradeEligibility.ZeroWeight, UpgradeOfferPolicy.Classify(Dal(UpgradeBranch.Chain, 0f), 0, true, true));
            Assert.AreEqual(UpgradeEligibility.InvalidSettings, UpgradeOfferPolicy.Classify(null, 0, true, true));
            Assert.AreEqual(UpgradeEligibility.InvalidSettings, UpgradeOfferPolicy.Classify(Dal(UpgradeBranch.Chain, 1f, 0), 0, true, true));
            // Classify'ın imzasında cüzdan/fiyat yoktur: pahalı kart havuzdan çıkmaz (karar 9).
        }

        // ------------------------------------------------------------------ build

        [Test]
        public void Teklif_TamUcFarkliKart_HavuzTuketilir()
        {
            var rng = new SeededUpgradeOfferRandom(7);
            for (int seed = 0; seed < 50; seed++)
            {
                List<UpgradeBranchSettings> havuz = Havuz(On);
                var picks = new List<UpgradeBranchSettings>();
                UpgradeOfferFault fault = UpgradeOfferPolicy.TryBuildOffer(havuz, Agirliklar(havuz), rng, picks);
                Assert.AreEqual(UpgradeOfferFault.None, fault);
                Assert.AreEqual(UpgradeOfferPolicy.CardsPerOffer, picks.Count);
                Assert.IsTrue(UpgradeOfferPolicy.Validate(picks), "Üç kart birbirinden farklı olmalı.");
                Assert.AreEqual(On.Length - 3, havuz.Count, "Seçilenler havuzdan çıkarılır (yerine koymadan çekiliş).");
            }
        }

        [Test]
        public void Teklif_UctenAzAday_Hata_KartUydurulmaz()
        {
            List<UpgradeBranchSettings> havuz = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Chain);
            var picks = new List<UpgradeBranchSettings> { Dal(UpgradeBranch.FireRate) };
            Assert.AreEqual(UpgradeOfferFault.NotEnoughEligibleBranches,
                UpgradeOfferPolicy.TryBuildOffer(havuz, Agirliklar(havuz), new SeededUpgradeOfferRandom(1), picks));
            Assert.AreEqual(0, picks.Count, "Hata durumunda kısmi/duplicate kart yok.");

            var bos = new List<UpgradeBranchSettings>();
            Assert.AreEqual(UpgradeOfferFault.NotEnoughEligibleBranches,
                UpgradeOfferPolicy.TryBuildOffer(bos, new List<float>(), new SeededUpgradeOfferRandom(1), picks));
        }

        [Test]
        public void Teklif_TabloAyniDaliIkiKezListeler_DuplicateHatasi()
        {
            List<UpgradeBranchSettings> havuz = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Piercing, UpgradeBranch.Chain, UpgradeBranch.FireRate);
            var picks = new List<UpgradeBranchSettings>();
            Assert.AreEqual(UpgradeOfferFault.DuplicateBranch,
                UpgradeOfferPolicy.TryBuildOffer(havuz, Agirliklar(havuz), new SeededUpgradeOfferRandom(1), picks));
            Assert.AreEqual(0, picks.Count);
        }

        [Test]
        public void Teklif_AyniTohum_AyniSonuc_FarkliTohum_EnAzBirFark()
        {
            List<UpgradeBranchSettings> a = Havuz(On);
            List<UpgradeBranchSettings> b = Havuz(On);
            var pa = new List<UpgradeBranchSettings>();
            var pb = new List<UpgradeBranchSettings>();
            UpgradeOfferPolicy.TryBuildOffer(a, Agirliklar(a), new SeededUpgradeOfferRandom(1234), pa);
            UpgradeOfferPolicy.TryBuildOffer(b, Agirliklar(b), new SeededUpgradeOfferRandom(1234), pb);
            for (int i = 0; i < pa.Count; i++) Assert.AreEqual(pa[i].Branch, pb[i].Branch, "Aynı tohum aynı teklif.");

            bool anyDifferent = false;
            for (int seed = 0; seed < 20 && !anyDifferent; seed++)
            {
                List<UpgradeBranchSettings> c = Havuz(On);
                var pc = new List<UpgradeBranchSettings>();
                UpgradeOfferPolicy.TryBuildOffer(c, Agirliklar(c), new SeededUpgradeOfferRandom(seed), pc);
                for (int i = 0; i < pc.Count; i++) anyDifferent |= pc[i].Branch != pa[i].Branch;
            }

            Assert.IsTrue(anyDifferent, "Farklı tohumlar farklı teklifler üretebilmeli (RNG gerçekten tüketiliyor).");
        }

        [Test]
        public void AgirlikliCekilis_SinirlarIcinde_SifirAgirlikHicSecilmez()
        {
            var weights = new List<float> { 0f, 5f, 0f };
            var rng = new SeededUpgradeOfferRandom(3);
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(1, UpgradeOfferPolicy.DrawWeightedIndex(weights, rng), "Yalnız pozitif ağırlıklı indeks seçilir.");
            }

            Assert.AreEqual(-1, UpgradeOfferPolicy.DrawWeightedIndex(new List<float>(), rng));
            Assert.AreEqual(0, UpgradeOfferPolicy.DrawWeightedIndex(new List<float> { 0f, 0f }, rng), "Toplam 0 → ilk indeks (döngü yok).");
        }

        // ------------------------------------------------------------------ reroll

        [Test]
        public void Reroll_BirVeyaIkiSlotDegisir_EnAzBirKartKalir_UcFarkli()
        {
            var counts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } };
            for (int seed = 0; seed < 200; seed++)
            {
                List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
                List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed, UpgradeBranch.MaximumHealth);
                var result = new List<UpgradeBranchSettings>();
                var slots = new List<int>();
                UpgradeOfferFault fault = UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), new SeededUpgradeOfferRandom(seed), result, slots);

                Assert.AreEqual(UpgradeOfferFault.None, fault);
                Assert.IsTrue(UpgradeOfferPolicy.Validate(result), "Sonuç üç farklı kart.");
                int changed = UpgradeOfferPolicy.CountChanged(current, result);
                Assert.That(changed, Is.InRange(1, 2), "Tam olarak bir veya iki slot değişir.");
                Assert.AreEqual(changed, slots.Count);
                counts[changed]++;

                // Değişen slotlar havuzdan, kalan slotlar aynen yerinde.
                for (int i = 0; i < result.Count; i++)
                {
                    bool replaced = slots.Contains(i);
                    if (replaced)
                    {
                        Assert.AreNotEqual(current[i].Branch, result[i].Branch);
                        Assert.Less(UpgradeOfferPolicy.IndexOfBranch(current, result[i].Branch), 0, "Yeni kart eski teklifte yoktu.");
                    }
                    else
                    {
                        Assert.AreEqual(current[i].Branch, result[i].Branch, "Korunan kart slotunda kalır.");
                    }
                }
            }

            Assert.Greater(counts[1], 0, "Tek slot değişimi de gerçekleşmeli.");
            Assert.Greater(counts[2], 0, "İki slot değişimi de gerçekleşmeli.");
        }

        [Test]
        public void Reroll_RT4_TutulanSlotHicDegismez_TutulmayincaKlasikCekilis()
        {
            var counts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } };
            for (int keep = 0; keep < UpgradeOfferPolicy.CardsPerOffer; keep++)
            {
                for (int seed = 0; seed < 120; seed++)
                {
                    List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
                    List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed);
                    var result = new List<UpgradeBranchSettings>();
                    var slots = new List<int>();
                    Assert.AreEqual(UpgradeOfferFault.None, UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), new SeededUpgradeOfferRandom(seed), result, slots, keep));
                    Assert.IsFalse(slots.Contains(keep), "pinli kartın slotu değişmez (keep " + keep + ", seed " + seed + ")");
                    Assert.AreEqual(current[keep].Branch, result[keep].Branch);
                    Assert.IsTrue(UpgradeOfferPolicy.Validate(result));
                    counts[slots.Count]++;
                }
            }

            Assert.Greater(counts[1], 0);
            Assert.Greater(counts[2], 0, "tutulan slot dışındaki iki slot da değişebilir");

            // keepSlot -1: çekiliş klasik yolla birebir aynı (aynı tohum, aynı slotlar).
            for (int seed = 0; seed < 50; seed++)
            {
                List<UpgradeBranchSettings> a = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
                List<UpgradeBranchSettings> pa = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed);
                List<UpgradeBranchSettings> pb = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed);
                var ra = new List<UpgradeBranchSettings>(); var sa = new List<int>();
                var rb = new List<UpgradeBranchSettings>(); var sb = new List<int>();
                UpgradeOfferPolicy.TryReroll(a, pa, Agirliklar(pa), new SeededUpgradeOfferRandom(seed), ra, sa);
                UpgradeOfferPolicy.TryReroll(a, pb, Agirliklar(pb), new SeededUpgradeOfferRandom(seed), rb, sb, -1);
                CollectionAssert.AreEqual(sa, sb);
                for (int i = 0; i < ra.Count; i++) Assert.AreEqual(ra[i].Branch, rb[i].Branch);
            }
        }

        [Test]
        public void Reroll_TekAday_TekSlotDegisir()
        {
            List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
            List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.FireRate);
            var result = new List<UpgradeBranchSettings>();
            var slots = new List<int>();
            Assert.AreEqual(UpgradeOfferFault.None, UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), new SeededUpgradeOfferRandom(9), result, slots));
            Assert.AreEqual(1, UpgradeOfferPolicy.CountChanged(current, result));
            Assert.AreEqual(UpgradeBranch.FireRate, result[slots[0]].Branch);
            Assert.AreEqual(0, pool.Count, "Aday tüketildi.");
        }

        [Test]
        public void Reroll_AdayYok_Hata_SonucBos_RngTuketilmez()
        {
            List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
            var result = new List<UpgradeBranchSettings>();
            var slots = new List<int>();
            var rng = new SayanRng();
            Assert.AreEqual(UpgradeOfferFault.NoReplacementAvailable,
                UpgradeOfferPolicy.TryReroll(current, new List<UpgradeBranchSettings>(), new List<float>(), rng, result, slots));
            Assert.AreEqual(0, result.Count);
            Assert.AreEqual(0, slots.Count);
            Assert.AreEqual(0, rng.Calls, "Üretilemeyen reroll rastgelelik tüketmez (deterministik tekrar).");
        }

        [Test]
        public void Reroll_AdayTeklifteVar_Assertion_HicbirSeyUretilmez()
        {
            List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
            List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.Chain, UpgradeBranch.FireRate);
            var result = new List<UpgradeBranchSettings>();
            var slots = new List<int>();
            Assert.AreEqual(UpgradeOfferFault.InvalidResult,
                UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), new SeededUpgradeOfferRandom(1), result, slots));
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Reroll_GecersizMevcutTeklif_Assertion()
        {
            List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Piercing, UpgradeBranch.Chain);
            List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.FireRate);
            var result = new List<UpgradeBranchSettings>();
            var slots = new List<int>();
            Assert.AreEqual(UpgradeOfferFault.InvalidResult,
                UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), new SeededUpgradeOfferRandom(1), result, slots));
        }

        [Test]
        public void Sabitler_UrunKurallari()
        {
            Assert.AreEqual(3, UpgradeOfferPolicy.CardsPerOffer, "DEC-047: tam üç kart.");
            Assert.AreEqual(1, UpgradeOfferPolicy.MaxPurchasesPerRound, "DEC-048/071 geri dönüş sabiti (config yokken); sevk kuralı DEC-087 = config PurchasesPerRound (2).");
            Assert.AreEqual(1, UpgradeOfferPolicy.MaxRerollsPerRound, "DEC-050: round başına bir ücretli reroll.");
            Assert.AreEqual(1, UpgradeOfferPolicy.MinRerollReplacements);
            Assert.AreEqual(2, UpgradeOfferPolicy.MaxRerollReplacements, "Üç kartın tamamı asla değişmez.");
        }

        [Test]
        public void Rng_UstSinir_1_0_SonKovaVeGecerliSlotlar()
        {
            var bir = new SabitRng(1f);
            Assert.AreEqual(2, UpgradeOfferPolicy.DrawWeightedIndex(new List<float> { 1f, 1f, 1f }, bir), "1.0 → son kova, taşma yok.");

            List<UpgradeBranchSettings> current = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
            List<UpgradeBranchSettings> pool = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, UpgradeBranch.MovementSpeed);
            var result = new List<UpgradeBranchSettings>();
            var slots = new List<int>();
            Assert.AreEqual(UpgradeOfferFault.None, UpgradeOfferPolicy.TryReroll(current, pool, Agirliklar(pool), bir, result, slots));
            Assert.AreEqual(2, slots.Count, "coin 1.0 → iki slot.");
            Assert.IsTrue(UpgradeOfferPolicy.Validate(result));
            CollectionAssert.AllItemsAreUnique(slots);
            foreach (int slot in slots) Assert.That(slot, Is.InRange(0, 2));

            var sifir = new SabitRng(0f);
            List<UpgradeBranchSettings> current2 = Havuz(UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Chain);
            List<UpgradeBranchSettings> pool2 = Havuz(UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed);
            Assert.AreEqual(UpgradeOfferFault.None, UpgradeOfferPolicy.TryReroll(current2, pool2, Agirliklar(pool2), sifir, result, slots));
            Assert.AreEqual(1, slots.Count, "coin 0.0 → tek slot.");
            Assert.AreEqual(0, slots[0]);
        }

        [Test]
        public void SifirAgirlikliDal_TeklifeGirmez_UygunlukKatmani()
        {
            List<UpgradeBranchSettings> havuz = Havuz(On);
            havuz[0].OfferWeight = 0f;   // Piercing: tablo "asla çekme" diyor
            var uygun = new List<UpgradeBranchSettings>();
            for (int i = 0; i < havuz.Count; i++)
            {
                if (UpgradeOfferPolicy.Classify(havuz[i], 0, true, true) == UpgradeEligibility.Eligible) uygun.Add(havuz[i]);
            }

            Assert.AreEqual(On.Length - 1, uygun.Count);
            for (int seed = 0; seed < 50; seed++)
            {
                var kopya = new List<UpgradeBranchSettings>(uygun);
                var picks = new List<UpgradeBranchSettings>();
                Assert.AreEqual(UpgradeOfferFault.None, UpgradeOfferPolicy.TryBuildOffer(kopya, Agirliklar(kopya), new SeededUpgradeOfferRandom(seed), picks));
                Assert.Less(UpgradeOfferPolicy.IndexOfBranch(picks, UpgradeBranch.Piercing), 0, "Sıfır ağırlıklı dal teklif edilmez.");
            }
        }

        private sealed class SabitRng : IUpgradeOfferRandom
        {
            private readonly float _deger;

            public SabitRng(float deger)
            {
                _deger = deger;
            }

            public float NextValue()
            {
                return _deger;
            }
        }

        private sealed class SayanRng : IUpgradeOfferRandom
        {
            public int Calls;

            public float NextValue()
            {
                Calls++;
                return 0.5f;
            }
        }
    }
}
