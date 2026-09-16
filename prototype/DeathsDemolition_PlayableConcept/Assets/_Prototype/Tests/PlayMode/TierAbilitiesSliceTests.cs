using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-040 on real bodies in the Run scene: (1) DEC-081 hybrid rise — an off-screen spawn rises
    /// and lands, an on-screen / unjudged one walks at once; the director judges visibility with
    /// the live camera; (2) ADR-040-1 beats in order for a swing (telegraph → commit → hit | miss),
    /// an armour break, a heavy stagger and a death, all stamped with the life id; (3) the Elite
    /// profile reaches the follower (corridor closing); (4) the Special cooldown floor on a live body.
    /// </summary>
    [Category("WP040")]
    public sealed class TierAbilitiesSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private readonly List<EnemyCombatEventInfo> _beats = new List<EnemyCombatEventInfo>();

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            TierBehaviourProfileSet.ResetStatics();
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            EnemyCombatEvents.ResetStatics();
            _beats.Clear();
            EnemyCombatEvents.Raised += Kaydet;
            yield return null;
        }

        private void Kaydet(EnemyCombatEventInfo i) => _beats.Add(i);

        [TearDown]
        public void TearDown()
        {
            EnemyCombatEvents.Raised -= Kaydet;
            EnemyCombatEvents.ResetStatics();
            ZombieEnemy.ResetRisePolicy();
            TierBehaviourProfileSet.ResetStatics();
            ThreatDirector.ContextOverride = null;
            Time.captureDeltaTime = 0f;
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private Vector3 Zemin(float x, float z)
        {
            Vector3 p = _rig.transform.position + new Vector3(x, 0f, z);
            p.y = _rig.transform.position.y;
            return p;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 konum, SpawnVisibility gorunurluk)
        {
            Vector3 d = _rig.transform.position - konum; d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 8, konum, bakis, gorunurluk);
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        private int Say(EnemyCombatEventKind kind, int lifeId)
        {
            int n = 0;
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].Kind == kind && _beats[i].LifeId == lifeId) n++;
            return n;
        }

        private static ShotResult Vur(ZombieEnemy z, BodyRegion bolge, float hasar)
        {
            Collider hitbox = z.HitboxFor(bolge);
            Assert.IsNotNull(hitbox, "Rig'de " + bolge + " hitbox'ı yok.");
            Vector3 hedef = hitbox.bounds.center;
            Vector3 geri = -z.transform.forward; geri.y = 0f;
            geri = geri.sqrMagnitude > 0.0001f ? geri.normalized : Vector3.back;
            Vector3 kaynak = hedef - geri * 2.5f; kaynak.y = hedef.y;
            var istek = new ShotRequest
            {
                Origin = kaynak, Direction = (hedef - kaynak).normalized, Damage = hasar, Range = 6f,
                ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                PierceTargets = 0, PierceDamageFalloff = 1f, DrawTracer = false
            };
            return HitscanEngine.Fire(in istek);
        }

        // ================================================================= DEC-081 rise

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MelezKalkis_KameraDisiKalkar_KameraIciYurur_YargiKamerayla()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.OffScreenOnly;

            // the director judges with the live camera: in front = OnScreen, behind = OffScreen
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "Run sahnesinde ana kamera yok");
            Vector3 onde = cam.transform.position + cam.transform.forward * 6f;
            Vector3 arkada = cam.transform.position - cam.transform.forward * 6f;
            Assert.AreEqual(SpawnVisibility.OnScreen, _director.JudgeVisibility(onde), "kameranın önü → OnScreen");
            Assert.AreEqual(SpawnVisibility.OffScreen, _director.JudgeVisibility(arkada), "kameranın arkası → OffScreen");
            // the margin band: just past the frame edge still counts as visible (no rise into a pan)
            Vector3 kenar = cam.ViewportToWorldPoint(new Vector3(1f + EnemySpawnDirector.RiseViewMargin * 0.5f, 0.5f, 6f)) - Vector3.up * 0.9f;
            Assert.AreEqual(SpawnVisibility.OnScreen, _director.JudgeVisibility(kenar), "kenar bandı içi → OnScreen (histerezis)");
            Vector3 disi = cam.ViewportToWorldPoint(new Vector3(1f + EnemySpawnDirector.RiseViewMargin * 2f, 0.5f, 6f)) - Vector3.up * 0.9f;
            Assert.AreEqual(SpawnVisibility.OffScreen, _director.JudgeVisibility(disi), "bandın dışı → OffScreen");

            ZombieEnemy dis = Dogur(EnemyTier.Standard, Zemin(-3f, 9f), SpawnVisibility.OffScreen);
            ZombieEnemy ic = Dogur(EnemyTier.Standard, Zemin(0f, 9f), SpawnVisibility.OnScreen);
            ZombieEnemy bilinmez = Dogur(EnemyTier.Standard, Zemin(3f, 9f), SpawnVisibility.Unknown);
            yield return null;

            Assert.IsTrue(dis.RoseOnSpawn, "kamera dışı spawn kalkar");
            Assert.IsFalse(ic.RoseOnSpawn, "kamera içi spawn kalkmaz");
            Assert.IsFalse(bilinmez.RoseOnSpawn, "yargısız (debug) spawn kalkmaz");
            Assert.AreEqual(SpawnVisibility.OffScreen, dis.SpawnVisibilityClass);
            Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnLand, ic.LifeId), "kamera içi: hemen spawn_land");
            Assert.AreEqual(0, Say(EnemyCombatEventKind.SpawnRise, ic.LifeId));
            Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnLand, bilinmez.LifeId));

            bool skinned = ((ZombieAppearance)dis.VersionVisual).IsSkinned;
            if (skinned)
            {
                Assert.IsTrue(dis.IsRising, "gerçek gövde: rise klibi oynuyor");
                Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnRise, dis.LifeId), "spawn_rise vuruşu");
                Assert.AreEqual(0, Say(EnemyCombatEventKind.SpawnLand, dis.LifeId), "kalkarken land yok");
                float t0 = Time.time;
                yield return Bekle(() => !dis.IsRising, 4f, "rise_end gelmedi (1,57 s klip)");
                Assert.Greater(Time.time - t0, 0.5f, "kalkış anlık bitmez");
                yield return null;
                Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnLand, dis.LifeId), "rise_end → spawn_land");
            }
            else
            {
                // block body: no clip to play — the policy still records the decision, the land beat is immediate
                Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnLand, dis.LifeId));
                Debug.Log("[DD-OBS] wp040.rise block body (no skinned rig) — rise clip not exercised here");
            }

            // policy Off / Always still honoured on the same route
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy kapali = Dogur(EnemyTier.Standard, Zemin(-6f, 9f), SpawnVisibility.OffScreen);
            Assert.IsFalse(kapali.RoseOnSpawn, "Off: kamera dışı bile kalkmaz");
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Always;
            ZombieEnemy hep = Dogur(EnemyTier.Standard, Zemin(6f, 9f), SpawnVisibility.OnScreen);
            Assert.IsTrue(hep.RoseOnSpawn, "Always: kamera içi bile kalkar");
            Debug.Log("[DD-OBS] wp040.rise policy=hybrid skinned=" + skinned + " judged(front/back)=OnScreen/OffScreen margin=" + EnemySpawnDirector.RiseViewMargin);
            EnemyFactory.ClearAll();
        }

        // ================================================================= ADR-040-1 beats

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Vuruslar_Telegraph_Commit_HitVeyaMiss_Sirali_YasamKimlikli()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy z = Dogur(EnemyTier.Standard, Zemin(0f, 1.3f), SpawnVisibility.Unknown);
            int life = z.LifeId;
            yield return null;
            Assert.AreEqual(1, Say(EnemyCombatEventKind.SpawnLand, life));

            yield return Bekle(() => Say(EnemyCombatEventKind.TelegraphStart, life) >= 1, 8f, "telegraph_start gelmedi (menzil içinde saldırı başlamalı)");
            yield return Bekle(() => Say(EnemyCombatEventKind.AttackCommit, life) >= 1, 4f, "attack_commit gelmedi");
            yield return Bekle(() => Say(EnemyCombatEventKind.AttackHit, life) + Say(EnemyCombatEventKind.AttackMiss, life) >= 1, 4f, "attack_hit / attack_miss gelmedi");

            // order of the first swing
            var sira = new List<EnemyCombatEventKind>();
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == life && _beats[i].Kind != EnemyCombatEventKind.SpawnLand) sira.Add(_beats[i].Kind);
            Assert.GreaterOrEqual(sira.Count, 3);
            Assert.AreEqual(EnemyCombatEventKind.TelegraphStart, sira[0]);
            Assert.AreEqual(EnemyCombatEventKind.AttackCommit, sira[1]);
            Assert.IsTrue(sira[2] == EnemyCombatEventKind.AttackHit || sira[2] == EnemyCombatEventKind.AttackMiss, "commit'ten sonra hit ya da miss: " + sira[2]);

            EnemyCombatEventInfo tel = default;
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == life && _beats[i].Kind == EnemyCombatEventKind.TelegraphStart) { tel = _beats[i]; break; }
            Assert.AreEqual(EnemyTier.Standard, tel.Tier);
            Assert.AreSame(z.transform, tel.Body);
            Assert.AreEqual(z.AttackAnimationDriven, tel.AnimationDriven, "sürüş bayrağı gövdeyle aynı");
            Assert.Greater(tel.Seconds, 0f, "telegraph süresi taşınır");
            Assert.AreEqual("telegraph_start", tel.RowName);
            Debug.Log("[DD-OBS] wp040.beats swing=" + string.Join(">", sira) + " driven=" + tel.AnimationDriven + " telegraph=" + tel.Seconds.ToString("0.00"));
            EnemyFactory.ClearAll();
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ZirhKirilmasi_Stagger_Olum_Vuruslari()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy z = Dogur(EnemyTier.Elite, Zemin(0f, 7f), SpawnVisibility.Unknown);
            int life = z.LifeId;
            yield return null;
            z.FitArmorComposition(false, true, false);
            z.Armor.Equip(ArmorSlot.Body, 5f);
            Assert.IsTrue(z.Armor.IsEquipped(ArmorSlot.Body));

            // break the body plate: armor_break_torso + (real body) the heavy stagger beat
            // direct receiver call (RegionalArmorSliceTests [#22] recipe): the same ProcessResult path as a shot,
            // without the physics ray (the walking Elite's hitbox geometry is not what this test is about)
            DamageResult kirSonuc = z.ApplyHit(new DamageHit { Amount = 1000f, Region = BodyRegion.UpperBody, Point = z.transform.position + Vector3.up * 1.1f, Direction = -z.transform.forward, ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet });
            yield return null;
            Assert.IsFalse(DamageResults.IsEmpty(in kirSonuc), "vuruş işlenmedi");
            Assert.IsFalse(z.Armor.Protects(ArmorSlot.Body), "dō kırılmadı (kırık plaka takılı kalır ama korumaz)");
            Assert.IsTrue(kirSonuc.ArmorBroke, "sonuç kırılmayı raporlar");
            Assert.IsTrue(z.IsAlive, "kırılma vuruşu öldürmez (emilir)");
            Assert.AreEqual(1, Say(EnemyCombatEventKind.ArmorBreak, life), "armor_break_torso bir kez");
            EnemyCombatEventInfo kir = default;
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == life && _beats[i].Kind == EnemyCombatEventKind.ArmorBreak) kir = _beats[i];
            Assert.AreEqual(ArmorSlot.Body, kir.Region); Assert.AreEqual("armor_break_torso", kir.RowName); Assert.AreEqual(EnemyTier.Elite, kir.Tier);
            ZombieAppearance look = (ZombieAppearance)z.VersionVisual;
            bool skinned = look.IsSkinned;
            if (skinned && look.HasArmor)
            {
                Assert.GreaterOrEqual(Say(EnemyCombatEventKind.Stagger, life), 1, "kırılma ağır sendeleme vuruşu (klip stagger_end'e kadar tutar)");
                Assert.IsTrue(look.Skinned.IsHeavyStaggering, "ağır sendeleme klibi tutuyor");
                yield return Bekle(() => !look.Skinned.IsHeavyStaggering, ZombieSkinnedVisual.HeavyStaggerCapSeconds + 0.5f, "stagger_end / tavan gelmedi");
            }

            // death beat once, with the tier row
            z.Kill();
            yield return null;
            Assert.AreEqual(1, Say(EnemyCombatEventKind.Death, life), "death_elite bir kez");
            EnemyCombatEventInfo olum = default;
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == life && _beats[i].Kind == EnemyCombatEventKind.Death) olum = _beats[i];
            Assert.AreEqual("death_elite", olum.RowName);
            Assert.AreEqual(1, EnemyCombatEvents.Count(EnemyCombatEventKind.Death));
            Debug.Log("[DD-OBS] wp040.beats break=armor_break_torso stagger=" + Say(EnemyCombatEventKind.Stagger, life) + " death=death_elite skinned=" + skinned);
            EnemyFactory.ClearAll();
        }

        // ================================================================= B·B·B on live bodies

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Elite_KoridorKapatir_Special_CooldownTabani_Ultra_UcBolge()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy el = Dogur(EnemyTier.Elite, Zemin(-2f, 9f), SpawnVisibility.Unknown);
            ZombieEnemy sp = Dogur(EnemyTier.Special, Zemin(0f, 9f), SpawnVisibility.Unknown);
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(2f, 9f), SpawnVisibility.Unknown);
            ZombieEnemy st = Dogur(EnemyTier.Standard, Zemin(4f, 9f), SpawnVisibility.Unknown);
            yield return null;

            Assert.IsTrue(el.PathFollower.HoldsChokepoints, "Elite: koridor kapatma takipçiye ulaşır");
            Assert.IsFalse(st.PathFollower.HoldsChokepoints); Assert.IsFalse(sp.PathFollower.HoldsChokepoints); Assert.IsFalse(ul.PathFollower.HoldsChokepoints);
            Assert.IsTrue(el.Armor.IsEquipped(ArmorSlot.Body), "Elite dō garanti");
            Assert.AreEqual(3, ul.Armor.EquippedCount, "Ultra üç bölge (eğilim 1.0)");
            Assert.GreaterOrEqual(sp.AttackCooldownSeconds, 1.9f, "Special cooldown ≥ 1.9 bandı canlı gövdede");
            Assert.AreEqual(PrototypeConfig.Active.Enemies.Find(EnemyTier.Standard).AttackCooldown, st.AttackCooldownSeconds, 1e-4f, "Standard tablo değeri");

            // Threat Director A/B (WP-033 signal, WP-022B ELITE|ULTRA NEARBY chip): the same crowd geometry with
            // Elite + Ultra near the player raises TierPresence / StrongVersionPresent against a Standard-only crowd.
            ThreatDirector.ResetStatics();
            var ctx = new ThreatContext { PlayerPosition = _rig.transform.position, ViewForward = _rig.transform.forward, Health01 = 1f, RoundProgress01 = 0.5f, InFight = true };
            ThreatDirector.ContextOverride = ctx;
            ThreatSnapshot b = ThreatDirector.Sample();   // A: Elite + Ultra + Special + Standard at 9 m
            float tierB = b.TierPresence; bool strongB = b.StrongVersionPresent;
            EnemyFactory.ClearAll(); yield return null;
            ThreatDirector.ResetStatics();
            ThreatDirector.ContextOverride = ctx;
            for (int i = 0; i < 4; i++) Dogur(EnemyTier.Standard, Zemin(-2f + 2f * i, 9f), SpawnVisibility.Unknown);
            yield return null;
            ThreatSnapshot a = ThreatDirector.Sample();   // B: four Standards in the same spots
            ThreatDirector.ContextOverride = null;
            Assert.Greater(tierB, a.TierPresence, "Elite+Ultra yakınken tier presence Standard kalabalıktan yüksek (A/B)");
            Assert.IsTrue(strongB, "ELITE|ULTRA NEARBY bayrağı (WP-022B çipi) güçlü versiyon varken");
            Assert.IsFalse(a.StrongVersionPresent, "yalnız Standard: bayrak kapalı");
            Debug.Log("[DD-OBS] wp040.threat A/B tierPresence strong=" + tierB.ToString("0.000") + " standardOnly=" + a.TierPresence.ToString("0.000") + " strongFlag=" + strongB + "/" + a.StrongVersionPresent);
            EnemyFactory.ClearAll(); yield return null;

            // second life from the pool: the Elite flag does not leak into a Standard
            ZombieEnemy again = Dogur(EnemyTier.Standard, Zemin(0f, 9f), SpawnVisibility.Unknown);
            yield return null;
            Assert.IsFalse(again.PathFollower.HoldsChokepoints, "havuzdan ikinci yaşam: bayrak sızmaz");
            EnemyFactory.ClearAll();
        }
    }
}
