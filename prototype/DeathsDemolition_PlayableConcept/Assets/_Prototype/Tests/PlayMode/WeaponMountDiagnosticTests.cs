using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using DD.Prototype.Core.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-059 (C-00 / P-06 AD finding: the right arm reads apart from the weapon, the rifle stands
    /// vertical in the A aim): where the weapon frame actually is on the live rig. For each aim pose
    /// A / B / C with ITS OWN AP-013 model mounted the way the controller mounts it (socket →
    /// mount → rig → body → model, all identity), log the socket's forward / up in character space,
    /// the socket's distance to the RightHand bone and the palm, the model's grip marker distance to
    /// the palm, and the muzzle direction in character space. Numbers, not a gate: they decide whether
    /// the fix is the clip (socket bone) or the mount (code / import). The "Diag" category keeps it
    /// out of the regular filters.
    /// </summary>
    [TestFixture]
    [Category("WP059Diag")]
    public sealed class WeaponMountDiagnosticTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + v.x.ToString("F2", CultureInfo.InvariantCulture) + ", " + v.y.ToString("F2", CultureInfo.InvariantCulture) + ", " + v.z.ToString("F2", CultureInfo.InvariantCulture) + ")";

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahMontaji_UcNisanPozu_SoketVeModelCerceveleri()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!(SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded) && Time.realtimeSinceStartup - t0 < SahneZamanAsimi) yield return null;
            yield return null;

            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP059_MountDiag");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            Transform socket = pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            Assert.IsNotNull(socket, "socket_weapon_r yok.");
            Animator an = pv.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform rh = an.GetBoneTransform(HumanBodyBones.RightHand);
            Transform palm = an.GetBoneTransform(HumanBodyBones.RightMiddleProximal) ?? rh;
            Transform root = an.transform;
            ClassicWeaponVisualLibrary library = ClassicWeaponVisualLibrary.Current;
            Assert.IsNotNull(library, "DD_ClassicWeapons yok.");

            // Socket bone: its rest (bind-pose) frame relative to its parent, before any clip drives it.
            Debug.Log("[DD-TEST] wp059.mount socket parent=" + (socket.parent != null ? socket.parent.name : "-") + " restLocalRot=" + socket.localRotation.eulerAngles.ToString("F1") +
                      " restLocalPos=" + V(socket.localPosition * 100f) + "cm rh=" + rh.name + " palm=" + palm.name);

            for (int w = 0; w < 3; w++)
            {
                pv.SetWeapon(w);
                for (int i = 0; i < 40; i++) yield return null;
                var id = (ClassicWeaponId)w;
                ClassicWeaponVisualLibrary.Entry entry = library.Find(id);
                Assert.IsNotNull(entry, id + " library entry");
                GameObject prefabInstance = UnityEngine.Object.Instantiate(entry.model);
                Quaternion importRot = prefabInstance.transform.localRotation;
                Vector3 importPos = prefabInstance.transform.localPosition;
                var mount = new GameObject("MountDiag_" + id).transform;
                mount.SetParent(socket, false);
                mount.localPosition = Vector3.zero;
                mount.localRotation = Quaternion.identity;
                prefabInstance.transform.SetParent(mount, false);
                prefabInstance.transform.localPosition = Vector3.zero;
                prefabInstance.transform.localRotation = Quaternion.identity;
                prefabInstance.transform.localScale = Vector3.one;
                yield return null;

                Transform muzzle = WeaponSocketContract.Find(prefabInstance.transform, WeaponSocketContract.Muzzle);
                Transform grip = WeaponSocketContract.Find(prefabInstance.transform, WeaponSocketContract.MarkerGrip);
                Transform stock = WeaponSocketContract.Find(prefabInstance.transform, WeaponSocketContract.AttachStock);
                Vector3 sockFwd = root.InverseTransformDirection(socket.forward);
                Vector3 sockUp = root.InverseTransformDirection(socket.up);
                Vector3 muzzleDir = muzzle != null ? root.InverseTransformDirection((muzzle.position - prefabInstance.transform.position).normalized) : Vector3.zero;
                float sockToHand = Vector3.Distance(socket.position, rh.position) * 100f;
                float sockToPalm = Vector3.Distance(socket.position, palm.position) * 100f;
                float gripToPalm = grip != null ? Vector3.Distance(grip.position, palm.position) * 100f : -1f;
                float gripToHand = grip != null ? Vector3.Distance(grip.position, rh.position) * 100f : -1f;
                Bounds b = new Bounds(prefabInstance.transform.position, Vector3.zero);
                foreach (Renderer r in prefabInstance.GetComponentsInChildren<Renderer>()) if (r.name.EndsWith("LOD0")) b.Encapsulate(r.bounds);
                float muzzleHeight = muzzle != null ? root.InverseTransformPoint(muzzle.position).y : -1f;
                float stockHeight = stock != null ? root.InverseTransformPoint(stock.position).y : -1f;
                Transform hand = rh;
                // Humanoid muscle values of the right hand / lower arm in this pose (±1 = the muscle range limit: the source pose was clamped).
                var poseHandler = new HumanPoseHandler(an.avatar, an.transform);
                var pose = new HumanPose();
                poseHandler.GetHumanPose(ref pose);
                var muscles = new System.Text.StringBuilder();
                for (int mi = 0; mi < HumanTrait.MuscleCount; mi++)
                {
                    string mn = HumanTrait.MuscleName[mi];
                    if (mn.StartsWith("Right Hand") || mn.StartsWith("Right Forearm") || mn.StartsWith("Right Arm"))
                    {
                        muscles.Append(mn).Append('=').Append(pose.muscles[mi].ToString("F2", CultureInfo.InvariantCulture)).Append(' ');
                    }
                }
                poseHandler.Dispose();
                Debug.Log("[DD-TEST] wp059.mount w=" + w + " " + id + " muscles: " + muscles);
                Debug.Log("[DD-TEST] wp059.mount w=" + w + " " + id + " socketLocalRot=" + socket.localRotation.eulerAngles.ToString("F1") + " socketLocalPos=" + V(socket.localPosition * 100f) + "cm handLocalRot=" + hand.localRotation.eulerAngles.ToString("F1") +
                          " rootFwdWorld=" + V(root.forward) + " visualFwd=" + V(visual.forward));
                Debug.Log("[DD-TEST] wp059.mount w=" + w + " " + id + " state=" + (an.GetCurrentAnimatorStateInfo(an.GetLayerIndex(C.OverrideUpperLayer)).IsName(C.AimState + "_" + w) ? "Aim_" + w : "?") +
                          " importRot=" + importRot.eulerAngles.ToString("F1") + " importPos=" + V(importPos * 100f) + "cm" +
                          " socketFwd=" + V(sockFwd) + " socketUp=" + V(sockUp) + " muzzleDir=" + V(muzzleDir) + " muzzleY=" + F(muzzleHeight * 100f) + "cm stockY=" + F(stockHeight * 100f) + "cm" +
                          " socket→hand=" + F(sockToHand) + "cm socket→palm=" + F(sockToPalm) + "cm grip→palm=" + F(gripToPalm) + "cm grip→hand=" + F(gripToHand) + "cm" +
                          " socketPos=" + V(root.InverseTransformPoint(socket.position)) + " handPos=" + V(root.InverseTransformPoint(rh.position)) + " boundsLOD0=" + V(b.size));
                UnityEngine.Object.Destroy(mount.gameObject);
                yield return null;
            }
        }
    }
}
