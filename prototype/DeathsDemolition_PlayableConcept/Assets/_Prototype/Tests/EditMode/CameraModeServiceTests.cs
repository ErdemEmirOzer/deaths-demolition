using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.InputAbstraction;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-070: CameraMode API contract (mode state, events, settings sanitising, command line, look input fallback).</summary>
    public sealed class CameraModeServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            CameraModeService.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CameraModeService.ResetForTests();
        }

        [Test]
        public void Varsayilan_TepedenVeGecisYok()
        {
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            Assert.IsFalse(CameraModeService.IsTransitioning);
            Assert.IsFalse(CameraModeService.IsLookMode);
            Assert.AreEqual(1f, CameraModeService.Transition01);
        }

        [Test]
        public void Istek_OlayBirKezVeCurrentOnceGuncellenir()
        {
            var seen = new List<CameraModeChange>();
            CameraMode currentInsideHandler = CameraMode.TopDown;
            CameraModeService.Changed += c => { seen.Add(c); currentInsideHandler = CameraModeService.Current; };

            Assert.IsTrue(CameraModeService.Request(CameraMode.FirstPerson));
            Assert.IsFalse(CameraModeService.Request(CameraMode.FirstPerson), "Aynı moda istek no-op olmalı.");

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(CameraMode.TopDown, seen[0].From);
            Assert.AreEqual(CameraMode.FirstPerson, seen[0].To);
            Assert.IsFalse(seen[0].Instant);
            Assert.AreEqual(CameraMode.FirstPerson, currentInsideHandler, "Olay anında Current yeni mod olmalı.");
            Assert.IsTrue(CameraModeService.IsTransitioning);
            Assert.IsTrue(CameraModeService.IsLookMode);
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Previous);
        }

        [Test]
        public void Gecis_IlerleyinceBiterVePreviousEsitlenir()
        {
            CameraModeService.Request(CameraMode.ThirdPerson);
            CameraModeService.AdvanceTransition(0.4f);
            Assert.AreEqual(0.4f, CameraModeService.Transition01, 1e-5f);
            CameraModeService.AdvanceTransition(0.7f);
            Assert.AreEqual(1f, CameraModeService.Transition01);
            Assert.IsFalse(CameraModeService.IsTransitioning);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Previous);
        }

        [Test]
        public void AniIstek_GecisSiz()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Assert.IsFalse(CameraModeService.IsTransitioning);
        }

        [Test]
        public void Ayarlar_TemizlenirVeModUygulanir()
        {
            int settingsEvents = 0;
            CameraModeService.SettingsChanged += () => settingsEvents++;
            var s = new CameraModeSettings
            {
                Mode = CameraMode.ThirdPerson,
                LookSensitivity = 50f,
                StickLookDegreesPerSecond = float.NaN,
                FirstPersonFov = 120f,
                ThirdPersonFov = 10f,
                HeadBobScale = -1f,
                Shoulder = (CameraShoulder)7,
            };

            CameraModeService.ApplySettings(s, applyMode: true, instant: true);

            CameraModeSettings live = CameraModeService.Settings;
            Assert.AreEqual(1, settingsEvents);
            Assert.AreEqual(CameraModeSettings.MaxLookSensitivity, live.LookSensitivity);
            Assert.AreEqual(180f, live.StickLookDegreesPerSecond);
            Assert.AreEqual(CameraModeSettings.MaxFirstPersonFov, live.FirstPersonFov);
            Assert.AreEqual(CameraModeSettings.MinThirdPersonFov, live.ThirdPersonFov);
            Assert.AreEqual(0f, live.HeadBobScale);
            Assert.AreEqual(CameraShoulder.Right, live.Shoulder);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Current);
            Assert.AreNotSame(s, live, "Servis çağıranın nesnesini değil kopyasını tutmalı.");
            Assert.AreEqual(50f, s.LookSensitivity, "Çağıranın nesnesi değiştirilmemeli.");
        }

        [Test]
        public void OlumKilidi_IstekKuyruklanirVeSonKilitteUygulanir()
        {
            int events = 0;
            CameraModeService.Changed += c => events++;
            CameraModeService.LockModeChanges("death");
            CameraModeService.LockModeChanges("other");

            Assert.IsFalse(CameraModeService.Request(CameraMode.ThirdPerson));
            Assert.IsFalse(CameraModeService.Request(CameraMode.FirstPerson));
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            Assert.AreEqual(CameraMode.FirstPerson, CameraModeService.Target, "Son istek kuyrukta olmalı.");
            Assert.AreEqual(0, events);

            CameraModeService.UnlockModeChanges("death");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "Kilit kalmışken uygulanmamalı.");
            CameraModeService.UnlockModeChanges("other");
            Assert.AreEqual(CameraMode.FirstPerson, CameraModeService.Current);
            Assert.AreEqual(1, events);
            Assert.IsTrue(CameraModeService.IsTransitioning, "Kuyruktan uygulama yumuşak geçiş olmalı.");
        }

        [Test]
        public void ImlecNiyeti_YalnizOyunIciBakisModundaKilitli()
        {
            Assert.AreEqual(CameraCursorIntent.Free, CameraModeService.CursorIntent);
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Assert.AreEqual(CameraCursorIntent.Locked, CameraModeService.CursorIntent);
            CameraModeService.SetFrozen(true);
            Assert.AreEqual(CameraCursorIntent.Free, CameraModeService.CursorIntent, "Pause'da imleç serbest.");
            CameraModeService.SetFrozen(false);
            CameraModeService.AddLookBlock("shop");
            Assert.AreEqual(CameraCursorIntent.Free, CameraModeService.CursorIntent, "Bakış engelinde imleç serbest.");
            CameraModeService.RemoveLookBlock("shop");
            Assert.AreEqual(CameraCursorIntent.Locked, CameraModeService.CursorIntent);
        }

        [Test]
        public void GecisSureleri_ModCiftineGoreVeAzaltilmisHareketteAni()
        {
            Assert.AreEqual(0.45f, CameraModeService.TransitionSeconds(CameraMode.TopDown, CameraMode.ThirdPerson), 1e-6f);
            Assert.AreEqual(0.30f, CameraModeService.TransitionSeconds(CameraMode.FirstPerson, CameraMode.ThirdPerson), 1e-6f);
            Assert.AreEqual(0.55f, CameraModeService.TransitionSeconds(CameraMode.FirstPerson, CameraMode.TopDown), 1e-6f);

            CameraModeService.ApplySettings(new CameraModeSettings { ReducedMotion = true }, applyMode: false);
            Assert.AreEqual(0f, CameraModeService.TransitionSeconds(CameraMode.TopDown, CameraMode.FirstPerson));
            CameraModeService.Request(CameraMode.FirstPerson);
            Assert.IsFalse(CameraModeService.IsTransitioning, "Azaltılmış hareket: geçiş anlık.");
        }

        [Test]
        public void Profiller_VarsayilanVeDegistirilebilir()
        {
            Assert.AreEqual(CameraOccluderPolicy.FadeAll, CameraModeService.Profile(CameraMode.TopDown).OccluderPolicy);
            Assert.AreEqual(CameraOccluderPolicy.None, CameraModeService.Profile(CameraMode.FirstPerson).OccluderPolicy);
            Assert.GreaterOrEqual(CameraModeService.Profile(CameraMode.FirstPerson).FarClip, 450f, "Ufuk silindirleri (r 420) görünmeli.");
            int changed = 0;
            CameraModeService.ProfilesChanged += () => changed++;
            CameraModeProfile p = CameraModeService.Profile(CameraMode.ThirdPerson);
            p.ShadowDistance = 45f;
            CameraModeService.SetProfile(CameraMode.ThirdPerson, p);
            Assert.AreEqual(45f, CameraModeService.Profile(CameraMode.ThirdPerson).ShadowDistance);
            Assert.AreEqual(1, changed);
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Assert.AreEqual(45f, CameraModeService.CurrentProfile.ShadowDistance);
        }

        [Test]
        public void GirdiTercihleri_V3BakisAlanlari_GocVeKopru()
        {
            InputPreferences v2 = InputPreferences.FromJson("{\"Version\":2,\"StickDeadzone\":0.2,\"AimAssistLevel\":2}");
            Assert.AreEqual(InputPreferences.CurrentVersion, v2.Version);
            Assert.AreEqual(3, InputPreferences.CurrentVersion);
            Assert.AreEqual(0.08f, v2.LookSensitivity, 1e-6f, "v2 dosyası bakış varsayılanını alır.");
            Assert.IsFalse(v2.LookInvertY);
            Assert.AreEqual(180f, v2.StickLookDegreesPerSecond, 1e-4f);
            Assert.AreEqual(0.2f, v2.StickDeadzone, 1e-6f, "Mevcut alanlar korunur.");

            var p = new InputPreferences { LookSensitivity = 5f, StickLookDegreesPerSecond = 10f, LookInvertY = true }.Clamp();
            Assert.AreEqual(InputPreferences.MaxLookSensitivity, p.LookSensitivity);
            Assert.AreEqual(InputPreferences.MinStickLookDegreesPerSecond, p.StickLookDegreesPerSecond);

            var s = new CameraModeSettings();
            CameraInputPreferencesBridge.CopyLook(p, s);
            Assert.AreEqual(p.LookSensitivity, s.LookSensitivity);
            Assert.IsTrue(s.InvertY);
            Assert.AreEqual(p.StickLookDegreesPerSecond, s.StickLookDegreesPerSecond);
            Assert.AreEqual(CameraModeSettings.MaxLookSensitivity, InputPreferences.MaxLookSensitivity, "Aralıklar iki tarafta aynı.");
            Assert.AreEqual(CameraModeSettings.MaxStickLookDegreesPerSecond, InputPreferences.MaxStickLookDegreesPerSecond);

            var copy = new InputPreferences();
            copy.CopyFrom(p);
            Assert.IsTrue(copy.SameAs(p), "CopyFrom/SameAs bakış alanlarını taşır.");
        }

        [Test]
        public void R1A7_AniIstekAyniModda_BekleyenGecisiBitirir()
        {
            int events = 0;
            CameraModeService.Changed += c => events++;
            CameraModeService.Request(CameraMode.ThirdPerson);
            Assert.IsTrue(CameraModeService.IsTransitioning);
            Assert.IsFalse(CameraModeService.Request(CameraMode.ThirdPerson, instant: true), "Mod değişmedi: false.");
            Assert.IsFalse(CameraModeService.IsTransitioning, "Anlık istek bekleyen geçişi bitirmeli (R1-A #7).");
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Previous);
            Assert.AreEqual(1, events, "Aynı moda anlık istek olay üretmez.");

            CameraModeService.Request(CameraMode.FirstPerson);
            CameraModeService.CompleteTransition();
            Assert.IsFalse(CameraModeService.IsTransitioning);
        }

        [Test]
        public void R1A17_EtkinProfil_GecisinIlkYarisindaOncekiMod()
        {
            CameraModeService.Request(CameraMode.FirstPerson);
            Assert.AreEqual(CameraOccluderPolicy.FadeAll, CameraModeService.EffectiveProfile.OccluderPolicy, "t<0,5: önceki (tepeden) politika.");
            CameraModeService.AdvanceTransition(0.6f);
            Assert.AreEqual(CameraOccluderPolicy.None, CameraModeService.EffectiveProfile.OccluderPolicy, "t≥0,5: yeni mod politikası.");
        }

        [Test]
        public void R1A18_ImlecNiyeti_RunFlowClockuDogrudanOkur()
        {
            float scale = Time.timeScale;
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Assert.AreEqual(CameraCursorIntent.Locked, CameraModeService.CursorIntent);
            DD.Prototype.Rounds.RunFlowClock.AddHold("wp070-test");
            try
            {
                Assert.AreEqual(CameraCursorIntent.Free, CameraModeService.CursorIntent, "Saat donukken (rig olmadan da) imleç serbest.");
            }
            finally
            {
                DD.Prototype.Rounds.RunFlowClock.RemoveHold("wp070-test");
                Time.timeScale = scale;
            }

            Assert.AreEqual(CameraCursorIntent.Locked, CameraModeService.CursorIntent);
        }

        [Test]
        public void KomutSatiri_ModAdlariAyristirilir()
        {
            CameraMode m;
            Assert.IsTrue(CameraModeService.TryParseMode("tp", out m)); Assert.AreEqual(CameraMode.ThirdPerson, m);
            Assert.IsTrue(CameraModeService.TryParseMode("FP", out m)); Assert.AreEqual(CameraMode.FirstPerson, m);
            Assert.IsTrue(CameraModeService.TryParseMode(" topdown ", out m)); Assert.AreEqual(CameraMode.TopDown, m);
            Assert.IsFalse(CameraModeService.TryParseMode("isometric", out m));
            Assert.IsFalse(CameraModeService.TryParseMode(null, out m));
        }

        [Test]
        public void BakisGirdisi_DestekYoksaCubukAimStickDoner()
        {
            var plain = new StubSource { Stick = new Vector2(0.5f, -0.25f) };
            Assert.AreEqual(Vector2.zero, plain.ReadLookDelta());
            Assert.AreEqual(plain.Stick, plain.ReadLookStick());

            var look = new StubLookSource { Delta = new Vector2(3f, -2f), Look = new Vector2(0.1f, 0.2f) };
            Assert.AreEqual(look.Delta, look.ReadLookDelta());
            Assert.AreEqual(look.Look, look.ReadLookStick());
        }

        [Test]
        public void Tepkiler_BiriktirilirVeBosaltilir()
        {
            CameraReactions.AddRecoil(1.5f, -0.25f);
            CameraReactions.AddRecoil(0.5f, 0f);
            CameraReactions.AddKick(new Vector3(0f, 0f, -0.02f), 1f);
            CameraReactions.FovPunch(2f, 0.2f);
            CameraReactions.FovPunch(1f, 0.5f);   // weaker punch does not replace the stronger one

            Vector2 recoil; Vector3 kick; float roll, fov, fovSeconds;
            CameraReactions.Drain(out recoil, out kick, out roll, out fov, out fovSeconds);
            Assert.AreEqual(new Vector2(2f, -0.25f), recoil);
            Assert.AreEqual(-0.02f, kick.z, 1e-6f);
            Assert.AreEqual(1f, roll);
            Assert.AreEqual(2f, fov);
            Assert.AreEqual(0.2f, fovSeconds, 1e-6f);

            CameraReactions.Drain(out recoil, out kick, out roll, out fov, out fovSeconds);
            Assert.AreEqual(Vector2.zero, recoil);
            Assert.AreEqual(0f, fov);
        }

        [Test]
        public void GorusSorgusu_RigYokkenGorunurVeTepedenArkaKorDegil()
        {
            Assert.IsTrue(CameraView.IsInView(new Vector3(0f, 0f, -50f)));
            Assert.IsFalse(CameraView.IsRearBlind);
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            Assert.IsTrue(CameraView.IsRearBlind);
            Assert.IsNull(CameraView.ViewmodelAnchor);
        }

        private class StubSource : IInputSource
        {
            public Vector2 Stick;
            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Stick;
            public Vector2 AimScreenPosition => Vector2.zero;
            public Vector2 AimStick => Stick;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
            public event System.Action<AimSourceKind> AimKindChanged { add { } remove { } }
            public void Enable() { }
            public void Disable() { }
        }

        private sealed class StubLookSource : StubSource, ILookInputSource
        {
            public Vector2 Delta;
            public Vector2 Look;
            public Vector2 LookDelta => Delta;
            public Vector2 LookStick => Look;
            public bool ShoulderSwapPressed => false;
        }
    }
}
