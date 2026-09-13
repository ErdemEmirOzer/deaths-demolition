using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DamageTarget = DD.Prototype.Weapons.DamageTarget;
using WeaponPower = DD.Prototype.Feedback.WeaponPower;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · Special Weapon ↔ WP-006 DamageTransaction sözleşmesi (DT-1..DT-12 · TM-37..43).
    /// Gerçek zombi, gerçek hasar yolu. Her bağımsız saldırı tek transaction: aynı saldırıdaki her
    /// hedef aynı kimliği taşır, hedef başına/iç içe transaction yok, NestedBegins 0, duplicate 0,
    /// ölü hedefe geç hasar boş, zırhlı hedef mevcut direct/explosion yolundan birindedir (üçüncü
    /// yol yok). Silah kimlikleri fixture'dır; sayılar config'ten.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponDamageSliceTests
    {
        private const int ZombiSayisi = 3;
        private const float TideAtisMenzili = 5f;
        private const float CanPayi = 1.5f;

        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private IEnumerator Kur(SpecialWeaponId silah)
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _s.Controller.ActivateSpecialWeapon(silah);
            yield return null;
            Assert.IsTrue(_s.Controller.SpecialWeaponActive);
        }

        private int OlumsuzRound(float toplamHasar)
        {
            for (int r = 1; r <= 400; r++)
            {
                if (_s.Config.EnemyHealth(EnemyTier.Standard, r) > toplamHasar * CanPayi)
                {
                    return r;
                }
            }

            Assume.That(false, "Türetme öncülü: can eğrisi yetmiyor.");
            return 1;
        }

        private ZombieEnemy Dogur(Vector3 konum, int round, bool zirhli)
        {
            Vector3 d = _s.Rig.transform.position - konum;
            d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, round, konum, bakis);
            Assert.IsNotNull(z, "Zombi doğmadı.");
            z.FitArmorComposition(zirhli, zirhli, zirhli);
            return z;
        }

        private static HashSet<int> TransactionKumesi(List<EnemyDamageInfo> olaylar, int bas, int son)
        {
            var k = new HashSet<int>();
            for (int i = bas; i < son; i++)
            {
                k.Add(olaylar[i].Meta.TransactionId);
            }

            return k;
        }

        // ============================================================ TM-37 / 38 / 39 / 40 / 41 · Kagura tek bant

        /// <summary>[TM-37][TM-38][TM-39][TM-40][TM-41] Bir dalga bandı = bir transaction: N hedef aynı kimliği taşır, transaction sayısı 1 artar, nested 0, duplicate 0.</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Kagura_BirBant_TekTransaction_TumHedeflerAyniKimlik()
        {
            yield return Kur(SpecialWeaponId.KaguraBell);
            KaguraBellSettings k = _s.Special.KaguraBell;
            int round = OlumsuzRound(k.Common.Damage);

            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < ZombiSayisi; i++)
            {
                // Aynı bandın içinde olmaları için aynı yarıçapa, farklı açılara.
                float aci = (i - (ZombiSayisi - 1) * 0.5f) * 25f * Mathf.Deg2Rad;
                float r = 0.5f * k.WaveRadius;
                zombiler.Add(Dogur(_s.Zemin(Mathf.Sin(aci) * r, Mathf.Cos(aci) * r), round, false));
            }

            yield return null;

            int nestedOnce = DamageTransaction.NestedBegins;
            int dupOnce = ZombieEnemy.DuplicateApplicationCount;
            int lastOnce = DamageTransaction.Last;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;
                Vector3 origin = _s.Rig.transform.position;

                // Tek karede tam süpürme: dalga süresi kadar bir adım → bütün hedefler aynı bant.
                Time.captureDeltaTime = k.WaveTravelSeconds;
                rt.Fire(origin, Vector3.forward);
                yield return null;
                Time.captureDeltaTime = 0f;
                yield return null;

                Assert.AreEqual(ZombiSayisi, sayac.Damaged.Count, "Öncül: her zombi bir kez kesilmeli.");
                HashSet<int> kimlikler = TransactionKumesi(sayac.Damaged, 0, sayac.Damaged.Count);
                Assert.AreEqual(1, kimlikler.Count, "Aynı bandın hedefleri farklı transaction kimlikleri taşıyor (DT-3).");
                foreach (int id in kimlikler)
                {
                    Assert.AreNotEqual(0, id, "Transaction kimliği 0: hasar transaction dışında uygulandı (DT-1).");
                }

                Assert.AreEqual(1, DamageTransaction.Last - lastOnce, "Bir bant için birden fazla transaction açıldı (DT-4, hedef başına).");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı (DT-5).");
                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi (DT-6).");
                Assert.AreEqual(0, DamageTransaction.Current, "Transaction saldırı sonrası açık kaldı (DT-2).");
                for (int i = 0; i < ZombiSayisi; i++)
                {
                    Assert.AreEqual(1, sayac.DamagedFor(zombiler[i].LifeId), "Zombi " + i + " bir bantta bir kez.");
                    Assert.IsTrue(sayac.Damaged[i].Meta.IsPresent, "Meta yok (DT-8).");
                }
            }
        }

        // ============================================================ TM-37 / 38 · Ofuda collapse

        /// <summary>[TM-37][TM-38] Ağ çöküşü tek transaction: mühürlü N hedef aynı kimliği taşır; dart'lar ayrı (mevcut Fire transaction'ı).</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Ofuda_Collapse_TekTransaction()
        {
            yield return Kur(SpecialWeaponId.OfudaLoom);
            OfudaLoomSettings o = _s.Special.OfudaLoom;
            int n = Mathf.Max(1, o.MinSealsToCollapse);
            int round = OlumsuzRound(2f * o.Common.Damage + o.CollapseDamage);

            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < n; i++)
            {
                zombiler.Add(Dogur(_s.Zemin((i - (n - 1) * 0.5f) * 3f, 5f), round, false));
            }

            yield return null;
            int nestedOnce = DamageTransaction.NestedBegins;
            int dupOnce = ZombieEnemy.DuplicateApplicationCount;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;
                for (int i = 0; i < n; i++)
                {
                    Dart(rt, zombiler[i]);
                    yield return null;
                }

                yield return SpecialWeaponSahne.Bekle(() => rt.Web.ArmedSealCount >= n, SpecialWeaponSahne.FazZamanAsimi, "Mühürler kurulmadı.");
                int oncekiOlay = sayac.Damaged.Count;
                int lastOnce = DamageTransaction.Last;
                Dart(rt, zombiler[0]);
                yield return null;
                yield return null;

                Assert.AreEqual(1, rt.Web.CollapseCount);
                // Tetik dart (1 olay, kendi tx'i) + collapse (n olay, tek tx).
                Assert.AreEqual(oncekiOlay + 1 + n, sayac.Damaged.Count, "Tetik dart + collapse olay sayısı.");
                HashSet<int> collapseTx = TransactionKumesi(sayac.Damaged, oncekiOlay + 1, sayac.Damaged.Count);
                Assert.AreEqual(1, collapseTx.Count, "Collapse hedefleri farklı transaction kimlikleri taşıyor (DT-3).");
                Assert.AreNotEqual(sayac.Damaged[oncekiOlay].Meta.TransactionId, sayac.Damaged[oncekiOlay + 1].Meta.TransactionId,
                    "Tetik dart ile collapse aynı transaction'da: mühürlü hedefin collapse payı reddedilirdi (?U-4 sınırı).");
                Assert.AreEqual(2, DamageTransaction.Last - lastOnce, "Tetik dart + collapse = tam iki transaction (DT-4).");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı (DT-5).");
                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi (DT-6).");
            }
        }

        private void Dart(SpecialWeaponRuntime rt, ZombieEnemy hedef)
        {
            Collider hitbox = hedef.HitboxFor(BodyRegion.UpperBody);
            Assert.IsNotNull(hitbox);
            Vector3 hedefNokta = hitbox.bounds.center;
            Vector3 origin = _s.Rig.transform.position + Vector3.up * (hedefNokta.y - _s.Rig.transform.position.y);
            rt.Fire(origin, (hedefNokta - origin).normalized);
        }

        // ============================================================ TM-38 / 43 · Tide pull tick + zırhlı hedef

        /// <summary>
        /// [TM-38][TM-43] Her pull tick'i tek transaction (tick içindeki hedefler aynı kimlik, tick'ler farklı); zırhlı hedefte pull
        /// doğrudan yol, implode patlama yolu — üçüncü bir zırh yolu yok; nested 0.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Tide_PullTickTekTransaction_ZirhliHedef_DirectVeyaExplosion()
        {
            yield return Kur(SpecialWeaponId.TideJewel);
            TideJewelSettings t = _s.Special.TideJewel;
            t.ThrowRange = TideAtisMenzili;
            _s.Config.NotifyChanged();
            int tick = Mathf.CeilToInt(t.WellSeconds / Mathf.Max(0.001f, t.PullInterval));
            int round = OlumsuzRound(tick * t.PullDamage + t.ImplosionDamage);

            Vector3 inis = _s.Zemin(0f, TideAtisMenzili);
            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < ZombiSayisi; i++)
            {
                zombiler.Add(Dogur(inis + new Vector3((i - (ZombiSayisi - 1) * 0.5f) * 2f, 0f, 2f), round, zirhli: true));
            }

            yield return null;
            int nestedOnce = DamageTransaction.NestedBegins;
            int dupOnce = ZombieEnemy.DuplicateApplicationCount;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;
                rt.Fire(_s.Rig.transform.position + Vector3.up, Vector3.forward);
                yield return null;
                Assert.AreEqual(1, TideWell.LiveCount);
                float t0 = Time.realtimeSinceStartup;
                while (TideWell.LiveCount > 0)
                {
                    Assert.Less(Time.realtimeSinceStartup - t0, SpecialWeaponSahne.FazZamanAsimi, "Kuyu kapanmadı.");
                    yield return null;
                }

                yield return null;

                // Transaction gruplaması, olay SIRASIYLA: aynı transaction kimliği = bir saldırı
                // (pull tick'i ya da implode); grup başına hedef tekil. Implode kuyu kapanınca
                // ateşlenir, dolayısıyla SON grup implode'dur. Ayrım bilerek `Meta.Kind`'a
                // bağlanmaz: pull'ların Direct, implode'un Explosion sınıflanması ?U-1 (kullanıcı
                // kararı) — bu sözleşme testi o kararı kilitlemez (K-2).
                var gruplar = new List<KeyValuePair<int, HashSet<int>>>();
                var grupIndeks = new Dictionary<int, int>();
                for (int e = 0; e < sayac.Damaged.Count; e++)
                {
                    EnemyDamageInfo info = sayac.Damaged[e];
                    Assert.IsTrue(info.Meta.IsPresent, "Meta yok (DT-8).");
                    Assert.IsTrue(info.Meta.Kind == DamageKind.Direct || info.Meta.Kind == DamageKind.Explosion,
                        "Zırhlı hedefe direct/explosion dışı bir yol uygulandı (DT-12).");
                    if (!grupIndeks.TryGetValue(info.Meta.TransactionId, out int gi))
                    {
                        gi = gruplar.Count;
                        grupIndeks[info.Meta.TransactionId] = gi;
                        gruplar.Add(new KeyValuePair<int, HashSet<int>>(info.Meta.TransactionId, new HashSet<int>()));
                    }

                    Assert.AreEqual(gruplar.Count - 1, gi, "Bir transaction'ın olayları araya başka transaction girdikten sonra devam etti (Pull+Implode birleşmemeli / DT-3).");
                    Assert.IsTrue(gruplar[gi].Value.Add(info.LifeId), "Aynı saldırı transaction'ında aynı hedef iki kez (DT-6).");
                }

                int pullTickSayisi = gruplar.Count - 1;
                Assert.GreaterOrEqual(pullTickSayisi, 1, "Hiç pull tick'i yok (implode dışında grup yok).");
                Assert.LessOrEqual(pullTickSayisi, tick, "Tick sayısı formülü aşıyor.");
                Assert.AreEqual(ZombiSayisi, gruplar[gruplar.Count - 1].Value.Count, "Implode (son transaction) her zombiye bir kez.");
                for (int g = 0; g < pullTickSayisi; g++)
                {
                    Assert.AreEqual(ZombiSayisi, gruplar[g].Value.Count, "Bir tick'te bütün zombiler aynı transaction kimliğini taşımalı (DT-3).");
                }

                // Zırh: zırhlı hedefte pull ve implode zırhı etkiler; mevcut sözleşme (WP-006) dışında yol yok.
                float zirhToplam = 0f;
                for (int e = 0; e < sayac.Damaged.Count; e++)
                {
                    zirhToplam += sayac.Damaged[e].Result.ArmorDamage;
                }

                Assert.Greater(zirhToplam, 0f, "Zırhlı hedeflerde zırh hasarı 0: zırh atlanıyor (DT-12).");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı (DT-5).");
                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi (DT-6).");
                Assert.AreEqual(0, DamageTransaction.Current, "Transaction açık kaldı (DT-2).");
            }
        }

        // ============================================================ TM-42 · ölü hedefe geç hasar

        /// <summary>[TM-42] Ölü hedefe Special saldırısı boş sonuç: ikinci ölüm, kill, gold, giydirme yok.</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator OluHedef_GecHasar_YeniOlumKillGoldYok()
        {
            yield return Kur(SpecialWeaponId.KaguraBell);
            ZombieEnemy z = Dogur(_s.Zemin(0f, 4f), 1, false);
            yield return null;

            using (var sayac = new SpecialOlaySayaci())
            {
                int killOnce = RunDirector.Instance.Stats.TotalKills;
                z.Kill();
                yield return null;
                Assert.AreEqual(1, sayac.Died.Count, "Öncül: ilk ölüm bir kez.");
                int olum = sayac.Died.Count;
                int hasar = sayac.Damaged.Count;
                int giydirme = sayac.Dressed;
                int kill = RunDirector.Instance.Stats.TotalKills;
                int gold = GoldService.Instance != null ? GoldService.Instance.Ledger.Granted : 0;
                Assert.AreEqual(killOnce + 1, kill, "Öncül: ilk ölüm kill sayacına yazılmalı.");

                // Geç dış vuruş (aynı yol: ApplyExternalHit) ve dalga.
                Assert.IsTrue(DamageTargetResolver.TryResolve(z.HitboxFor(BodyRegion.UpperBody), out DamageTarget hedef), "Öncül: ölü zombinin hedefi çözülmeli (havuz önbelleği).");
                DamageResult r = HitscanEngine.ApplyExternalHit(hedef, 100f, z.transform.position, Vector3.forward, DamageForce.Chain, DamageSourceKind.Explosion, WeaponPower.Heavy);
                Assert.IsTrue(DamageResults.IsEmpty(in r), "Ölü hedefe geç hasar boş sonuç değil (DT-7).");
                _s.Controller.Special.Fire(_s.Rig.transform.position, Vector3.forward);
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Special.Waves.ActiveWaves == 0, SpecialWeaponSahne.FazZamanAsimi, "Dalga bitmedi.");
                yield return null;

                Assert.AreEqual(olum, sayac.Died.Count, "Ölü hedefe geç hasar ikinci ölüm üretti (DT-7).");
                Assert.AreEqual(hasar, sayac.Damaged.Count, "Ölü hedefe geç hasar EnemyDamaged üretti.");
                Assert.AreEqual(giydirme, sayac.Dressed, "Ölü hedefe geç hasar giydirildi.");
                Assert.AreEqual(kill, RunDirector.Instance.Stats.TotalKills, "Ölü hedefe geç hasar kill sayacını artırdı.");
                if (GoldService.Instance != null)
                {
                    Assert.AreEqual(gold, GoldService.Instance.Ledger.Granted, "Ölü hedefe geç hasar gold verdi.");
                }
            }
        }
    }
}
