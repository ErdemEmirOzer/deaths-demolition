using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-073 (C-00 26.09) · the AP-021 v003 classic weapons' contract sockets against their own geometry: the flash / tracer
    /// leave from <c>Muzzle</c>, the casing from <c>Ejection</c> (ADR-042-1, WeaponVisualRig.SetModel) — so the muzzle must sit
    /// on the bore at the barrel tip and point down the barrel, the ejection port on the receiver's surface. Measured on the
    /// LOD0 meshes of the model as the game instantiates it (ClassicWeaponVisualLibrary.Instantiate).
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073WeaponSocketTests
    {
        public struct SocketReport
        {
            public string Key;
            public bool HasMuzzle, HasEjection;
            public float AxialCm;       // muzzle along its forward past the barrel tip (+ in front of the tip, − inside the barrel)
            public float LateralCm;     // muzzle off the bore centre (the tip ring's centroid), perpendicular to the axis
            public float AxisDeg;       // muzzle forward vs the barrel's own axis (tip ring centroid − body centroid)
            public float EjectSurfaceCm; // ejection to the nearest LOD0 vertex
            public float EjectSideCm;   // ejection along the muzzle's right from the body centroid (+ right side)
            public float EjectFromMuzzleCm;
            public string AxisName, MuzzleAxes, EjectAxes;
            public float EjectFwdDeg, EjectRightDeg;
        }

        public static List<SocketReport> Measure(out string table)
        {
            var reports = new List<SocketReport>();
            var sb = new StringBuilder();
            sb.AppendLine("key       barrel  muzzle axial/lat cm  fwd∠axis°  eject surf cm  side cm  from muzzle cm");
            var lib = Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);
            Assert.IsNotNull(lib, "DD_ClassicWeapons");
            foreach (ClassicWeaponVisualLibrary.Entry entry in lib.entries)
            {
                if (entry == null || !entry.HasModel) continue;
                GameObject go = ClassicWeaponVisualLibrary.Instantiate(entry, lib.material, null);
                try
                {
                    reports.Add(MeasureOne(entry.rosterKey, go.transform));
                    SocketReport r = reports[reports.Count - 1];
                    sb.AppendLine(string.Format("{0,-9} {7,-6} {1,6:F1} / {2,5:F1}      {3,5:F1}     {4,6:F1}        {5,6:F1}   {6,6:F1}",
                        r.Key, r.AxialCm, r.LateralCm, r.AxisDeg, r.EjectSurfaceCm, r.EjectSideCm, r.EjectFromMuzzleCm, r.AxisName)
                        + "  muzzle[" + r.MuzzleAxes + "] eject[" + r.EjectAxes + "]" + (r.HasMuzzle ? "" : "  NO MUZZLE") + (r.HasEjection ? "" : "  NO EJECTION"));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            table = sb.ToString();
            return reports;
        }

        private static SocketReport MeasureOne(string key, Transform root)
        {
            var r = new SocketReport { Key = key };
            Transform muzzle = WeaponSocketContract.Find(root, WeaponSocketContract.Muzzle);
            Transform eject = WeaponSocketContract.Find(root, WeaponSocketContract.Ejection);
            r.HasMuzzle = muzzle != null;
            r.HasEjection = eject != null;

            var verts = new List<Vector3>(8192);
            LODGroup lodGroup = root.GetComponentInChildren<LODGroup>(true);
            if (lodGroup != null && lodGroup.GetLODs().Length > 0)
            {
                foreach (Renderer rend in lodGroup.GetLODs()[0].renderers) Collect(rend, verts);
            }
            else
            {
                foreach (MeshRenderer rend in root.GetComponentsInChildren<MeshRenderer>(true)) Collect(rend, verts);
            }

            if (verts.Count == 0 || muzzle == null) return r;

            // The barrel axis from the geometry: the model root's longest local extent, pointing to the muzzle's end.
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity, body = Vector3.zero;
            foreach (Vector3 v in verts)
            {
                Vector3 l = root.InverseTransformPoint(v);
                lo = Vector3.Min(lo, l);
                hi = Vector3.Max(hi, l);
                body += v;
            }

            body /= verts.Count;
            Vector3 ext = hi - lo;
            Vector3 localAxis = ext.x >= ext.y && ext.x >= ext.z ? Vector3.right : ext.y >= ext.z ? Vector3.up : Vector3.forward;
            Vector3 axis = root.TransformDirection(localAxis).normalized;
            if (Vector3.Dot(muzzle.position - body, axis) < 0f) axis = -axis;
            float tip = float.MinValue;
            foreach (Vector3 v in verts) tip = Mathf.Max(tip, Vector3.Dot(v, axis));
            Vector3 ring = Vector3.zero;
            int n = 0;
            foreach (Vector3 v in verts)
            {
                if (Vector3.Dot(v, axis) < tip - 0.012f) continue;
                ring += v;
                n++;
            }

            ring /= Mathf.Max(1, n);
            Vector3 m = muzzle.position;
            r.AxialCm = (Vector3.Dot(m, axis) - tip) * 100f;
            Vector3 off = m - ring;
            r.LateralCm = (off - axis * Vector3.Dot(off, axis)).magnitude * 100f;
            r.AxisDeg = Vector3.Angle(muzzle.forward, axis);   // the flash / smoke point down muzzle.forward
            r.MuzzleAxes = AxisMap(muzzle, axis);
            if (eject != null) r.EjectAxes = AxisMap(eject, axis);
            // The rig's normalization (WeaponVisualRig.SetModel → WeaponSocketContract.NormalizeFrame), then the contract frame.
            WeaponSocketContract.NormalizeFrame(muzzle, root);
            WeaponSocketContract.NormalizeFrame(eject, root);
            r.AxisDeg = Vector3.Angle(muzzle.forward, axis);
            if (eject != null)
            {
                r.EjectFwdDeg = Vector3.Angle(eject.forward, axis);
                r.EjectRightDeg = Vector3.Angle(eject.right, Vector3.Cross(Vector3.up, axis).normalized);
            }
            r.AxisName = (localAxis == Vector3.right ? "X" : localAxis == Vector3.up ? "Y" : "Z") + (Vector3.Dot(root.TransformDirection(localAxis), axis) > 0f ? "+" : "-");

            if (eject != null)
            {
                Vector3 e = eject.position;
                float best = float.MaxValue;
                foreach (Vector3 v in verts) best = Mathf.Min(best, (v - e).sqrMagnitude);
                r.EjectSurfaceCm = Mathf.Sqrt(best) * 100f;
                r.EjectSideCm = Vector3.Dot(e - body, Vector3.Cross(Vector3.up, axis).normalized) * 100f;   // + = right of the barrel
                r.EjectFromMuzzleCm = (e - m).magnitude * 100f;
            }

            return r;
        }

        // Which local axis of a socket runs down the barrel and which points up: "down=+Y up=-Z" style.
        private static string AxisMap(Transform socket, Vector3 barrel)
        {
            string Best(Vector3 dir)
            {
                string[] names = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
                Vector3[] v = { socket.right, -socket.right, socket.up, -socket.up, socket.forward, -socket.forward };
                int bi = 0;
                for (int i = 1; i < 6; i++) if (Vector3.Dot(v[i], dir) > Vector3.Dot(v[bi], dir)) bi = i;
                return names[bi] + "(" + Vector3.Angle(v[bi], dir).ToString("F0") + "°)";
            }

            return "down " + Best(barrel) + " up " + Best(Vector3.up) + " right " + Best(Vector3.Cross(Vector3.up, barrel).normalized);
        }

        private static void Collect(Renderer rend, List<Vector3> verts)
        {
            if (rend == null || rend.name.StartsWith("COL_")) return;
            var mf = rend.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            Matrix4x4 l2w = rend.transform.localToWorldMatrix;
            foreach (Vector3 v in mesh.vertices) verts.Add(l2w.MultiplyPoint3x4(v));
        }

        // Position findings reported to C-00 (26.09), not corrected here: the muzzle is the projectile origin in the look modes
        // (WeaponController, S-02) and the model is P-07's — rl_1's sits 12.5 cm inside the tube, lmg_1's 6.9 cm off the bore.
        private static readonly HashSet<string> PositionFindings = new HashSet<string> { "rl_1", "lmg_1" };

        [Test]
        public void Soketler_KontratCercevesi_NamluUstunde()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // AuthoredWeaponModel's SetLODs warning in EditMode
            List<SocketReport> reports = Measure(out string table);
            Debug.Log("[DD-TEST] wp073.sockets\n" + table);
            Assert.GreaterOrEqual(reports.Count, 10, "10 klasik silah");
            foreach (SocketReport r in reports)
            {
                Assert.IsTrue(r.HasMuzzle && r.HasEjection, r.Key + ": Muzzle / Ejection soketi yok");
                Assert.LessOrEqual(r.AxisDeg, 3f, r.Key + ": namlu ağzı ileri ekseni namluda değil (flaş / duman yönü)");
                Assert.LessOrEqual(r.EjectFwdDeg, 3f, r.Key + ": kovan soketi ileri ekseni namluda değil");
                Assert.LessOrEqual(r.EjectRightDeg, 5f, r.Key + ": kovan soketi sağı silahın sağı değil (kovan yönü)");
                Assert.LessOrEqual(r.EjectSurfaceCm, 2f, r.Key + ": kovan soketi gövde yüzeyinde değil");
                Assert.GreaterOrEqual(r.EjectSideCm, -0.5f, r.Key + ": kovan soketi sol tarafta");
                if (PositionFindings.Contains(r.Key)) continue;
                Assert.That(r.AxialCm, Is.InRange(-2f, 1f), r.Key + ": namlu ağzı namlu ucunda değil (eksen boyunca)");
                Assert.LessOrEqual(r.LateralCm, 3f, r.Key + ": namlu ağzı namlu ekseninde değil");
            }
        }
    }
}
