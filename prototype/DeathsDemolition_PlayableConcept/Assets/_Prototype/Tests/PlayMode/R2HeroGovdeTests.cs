using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 hero gates H2 and H6 (S-06, tools/qa/r2-gates.json), measured on the default player path (the WP-083 hold and the
    /// B2f grasp on; DD_HOLD_IK=0 / DD_GRASP=0 are the owner's A/B switches, as in <see cref="R2HeroOlcumTests"/>). Every enabled
    /// classic weapon (WeaponRoster.Enabled), third person, from one spot: stand 1 s, walk 1.5 s, stop; reload frames are not counted.
    /// <para>
    /// H2 clipFrames — the sampled frames (every 3rd) in which any LOD0 weapon point sits inside the AP-006 LOD0 skin by more than
    /// <see cref="ClipToleranceM"/> where that skin belongs to the chest / upper chest / shoulders / upper arms. Weapon points = the
    /// LOD0 vertices + their edges split every 1.5 cm. Inside = KundakOmuz's own measure (WeaponModelSliceTests.SkinnedPenetration):
    /// depth behind the nearest baked skin vertex along its normal. The skin's region = the bone segment nearest to that vertex
    /// (the matrix's "shoulder / chest capsules": Chest→UpperChest, UpperChest→Neck, Shoulder→UpperArm, UpperArm→LowerArm, both
    /// sides). The designed contact is the stock in the shoulder pocket: the weapon's rearmost <see cref="StockZoneM"/> along the
    /// stock→muzzle line from attach_stock is held to KundakOmuz's limit (<see cref="StockAllowanceM"/>) instead — H2 never counts
    /// what KundakOmuz allows, and a stock sunk deeper than that is a clip.
    /// </para>
    /// <para>
    /// H6 — the real game camera (Camera.main) in ThirdPerson, every 5th frame, three renders into one 1280×720 target with the
    /// weapon's renderers on one free layer (flat unlit red) and the player's own renderers on another (flat unlit green), post,
    /// fog and MSAA off: (a) weapon alone → mask M; (b) weapon + body, depth tested → visible V = mask pixels still red; (c) body
    /// alone → silhouette S. Share per weapon = mean over its frames of V/M (frames whose M is 0 — the weapon outside the frustum —
    /// are left out and logged); visibleShareMin = the worst weapon, secondWorstShare the next. muzzleInsideSilhouetteFrames = the
    /// frames whose Muzzle socket projects onto an S pixel (in front of the camera). The occluder is the player's own body only, so
    /// the value does not depend on world props near the fixture's spot. Materials, layers, property blocks and the camera are put
    /// back in a finally on every render.
    /// </para>
    /// [Explicit]: the R2 chain runs each by its full name; each writes r2-&lt;gate&gt;.json through R2Olcum (DD_R2_MEASURE_DIR) after
    /// its source checks and before its threshold assert, so a red value is recorded as red.
    /// </summary>
    [TestFixture]
    [Category("WP083")]
    public sealed class R2HeroGovdeTests
    {
        public const string H2Name = "DD.Prototype.Tests.PlayMode.R2HeroGovdeTests.H2_SilahGovdeGomulmesi_TumKlasikSilahlar_DurusYuruyus";
        public const string H6Name = "DD.Prototype.Tests.PlayMode.R2HeroGovdeTests.H6_TpSilahGorunurlugu_TumKlasikSilahlar_DurusYuruyus";

        /// <summary>H2: a weapon point deeper than this behind the skin is a clip (skinning / vertex-spacing noise stays under it).</summary>
        public const float ClipToleranceM = 0.005f;
        /// <summary>H2: the stock's designed contact — WeaponModelSliceTests.StockPenetrationMaxMetres (KundakOmuz, ≤ 2 cm).</summary>
        public const float StockAllowanceM = 0.02f;
        /// <summary>H2: the stock zone — weapon points within this of attach_stock along the stock→muzzle line (the butt plate).</summary>
        public const float StockZoneM = 0.03f;
        private const float SampleSpacingM = 0.015f;
        private const int SampleCap = 60000;
        private const int RtW = 1280, RtH = 720;

        private SpecialWeaponSahne _s;
        private bool _fpArmsBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // the owner's A/B switches (as R2HeroOlcumTests): DD_HOLD_IK=0 the hold off, DD_GRASP=0 the B2f grasp off; both on by default
            WeaponHoldIk.Enabled = Environment.GetEnvironmentVariable("DD_HOLD_IK") != "0";
            WeaponGripIk.GraspEnabled = Environment.GetEnvironmentVariable("DD_GRASP") != "0";
            _fpArmsBefore = PlayerSkinnedVisual.FirstPersonV3Arms;
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
            WeaponGripIk.ResetStatics();
            PlayerSkinnedVisual.FirstPersonV3Arms = _fpArmsBefore;
        }

        // ------------------------------------------------------------------------------------------------------------------ H2

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(1200000)]
        public IEnumerator H2_SilahGovdeGomulmesi_TumKlasikSilahlar_DurusYuruyus()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Animator a = body.Animator;
            Assert.IsNotNull(a, "oyuncu Animator yok");
            List<SkinnedMeshRenderer> skins = BodyLod0(body);
            Assert.Greater(skins.Count, 0, "gövde LOD0 SkinnedMeshRenderer yok");
            var skin = new SkinProbe(skins, a);
            Vector3 spot = _s.Rig.transform.position;
            var report = new List<string>();
            var failed = new List<string>();
            int clipFrames = 0, frames = 0, weapons = 0;
            try
            {
                foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                {
                    _s.Rig.Motor.Teleport(spot);
                    _s.Girdi.Yon = Vector2.zero;
                    _s.Controller.EquipClassic(d.Id, _s.Config);
                    yield return SpecialWeaponSahne.Kareler(60);   // the switch-in has played out
                    var w = new H2Weapon { Key = d.RosterKey };
                    Transform mount = null;
                    try
                    {
                        var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
                        mount = binding != null ? binding.WeaponMount : null;
                        if (mount == null) w.Error = "no weapon mount";
                        else if (!_s.Controller.ClassicRigHasAuthoredModel) w.Error = "no authored model in the hand";
                        else w.Samples = WeaponPoints.Build(mount);
                        if (w.Samples != null && w.Samples.Count == 0) w.Error = "no LOD0 weapon points";
                    }
                    catch (Exception e)
                    {
                        w.Error = "setup " + e.GetType().Name + ": " + e.Message;
                    }

                    try
                    {
                        for (int f = 0; f < 180; f++)
                        {
                            _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;   // stand 1 s, walk 1.5 s, stop
                            yield return null;
                            if (w.Error != null || f % 3 != 0) continue;
                            if (_s.Controller.Classic != null && _s.Controller.Classic.IsReloading) continue;
                            try
                            {
                                H2Frame r = skin.Measure(w.Samples, mount, w.Frames == 0);
                                if (r.BakeError != null) { w.Error = r.BakeError; continue; }
                                w.Add(r, f >= 60 && f < 150);
                            }
                            catch (Exception e)
                            {
                                w.Error = "frame " + f + " " + e.GetType().Name + ": " + e.Message;
                            }
                        }
                    }
                    finally
                    {
                        _s.Girdi.Yon = Vector2.zero;
                    }

                    // the per-weapon line goes to the log before any assert
                    string line = w.Line();
                    Debug.Log("[DD-TEST] wp083.h2.weapon " + line);
                    report.Add(line);
                    if (w.Error != null || w.Frames == 0) failed.Add(d.RosterKey + ": " + (w.Error ?? "no frame measured"));
                    else weapons++;
                    frames += w.Frames;
                    clipFrames += w.ClipFrames;
                }
            }
            finally
            {
                _s.Girdi.Yon = Vector2.zero;
                skin.Dispose();
            }

            Debug.Log("[DD-TEST] wp083.h2 hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " grasp=" + (WeaponGripIk.GraspEnabled ? "on" : "off") +
                      " clipFrames=" + clipFrames + " frames=" + frames + " weapons=" + weapons + " tol=" + Cm(ClipToleranceM) + "cm stockAllowance=" +
                      Cm(StockAllowanceM) + "cm :: " + string.Join(" | ", report));
            // the source is in the record before the artefact (S-10 R2 standard): every enabled weapon in the hand, points, frames, the skin
            Assert.Greater(weapons, 0, "R2 H2: ölçülen silah yok");
            Assert.Greater(frames, 0, "R2 H2: ölçülen kare yok");
            Assert.IsEmpty(failed, "R2 H2: kaynak eksik — ölçülemeyen silah var:\n" + string.Join("\n", failed));
            R2Olcum.Yaz("H2", H2Name, new Dictionary<string, double> { { "clipFrames", clipFrames } }, frames);
            Assert.AreEqual(0, clipFrames, "R2 H2: silah omuza / göğse gömülüyor (" + clipFrames + " kare)\n" + string.Join("\n", report));
        }

        private sealed class H2Weapon
        {
            public string Key, Error, WorstAt = "";
            public WeaponPoints Samples;
            public int Frames, ClipFrames, StandFrames, WalkFrames, OtherRegionFrames, NearFramesMax;
            public float WorstDepth, StockDepth, BakeCheck;
            public string WorstRegion = "-";

            public void Add(H2Frame r, bool walk)
            {
                Frames++;
                if (walk) WalkFrames++; else StandFrames++;
                if (r.Clip) ClipFrames++;
                if (r.OtherClip) OtherRegionFrames++;
                NearFramesMax = Mathf.Max(NearFramesMax, r.Near);
                StockDepth = Mathf.Max(StockDepth, r.StockDepth);
                if (r.FirstFrameCheck > 0f) BakeCheck = r.FirstFrameCheck;
                if (r.Clip && r.Depth > WorstDepth)
                {
                    WorstDepth = r.Depth;
                    WorstRegion = r.Region;
                    WorstAt = (walk ? "walk" : "stand") + (r.InStockZone ? " stock" : "");
                }
            }

            public string Line() => Key + " frames " + Frames + " (stand " + StandFrames + ", walk " + WalkFrames + ") clip " + ClipFrames +
                                    " worst " + Cm(WorstDepth) + "cm @" + WorstRegion + " " + WorstAt + " | stockMax " + Cm(StockDepth) +
                                    "cm (allow " + Cm(StockAllowanceM) + ") otherRegionClipFrames " + OtherRegionFrames + " nearPts " + NearFramesMax +
                                    " points " + (Samples != null ? Samples.Count : 0) + " skinCheck " + Cm(BakeCheck) + "cm" + (Error != null ? " ERROR " + Error : "");
        }

        private struct H2Frame
        {
            public bool Clip, OtherClip, InStockZone;
            public float Depth, StockDepth, FirstFrameCheck;
            public int Near;
            public string Region, BakeError;
        }

        /// <summary>The LOD0 weapon points under the mount (vertices + edge splits), kept in each mesh's own space.</summary>
        private sealed class WeaponPoints
        {
            public readonly List<Transform> Owners = new List<Transform>();
            public readonly List<Vector3[]> Local = new List<Vector3[]>();
            public int Count;

            public static WeaponPoints Build(Transform mount)
            {
                var wp = new WeaponPoints();
                var verts = new List<Vector3>();
                var tris = new List<int>();
                var edges = new HashSet<long>();
                foreach (MeshFilter mf in mount.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled || mf.gameObject.layer == DD.Prototype.CameraSystem.CameraLayers.FirstPersonView) continue;
                    Mesh m = Readable(mf.sharedMesh);
                    m.GetVertices(verts);
                    if (verts.Count == 0) continue;
                    var pts = new List<Vector3>(verts);
                    Matrix4x4 w = mf.transform.localToWorldMatrix;
                    edges.Clear();
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        m.GetTriangles(tris, s);
                        for (int t = 0; t + 2 < tris.Count; t += 3)
                        {
                            for (int e = 0; e < 3; e++)
                            {
                                int i0 = tris[t + e], i1 = tris[t + (e + 1) % 3];
                                long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
                                if (!edges.Add(key)) continue;
                                float len = Vector3.Distance(w.MultiplyPoint3x4(verts[i0]), w.MultiplyPoint3x4(verts[i1]));
                                int n = Mathf.Min(64, Mathf.CeilToInt(len / SampleSpacingM));
                                for (int k = 1; k < n && wp.Count + pts.Count < SampleCap; k++) pts.Add(Vector3.Lerp(verts[i0], verts[i1], (float)k / n));
                            }
                        }
                    }

                    wp.Owners.Add(mf.transform);
                    wp.Local.Add(pts.ToArray());
                    wp.Count += pts.Count;
                }

                return wp;
            }
        }

        /// <summary>
        /// The AP-006 LOD0 skin baked each measured frame, a 3 cm grid over its vertices, and each vertex's region = the nearest
        /// humanoid bone segment (computed only for the vertices a weapon point lands behind).
        /// </summary>
        private sealed class SkinProbe : IDisposable
        {
            private const float Cell = 0.03f;
            // regions: 1 Chest, 2 UpperChest, 3 L shoulder, 4 R shoulder, 5 L upper arm, 6 R upper arm (the gate's); 7 Spine,
            // 8 Neck / Head, 9 L forearm / hand, 10 R forearm / hand, 11 Hips / legs (logged only)
            private static readonly string[] RegionNames = { "?", "Chest", "UpperChest", "LShoulder", "RShoulder", "LUpperArm", "RUpperArm", "Spine", "NeckHead", "LForearmHand", "RForearmHand", "HipsLegs" };
            private readonly List<SkinnedMeshRenderer> _skins;
            private readonly Animator _a;
            private readonly List<Mesh> _baked = new List<Mesh>();
            private readonly List<Vector3> _p = new List<Vector3>(), _n = new List<Vector3>();
            private readonly List<Vector3> _tmpV = new List<Vector3>(), _tmpN = new List<Vector3>();
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly Stack<List<int>> _pool = new Stack<List<int>>();
            private readonly List<(Vector3 a, Vector3 b, int region)> _segs = new List<(Vector3, Vector3, int)>();
            private int[] _region = new int[0];
            private Bounds _box;

            public SkinProbe(List<SkinnedMeshRenderer> skins, Animator a)
            {
                _skins = skins;
                _a = a;
                for (int i = 0; i < skins.Count; i++) _baked.Add(new Mesh { name = "WP083_H2_Skin" });
            }

            public void Dispose()
            {
                foreach (Mesh m in _baked) if (m != null) Object.Destroy(m);
                _baked.Clear();
            }

            public H2Frame Measure(WeaponPoints wp, Transform mount, bool firstFrame)
            {
                var r = new H2Frame { Region = "-" };
                Bake();
                if (_p.Count == 0) { r.BakeError = "skin baked empty"; return r; }
                Segments();
                if (firstFrame)
                {
                    // the bake sits on the skeleton (a scale / space slip would put the skin away from its bones): the skin's nearest vertex
                    // to the chest and to both shoulder joints within 20 cm
                    float worst = 0f;
                    foreach (HumanBodyBones hb in new[] { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                    {
                        Transform bt = _a.GetBoneTransform(hb);
                        if (bt == null) continue;
                        float best = float.MaxValue;
                        for (int i = 0; i < _p.Count; i++) best = Mathf.Min(best, (_p[i] - bt.position).sqrMagnitude);
                        worst = Mathf.Max(worst, Mathf.Sqrt(best));
                    }

                    r.FirstFrameCheck = Mathf.Max(worst, 1e-4f);
                    if (worst > 0.2f) { r.BakeError = "skin bake off the skeleton (" + Cm(worst) + " cm)"; return r; }
                }

                Transform stock = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachStock);
                Transform muzzle = WeaponSocketContract.Find(mount, WeaponSocketContract.Muzzle);
                bool zone = stock != null && muzzle != null && (muzzle.position - stock.position).sqrMagnitude > 1e-4f;
                Vector3 s0 = zone ? stock.position : Vector3.zero, axis = zone ? (muzzle.position - stock.position).normalized : Vector3.forward;
                Bounds box = _box;
                box.Expand(2f * Cell);
                for (int o = 0; o < wp.Owners.Count; o++)
                {
                    Transform t = wp.Owners[o];
                    if (t == null) continue;
                    Matrix4x4 m = t.localToWorldMatrix;
                    Vector3[] pts = wp.Local[o];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        Vector3 p = m.MultiplyPoint3x4(pts[i]);
                        if (!box.Contains(p)) continue;
                        int k = Nearest(p);
                        if (k < 0) continue;
                        r.Near++;
                        float depth = -Vector3.Dot(p - _p[k], _n[k]);
                        if (depth <= ClipToleranceM) continue;
                        bool inZone = zone && Vector3.Dot(p - s0, axis) <= StockZoneM;
                        if (inZone) r.StockDepth = Mathf.Max(r.StockDepth, depth);
                        if (inZone && depth <= StockAllowanceM) continue;   // the designed contact, KundakOmuz's limit
                        int region = RegionOf(k);
                        if (region >= 1 && region <= 6)
                        {
                            r.Clip = true;
                            if (depth > r.Depth) { r.Depth = depth; r.Region = RegionNames[region]; r.InStockZone = inZone; }
                        }
                        else r.OtherClip = true;
                    }
                }

                return r;
            }

            private void Bake()
            {
                _p.Clear();
                _n.Clear();
                for (int i = 0; i < _skins.Count; i++)
                {
                    SkinnedMeshRenderer smr = _skins[i];
                    if (smr == null || smr.sharedMesh == null) continue;
                    smr.BakeMesh(_baked[i]);
                    _baked[i].GetVertices(_tmpV);
                    _baked[i].GetNormals(_tmpN);
                    // BakeMesh (useScale false) is in the renderer's space without its scale
                    Transform t = smr.transform;
                    Matrix4x4 m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                    bool normals = _tmpN.Count == _tmpV.Count;
                    for (int v = 0; v < _tmpV.Count; v++)
                    {
                        _p.Add(m.MultiplyPoint3x4(_tmpV[v]));
                        _n.Add(normals ? m.MultiplyVector(_tmpN[v]).normalized : Vector3.up);
                    }
                }

                foreach (List<int> l in _cells.Values) { l.Clear(); _pool.Push(l); }
                _cells.Clear();
                if (_region.Length < _p.Count) _region = new int[_p.Count];
                for (int i = 0; i < _p.Count; i++) _region[i] = -1;
                _box = _p.Count > 0 ? new Bounds(_p[0], Vector3.zero) : new Bounds();
                for (int i = 0; i < _p.Count; i++)
                {
                    _box.Encapsulate(_p[i]);
                    long key = Key(_p[i]);
                    if (!_cells.TryGetValue(key, out List<int> l))
                    {
                        l = _pool.Count > 0 ? _pool.Pop() : new List<int>(16);
                        _cells[key] = l;
                    }

                    l.Add(i);
                }
            }

            private static long Key(Vector3 p) => Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

            private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

            // the nearest skin vertex within the 3×3×3 cells about p (every vertex within 3 cm is seen); -1 when none
            private int Nearest(Vector3 p)
            {
                int cx = Mathf.FloorToInt(p.x / Cell), cy = Mathf.FloorToInt(p.y / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                int best = -1;
                float bestSq = float.MaxValue;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int> l)) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        float sq = (_p[l[i]] - p).sqrMagnitude;
                        if (sq < bestSq) { bestSq = sq; best = l[i]; }
                    }
                }

                return best;
            }

            private void Segments()
            {
                _segs.Clear();
                Transform hips = B(HumanBodyBones.Hips), spine = B(HumanBodyBones.Spine), chest = B(HumanBodyBones.Chest), upper = B(HumanBodyBones.UpperChest),
                          neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
                Transform chestTop = upper != null ? upper : neck;
                Seg(hips, spine, 11);
                Seg(spine, chest, 7);
                Seg(chest, chestTop, 1);
                Seg(upper, neck, 2);
                Seg(neck, head, 8);
                if (head != null && neck != null) _segs.Add((head.position, head.position + (head.position - neck.position).normalized * 0.2f, 8));
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    Transform sh = B(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder), ua = B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                              la = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm), hand = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
                              ul = B(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg), ll = B(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                              foot = B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot), toes = B(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                    Seg(sh != null ? sh : chestTop, ua, left ? 3 : 4);
                    Seg(ua, la, left ? 5 : 6);
                    Seg(la, hand, left ? 9 : 10);
                    if (hand != null && la != null) _segs.Add((hand.position, hand.position + (hand.position - la.position).normalized * 0.1f, left ? 9 : 10));
                    Seg(hips, ul, 11);
                    Seg(ul, ll, 11);
                    Seg(ll, foot, 11);
                    Seg(foot, toes, 11);
                }
            }

            private Transform B(HumanBodyBones b) => _a.GetBoneTransform(b);

            private void Seg(Transform x, Transform y, int region)
            {
                if (x != null && y != null) _segs.Add((x.position, y.position, region));
            }

            private int RegionOf(int k)
            {
                if (_region[k] >= 0) return _region[k];
                Vector3 p = _p[k];
                int best = 0;
                float bestSq = float.MaxValue;
                foreach ((Vector3 a, Vector3 b, int region) in _segs)
                {
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    float sq = (a + ab * t - p).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = region; }
                }

                _region[k] = best;
                return best;
            }
        }

        private static List<SkinnedMeshRenderer> BodyLod0(PlayerSkinnedVisual body)
        {
            var list = new List<SkinnedMeshRenderer>();
            if (body.LodGroup != null)
            {
                LOD[] lods = body.LodGroup.GetLODs();
                if (lods.Length > 0)
                    foreach (Renderer r in lods[0].renderers)
                        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null && !list.Contains(smr)) list.Add(smr);
            }

            if (list.Count == 0)
                foreach (SkinnedMeshRenderer smr in body.Renderers)
                    if (smr != null && smr.sharedMesh != null && smr.name.Contains("LOD0")) list.Add(smr);
            return list;
        }

        // ------------------------------------------------------------------------------------------------------------------ H6

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(1200000)]
        public IEnumerator H6_TpSilahGorunurlugu_TumKlasikSilahlar_DurusYuruyus()
        {
            // a render-only measure: a frustum / reflection log from the camera's temporary state must not fail the recorded run
            LogAssert.ignoreFailingMessages = true;
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            yield return SpecialWeaponSahne.Kareler(5);
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "oyun kamerası (Camera.main) yok");
            Debug.Log("[DD-TEST] wp083.h6.camera " + cam.name + " mode=" + DD.Prototype.CameraSystem.CameraModeService.Current + " fov=" + cam.fieldOfView.ToString("F1", CultureInfo.InvariantCulture) +
                      " atmosphereSwitch=" + (FlatRender.Atmosphere != null ? "found" : "absent"));
            Assert.AreEqual(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, DD.Prototype.CameraSystem.CameraModeService.Current, "kamera ThirdPerson değil");
            int[] layers = FreeLayers();
            Vector3 spot = _s.Rig.transform.position;
            var rows = new List<H6Weapon>();
            var failed = new List<string>();
            var flat = new FlatRender(cam, layers[0], layers[1]);
            try
            {
                foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                {
                    _s.Rig.Motor.Teleport(spot);
                    _s.Girdi.Yon = Vector2.zero;
                    _s.Controller.EquipClassic(d.Id, _s.Config);
                    yield return SpecialWeaponSahne.Kareler(60);   // the switch-in has played out, the camera has settled
                    var w = new H6Weapon { Key = d.RosterKey };
                    rows.Add(w);
                    try
                    {
                        for (int f = 0; f < 180; f++)
                        {
                            _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;   // stand 1 s, walk 1.5 s, stop
                            yield return null;
                            if (w.Error != null || f % 5 != 0) continue;
                            if (_s.Controller.Classic != null && _s.Controller.Classic.IsReloading) continue;
                            try
                            {
                                var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
                                Transform mount = binding != null ? binding.WeaponMount : null;
                                if (mount == null) { w.Error = "no weapon mount"; continue; }
                                if (!_s.Controller.ClassicRigHasAuthoredModel) { w.Error = "no authored model in the hand"; continue; }
                                Transform muzzle = WeaponSocketContract.Find(mount, WeaponSocketContract.Muzzle);
                                if (muzzle == null) { w.Error = "no Muzzle socket"; continue; }
                                List<Renderer> weaponR = Drawn(mount, null, cam), bodyR = Drawn(body.transform, mount, cam);
                                w.WeaponRenderers = Math.Max(w.WeaponRenderers, weaponR.Count);
                                w.BodyRenderers = Math.Max(w.BodyRenderers, bodyR.Count);
                                H6Frame r = flat.Measure(weaponR, bodyR, muzzle.position);
                                w.Add(r);
                            }
                            catch (Exception e)
                            {
                                w.Error = "frame " + f + " " + e.GetType().Name + ": " + e.Message;
                            }
                        }
                    }
                    finally
                    {
                        _s.Girdi.Yon = Vector2.zero;
                    }

                    // the per-weapon line goes to the log before any assert
                    Debug.Log("[DD-TEST] wp083.h6.weapon " + w.Line());
                    if (w.Error != null) failed.Add(d.RosterKey + ": " + w.Error);
                    else if (w.Frames == 0) failed.Add(d.RosterKey + ": no frame with the weapon on screen (mask 0 every frame)");
                    else if (w.BodyPxMin <= 0) failed.Add(d.RosterKey + ": body silhouette empty on a measured frame");
                    else if (w.FlatShare < 0.95) failed.Add(d.RosterKey + ": the flat weapon render is not flat (red share of its mask " + F3(w.FlatShare) + " < 0.95 — a screen pass tints it)");
                }
            }
            finally
            {
                _s.Girdi.Yon = Vector2.zero;
                flat.Dispose();
            }

            var measured = rows.FindAll(x => x.Error == null && x.Frames > 0);
            measured.Sort((x, y) => x.Share.CompareTo(y.Share));
            double worst = measured.Count > 0 ? measured[0].Share : 0.0, second = measured.Count > 1 ? measured[1].Share : worst;
            int inside = 0, frames = 0;
            foreach (H6Weapon x in measured) { inside += x.MuzzleInside; frames += x.Frames; }
            var lines = new List<string>();
            foreach (H6Weapon x in rows) lines.Add(x.Line());
            Debug.Log("[DD-TEST] wp083.h6 hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " grasp=" + (WeaponGripIk.GraspEnabled ? "on" : "off") +
                      " visibleShareMin=" + F3(worst) + " (" + (measured.Count > 0 ? measured[0].Key : "-") + ") secondWorstShare=" + F3(second) +
                      " (" + (measured.Count > 1 ? measured[1].Key : "-") + ") muzzleInsideSilhouetteFrames=" + inside + " weapons=" + measured.Count + " frames=" + frames +
                      " rt=" + RtW + "x" + RtH + " :: " + string.Join(" | ", lines));
            // the source is in the record before the artefact (S-10 R2 standard): every enabled weapon drawn on the game camera, its mask
            // and the body's silhouette non-empty, frames counted
            Assert.Greater(measured.Count, 0, "R2 H6: ölçülen silah yok");
            Assert.Greater(frames, 0, "R2 H6: ölçülen kare yok");
            foreach (H6Weapon x in measured) Assert.Greater(x.MaskPxTotal, 0L, "R2 H6: " + x.Key + " maske pikseli yok");
            Assert.IsEmpty(failed, "R2 H6: kaynak eksik — ölçülemeyen silah var:\n" + string.Join("\n", failed));
            string dir = Environment.GetEnvironmentVariable("DD_R2_MEASURE_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                // the per-weapon table beside the artefact (contract: rosterKey, share, muzzleInsideFrames; the rest are notes)
                var csv = new System.Text.StringBuilder("rosterKey,share,muzzleInsideFrames,shareWorstFrame,frames,maskPxMean,muzzleHiddenFrames,offScreenFrames\n");
                foreach (H6Weapon x in measured)
                    csv.Append(x.Key).Append(',').Append(F3(x.Share)).Append(',').Append(x.MuzzleInside).Append(',').Append(F3(x.WorstFrame)).Append(',')
                       .Append(x.Frames).Append(',').Append((x.MaskPxTotal / Math.Max(1, x.Frames)).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(x.MuzzleHidden).Append(',').Append(x.OffScreen).Append('\n');
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "r2-H6-perweapon.csv"), csv.ToString());
            }

            R2Olcum.Yaz("H6", H6Name, new Dictionary<string, double>
            {
                { "visibleShareMin", Math.Round(worst, 4) }, { "muzzleInsideSilhouetteFrames", inside },
                { "weaponsMeasured", measured.Count }, { "secondWorstShare", Math.Round(second, 4) },
            }, frames);
            Assert.GreaterOrEqual(worst, 0.25, "R2 H6: TP'de görünür silah payı < 0,25\n" + string.Join("\n", lines));
            Assert.AreEqual(0, inside, "R2 H6: namlu ucu gövde siluetinin içinde (" + inside + " kare)\n" + string.Join("\n", lines));
        }

        private sealed class H6Weapon
        {
            public string Key, Error;
            public int Frames, MuzzleInside, MuzzleHidden, OffScreen, WeaponRenderers, BodyRenderers, BodyPxMin = int.MaxValue;
            public long MaskPxTotal, MaskRedTotal;
            public double FlatShare => MaskPxTotal > 0 ? (double)MaskRedTotal / MaskPxTotal : 0.0;
            public double ShareSum, WorstFrame = 1.0;
            public double Share => Frames > 0 ? ShareSum / Frames : 0.0;

            public void Add(H6Frame r)
            {
                if (r.Mask <= 0) { OffScreen++; return; }
                Frames++;
                MaskPxTotal += r.Mask;
                MaskRedTotal += r.MaskRed;
                double s = (double)r.Visible / r.Mask;
                ShareSum += s;
                WorstFrame = Math.Min(WorstFrame, s);
                BodyPxMin = Math.Min(BodyPxMin, r.BodyPx);
                if (r.MuzzleInside) MuzzleInside++;
                if (r.MuzzleHidden) MuzzleHidden++;
            }

            public string Line() => Key + " share " + F3(Share) + " worstFrame " + F3(Frames > 0 ? WorstFrame : 0.0) + " frames " + Frames + " maskPxMean " +
                                    (MaskPxTotal / Math.Max(1, Frames)) + " muzzleInside " + MuzzleInside + " muzzleHidden " + MuzzleHidden + " offScreen " + OffScreen +
                                    " bodyPxMin " + (Frames > 0 ? BodyPxMin : 0) + " flat " + F3(FlatShare) + " renderers weapon/body " + WeaponRenderers + "/" + BodyRenderers + (Error != null ? " ERROR " + Error : "");
        }

        private struct H6Frame
        {
            public int Mask, MaskRed, Visible, BodyPx;
            public bool MuzzleInside, MuzzleHidden;
        }

        // the renderers the game camera draws under root (not under skip): mesh / skinned, enabled, active, not shadows-only, on a
        // layer in the camera's culling mask
        private static List<Renderer> Drawn(Transform root, Transform skip, Camera cam)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                if (skip != null && r.transform.IsChildOf(skip)) continue;
                list.Add(r);
            }

            return list;
        }

        /// <summary>
        /// The three flat renders of one frame on the game camera. Each Measure swaps layers, materials and property blocks of the
        /// given renderers and the camera's target / mask / clear / post, renders, reads back and restores all of it in a finally.
        /// </summary>
        private sealed class FlatRender : IDisposable
        {
            private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), BaseMapId = Shader.PropertyToID("_BaseMap");
            // DD.Art.ScreenAtmosphereFeature.WorldActive (DD.Art.Runtime is not referenced by this assembly); null when absent
            public static readonly System.Reflection.FieldInfo Atmosphere =
                Type.GetType("DD.Art.ScreenAtmosphereFeature, DD.Art.Runtime")?.GetField("WorldActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            private readonly Camera _cam;
            private readonly int _lw, _lb;
            private readonly Material _red, _green;
            private readonly MaterialPropertyBlock _redBlock = new MaterialPropertyBlock(), _greenBlock = new MaterialPropertyBlock();
            private readonly RenderTexture _rt;
            private readonly Texture2D _tex;
            private readonly bool[] _mask = new bool[RtW * RtH];

            public FlatRender(Camera cam, int weaponLayer, int bodyLayer)
            {
                _cam = cam;
                _lw = weaponLayer;
                _lb = bodyLayer;
                _red = new Material(PrototypeShaders.Unlit) { name = "WP083_H6_Weapon" };
                _green = new Material(PrototypeShaders.Unlit) { name = "WP083_H6_Body" };
                _red.SetColor(BaseColorId, Color.red);
                _green.SetColor(BaseColorId, Color.green);
                _redBlock.SetColor(BaseColorId, Color.red);
                _redBlock.SetTexture(BaseMapId, Texture2D.whiteTexture);
                _greenBlock.SetColor(BaseColorId, Color.green);
                _greenBlock.SetTexture(BaseMapId, Texture2D.whiteTexture);
                _rt = new RenderTexture(RtW, RtH, 24, RenderTextureFormat.ARGB32) { name = "WP083 H6", antiAliasing = 1 };
                _rt.Create();
                _tex = new Texture2D(RtW, RtH, TextureFormat.RGBA32, false);
            }

            public void Dispose()
            {
                if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
                if (_tex != null) Object.Destroy(_tex);
                if (_red != null) Object.Destroy(_red);
                if (_green != null) Object.Destroy(_green);
            }

            public H6Frame Measure(List<Renderer> weapon, List<Renderer> body, Vector3 muzzle)
            {
                var r = new H6Frame();
                var all = new List<Renderer>(weapon.Count + body.Count);
                all.AddRange(weapon);
                all.AddRange(body);
                var layers = new int[all.Count];
                var mats = new Material[all.Count][];
                var hadBlock = new bool[all.Count];
                var blocks = new MaterialPropertyBlock[all.Count];
                int swapped = 0;
                int cull = _cam.cullingMask;
                CameraClearFlags clear = _cam.clearFlags;
                Color bg = _cam.backgroundColor;
                RenderTexture target = _cam.targetTexture, active = RenderTexture.active;
                bool msaa = _cam.allowMSAA, fog = RenderSettings.fog;
                Component urp = _cam.GetComponent("UniversalAdditionalCameraData");   // no URP reference in this assembly
                System.Reflection.PropertyInfo post = urp != null ? urp.GetType().GetProperty("renderPostProcessing") : null;
                object hadPost = post != null ? post.GetValue(urp) : null;
                // DD.Art's full-screen height fog / shafts pass runs on every game camera whatever the post flag (BeforeRenderingTransparents);
                // the volumetric pass skips a camera without the Environment layer by itself
                object hadAtmo = Atmosphere != null ? Atmosphere.GetValue(null) : null;
                try
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Renderer x = all[i];
                        bool isWeapon = i < weapon.Count;
                        layers[i] = x.gameObject.layer;
                        mats[i] = x.sharedMaterials;
                        hadBlock[i] = x.HasPropertyBlock();
                        if (hadBlock[i]) { blocks[i] = new MaterialPropertyBlock(); x.GetPropertyBlock(blocks[i]); }
                        swapped = i + 1;
                        x.gameObject.layer = isWeapon ? _lw : _lb;
                        var flat = new Material[Math.Max(1, mats[i].Length)];
                        for (int k = 0; k < flat.Length; k++) flat[k] = isWeapon ? _red : _green;
                        x.sharedMaterials = flat;
                        x.SetPropertyBlock(isWeapon ? _redBlock : _greenBlock);
                    }

                    _cam.targetTexture = _rt;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = Color.black;
                    _cam.allowMSAA = false;
                    RenderSettings.fog = false;
                    if (post != null) post.SetValue(urp, false);
                    if (Atmosphere != null) Atmosphere.SetValue(null, false);

                    // (a) the weapon alone: its own unoccluded mask
                    NativeArray<Color32> px = Draw(1 << _lw);
                    for (int i = 0; i < _mask.Length; i++)
                    {
                        Color32 c = px[i];
                        _mask[i] = c.r + c.g + c.b > 48;
                        if (!_mask[i]) continue;
                        r.Mask++;
                        if (IsRed(c)) r.MaskRed++;
                    }

                    // (b) weapon + body, depth tested: the mask pixels still the weapon's colour
                    px = Draw((1 << _lw) | (1 << _lb));
                    for (int i = 0; i < _mask.Length; i++)
                        if (_mask[i] && IsRed(px[i])) r.Visible++;

                    // the muzzle on the target the renders used (row 0 = bottom, as ReadPixels)
                    Vector3 sp = _cam.WorldToScreenPoint(muzzle);
                    int mx = Mathf.FloorToInt(sp.x), my = Mathf.FloorToInt(sp.y);
                    bool onScreen = sp.z > 0f && mx >= 0 && mx < RtW && my >= 0 && my < RtH;
                    if (onScreen && IsGreen(px[my * RtW + mx])) r.MuzzleHidden = true;

                    // (c) the body alone: its silhouette
                    px = Draw(1 << _lb);
                    for (int i = 0; i < _mask.Length; i++)
                    {
                        Color32 c = px[i];
                        if (c.r + c.g + c.b > 48) r.BodyPx++;
                    }

                    if (onScreen)
                    {
                        Color32 c = px[my * RtW + mx];
                        r.MuzzleInside = c.r + c.g + c.b > 48;
                    }
                }
                finally
                {
                    for (int i = swapped - 1; i >= 0; i--)
                    {
                        Renderer x = all[i];
                        if (x == null) continue;
                        x.sharedMaterials = mats[i];
                        x.SetPropertyBlock(hadBlock[i] ? blocks[i] : null);
                        x.gameObject.layer = layers[i];
                    }

                    _cam.targetTexture = target;
                    _cam.cullingMask = cull;
                    _cam.clearFlags = clear;
                    _cam.backgroundColor = bg;
                    _cam.allowMSAA = msaa;
                    RenderSettings.fog = fog;
                    if (post != null) post.SetValue(urp, hadPost);
                    if (Atmosphere != null) Atmosphere.SetValue(null, hadAtmo);
                    RenderTexture.active = active;
                }

                return r;
            }

            private NativeArray<Color32> Draw(int mask)
            {
                _cam.cullingMask = mask;
                _cam.Render();
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, RtW, RtH), 0, 0, false);
                return _tex.GetRawTextureData<Color32>();
            }

            private static bool IsRed(Color32 c) => c.r >= 96 && c.r > 2 * c.g && c.r > 2 * c.b;

            private static bool IsGreen(Color32 c) => c.g >= 96 && c.g > 2 * c.r && c.g > 2 * c.b;
        }

        // two unnamed user layers (ZombieR2Tests.FreeLayer's search), one for the weapon and one for the body
        private static int[] FreeLayers()
        {
            var found = new List<int>(2);
            for (int i = 31; i >= 8 && found.Count < 2; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) found.Add(i);
            Assert.AreEqual(2, found.Count, "kaynak yok: iki boş katman");
            return found.ToArray();
        }

        // ------------------------------------------------------------------------------------------------------------- helpers

        private static string Cm(float m) => (m * 100f).ToString("F1", CultureInfo.InvariantCulture);

        private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        // the AP-021 weapon meshes are imported without Read/Write: a readable copy through a bone-less SkinnedMeshRenderer's BakeMesh
        // (R2HeroOlcumTests.Readable's pattern, kept local so that file is not touched), once per mesh for the test
        private static readonly Dictionary<Mesh, Mesh> s_readable = new Dictionary<Mesh, Mesh>();

        private static Mesh Readable(Mesh m)
        {
            if (m.isReadable) return m;
            if (s_readable.TryGetValue(m, out Mesh copy) && copy != null) return copy;
            var go = new GameObject("WP083_H2_Bake");
            try
            {
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = m;
                copy = new Mesh();
                smr.BakeMesh(copy);
            }
            finally
            {
                Object.Destroy(go);
            }

            s_readable[m] = copy;
            return copy;
        }
    }
}
