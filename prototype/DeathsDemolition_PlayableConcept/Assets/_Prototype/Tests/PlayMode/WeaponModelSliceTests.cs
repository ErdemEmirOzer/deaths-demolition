using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-042B · the AP-013 model in hand: equip A/B/C → the classic rig carries the library
    /// model (LODGroup re-thresholded, atlas material), its muzzle / ejection sockets sit at the
    /// contract measures in the socket frame, the mechanism slot reads the model's port; one
    /// orthographic side frame per weapon is written as evidence and the stock does not sink
    /// into the shoulder by more than 2 cm (AP-006 skinned mesh, current hold pose).
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponModelSliceTests
    {
        private const float StockPenetrationMaxMetres = 0.02f;
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            DD.Prototype.CameraSystem.CameraModeService.ResetForTests();   // WP-069 T3 switches to FP
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        private static string EvidenceDir
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "dd-wp042b");   // OS temp (Unity wipes the project Temp on exit); the chain copies it to evidence
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// WP-069 T3 (S-01 WP-070V finding: the FP view drew AP013_A_rifle_LOD2). In FP the weapon in hand — classic A
        /// (the finding), ar_2 (AP-021 merged group) and the Special — is pinned to LOD0 while it sits on the
        /// FirstPersonView layer, and released when the mode leaves FP. Pixel proof on the viewmodel layer alone (the
        /// viewmodel camera's pose and projection): the frame as drawn equals the LOD0 frame and differs from the LOD2
        /// frame; each weapon also writes a 1920×1080 FP close-up as evidence.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator FP_SilahLod0_KlasikVeSpecial_ModDegisinceSerbest()
        {
            yield return Kur();
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            Assert.IsNotNull(binding);
            Transform mount = binding.WeaponMount;
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            yield return SpecialWeaponSahne.Kareler(60);
            var camRig = DD.Prototype.CameraSystem.PrototypeCameraRig.Active;
            Assert.IsNotNull(camRig, "kamera rig'i yok");
            Camera vm = camRig.ViewmodelCamera;
            Assert.IsNotNull(vm, "FP'de görüntü modeli kamerası yok.");
            camRig.Camera.aspect = KareW / (float)KareH;
            vm.aspect = KareW / (float)KareH;

            var report = new List<string>();
            int classics = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                if (d.RosterKey != "A" && d.RosterKey != "ar_2") continue;
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(15);
                LODGroup g = mount.Find("ClassicWeaponRig").GetComponentInChildren<LODGroup>();
                Assert.IsNotNull(g, d.RosterKey + ": LODGroup yok.");
                Assert.IsTrue(_s.Controller.ClassicViewmodelLodPinned, d.RosterKey + ": FP'de LOD0'a sabitlenmedi.");
                report.Add(d.RosterKey + " " + LodKaniti(vm, g, "classic-" + d.RosterKey));
                classics++;
            }

            Assert.AreEqual(2, classics, "A ve ar_2 ölçülmedi.");
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, instant: true);
            yield return SpecialWeaponSahne.Kareler(10);
            Assert.IsFalse(_s.Controller.ClassicViewmodelLodPinned, "FP'den çıkınca klasik silahın LOD sabitlemesi kalkmadı.");

            // The Special: picked up top-down (the kit's walk), then FP again, then TP releases it.
            yield return _s.SpawnEt(0.5f);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup, "Special pickup yok.");
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "pickup_done / yedek");
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            yield return SpecialWeaponSahne.Kareler(15);
            Assert.IsTrue(_s.Controller.SpecialRigVisible, "Special rig görünmüyor.");
            LODGroup sg = _s.Controller.SpecialRigBody.GetComponentInChildren<LODGroup>();
            Assert.IsNotNull(sg, "Special modelinde LODGroup yok.");
            Assert.IsTrue(_s.Controller.SpecialViewmodelLodPinned, "FP'de Special LOD0'a sabitlenmedi.");
            report.Add(pickup.WeaponId + " " + LodKaniti(vm, sg, "special-" + pickup.WeaponId));
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            yield return SpecialWeaponSahne.Kareler(10);
            Assert.IsFalse(_s.Controller.SpecialViewmodelLodPinned, "FP'den çıkınca Special'ın LOD sabitlemesi kalkmadı.");

            camRig.Camera.ResetAspect();
            vm.ResetAspect();
            Debug.Log("[DD-TEST] wp069.t3.fp-lod " + string.Join(" | ", report));
        }

        private const int KareW = 1920, KareH = 1080;

        /// <summary>
        /// The group as pinned vs forced LOD0 / LOD(last): silhouettes on the viewmodel layer (coverage over a magenta
        /// key — the lit colour carries per-render shading noise, the outline does not). The repeat render of the
        /// same state is the noise floor; a PNG of the drawn frame is the evidence.
        /// </summary>
        private static string LodKaniti(Camera vm, LODGroup g, string ad)
        {
            int last = g.lodCount - 1;
            Assert.Greater(last, 0, ad + ": tek LOD — ölçüm anlamsız.");
            VmKare(vm, null);   // warm-up: the probe's first render
            bool[] cizilen = Kapsama(VmKare(vm, ad + "-fp-1080p"));
            bool[] tekrar = Kapsama(VmKare(vm, null));
            g.ForceLOD(0);
            bool[] lod0 = Kapsama(VmKare(vm, null));
            g.ForceLOD(last);
            bool[] lodSon = Kapsama(VmKare(vm, null));
            g.ForceLOD(0);   // back to the pin's state (FP)
            int kapli = 0;
            for (int i = 0; i < cizilen.Length; i++) if (cizilen[i]) kapli++;
            int taban = Fark(cizilen, tekrar), farkLod0 = Fark(cizilen, lod0), farkSon = Fark(lod0, lodSon);
            int esik = Mathf.Max(50, 2 * taban);
            Assert.Greater(kapli, 1000, ad + ": görüntü modeli katmanında çizim yok — ölçüm kör.");
            Assert.Greater(farkSon, 2 * esik, ad + ": LOD0 ile LOD" + last + " silüeti ayrışmıyor (" + farkSon + " px) — pozitif kontrol kör.");
            Assert.LessOrEqual(farkLod0, esik, ad + ": FP silüeti LOD0 silüeti değil (" + farkLod0 + " px, taban " + taban + ").");
            return "coveredPx=" + kapli + " silhouette drawn≠LOD0 " + farkLod0 + " px (noise " + taban + "), LOD0≠LOD" + last + " " + farkSon + " px";
        }

        private static bool[] Kapsama(Color32[] px)
        {
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r < 240 || px[i].g > 15 || px[i].b < 240;   // anything but the magenta key
            return m;
        }

        private static int Fark(bool[] a, bool[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
            return n;
        }

        /// <summary>The FirstPersonView layer alone at 1920×1080 over a magenta key, with the viewmodel camera's pose and projection.</summary>
        private static Color32[] VmKare(Camera vm, string png)
        {
            var target = new RenderTexture(KareW, KareH, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("[WP069T3-LodProbe]");
            go.transform.SetPositionAndRotation(vm.transform.position, vm.transform.rotation);
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(255, 0, 255, 255);
            cam.cullingMask = 1 << DD.Prototype.CameraSystem.CameraLayers.FirstPersonView;
            cam.nearClipPlane = vm.nearClipPlane;
            cam.farClipPlane = vm.farClipPlane;
            cam.fieldOfView = vm.fieldOfView;
            cam.aspect = KareW / (float)KareH;
            cam.projectionMatrix = vm.projectionMatrix;
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(KareW, KareH, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, KareW, KareH), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color32[] px = read.GetPixels32();
            if (png != null) File.WriteAllBytes(Path.Combine(EvidenceDir, "wp069-t3-" + png + ".png"), read.EncodeToPNG());
            Object.Destroy(read);
            cam.targetTexture = null;
            Object.Destroy(go);
            target.Release();
            Object.Destroy(target);
            return px;
        }

        /// <summary>
        /// WP-069 T4 (C-00: the P-06 ↔ P-07 frame question, one measured answer). In the Run scene, with every enabled
        /// classic weapon in the player's hand, the Ejection socket sits on the SHOOTER'S RIGHT of the barrel line (a
        /// centred port, e.g. a break-action breech, is allowed) and the casing throw — the socket's +X, which both
        /// VfxDressing.EjectCasing and WeaponController.PublishMechanism read — points to the shooter's right. World-space
        /// sign test against the character's right (it faces the aim) and the camera's right.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HerSilah_Ejection_AticininSaginda_KovanSagaFirlar()
        {
            yield return Kur();
            ClassicWeaponVisualLibrary lib = ClassicWeaponVisualLibrary.Current;
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            Assert.IsNotNull(binding);
            Transform mount = binding.WeaponMount;
            Assert.IsNotNull(mount, "silah montaj noktası (socket_weapon_r) yok.");
            Camera cam = DD.Prototype.Feedback.FeedbackCameraRef.Current;

            var report = new List<string>();
            var wrong = new List<string>();
            var legacyLeft = new List<string>();
            int measured = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return new WaitForSeconds(0.5f);
                Transform rigT = mount.Find("ClassicWeaponRig");
                Assert.IsNotNull(rigT, d.RosterKey + ": ClassicWeaponRig montajda değil.");
                Transform port = WeaponSocketContract.Find(rigT, WeaponSocketContract.Ejection);
                ClassicWeaponVisualLibrary.Entry own = lib != null ? lib.Find(d.Id) : null;
                string source = own != null && own.ownMaterials ? "AP-021" : "AP-013";
                if (port == null)
                {
                    report.Add(d.RosterKey + " (" + source + ") Ejection yok");
                    continue;
                }

                Vector3 right = _s.Rig.transform.right;
                Vector3 barrel = (_s.Controller.Muzzle.position - mount.position).normalized;
                Vector3 offset = port.position - mount.position;
                float side = Vector3.Dot(offset - Vector3.Dot(offset, barrel) * barrel, right);   // off the barrel line, + = shooter's right
                float throwDot = Vector3.Dot(port.right, right);
                float throwCam = cam != null ? Vector3.Dot(port.right, cam.transform.right) : float.NaN;
                string line = d.RosterKey + " (" + source + ") port " + (side * 100f).ToString("+0.0;-0.0") + " cm, throw·right " + throwDot.ToString("0.00") +
                              ", throw·camRight " + throwCam.ToString("0.00");
                report.Add(line);
                measured++;
                // AP-013 (P-06, M3) authored its port on Blender +X → Unity −X (measured −3.0…−3.5 cm, the shooter's left); P-07's
                // AP-021 set replaces every classic model, so the legacy port side is listed, not failed — its throw must still go right.
                if (throwDot < 0.5f || (source == "AP-021" && side < -0.005f)) wrong.Add(line);
                else if (side < -0.005f) legacyLeft.Add(d.RosterKey);
            }

            Debug.Log("[DD-TEST] wp069.t4.eject " + string.Join(" | ", report) + " || AP-013 port left (legacy): " + string.Join(",", legacyLeft));
            Assert.Greater(measured, 0, "Ejection soketli hiçbir silah ölçülmedi.");
            Assert.IsEmpty(wrong, "Ejection / kovan atıcının SAĞINDA değil: " + string.Join(" | ", wrong));
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HerSilah_AP013Modeli_Elde_Soketler_OrtoKare_KundakOmuz()
        {
            yield return Kur();
            ClassicWeaponVisualLibrary lib = ClassicWeaponVisualLibrary.Current;
            Assert.IsNotNull(lib, "DD_ClassicWeapons kütüphanesi yok.");
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            Assert.IsNotNull(binding);
            Transform mount = binding.WeaponMount;
            Assert.IsNotNull(mount, "silah montaj noktası (socket_weapon_r) yok.");
            var skinned = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>();
            Assert.IsNotNull(skinned, "AP-006 skinned oyuncu yok (kundak–omuz ölçümü gerçek gövdeye karşı).");

            var report = new List<string>();
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return new WaitForSeconds(0.5f);   // the previous weapon's kick settles; the new model is in place
                Assert.AreEqual(WeaponController.ClassicModelKind.Authored, _s.Controller.ClassicModelSource, d.RosterKey + ": AP-013 modeli takılı değil.");
                Assert.IsTrue(_s.Controller.ClassicRigHasAuthoredModel);

                Transform classicRig = mount.Find("ClassicWeaponRig");
                Assert.IsNotNull(classicRig, "ClassicWeaponRig montajda değil.");
                var lodGroup = classicRig.GetComponentInChildren<LODGroup>();
                Assert.IsNotNull(lodGroup, d.RosterKey + ": LODGroup yok.");
                LOD[] lods = lodGroup.GetLODs();
                Assert.AreEqual(3, lods.Length);
                Assert.AreEqual(0.12f, lods[0].screenRelativeTransitionHeight, 1e-3f, "LOD0 eşiği yeniden ayarlanmış (import varsayılanı değil).");
                // WP-069 B4d: an AP-021 model keeps its own materials and its own measures (P-07 manifest), not the AP-009C class blockout's.
                ClassicWeaponVisualLibrary.Entry own = lib.Find(d.Id);
                bool ap021 = own != null && own.ownMaterials;
                foreach (MeshRenderer r in lodGroup.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (ap021) Assert.IsTrue(r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_AP021_"), d.RosterKey + ": AP-021 malzemesi (" + r.name + ")");
                    else Assert.AreEqual(lib.material, r.sharedMaterial, d.RosterKey + ": atlas materyali.");
                }

                // Sockets in the socket frame: the rig's muzzle / ejection are the model's markers.
                Vector3 expectedMuzzle = ap021
                    ? own.model.transform.InverseTransformPoint(WeaponSocketContract.Find(own.model.transform, WeaponSocketContract.Muzzle).position)
                    : WeaponSocketContract.MeasuresOf(d.TypeClass).Muzzle;
                Vector3 muzzleLocal = mount.InverseTransformPoint(_s.Controller.Muzzle.position);
                Assert.Less(Vector3.Distance(muzzleLocal, expectedMuzzle), WeaponSocketContract.MarkerToleranceMetres, d.RosterKey + ": namlu " + muzzleLocal + " ≠ " + expectedMuzzle);

                // WP-059: the weapon frame on the LIVE rig — the mount re-asserts the authored intent after the Animator
                // (facing, level; WeaponMountAlignment), so the muzzle points where the character faces and the grip
                // sits in the palm, for every weapon and every aim pose (the socket bone alone stood the rifle on end).
                var alignment = mount.GetComponent<WeaponMountAlignment>();
                Assert.IsNotNull(alignment, "WeaponMountAlignment montajda yok.");
                Assert.IsTrue(alignment.AlignedThisFrame, d.RosterKey + ": montaj bu karede hizalanmadı.");
                Vector3 muzzleDir = _s.Rig.transform.InverseTransformDirection((_s.Controller.Muzzle.position - mount.position).normalized);
                Assert.Greater(muzzleDir.z, 0.95f, d.RosterKey + ": namlu karakterin önüne bakmıyor " + muzzleDir);
                Assert.Less(Mathf.Abs(muzzleDir.y), 0.2f, d.RosterKey + ": namlu düz değil " + muzzleDir);
                Transform rightHand = skinned.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform grip = WeaponSocketContract.Find(mount, WeaponSocketContract.MarkerGrip);
                Assert.IsNotNull(grip, d.RosterKey + ": marker_grip yok.");
                float gripToHand = Vector3.Distance(grip.position, rightHand.position);
                Assert.Less(gripToHand, 0.08f, d.RosterKey + ": kabza sağ elden " + (gripToHand * 100f).ToString("0.0") + " cm uzakta.");
                report.Add(d.RosterKey + " muzzleDir=" + muzzleDir.ToString("F2") + " grip→hand=" + (gripToHand * 100f).ToString("0.0") + "cm");

                // WP-059C: the alignment keeps the clip's motion — at rest the socket's deviation from its captured aim pose
                // is ~0; one shot's additive kick (Fire_<w>) rotates the hand and the mount follows it (muzzle rise), then settles.
                Assert.IsTrue(alignment.DeltaApplied, d.RosterKey + ": dinlenme pozu referansı yakalanmadı.");
                Assert.Less(alignment.DeltaDegrees, 0.75f, d.RosterKey + ": dinlenmede sapma " + alignment.DeltaDegrees.ToString("0.00") + "°");
                Vector3 muzzleRest = muzzleDir;
                float kickMax = 0f, riseMax = 0f;
                int ammoBefore = _s.Controller.Classic.Ammo;
                _s.Girdi.AtesBasti = true;
                _s.Girdi.AtesBasili = true;
                for (int f = 0; f < 30; f++)
                {
                    yield return null;
                    if (f == 0) { _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false; }
                    kickMax = Mathf.Max(kickMax, alignment.DeltaDegrees);
                    Vector3 muzzleNow = _s.Rig.transform.InverseTransformDirection((_s.Controller.Muzzle.position - mount.position).normalized);
                    riseMax = Mathf.Max(riseMax, muzzleNow.y - muzzleRest.y);
                }

                Assert.Less(_s.Controller.Classic.Ammo, ammoBefore, d.RosterKey + ": atış olmadı.");
                Assert.GreaterOrEqual(kickMax, 1f, d.RosterKey + ": klibin kick dönüşü montaja geçmedi (" + kickMax.ToString("0.00") + "°)");
                yield return new WaitForSeconds(1.2f);
                Assert.Less(alignment.DeltaDegrees, 0.75f, d.RosterKey + ": kick sonrası dinlenmeye dönmedi " + alignment.DeltaDegrees.ToString("0.00") + "°");
                report.Add(d.RosterKey + " kick=" + kickMax.ToString("0.0") + "° muzzleRise=" + riseMax.ToString("0.000"));

                // Orthographic side frame (evidence) — camera on the character's right, looking −X in the mount frame.
                string png = OrthoSideFrame(mount, d.RosterKey);
                report.Add(d.RosterKey + " ortho=" + png);

                // Stock–shoulder: the stock end must not sink more than 2 cm into the AP-006 body (nearest baked vertex, its normal).
                Transform stock = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachStock);
                Assert.IsNotNull(stock, d.RosterKey + ": attach_stock yok.");
                float penetration = SkinnedPenetration(skinned, stock.position, out float nearest);
                report.Add(d.RosterKey + " stockPenetration=" + penetration.ToString("0.000") + " nearestVertex=" + nearest.ToString("0.000"));
                Assert.LessOrEqual(penetration, StockPenetrationMaxMetres, d.RosterKey + ": kundak omuza " + (penetration * 100f).ToString("0.0") + " cm gömülü (> 2 cm).");

                // One shot → the mechanism slot (casing) reads the model's ejection port, +X of the socket frame.
                // The slot fires at the shot (A/B) or at the pump-back 0.4 s later (C, mid-recoil): compare with
                // where the port IS at that moment, sampled inside the handler.
                Transform ejection = WeaponSocketContract.Find(mount, WeaponSocketContract.Ejection);
                Assert.IsNotNull(ejection, d.RosterKey + ": Ejection soketi rig'de yok.");
                // WP-069 B3: rl_1 carries one rocket — the kick shot above emptied it; reload (an empty tube reloads on the
                // trigger or the reload key, never by itself) and wait for it.
                if (_s.Controller.Classic.Ammo == 0 && !_s.Controller.Classic.IsReloading)
                {
                    _s.Girdi.DoldurBasti = true;
                    yield return null;
                    _s.Girdi.DoldurBasti = false;
                }

                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo > 0 && !_s.Controller.Classic.IsReloading, 4f, "dolum");
                // A casing-less weapon (rl_1: WeaponCasing.None) has no ejection slot to measure.
                bool casing = Arsenal.Get(d.Id) == null || Arsenal.Get(d.Id).presentation.casing != WeaponCasing.None;
                var events = new List<WeaponMechanismEvent>();
                var portAtEvent = new List<Vector3>();
                System.Action<WeaponMechanismEvent> onMech = e => { events.Add(e); portAtEvent.Add(ejection.position); };
                WeaponEvents.Mechanism += onMech;
                int ammo0 = _s.Controller.Classic.Ammo;
                _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo < ammo0, 2f, "atış");
                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                if (casing)
                {
                    yield return SpecialWeaponSahne.Bekle(() => events.Count > 0, 1.5f, "kovan slotu");
                    Assert.Less(Vector3.Distance(events[0].Position, portAtEvent[0]), 0.01f, d.RosterKey + ": kovan slotu port konumunda değil (" + Vector3.Distance(events[0].Position, portAtEvent[0]).ToString("0.000") + " m).");
                    Assert.Greater(Vector3.Dot(mount.InverseTransformDirection(events[0].Direction), Vector3.right), 0.7f, d.RosterKey + ": kovan +X'e uçar.");
                }

                WeaponEvents.Mechanism -= onMech;
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 4f, "reload biter");

            }

            Debug.Log("[DD-TEST] wp042b.model " + string.Join(" | ", report));
        }

        /// <summary>Render the mount region from the character's right side with a temporary orthographic camera; returns the PNG path ("" when rendering is unavailable).</summary>
        private static string OrthoSideFrame(Transform mount, string key)
        {
            var go = new GameObject("WP042B_OrthoSide");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 0.9f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 6f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
                // Level side view: the character's right, projected on the ground plane (the socket's own right tilts with the hold).
                Vector3 side = Vector3.ProjectOnPlane(mount.root.right, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                Vector3 centre = mount.position + Vector3.down * 0.15f;
                cam.transform.position = centre + side.normalized * 2.5f;
                cam.transform.LookAt(centre, Vector3.up);
                var rt = new RenderTexture(768, 768, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                string path = Path.Combine(EvidenceDir, "ortho_side_" + key + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
                Object.Destroy(rt);
                return path;
            }
            catch (System.Exception e)
            {
                Debug.Log("[DD-TEST] wp042b.ortho unavailable: " + e.Message);
                return "";
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        /// <summary>Depth (m) the point sits behind the nearest baked vertex of the character's LOD0 skin along that vertex's normal; 0 when outside.</summary>
        private static float SkinnedPenetration(PlayerSkinnedVisual skinned, Vector3 point, out float nearestDistance)
        {
            nearestDistance = float.MaxValue;
            if (skinned == null || skinned.Renderers == null || skinned.Renderers.Length == 0) return 0f;
            SkinnedMeshRenderer best = null;
            for (int i = 0; i < skinned.Renderers.Length; i++)
            {
                SkinnedMeshRenderer r = skinned.Renderers[i];
                if (r == null || r.sharedMesh == null) continue;
                if (best == null || r.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = r;
            }

            if (best == null) return 0f;
            var baked = new Mesh();
            best.BakeMesh(baked);
            Vector3[] verts = baked.vertices;
            Vector3[] normals = baked.normals;
            Transform t = best.transform;
            int nearest = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float sq = (t.TransformPoint(verts[i]) - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; nearest = i; }
            }

            float penetration = 0f;
            if (nearest >= 0)
            {
                nearestDistance = Mathf.Sqrt(bestSq);
                Vector3 n = normals != null && normals.Length == verts.Length ? t.TransformDirection(normals[nearest]).normalized : Vector3.up;
                float signed = Vector3.Dot(point - t.TransformPoint(verts[nearest]), n);
                penetration = Mathf.Max(0f, -signed);
            }

            Object.Destroy(baked);
            return penetration;
        }
    }
}
