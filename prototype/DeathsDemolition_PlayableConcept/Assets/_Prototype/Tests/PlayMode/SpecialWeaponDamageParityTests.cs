using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · PrototypeOnly KARAKTERİZASYON — hasar-çıktı paritesi (C-00 koşulu ?U-4 / K-1).
    ///
    /// Üç prototip Special Weapon'ın (Kagura Bell / Ofuda Loom / Tide Jewel — final içerik
    /// değil, fixture) HEAD'deki hasar çıktısını sabitler: hedef başına uygulama sayısı,
    /// toplam uygulanan miktar (config formülüyle), duplicate reddi 0, NestedBegins 0.
    /// DamageTransaction kapsamı değiştiğinde (DT-1/3/4) bu assert'lerin AYNEN yeşil kalması
    /// "teknik hizalama"nın kanıtıdır; herhangi biri kırılırsa hasar davranışı değişmiştir →
    /// ?U-4 → kullanıcı kararı. Mutlak ölçümler `[DD-TEST] parity.*` satırına yazılır (defter
    /// önce/sonra tablosu). U-1 (Kagura/Tide doğrudan yol → Kind=Direct) burada yalnız
    /// karakterize edilir; canonical değildir.
    ///
    /// Gerçek zaman: Heavy vuruşlar hit-stop ister ve sabit yakalama adımı ölçeksiz saati
    /// ilerletmez (WP-002 dersi); adımlar ölçülmez, olay sayıları ve miktarlar ölçülür.
    /// </summary>
    [TestFixture]
    [Category("PrototypeOnly")]
    [Category("WP007")]
    public sealed class SpecialWeaponDamageParityTests
    {
        /// <summary>Test parametreleri: zombi sayısı, mesafe oranları, hasar/can payı. Ürün sayısı değil.</summary>
        private const int ZombiSayisi = 3;
        private const float CanPayi = 1.5f;
        private const float TideAtisMenzili = 5f;
        private const float Tolerans = 0.02f;

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
            Assert.IsTrue(_s.Controller.SpecialWeaponActive, "Special aktif olmadı.");
            Assert.AreEqual(silah, _s.Controller.Special.Id);
        }

        /// <summary>Verilen toplam hasarı ölümsüz karşılayacak round'u config'ten arar (can eğrisi test öncülü).</summary>
        private int OlumsuzRound(float toplamHasar)
        {
            PrototypeConfig c = _s.Config;
            for (int r = 1; r <= 400; r++)
            {
                if (c.EnemyHealth(EnemyTier.Standard, r) > toplamHasar * CanPayi)
                {
                    return r;
                }
            }

            Assume.That(false, "Türetme öncülü: can eğrisi " + toplamHasar + " hasarı hiçbir roundda karşılamıyor.");
            return 1;
        }

        /// <summary>Zırhsız, ölümsüz-canlı Standard: parite formülü yalnız cana giden miktarı sayar.</summary>
        private ZombieEnemy ZirhsizDogur(Vector3 konum, int round)
        {
            Vector3 d = _s.Rig.transform.position - konum;
            d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, round, konum, bakis);
            Assert.IsNotNull(z, "Zombi doğmadı.");
            z.FitArmorComposition(false, false, false);
            Assert.AreEqual(0, z.Armor.EquippedCount, "Zırh temizlenmedi.");
            return z;
        }

        private static float Miktar(in EnemyDamageInfo i) => i.Result.HealthDamage + i.Result.ArmorDamage;

        private static int FarkliTransaction(List<EnemyDamageInfo> olaylar, int baslangic, int bitis)
        {
            var kume = new HashSet<int>();
            for (int i = baslangic; i < bitis; i++)
            {
                kume.Add(olaylar[i].Meta.TransactionId);
            }

            return kume.Count;
        }

        // ============================================================ Kagura Bell · tek Emit, N zombi

        /// <summary>
        /// Bir dalga, yarıçap içindeki her zombiyi TAM BİR kez keser; miktar = Damage · lerp(1, FalloffAtRim, d/R).
        /// Duplicate reddi 0, NestedBegins 0. U-1: Source=Explosion, Kind=Direct (karakterizasyon).
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HasarCiktiParitesi_KaguraBell_TekEmitNZombi()
        {
            yield return Kur(SpecialWeaponId.KaguraBell);
            KaguraBellSettings k = _s.Special.KaguraBell;
            int round = OlumsuzRound(k.Common.Damage);

            var zombiler = new List<ZombieEnemy>(ZombiSayisi);
            for (int i = 0; i < ZombiSayisi; i++)
            {
                float oran = (i + 1f) / (ZombiSayisi + 1f);           // 0.25, 0.5, 0.75 · R
                float x = (i - (ZombiSayisi - 1) * 0.5f) * 1.5f;
                zombiler.Add(ZirhsizDogur(_s.Zemin(x, oran * k.WaveRadius), round));
            }

            yield return null;

            int dupOnce = ZombieEnemy.DuplicateApplicationCount;
            int nestedOnce = DamageTransaction.NestedBegins;
            int lastOnce = DamageTransaction.Last;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;
                Vector3 origin = _s.Rig.transform.position;
                rt.Fire(origin, Vector3.forward);
                Assert.AreEqual(1, rt.Waves.ActiveWaves, "Tek Emit tek dalga üretmeli.");

                yield return SpecialWeaponSahne.Bekle(() => rt.Waves.ActiveWaves == 0, SpecialWeaponSahne.FazZamanAsimi, "Dalga bitmedi.");
                yield return null;

                var log = new StringBuilder("[DD-TEST] parity.kagura round=").Append(round).Append(' ');
                for (int i = 0; i < zombiler.Count; i++)
                {
                    int life = zombiler[i].LifeId;
                    Assert.AreEqual(1, sayac.DamagedFor(life), "Zombi " + i + " bir dalgada bir kez kesilmeli.");
                    Assert.IsTrue(zombiler[i].IsAlive, "Zombi " + i + " öldü: formül geçersiz (öncül).");
                }

                Assert.AreEqual(ZombiSayisi, sayac.Damaged.Count, "Olay sayısı zombi sayısına eşit değil.");
                float toplam = 0f;
                for (int e = 0; e < sayac.Damaged.Count; e++)
                {
                    EnemyDamageInfo info = sayac.Damaged[e];
                    Vector3 flat = info.Point - origin;
                    flat.y = 0f;
                    float d = flat.magnitude;
                    float beklenen = k.Common.Damage * Mathf.Lerp(1f, k.FalloffAtRim, Mathf.Clamp01(d / Mathf.Max(0.001f, k.WaveRadius)));
                    Assert.AreEqual(beklenen, Miktar(info), beklenen * Tolerans, "Olay " + e + " miktarı falloff formülüne uymuyor (d=" + d + ").");
                    Assert.AreEqual(DamageSourceKind.Explosion, info.Source, "U-1 karakterizasyon: Kagura kaynağı Explosion.");
                    Assert.AreEqual(DamageKind.Direct, info.Meta.Kind, "U-1 karakterizasyon: Kagura doğrudan yol → Kind=Direct.");
                    toplam += Miktar(info);
                    log.Append("z").Append(e).Append("=d").Append(d.ToString("0.00")).Append('/').Append(Miktar(info).ToString("0.00")).Append(' ');
                }

                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi oluştu.");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı.");
                Assert.AreEqual(ZombiSayisi, rt.Waves.TotalCrossings, "TotalCrossings zombi sayısına eşit değil.");
                log.Append("events=").Append(sayac.Damaged.Count).Append(" total=").Append(toplam.ToString("0.00"))
                   .Append(" dupDelta=").Append(ZombieEnemy.DuplicateApplicationCount - dupOnce)
                   .Append(" nestedDelta=").Append(DamageTransaction.NestedBegins - nestedOnce)
                   .Append(" txDistinct=").Append(FarkliTransaction(sayac.Damaged, 0, sayac.Damaged.Count))
                   .Append(" txLastDelta=").Append(DamageTransaction.Last - lastOnce);
                Debug.Log(log.ToString());
            }
        }

        // ============================================================ Ofuda Loom · N seal + collapse

        /// <summary>
        /// N dart N zombiyi mühürler; mühürlü bir hedefe ikinci dart ağı çökertir. Zombi₁: dart+dart+collapse,
        /// diğerleri dart+collapse; miktarlar config'ten. Duplicate reddi 0, NestedBegins 0.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HasarCiktiParitesi_OfudaLoom_NSealVeCollapse()
        {
            yield return Kur(SpecialWeaponId.OfudaLoom);
            OfudaLoomSettings o = _s.Special.OfudaLoom;
            int n = Mathf.Max(1, o.MinSealsToCollapse);
            int round = OlumsuzRound(2f * o.Common.Damage + o.CollapseDamage);

            var zombiler = new List<ZombieEnemy>(n);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * 3f;
                zombiler.Add(ZirhsizDogur(_s.Zemin(x, 5f), round));
            }

            yield return null;

            int dupOnce = ZombieEnemy.DuplicateApplicationCount;
            int nestedOnce = DamageTransaction.NestedBegins;
            int lastOnce = DamageTransaction.Last;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;

                for (int i = 0; i < n; i++)
                {
                    Dart(rt, zombiler[i]);
                    yield return null;
                    Assert.AreEqual(i + 1, rt.Web.SealCount, "Dart " + i + " mühür üretmedi.");
                }

                yield return SpecialWeaponSahne.Bekle(() => rt.Web.ArmedSealCount >= n, SpecialWeaponSahne.FazZamanAsimi, "Mühürler kurulmadı.");
                int collapseOnce = sayac.Damaged.Count;
                Dart(rt, zombiler[0]);
                yield return null;
                yield return null;

                Assert.AreEqual(1, rt.Web.CollapseCount, "Çöküş bir kez.");
                Assert.AreEqual(0, rt.Web.SealCount, "Çöküş mühürleri tüketmedi.");

                var log = new StringBuilder("[DD-TEST] parity.ofuda round=").Append(round).Append(" n=").Append(n).Append(' ');
                float toplam = 0f;
                for (int i = 0; i < n; i++)
                {
                    int life = zombiler[i].LifeId;
                    int beklenenSayi = i == 0 ? 3 : 2;
                    Assert.AreEqual(beklenenSayi, sayac.DamagedFor(life), "Zombi " + i + " uygulama sayısı.");
                    Assert.IsTrue(zombiler[i].IsAlive, "Zombi " + i + " öldü: formül geçersiz (öncül).");
                    float zombiToplam = 0f;
                    for (int e = 0; e < sayac.Damaged.Count; e++)
                    {
                        if (sayac.Damaged[e].LifeId == life)
                        {
                            zombiToplam += Miktar(sayac.Damaged[e]);
                        }
                    }

                    float beklenen = (i == 0 ? 2f : 1f) * o.Common.Damage + o.CollapseDamage;
                    Assert.AreEqual(beklenen, zombiToplam, beklenen * Tolerans, "Zombi " + i + " toplam miktarı config formülüne uymuyor.");
                    toplam += zombiToplam;
                    log.Append("z").Append(i).Append('=').Append(sayac.DamagedFor(life)).Append('/').Append(zombiToplam.ToString("0.00")).Append(' ');
                }

                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi oluştu.");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı.");
                log.Append("events=").Append(sayac.Damaged.Count).Append(" total=").Append(toplam.ToString("0.00"))
                   .Append(" dupDelta=").Append(ZombieEnemy.DuplicateApplicationCount - dupOnce)
                   .Append(" nestedDelta=").Append(DamageTransaction.NestedBegins - nestedOnce)
                   .Append(" collapseTxDistinct=").Append(FarkliTransaction(sayac.Damaged, collapseOnce + 1, sayac.Damaged.Count))
                   .Append(" txLastDelta=").Append(DamageTransaction.Last - lastOnce);
                Debug.Log(log.ToString());
            }
        }

        private void Dart(SpecialWeaponRuntime rt, ZombieEnemy hedef)
        {
            Collider hitbox = hedef.HitboxFor(BodyRegion.UpperBody);
            Assert.IsNotNull(hitbox, "Gövde hitbox'ı yok.");
            Vector3 hedefNokta = hitbox.bounds.center;
            Vector3 origin = _s.Rig.transform.position + Vector3.up * (hedefNokta.y - _s.Rig.transform.position.y);
            rt.Fire(origin, (hedefNokta - origin).normalized);
        }

        // ============================================================ Tide Jewel · kuyu pull + implode

        /// <summary>
        /// Kuyu her pull tick'inde yarıçaptaki her zombiye bir kez, sonra implode bir kez: pull_i ≥ 1,
        /// Σ pull_i = TotalPullTicks, tick sayısı = ceil(WellSeconds/PullInterval); miktar = pull_i·PullDamage + implode_i.
        /// U-1: pull Source=Explosion/Kind=Direct; implode Kind=Explosion (karakterizasyon).
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HasarCiktiParitesi_TideJewel_KuyuPullVeImplode()
        {
            yield return Kur(SpecialWeaponId.TideJewel);
            TideJewelSettings t = _s.Special.TideJewel;
            t.ThrowRange = TideAtisMenzili;                 // test parametresi: kuyu oyuncunun önüne, açık zemine
            _s.Config.NotifyChanged();
            int tickBeklenen = Mathf.CeilToInt(t.WellSeconds / Mathf.Max(0.001f, t.PullInterval));
            int round = OlumsuzRound(tickBeklenen * t.PullDamage + t.ImplosionDamage);

            Vector3 origin = _s.Rig.transform.position + Vector3.up;
            Vector3 inis = _s.Zemin(0f, TideAtisMenzili);
            var zombiler = new List<ZombieEnemy>(ZombiSayisi);
            for (int i = 0; i < ZombiSayisi; i++)
            {
                float x = (i - (ZombiSayisi - 1) * 0.5f) * 2f;
                zombiler.Add(ZirhsizDogur(inis + new Vector3(x, 0f, 2f), round));
            }

            yield return null;

            int dupOnce = ZombieEnemy.DuplicateApplicationCount;
            int nestedOnce = DamageTransaction.NestedBegins;
            int lastOnce = DamageTransaction.Last;

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRuntime rt = _s.Controller.Special;
                rt.Fire(origin, Vector3.forward);
                yield return null;
                Assert.AreEqual(1, TideWell.LiveCount, "Tek atış tek kuyu üretmeli.");

                float t0 = Time.realtimeSinceStartup;
                while (TideWell.LiveCount > 0)
                {
                    Assert.Less(Time.realtimeSinceStartup - t0, SpecialWeaponSahne.FazZamanAsimi, "Kuyu kapanmadı.");
                    yield return null;
                }

                // Implode karesinden sonra sayaç End/CloseAll'a kadar durur (Despawn sıfırlamaz).
                int pullTicks = TideWell.TotalPullTicks;
                yield return null;

                var log = new StringBuilder("[DD-TEST] parity.tide round=").Append(round).Append(" ticksExpected=").Append(tickBeklenen).Append(' ');
                float toplam = 0f;
                int pullToplam = 0;
                int implodeToplam = 0;
                for (int i = 0; i < ZombiSayisi; i++)
                {
                    int life = zombiler[i].LifeId;
                    int pull = 0, implode = 0;
                    float zombiToplam = 0f, implodeMiktar = 0f;
                    for (int e = 0; e < sayac.Damaged.Count; e++)
                    {
                        EnemyDamageInfo info = sayac.Damaged[e];
                        if (info.LifeId != life)
                        {
                            continue;
                        }

                        zombiToplam += Miktar(info);
                        if (info.Meta.Kind == DamageKind.Explosion)
                        {
                            implode++;
                            implodeMiktar += Miktar(info);
                        }
                        else
                        {
                            pull++;
                            Assert.AreEqual(DamageSourceKind.Explosion, info.Source, "U-1 karakterizasyon: pull kaynağı Explosion.");
                            Assert.AreEqual(DamageKind.Direct, info.Meta.Kind, "U-1 karakterizasyon: pull doğrudan yol → Kind=Direct.");
                            Assert.AreEqual(t.PullDamage, Miktar(info), t.PullDamage * Tolerans, "Pull miktarı config'ten farklı.");
                        }
                    }

                    Assert.GreaterOrEqual(pull, 1, "Zombi " + i + " hiç çekilmedi.");
                    Assert.LessOrEqual(pull, tickBeklenen, "Zombi " + i + " tick sayısından fazla çekildi.");
                    Assert.AreEqual(1, implode, "Zombi " + i + " implode tam bir kez almalı.");
                    Assert.IsTrue(zombiler[i].IsAlive, "Zombi " + i + " öldü: formül geçersiz (öncül).");
                    float beklenen = pull * t.PullDamage + implodeMiktar;
                    Assert.AreEqual(beklenen, zombiToplam, beklenen * Tolerans, "Zombi " + i + " toplam miktarı formüle uymuyor.");
                    toplam += zombiToplam;
                    pullToplam += pull;
                    implodeToplam += implode;
                    log.Append("z").Append(i).Append("=p").Append(pull).Append("/i").Append(implode).Append('/').Append(zombiToplam.ToString("0.00")).Append(' ');
                }

                Assert.AreEqual(pullToplam, pullTicks, "Σ pull_i ≠ TotalPullTicks.");
                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dupOnce, "Duplicate reddi oluştu.");
                Assert.AreEqual(0, DamageTransaction.NestedBegins - nestedOnce, "NestedBegins arttı.");
                log.Append("pullTicks=").Append(pullTicks).Append(" implodes=").Append(implodeToplam).Append(" events=").Append(sayac.Damaged.Count)
                   .Append(" total=").Append(toplam.ToString("0.00"))
                   .Append(" dupDelta=").Append(ZombieEnemy.DuplicateApplicationCount - dupOnce)
                   .Append(" nestedDelta=").Append(DamageTransaction.NestedBegins - nestedOnce)
                   .Append(" txDistinct=").Append(FarkliTransaction(sayac.Damaged, 0, sayac.Damaged.Count))
                   .Append(" txLastDelta=").Append(DamageTransaction.Last - lastOnce);
                Debug.Log(log.ToString());
            }
        }
    }
}
