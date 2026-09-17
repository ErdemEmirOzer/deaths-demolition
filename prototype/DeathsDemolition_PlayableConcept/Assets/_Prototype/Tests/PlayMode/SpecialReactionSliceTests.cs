using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DamageTarget = DD.Prototype.Weapons.DamageTarget;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-055 on real bodies in the Run scene, through the REAL weapon code (S-02's WP-007 files with the
    /// one-line hooks): the Kagura wave shoves and dazes by tier (Elite ×0.5 / Ultra ×0.2, plates untouched),
    /// the Ofuda seal nets by tier (Standard rooted, the rest slowed, shorter per tier), the Tide well pulls
    /// every tier but the Ultra (which only slows) — each as one sw_* beat with the weapon id; a dead body
    /// refuses the reaction.
    /// </summary>
    [Category("WP055")]
    public sealed class SpecialReactionSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private WeaponController _weapons;
        private readonly List<EnemyCombatEventInfo> _beats = new List<EnemyCombatEventInfo>();
        private readonly List<DamageTarget> _targets = new List<DamageTarget>(16);
        private void Kaydet(EnemyCombatEventInfo i) => _beats.Add(i);

        private int Beats(int lifeId, EnemyCombatEventKind kind, SpecialWeaponId sw)
        {
            int n = 0;
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == lifeId && _beats[i].Kind == kind && _beats[i].Special == sw) n++;
            return n;
        }

        private float BeatSeconds(int lifeId, EnemyCombatEventKind kind)
        {
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == lifeId && _beats[i].Kind == kind) return _beats[i].Seconds;
            return -1f;
        }

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
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _weapons = WeaponRegistry.ActiveController;
            _rig.Health.DiagnosisInvulnerable = true;
            TierBehaviourProfileSet.ResetStatics();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            EnemyCombatEvents.ResetStatics();
            EnemyAttackState.ResetStatics();
            _beats.Clear();
            EnemyCombatEvents.Raised += Kaydet;
            SpecialReactionBinder.Rebind();   // WP-056 ResetForTests in other suites drops every subscriber
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyCombatEvents.Raised -= Kaydet;
            if (_weapons != null) _weapons.DeactivateSpecialWeapon();
            TideWell.CloseAll();
            ZombieEnemy.ResetRisePolicy();
            TierBehaviourProfileSet.ResetStatics();
            EnemyCombatEvents.ResetStatics();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private Vector3 Zemin(Vector3 merkez, float x, float z)
        {
            Vector3 p = merkez + new Vector3(x, 0f, z);
            p.y = _rig.transform.position.y;
            return p;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 konum)
        {
            Vector3 d = _rig.transform.position - konum; d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 9, konum, bakis, SpawnVisibility.Unknown);
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ================================================================= Kagura Bell · push + daze by tier, plates untouched

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KaguraDalgasi_TierDirenciyleIter_Sersemletir_ZirhaDokunmaz()
        {
            yield return RunSahnesiniHazirla();
            Vector3 o = _rig.transform.position;
            ZombieEnemy st = Dogur(EnemyTier.Standard, Zemin(o, -2.5f, 3f));
            ZombieEnemy el = Dogur(EnemyTier.Elite, Zemin(o, 0f, 3.9f));
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(o, 2.5f, 3f));
            el.FitArmorComposition(false, true, false);
            el.Armor.Equip(ArmorSlot.Body, 500f);
            yield return null;
            Assert.IsTrue(el.Armor.Protects(ArmorSlot.Body));

            _weapons.ActivateSpecialWeapon(SpecialWeaponId.KaguraBell);
            Assert.IsTrue(_weapons.Special.IsActive && _weapons.Special.Id == SpecialWeaponId.KaguraBell);
            _weapons.Special.Fire(o, _rig.transform.forward);
            yield return Bekle(() => st.SpecialReactionCount > 0 && el.SpecialReactionCount > 0 && ul.SpecialReactionCount > 0, 3f, "dalga üç gövdeyi de geçmedi");

            Assert.AreEqual(SpecialReactionKind.Push, st.LastSpecialReaction);
            float s = Flat(st.LastSpecialImpulse), e = Flat(el.LastSpecialImpulse), u = Flat(ul.LastSpecialImpulse);
            Assert.Greater(s, 1f, "Standard itildi");
            Assert.AreEqual(0.5f, e / s, 0.15f, "Elite ×0,5 (aynı yarıçap)");
            Assert.AreEqual(0.2f, u / s, 0.12f, "Ultra ×0,2");
            Assert.LessOrEqual(s, SpecialReactionProfile.PushSpeedCap + 1e-3f);
            Vector3 away = st.transform.position - o; away.y = 0f;
            Assert.Greater(Vector3.Dot(away.normalized, st.LastSpecialImpulse.normalized), 0.7f, "itiş merkezden dışarı");
            Assert.IsTrue(st.Animator.IsStaggering, "Standard sersemledi");
            Assert.IsTrue(st.IsDazed, "sersemleme zamanlayıcısı");
            if (st.Animator.Driven) Assert.AreEqual(ZombieSpecialVisual.Dazed, st.SpecialVisual, "WP-032E Dazed görseli (stumble + sway)");
            Assert.IsFalse(ul.IsDazed, "Ultra 0,09 s → sersemleme yok");

            Assert.AreEqual(1, Beats(st.LifeId, EnemyCombatEventKind.SpecialPush, SpecialWeaponId.KaguraBell), "sw_push beat'i (Kagura)");
            Assert.AreEqual(1, Beats(el.LifeId, EnemyCombatEventKind.SpecialPush, SpecialWeaponId.KaguraBell));
            Assert.AreEqual(1, Beats(ul.LifeId, EnemyCombatEventKind.SpecialPush, SpecialWeaponId.KaguraBell));
            Assert.AreEqual(SpecialReactionProfile.PushDazeSeconds, BeatSeconds(st.LifeId, EnemyCombatEventKind.SpecialPush), 1e-3f);
            Assert.AreEqual(SpecialReactionProfile.PushDazeSeconds * 0.5f, BeatSeconds(el.LifeId, EnemyCombatEventKind.SpecialPush), 1e-3f, "Elite sersemlemesi yarı");
            Assert.AreEqual("sw_push", EnemyCombatEvents.Last.RowName);

            Assert.IsTrue(el.Armor.Protects(ArmorSlot.Body), "dalga zırh kırmaz (hasar WP-007 yolu, plaka 500)");
            Assert.GreaterOrEqual(SpecialReactionBinder.HitsRouted, 3, "üç Hit olayı binder üzerinden gövdeye ulaştı (ADR-056-1)");
            Assert.IsTrue(st.IsAlive && el.IsAlive && ul.IsAlive, "dalga hasarı WP-007 değerinde kalır");
            Debug.Log("[DD-OBS] wp055.kagura impulse S/E/U=" + s.ToString("0.00") + "/" + e.ToString("0.00") + "/" + u.ToString("0.00") + " daze S=" + BeatSeconds(st.LifeId, EnemyCombatEventKind.SpecialPush) + " U=" + BeatSeconds(ul.LifeId, EnemyCombatEventKind.SpecialPush));
            _weapons.DeactivateSpecialWeapon();
            EnemyFactory.ClearAll();
        }

        // ================================================================= Ofuda Loom · net by tier (root / slow, shorter per tier)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator OfudaAgi_StandardKok_DigerleriTierYavaslamasi_SureTierGore()
        {
            yield return RunSahnesiniHazirla();
            Vector3 o = _rig.transform.position;
            ZombieEnemy st = Dogur(EnemyTier.Standard, Zemin(o, -3f, 8f));
            ZombieEnemy sp = Dogur(EnemyTier.Special, Zemin(o, -1f, 8f));
            ZombieEnemy el = Dogur(EnemyTier.Elite, Zemin(o, 1f, 8f));
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(o, 3f, 8f));
            yield return null;
            yield return new WaitForFixedUpdate();

            _weapons.ActivateSpecialWeapon(SpecialWeaponId.OfudaLoom);
            OfudaLoomSettings loom = PrototypeConfig.Active.Weapons.Special.OfudaLoom;
            int count = HitscanEngine.OverlapEnemies(Zemin(o, 0f, 8f), 4f, _targets);
            Assert.GreaterOrEqual(count, 4, "dört gövde kuşatma sorgusunda");
            int sealed_ = 0;
            for (int i = 0; i < count; i++) if (_weapons.Special.Web.ApplySeal(_targets[i], loom)) sealed_++;
            Assert.GreaterOrEqual(sealed_, 4, "mühürler takıldı");
            yield return null;

            Assert.AreEqual(0f, st.SpecialSlowFactor, 1e-4f, "Standard kök");
            Assert.AreEqual(SpecialReactionProfile.WebFactor(EnemyTier.Special), sp.SpecialSlowFactor, 1e-4f);
            Assert.AreEqual(SpecialReactionProfile.WebFactor(EnemyTier.Elite), el.SpecialSlowFactor, 1e-4f);
            Assert.AreEqual(SpecialReactionProfile.WebFactor(EnemyTier.Ultra), ul.SpecialSlowFactor, 1e-4f);
            Assert.IsTrue(st.IsNetted && sp.IsNetted && el.IsNetted && ul.IsNetted);
            Assert.IsTrue(st.IsRooted && !sp.IsRooted && !ul.IsRooted, "yalnız Standard kök");
            if (st.Animator.Driven) Assert.AreEqual(ZombieSpecialVisual.Netted, st.SpecialVisual, "kök = WP-032E Netted (poz donar)");
            Assert.AreEqual(ZombieSpecialVisual.None, ul.SpecialVisual, "yavaşlayan gövde yürümeye devam eder (görsel yok)");
            Assert.AreEqual(1, Beats(st.LifeId, EnemyCombatEventKind.SpecialWeb, SpecialWeaponId.OfudaLoom), "sw_web beat'i (Ofuda)");
            float sSt = BeatSeconds(st.LifeId, EnemyCombatEventKind.SpecialWeb), sSp = BeatSeconds(sp.LifeId, EnemyCombatEventKind.SpecialWeb),
                  sEl = BeatSeconds(el.LifeId, EnemyCombatEventKind.SpecialWeb), sUl = BeatSeconds(ul.LifeId, EnemyCombatEventKind.SpecialWeb);
            Assert.AreEqual(SpecialReactionProfile.WebSeconds(EnemyTier.Standard, loom.SealSeconds), sSt, 1e-3f);
            Assert.IsTrue(sSt > sSp && sSp > sEl && sEl > sUl, "süre tier'a göre kısalır: " + sSt + "/" + sSp + "/" + sEl + "/" + sUl);

            // the rooted Standard does not close in while netted; the netted Ultra still walks, slower
            Vector3 p0 = st.transform.position, u0 = ul.transform.position;
            float t0 = Time.time;
            while (Time.time - t0 < 0.6f) yield return null;
            float stMoved = Flat(st.transform.position - p0), ulMoved = Flat(ul.transform.position - u0);
            Assert.Less(stMoved, 0.12f, "kökteki Standard yerinde (" + stMoved.ToString("0.00") + " m)");
            Debug.Log("[DD-OBS] wp055.ofuda seconds S/Sp/E/U=" + sSt + "/" + sSp + "/" + sEl + "/" + sUl + " moved S=" + stMoved.ToString("0.00") + " U=" + ulMoved.ToString("0.00"));

            // the net lifts on its own: after the Ultra's window (0.9 s) it walks at full speed again
            yield return Bekle(() => !ul.IsNetted, sUl + 1f, "Ultra ağı zamanında kalkmadı");
            Assert.IsTrue(st.IsNetted, "Standard kökü daha uzun (2,1 s)");
            Assert.IsTrue(st.IsRooted, "hâlâ kökte");
            int ended0 = SpecialReactionBinder.FieldsEnded;
            // diagnostic (S-02 parity test interaction): a dart aimed at the ROOTED Standard's torso centre — which region does the
            // frozen hunched pose present first? (WP-007 parity assumes the torso; a head in front reads ×2)
            var regions = new List<string>();
            System.Action<EnemyDamageInfo> dmg = i => { if (i.Owner == st.transform) regions.Add(i.Result.Region + ":" + i.Result.HealthDamage.ToString("0.#")); };
            DamageEvents.EnemyDamaged += dmg;
            try
            {
                Collider torso = st.HitboxFor(BodyRegion.UpperBody); Collider head = st.HitboxFor(BodyRegion.Head);
                Vector3 aim = torso.bounds.center; Vector3 from = _rig.transform.position + Vector3.up * (aim.y - _rig.transform.position.y);
                Debug.Log("[DD-OBS] wp055.ofuda rooted-pose torso=" + torso.bounds.center.ToString("F2") + " head=" + head.bounds.center.ToString("F2") + " headR=" + head.bounds.extents.y.ToString("F2") + " from=" + from.ToString("F2"));
                _weapons.Special.Fire(from, (aim - from).normalized);
                yield return null;
                Debug.Log("[DD-OBS] wp055.ofuda rooted-dart regions=" + string.Join(",", regions));
            }
            finally { DamageEvents.EnemyDamaged -= dmg; }

            // the web collapsing (FieldEnded) frees the rest at once (the diagnostic dart on a sealed body may already have collapsed it: 4 seals ≥ MinSealsToCollapse)
            if (_weapons.Special.Web.SealCount > 0) _weapons.Special.Web.Collapse(in loom);
            yield return null;
            Assert.Greater(SpecialReactionBinder.FieldsEnded, ended0, "çökme FieldEnded yayınladı");
            Assert.IsFalse(st.IsNetted || sp.IsNetted || el.IsNetted, "çökme ağları kaldırdı (EndSpecialField)");
            Assert.AreEqual(ZombieSpecialVisual.None, st.SpecialVisual, "Netted görseli kalktı");
            _weapons.DeactivateSpecialWeapon();
            EnemyFactory.ClearAll();
        }

        // ================================================================= diagnostic · S-02 parity replay (rooted bodies)

        /// <summary>Replays WP-007's Ofuda parity sequence (3 unarmoured Standards at z 5, 3 m apart; dart each; dart the first again → collapse) and logs every damage event per body with region / amount / source, to locate the extra 9 the parity test sees since the seal roots a Standard.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Tani_OfudaPariteTekrari_KokluGovdelerdeOlayListesi()
        {
            yield return RunSahnesiniHazirla();
            OfudaLoomSettings loom = PrototypeConfig.Active.Weapons.Special.OfudaLoom;
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = _rig.transform.position + _rig.transform.rotation * new Vector3((i - 1f) * 3f, 0f, 5f); p.y = _rig.transform.position.y;
                ZombieEnemy z = Dogur(EnemyTier.Standard, p); z.FitArmorComposition(false, false, false); bodies.Add(z);
            }
            yield return null;
            var log = new System.Text.StringBuilder("[DD-OBS] wp055.parity-replay ");
            System.Action<EnemyDamageInfo> dmg = i =>
            {
                int idx = bodies.FindIndex(b => b.transform == i.Owner);
                log.Append("z").Append(idx).Append(':').Append(i.Result.Region).Append('/').Append((i.Result.HealthDamage + i.Result.ArmorDamage).ToString("0.#")).Append('/').Append(i.Source).Append(' ');
            };
            DamageEvents.EnemyDamaged += dmg;
            try
            {
                _weapons.ActivateSpecialWeapon(SpecialWeaponId.OfudaLoom);
                for (int i = 0; i < 3; i++)
                {
                    Collider hb = bodies[i].HitboxFor(BodyRegion.UpperBody); Vector3 aim = hb.bounds.center;
                    Vector3 from = _rig.transform.position + Vector3.up * (aim.y - _rig.transform.position.y);
                    log.Append("| dart").Append(i).Append(" rooted=").Append(bodies[i].IsRooted).Append(" vis=").Append(bodies[i].SpecialVisual).Append(' ');
                    _weapons.Special.Fire(from, (aim - from).normalized);
                    yield return null;
                    log.Append("seals=").Append(_weapons.Special.Web.SealCount).Append(' ');
                }
                yield return Bekle(() => _weapons.Special.Web.ArmedSealCount >= 3, 8f, "mühürler kurulmadı");
                Collider hb0 = bodies[0].HitboxFor(BodyRegion.UpperBody); Vector3 aim0 = hb0.bounds.center;
                Vector3 from0 = _rig.transform.position + Vector3.up * (aim0.y - _rig.transform.position.y);
                log.Append("| dart0b rooted=").Append(bodies[0].IsRooted).Append(" vis=").Append(bodies[0].SpecialVisual).Append(" torso=").Append(aim0.ToString("F2")).Append(" head=").Append(bodies[0].HitboxFor(BodyRegion.Head).bounds.center.ToString("F2")).Append(' ');
                _weapons.Special.Fire(from0, (aim0 - from0).normalized);
                yield return null; yield return null;
                log.Append("| collapses=").Append(_weapons.Special.Web.CollapseCount);
            }
            finally { DamageEvents.EnemyDamaged -= dmg; Debug.Log(log.ToString()); _weapons.DeactivateSpecialWeapon(); EnemyFactory.ClearAll(); }
        }

        // ================================================================= Tide Jewel · pull toward the core, the Ultra only slows; a corpse refuses

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator TideKuyusu_MerkezeCeker_UltraCekilmezYavaslar_OluReddeder()
        {
            yield return RunSahnesiniHazirla();
            Vector3 o = _rig.transform.position;
            _weapons.ActivateSpecialWeapon(SpecialWeaponId.TideJewel);
            _weapons.Special.Fire(o + Vector3.up * 1.2f, _rig.transform.forward);
            yield return null;
            TideWell well = null;
            foreach (TideWell w in Object.FindObjectsOfType<TideWell>()) if (w.isActiveAndEnabled) { well = w; break; }
            Assert.IsNotNull(well, "kuyu açılmadı (LiveCount " + TideWell.LiveCount + ")");
            Vector3 core = well.transform.position;

            ZombieEnemy st = Dogur(EnemyTier.Standard, Zemin(core, -3f, 0f));
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(core, 3f, 0f));
            ZombieEnemy dead = Dogur(EnemyTier.Standard, Zemin(core, 0f, 3f));
            yield return null;
            dead.Kill();
            yield return null;
            Assert.IsFalse(dead.ApplySpecialReaction(new SpecialReaction(SpecialReactionKind.Pull, SpecialWeaponId.TideJewel, Vector3.forward, 5f, 0.6f, 0)), "ölü gövde tepkiyi reddeder");

            yield return Bekle(() => st.SpecialReactionCount > 0 && ul.SpecialReactionCount > 0, 2.5f, "çekim tıkı gelmedi (ticks " + TideWell.TotalPullTicks + ")");
            Assert.AreEqual(SpecialReactionKind.Pull, st.LastSpecialReaction);
            Vector3 inward = core - st.transform.position; inward.y = 0f;
            Assert.Greater(Flat(st.LastSpecialImpulse), 0.5f, "Standard çekildi");
            Assert.Greater(Vector3.Dot(inward.normalized, st.LastSpecialImpulse.normalized), 0.7f, "çekim merkeze doğru");
            Assert.AreEqual(0f, Flat(ul.LastSpecialImpulse), 1e-4f, "Ultra çekilmez");
            Assert.AreEqual(SpecialReactionProfile.UltraPullSlowFactor, ul.SpecialSlowFactor, 1e-4f, "…ama yavaşlar");
            Assert.GreaterOrEqual(Beats(st.LifeId, EnemyCombatEventKind.SpecialPull, SpecialWeaponId.TideJewel), 1, "sw_pull beat'i (Tide)");
            Assert.GreaterOrEqual(Beats(ul.LifeId, EnemyCombatEventKind.SpecialPull, SpecialWeaponId.TideJewel), 1);
            Assert.AreEqual(0, Beats(dead.LifeId, EnemyCombatEventKind.SpecialPull, SpecialWeaponId.TideJewel), "ölüden beat yok");
            // the implosion (FieldEnded) releases the Ultra's drag
            yield return Bekle(() => TideWell.LiveCount == 0, 4f, "kuyu patlamadı");
            yield return null;
            Assert.IsFalse(ul.IsNetted, "kuyu bitince Ultra sürüklenmesi kalkar (FieldEnded → EndSpecialField)");
            Debug.Log("[DD-OBS] wp055.tide pull S=" + Flat(st.LastSpecialImpulse).ToString("0.00") + " U=" + Flat(ul.LastSpecialImpulse).ToString("0.00") + " ultraSlow=" + ul.SpecialSlowFactor + " ticks=" + TideWell.TotalPullTicks);
            _weapons.DeactivateSpecialWeapon();
            EnemyFactory.ClearAll();
        }
    }
}
