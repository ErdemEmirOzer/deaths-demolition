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
            if (authoredExpected)
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
            yield return null;
            Assert.IsTrue(_s.Controller.SpecialRigVisible, "Aktifken special rig görünür olmalı.");
            Assert.AreEqual(authoredExpected, _s.Controller.SpecialRigHasAuthoredModel, id + ": el modeli kütüphaneyle uyumsuz.");

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
