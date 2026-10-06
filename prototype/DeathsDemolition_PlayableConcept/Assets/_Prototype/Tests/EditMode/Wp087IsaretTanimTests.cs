using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-087 (C-00 05.10) · marker ↔ definition for the ten classic weapons, on the model as the game instantiates it: the
    /// definition's muzzle / ejection socket names resolve to the AP-021 model's ADR-042-1 sockets, and those sit where the flash,
    /// the shot and the casing are supposed to leave — the muzzle at the barrel's end (rl_1: the tube's, the seated rocket left
    /// out) on a bore (a double barrel: the nearer one), the ejection port just off the receiver on its right (point ↔ triangle,
    /// ≤ 2 cm out, never inside — a shell born in the metal clips it), its +X the weapon's right (the eject axis). Muzzle ≤ 1 cm,
    /// axes ≤ 10° (C-00 05.10 proposal).
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087IsaretTanimTests
    {
        private static readonly ClassicWeaponId[] Ids =
        {
            ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Ar2, ClassicWeaponId.Smg2,
            ClassicWeaponId.Sg2, ClassicWeaponId.Lmg1, ClassicWeaponId.Lmg2, ClassicWeaponId.Sr1, ClassicWeaponId.Rl1
        };

        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>
        /// WP-087 (C-00 06.10, main-ci 173890ac: "SetLODs: … greater then or equal to a higher detail LOD level"): an authored model
        /// built without a screen-height row (this fixture's geometry reads, the EditMode socket gates) or with a bad one gets LOD
        /// heights that fall strictly from LOD0 down — no SetLODs error (the runner fails any unhandled error log).
        /// </summary>
        [Test]
        public void AuthoredModel_YukseklikSirasiYoksaYaDaBozuksa_LodlarKesinAzalir()
        {
            var lib = Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);
            Assert.IsNotNull(lib, "DD_ClassicWeapons");
            int multiLevel = 0;
            foreach (ClassicWeaponId id in Ids)
            {
                ClassicWeaponVisualLibrary.Entry entry = lib.Find(id);
                Assert.IsTrue(entry != null && entry.HasModel, id + ": model yok");
                foreach (float[] row in new[] { null, new[] { 0.05f, 0.05f, 0.05f }, new[] { 0.10f } })
                {
                    GameObject go = ClassicWeaponVisualLibrary.Instantiate(entry, lib.material, row);
                    try
                    {
                        LOD[] lods = go.GetComponent<LODGroup>().GetLODs();
                        if (lods.Length > 1 && row == null) multiLevel++;
                        for (int i = 1; i < lods.Length; i++)
                            Assert.Less(lods[i].screenRelativeTransitionHeight, lods[i - 1].screenRelativeTransitionHeight,
                                id + " LOD" + i + " (sıra " + (row == null ? "yok" : string.Join("/", row)) + ")");
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }

            Assert.Greater(multiLevel, 0, "kaynak kayıtta: en az bir modelin birden çok LOD seviyesi var");
        }

        [Test]
        public void IsaretTanim_NamluVeKovan_ModelGeometrisinde()
        {
            var lib = Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);
            Assert.IsNotNull(lib, "DD_ClassicWeapons");
            var rows = new List<string>();
            var wrong = new List<string>();
            foreach (ClassicWeaponId id in Ids)
            {
                ClassicWeaponVisualLibrary.Entry entry = lib.Find(id);
                ClassicWeaponDefinition def = WeaponRoster.Find(id);   // the roster row the game reads (its socket names copied from the definition asset)
                Assert.IsTrue(entry != null && entry.HasModel, id + ": model yok");
                Assert.IsNotNull(def, id + ": tanım yok");
                GameObject go = ClassicWeaponVisualLibrary.Instantiate(entry, lib.material, null);
                try
                {
                    Transform root = go.transform;
                    Transform muzzle = WeaponSocketContract.Find(root, WeaponSocketContract.Muzzle);
                    Transform port = WeaponSocketContract.Find(root, WeaponSocketContract.Ejection);
                    string key = entry.rosterKey;
                    // marker ↔ definition: the names the definition carries resolve to the sockets the game reads
                    if (WeaponSocketContract.Find(root, def.MuzzleSocket) != muzzle || muzzle == null) wrong.Add(key + ": tanımın namlu adı '" + def.MuzzleSocket + "' modelin Muzzle soketi değil");
                    if (WeaponSocketContract.Find(root, def.EjectionSocket) != port || port == null) wrong.Add(key + ": tanımın kovan adı '" + def.EjectionSocket + "' modelin Ejection soketi değil");
                    if (muzzle == null || port == null) continue;
                    WeaponSocketContract.NormalizeFrame(muzzle, root);
                    WeaponSocketContract.NormalizeFrame(port, root);

                    // LOD0 geometry in the model frame; the seated rocket (rl_1) and the magazine are parts, not the barrel / receiver
                    var verts = new List<Vector3>(16384);
                    var tris = new List<int>(32768);
                    LODGroup group = go.GetComponentInChildren<LODGroup>(true);
                    var renderers = new List<Renderer>();
                    if (group != null && group.lodCount > 0) renderers.AddRange(group.GetLODs()[0].renderers);
                    Assert.IsNotEmpty(renderers, key + ": LOD0 renderer yok");
                    foreach (Renderer r in renderers)
                    {
                        if (r == null || UnderPart(r.transform, root)) continue;
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        Mesh mesh = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                        if (mesh == null) continue;
                        int baseIndex = verts.Count;
                        foreach (Vector3 v in mesh.vertices) verts.Add(root.InverseTransformPoint(r.transform.TransformPoint(v)));
                        foreach (int t in mesh.triangles) tris.Add(baseIndex + t);
                    }

                    Assert.Greater(tris.Count, 300, key + ": LOD0 üçgeni yok denecek kadar az");
                    Vector3 m = root.InverseTransformPoint(muzzle.position);
                    float tip = float.NegativeInfinity;
                    foreach (Vector3 v in verts) if (Mathf.Abs(v.x) < 0.06f && v.y > -0.12f && v.y < 0.15f) tip = Mathf.Max(tip, v.z);
                    // the tip ring: vertices within 1 cm of the barrel's end; a double barrel splits along its wider axis
                    var ring = new List<Vector2>();
                    foreach (Vector3 v in verts) if (v.z >= tip - 0.01f && Mathf.Abs(v.x) < 0.06f && v.y > -0.12f && v.y < 0.15f) ring.Add(new Vector2(v.x, v.y));
                    float lateral = NearestBore(ring, new Vector2(m.x, m.y), out int bores);
                    float axial = (m.z - tip) * 100f;
                    Vector3 e = root.InverseTransformPoint(port.position);
                    float surface = SurfaceDistance(e, verts, tris, out float outward) * 100f;
                    float ejectDeg = Vector3.Angle(root.InverseTransformDirection(port.right), Vector3.right);
                    float muzzleDeg = Vector3.Angle(root.InverseTransformDirection(muzzle.forward), Vector3.forward);
                    rows.Add(key + " muzzle axial " + F(axial) + " cm, lateral " + F(lateral * 100f) + " cm (" + bores + " bore" + (bores > 1 ? "s" : "") + "), fwd∠+Z " + F(muzzleDeg)
                             + "° | port surface " + F(surface) + " cm (" + (outward >= -0.002f ? "outside" : "INSIDE") + "), +X∠right " + F(ejectDeg) + "° | muzzle " + V(m) + " port " + V(e));
                    if (Mathf.Abs(axial) > 1f) wrong.Add(key + ": namlu soketi namlu ucundan " + F(axial) + " cm");
                    if (lateral * 100f > 1f) wrong.Add(key + ": namlu soketi namlu ekseninden " + F(lateral * 100f) + " cm");
                    if (muzzleDeg > 10f) wrong.Add(key + ": namlu soketi ileri ekseni namluda değil (" + F(muzzleDeg) + "°)");
                    // the port stands just off the receiver on the eject side (a shell born inside the metal clips it): ≤ 2 cm out, never in;
                    // a weapon that ejects no casing (rl_1, VfxWeaponProfile.HasCasing false) is not held to it
                    DD.Prototype.Feedback.Vfx.VfxWeaponProfile profile = DD.Prototype.Feedback.Vfx.VfxWeaponProfiles.For(id);
                    bool casing = profile == null || profile.HasCasing;
                    if (casing && surface > 2f) wrong.Add(key + ": kovan soketi gövde yüzeyinden " + F(surface) + " cm");
                    if (casing && outward < -0.002f) wrong.Add(key + ": kovan soketi gövdenin içinde (" + F(outward * 100f) + " cm)");
                    if (casing && ejectDeg > 10f) wrong.Add(key + ": kovan soketi +X silahın sağı değil (" + F(ejectDeg) + "°)");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            Debug.Log("[DD-TEST] wp087.markerdef\n" + string.Join("\n", rows));
            Assert.AreEqual(Ids.Length, rows.Count, "on klasik silahın hepsi ölçülmeli");
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }

        private static string V(Vector3 v) => "(" + F(v.x * 100f) + ", " + F(v.y * 100f) + ", " + F(v.z * 100f) + ") cm";

        /// <summary>The seated rocket and the magazine are separate parts (part_rocket / part_mag), not the barrel or the receiver.</summary>
        private static bool UnderPart(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name.StartsWith("part_rocket") || t.name.StartsWith("part_mag")) return true;
            return false;
        }

        /// <summary>The muzzle's distance to the nearest bore centre of the tip ring (one bore, or two split along the wider axis).</summary>
        private static float NearestBore(List<Vector2> ring, Vector2 muzzle, out int bores)
        {
            bores = 0;
            if (ring.Count == 0) return float.PositiveInfinity;
            Vector2 min = ring[0], max = ring[0], sum = Vector2.zero;
            foreach (Vector2 p in ring) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); sum += p; }
            Vector2 centre = sum / ring.Count;
            Vector2 size = max - min;
            float single = (muzzle - centre).magnitude;
            bores = 1;
            if (Mathf.Max(size.x, size.y) < 0.045f) return single;   // a wider ring may be two bores (sg_2) — or a sight / bipod at the tip
            bool alongX = size.x >= size.y;
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int na = 0, nb = 0;
            foreach (Vector2 p in ring)
            {
                if ((alongX ? p.x - centre.x : p.y - centre.y) < 0f) { a += p; na++; } else { b += p; nb++; }
            }

            float da = na > 0 ? (muzzle - a / na).magnitude : float.PositiveInfinity, db = nb > 0 ? (muzzle - b / nb).magnitude : float.PositiveInfinity;
            float split = Mathf.Min(da, db);
            if (split < single) { bores = 2; return split; }
            return single;
        }

        private static float SurfaceDistance(Vector3 p, List<Vector3> verts, List<int> tris, out float outward)
        {
            float best = float.PositiveInfinity;
            Vector3 nearest = p;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 q = ClosestOnTriangle(p, verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]);
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; nearest = q; }
            }

            outward = p.x - nearest.x;   // + : the port stands off the surface on the weapon's right (the eject side)
            return Mathf.Sqrt(best);
        }

        // Ericson, Real-Time Collision Detection §5.1.5
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}
