using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032C · Root-cause probe for RegionalArmorSliceTests.DogrudanIsabet (main FAIL candidate): a horizontal ray from the
    /// front at the chest hitbox centre — which collider does it meet first with the body (a) animated + look-at, (b) animated
    /// without look-at, (c) frozen (Animator off)? Not a gate — run by filter, writes DD-TEST lines.
    /// </summary>
    [TestFixture]
    [Category("WP032CDiag")]
    public sealed class HitboxPoseDiagnosticTests
    {
        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }
        [UnityTearDown] public IEnumerator UnityTearDown() { RunFlowClock.ClearHolds(); System.Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null); CharacterAssetSet.ResetCache(); yield return null; }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator GogusIsini_OndenYatay_IlkCarpan_Animasyon_LookAt_Donmus()
        {
            System.Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "Procedural");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, 60f, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, 60f, "Run");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, 60f, "Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
            RunFlowClock.AddHold("wp032c-diag");
            foreach (ZombieEnemy live in new List<ZombieEnemy>(ZombieEnemy.Live)) if (live != null && live.IsSpawned) live.Despawn();
            yield return null;
            Transform player = PlayerRegistry.Primary.transform;
            Vector3 konum = player.position + player.forward * 6f;
            Vector3 d = player.position - konum; d.y = 0f;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, konum, Quaternion.LookRotation(d.normalized, Vector3.up));
            Assert.IsNotNull(z);
            for (int i = 0; i < 20; i++) yield return null;
            CharacterLookAt look = z.Skinned != null ? z.Skinned.LookAt : null;
            Animator an = z.Skinned != null ? z.Skinned.Animator : null;

            string Probe(string tag)
            {
                Collider chest = z.HitboxFor(BodyRegion.UpperBody), head = z.HitboxFor(BodyRegion.Head), legs = z.HitboxFor(BodyRegion.LowerBody);
                string[] names = { C.LocomotionState, C.WaitState, C.RiseState, C.RiseState + "_1", C.RiseState + "_2", C.DeathState + "_0", C.DeathState + "_1", C.DeathState + "_2", C.DeathState + "_3", C.StaggerState, C.AttackSwipeState };
                string state = "?";
                if (an != null) foreach (string n in names) if (an.GetCurrentAnimatorStateInfo(0).IsName(n)) state = n;
                Transform hips = an != null ? an.GetBoneTransform(HumanBodyBones.Hips) : null, headB = an != null ? an.GetBoneTransform(HumanBodyBones.Head) : null;
                Debug.Log("[DD-TEST] wp032c.hitbox.state " + tag + " state=" + state + " norm=" + (an != null ? an.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F2", CultureInfo.InvariantCulture) : "-")
                    + " authored=" + (z.Skinned != null && z.Skinned.Authored) + " rising=" + z.IsRising + " dying=" + z.IsDying + " alive=" + z.IsAlive + " aliveParam=" + (an != null && an.GetBool(C.Alive))
                    + " cull=" + (an != null ? an.cullingMode.ToString() : "-") + " anEnabled=" + (an != null && an.enabled) + " hipsY=" + (hips != null ? hips.position.y.ToString("F2", CultureInfo.InvariantCulture) : "-")
                    + " headY=" + (headB != null ? headB.position.y.ToString("F2", CultureInfo.InvariantCulture) : "-") + " rootY=" + z.transform.position.y.ToString("F2", CultureInfo.InvariantCulture)
                    + " visualY=" + (z.Skinned != null ? z.Skinned.transform.position.y.ToString("F2", CultureInfo.InvariantCulture) : "-") + " skinnedParent=" + (z.Skinned != null && z.Skinned.transform.parent != null ? z.Skinned.transform.parent.name : "-"));
                Vector3 hedef = chest.bounds.center;
                Vector3 geri = -z.transform.forward; geri.y = 0f; geri.Normalize();
                Vector3 kaynak = hedef - geri * 2.5f; kaynak.y = hedef.y;
                var hits = Physics.RaycastAll(kaynak, (hedef - kaynak).normalized, 6f, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                string first = hits.Length > 0 ? hits[0].collider.name + "@" + hits[0].distance.ToString("F2", CultureInfo.InvariantCulture) : "(none)";
                string line = "[DD-TEST] wp032c.hitbox " + tag + " first=" + first + " chestC=" + chest.bounds.center.ToString("F2") + " chestExt=" + chest.bounds.extents.ToString("F2")
                    + " headC=" + head.bounds.center.ToString("F2") + " headExt=" + head.bounds.extents.ToString("F2") + " legsC=" + legs.bounds.center.ToString("F2")
                    + " lookW=" + (look != null ? look.Weight.ToString("F2", CultureInfo.InvariantCulture) : "-") + " fwd=" + z.transform.forward.ToString("F2");
                Debug.Log(line);
                return first;
            }

            string a = Probe("animated+lookat");
            if (look != null) { look.enabled = false; }
            for (int i = 0; i < 20; i++) yield return null;
            string b = Probe("animated-nolookat");
            if (an != null) an.enabled = false;
            yield return null;
            string c = Probe("frozen(animator-off)");
            if (an != null) { an.enabled = true; }
            if (look != null) look.enabled = true;
            Assert.IsNotNull(a);
        }
    }
}
