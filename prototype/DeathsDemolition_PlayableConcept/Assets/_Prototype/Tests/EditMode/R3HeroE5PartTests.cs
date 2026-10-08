using DD.Prototype.Characters;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    [TestFixture]
    public sealed class R3HeroE5PartTests
    {
        [TestCase("lmg_1")]
        [TestCase("lmg_2")]
        [TestCase("sw_gel")]
        [TestCase("sw_arc")]
        public void CalibratedPart_RotatedScaledSocket_AndExactHome(string key)
        {
            GameObject weapon = new GameObject("AP021_" + key), hand = new GameObject("socket_mag_l");
            try
            {
                weapon.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(17f, 42f, -23f));
                hand.transform.SetPositionAndRotation(new Vector3(-2f, 1f, 5f), Quaternion.Euler(43f, 127f, 71f));
                hand.transform.localScale = Vector3.one * 1.3f;
                string[] names = key == "sw_arc" ? ReloadPartCalibration.ArcGroup : new[] { ReloadPartCalibration.PartName(key) };
                var parts = new Transform[names.Length];
                var homes = new Vector3[names.Length];
                var rotations = new Quaternion[names.Length];
                var scales = new Vector3[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    parts[i] = new GameObject(names[i]).transform;
                    parts[i].SetParent(weapon.transform, false);
                    homes[i] = parts[i].localPosition = new Vector3(i * 0.07f, 0.2f, -0.3f);
                    rotations[i] = parts[i].localRotation = Quaternion.Euler(0f, 21f, 0f);
                    scales[i] = parts[i].localScale;
                }
                Matrix4x4 relative = parts[0].worldToLocalMatrix * parts[parts.Length - 1].localToWorldMatrix;
                var visual = new WeaponMagazineVisual();
                visual.Bind(weapon.transform, key);
                ReloadPartCalibration.ConfigureSocket(hand.transform, key);
                Assert.IsTrue(visual.ToHand(hand.transform));
                Assert.IsTrue(ReloadPartCalibration.TryGet(key, out Matrix4x4 m));
                Assert.Less(Vector3.Distance(parts[0].position, hand.transform.TransformPoint(m.GetColumn(3))), 0.00001f);
                Assert.Less(Quaternion.Angle(parts[0].rotation, hand.transform.rotation * m.rotation), 0.01f);
                if (parts.Length > 1)
                {
                    Matrix4x4 actual = parts[0].worldToLocalMatrix * parts[parts.Length - 1].localToWorldMatrix;
                    Assert.Less(Vector3.Distance(actual.GetColumn(3), relative.GetColumn(3)), 0.00001f, "3-cell group relative arrangement");
                    Assert.Less(Quaternion.Angle(actual.rotation, relative.rotation), 0.01f);
                }
                Assert.IsTrue(visual.ToHand(hand.transform));
                Assert.AreEqual(1, visual.ToHandCount, "repeated beat is idempotent");
                visual.Home();
                for (int i = 0; i < parts.Length; i++)
                {
                    Assert.AreSame(weapon.transform, parts[i].parent);
                    Assert.AreEqual(homes[i], parts[i].localPosition);
                    Assert.AreEqual(rotations[i], parts[i].localRotation);
                    Assert.AreEqual(scales[i], parts[i].localScale);
                }
                Assert.IsFalse(visual.InHand);
            }
            finally { Object.DestroyImmediate(weapon); Object.DestroyImmediate(hand); }
        }

        [Test]
        public void SpecialAliases_MapToVisualOwnershipBeats()
        {
            foreach (CharacterAnimationEvent e in new[] { CharacterAnimationEvent.CanisterOut, CharacterAnimationEvent.CellOut })
            { Assert.IsTrue(ReloadPartCalibration.TryStep(e, out MagazineStep s)); Assert.AreEqual(MagazineStep.Detach, s); }
            foreach (CharacterAnimationEvent e in new[] { CharacterAnimationEvent.CanisterIn, CharacterAnimationEvent.CellIn })
            { Assert.IsTrue(ReloadPartCalibration.TryStep(e, out MagazineStep s)); Assert.AreEqual(MagazineStep.Insert, s); }
        }
    }
}
