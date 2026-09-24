using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-069 B4 · the two Specials on the live scene (DIRECTION §7a / §7b, DEC-097). The orb / blob is launched straight
    /// into the ProjectileSystem (no cursor aiming), so every assertion is about what lands: Raiju's chain (hop order,
    /// one tremble per body, the lethal damage at the END of the tremble, armour ignored, one transaction, the Ultra's
    /// 60 % share), the ground fizzle, the self-shock; Namekuji's direct hit (liquefy / the Executioner's 25 % + knock-down),
    /// its puddles on S-03's GelPuddleRegistry (impact, wall → floor, expiry, run end), the slip outcome (death + the chain
    /// puddle / the Executioner's 20 % + immunity) and the player's slide; and the controller path (charge → orb, early
    /// release cancels, the usage bus). Replaces the WP-007 / WP-056 Kagura / Ofuda / Tide suites.
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class SpecialWeaponV3SliceTests
    {
        private SpecialWeaponSahne _s;
        private readonly List<SpecialUsageEvent> _events = new List<SpecialUsageEvent>(64);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialImpacts.PlayerOverride = null;
            _events.Clear();
            ProjectileSystem.Clear();
            SpecialImpacts.Clear();
            SpecialGelEffects.ResetForTests();
            GelPuddleRegistry.ResetStatics();
            if (_s != null && _s.Girdi != null) _s.Girdi.Yon = Vector2.zero;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            SpecialGelEffects.ResetForTests();
            GelPuddleRegistry.ResetStatics();
            _events.Clear();
            SpecialWeaponUsageEvents.Usage += OnUsage;
        }

        private void OnUsage(SpecialUsageEvent e) => _events.Add(e);

        private int Count(SpecialUsageKind kind, SpecialWeaponId id)
        {
            int n = 0;
            for (int i = 0; i < _events.Count; i++) if (_events[i].Kind == kind && _events[i].Id == id) n++;
            return n;
        }

        private SpecialUsageEvent? Find(SpecialUsageKind kind, SpecialWeaponId id, float radius)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Kind == kind && _events[i].Id == id && Mathf.Abs(_events[i].Radius - radius) < 0.01f) return _events[i];
            }

            return null;
        }

        private ZombieEnemy Spawn(EnemyTier tier, Vector3 at)
        {
            Vector3 d = _s.Rig.transform.position - at;
            d.y = 0f;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, at, Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward, Vector3.up));
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        private static Vector3 Torso(ZombieEnemy z)
        {
            Collider c = z.HitboxFor(BodyRegion.UpperBody);
            return c != null ? c.bounds.center : z.transform.position + Vector3.up * 1.2f;
        }

        /// <summary>Launch one Special projectile from <paramref name="from"/> at <paramref name="at"/> (the definition's numbers, no aiming).</summary>
        private static void Launch(SpecialWeaponId id, Vector3 from, Vector3 at)
        {
            WeaponDefinition def = SpecialWeaponConfigSection.DefinitionOf(id);
            Assert.IsNotNull(def, id + " tanımı yok");
            var shot = new ShotRequest
            {
                Origin = from, Direction = (at - from).normalized, Damage = def.fire.damage, Range = def.projectile.maxRange,
                ImpactForce = def.projectile.impulse, Source = DamageSourceKind.Chain, Power = WeaponPower.Heavy, PierceDamageFalloff = 1f
            };
            ProjectileSystem.Spawn(shot, ProjectileLaunch.From(def, SpecialWeaponRuntime.ProjectileWeaponId(id), def.projectile.maxRange));
        }

        // ------------------------------------------------------------------ Raiju

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Raiju_Zincir_TitremeSonundaOlum_ZirhYokSayilir_TekIslem()
        {
            yield return Kur();
            var bodies = new List<ZombieEnemy>
            {
                Spawn(EnemyTier.Standard, _s.Zemin(0f, 8f)),
                Spawn(EnemyTier.Standard, _s.Zemin(0f, 11f)),
                Spawn(EnemyTier.Standard, _s.Zemin(0f, 14f)),
                Spawn(EnemyTier.Standard, _s.Zemin(3f, 16f))
            };
            bodies[1].FitArmorComposition(true, true, true);   // a full plate set: electricity passes it
            yield return new WaitForSeconds(1.5f);

            int nested = DamageTransaction.NestedBegins;
            int dup = ZombieEnemy.DuplicateApplicationCount;
            int shocks = ZombieEnemy.ShocksApplied;
            int selfShocks = SpecialImpacts.SelfShocks;
            float firstHitAt = -1f, firstDeathAt = -1f;
            var died = new List<int>();
            System.Action<EnemyDeathInfo> onDied = i => { died.Add(i.LifeId); if (firstDeathAt < 0f) firstDeathAt = Time.time; };
            DamageEvents.EnemyDied += onDied;
            try
            {
                Vector3 from = _s.Rig.transform.position + Vector3.up * 1.39f;
                Launch(SpecialWeaponId.Raiju, from, Torso(bodies[0]));
                yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Hit, SpecialWeaponId.Raiju) >= 1, 3f, "küre çarpmadı");
                firstHitAt = Time.time;
                yield return SpecialWeaponSahne.Bekle(() => died.Count >= 4 || (SpecialImpacts.ActiveChains == 0 && died.Count > 0), 6f, "zincir bitmedi (ölü " + died.Count + ")");
                yield return SpecialWeaponSahne.Bekle(() => SpecialImpacts.ActiveChains == 0, 3f, "zincir kaydı kapanmadı");

                Debug.Log("[DD-TEST] wp069.b4.chain hits=" + Count(SpecialUsageKind.Hit, SpecialWeaponId.Raiju) + " died=" + died.Count +
                          " shocks=" + (ZombieEnemy.ShocksApplied - shocks) + " firstDeathAfter=" + (firstDeathAt - firstHitAt).ToString("0.00") + " s");
                Assert.AreEqual(4, Count(SpecialUsageKind.Hit, SpecialWeaponId.Raiju), "dört halka (3 + 3 + 3,6 m sıçrama ≤ 6 m)");
                Assert.AreEqual(4, died.Count, "Standard'lar ölür; zırhlı olan dahil (elektrik zırhı yok sayar)");
                Assert.GreaterOrEqual(ZombieEnemy.ShocksApplied - shocks, 4, "her halkaya titreme");
                Assert.GreaterOrEqual(firstDeathAt - firstHitAt, 1.0f, "ölüm titremenin SONUNDA (1,2 s)");
                int hop = 0;
                for (int i = 0; i < _events.Count; i++)
                {
                    if (_events[i].Kind != SpecialUsageKind.Hit || _events[i].Id != SpecialWeaponId.Raiju) continue;
                    Assert.AreEqual(hop++, _events[i].Count, "Hit.Count = sıçrama sırası");
                    Assert.IsTrue(_events[i].Target.IsValid);
                }

                Assert.AreEqual(0, DamageTransaction.NestedBegins - nested, "iç içe işlem yok");
                Assert.AreEqual(0, ZombieEnemy.DuplicateApplicationCount - dup, "gövde başına tek uygulama");
                Assert.AreEqual(selfShocks, SpecialImpacts.SelfShocks, "8 m uzakta oyuncu çarpılmaz");
                Assert.AreEqual(1, Count(SpecialUsageKind.FieldEnded, SpecialWeaponId.Raiju), "küre uçuşu temasta biter");
            }
            finally
            {
                DamageEvents.EnemyDied -= onDied;
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Raiju_Ultra_Yuzde60_Sersemler_ZincirOndanSurer()
        {
            yield return Kur();
            ZombieEnemy ultra = Spawn(EnemyTier.Ultra, _s.Zemin(0f, 9f));
            ZombieEnemy after = Spawn(EnemyTier.Standard, _s.Zemin(0f, 13f));
            yield return new WaitForSeconds(1.5f);
            float max = ultra.MaxHealth;

            Launch(SpecialWeaponId.Raiju, _s.Rig.transform.position + Vector3.up * 1.39f, Torso(ultra));
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Hit, SpecialWeaponId.Raiju) >= 2, 3f, "zincir Ultra'dan sürmedi");
            yield return SpecialWeaponSahne.Bekle(() => !after.IsAlive, 4f, "Ultra'dan sonraki halka ölmedi");

            Debug.Log("[DD-TEST] wp069.b4.ultra hp " + ultra.Health.ToString("0") + " / " + max.ToString("0") + " electrocuted=" + ultra.IsElectrocuted);
            Assert.IsTrue(ultra.IsAlive, "Ultra ölmez (🔴 #2: %60)");
            Assert.AreEqual(max * 0.4f, ultra.Health, max * 0.02f, "Ultra azami HP'nin %60'ını kaybeder");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Raiju_YereCarpanKure_Soner_ZincirYok()
        {
            yield return Kur();
            int fizzles = SpecialImpacts.Fizzles;
            Vector3 from = _s.Rig.transform.position + Vector3.up * 1.39f;
            Launch(SpecialWeaponId.Raiju, from, _s.Rig.transform.position + new Vector3(0f, -0.5f, 3f));
            yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 3f, "küre sönmedi");
            yield return null;
            Assert.AreEqual(fizzles + 1, SpecialImpacts.Fizzles, "yer teması = sönme");
            Assert.AreEqual(0, SpecialImpacts.ActiveChains);
            Assert.AreEqual(0, Count(SpecialUsageKind.Hit, SpecialWeaponId.Raiju), "zincir yok");
            Assert.AreEqual(1, Count(SpecialUsageKind.FieldEnded, SpecialWeaponId.Raiju));
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Raiju_YakinZincir_OyuncuCarpilir_Bir_Kez()
        {
            yield return Kur();
            ZombieEnemy near = Spawn(EnemyTier.Standard, _s.Zemin(0f, 1.3f));
            yield return new WaitForSeconds(1.5f);
            var health = _s.Rig.GetComponentInChildren<DD.Prototype.PlayerSystem.PlayerHealth>();
            Assert.IsNotNull(health);
            int selfShocks = SpecialImpacts.SelfShocks;

            // The kit keeps the player invulnerable; the shock is measured on its own source id (sw_arc), not on a
            // swing from the body next to the player.
            float shock = 0f;
            int shockHits = 0;
            System.Action<DD.Prototype.PlayerSystem.PlayerDamageInfo> onDamaged = i =>
            {
                if (i.SourceId != SpecialWeaponKeys.Raiju) return;
                shockHits++;
                shock += i.AppliedAmount;
            };
            health.DiagnosisInvulnerable = false;
            health.Damaged += onDamaged;
            try
            {
                Launch(SpecialWeaponId.Raiju, _s.Rig.transform.position + Vector3.up * 1.39f + Vector3.back * 0.2f, Torso(near));
                yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.SelfShock, SpecialWeaponId.Raiju) >= 1, 3f, "oyuncu çarpılmadı");
                yield return SpecialWeaponSahne.Kareler(5);
            }
            finally
            {
                health.Damaged -= onDamaged;
                health.DiagnosisInvulnerable = true;
            }

            Assert.AreEqual(selfShocks + 1, SpecialImpacts.SelfShocks, "atış başına bir kez");
            Assert.AreEqual(1, shockHits, "tek çarpma hasarı");
            Assert.AreEqual(40f, shock, 0.5f, "ilk hedef ≤ 1,5 m → 40 HP");
        }

        // ------------------------------------------------------------------ Namekuji

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Namekuji_DogrudanIsabet_StandardHemenOlur()
        {
            yield return Kur();
            ZombieEnemy z = Spawn(EnemyTier.Standard, _s.Zemin(0f, 6f));
            yield return new WaitForSeconds(1.5f);
            int gelHits = SpecialImpacts.GelHits;
            Launch(SpecialWeaponId.Namekuji, _s.Rig.transform.position + Vector3.up * 1.39f, Torso(z) + Vector3.up * 0.3f);
            yield return SpecialWeaponSahne.Bekle(() => !z.IsAlive, 3f, "jel isabeti öldürmedi");
            Assert.AreEqual(gelHits + 1, SpecialImpacts.GelHits);
            Assert.AreEqual(1, Count(SpecialUsageKind.Hit, SpecialWeaponId.Namekuji));
            Assert.AreEqual(1, Count(SpecialUsageKind.Liquefied, SpecialWeaponId.Namekuji), "sıvılaşma olayı");
            SpecialUsageEvent? puddle = Find(SpecialUsageKind.FieldSpawned, SpecialWeaponId.Namekuji, 1.6f);
            Assert.IsTrue(puddle.HasValue, "isabet noktasında r 1,6 m birikinti");
            Assert.AreEqual(25f, puddle.Value.Seconds, 0.01f, "25 s");
            Assert.IsTrue(GelPuddleRegistry.TryGet(puddle.Value.Count, out GelPuddleInfo info), "Count = birikinti kimliği");
            Assert.Less(Mathf.Abs(info.SurfaceY - z.transform.position.y), 0.6f, "birikinti gövdenin ayağında, göğsünde değil");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Namekuji_DogrudanIsabet_UltraYuzde25_Devrilir()
        {
            yield return Kur();
            ZombieEnemy u = Spawn(EnemyTier.Ultra, _s.Zemin(0f, 8f));
            yield return new WaitForSeconds(1.5f);
            float max = u.MaxHealth;
            int knockdowns = SpecialImpacts.Knockdowns;
            Launch(SpecialWeaponId.Namekuji, _s.Rig.transform.position + Vector3.up * 1.39f, Torso(u) + Vector3.up * 0.5f);
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Hit, SpecialWeaponId.Namekuji) >= 1, 3f, "jel Ultra'ya çarpmadı");
            yield return null;

            Debug.Log("[DD-TEST] wp069.b4.gelUltra hp " + u.Health.ToString("0") + " / " + max.ToString("0") + " gel=" + u.Gel);
            Assert.IsTrue(u.IsAlive, "Executioner doğrudan isabette ölmez");
            Assert.AreEqual(max * 0.75f, u.Health, max * 0.02f, "azami HP'nin %25'i");
            Assert.AreEqual(knockdowns + 1, SpecialImpacts.Knockdowns, "devrilme 1,5 s");
            Assert.AreNotEqual(GelState.None, u.Gel, "devrilmiş (Falling / Downed)");
            Assert.AreEqual(0, Count(SpecialUsageKind.Liquefied, SpecialWeaponId.Namekuji));
            Assert.AreEqual(1, GelPuddleRegistry.Count, "gövdenin altında birikinti");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Namekuji_Kayma_StandardOlur_OlumBirikintisi_UltraYuzde20Bagisik()
        {
            yield return Kur();
            ZombieEnemy z = Spawn(EnemyTier.Standard, _s.Zemin(0f, 11f));
            yield return new WaitForSeconds(1.5f);
            int kills = SpecialGelEffects.SlipKills, deathPuddles = SpecialGelEffects.DeathPuddles;

            // A puddle on the body's way to the player: it slides, falls after 0.9 s and dies (every round), leaving r 0.9 m / 8 s.
            Vector3 mid = Vector3.Lerp(z.transform.position, _s.Rig.transform.position, 0.5f);
            Assert.Greater(SpecialGelEffects.MakePuddle(0, mid, Vector3.up, 1.6f, 25f), 0);
            yield return SpecialWeaponSahne.Bekle(() => !z.IsAlive, 12f, "kayan Standard ölmedi (kayma " + ZombieEnemy.SlidesStarted + ")");
            Debug.Log("[DD-TEST] wp069.b4.slip slides=" + ZombieEnemy.SlidesStarted + " falls=" + ZombieEnemy.SlipFalls + " kills=" + (SpecialGelEffects.SlipKills - kills));
            Assert.AreEqual(kills + 1, SpecialGelEffects.SlipKills, "kayma ölümü S-02 sonucundan");
            Assert.AreEqual(deathPuddles + 1, SpecialGelEffects.DeathPuddles, "ölüm birikintisi (zincir)");
            SpecialUsageEvent? chain = Find(SpecialUsageKind.FieldSpawned, SpecialWeaponId.Namekuji, 0.9f);
            Assert.IsTrue(chain.HasValue, "r 0,9 m birikinti olayı");
            Assert.AreEqual(8f, chain.Value.Seconds, 0.01f, "8 s");

            // The Executioner: 20 % and immune once it is up (standing here: at once).
            ZombieEnemy u = Spawn(EnemyTier.Ultra, _s.Zemin(5f, 14f));
            yield return new WaitForSeconds(1.5f);
            float max = u.MaxHealth, hp = u.Health;
            int shares = SpecialGelEffects.SlipShares;
            SpecialGelEffects.ApplySlipOutcome(u, 0);
            Assert.IsTrue(u.IsAlive);
            Assert.AreEqual(hp - max * 0.2f, u.Health, max * 0.02f, "azami HP'nin %20'si");
            Assert.AreEqual(shares + 1, SpecialGelEffects.SlipShares);
            Assert.IsTrue(u.IsGelImmune, "3 s bağışık");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Namekuji_YerTemasi_Birikinti_SureSonu_KosuSonu()
        {
            yield return Kur();
            int fizzles = SpecialImpacts.Fizzles;
            Launch(SpecialWeaponId.Namekuji, _s.Rig.transform.position + Vector3.up * 1.39f, _s.Rig.transform.position + new Vector3(0f, -0.5f, 3f));
            yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 3f, "jel yere inmedi");
            yield return null;
            Assert.AreEqual(fizzles, SpecialImpacts.Fizzles, "yer teması sönme değil, birikinti");
            Assert.AreEqual(1, Count(SpecialUsageKind.FieldSpawned, SpecialWeaponId.Namekuji));
            Assert.AreEqual(1, GelPuddleRegistry.Count);

            // Expiry → FieldEnded with the puddle's id.
            int shortLived = SpecialGelEffects.MakePuddle(0, _s.Zemin(8f, 0f), Vector3.up, 1f, 0.3f);
            yield return new WaitForSeconds(0.5f);
            yield return null;
            bool ended = false;
            for (int i = 0; i < _events.Count; i++) ended |= _events[i].Kind == SpecialUsageKind.FieldEnded && _events[i].Count == shortLived;
            Assert.IsTrue(ended, "süresi dolan birikinti FieldEnded (Count = kimlik)");

            // Run end (scene unload path): every puddle ends, none carries into the next run.
            SpecialImpacts.Clear();
            Assert.AreEqual(0, GelPuddleRegistry.Count, "koşu sonunda birikinti kalmaz");
            Assert.AreEqual(2, Count(SpecialUsageKind.FieldEnded, SpecialWeaponId.Namekuji));
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Oyuncu_Birikintide_Kayar_Hizli_Cikista03sNormal()
        {
            yield return Kur();
            DD.Prototype.PlayerSystem.PlayerMotor m = _s.Rig.Motor;
            Assert.IsNotNull(m);

            _s.Girdi.Yon = new Vector2(1f, 0f);
            float normalPeak = 0f;
            for (float t = 0f; t < 1f; t += Time.deltaTime) { yield return null; normalPeak = Mathf.Max(normalPeak, m.PlanarVelocity.magnitude); }
            _s.Girdi.Yon = Vector2.zero;
            yield return new WaitForSeconds(0.5f);

            int id = SpecialGelEffects.MakePuddle(0, m.transform.position, Vector3.up, 7f, 25f);
            yield return null;
            yield return null;
            Assert.IsTrue(m.InGel, "oyuncu birikintide");
            Assert.AreEqual(1, Count(SpecialUsageKind.PlayerSlideStarted, SpecialWeaponId.Namekuji));

            _s.Girdi.Yon = new Vector2(-1f, 0f);
            float gelPeak = 0f;
            for (float t = 0f; t < 1.2f && m.InGel; t += Time.deltaTime) { yield return null; gelPeak = Mathf.Max(gelPeak, m.PlanarVelocity.magnitude); }
            _s.Girdi.Yon = Vector2.zero;
            Debug.Log("[DD-TEST] wp069.b4.playerSlide normalPeak=" + normalPeak.ToString("0.00") + " gelPeak=" + gelPeak.ToString("0.00"));
            Assert.Greater(gelPeak, normalPeak * 1.15f, "jelde hız ×1,8 hedefi");

            GelPuddleRegistry.Expire(id);
            yield return null;
            yield return null;
            Assert.IsFalse(m.InGel);
            Assert.AreEqual(1, Count(SpecialUsageKind.PlayerSlideEnded, SpecialWeaponId.Namekuji));
            Assert.Greater(m.Gel01, 0f, "çıkışta 0,3 s rampa");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0f, m.Gel01, "0,3 s sonra normal");
        }

        // ------------------------------------------------------------------ controller path

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Kontrolcu_RaijuSarj_KureCikar_ErkenBirakmaIptal()
        {
            yield return Kur();
            _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.Raiju);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "el modeli");
            Assert.AreEqual(3, _s.Controller.Special.Ammo);
            Assert.AreEqual(_s.Special.CapSeconds(SpecialWeaponId.Raiju), _s.Controller.Special.Duration, 1e-3f);

            // Early release: the charge cancels, no ammo spent.
            _s.Girdi.AtesBasili = true;
            _s.Girdi.AtesBasti = true;
            yield return null;
            _s.Girdi.AtesBasti = false;
            yield return null;
            _s.Girdi.AtesBasili = false;
            yield return SpecialWeaponSahne.Kareler(3);
            Assert.GreaterOrEqual(Count(SpecialUsageKind.ChargeCancelled, SpecialWeaponId.Raiju), 1, "erken bırakma iptal");
            Assert.AreEqual(3, _s.Controller.Special.Ammo, "iptal mermi harcamaz");

            // Hold through the charge: one orb leaves.
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Fired, SpecialWeaponId.Raiju) >= 1, 2f, "şarj sonrası ateş yok");
            _s.Girdi.AtesBasili = false;
            Assert.AreEqual(2, _s.Controller.Special.Ammo);
            Assert.AreEqual(1, Count(SpecialUsageKind.FieldSpawned, SpecialWeaponId.Raiju), "küre uçuşu başladı");
            Assert.AreEqual(3, WeaponEvents.Special.MagazineSize, "HUD şarjör");
            Assert.AreEqual(9, WeaponEvents.Special.ReserveAmmo, "HUD yedek");
        }
    }
}
