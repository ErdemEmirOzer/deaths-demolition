using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-082 · hero v2 hold pre-study (C-00 05.10; measurement only, no import): for every enabled classic weapon, in third person,
    /// standing in the aim pose, the current player's left arm and the two-bone grip goal (WeaponGripIk's grip_l) in the character's
    /// frame around UpperChest, its arm segments, shoulders and height, and where socket_weapon_r sits in the right hand. One JSON line
    /// per weapon (wp082.heroreach); the hero v2 (AP-024-B, AP-026 default77 template) reach / elbow / wrist table is computed from
    /// these offline (K/wp-082/evidence/hero-v2-reach/). [Explicit]: run by its full name.
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class Wp082HeroV2ErisimTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            DD.Prototype.CameraSystem.CameraModeService.ResetForTests();
            WeaponHoldIk.ResetStatics();
        }

        private static string V(Vector3 v) => "[" + v.x.ToString("0.0000", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.0000", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.0000", CultureInfo.InvariantCulture) + "]";
        private static string F(float f) => f.ToString("0.0000", CultureInfo.InvariantCulture);

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator HeroV2_Erisim_OlcumTablosu()
        {
            // the owner's A/B switch (as R2HeroOlcumTests): DD_HOLD_IK=1 measures with the WP-083 B2c weapon-first hold on
            WeaponHoldIk.Enabled = System.Environment.GetEnvironmentVariable("DD_HOLD_IK") == "1";
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Animator a = body.Animator;
            Transform chest = a.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = a.GetBoneTransform(HumanBodyBones.Chest);
            Transform lUa = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), lLa = a.GetBoneTransform(HumanBodyBones.LeftLowerArm), lH = a.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rUa = a.GetBoneTransform(HumanBodyBones.RightUpperArm), rH = a.GetBoneTransform(HumanBodyBones.RightHand);
            Transform head = a.GetBoneTransform(HumanBodyBones.Head), hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Transform root = _s.Rig.transform;
            Assert.IsTrue(chest != null && lUa != null && lLa != null && lH != null && rUa != null && rH != null, "kol kemikleri yok");
            int lines = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(90);   // switch-in played out, aim pose settled (game frames)
                WeaponGripIk ik = body.GripIk;
                Transform grip = ik != null ? ik.Grip : null;
                // the character frame: origin UpperChest, axes the player root's (x right, y up, z forward)
                Quaternion inv = Quaternion.Inverse(root.rotation);
                Vector3 R(Vector3 p) => inv * (p - chest.position);
                string json = "{\"weapon\":\"" + d.RosterKey + "\""
                    + ",\"shoulderL\":" + V(R(lUa.position)) + ",\"elbowL\":" + V(R(lLa.position)) + ",\"wristL\":" + V(R(lH.position))
                    + ",\"shoulderR\":" + V(R(rUa.position)) + ",\"wristR\":" + V(R(rH.position))
                    + ",\"socketR\":" + V(R(body.WeaponSocket.position)) + ",\"socketInHandR\":" + V(rH.InverseTransformPoint(body.WeaponSocket.position))
                    + ",\"grip\":" + (grip != null ? V(R(grip.position)) : "null")
                    + ",\"gripFwd\":" + (grip != null ? V(inv * grip.forward) : "null")
                    + ",\"barrel\":" + V(inv * body.WeaponSocket.forward)
                    + ",\"upperL\":" + F((lUa.position - lLa.position).magnitude) + ",\"foreL\":" + F((lLa.position - lH.position).magnitude)
                    + ",\"ikWeight\":" + F(ik != null ? ik.Weight : -1f) + ",\"ikErrorCm\":" + F(ik != null && !float.IsNaN(ik.LastError) ? ik.LastError * 100f : -1f)
                    + ",\"headY\":" + F(head != null ? head.position.y - root.position.y : -1f) + ",\"hipsY\":" + F(hips != null ? hips.position.y - root.position.y : -1f)
                    + ",\"chestY\":" + F(chest.position.y - root.position.y)
                    + ",\"gripOffset\":" + V(_s.Controller.Classic.Definition != null ? _s.Controller.Classic.Definition.GripOffset : Vector3.zero) + "}";
                Debug.Log("[DD-TEST] wp082.heroreach hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " " + json);
                lines++;
            }

            Assert.Greater(lines, 0, "ölçülen silah yok");
        }
    }
}
