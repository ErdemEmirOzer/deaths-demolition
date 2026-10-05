using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 hero gates (S-06, tools/qa/r2-gates.json, measured on the default player path — no test switch).
    /// <para>
    /// H1 "hand to grip socket distance": for every enabled classic weapon, in third person, standing then walking, maxCm is the
    /// largest of two distances, in cm:
    /// <list type="bullet">
    /// <item>right — the hand's socket_weapon_r ↔ the definition's grip point (GripOffset in the mount frame), the point WeaponMountAlignment
    /// puts on the socket (C-00 05.10, H1 decision B: grip decision B of 25.09 stands — the AP-021 marker_grip is the model origin, the
    /// trigger root; each GripOffset is checked against the LOD0 grip region by Ap021ClassicModelGateTests);</item>
    /// <item>left — the left hand ↔ WeaponGripIk's goal while the grip IK carries full weight.</item>
    /// </list>
    /// Frames of reload / switch, where the clip takes the hands off, are not counted.
    /// </para>
    /// The default path has the WP-083 B2c hold off; DD_HOLD_IK=1 (the owner's switch) measures it on.
    /// [Explicit]: the R2 chain runs it by its full name; it writes r2-H1.json through R2Olcum when DD_R2_MEASURE_DIR is set and
    /// fails on the contract's limit itself.
    /// </summary>
    [TestFixture]
    [Category("WP083")]
    public sealed class R2HeroOlcumTests
    {
        public const string H1Name = "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus";
        private SpecialWeaponSahne _s;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // the owner's A/B switch (r2-measure-contract measuredBy.env): DD_HOLD_IK=1 measures with the WP-083 B2c weapon-first hold on
            WeaponHoldIk.Enabled = System.Environment.GetEnvironmentVariable("DD_HOLD_IK") == "1";
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            WeaponHoldIk.ResetStatics();
        }

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Transform socket = body.WeaponSocket, lHand = body.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            Transform mount = binding != null ? binding.WeaponMount : null;
            Assert.IsNotNull(mount, "silah montajı yok");
            var alignment = mount.GetComponent<WeaponMountAlignment>();
            var report = new List<string>();
            float maxRight = 0f, maxLeft = 0f;
            int samples = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);   // the switch-in has played out
                Assert.IsNotNull(alignment, "R2 H1: WeaponMountAlignment yok — tutamak noktası ölçülemez");
                float right = 0f, left = 0f;
                int n = 0, nLeft = 0;
                for (int f = 0; f < 180; f++)
                {
                    _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;   // stand 1 s, walk 1.5 s, stop
                    yield return null;
                    if (_s.Controller.Classic != null && _s.Controller.Classic.IsReloading) continue;
                    Vector3 grip = mount.TransformPoint(alignment.GripOffset);
                    right = Mathf.Max(right, Vector3.Distance(socket.position, grip));
                    n++;
                    WeaponGripIk ik = body.GripIk;
                    if (ik != null && ik.Weight >= 1f && !float.IsNaN(ik.LastError)) { left = Mathf.Max(left, ik.LastError); nLeft++; }
                }

                _s.Girdi.Yon = Vector2.zero;
                maxRight = Mathf.Max(maxRight, right);
                maxLeft = Mathf.Max(maxLeft, left);
                samples += n;
                report.Add(d.RosterKey + " right " + (right * 100f).ToString("F2", CultureInfo.InvariantCulture) + " (grip point)" +
                           " left " + (nLeft > 0 ? (left * 100f).ToString("F2", CultureInfo.InvariantCulture) + " (n " + nLeft + ")" : "n/a") + " cm, frames " + n);
            }

            float maxCm = Mathf.Max(maxRight, maxLeft) * 100f;
            Debug.Log("[DD-TEST] wp083.h1 hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " maxCm=" + maxCm.ToString("F2", CultureInfo.InvariantCulture) + " :: " + string.Join(" | ", report));
            R2Olcum.Yaz("H1", H1Name, new Dictionary<string, double> { { "maxCm", maxCm } }, samples);
            Assert.Greater(samples, 0, "ölçülen kare yok");
            Assert.LessOrEqual(maxCm, 1.0f, "R2 H1: el ↔ tutamak > 1 cm\n" + string.Join("\n", report));
        }
    }
}
