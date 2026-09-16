using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.InputAbstraction;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-024 · action map completeness, preferences persistence, aim assist bounds
    /// and the "no Input System outside the Input folder" rule. Reads the
    /// .inputactions asset as JSON text so this assembly needs no Input System reference.
    /// </summary>
    [TestFixture]
    [Category("WP024")]
    public sealed class InputParityContractTests
    {
        private const string AssetPath = "Assets/_Prototype/Input/Resources/DD_Controls.inputactions";
        private const string PrototypeRoot = "Assets/_Prototype";

        [Serializable]
        private sealed class Binding
        {
            public string path;
            public string groups;
            public string action;
            public bool isComposite;
            public bool isPartOfComposite;
            public string processors;
        }

        [Serializable]
        private sealed class Action
        {
            public string name;
        }

        [Serializable]
        private sealed class Map
        {
            public string name;
            public List<Action> actions;
            public List<Binding> bindings;
        }

        [Serializable]
        private sealed class Scheme
        {
            public string name;
        }

        [Serializable]
        private sealed class Asset
        {
            public List<Map> maps;
            public List<Scheme> controlSchemes;
        }

        private static Asset LoadAsset()
        {
            Assert.IsTrue(File.Exists(AssetPath), "DD_Controls.inputactions yok: " + AssetPath);
            Asset a = JsonUtility.FromJson<Asset>(File.ReadAllText(AssetPath));
            Assert.IsNotNull(a);
            return a;
        }

        private static Map MapOf(Asset a, string name)
        {
            Map m = a.maps.Find(x => x.name == name);
            Assert.IsNotNull(m, "Action map yok: " + name);
            return m;
        }

        /// <summary>Every Gameplay action except the developer panel has a binding in BOTH schemes (parity).</summary>
        [Test]
        public void GameplayMap_HerAksiyon_IkiSemadaDaBagli()
        {
            Asset a = LoadAsset();
            Map gp = MapOf(a, PrototypeInputNames.MapGameplay);
            CollectionAssert.AreEquivalent(
                new[] { "KeyboardMouse", "Gamepad" }, a.controlSchemes.ConvertAll(s => s.name), "Şema kümesi.");

            string[] required =
            {
                PrototypeInputNames.ActionMove, PrototypeInputNames.ActionFire, PrototypeInputNames.ActionReload,
                PrototypeInputNames.ActionPause, PrototypeInputNames.ActionSpecial, PrototypeInputNames.ActionInteract,
                PrototypeInputNames.ActionAim, PrototypeInputNames.ActionAimStick, PrototypeInputNames.ActionDebugPanel
            };
            foreach (string name in required)
            {
                Assert.IsNotNull(gp.actions.Find(x => x.name == name), "Gameplay aksiyonu yok: " + name);
            }

            // Aim (pointer) and AimStick are the two halves of one control; DebugPanel is dev-only.
            string[] parity =
            {
                PrototypeInputNames.ActionMove, PrototypeInputNames.ActionFire, PrototypeInputNames.ActionReload,
                PrototypeInputNames.ActionPause, PrototypeInputNames.ActionSpecial, PrototypeInputNames.ActionInteract
            };
            foreach (string name in parity)
            {
                bool kb = gp.bindings.Exists(b => b.action == name && !b.isComposite && (b.groups ?? "").Contains("KeyboardMouse"));
                bool pad = gp.bindings.Exists(b => b.action == name && !b.isComposite && (b.groups ?? "").Contains("Gamepad"));
                Assert.IsTrue(kb, name + ": KeyboardMouse bağı yok.");
                Assert.IsTrue(pad, name + ": Gamepad bağı yok.");
            }

            Assert.IsTrue(gp.bindings.Exists(b => b.action == PrototypeInputNames.ActionAim && (b.groups ?? "").Contains("KeyboardMouse")), "Aim (pointer) KeyboardMouse bağı yok.");
            Assert.IsTrue(gp.bindings.Exists(b => b.action == PrototypeInputNames.ActionAimStick && b.path == "<Gamepad>/rightStick"), "AimStick sağ çubuğa bağlı değil.");
        }

        /// <summary>The deadzone is one software rule from InputPreferences: the Gameplay sticks carry no binding-level processor.</summary>
        [Test]
        public void GameplayCubuklari_BindingProcessoruTasimaz()
        {
            Map gp = MapOf(LoadAsset(), PrototypeInputNames.MapGameplay);
            foreach (Binding b in gp.bindings)
            {
                if (b.path == "<Gamepad>/leftStick" || b.path == "<Gamepad>/rightStick")
                {
                    Assert.IsTrue(string.IsNullOrEmpty(b.processors), b.action + " çubuğu hâlâ binding-processor taşıyor: " + b.processors);
                }
            }
        }

        /// <summary>UI map carries the actions the Input System UI module is wired to, with gamepad navigation.</summary>
        [Test]
        public void UiMap_InputSystemModuluIcinTam()
        {
            Map ui = MapOf(LoadAsset(), PrototypeInputNames.MapUI);
            foreach (string name in new[] { PrototypeInputNames.UiNavigate, PrototypeInputNames.UiSubmit, PrototypeInputNames.UiCancel, PrototypeInputNames.UiPoint, PrototypeInputNames.UiClick, PrototypeInputNames.UiScrollWheel })
            {
                Assert.IsNotNull(ui.actions.Find(x => x.name == name), "UI aksiyonu yok: " + name);
            }

            Assert.IsTrue(ui.bindings.Exists(b => b.action == PrototypeInputNames.UiNavigate && b.path == "<Gamepad>/dpad"), "UI Navigate dpad bağı yok.");
            Assert.IsTrue(ui.bindings.Exists(b => b.action == PrototypeInputNames.UiSubmit && b.path == "<Gamepad>/buttonSouth"), "UI Submit güney düğmesi yok.");
            Assert.IsTrue(ui.bindings.Exists(b => b.action == PrototypeInputNames.UiCancel && b.path == "<Gamepad>/buttonEast"), "UI Cancel doğu düğmesi yok.");
        }

        /// <summary>
        /// Only Assets/_Prototype/Input may reference UnityEngine.InputSystem (IInputSource
        /// contract). Known pre-WP-024 exception: the F1 panel's keyboard fallback for a
        /// scene without a real source (Core/Debug/PrototypeDebugPanel.cs). Tests are scanned
        /// too — the WP-024 seam (VirtualGamepadDriver) keeps the type out of them.
        /// </summary>
        [Test]
        public void InputSystem_YalnizInputKlasorunde()
        {
            var offenders = new List<string>();
            foreach (string path in Directory.GetFiles(PrototypeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string rel = path.Replace('\\', '/').Substring(PrototypeRoot.Length + 1);
                if (rel.StartsWith("Input/") || rel == "Core/Debug/PrototypeDebugPanel.cs" || rel.EndsWith("InputParityContractTests.cs"))
                {
                    continue;
                }

                string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", " ");
                if (Regex.IsMatch(code, @"\bUnityEngine\.InputSystem\b"))
                {
                    offenders.Add(rel);
                }
            }

            CollectionAssert.IsEmpty(offenders, "Input klasörü dışında UnityEngine.InputSystem referansı.");
        }

        // ---------------------------------------------------------------- preferences

        [Test]
        public void InputPreferences_JsonRoundTrip_VeClamp()
        {
            var p = new InputPreferences
            {
                StickDeadzone = 0.22f, RumbleEnabled = false, RumbleIntensity = 0.9f,
                AimAssistEnabled = true, AimAssistAngleDegrees = 12f, AimAssistDistance = 40f
            };
            p.Clamp();
            Assert.AreEqual(InputPreferences.MaxRumbleIntensity, p.RumbleIntensity, 1e-5f, "Rumble tavanı 0.3.");
            Assert.AreEqual(InputPreferences.MaxAimAssistAngleDegrees, p.AimAssistAngleDegrees, 1e-5f, "Assist açısı tavanı 6°.");
            Assert.AreEqual(InputPreferences.MaxAimAssistDistance, p.AimAssistDistance, 1e-5f, "Assist mesafesi tavanı 15 m.");

            InputPreferences back = InputPreferences.FromJson(p.ToJson());
            Assert.IsTrue(back.SameAs(p), "JSON round-trip değer kaybetti.");
            Assert.IsTrue(InputPreferences.FromJson("not json").SameAs(new InputPreferences()), "Bozuk JSON varsayılana düşmeli.");
            Assert.IsTrue(InputPreferences.FromJson("").SameAs(new InputPreferences()));
        }

        [Test]
        public void InputPreferencesStore_DosyayaYazarOkur_ProfilDisinda()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dd-wp024-" + System.Guid.NewGuid().ToString("N"));
            string file = Path.Combine(dir, "input-settings.json");
            InputPreferencesStore.SetPathOverride(file);
            try
            {
                Assert.IsFalse(File.Exists(file));
                var edited = InputPreferencesStore.Current.Copy();
                edited.StickDeadzone = 0.3f;
                edited.RumbleEnabled = false;
                InputPreferencesStore.Apply(edited);
                Assert.IsTrue(File.Exists(file), "Ayar dosyası yazılmadı.");
                Assert.IsFalse(file.Contains("prototype-profile"), "Ayar dosyası profil dosyası değil.");

                InputPreferencesStore.SetPathOverride(file);   // drop the loaded state, force a re-read
                Assert.AreEqual(0.3f, InputPreferencesStore.Current.StickDeadzone, 1e-5f, "Deadzone diskten geri gelmedi.");
                Assert.IsFalse(InputPreferencesStore.Current.RumbleEnabled);

                InputPreferencesStore.ResetToDefaults();
                Assert.IsTrue(InputPreferencesStore.Current.SameAs(InputSettingsAsset.LoadDefaults()), "Varsayılana dönüş asset varsayılanlarına eşit değil.");
            }
            finally
            {
                InputPreferencesStore.SetPathOverride(null);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void InputSettingsAsset_ResourcesTaVar_VeSinirlarIcinde()
        {
            InputPreferences d = InputSettingsAsset.LoadDefaults();
            Assert.IsNotNull(Resources.Load<InputSettingsAsset>(InputSettingsAsset.ResourcePath), "DD_InputSettings asset'i Resources'ta yok.");
            Assert.AreEqual(0.15f, d.StickDeadzone, 1e-5f, "Varsayılan ölü bölge 0.15 (istek).");
            Assert.LessOrEqual(d.RumbleIntensity, InputPreferences.MaxRumbleIntensity);
            Assert.LessOrEqual(d.AimAssistAngleDegrees, InputPreferences.MaxAimAssistAngleDegrees);
            Assert.LessOrEqual(d.AimAssistDistance, InputPreferences.MaxAimAssistDistance);
        }

        // ---------------------------------------------------------------- aim assist

        private static readonly Vector3 Origin = new Vector3(10f, 0f, 10f);

        private static Vector3 At(float angleDeg, float distance)
        {
            Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
            return Origin + dir * distance;
        }

        [Test]
        public void AimAssist_KoniIcindekiEnYakinAciyiSecer_DisindakineDokunmaz()
        {
            var c = new List<Vector3> { At(20f, 5f), At(4f, 8f), At(-2f, 12f), At(5.5f, 14.9f) };
            AimAssist.Result r = AimAssist.Resolve(Origin, Vector3.forward, c, 6f, 15f);
            Assert.IsTrue(r.Assisted);
            Assert.AreEqual(2, r.TargetIndex, "En küçük açı (−2°) kazanmalı.");
            Assert.AreEqual(2f, r.AngleDegrees, 0.01f);
            Assert.AreEqual(12f, r.Distance, 0.01f);
            Assert.AreEqual(1f, r.Direction.magnitude, 1e-4f, "Yön birim vektör.");
            Assert.AreEqual(0f, r.Direction.y, 1e-6f, "Yatay düzlem.");

            AimAssist.Result none = AimAssist.Resolve(Origin, Vector3.forward, new List<Vector3> { At(6.5f, 5f), At(0f, 15.5f) }, 6f, 15f);
            Assert.IsFalse(none.Assisted, "6.5° ve 15.5 m koni dışında.");
            Assert.AreEqual(Vector3.forward, none.Direction, "Koni dışında yön aynen.");
        }

        [Test]
        public void AimAssist_IstekTavanlariAsilamaz_KapaliykenYonAynen()
        {
            var c = new List<Vector3> { At(9f, 5f), At(0f, 20f) };
            AimAssist.Result r = AimAssist.Resolve(Origin, Vector3.forward, c, 45f, 100f);
            Assert.IsFalse(r.Assisted, "Tercih tavanı genişletemez (6°/15 m).");

            AimAssist.Result off = AimAssist.Resolve(Origin, Vector3.forward, new List<Vector3> { At(1f, 3f) }, 0f, 15f);
            Assert.IsFalse(off.Assisted);
            Assert.AreEqual(Vector3.forward, off.Direction);

            AimAssist.Result empty = AimAssist.Resolve(Origin, Vector3.forward, null, 6f, 15f);
            Assert.IsFalse(empty.Assisted);
        }

        /// <summary>The assist is a direction function: no reference to it exists in the damage / weapon pipeline.</summary>
        [Test]
        public void AimAssist_HasarHattindaReferansYok()
        {
            foreach (string dir in new[] { "Weapons", "Damage", "Enemies", "Economy" })
            {
                foreach (string path in Directory.GetFiles(Path.Combine(PrototypeRoot, dir), "*.cs", SearchOption.AllDirectories))
                {
                    Assert.IsFalse(File.ReadAllText(path).Contains("AimAssist"), path + " AimAssist'e başvuruyor (ADR-024-1: yalnız PlayerAim).");
                }
            }

            Assert.IsTrue(File.ReadAllText(Path.Combine(PrototypeRoot, "Player/PlayerAim.cs")).Contains("AimAssist.Resolve"), "PlayerAim assist'i çağırmıyor.");
        }
    }
}
