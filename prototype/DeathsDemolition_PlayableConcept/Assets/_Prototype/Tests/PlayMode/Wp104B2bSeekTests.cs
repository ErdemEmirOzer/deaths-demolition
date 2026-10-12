using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B2b · ağacın son üç ateş yolu düğümü, Run sahnesinde gerçek mermiyle:
    /// - Seeker Swarm (sh_cap, ExtraShotSeekDegrees 25°): ek volley atışı konideki gövdeye kilitlenip döner ve vurur; ana atış
    ///   düz gider; nötr ağaçta ek atış da düz gider.
    /// - Bouncing Storm (sh_storm, SeekDegrees 15°): sekme yarıçapında kimse yoksa koni içinde 2 × yarıçapa bakar; nötrde sekmez.
    /// - Rail Line (sh_rail, PierceStaggerSeconds 0,2): mermi deldiği gövdeyi S-03'ün ağaç sendelemesiyle (ApplyTreeStagger) sendeletir;
    ///   durduğu gövdeyi değil; nötrde hiç.
    /// Hedefler zırhsız ve donuk (Update + Animator kapalı, teardown'da canlı geri verilir — ProjectileSliceTests yöntemi).
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B2bSeekTests
    {
        private SpecialWeaponSahne _s;
        private readonly List<ZombieEnemy> _donuk = new List<ZombieEnemy>(4);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            ProjectileSystem.Clear();
            TreeRuntime.ClearTestStats();
            foreach (ZombieEnemy z in _donuk)
            {
                if (z == null) continue;
                z.enabled = true;
                foreach (Animator a in z.GetComponentsInChildren<Animator>(true)) a.enabled = true;
            }

            _donuk.Clear();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur(params Vector2[] konumlar)
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            LogAssert.ignoreFailingMessages = true;
            var yeni = new List<ZombieEnemy>(konumlar.Length);
            foreach (Vector2 k in konumlar)
            {
                ZombieEnemy z = _s.Dogur(_s.Zemin(k.x, k.y));
                z.CompleteRise();
                z.FitArmorComposition(false, false, false);
                yeni.Add(z);
            }

            yield return null;
            yield return null;
            foreach (ZombieEnemy z in yeni)
            {
                z.enabled = false;
                foreach (Animator a in z.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                _donuk.Add(z);
            }

            yield return new WaitForSeconds(1.5f);
        }

        private static Vector3 Govde(ZombieEnemy z)
        {
            Collider torso = z.HitboxFor(BodyRegion.UpperBody);
            return torso != null ? torso.bounds.center : z.transform.position + Vector3.up;
        }

        private static ShotRequest Atis(Vector3 from, Vector3 direction, int volley, int pierce = 0)
        {
            return new ShotRequest
            {
                Origin = from, Direction = direction.normalized, Damage = 2f, Range = 30f,
                ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                PierceDamageFalloff = 1f, PierceTargets = pierce, VolleyIndex = volley
            };
        }

        private static ProjectileLaunch Firlat()
        {
            return new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Caliber = WeaponCaliber.Medium,
                Speed = 80f, Gravity = 0f, MaxRange = 30f, MaxLifetime = 5f,
                FalloffStart = 30f, FalloffEnd = 30f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
        }

        private static IEnumerator Ates(ShotRequest shot)
        {
            ProjectileSystem.Spawn(shot, Firlat());
            yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 3f, "mermi bitmedi");
            yield return null;
        }

        private static void Kullan(float extraSeek, float bounceSeek, float stagger)
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShotSeekDegrees = extraSeek;
            s.SeekDegrees = bounceSeek;
            s.PierceStaggerSeconds = stagger;
            TreeRuntime.SetTestStats(s);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SeekerSwarm_EkAtisKonidekiGovdeyeDoner_AnaAtisVeNotrDuzGider()
        {
            yield return Kur(new Vector2(2.5f, 12f));   // 2,5 m yanda, 12 m önde: çizgiden ≈ 12°
            ZombieEnemy z = _donuk[0];
            Vector3 hedef = Govde(z);
            Vector3 from = hedef + new Vector3(-2.5f, 0f, -12f);
            Vector3 ileri = Vector3.forward;

            Kullan(25f, 0f, 0f);
            float hp = z.Health;
            yield return Ates(Atis(from, ileri, 0));
            Assert.AreEqual(hp, z.Health, 1e-4f, "ana atış (VolleyIndex 0) hedef aramaz: 2,5 m yandan geçer");

            int kilit = ProjectileSystem.SeekLocks;
            yield return Ates(Atis(from, ileri, 1));
            Assert.AreEqual(kilit + 1, ProjectileSystem.SeekLocks, "ek atış koni içindeki gövdeye kilitlenir");
            Assert.Less(z.Health, hp, "ek atış dönüp gövdeyi vurur");

            TreeRuntime.SetTestStats(TreeStats.Neutral);
            hp = z.Health;
            yield return Ates(Atis(from, ileri, 1));
            Assert.AreEqual(hp, z.Health, 1e-4f, "nötr ağaç: ek atış da düz gider");

            Kullan(10f, 0f, 0f);
            yield return Ates(Atis(from, ileri, 1));
            Assert.AreEqual(hp, z.Health, 1e-4f, "koni dışındaki gövde (12° > 10°) seçilmez");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator BouncingStorm_YaricaptaKimseYokkenKonideIkiKatUzagaSeker_NotrdeSekmez()
        {
            yield return Kur(new Vector2(0f, 10f), new Vector2(0.8f, 15f));   // B, A'nın 5 m ötesinde, yönün ≈ 9° yanında
            ZombieEnemy a = _donuk[0], b = _donuk[1];
            Vector3 from = Govde(a) + new Vector3(0f, 0f, -10f);
            ShotRequest shot = Atis(from, Vector3.forward, 0);
            shot.RicochetChance = 1f;
            shot.RicochetBounces = 1;
            shot.RicochetRadius = 3f;   // B 3 m'nin dışında, SeekReach × 3 = 6 m'nin içinde
            shot.RicochetDamageScale = 0.5f;

            TreeRuntime.SetTestStats(TreeStats.Neutral);
            float hpA = a.Health, hpB = b.Health;
            yield return Ates(shot);
            Assert.Less(a.Health, hpA, "mermi A'yı vurur");
            Assert.AreEqual(hpB, b.Health, 1e-4f, "nötr ağaç: yarıçapta kimse yok, sekme yok");

            Kullan(0f, 15f, 0f);
            int sek = HitscanEngine.SeekBounces;
            hpB = b.Health;
            yield return Ates(shot);
            Assert.Less(b.Health, hpB, "arama açık: sekme konideki B'ye ulaşır");
            Assert.Greater(HitscanEngine.SeekBounces, sek);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RailLine_DelinenGovdeSendeler_DuranVeNotrSendelemez()
        {
            yield return Kur(new Vector2(0f, 8f), new Vector2(0f, 12f));
            ZombieEnemy a = _donuk[0];
            Vector3 from = Govde(a) + new Vector3(0f, 0f, -8f);

            TreeRuntime.SetTestStats(TreeStats.Neutral);
            int s0 = HitscanEngine.PierceStaggers;
            yield return Ates(Atis(from, Vector3.forward, 0, 1));
            Assert.AreEqual(s0, HitscanEngine.PierceStaggers, "nötr ağaç: sendeleme yok");

            Kullan(0f, 0f, 0.2f);
            yield return Ates(Atis(from, Vector3.forward, 0, 1));
            Assert.AreEqual(s0 + 1, HitscanEngine.PierceStaggers, "delinen A sendeler, mermi B'de durur ve B sendelemez");
        }
    }
}
