using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = UnityEngine.TestTools.Constraints.Is;   // derives from NUnit's Is: Is.InRange etc. still resolve
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R4 WP-106 P1 · Prepare Run's weapon stage in a live URP frame (an overlay canvas, a 640 × 360 host):
    ///   1. Create → a target the host's size on the view; two weapons → a render each; the models are cached per id; idle frames draw nothing.
    ///   2. The weapon lies on the post caps (AD M1): its seat's tilt and place, along the posts' line, the lowest point over the
    ///      disc, the class display length, on the stage layer; the turntable rests turned to the first view; the ground is
    ///      SurfaceInset (AD M2) and the set is placed through the mirror fix (S1).
    ///   3. Orbit / Zoom keep to the json's limits; several changes in one frame are one render however many frames follow;
    ///      the camera is never enabled (fix, PM run w106-tests 12.10: an enabled camera was drawn on every editor tick until the
    ///      next LateUpdate - one change ~20 renders); ResetView.
    ///   4. Reduced Motion: the auto turn neither turns nor draws; without it the weapon turns.
    ///   5. A hidden view (inactive, CanvasGroup α 0) never draws; shown again it draws what changed.
    ///   6. The pad through UiInput's seam turns, tilts and zooms; the pointer's drag / wheel / double click.
    ///   7. 0 B managed allocation per frame (Tick with the pad held, the auto turn, the pointer path) - with a positive control.
    ///   8. Destroying the host releases the target, the look profile and the whole stage root.
    /// </summary>
    [TestFixture]
    [Category("WP106")]
    public sealed class PrepareRunStagePlayTests
    {
        private const float Wait = 5f;
        private readonly List<Object> _temp = new List<Object>();
        private bool _reducedWas;

        [SetUp]
        public void SetUp()
        {
            _reducedWas = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiMotion.TestNow = null;
            UiInput.ClearSimulatedViewInput();
        }

        [TearDown]
        public void TearDown()
        {
            UiInput.ClearSimulatedViewInput();
            UiMotion.TestNow = null;
            UiMotion.ReducedMotion = _reducedWas;
            foreach (Object o in _temp)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _temp.Clear();
        }

        // ------------------------------------------------------------------ 1 · create, two weapons, cache, idle

        [UnityTest]
        public IEnumerator Olustur_IkiSilah_HerDegisimBirCizim_ModelOnbellekte_BostaCizmez()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = false;
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "ilk çizim olmadı");
            Assert.AreEqual(1, stage.Renders, "açılış tek çizim (kamera talep üzerine çizer, açık kalmaz)");
            Assert.IsFalse(stage.StageCamera.enabled, "sahne kamerası hiç açılmaz");
            Assert.AreEqual(640, stage.Target.width, "hedef host'un piksel boyunda");
            Assert.AreEqual(360, stage.Target.height);
            Assert.AreSame(stage.Target, stage.View.texture, "RawImage hedefi gösterir");
            Assert.AreNotEqual("MainCamera", stage.StageCamera.tag, "sahne kamerası ana kamera değildir");
            Assert.AreEqual(1 << PrepareRunStage.StageLayer, stage.StageCamera.cullingMask, "yalnız sahne katmanı");

            int r0 = stage.Renders;
            stage.ShowWeapon("ar_1");
            yield return LifecycleTestKit.Bekle(() => stage.Renders > r0, Wait, "silah değişimi çizmedi");
            Assert.AreEqual("ar_1", stage.WeaponId);
            Assert.IsNotNull(stage.CurrentModel, "ar_1 modeli (DD_ClassicWeapons)");
            Assert.IsTrue(stage.CurrentModel.activeSelf);

            int r1 = stage.Renders;
            GameObject first = stage.CurrentModel;
            stage.ShowWeapon("smg_1");
            yield return LifecycleTestKit.Bekle(() => stage.Renders > r1, Wait, "ikinci silah çizmedi");
            Assert.AreEqual("smg_1", stage.WeaponId);
            Assert.IsFalse(first.activeSelf, "önceki model gizlenir");
            Assert.AreEqual(2, stage.CachedModelCount);

            stage.ShowWeapon("AssaultRifle");
            Assert.AreEqual("ar_1", stage.WeaponId, "M3 adı v3 anahtarına çözülür");
            Assert.AreSame(first, stage.CurrentModel, "aynı kimlik: önbellekteki model, yeni kurulum yok");
            Assert.AreEqual(2, stage.CachedModelCount);

            yield return null;
            yield return null;
            int idle = stage.Renders;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(idle, stage.Renders, "değişiklik yokken kamera çizmez");
            Assert.IsFalse(stage.StageCamera.enabled, "boşta kamera kapalı");
        }

        // ------------------------------------------------------------------ 2 · the seat and the fit

        [UnityTest]
        public IEnumerator Silah_DayanaklaraOturur_KoltukTablosu_SinifUzunlugunda_SahneKatmaninda()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = false;
            stage.ShowWeapon("ar_1");
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");

            GameObject model = stage.CurrentModel;
            Transform socket = stage.WeaponSocketTransform;
            Transform mount = model != null ? model.transform.parent : null;
            Assert.IsNotNull(model);
            Assert.IsNotNull(socket);
            Assert.IsTrue(model.transform.IsChildOf(socket), "silah soketle (rotorla) döner");
            Transform root = stage.StageCamera.transform.parent;
            Assert.AreEqual(PrepareRunStage.WeaponSocketHeight, root.InverseTransformPoint(socket.position).y, 0.005f, "SOCKET_Weapon 0.94 m");
            Assert.AreEqual(UiTokens.Get(UiColor.SurfaceInset), stage.StageCamera.backgroundColor, "AD M2: zemin SurfaceInset");

            Bounds local = AuthoredWeaponModel.LocalLod0Bounds(model.transform);
            float scale = model.transform.localScale.x;
            Assert.AreEqual(scale, model.transform.localScale.y, 1e-5f, "tek uniform ölçek");
            Assert.AreEqual(scale, model.transform.localScale.z, 1e-5f);
            WeaponDefinition definition = Arsenal.Get(ClassicWeaponId.AssaultRifle);
            float length = PrepareRunStage.DisplayLength(definition != null ? definition.weaponClass : WeaponClass.AssaultRifle);
            Assert.AreEqual(length, Mathf.Max(local.size.x, local.size.z) * scale, 0.005f, "sınıf uzunluğu");

            Assert.IsTrue(PrepareRunStage.TrySeat("ar_1", scale, out PrepareRunStage.Seat seat), "ar_1 koltuğu bu ölçekte (" + scale.ToString("F4") + ")");
            Assert.Less(Quaternion.Angle(PrepareRunStage.SeatRotation(seat), model.transform.localRotation), 0.01f, "koltuğun eğimi");
            Assert.Less((PrepareRunStage.SeatPosition(local, scale, seat) - model.transform.localPosition).magnitude, 1e-4f, "koltuğun yeri");

            // The barrel along the posts' line and, at rest, square to the first view with the muzzle on the screen's left.
            Vector3 barrel = root.InverseTransformDirection(mount.forward);
            Vector3 expected = Quaternion.Euler(0f, PrepareRunStage.TurntableRestYaw, 0f) * Vector3.right;
            Assert.Less(Vector3.Angle(barrel, expected), 0.5f, "namlu dayanak hattında, tabla dinlenme dönüşünde: " + barrel);

            float lowest = float.MaxValue;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(PrepareRunStage.StageLayer, r.gameObject.layer, r.name + " sahne katmanında");
                if (AuthoredWeaponModel.LodIndexOf(r.name) == 0)
                {
                    lowest = Mathf.Min(lowest, root.InverseTransformPoint(r.bounds.min).y);
                }
            }

            Assert.Less(lowest, float.MaxValue, "LOD0 çizici yok");
            Assert.GreaterOrEqual(lowest, PrepareRunStage.RotorDiscTop, "silahın en alt noktası diskin üstünde (AD: ≥ 0.71 m; AABB ölçümü)");
            Assert.LessOrEqual(lowest, PrepareRunStage.PostCapTop, "silah kapaklardan aşağı iner (havada değil)");

            if (stage.HasSet)
            {
                Transform set = root.Find("PR_Stage");
                Assert.IsNotNull(set);
                foreach (PrepareRunStage.SetPiece piece in PrepareRunStage.Set)
                {
                    Transform t = set.Find(piece.Name);
                    Assert.IsNotNull(t, piece.Name);
                    Assert.Less((t.localPosition - piece.Position).magnitude, 1e-4f, piece.Name + " S1 aynası");
                }
            }
        }

        // ------------------------------------------------------------------ 3 · orbit and zoom

        [UnityTest]
        public IEnumerator OrbitZoom_JsonSinirlarinda_BirKarede_TekCizim_ResetView()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = false;
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");
            Assert.AreEqual(1, stage.Renders, "açılış tek çizim");
            Assert.AreEqual(PrepareRunStage.InitialPitch, stage.Pitch, 1e-3f);
            Assert.AreEqual(PrepareRunStage.InitialDistance, stage.Distance, 1e-3f);

            stage.Orbit(new Vector2(30f, 1000f));
            Assert.AreEqual(30f, stage.Yaw, 1e-3f);
            Assert.AreEqual(PrepareRunStage.PitchMax, stage.Pitch, 1e-4f, "eğim üst sınırı 35°");
            stage.Orbit(new Vector2(400f, -1000f));
            Assert.AreEqual(70f, stage.Yaw, 1e-3f, "dönüş serbest, katlanır");
            Assert.AreEqual(PrepareRunStage.PitchMin, stage.Pitch, 1e-4f, "eğim alt sınırı 8°");
            stage.Zoom(100f);
            Assert.AreEqual(PrepareRunStage.DistanceMin, stage.Distance, 1e-4f, "en yakın 3.8 m");
            stage.Zoom(-100f);
            Assert.AreEqual(PrepareRunStage.DistanceMax, stage.Distance, 1e-4f, "en uzak 6.1 m");

            int before = stage.Renders;
            for (int i = 0; i < 6; i++)
            {
                yield return null;
                Assert.IsFalse(stage.StageCamera.enabled, "kamera hiç açılmaz (kare " + i + ")");
            }

            Assert.AreEqual(before + 1, stage.Renders, "bir karedeki birkaç değişiklik, ardından kaç kare geçerse geçsin, tek çizim");

            Camera cam = stage.StageCamera;
            Vector3 target = cam.transform.parent.TransformPoint(PrepareRunStage.OrbitTarget);
            Assert.AreEqual(stage.Distance, (cam.transform.position - target).magnitude, 1e-3f, "kamera yörüngede");

            stage.ResetView();
            Assert.AreEqual(0f, stage.Yaw);
            Assert.AreEqual(PrepareRunStage.InitialPitch, stage.Pitch, 1e-4f);
            Assert.AreEqual(PrepareRunStage.InitialDistance, stage.Distance, 1e-4f);
            yield return LifecycleTestKit.Bekle(() => stage.Renders > before + 1, Wait, "ResetView çizmedi");
            yield return null;
            yield return null;
            Assert.AreEqual(before + 2, stage.Renders, "ResetView tek çizim");
        }

        // ------------------------------------------------------------------ 4 · Reduced Motion

        [UnityTest]
        public IEnumerator AzaltilmisHareket_OtomatikDonusDonmez_Cizmez_KapaninDoner()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = true;
            UiMotion.ReducedMotion = true;
            float t = 100f;
            UiMotion.TestNow = t;
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");
            yield return null;

            float yaw0 = stage.Yaw;
            int r0 = stage.Renders;
            for (int i = 0; i < 20; i++)
            {
                t += 0.05f;
                UiMotion.TestNow = t;
                yield return null;
            }

            Assert.AreEqual(yaw0, stage.Yaw, 1e-4f, "RM: otomatik dönüş yok");
            Assert.AreEqual(r0, stage.Renders, "RM: dönmeyen sahne çizilmez");

            UiMotion.ReducedMotion = false;
            for (int i = 0; i < 20; i++)
            {
                t += 0.05f;
                UiMotion.TestNow = t;
                yield return null;
            }

            float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, stage.Yaw));
            Assert.That(turned, Is.InRange(8f, 12.5f), "~1 s × 12°/s yavaş dönüş: " + turned);
            Assert.Greater(stage.Renders, r0, "dönüş çizer");
            Assert.LessOrEqual(stage.Renders - r0, 20, "en çok kare başına bir çizim (≤ 30/s)");
            Assert.GreaterOrEqual(stage.Renders - r0, 19, "0.05 s'lik her dönüş karesi bir çizim (≥ 1/30 s)");
        }

        // ------------------------------------------------------------------ 5 · hidden

        [UnityTest]
        public IEnumerator Gizli_Cizmez_GosterilinceDegisenCizilir()
        {
            PrepareRunStage stage = NewStage(out RectTransform host);
            stage.AutoTurn = false;
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");

            host.gameObject.SetActive(false);
            yield return null;
            int r = stage.Renders;
            stage.ShowWeapon("sg_1");
            stage.Orbit(new Vector2(10f, 0f));
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(r, stage.Renders, "etkin olmayan görünüm çizmez");
            Assert.IsFalse(stage.StageCamera.enabled);
            Assert.IsFalse(stage.Visible);

            host.gameObject.SetActive(true);
            yield return LifecycleTestKit.Bekle(() => stage.Renders > r, Wait, "gösterilince bekleyen değişiklik çizilmedi");

            var group = host.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            yield return null;
            yield return null;
            int hidden = stage.Renders;
            stage.Orbit(new Vector2(10f, 0f));
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(hidden, stage.Renders, "α 0 görünüm çizmez");
            group.alpha = 1f;
            yield return LifecycleTestKit.Bekle(() => stage.Renders > hidden, Wait, "α 1 olunca çizmedi");
        }

        // ------------------------------------------------------------------ 6 · the pad and the pointer

        [UnityTest]
        public IEnumerator Pad_UiInputTohumuyla_DonerEgerYakinlasir_IsaretciSurukleTekerlekCiftTik()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = false;
            float t = 10f;
            UiMotion.TestNow = t;
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");
            yield return null;

            UiInput.SimulateViewInput(new Vector2(1f, 0f), 1f);
            for (int i = 0; i < 10; i++)
            {
                t += 0.05f;
                UiMotion.TestNow = t;
                yield return null;
                Assert.IsTrue(stage.Interacting, "pad tutulurken etkileşim");
            }

            Assert.AreEqual(-75f, stage.Yaw, 8f, "sağ çubuk tam sağa: ~0.5 s × 150°/s, ön yüz sağa (yaw azalır)");
            Assert.Less(stage.Distance, PrepareRunStage.InitialDistance, "RT yakınlaştırır");

            UiInput.SimulateViewInput(new Vector2(0f, 1f), -1f);
            float pitch = stage.Pitch;
            float distance = stage.Distance;
            for (int i = 0; i < 4; i++)
            {
                t += 0.05f;
                UiMotion.TestNow = t;
                yield return null;
            }

            Assert.Less(stage.Pitch, pitch + 1e-4f, "çubuk yukarı: kamera alçalır (oyun kamerası gibi)");
            Assert.Greater(stage.Distance, distance, "LT uzaklaştırır");

            UiInput.ClearSimulatedViewInput();
            t += 0.05f;
            UiMotion.TestNow = t;
            yield return null;
            Assert.IsFalse(stage.Interacting);

            // The pointer (the EventSystem's own dispatch).
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                var es = new GameObject("PrepareRunTestEventSystem", typeof(EventSystem));
                _temp.Add(es);
                system = es.GetComponent<EventSystem>();
            }

            stage.ResetView();
            GameObject view = stage.View.gameObject;
            var pointer = new PointerEventData(system) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(stage.Interacting, "sürükleme etkileşimdir");
            pointer.delta = new Vector2(36f, 0f);   // a tenth of the 360 px view → 24°
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.dragHandler);
            Assert.AreEqual(-PrepareRunStage.DragTurnPerHeight * 0.1f, stage.Yaw, 0.5f, "sağa sürükleme ön yüzü sağa döndürür");
            pointer.delta = new Vector2(0f, -36f);
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.dragHandler);
            Assert.AreEqual(PrepareRunStage.ClampPitch(PrepareRunStage.InitialPitch + PrepareRunStage.DragTiltPerHeight * 0.1f), stage.Pitch, 0.1f, "aşağı sürükleme kamerayı yükseltir");
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.endDragHandler);
            Assert.IsFalse(stage.Interacting);

            pointer.scrollDelta = new Vector2(0f, PrepareRunStage.ScrollLinesPerStep);
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.scrollHandler);
            Assert.AreEqual(PrepareRunStage.InitialDistance / PrepareRunStage.ZoomStep, stage.Distance, 1e-3f, "tekerlek bir adım yakınlaştırır");

            pointer.clickCount = 2;
            ExecuteEvents.Execute(view, pointer, ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(0f, stage.Yaw, "çift tık: ResetView");
            Assert.AreEqual(PrepareRunStage.InitialDistance, stage.Distance, 1e-4f);
        }

        // ------------------------------------------------------------------ 7 · allocation

        [UnityTest]
        public IEnumerator KareBasi_SifirAyirma_PadOtomatikDonusIsaretci()
        {
            PrepareRunStage stage = NewStage(out _);
            stage.AutoTurn = true;
            stage.ShowWeapon("ar_1");
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");
            yield return null;

            float t = 1000f;
            for (int i = 0; i < 5; i++)
            {
                stage.Tick(t += 1f / 60f);   // warm: first calls into UiInput / UiCurtain / UiMotion statics
            }

            object[] bait = null;
            Assert.That(() => { bait = new object[64]; }, Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            System.GC.KeepAlive(bait);

            UiInput.SimulateViewInput(new Vector2(0.5f, 0.25f), 1f);
            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    stage.Tick(t += 1f / 60f);
                }
            }, Is.Not.AllocatingGCMemory(), "pad tutulurken kare yolu ayırdı (bu iş parçacığında GC.Alloc)");

            UiInput.ClearSimulatedViewInput();
            t += 10f;   // past the auto turn's wait
            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    stage.Tick(t += 1f / 60f);
                }
            }, Is.Not.AllocatingGCMemory(), "otomatik dönüş kare yolu ayırdı");

            EventSystem system = EventSystem.current;
            if (system == null)
            {
                var es = new GameObject("PrepareRunTestEventSystem", typeof(EventSystem));
                _temp.Add(es);
                system = es.GetComponent<EventSystem>();
            }

            var pointer = new PointerEventData(system) { button = PointerEventData.InputButton.Left, delta = new Vector2(3f, 1f), scrollDelta = new Vector2(0f, 1f) };
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    stage.OnBeginDrag(pointer);
                    stage.OnDrag(pointer);
                    stage.OnEndDrag(pointer);
                    stage.OnScroll(pointer);
                }
            }, Is.Not.AllocatingGCMemory(), "işaretçi yolu ayırdı");

            yield return null;
            Debug.Log("[DD-TEST] wp106.stage renders=" + stage.Renders + " target=" + stage.Target.width + "x" + stage.Target.height + " msaa=" + stage.Target.antiAliasing + " set=" + stage.HasSet);
        }

        // ------------------------------------------------------------------ 8 · release

        [UnityTest]
        public IEnumerator HostYokEdilince_HedefProfilVeSahneKokuBirakilir()
        {
            PrepareRunStage stage = NewStage(out RectTransform host);
            stage.ShowWeapon("ar_1");
            yield return LifecycleTestKit.Bekle(() => stage.Renders >= 1, Wait, "çizim olmadı");

            RenderTexture target = stage.Target;
            GameObject world = stage.StageCamera.transform.parent.gameObject;
            Assert.AreEqual(PrepareRunStage.WorldName, world.name, "sahne kökü");
            Assert.GreaterOrEqual(Count<ScriptableObject>(PrepareRunStage.ProfileName), 1, "bakış profili");

            Object.Destroy(host.gameObject);
            yield return null;
            yield return null;

            Assert.IsTrue(target == null, "hedef bırakıldı");
            Assert.IsTrue(world == null, "kök (set, ışıklar, hacim, kamera, modeller) yok edildi");
            Assert.AreEqual(0, Count<RenderTexture>(PrepareRunStage.TargetName), "artık hedef yok");
            Assert.AreEqual(0, Count<ScriptableObject>(PrepareRunStage.ProfileName), "artık profil yok");
        }

        // ------------------------------------------------------------------ helpers

        private PrepareRunStage NewStage(out RectTransform host)
        {
            var canvasGo = new GameObject("PrepareRunTestCanvas", typeof(RectTransform), typeof(Canvas));
            _temp.Add(canvasGo);
            canvasGo.layer = PrepareRunStage.StageLayer;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            host = UiFactory.NewRect("PrepareRunTestHost", canvasGo.transform);
            host.sizeDelta = new Vector2(640f, 360f);
            return PrepareRunStage.Create(host);
        }

        private static int Count<T>(string name) where T : Object
        {
            int n = 0;
            foreach (T o in Resources.FindObjectsOfTypeAll<T>())
            {
                if (o != null && o.name == name)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
