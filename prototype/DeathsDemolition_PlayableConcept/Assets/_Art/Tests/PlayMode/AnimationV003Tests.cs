using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032C · The AP-009C v003 set on the characters: the classic weapon's Aim / Fire / Reload
    /// clips follow S-02's WeaponEvents (reload rate-scaled to the config time with mag_out /
    /// mag_in / chamber inside it), the Special Weapon hold pose, the zombie's directional × tier
    /// hit reads, armour-break-by-region and stumble clips, rise variants with spawn beats and the
    /// tier deaths.
    /// </summary>
    [TestFixture]
    [Category("WP032C")]
    public sealed class AnimationV003Tests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            CharacterAnimatorLod.ResetStatics();
            FootIkAdapter.ResetStatics();
            CharacterAnimationEvents.ResetStatics();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            RunFlowClock.ClearHolds();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private IEnumerator MainMenu()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
        }

        private ZombieSkinnedVisual ZombieOnStage(Vector3 at, EnemyTier tier, uint seed, bool rise = false)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032C_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(tier, 2.7f, "A", 0, rise);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        private PlayerSkinnedVisual PlayerOnStage(Func<Vector3> velocity)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032C_PlayerStage");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            pv.Attach(visual, velocity, () => 6.2f, () => Vector3.forward);
            Assert.IsTrue(pv.Authored, "oyuncu AP-009 seti değil");
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return pv;
        }

        private static bool In(Animator an, int layer, string state)
        {
            return an.GetCurrentAnimatorStateInfo(layer).IsName(state) || an.GetNextAnimatorStateInfo(layer).IsName(state);
        }

        // ============================================================ classic weapon: aim / fire / reload / switch / hold

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Silah_AimKatmani_Fire_Reload_Olaylari_Switch_SpecialHold()
        {
            yield return MainMenu();
            Vector3 velocity = Vector3.zero;
            PlayerSkinnedVisual pv = PlayerOnStage(() => velocity);
            Animator an = pv.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer), add = ArtAnimatorLayers.Index(an, C.AdditiveLayer);
            // Rest while armed: Aim_0 (A rifle) from the first frame; equip B → Aim_1.
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0"), 2f, "Aim_0 (A) rest pozu");
            WeaponEvents.PublishWeaponEquipped(ClassicWeaponId.AutoSmg);
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_1") && !an.IsInTransition(over), 2f, "B → Aim_1");
            Assert.AreEqual(1, pv.Weapon);
            // Fire → the weapon's additive recoil, legs untouched.
            velocity = Vector3.forward * 6.2f;
            yield return CharacterTests.Bekle(() => pv.Move.y > 0.9f, 2f, "koşu");
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(In(an, add, C.FireState + "_1"), "Fire_1 additive");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "ateş taban katmanını kesti");
            // Reload: rate-scaled to the config time; mag_out → mag_in → chamber arrive in order and inside the window.
            const float reloadSeconds = 1.7f;
            var seen = new List<CharacterAnimationEvent>();
            float reloadStart = -1f, chamberAt = -1f;
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e == CharacterAnimationEvent.MagOut || e == CharacterAnimationEvent.MagIn || e == CharacterAnimationEvent.Chamber) seen.Add(e);
                if (e == CharacterAnimationEvent.Chamber) chamberAt = Time.time;
            };
            pv.Events.Raised += onEvent;
            WeaponEvents.PublishReloadStarted(reloadSeconds);
            reloadStart = Time.time;
            yield return null; yield return null;
            Assert.IsTrue(In(an, over, C.ReloadState + "_1"), "Reload_1");
            float expectedRate = pv.AnimationSet.Find(C.PlayerWeaponClip(1, "reload")).seconds / reloadSeconds;
            Assert.AreEqual(expectedRate, an.GetFloat(C.ReloadRate), 1e-3f, "ReloadRate = klip s / ReloadTime");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && pv.Move.y > 0.9f, "reload yürüyüşü kesti");
            yield return CharacterTests.Bekle(() => chamberAt > 0f, reloadSeconds + 1f, "chamber olayı gelmedi");
            pv.Events.Raised -= onEvent;
            Debug.Log("[DD-TEST] wp032c.reload events=" + string.Join(",", seen) + " chamberAt=" + (chamberAt - reloadStart).ToString("F2", CultureInfo.InvariantCulture) + " s rate=" + expectedRate.ToString("F2", CultureInfo.InvariantCulture));
            Assert.AreEqual(3, seen.Count, "mag_out / mag_in / chamber");
            Assert.AreEqual(CharacterAnimationEvent.MagOut, seen[0]); Assert.AreEqual(CharacterAnimationEvent.MagIn, seen[1]); Assert.AreEqual(CharacterAnimationEvent.Chamber, seen[2]);
            Assert.LessOrEqual(chamberAt - reloadStart, reloadSeconds + 0.1f, "chamber ReloadTime içinde olmalı");
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_1") && !an.IsInTransition(over), 2f, "reload sonrası Aim_1'e dönüş");
            // Interrupted reload snaps back to the rest.
            WeaponEvents.PublishReloadStarted(reloadSeconds);
            yield return null; yield return null;
            Assert.IsTrue(In(an, over, C.ReloadState + "_1"));
            WeaponEvents.PublishReloadInterrupted();
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_1"), 1f, "kesilen reload Aim'e dönmedi");
            // Switch: out (switch_away) then in (switch_ready) on the new weapon.
            int away = 0, ready = 0;
            Action<CharacterAnimationEvent> onSwitch = e => { if (e == CharacterAnimationEvent.SwitchAway) away++; if (e == CharacterAnimationEvent.SwitchReady) ready++; };
            pv.Events.Raised += onSwitch;
            pv.PlaySwitchOut();
            yield return CharacterTests.Bekle(() => away > 0, 1.5f, "switch_away");
            WeaponEvents.PublishWeaponEquipped(ClassicWeaponId.CombatShotgun);
            pv.PlaySwitchIn();
            yield return CharacterTests.Bekle(() => ready > 0, 1.5f, "switch_ready");
            pv.Events.Raised -= onSwitch;
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_2") && !an.IsInTransition(over), 2f, "C → Aim_2");
            // Special Weapon held: the hold loop replaces the Aim; ended → back to the classic Aim.
            var state = new SpecialWeaponHudState { Phase = SpecialWeaponPhase.Active, WeaponId = SpecialWeaponId.Namekuji };
            WeaponEvents.PublishSpecial(state, true);
            yield return CharacterTests.Bekle(() => In(an, over, C.SwHoldState + "_2") && !an.IsInTransition(over), 2f, "SwHold_2 (loom)");
            Assert.AreEqual(2, pv.SpecialHold);
            WeaponEvents.PublishSpecialEnded();
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_2") && !an.IsInTransition(over), 2f, "special bitince Aim_2");
            velocity = Vector3.zero;
        }

        // ============================================================ player: hurt A/B, death_side

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_HurtA_UstGovde_HurtB_Taban_DeathSide()
        {
            yield return MainMenu();
            Vector3 velocity = Vector3.zero;
            PlayerSkinnedVisual pv = PlayerOnStage(() => velocity);
            Animator an = pv.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer);
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0"), 2f, "Aim_0");
            pv.PlayHurt(true);
            yield return null; yield return null;
            Assert.IsTrue(In(an, over, C.HurtState + "_A"), "hurt_A üst katman");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "hurt_A tabanı kesti");
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0") && !an.IsInTransition(over), 2f, "hurt_A sonrası Aim");
            pv.PlayHurt(false);
            yield return null; yield return null;
            Assert.IsTrue(In(an, 0, C.HurtState + "_B"), "hurt_B taban katman");
            yield return CharacterTests.Bekle(() => an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !an.IsInTransition(0), 2f, "hurt_B sonrası Locomotion");
            pv.PlayDeath(Vector3.right, false);
            yield return null; yield return null;
            Assert.IsTrue(In(an, 0, C.DeathState + "_2"), "yandan itiş → death_side");
            yield return CharacterTests.Bekle(() => pv.DeathFinished, 3f, "death_end");
        }

        // ============================================================ zombie: directional × tier hits, armour break, stumble

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_YonluTierVurus_ZirhKirilmasiBolge_Stumble()
        {
            yield return MainMenu();
            ZombieSkinnedVisual std = ZombieOnStage(new Vector3(0f, 0f, 0f), EnemyTier.Standard, 3u);
            ZombieSkinnedVisual ultra = ZombieOnStage(new Vector3(3f, 0f, 0f), EnemyTier.Ultra, 4u);
            int over = ArtAnimatorLayers.Index(std.Animator, C.OverrideUpperLayer);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0, std.HitTier); Assert.AreEqual(2, ultra.HitTier);
            // pushed back (+z? no: the impulse pushes the body BACK = hit from the front → "front"); shoved right → "right"
            std.PlayFlinch(Vector3.back, BodyRegion.UpperBody, true);
            ultra.PlayFlinch(Vector3.right, BodyRegion.UpperBody, true);
            yield return null; yield return null;
            Assert.IsTrue(In(std.Animator, over, C.HitState + "_front_std"), "Standard önden vuruş");
            Assert.IsTrue(In(ultra.Animator, over, C.HitState + "_right_ultra"), "Ultra sağdan vuruş (yalnız üst gövde)");
            Assert.IsTrue(ultra.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "Ultra vuruşu tabanı kesmemeli");
            Assert.AreEqual(0, ZombieSkinnedVisual.HitDirectionIndex(Vector3.back)); Assert.AreEqual(1, ZombieSkinnedVisual.HitDirectionIndex(Vector3.forward));
            Assert.AreEqual(2, ZombieSkinnedVisual.HitDirectionIndex(Vector3.left)); Assert.AreEqual(3, ZombieSkinnedVisual.HitDirectionIndex(Vector3.right));
            yield return CharacterTests.Bekle(() => In(std.Animator, over, C.EmptyState) && !std.Animator.IsInTransition(over), 2f, "vuruş sonrası Empty");
            // armour break by region (base, full body) with the armor_break event; held like a stagger for the clip
            int breaks = 0;
            Action<CharacterAnimationEvent> onEvent = e => { if (e == CharacterAnimationEvent.ArmorBreak) breaks++; };
            std.Events.Raised += onEvent;
            std.NotifyArmorBroken(BodyRegion.Head);
            yield return null; yield return null;
            Assert.IsTrue(In(std.Animator, 0, C.ArmorBreakState + "_0"), "armorbreak_head taban");
            Assert.IsTrue(std.IsHeavyStaggering, "zırh kırılması gövdeyi tutmalı");
            yield return CharacterTests.Bekle(() => breaks > 0, 1f, "armor_break olayı");
            std.Events.Raised -= onEvent;
            yield return CharacterTests.Bekle(() => std.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !std.Animator.IsInTransition(0), 2f, "kırılma sonrası Locomotion");
            Assert.IsFalse(std.IsHeavyStaggering, "tutma klip sonunda kalkmalı");
            // Runner stumble (S-03 hook)
            ZombieSkinnedVisual runner = ZombieOnStage(new Vector3(6f, 0f, 0f), EnemyTier.Special, 5u);
            for (int i = 0; i < 3; i++) yield return null;
            runner.PlayStumble();
            yield return null; yield return null;
            Assert.IsTrue(In(runner.Animator, 0, C.StumbleState), "hit_special_stumble");
            yield return CharacterTests.Bekle(() => runner.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !runner.Animator.IsInTransition(0), 2f, "stumble sonrası Locomotion");
        }

        // ============================================================ zombie: rise variants + spawn beats, tier deaths

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_RiseVaryantlari_SpawnOlaylari_TierOlumleri()
        {
            yield return MainMenu();
            int variantsSeen = 0;
            var seen = new HashSet<int>();
            for (int k = 0; k < 6 && seen.Count < 3; k++)
            {
                ZombieSkinnedVisual v = ZombieOnStage(new Vector3(k * 2f, 0f, 0f), EnemyTier.Standard, (uint)(10 + k), rise: true);
                yield return null; yield return null;
                int variant = v.Animator.GetInteger(C.RiseVariant);
                string state = variant == 0 ? C.RiseState : C.RiseState + "_" + variant;
                Assert.IsTrue(In(v.Animator, 0, state), "kalkış durumu " + state);
                Assert.IsTrue(v.IsRising);
                if (variant > 0)
                {
                    int rises = 0, lands = 0;
                    Action<CharacterAnimationEvent> onEvent = e => { if (e == CharacterAnimationEvent.SpawnRise) rises++; if (e == CharacterAnimationEvent.SpawnLand) lands++; };
                    v.Events.Raised += onEvent;
                    yield return CharacterTests.Bekle(() => !v.IsRising, 2.5f, "rise_end (" + state + ")");
                    v.Events.Raised -= onEvent;
                    Assert.GreaterOrEqual(lands, 1, state + " spawn_land olayı");
                    Assert.LessOrEqual(v.AnimationSet.Find(C.ZombieRiseClips[variant]).seconds, 1.2f, "B/C ≤ 1,2 s");
                }

                if (seen.Add(variant)) variantsSeen++;
            }

            Assert.GreaterOrEqual(variantsSeen, 2, "kalkış varyantları tek tip");
            // tier deaths: Special → special_A/B, Ultra → death_ultra (60 f, death_end)
            ZombieSkinnedVisual sp = ZombieOnStage(new Vector3(0f, 0f, 4f), EnemyTier.Special, 21u);
            ZombieSkinnedVisual ul = ZombieOnStage(new Vector3(3f, 0f, 4f), EnemyTier.Ultra, 22u);
            for (int i = 0; i < 3; i++) yield return null;
            sp.PlayDeath(5); ul.PlayDeath(7);
            yield return null; yield return null;
            Assert.IsTrue(In(sp.Animator, 0, C.DeathState + "_5"), "death_special_A");
            Assert.IsTrue(In(ul.Animator, 0, C.DeathState + "_7"), "death_ultra");
            yield return CharacterTests.Bekle(() => sp.DeathFinished && ul.DeathFinished, 6f, "death_end (tier ölümleri)");   // WP-071: ragdoll settle (Ultra hand-off 1.1 s + ≤ 3 s)
        }
    }
}
