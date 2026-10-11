using System;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// R4 WP-106 P1 · Prepare Run's weapon stage, the rules that need no frame: the target's size, the 45 mm lens, the AP-040
    /// camera orbit read back from its first pose, the clamps, the zoom and wheel steps, the one-scale fit and the seat, the
    /// muzzle's rest heading, the id table, the json's lights, the pad's view controls through UiInput (a stub source), the
    /// imported set when it is there, and the stage file's hand-made motion count (T6's patterns).
    /// </summary>
    [TestFixture]
    [Category("WP106")]
    public sealed class PrepareRunStageTests
    {
        private const float Eps = 1e-3f;

        // ------------------------------------------------------------------ target and lens

        [Test]
        public void HedefBoyutu_KisaKenarEnCok1024_UzunKenarEnCok2048_EnAz16()
        {
            Assert.AreEqual(new Vector2Int(640, 360), PrepareRunStage.TargetSize(new Vector2(640f, 360f), PrepareRunStage.MaxShortSide));
            Assert.AreEqual(new Vector2Int(1820, 1024), PrepareRunStage.TargetSize(new Vector2(2560f, 1440f), PrepareRunStage.MaxShortSide), "kısa kenar 1024'e iner, oran korunur");
            Vector2Int tall = PrepareRunStage.TargetSize(new Vector2(600f, 4000f), PrepareRunStage.MaxShortSide);
            Assert.AreEqual(PrepareRunStage.MaxLongSide, tall.y, "uzun kenar 2048'i aşmaz");
            Assert.AreEqual(307, tall.x, "oran korunur");
            Assert.AreEqual(new Vector2Int(16, 16), PrepareRunStage.TargetSize(Vector2.zero, PrepareRunStage.MaxShortSide), "boş yerleşim ilk karede 16²");
        }

        [Test]
        public void Lens45mm_SensorAuto_YatayCerceveGenisligeOturur()
        {
            float landscape = PrepareRunStage.VerticalFov(45f, 36f, 16f / 9f);
            Assert.AreEqual(25.36f, landscape, 0.05f, "16:9: 36 mm yatay → dikey 25.4°");
            float square = PrepareRunStage.VerticalFov(45f, 36f, 1f);
            Assert.AreEqual(43.60f, square, 0.05f, "kare: 36 mm dikey");
            Assert.AreEqual(square, PrepareRunStage.VerticalFov(45f, 36f, 0.5f), 1e-4f, "dikey çerçeve: sensör uzun kenara (dikeye) oturur");
        }

        // ------------------------------------------------------------------ the orbit

        [Test]
        public void IlkPoz_JsonKameraOrbitiniGeriVerir()
        {
            Assert.AreEqual(5.713799f, PrepareRunStage.InitialDistance, Eps, "camera_orbit.distance_m");
            Assert.AreEqual(14.70f, PrepareRunStage.InitialPitch, 0.01f);
            Assert.AreEqual(28.65f, PrepareRunStage.InitialAzimuth, 0.01f);
            Assert.That(PrepareRunStage.InitialPitch, NUnit.Framework.Is.InRange(PrepareRunStage.PitchMin, PrepareRunStage.PitchMax), "ilk poz json'un sınırları içinde");
            Assert.That(PrepareRunStage.InitialDistance, NUnit.Framework.Is.InRange(PrepareRunStage.DistanceMin, PrepareRunStage.DistanceMax));

            Vector3 eye = PrepareRunStage.OrbitTarget + PrepareRunStage.OrbitOffset(PrepareRunStage.InitialAzimuth, PrepareRunStage.InitialPitch, PrepareRunStage.InitialDistance);
            Assert.Less((eye - PrepareRunStage.InitialCamera).magnitude, Eps, "yörünge ilk kamerayı (2.65, 2.45, 4.70) geri verir: " + eye);
        }

        [Test]
        public void Kelepceler_EgimMesafeDonus()
        {
            Assert.AreEqual(PrepareRunStage.PitchMin, PrepareRunStage.ClampPitch(-40f));
            Assert.AreEqual(PrepareRunStage.PitchMax, PrepareRunStage.ClampPitch(90f));
            Assert.AreEqual(20f, PrepareRunStage.ClampPitch(20f));
            Assert.AreEqual(PrepareRunStage.DistanceMin, PrepareRunStage.ClampDistance(0.5f));
            Assert.AreEqual(PrepareRunStage.DistanceMax, PrepareRunStage.ClampDistance(50f));
            Assert.AreEqual(10f, PrepareRunStage.WrapYaw(370f), Eps, "dönüş serbest, (−180, 180]'e katlanır");
            Assert.AreEqual(170f, PrepareRunStage.WrapYaw(-190f), Eps);
            Assert.AreEqual(180f, Mathf.Abs(PrepareRunStage.WrapYaw(180f)), Eps);
        }

        [Test]
        public void Yakinlasma_AdimVeTekerlek()
        {
            float d = PrepareRunStage.InitialDistance;
            Assert.AreEqual(d / PrepareRunStage.ZoomStep, PrepareRunStage.ZoomedDistance(d, 1f), Eps, "+1 adım yakınlaşır");
            Assert.AreEqual(d * PrepareRunStage.ZoomStep, PrepareRunStage.ZoomedDistance(d, -1f), Eps, "−1 adım uzaklaşır");
            Assert.AreEqual(PrepareRunStage.DistanceMin, PrepareRunStage.ZoomedDistance(d, 100f), Eps);
            Assert.AreEqual(PrepareRunStage.DistanceMax, PrepareRunStage.ZoomedDistance(d, -100f), Eps);

            Assert.AreEqual(1f, PrepareRunStage.ScrollSteps(6f), Eps, "Input System UI modülü: Windows tekerlek çentiği 6 satır = 1 adım");
            Assert.AreEqual(-1f, PrepareRunStage.ScrollSteps(-6f), Eps);
            Assert.AreEqual(PrepareRunStage.MaxScrollSteps, PrepareRunStage.ScrollSteps(600f), Eps, "tek olay en çok 2 adım");
            Assert.Greater(PrepareRunStage.ScrollSteps(1f), 0f, "eski modülün 1 satırı da yakınlaştırır");
        }

        // ------------------------------------------------------------------ the weapon

        [Test]
        public void Sigdirma_TekUniformOlcek_SinifUzunlugu_YukseklikTavani()
        {
            float rifle = PrepareRunStage.FitScale(new Vector3(0.1f, 0.22f, 1.2f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(0.95f / 1.2f, rifle, 1e-4f, "namlu ekseni sınıf uzunluğuna");
            float sideways = PrepareRunStage.FitScale(new Vector3(1.2f, 0.22f, 0.1f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(rifle, sideways, 1e-4f, "yatay uzun kenar (x ya da z)");
            float tall = PrepareRunStage.FitScale(new Vector3(0.1f, 0.8f, 0.9f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(PrepareRunStage.MaxWeaponHeight / 0.8f, tall, 1e-4f, "boy tavanı bağlar");
            Assert.AreEqual(1f, PrepareRunStage.FitScale(Vector3.zero, 0.95f, PrepareRunStage.MaxWeaponHeight), "boş sınırlar: ölçek 1");
        }

        [Test]
        public void Oturtma_AyakSoketteIzDonmeEkseninde()
        {
            var bounds = new Bounds(new Vector3(0.01f, 0.05f, 0.3f), new Vector3(0.1f, 0.3f, 1.2f));
            Vector3 seat = PrepareRunStage.SeatOffset(bounds, 0.5f);
            Assert.AreEqual(-0.005f, seat.x, 1e-5f);
            Assert.AreEqual(0.05f, seat.y, 1e-5f, "alt sınır (−0.1 × 0.5) sokete oturur");
            Assert.AreEqual(-0.15f, seat.z, 1e-5f);
            Vector3 foot = bounds.min * 0.5f + seat;
            Assert.AreEqual(0f, foot.y, 1e-5f);
            Vector3 centre = bounds.center * 0.5f + seat;
            Assert.AreEqual(0f, centre.x, 1e-5f);
            Assert.AreEqual(0f, centre.z, 1e-5f);
        }

        [Test]
        public void SinifUzunluklari_SiraliVeEnYakinKareyeSigar()
        {
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.Smg), PrepareRunStage.DisplayLength(WeaponClass.Shotgun));
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.Shotgun), PrepareRunStage.DisplayLength(WeaponClass.AssaultRifle));
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.AssaultRifle), PrepareRunStage.DisplayLength(WeaponClass.Lmg));
            Assert.LessOrEqual(PrepareRunStage.DisplayLength(WeaponClass.Lmg), PrepareRunStage.DisplayLength(WeaponClass.Sniper));

            // The nearest zoom in a square view: the frame is 2·d·tan(fov/2) wide; the longest weapon keeps a margin either side.
            float width = 2f * PrepareRunStage.DistanceMin * Mathf.Tan(PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, 1f) * 0.5f * Mathf.Deg2Rad);
            foreach (WeaponClass c in Enum.GetValues(typeof(WeaponClass)))
            {
                Assert.Less(PrepareRunStage.DisplayLength(c), 0.5f * width, c + ": en yakın yakınlaşmada kare görüntünün yarısından kısa");
            }
        }

        [Test]
        public void Namlu_IlkBakistaEkranSolunda_GorusDikgen()
        {
            Vector3 eye = PrepareRunStage.OrbitTarget + PrepareRunStage.OrbitOffset(PrepareRunStage.InitialAzimuth, PrepareRunStage.InitialPitch, PrepareRunStage.InitialDistance);
            Vector3 forward = PrepareRunStage.OrbitTarget - eye;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 barrel = Quaternion.Euler(0f, PrepareRunStage.MountYaw, 0f) * Vector3.forward;
            Assert.Less(Vector3.Dot(barrel, right), -0.999f, "namlu ekranın soluna (AP-036 yan görünüşleri)");
            Assert.AreEqual(0f, Vector3.Dot(barrel, forward), 1e-3f, "namlu ilk bakışa dik: tam yan profil");
        }

        [Test]
        public void Anahtarlar_GocTablosuylaTutarli()
        {
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                string key = PrepareRunStage.KeyOf(id);
                Assert.IsNotNull(key, id + " anahtarı");
                Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId(key, out ClassicWeaponId back), key);
                Assert.AreEqual(id, back, key + " göç tablosuyla aynı kimliğe döner");
                Assert.IsTrue(PrepareRunStage.TryResolve(key, out ClassicWeaponId resolved));
                Assert.AreEqual(id, resolved);
            }

            Assert.IsTrue(PrepareRunStage.TryResolve("AssaultRifle", out ClassicWeaponId legacy), "M3 adı da çözülür");
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, legacy);
            Assert.IsFalse(PrepareRunStage.TryResolve("sw_arc", out _), "Special kaideye çıkmaz");
            Assert.IsFalse(PrepareRunStage.TryResolve("nope", out _));
            Assert.IsFalse(PrepareRunStage.TryResolve(null, out _));
        }

        // ------------------------------------------------------------------ the light

        [Test]
        public void Isiklar_JsonDonusumuyleAyni_Siddet()
        {
            // preview/AP040_PrepareRun_v001.json lights[]: position_blender_m / aim_blender_m / energy_w / temperature_K.
            Vector3[] blenderPositions = { new Vector3(-1.05f, 0.205f, 1.47f), new Vector3(1.05f, 0.205f, 1.77f), new Vector3(0f, 0.85f, 2.65f) };
            Vector3[] blenderAims = { new Vector3(0f, 0f, 0.94f), new Vector3(0f, 0f, 1.05f), new Vector3(0f, 0f, 0.85f) };
            float[] watts = { 105f, 62f, 80f };
            float[] kelvin = { 2800f, 6500f, 3400f };
            Assert.AreEqual(3, PrepareRunStage.Lights.Length);
            for (int i = 0; i < 3; i++)
            {
                PrepareRunStage.StageLight l = PrepareRunStage.Lights[i];
                Assert.Less((PrepareRunStage.BlenderToUnity(blenderPositions[i]) - l.Position).magnitude, 1e-4f, l.Name + " konumu [x, z, −y]");
                Assert.Less((PrepareRunStage.BlenderToUnity(blenderAims[i]) - l.Aim).magnitude, 1e-4f, l.Name + " hedefi");
                Assert.AreEqual(watts[i], l.Watts, 1e-4f);
                Assert.AreEqual(kelvin[i], l.Kelvin, 1e-4f);
                Assert.That(PrepareRunStage.SpotAngle((l.Aim - l.Position).magnitude), NUnit.Framework.Is.InRange(30f, 120f));
            }

            Assert.AreEqual(105f / (Mathf.PI * Mathf.PI), PrepareRunStage.SpotIntensity(105f), 1e-4f, "P / π² (alan ışığı → URP spot)");
            Assert.IsTrue(PrepareRunStage.Lights[0].Shadows, "yalnız anahtar ışık gölge düşürür");
            Assert.IsFalse(PrepareRunStage.Lights[1].Shadows);
            Assert.IsFalse(PrepareRunStage.Lights[2].Shadows);
        }

        // ------------------------------------------------------------------ the pad's view controls (UiInput)

        [Test]
        public void UiInput_GorunumEksenleri_CoreInputUzerinden_YalnizPadde()
        {
            IInputSource was = InputSourceRegistry.Current;
            var source = new StubViewSource { Enabled = true, Scheme = InputSchemeKind.Gamepad, Look = new Vector2(0.5f, -0.25f) };
            try
            {
                UiInput.ClearSimulatedViewInput();
                InputSourceRegistry.SetCurrent(source);
                Assert.AreEqual(new Vector2(0.5f, -0.25f), UiInput.ViewStick(), "sağ çubuk = Core/Input look stick");
                Assert.AreEqual(0f, UiInput.ViewZoomAxis());

                source.Fire = true;
                Assert.AreEqual(1f, UiInput.ViewZoomAxis(), "RT (Fire) yakınlaştırır");
                source.Focus = true;
                Assert.AreEqual(0f, UiInput.ViewZoomAxis(), "iki tetik birden: 0");
                source.Fire = false;
                Assert.AreEqual(-1f, UiInput.ViewZoomAxis(), "LT (Focus) uzaklaştırır");

                source.Scheme = InputSchemeKind.KeyboardMouse;
                Assert.AreEqual(0f, UiInput.ViewZoomAxis(), "farede aynı aksiyonlar sürükleme düğmeleri: yakınlaşma yok");

                source.Enabled = false;
                Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "askıdaki kaynak (yeniden bağlama) okunmaz");

                InputSourceRegistry.Clear();
                Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "gerçek kaynak yok: sıfır");
                Assert.AreEqual(0f, UiInput.ViewZoomAxis());
            }
            finally
            {
                InputSourceRegistry.SetCurrent(was);
            }
        }

        [Test]
        public void UiInput_GorunumTohumu_TestGirdisiVerir_Temizlenince_Cihaz()
        {
            try
            {
                UiInput.SimulateViewInput(new Vector2(2f, 0f), 3f);
                Assert.AreEqual(new Vector2(1f, 0f), UiInput.ViewStick(), "çubuk büyüklüğü ≤ 1");
                Assert.AreEqual(1f, UiInput.ViewZoomAxis(), "tetik ekseni −1..1");
            }
            finally
            {
                UiInput.ClearSimulatedViewInput();
            }

            Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "EditMode'da gerçek kaynak yok");
        }

        // ------------------------------------------------------------------ the imported set

        [Test]
        public void Sahne_IceAktarildiysa_SoketlerTemizlikKatman()
        {
            var prefab = Resources.Load<GameObject>(PrepareRunStage.StageResource);
            if (prefab == null)
            {
                Assert.Ignore("AP-040 Prepare Run seti içe aktarılmadı (PrepareRunStageBuilder.BuildCli -ddAp040Export …); sahne çıplak pivotla çalışır.");
            }

            Transform turntable = Find(prefab.transform, PrepareRunStage.TurntableSocket);
            Transform socket = Find(prefab.transform, PrepareRunStage.WeaponSocket);
            Assert.IsNotNull(turntable, "SOCKET_Turntable");
            Assert.IsNotNull(socket, "SOCKET_Weapon");
            Assert.IsTrue(socket.IsChildOf(turntable), "silah soketi rotorla döner");
            Assert.AreEqual(0.94f, prefab.transform.InverseTransformPoint(socket.position).y, 0.005f, "SOCKET_Weapon 0.94 m");
            Assert.AreEqual(0.70f, prefab.transform.InverseTransformPoint(turntable.position).y, 0.005f, "SOCKET_Turntable 0.70 m");

            Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true), "yakın plan: LODGroup yok");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "çarpışma yok");
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(PrepareRunStage.StageLayer, t.gameObject.layer, t.name + " sahne katmanında");
                Assert.IsFalse(t.name.EndsWith("_COL", StringComparison.Ordinal), t.name + " vekil gövde kaldırılmalı");
                Assert.IsFalse(t.name.EndsWith("_LOD1", StringComparison.Ordinal) || t.name.EndsWith("_LOD2", StringComparison.Ordinal), t.name + " LOD1/2 kaldırılmalı");
            }

            int renderers = 0;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                foreach (Material m in r.sharedMaterials)
                {
                    Assert.IsNotNull(m, r.name + " malzemesi");
                    StringAssert.StartsWith("M_AP040_PR_", m.name, r.name + " sahnenin URP Lit malzemesi");
                    Assert.IsNotNull(m.GetTexture("_BaseMap"), m.name + " BaseColor atlası");
                }
            }

            Assert.GreaterOrEqual(renderers, 5, "LOD0: kaide tabanı + tablası, panel, iki fener");
        }

        // ------------------------------------------------------------------ T6 (hand-made motion) count

        [Test]
        public void SahneDosyasi_ElYapimiHareketSayaci_T6Desenleri()
        {
            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Prepare", "PrepareRunStage.cs");
            Assert.IsTrue(File.Exists(path), path);
            string text = File.ReadAllText(path);
            string[] patterns = { @"Mathf\.Lerp|Color\.Lerp", @"unscaledDeltaTime", @"MoveTowards", @"new Color\(", @"void (Late)?Update\(\)" };
            int[] limit = { 0, 0, 0, 0, 1 };   // one LateUpdate: the pad, the auto turn on UiMotion's clock, the draw decision
            for (int i = 0; i < patterns.Length; i++)
            {
                Assert.LessOrEqual(Regex.Matches(text, patterns[i]).Count, limit[i], "PrepareRunStage.cs '" + patterns[i] + "'");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = Find(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>A device source with the look stick, the scheme and the two triggers' actions (Fire, Focus).</summary>
        private sealed class StubViewSource : IExtendedInputSource, IFocusInputSource, ILookInputSource
        {
            public bool Enabled;
            public InputSchemeKind Scheme;
            public Vector2 Look;
            public bool Fire;
            public bool Focus;

            public bool IsEnabled => Enabled;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Stick;
            public Vector2 AimScreenPosition => Vector2.zero;
            public Vector2 AimStick => Look;
            public bool FireHeld => Fire;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
            public bool SpecialHeld => false;
            public bool SpecialPressed => false;
            public bool InteractPressed => false;
            public InputSchemeKind ActiveScheme => Scheme;
            public float StickDeadzone => 0.15f;
            public bool FocusHeld => Focus;
            public Vector2 LookDelta => Vector2.zero;
            public Vector2 LookStick => Look;
            public bool ShoulderSwapPressed => false;

#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67

            public void Enable()
            {
                Enabled = true;
            }

            public void Disable()
            {
                Enabled = false;
            }
        }
    }
}
