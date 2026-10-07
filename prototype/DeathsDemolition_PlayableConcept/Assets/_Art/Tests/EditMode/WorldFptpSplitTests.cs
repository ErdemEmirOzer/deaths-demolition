using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-092 A (R3 #8) · World_FPTPOnly: the committed visibility list → split is deterministic and leaves the world's colliders and
    /// NavMesh build sources exactly as they were (a synthetic world and the real World_V6_Greybox with the committed list, merged back
    /// to the identical hierarchy); a stale list moves nothing; the loader policy never loads the FPTP scene in top-down and does in
    /// TP / FP (start and mid-run) and under the menu backdrop.
    /// </summary>
    public sealed class WorldFptpSplitTests
    {
        private const string SugiPrefab = "Assets/_Art/Environment/Families/AP-023/sugi/SM_sugi_V01.prefab";

        // ------------------------------------------------------------------------------------------------ loader policy (TD / TP / FP)

        private sealed class FakeOps
        {
            public readonly List<string> Calls = new List<string>();
            public bool InBuild = true, Loaded, AsyncDone = true;

            public FptpSceneDriver.Ops Ops()
            {
                return new FptpSceneDriver.Ops
                {
                    CanLoad = _ => InBuild,
                    LoadNow = s => { Calls.Add("loadNow " + s); Loaded = true; },
                    LoadAsync = s => { Calls.Add("loadAsync " + s); return () => { if (AsyncDone) Loaded = true; return AsyncDone; }; },
                    UnloadAsync = s => { Calls.Add("unload " + s); return () => { if (AsyncDone) Loaded = false; return AsyncDone; }; },
                    IsLoaded = _ => Loaded,
                    AfterLoad = () => Calls.Add("afterLoad"),
                    AfterUnload = () => Calls.Add("afterUnload"),
                };
            }
        }

        [Test]
        [Category("R3")]
        public void Yukleyici_TopDownRun_FptpSahnesiniHicYuklemez()
        {
            var fake = new FakeOps();
            var driver = new FptpSceneDriver(WorldFptpOnlyLoader.DefaultSceneName, fake.Ops());
            driver.Begin(CameraMode.TopDown, false);
            for (int i = 0; i < 120; i++) driver.Tick(CameraMode.TopDown, false);
            driver.Release();
            Assert.AreEqual(0, driver.Loads, "TD: yükleme isteği");
            Assert.IsEmpty(fake.Calls, "TD: hiçbir sahne işlemi olmamalı: " + string.Join(", ", fake.Calls));
            Assert.AreEqual(FptpSceneDriver.State.Unloaded, driver.Current);
            Assert.IsFalse(FptpSceneDriver.ShouldLoad(CameraMode.TopDown, false), "politika: TD yüklemez");
            Debug.Log("[DD-TEST] wp092.fptp.td loads=0 ticks=120");
        }

        [Test]
        [Category("FPTP")]
        public void Yukleyici_TpFpBaslangicta_KareylaYukler_ModDegisinceAsenkronIzler()
        {
            var fake = new FakeOps();
            var driver = new FptpSceneDriver(WorldFptpOnlyLoader.DefaultSceneName, fake.Ops());
            driver.Begin(CameraMode.FirstPerson, false);
            Assert.AreEqual(new[] { "loadNow World_FPTPOnly" }, fake.Calls.ToArray(), "FP başlangıcı: dünya ile aynı karede yükler");
            driver.Tick(CameraMode.FirstPerson, false);
            Assert.AreEqual(FptpSceneDriver.State.Loaded, driver.Current);
            driver.Tick(CameraMode.ThirdPerson, false);
            Assert.AreEqual(1, driver.Loads, "FP → TP ikinci yükleme yapmaz");

            driver.Tick(CameraMode.TopDown, false);   // Pause menu: TP → TD
            Assert.AreEqual(FptpSceneDriver.State.Unloading, driver.Current);
            driver.Tick(CameraMode.TopDown, false);
            Assert.AreEqual(FptpSceneDriver.State.Unloaded, driver.Current, "TD'ye dönünce boşaltılır");
            Assert.IsFalse(fake.Loaded);

            // TD → TP → TD while the async load is in flight ends unloaded
            fake.AsyncDone = false;
            driver.Tick(CameraMode.ThirdPerson, false);
            Assert.AreEqual(FptpSceneDriver.State.Loading, driver.Current);
            driver.Tick(CameraMode.TopDown, false);
            fake.AsyncDone = true;
            driver.Tick(CameraMode.TopDown, false);
            driver.Tick(CameraMode.TopDown, false);
            driver.Tick(CameraMode.TopDown, false);
            Assert.AreEqual(FptpSceneDriver.State.Unloaded, driver.Current, "uçuşta TD'ye dönüş: sonunda boş");
            Assert.IsFalse(fake.Loaded);
            Assert.AreEqual(2, driver.Loads);
            Assert.AreEqual(2, driver.Unloads);
            Assert.IsTrue(FptpSceneDriver.ShouldLoad(CameraMode.ThirdPerson, false) && FptpSceneDriver.ShouldLoad(CameraMode.FirstPerson, false));
            Debug.Log("[DD-TEST] wp092.fptp.tpfp " + string.Join(" | ", fake.Calls));
        }

        [Test]
        [Category("R3")]
        public void Yukleyici_MenuArkaPlani_Yukler_BuildDisiyse_Uyarir()
        {
            var fake = new FakeOps();
            var driver = new FptpSceneDriver(WorldFptpOnlyLoader.DefaultSceneName, fake.Ops());
            driver.Begin(CameraMode.TopDown, true);
            Assert.AreEqual(1, driver.Loads, "menü sinematiği vista / ufuk görür: yükler");

            var missing = new FakeOps { InBuild = false };
            var none = new FptpSceneDriver(WorldFptpOnlyLoader.DefaultSceneName, missing.Ops());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not in the build settings"));
            none.Begin(CameraMode.ThirdPerson, false);
            none.Tick(CameraMode.ThirdPerson, false);
            Assert.AreEqual(0, none.Loads, "build dışı sahne yüklenmeye çalışılmaz");
        }

        // ------------------------------------------------------------------------------------------------ split (synthetic world)

        private sealed class World
        {
            public Scene Main, Fptp;
            public Transform Root;
        }

        private static World BuildSyntheticWorld()
        {
            var w = new World { Main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single) };
            var root = new GameObject(Ap005WorldBootstrap.RootName);
            w.Root = root.transform;
            Transform dress = Child(w.Root, "Dress v5 (AP-023)"), vista = Child(dress, "VistaForest"), sky = Child(dress, "HorizonRings");
            dress.gameObject.AddComponent<DD.Art.VolumetricLight>();                             // the real dress root's field: pins nothing
            vista.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var sugi = AssetDatabase.LoadAssetAtPath<GameObject>(SugiPrefab);
            Assert.IsNotNull(sugi, SugiPrefab);
            foreach (Vector3 p in new[] { new Vector3(70f, 0f, 80f), new Vector3(-75f, 0f, -80f), new Vector3(0f, 0f, 70f) })
            {
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(sugi, w.Main);
                tree.transform.SetParent(vista, false);
                tree.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, p.x, 0f));
            }

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(GreyboxV6Builder.DataFolder + "/Materials/Greybox_roof.mat") ?? new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Leaf(sky, "Horizon_far", new Vector3(0f, 20f, 300f), cube, mat);                  // plain visual leaf → Move
            GameObject box = Leaf(Child(w.Root, "Greybox v6"), "VistaRoof_3", new Vector3(80f, 3f, 60f), cube, mat);
            box.AddComponent<BoxCollider>();                                                     // renderer + collider → Twin
            Leaf(sky, "Stone_Lantern_Far", new Vector3(0f, 1f, 90f), cube, mat);                 // scanner name → pinned
            GameObject roof = Leaf(dress, "RoofBlock", new Vector3(10f, 6f, 40f), cube, mat);
            roof.layer = PrototypeLayers.CameraOccluder;                                         // fade hierarchy → pinned
            GameObject nav = Leaf(dress, "Marker", new Vector3(5f, 0f, 5f), cube, mat);
            nav.AddComponent<NavMeshModifier>();                                                 // NavMesh component → pinned
            Leaf(dress, "Plaza_Visible", new Vector3(0f, 0f, 0f), cube, mat);                    // "visible": not in the list

            // WP-092 lead 08.10: the layout's lamps and building shells never move
            Transform layout = Child(w.Root, "Layout v5 (WP-092)");
            Leaf(layout, "Lamp_L01_emissive", new Vector3(30f, 1f, 30f), cube, mat);           // a layout lamp by name → pinned
            GameObject socketed = Leaf(layout, "Andon_Far", new Vector3(32f, 1f, 30f), cube, mat);
            Child(socketed.transform, "SOCKET_Flame");                                         // a kit socket → pinned
            GameObject lit = Leaf(layout, "Chochin_Far", new Vector3(34f, 1f, 30f), cube, mat);
            Child(lit.transform, "LampLight").gameObject.AddComponent<Light>().type = LightType.Point;   // a Light in the unit → pinned
            Transform shell = Child(layout, "N1_Shell");
            shell.gameObject.AddComponent<OccluderFadeState>();
            Leaf(shell, "N1_00_kairo_bay_V02", new Vector3(36f, 0f, 30f), cube, mat);          // under a fade shell → pinned
            return w;
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Leaf(Transform parent, string name, Vector3 position, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static WorldFptpSplit.VisibilityList ListFor(World w, Func<WorldFptpSplit.Unit, bool> include, string hash = null)
        {
            List<WorldFptpSplit.Unit> units = WorldFptpSplit.Units(w.Root);
            var list = new WorldFptpSplit.VisibilityList
            {
                contentHash = hash ?? WorldFptpSplit.ContentHash(w.Root, units, WorldFptpSplit.EffectiveSunEuler(), WorldFptpSplit.RigSignature()),
                units = units.Count
            };
            foreach (WorldFptpSplit.Unit u in units)
                if (include(u)) list.entries.Add(new WorldFptpSplit.Entry { key = u.Key, family = u.Family, fingerprint = u.Fingerprint, mode = u.Mode.ToString() });
            return list;
        }

        private static readonly List<Scene> s_previews = new List<Scene>();

        /// <summary>The FPTP side as a preview scene: the editor allows one untitled scene, and these are never saved.</summary>
        private static WorldFptpSplit.Report Split(World w, WorldFptpSplit.VisibilityList list, bool force, WorldFptpSplit.Applied applied)
        {
            w.Fptp = EditorSceneManager.NewPreviewScene();
            s_previews.Add(w.Fptp);
            SceneManager.SetActiveScene(w.Main);
            return WorldFptpSplit.Apply(w.Main, w.Root, w.Fptp, list, force, applied);
        }

        [TearDown]
        public void ClosePreviews()
        {
            foreach (Scene s in s_previews)
                if (s.IsValid()) EditorSceneManager.ClosePreviewScene(s);
            s_previews.Clear();
        }

        [Test]
        [Category("R3")]
        public void Ayirma_AyniListe_IkiKosu_AyniIkiSahne_PinliKalir()
        {
            string first = null;
            for (int run = 0; run < 2; run++)
            {
                World w = BuildSyntheticWorld();
                WorldFptpSplit.Report report = Split(w, ListFor(w, u => !u.Root.name.StartsWith("Plaza_", StringComparison.Ordinal)), false, new WorldFptpSplit.Applied());
                Assert.IsTrue(report.Applied, report.ToString());
                Assert.AreEqual(5, report.Moved, "3 sugi (proxy) + ufuk (move) + kutu (twin)");
                Assert.AreEqual(3, report.Proxies);
                Assert.AreEqual(1, report.Twins);
                Assert.AreEqual(7, report.Pinned, "fener adı + CameraOccluder + NavMeshModifier + Lamp_ adı + SOCKET_ + Light + solma kabuğu");
                foreach (string kept in new[] { "Layout v5 (WP-092)/Lamp_L01_emissive", "Layout v5 (WP-092)/Andon_Far", "Layout v5 (WP-092)/Chochin_Far", "Layout v5 (WP-092)/N1_Shell/N1_00_kairo_bay_V02" })
                    Assert.IsTrue(w.Root.Find(kept) != null && w.Root.Find(kept).GetComponent<MeshRenderer>().enabled, "lamba / kabuk dünyada çizer: " + kept);
                Assert.IsNotNull(w.Root.Find("Dress v5 (AP-023)/HorizonRings/Stone_Lantern_Far").GetComponent<MeshRenderer>(), "pinli: dünyada kalır");
                Assert.IsTrue(w.Root.Find("Dress v5 (AP-023)/Plaza_Visible").GetComponent<MeshRenderer>().enabled, "görünen dünyada");
                Assert.IsNull(w.Root.Find("Dress v5 (AP-023)/HorizonRings/Horizon_far"), "ufuk taşındı");
                Assert.IsFalse(w.Root.Find("Greybox v6/VistaRoof_3").GetComponent<MeshRenderer>().enabled, "twin: dünyadaki renderer kapalı");
                Assert.IsNotNull(w.Root.Find("Greybox v6/VistaRoof_3").GetComponent<BoxCollider>(), "twin: collider dünyada");
                WorldFptpOnlyLoader loader = w.Root.GetComponent<WorldFptpOnlyLoader>();
                Assert.IsNotNull(loader, "yükleyici kökte");
                Assert.AreEqual(WorldFptpSplit.SceneName, loader.SceneName);
                string dump = Dump(w.Main) + "\n=====\n" + Dump(w.Fptp);
                if (first == null) first = dump;
                else Assert.AreEqual(first, dump, "aynı liste → aynı iki sahne (deterministik)");
            }

            Debug.Log("[DD-TEST] wp092.split.determinism " + first.Length + " chars");
        }

        [Test]
        [Category("R3")]
        public void Ayirma_KolliderVeNavMeshKaynaklari_Degismez_BirlesimTamDoner()
        {
            World w = BuildSyntheticWorld();
            string colliders = Colliders(w.Main), sources = NavSources(), whole = Dump(w.Main);
            var applied = new WorldFptpSplit.Applied();
            WorldFptpSplit.Report report = Split(w, ListFor(w, u => !u.Root.name.StartsWith("Plaza_", StringComparison.Ordinal)), false, applied);
            Assert.AreEqual(5, report.Moved);
            Assert.AreEqual(colliders, Colliders(w.Main), "dünyanın collider'ları (dünya sınırları, katman, tetik, açık) aynı");
            Assert.AreEqual(sources, NavSources(), "NavMesh kaynakları (iki sahne yüklüyken) aynı");
            Assert.AreEqual("", Colliders(w.Fptp, enabledOnly: true), "FPTP sahnesinde açık collider yok");

            int merged = WorldFptpSplit.Merge(w.Main, w.Fptp, applied);
            Assert.AreEqual(5, merged);
            UnityEngine.Object.DestroyImmediate(w.Root.GetComponent<WorldFptpOnlyLoader>());
            Assert.AreEqual(whole, Dump(w.Main), "birleşim: ayrımdan önceki hiyerarşinin aynısı (sıra dahil)");
        }

        [Test]
        [Category("R3")]
        public void Ayirma_BayatListe_HicbirSeyTasimaz()
        {
            World w = BuildSyntheticWorld();
            string whole = Dump(w.Main);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("stale list"));
            WorldFptpSplit.Report report = Split(w, ListFor(w, u => true, hash: "0000000000000000000000000000000000000000"), false, new WorldFptpSplit.Applied());
            Assert.IsFalse(report.Applied, report.ToString());
            Assert.AreEqual(0, report.Moved);
            Assert.AreEqual(whole, Dump(w.Main), "bayat liste: dünya bütün kalır");
            Assert.IsNull(w.Root.GetComponent<WorldFptpOnlyLoader>(), "bayat liste: yükleyici yok");
        }

        // ------------------------------------------------------------------------------------------------ split (the real world)

        /// <summary>
        /// The committed list on the committed World_V6_Greybox, in memory (nothing saved): units move, every collider and NavMesh build
        /// source of the world is unchanged, no FPTP collider is on, none of the moved renderers is left drawing in the world, and the
        /// merge gives the identical world back. A list audited on other content is applied entry by entry (force); with no entry
        /// left matching the test is inconclusive (run TdVisibilityAudit). Before the first committed list (WP-092's final audit) the
        /// split runs on every movable unit.
        /// </summary>
        [Test]
        [Category("R3")]
        public void GercekDunya_ListeUygulaninca_KolliderVeNavMeshAyni_BirlesimAyni()
        {
            RealWorldSplit(WorldFptpSplit.ReadList());
        }

        /// <summary>The same on every movable unit (no pin) — a superset of any list, so the invariants hold for whatever the audit picks.</summary>
        [Test]
        [Category("R3")]
        public void GercekDunya_HerTasinabilirBirim_KolliderVeNavMeshAyni_BirlesimAyni()
        {
            RealWorldSplit(null);
        }

        private static void RealWorldSplit(WorldFptpSplit.VisibilityList list)
        {
            bool committed = list != null;
            Scene main = WorldFptpSplit.OpenMerged();
            Transform root = WorldFptpSplit.FindRoot(main, Ap005WorldBootstrap.RootName);
            Assert.IsNotNull(root);
            Physics.SyncTransforms();
            string colliders = Colliders(main), sources = NavSources(), whole = Dump(main);
            List<WorldFptpSplit.Unit> before = WorldFptpSplit.Units(root);
            string hash = WorldFptpSplit.ContentHash(root, before, WorldFptpSplit.EffectiveSunEuler(), WorldFptpSplit.RigSignature());
            if (!committed)
            {
                list = new WorldFptpSplit.VisibilityList { contentHash = hash, units = before.Count };
                foreach (WorldFptpSplit.Unit u in before)
                    if (u.Pin == null) list.entries.Add(new WorldFptpSplit.Entry { key = u.Key, family = u.Family, fingerprint = u.Fingerprint, mode = u.Mode.ToString() });
            }

            bool fresh = hash == list.contentHash;

            var w = new World { Main = main, Root = root };
            var applied = new WorldFptpSplit.Applied();
            WorldFptpSplit.Report report = Split(w, list, !fresh, applied);
            if (!fresh && report.Moved == 0) Assert.Inconclusive("liste bu içerikte hiçbir birimi tutmuyor (bayat) — TdVisibilityAudit");
            if (fresh) Assert.AreEqual(list.entries.Count, report.Moved, "taze liste: her giriş taşınır (" + report + ")");
            Assert.Greater(report.Moved, 0, report.ToString());

            Assert.AreEqual(colliders, Colliders(main), "dünyanın collider'ları değişmedi");
            Assert.AreEqual(sources, NavSources(), "NavMesh kaynakları değişmedi");
            Assert.AreEqual("", Colliders(w.Fptp, enabledOnly: true), "FPTP'de açık collider yok");
            var movedKeys = new HashSet<string>();
            foreach (WorldFptpSplit.AppliedEntry e in applied.entries) movedKeys.Add(e.key);
            int drawing = 0;
            foreach (WorldFptpSplit.Unit u in WorldFptpSplit.Units(root))
                if (movedKeys.Contains(u.Key))
                    foreach (Renderer r in u.Renderers) if (r.enabled) drawing++;
            Assert.AreEqual(0, drawing, "taşınan birimin dünyada çizen renderer'ı kalmadı (TD çizmez)");

            int merged = WorldFptpSplit.Merge(main, w.Fptp, applied);
            Assert.AreEqual(report.Moved, merged);
            UnityEngine.Object.DestroyImmediate(root.GetComponent<WorldFptpOnlyLoader>());
            Assert.AreEqual(whole, Dump(main), "birleşim: dünya aynen geri");
            Debug.Log("[DD-TEST] wp092.split.real list=" + (committed ? "committed" : "every-movable") + " moved=" + report.Moved + " proxies=" + report.Proxies + " twins=" + report.Twins + " fresh=" + fresh + " units=" + before.Count);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ------------------------------------------------------------------------------------------------ id codes

        [Test]
        [Category("R3")]
        public void KimlikKodu_UNorm8HedefteTamDoner_5ve6Bit()
        {
            foreach (int bits in new[] { 5, 6 })
            {
                int max = Math.Min(1 << (3 * bits), 1 << 18) - 1;
                for (int id = 1; id <= max; id += id < 4096 ? 1 : 37)
                {
                    Vector4 c = TdVisibilityAudit.Encode(id, bits);
                    uint v = (uint)Mathf.RoundToInt(c.x * 255f) | (uint)Mathf.RoundToInt(c.y * 255f) << 8 | (uint)Mathf.RoundToInt(c.z * 255f) << 16 | 0xFF000000u;
                    Assert.AreEqual(id, TdVisibilityAudit.Decode(v, bits), "bits " + bits);
                    Assert.AreNotEqual(0u, v & 0xFFFFFF, "kimlik siyah (boş) olamaz");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ snapshots

        private static string Colliders(Scene scene, bool enabledOnly = false)
        {
            var lines = new List<string>();
            foreach (GameObject go in scene.GetRootGameObjects())
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                bool on = c.enabled && c.gameObject.activeInHierarchy;
                if (enabledOnly && !on) continue;
                Matrix4x4 m = c.transform.localToWorldMatrix;
                string shape = c is BoxCollider b ? "box " + V(b.center) + " " + V(b.size)
                    : c is SphereCollider s ? "sphere " + V(s.center) + " " + F(s.radius)
                    : c is CapsuleCollider k ? "capsule " + V(k.center) + " " + F(k.radius) + " " + F(k.height) + " " + k.direction
                    : c is MeshCollider mc ? "mesh " + (mc.sharedMesh != null ? mc.sharedMesh.name : "-") + " " + mc.convex : c.GetType().Name;
                lines.Add(c.name + "|" + c.gameObject.layer + "|" + c.isTrigger + "|" + on + "|" + shape + "|" + M(m) + "|" + (c.sharedMaterial != null ? c.sharedMaterial.name : "-"));
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static string NavSources()
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(new Bounds(Vector3.zero, Vector3.one * 2000f), ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var lines = new List<string>(sources.Count);
            foreach (NavMeshBuildSource s in sources) lines.Add(s.shape + "|" + s.area + "|" + V(s.size) + "|" + M(s.transform) + "|" + (s.sourceObject is Mesh mesh ? mesh.name : "-"));
            lines.Sort(StringComparer.Ordinal);
            return lines.Count + "\n" + string.Join("\n", lines);
        }

        /// <summary>The scene as text: every transform (path, active, layer, static flags, local TRS) and its components' state.</summary>
        private static string Dump(Scene scene)
        {
            var sb = new StringBuilder(1 << 16);
            foreach (GameObject go in scene.GetRootGameObjects()) Dump(go.transform, "", sb);
            return sb.ToString();
        }

        private static void Dump(Transform t, string parent, StringBuilder sb)
        {
            string path = parent.Length == 0 ? t.name : parent + "/" + t.name;
            sb.Append(path).Append(" a").Append(t.gameObject.activeSelf ? 1 : 0).Append(" l").Append(t.gameObject.layer)
                .Append(" f").Append((int)GameObjectUtility.GetStaticEditorFlags(t.gameObject)).Append(" p").Append(V(t.localPosition))
                .Append(" r").Append(V(t.localEulerAngles)).Append(" s").Append(V(t.localScale)).Append(PrefabUtility.IsPartOfPrefabInstance(t.gameObject) ? " P" : "");
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(" [").Append(c.GetType().Name);
                if (c is Collider col) sb.Append(col.enabled ? "+" : "-");
                if (c is MeshRenderer mr)
                {
                    sb.Append(mr.enabled ? "+" : "-").Append(' ').Append((int)mr.shadowCastingMode);
                    foreach (Material m in mr.sharedMaterials) sb.Append(' ').Append(m != null ? m.name : "-");
                }

                if (c is MeshFilter mf) sb.Append(' ').Append(mf.sharedMesh != null ? mf.sharedMesh.name : "-");
                if (c is WorldFptpOnlyLoader l) sb.Append(' ').Append(l.SceneName).Append(' ').Append(l.MovedUnits);
                sb.Append(']');
            }

            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), path, sb);
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

        private static string M(Matrix4x4 m)
        {
            var sb = new StringBuilder(96);
            for (int i = 0; i < 16; i++) sb.Append(i > 0 ? "," : "").Append(F(m[i]));
            return sb.ToString();
        }
    }
}
