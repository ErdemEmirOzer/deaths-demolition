using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 B4d / B4e · the AP-021 (P-07 v002/v003) classics — all ten — in DD_ClassicWeapons: an own-material entry per runtime id, the
    /// ADR-042-1 frame (Muzzle on +Z, Ejection on +X — a mirrored export fails), the contract sockets, three LODs within
    /// P-07's budget (20 000 / 6 000 / 1 800), the body material with its maps (MRA → derived URP mask).
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class Ap021ClassicModelGateTests
    {
        private static ClassicWeaponVisualLibrary Library => Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);

        [TestCase(ClassicWeaponId.AssaultRifle)]
        [TestCase(ClassicWeaponId.AutoSmg)]
        [TestCase(ClassicWeaponId.CombatShotgun)]
        [TestCase(ClassicWeaponId.Ar2)]
        [TestCase(ClassicWeaponId.Smg2)]
        [TestCase(ClassicWeaponId.Sg2)]
        [TestCase(ClassicWeaponId.Lmg1)]
        [TestCase(ClassicWeaponId.Lmg2)]
        [TestCase(ClassicWeaponId.Sr1)]
        [TestCase(ClassicWeaponId.Rl1)]
        public void Ap021Klasik_CerceveSoketLodVeMalzeme(ClassicWeaponId id)
        {
            ClassicWeaponVisualLibrary.Entry e = Library.Find(id);
            Assert.IsNotNull(e, id + ": kütüphane girdisi yok (ArtAp021WeaponBuilder.BuildClassics)");
            Assert.IsTrue(e.ownMaterials && e.HasModel, id + ": AP-021 girdisi değil");
            Assert.AreEqual("AP-021", e.modelPackage);
            Transform root = e.model.transform;
            foreach (string socket in new[] { WeaponSocketContract.Muzzle, WeaponSocketContract.Ejection, WeaponSocketContract.MarkerGrip,
                         WeaponSocketContract.MarkerForegrip, WeaponSocketContract.AttachBarrel, WeaponSocketContract.AttachTop, WeaponSocketContract.AttachStock })
            {
                Assert.IsNotNull(WeaponSocketContract.Find(root, socket), id + ": soket yok " + socket);
            }

            Vector3 muzzle = root.InverseTransformPoint(WeaponSocketContract.Find(root, WeaponSocketContract.Muzzle).position);
            Vector3 port = root.InverseTransformPoint(WeaponSocketContract.Find(root, WeaponSocketContract.Ejection).position);
            Assert.Greater(muzzle.z, 0.3f, id + ": namlu +Z — " + muzzle.ToString("F3"));   // the compact smg_2 0.366 m; a barrel-up / mirrored export sits near 0 or below
            Assert.Less(Mathf.Abs(muzzle.x), 0.05f, id + ": namlu eksende");
            // The port sits on +X (ar_2's right-wall cover) or on the centre line (sg_2's break action ejects rearward) — never on -X (a mirrored export).
            Assert.GreaterOrEqual(port.x, -1e-3f, id + ": atım portu -X'te (aynalı dışa aktarım) — " + port.ToString("F3"));

            int[] budget = { 20000, 6000, 1800 };
            for (int lod = 0; lod < ClassicWeaponVisualLibrary.LodCount; lod++)
            {
                Assert.Greater(e.lodTriangles[lod], 0, id + " LOD" + lod);
                Assert.LessOrEqual(e.lodTriangles[lod], budget[lod], id + " LOD" + lod + " " + e.lodTriangles[lod]);
            }

            Material body = null;
            foreach (Renderer r in e.model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials) if (m != null && m.name.EndsWith("_body")) body = m;
            }

            Assert.IsNotNull(body, id + ": gövde malzemesi eşlenmedi");
            // In hand every slot is an AP-021 material — straight from the FBX: an empty slot comes in as the pipeline "Lit" and fails
            // (P-07 v003 filled lmg_1 part_trigger_LOD2; the B4e equip-time fill is gone).
            GameObject hand = ClassicWeaponVisualLibrary.Instantiate(e, Library.material, new[] { 0.12f, 0.05f, 0.01f });
            try
            {
                foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        Assert.IsTrue(m != null && m.name.StartsWith(AuthoredWeaponModel.OwnMaterialPrefix), id + ": " + r.name + " AP-021 malzemesi değil (" + (m != null ? m.name : "null") + ")");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(hand);
            }

            Assert.IsNotNull(body.GetTexture("_BaseMap"));
            Assert.IsNotNull(body.GetTexture("_BumpMap"));
            Assert.IsNotNull(body.GetTexture("_MetallicGlossMap"), "MRA'dan türetilmiş maske");
        }

        [Test]
        public void Ap021Klasik_ElModeli_TekLodGrubu_KabukYok()
        {
            ClassicWeaponVisualLibrary lib = Library;
            ClassicWeaponVisualLibrary.Entry e = lib.Find(ClassicWeaponId.Sg2);
            Assert.IsNotNull(e);
            GameObject go = ClassicWeaponVisualLibrary.Instantiate(e, lib.material, new[] { 0.12f, 0.05f, 0.01f });
            try
            {
                Assert.AreEqual(1, go.GetComponentsInChildren<LODGroup>(true).Length, "tek LODGroup");
                Assert.AreEqual(3, go.GetComponent<LODGroup>().GetLODs().Length);
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    Assert.IsFalse(t.name.StartsWith(AuthoredWeaponModel.ColliderPrefix), "COL_ kabuğu: " + t.name);
                }

                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Assert.AreNotEqual(lib.material, r.sharedMaterial, "atlas zorlanmaz: " + r.name);
                }

                Assert.IsNotNull(WeaponSocketContract.Find(go.transform, "Muzzle_2"), "sg_2 üst namlu");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
