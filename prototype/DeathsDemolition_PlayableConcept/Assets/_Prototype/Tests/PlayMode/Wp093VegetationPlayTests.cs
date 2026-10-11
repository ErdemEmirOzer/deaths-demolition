using System.Collections;
using DD.Prototype.World.Vegetation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-093 Faz 1 · living ground in a synthetic scene (no world scene): a flat 32 × 32 m field placed by the real
    /// probe → zoner → placer chain, the TD camera (15 m up, 13 m back, 42°): instances are culled and submitted
    /// (> 0 visible, draws, chunks) and nothing is drawn when the camera looks away; one simulation step + one cull/draw
    /// is 0 B (WP-059D pattern with a positive control); the push API bends (value at the pusher &gt; 0, springs back).
    /// TD only.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093VegetationPlayTests
    {
        private GameObject _cameraGo;
        private Camera _camera;
        private VegetationSystem _system;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("WP093 TD camera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 300f;
            _cameraGo.transform.position = new Vector3(0f, 15f, -13f);
            _cameraGo.transform.LookAt(Vector3.zero);

            _system = VegetationSystem.Ensure();
            _system.ClearWorld();
            _system.CameraOverride = _camera;
            _system.TierOverride = 1;                 // High: the tier the 1.0 ms budget is set at
            _system.DrawFromCameraCallback = false;   // the test drives RenderFor itself
            _system.StampActors = false;
        }

        [TearDown]
        public void TearDown()
        {
            // Immediate: the next SetUp's Ensure() must not find a system that is only pending destruction.
            if (_system != null) Object.DestroyImmediate(_system.gameObject);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
        }

        private static VegetationDensityField FlatField(float half)
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            var o = new VegetationProbeOptions
            {
                Min = new Vector2(-half, -half), Max = new Vector2(half, half),
                FlatGround = true, UseNavRegions = false, UseMarkers = false
            };
            return VegetationZoner.Evaluate(VegetationWorldProbe.Probe(o, recipe), recipe);
        }

        [UnityTest]
        public IEnumerator SentetikSahne_TdKamerasi_OrnekCizer_ArkayaBakincaCizmez()
        {
            _system.BuildNow(FlatField(16f));
            Assert.IsTrue(_system.IsBuilt);
            Assert.Greater(_system.TotalInstances, 1000, "flat lawn placed almost nothing");
            yield return null;

            _system.Step(0.016f);
            _system.RenderFor(_camera);
            Assert.Greater(_system.ChunksVisible, 0, "no chunk survived culling under the TD camera");
            Assert.Greater(_system.VisibleInstances, 0, "no instance submitted");
            Assert.Greater(_system.DrawCalls, 0, "no draw call");
            Assert.LessOrEqual(_system.ChunksVisible, _system.ChunksTotal);
            Assert.LessOrEqual(_system.VisibleInstances, _system.TotalInstances * 3, "LOD cross-fade overlap out of bounds");

            // Look at the sky: nothing should pass the frustum.
            _cameraGo.transform.rotation = Quaternion.LookRotation(Vector3.up + Vector3.forward * 0.01f);
            _cameraGo.transform.position = new Vector3(0f, 60f, 0f);
            _system.RenderFor(_camera);
            Assert.AreEqual(0, _system.VisibleInstances, "instances drawn outside the frustum");
            Assert.AreEqual(0, _system.DrawCalls);
        }

        [UnityTest]
        public IEnumerator SentetikSahne_AdimVeCizim_GcSifir_PozitifKontrol()
        {
            _system.StampActors = true;   // the player / zombie loops run (empty registries in this scene)
            _system.BuildNow(FlatField(16f));

            void Rep()
            {
                VegetationSystem.Push(new Vector3(2f, 0f, 3f), 2.5f, 1f);
                _system.PushMap.Stamp(new Vector2(-1f, 1f), new Vector2(2f, 0f), 0.55f, 1f, 1.5f, 0.6f, 0.6f, 0.016f);
                _system.Step(0.016f);
                _system.RenderFor(_camera);
            }

            _system.Step(0.016f);   // creates the push map, finds the wind driver (load-time allocations)
            for (int rep = 0; rep < 3; rep++) Rep();
            yield return null;
            yield return null;

            Assert.Greater(_system.VisibleInstances, 0, "the measured frame drew nothing (the 0 B claim would be empty)");
            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() => Rep(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "bitki adım + kırpma + çizim 0 B");
            Assert.IsNotNull(bait);
        }

        [UnityTest]
        public IEnumerator ItmeApisi_Eger_SonraToparlanir()
        {
            _system.BuildNow(FlatField(16f));
            _system.Step(0.016f);   // window centred on the camera focus (no player in this scene)
            VegetationPushMap map = _system.PushMap;
            Assert.IsNotNull(map);
            Assert.IsNotNull(map.Texture, "push map has no texture");

            // FINAL 10.10 perf: High × TopDown runs the Performance-size map (24 m), so the points sit relative to the window's centre
            Vector2 centre = map.Origin + Vector2.one * (map.WorldSize * 0.5f);
            Vector2 at = centre + new Vector2(1f, 2f);
            map.Stamp(at, Vector2.zero, 0.55f, 1f, 1.5f, 0.6f, 0f, 0.016f);
            float d0 = map.DisplacementAt(at);
            Assert.Greater(d0, 0f, "interaction value at the pusher is 0");
            Assert.Greater(map.FlattenAt(at), 0f, "no flatten at the pusher");

            // Explosion API: the blast core shoves at once.
            VegetationSystem.Push(new Vector3(centre.x - 4f, 0f, centre.y - 3f), 3f, 1f);
            Assert.Greater(map.DisplacementAt(centre + new Vector2(-3f, -3f)), 0.5f, "VegetationSystem.Push did not shove the core");

            for (int i = 0; i < 45; i++)
            {
                _system.Step(0.016f);   // ≈ 0.72 s, no new stamps at 'at'
                yield return null;
            }

            Assert.Less(map.DisplacementAt(at), 0.15f * Mathf.Max(d0, 0.05f) + 0.01f, "displacement did not spring back in ≈ 0.6 s");
        }
    }
}
