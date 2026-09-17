using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-042 · ADR-042-1 gate on the AP-009C weapon blockouts (the placeholders at
    /// socket_weapon_r until AP-013): every enabled roster weapon names a model resource that
    /// loads, carries the contract sockets at the AP-009C measures (±3 cm), is a rigid
    /// single-mesh prop inside the WP-034 attachment budget and has no collider.
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponBlockoutGateTests
    {
        [TearDown]
        public void TearDown()
        {
            WeaponRoster.SetForTests(null);
        }

        [Test]
        public void HerEtkinSilah_BlockoutModeli_Yuklenir_SoketleriSozlesmede()
        {
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                Assert.IsFalse(string.IsNullOrEmpty(d.ModelResource), d.RosterKey + ": ModelResource boş (ADR-042-1 yer tutucu).");
                var model = Resources.Load<GameObject>(d.ModelResource);
                Assert.IsNotNull(model, d.RosterKey + ": " + d.ModelResource + " Resources'ta yok.");

                List<string> faults = WeaponSocketContract.Validate(model.transform, d.TypeClass);
                Assert.IsEmpty(faults, d.RosterKey + " (" + d.ModelResource + "): " + string.Join("; ", faults));

                // Rigid single mesh, no collider, inside the attachment-scale budget (a blockout is measure-only).
                Assert.IsNull(model.GetComponentInChildren<SkinnedMeshRenderer>(), d.RosterKey + ": blockout rijit olmalı.");
                Assert.IsNull(model.GetComponentInChildren<Collider>(), d.RosterKey + ": el modelinde collider yok.");
                MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>();
                Assert.Greater(filters.Length, 0, d.RosterKey + ": mesh yok.");
                int triangles = 0;
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i].sharedMesh != null) triangles += filters[i].sharedMesh.triangles.Length / 3;
                }

                Assert.LessOrEqual(triangles, WeaponSocketContract.AttachmentLod0TriangleBudget, d.RosterKey + ": blockout " + triangles + " tri (AP-009C 104–148).");
                Assert.Greater(triangles, 0);

                // The muzzle sits on the barrel axis ahead of the grip and the ejection port opens to +X.
                Transform muzzle = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Muzzle);
                Vector3 local = model.transform.InverseTransformPoint(muzzle.position);
                Assert.Greater(local.z, 0.4f, d.RosterKey + ": namlu ucu +Z'de.");
                Assert.Less(Mathf.Abs(local.x), 0.03f, d.RosterKey + ": namlu ucu eksende.");
                Transform ejection = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Ejection);
                Vector3 ejectDir = model.transform.InverseTransformDirection(ejection.right);
                Assert.Greater(ejectDir.x, 0.7f, d.RosterKey + ": kovan portu +X'e bakar.");

                Debug.Log("[DD-TEST] wp042.blockout " + d.RosterKey + " tri=" + triangles + " muzzleZ=" + local.z.ToString("0.000"));
            }
        }

        [Test]
        public void D_KapaliTanim_ModelKaynagiYok()
        {
            ClassicWeaponDefinition d = WeaponRoster.FindByKey("D");
            Assert.IsNotNull(d);
            Assert.IsTrue(string.IsNullOrEmpty(d.ModelResource), "D için model üretilmez (DEC-083).");
        }
    }
}
