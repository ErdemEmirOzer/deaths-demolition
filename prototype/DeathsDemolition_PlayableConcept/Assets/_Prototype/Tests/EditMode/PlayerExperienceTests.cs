using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-059 · player experience v1, the pure parts: the game-feel asset carries the
    /// player's bump / death shake / death beat inside the WP-019 ceilings and the results
    /// delay always outlasts the slow-down; the death beat owns the clock like a hit-stop
    /// (refused when stopped, cancelled by a freeze, restores 1 not 0.3); the aim-assist
    /// level is WP-024 data with a version-1 migration and legacy-field edits honoured;
    /// a rebound Pause control reaches the prompt glyph through the remap service.
    /// </summary>
    [TestFixture]
    [Category("WP059")]
    public sealed class PlayerExperienceTests
    {
        private float _timeScaleWas;

        [SetUp]
        public void Kur()
        {
            _timeScaleWas = Time.timeScale;
            Time.timeScale = 1f;
            DeathSequence.Cancel();
            DeathSequence.ResetCounters();
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
        }

        [TearDown]
        public void Bitir()
        {
            DeathSequence.Cancel();
            RunFlowClock.ClearHolds();
            InputRemapRegistry.SetCurrent(null);
            GameFeelSettings.Override(null);
            FeelRuntime.ResetForTests();
            Time.timeScale = _timeScaleWas;
        }

        // ------------------------------------------------------------------ game feel: one source, inside the ceilings

        [Test]
        public void GameFeel_OyuncuDarbeVeOlum_TavanIcinde_TekKaynak()
        {
            GameFeelSettings feel = Resources.Load<GameFeelSettings>(GameFeelSettings.ResourceName);
            Assert.IsNotNull(feel, "DD_GameFeel asset'i Resources'ta yok.");
            Assert.Greater(feel.ShakePlayerDamageAmplitude, 0f, "Oyuncu darbe sarsıntısı asset'te 0 (REQ-045 bump tek kaynak burası).");
            Assert.LessOrEqual(feel.ShakePlayerDamageAmplitude, GameFeelSettings.MaxShakeAmplitude);
            Assert.LessOrEqual(feel.ShakePlayerDamageSeconds, GameFeelSettings.MaxShakeSeconds);
            Assert.Greater(feel.ShakePlayerDeathAmplitude, feel.ShakePlayerDamageAmplitude, "Ölüm sarsıntısı darbeden büyük.");
            Assert.LessOrEqual(feel.ShakePlayerDeathAmplitude, GameFeelSettings.MaxShakeAmplitude, "ADR-019-2: ölüm sarsıntısı 0,15 m tavanı");
            Assert.LessOrEqual(feel.ShakePlayerDeathSeconds, GameFeelSettings.MaxShakeSeconds, "ADR-019-2: ölüm sarsıntısı 0,2 s tavanı");

            Assert.That(feel.DeathSlowScale, Is.InRange(GameFeelSettings.MinDeathSlowScale, 0.6f), "ölüm yavaşlaması okunur bant");
            // The fall itself (death clips 1.17–1.57 s, the body lands past ~0.7 s of clip) must sit inside the slow part.
            float clipAtBeatEnd = feel.DeathSlowInSeconds * (1f + feel.DeathSlowScale) * 0.5f + feel.DeathSlowHoldSeconds * feel.DeathSlowScale + feel.DeathSlowOutSeconds * (1f + feel.DeathSlowScale) * 0.5f;
            Assert.GreaterOrEqual(clipAtBeatEnd, 0.9f, "yavaşlama düşüşü kapsamıyor (klip " + clipAtBeatEnd.ToString("F2") + " s)");
            Assert.Greater(feel.DeathSlowHoldSeconds, 0.5f, "çökme klibi için tutma");
            Assert.LessOrEqual(feel.DeathSlowTotalSeconds, GameFeelSettings.MaxDeathSlowSeconds + 1.5f);
            Assert.Less(feel.DeathSlowTotalSeconds, feel.DeathResultsDelaySeconds, "Results teslimi yavaşlama bittikten sonra");
            Assert.LessOrEqual(feel.DeathResultsDelaySeconds, GameFeelSettings.MaxDeathResultsDelaySeconds);
            Assert.That(feel.DeathCameraPushIn, Is.InRange(0.05f, GameFeelSettings.MaxDeathPushIn));

            // WP-062: the compatibility facade preserves the old layer-off behavior, with
            // FeelConfig as the single authority for that switch.
            FeelConfigData off = FeelConfig.CreateDefaults();
            off.enabled = false;
            Assert.IsTrue(FeelRuntime.QueueAuthored(off, "legacy-off", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(59062));
            Assert.IsFalse(feel.Enabled);
            Assert.AreEqual(1f, feel.DeathSlowScale, 1e-5f);
            Assert.AreEqual(0f, feel.DeathCameraPushIn, 1e-5f);
            Assert.AreEqual(0f, feel.ShakePlayerDeathAmplitude, 1e-5f);
            Assert.GreaterOrEqual(feel.DeathResultsDelaySeconds, .5f);
        }

        // ------------------------------------------------------------------ death beat on the clock

        [Test]
        public void OlumSekansi_SaatiHitStopGibiTutar_DondurmaIptalEder()
        {
            Assert.IsTrue(DeathSequence.Begin(0.3f, 0.2f, 1.0f, 0.4f), "Sekans başlamadı.");
            Assert.IsTrue(DeathSequence.IsActive);
            Assert.AreEqual(1, DeathSequence.Count);
            Assert.IsFalse(DeathSequence.Begin(0.3f, 0.2f, 1.0f, 0.4f), "İkinci Begin reddedilmeli.");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "t=0: rampa başında 1.");

            DeathSequence.Tick(0.1f);
            Assert.That(Time.timeScale, Is.InRange(0.3f, 1f), "rampa içinde");
            DeathSequence.Tick(0.1f);
            Assert.AreEqual(0.3f, Time.timeScale, 1e-3f, "rampa sonu → tutma ölçeği");
            Assert.AreEqual(1f, DeathSequence.PushIn01, 1e-3f, "push-in rampayla 1'e varır");
            DeathSequence.Tick(1.0f);
            Assert.AreEqual(0.3f, Time.timeScale, 1e-3f, "tutma");
            DeathSequence.Tick(0.2f);
            Assert.That(Time.timeScale, Is.InRange(0.3f, 1f), "çıkış rampası");
            DeathSequence.Tick(0.3f);
            Assert.IsFalse(DeathSequence.IsActive, "toplam süre sonunda biter");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "saat geri verildi");
            Assert.AreEqual(1f, DeathSequence.PushIn01, 1e-4f, "push-in kendi bitişinde tutulur (Results'a kadar çerçeve sabit)");
            DeathSequence.ReleasePushIn();
            Assert.AreEqual(0f, DeathSequence.PushIn01, 1e-4f);

            // A freeze (pause / results hold) during the beat: the hold sees 1 as the scale to restore, never 0.3.
            Assert.IsTrue(DeathSequence.Begin(0.3f, 0f, 1.0f, 0f));
            Assert.AreEqual(0.3f, Time.timeScale, 1e-3f);
            RunFlowClock.AddHold(RunFlowClock.DebugHold);
            Assert.IsFalse(DeathSequence.IsActive, "Dondurma sekansı iptal etmeli.");
            Assert.AreEqual(0f, Time.timeScale, 1e-4f);
            RunFlowClock.RemoveHold(RunFlowClock.DebugHold);
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "Dondurma kalkınca 0,3 değil 1.");

            // Refused while the clock is stopped; a foreign write ends the beat without touching the clock.
            Time.timeScale = 0f;
            Assert.IsFalse(DeathSequence.Begin(0.3f, 0f, 1f, 0f), "Durmuş saatte sekans başlamamalı.");
            Time.timeScale = 1f;
            Assert.IsTrue(DeathSequence.Begin(0.3f, 0f, 1f, 0f));
            Time.timeScale = 0.5f;   // somebody else took it
            DeathSequence.Tick(0.1f);
            Assert.IsFalse(DeathSequence.IsActive);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-4f, "Yabancı yazım korunur.");
            Time.timeScale = 1f;

            // Cancel restores and drops the push-in.
            Assert.IsTrue(DeathSequence.Begin(0.25f, 0f, 1f, 0f));
            DeathSequence.Cancel();
            Assert.AreEqual(1f, Time.timeScale, 1e-4f);
            Assert.IsFalse(DeathSequence.IsActive);
            Assert.AreEqual(0f, DeathSequence.PushIn01, 1e-4f);
        }

        // ------------------------------------------------------------------ aim assist level = WP-024 data

        [Test]
        public void NisanYardimi_Seviye_VeriKaynagi_V1Gocu_AlanDuzenlemesi()
        {
            var p = new InputPreferences();
            Assert.AreEqual(InputAimAssistLevel.Full, p.AimAssist, "Varsayılan Full.");
            Assert.AreEqual(InputPreferences.CurrentVersion, p.Version);

            p.AimAssist = InputAimAssistLevel.Light;
            Assert.IsTrue(p.AimAssistEnabled);
            Assert.AreEqual(InputPreferences.MaxAimAssistAngleDegrees * AimAssistLevels.LightAngleShare, p.AimAssistAngleDegrees, 1e-5f, "Light = yarım koni");
            Assert.AreEqual(InputPreferences.MaxAimAssistDistance, p.AimAssistDistance, 1e-5f, "Light = tam mesafe");
            p.AimAssist = InputAimAssistLevel.Off;
            Assert.IsFalse(p.AimAssistEnabled);
            p.AimAssist = InputAimAssistLevel.Full;
            Assert.IsTrue(p.AimAssistEnabled);
            Assert.AreEqual(InputPreferences.MaxAimAssistAngleDegrees, p.AimAssistAngleDegrees, 1e-5f);

            // Round trip keeps the level; the level rides in the JSON.
            InputPreferences back = InputPreferences.FromJson(p.ToJson());
            Assert.AreEqual(InputAimAssistLevel.Full, back.AimAssist);
            StringAssert.Contains("AimAssistLevel", p.ToJson());

            // Version-1 file (WP-024 / WP-022G wrote the fields only): Off / Light / Full read back from the fields.
            InputPreferences v1Off = InputPreferences.FromJson("{\"Version\":1,\"StickDeadzone\":0.15,\"RumbleEnabled\":true,\"RumbleIntensity\":0.3,\"AimAssistEnabled\":false,\"AimAssistAngleDegrees\":6.0,\"AimAssistDistance\":15.0}");
            Assert.AreEqual(InputAimAssistLevel.Off, v1Off.AimAssist, "v1 kapalı → Off");
            Assert.AreEqual(InputPreferences.CurrentVersion, v1Off.Version, "göç sürümü yükseltir");
            InputPreferences v1Light = InputPreferences.FromJson("{\"Version\":1,\"AimAssistEnabled\":true,\"AimAssistAngleDegrees\":3.0,\"AimAssistDistance\":15.0}");
            Assert.AreEqual(InputAimAssistLevel.Light, v1Light.AimAssist, "v1 3° → Light");
            InputPreferences v1Full = InputPreferences.FromJson("{\"Version\":1,\"AimAssistEnabled\":true,\"AimAssistAngleDegrees\":6.0,\"AimAssistDistance\":15.0}");
            Assert.AreEqual(InputAimAssistLevel.Full, v1Full.AimAssist, "v1 6° → Full");

            // A direct legacy-field edit (tests, imports) still means what it says: the level follows, the fields snap.
            var edited = new InputPreferences();
            edited.AimAssistEnabled = false;
            edited.Clamp();
            Assert.AreEqual(InputAimAssistLevel.Off, edited.AimAssist, "Enabled=false → Off");
            Assert.IsFalse(edited.AimAssistEnabled);
            var custom = new InputPreferences { AimAssistAngleDegrees = 4f };
            custom.Clamp();
            Assert.AreEqual(InputAimAssistLevel.Light, custom.AimAssist, "4° → Light");
            Assert.AreEqual(3f, custom.AimAssistAngleDegrees, 1e-5f, "Light sayısına oturur");

            // Preferences can only narrow the assist: every level sits inside the ADR-024-1 ceilings.
            foreach (InputAimAssistLevel level in Enum.GetValues(typeof(InputAimAssistLevel)))
            {
                Assert.LessOrEqual(AimAssistLevels.AngleDegreesOf(level), InputPreferences.MaxAimAssistAngleDegrees);
                Assert.LessOrEqual(AimAssistLevels.DistanceOf(level), InputPreferences.MaxAimAssistDistance);
                Assert.IsFalse(string.IsNullOrEmpty(AimAssistLevels.Label(level)));
            }

            Assert.IsTrue(InputSettingsAsset.LoadDefaults().AimAssist == InputAimAssistLevel.Full, "Asset varsayılanı Full.");
        }

        // ------------------------------------------------------------------ rebound key → prompt glyph

        private sealed class SahteRemap : IInputRemapService
        {
            public readonly List<InputRemapEntry> Liste = new List<InputRemapEntry>();
            public IReadOnlyList<InputRemapEntry> Entries => Liste;
            public bool IsRebinding => false;
            public string OverridesFilePath => "";
            public event Action BindingsChanged;
            public void Raise() { BindingsChanged?.Invoke(); }
            public bool StartRebind(string entryId, Action<InputRebindResult, InputRemapEntry> onFinished) => false;
            public void CancelRebind() { }
            public bool ApplyOverride(string entryId, string controlPath) => false;
            public void ResetBinding(string entryId) { }
            public void ResetToDefaults() { }
            public void Save() { }
            public void Load() { }
            public string ExportOverridesJson() => "{}";
            public void ImportOverridesJson(string json) { }
            public InputRemapEntry Find(string entryId) => default;
        }

        private static InputRemapEntry Giris(string action, InputSchemeKind scheme, string display, bool overridden, string part = "")
        {
            return new InputRemapEntry(Guid.NewGuid().ToString(), PrototypeInputNames.MapGameplay, action, scheme, part,
                "<default>", overridden ? "<override>" : "<default>", display, overridden);
        }

        [Test]
        public void YenidenBaglananTus_IstemGlifineYansir()
        {
            // No service → the WP-024 defaults.
            InputRemapRegistry.SetCurrent(null);
            Assert.AreEqual("ESC", UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.KeyboardMouse).Text);
            Assert.AreEqual("MENU", UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.Gamepad).Text);
            Assert.IsNull(UiInputGlyphs.GameplayActionOf(UiPromptAction.Submit), "Submit sabit UI tuşu");
            Assert.AreEqual(PrototypeInputNames.ActionPause, UiInputGlyphs.GameplayActionOf(UiPromptAction.Pause));

            var remap = new SahteRemap();
            remap.Liste.Add(Giris(PrototypeInputNames.ActionPause, InputSchemeKind.KeyboardMouse, "Escape", false));
            remap.Liste.Add(Giris(PrototypeInputNames.ActionPause, InputSchemeKind.Gamepad, "Start", false));
            remap.Liste.Add(Giris(PrototypeInputNames.ActionFire, InputSchemeKind.KeyboardMouse, "P", true));   // another action rebound to P: not ours
            InputRemapRegistry.SetCurrent(remap);
            Assert.AreEqual("ESC", UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.KeyboardMouse).Text, "Geçersiz kılınmamış → varsayılan");
            Assert.AreEqual("MENU", UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.Gamepad).Text);

            // Rebound: keyboard Pause → P, gamepad Pause → Button West (X, a disc).
            remap.Liste[0] = Giris(PrototypeInputNames.ActionPause, InputSchemeKind.KeyboardMouse, "P", true);
            remap.Liste[1] = Giris(PrototypeInputNames.ActionPause, InputSchemeKind.Gamepad, "Button West", true);
            UiInputGlyphs.Glyph key = UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.KeyboardMouse);
            UiInputGlyphs.Glyph pad = UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.Gamepad);
            Assert.AreEqual("P", key.Text);
            Assert.IsFalse(key.Round);
            Assert.GreaterOrEqual(key.Width, 1.2f);
            Assert.AreEqual("X", pad.Text);
            Assert.IsTrue(pad.Round, "Yüz düğmesi disk");
            Assert.AreEqual("ENTER", UiInputGlyphs.For(UiPromptAction.Submit, InputSchemeKind.KeyboardMouse).Text, "Submit etkilenmez");

            // Names → short labels.
            Assert.AreEqual("SPACE", UiInputGlyphs.FromControlName("Space", InputSchemeKind.KeyboardMouse).Text);
            Assert.AreEqual("SHIFT", UiInputGlyphs.FromControlName("Left Shift", InputSchemeKind.KeyboardMouse).Text);
            Assert.AreEqual("LMB", UiInputGlyphs.FromControlName("Left Button", InputSchemeKind.KeyboardMouse).Text);
            Assert.AreEqual("RB", UiInputGlyphs.FromControlName("Right Shoulder", InputSchemeKind.Gamepad).Text);
            Assert.AreEqual("D-UP", UiInputGlyphs.FromControlName("D-Pad Up", InputSchemeKind.Gamepad).Text);
            Assert.AreEqual("VIEW", UiInputGlyphs.FromControlName("Select", InputSchemeKind.Gamepad).Text);
            Assert.LessOrEqual(UiInputGlyphs.FromControlName("Numpad Multiply", InputSchemeKind.KeyboardMouse).Text.Length, 8, "uzun ad kırpılır");
            Assert.Greater(UiInputGlyphs.FromControlName("D-Down", InputSchemeKind.Gamepad).Width, UiInputGlyphs.FromControlName("P", InputSchemeKind.KeyboardMouse).Width, "genişlik metinle büyür");

            // Reset → default again.
            remap.Liste[0] = Giris(PrototypeInputNames.ActionPause, InputSchemeKind.KeyboardMouse, "Escape", false);
            Assert.AreEqual("ESC", UiInputGlyphs.For(UiPromptAction.Pause, InputSchemeKind.KeyboardMouse).Text);
        }
    }
}
