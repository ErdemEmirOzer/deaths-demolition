using System.Collections;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>WP-037 (AP-014) · the authored Special Weapon models reach the ground pickup and the hand rig; the lifecycle is unchanged.</summary>
    [TestFixture]
    [Category("WP037")]
    public sealed class SpecialWeaponVisualTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pickup_AP014ModeliVeAtlas_Elde_HandModeli_BitisteGizli()
        {
            SpecialWeaponVisualLibrary lib = SpecialWeaponVisualLibrary.Current;
            Assert.IsNotNull(lib, "DD_SpecialWeapons kütüphanesi yok.");

            _s = new SpecialWeaponSahne();
            yield return _s.Kur();

            // Force an authored weapon (Kagura) for a deterministic visual check.
            _s.Director.SetRandomSource(new ConstantRandom(0.1f));
            yield return _s.SpawnEt(0.5f);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup);
            SpecialWeaponId id = pickup.WeaponId;
            bool authoredExpected = lib.Find(id) != null;
            Assert.AreEqual(authoredExpected, pickup.UsesAuthoredModel, id + ": pickup görseli kütüphaneyle uyumsuz.");
            SpecialWeaponVisualLibrary.Entry entry = lib.Find(id);
            if (authoredExpected && entry.HasModel)
            {
                // WP-069 B4c: the AP-021 model on its side — its own materials, one LODGroup, no COL_* hull.
                Transform pose = null;
                foreach (Transform t in pickup.GetComponentsInChildren<Transform>(true)) if (t.name == "ModelPose") pose = t;
                Assert.IsNotNull(pose, "AP-021 pickup pozu yok");
                foreach (MeshRenderer r in pose.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Assert.IsFalse(r.name.StartsWith(SpecialWeaponVisualLibrary.ColliderPrefix), "pickup modelinde çarpışma kabuğu: " + r.name);
                    Assert.IsTrue(r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_AP021_"), "AP-021 malzemesi değil: " + r.name);
                }

                Assert.AreEqual(1, pose.GetComponentsInChildren<LODGroup>(true).Length, "tek LODGroup (parçalar gövdeyle birlikte geçer)");
            }
            else if (authoredExpected)
            {
                MeshRenderer[] renderers = pickup.GetComponentsInChildren<MeshRenderer>(true);
                bool atlas = false;
                foreach (MeshRenderer r in renderers)
                {
                    if (r.sharedMaterial == lib.material) atlas = true;
                }

                Assert.IsTrue(atlas, "Pickup modeli M_sw_atlas ile çizilmiyor.");
                Assert.IsNotNull(pickup.GetComponentInChildren<LODGroup>(true), "Pickup modelinde LODGroup yok.");
            }

            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
            // WP-042C / WP-032C: the hand model shows at the grab clip's pickup_done (0.73 s) or the 0.8 s fallback.
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "pickup_done / yedek");
            yield return null;
            Assert.IsTrue(_s.Controller.SpecialRigVisible, "Aktifken special rig görünür olmalı (pickup_done / 0,8 s yedek sonrası).");
            Assert.AreEqual(authoredExpected, _s.Controller.SpecialRigHasAuthoredModel, id + ": el modeli kütüphaneyle uyumsuz.");
            if (entry != null && entry.HasModel)
            {
                // WP-069 B4c: the rig took the AP-021 Muzzle socket (the orb / blob leaves from it) and carries no hull.
                Assert.AreEqual(DD.Prototype.Core.Weapons.WeaponSocketContract.Muzzle, _s.Controller.SpecialRigMuzzle.name);
                Assert.AreEqual(0, _s.Controller.SpecialRigBody.GetComponentsInChildren<Collider>(true).Length, "el modelinde collider yok");
                foreach (Transform t in _s.Controller.SpecialRigBody.GetComponentsInChildren<Transform>(true))
                {
                    Assert.IsFalse(t.name.StartsWith(SpecialWeaponVisualLibrary.ColliderPrefix), "el modelinde COL_ kabuğu: " + t.name);
                }
            }

            Time.captureDeltaTime = 0.05f;
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi + _s.Special.ActiveDuration, "Special süresi bitmedi.");
            Time.captureDeltaTime = 0f;
            yield return null;
            Assert.IsFalse(_s.Controller.SpecialRigVisible, "Bitişte special rig gizlenmeli.");
            Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, "Klasik geri dönmedi.");
        }

        private sealed class ConstantRandom : ISpecialWeaponRandom
        {
            private readonly float _v;
            public ConstantRandom(float v) { _v = v; }
            public float Value() => _v;
            public float Range(float min, float max) => min + (max - min) * _v;
            public int Range(int min, int maxExclusive) => min;
        }
    }
}
