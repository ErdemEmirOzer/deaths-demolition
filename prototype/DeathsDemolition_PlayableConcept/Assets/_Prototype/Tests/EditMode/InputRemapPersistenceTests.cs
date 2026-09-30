using System.IO;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Inputs;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-024 (ADR-024-2) · rebinding overrides: entries, override / reset, JSON round-trip on disk (profile untouched).</summary>
    [TestFixture]
    [Category("WP024")]
    public sealed class InputRemapPersistenceTests
    {
        private string _dir;
        private string _file;
        private IInputRemapService _svc;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "dd-wp024-remap-" + System.Guid.NewGuid().ToString("N"));
            _file = Path.Combine(_dir, "input-overrides.json");
            _svc = InputRemapService.CreateFromResources(_file);
            Assert.IsNotNull(_svc, "DD_Controls Resources'tan yüklenemedi.");
        }

        [TearDown]
        public void TearDown()
        {
            (_svc as System.IDisposable)?.Dispose();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private InputRemapEntry Entry(string action, InputSchemeKind scheme, string part = "")
        {
            foreach (InputRemapEntry e in _svc.Entries)
            {
                if (e.Action == action && e.Scheme == scheme && e.CompositePart == part)
                {
                    return e;
                }
            }

            Assert.Fail("Entry yok: " + action + "/" + scheme + "/" + part);
            return default;
        }

        [Test]
        public void Entries_HerAksiyonIkiSemada_WasdParcalari_DebugPanelYok()
        {
            foreach (string action in new[] { PrototypeInputNames.ActionFire, PrototypeInputNames.ActionReload, PrototypeInputNames.ActionPause, PrototypeInputNames.ActionSpecial, PrototypeInputNames.ActionInteract })
            {
                Entry(action, InputSchemeKind.KeyboardMouse);
                Entry(action, InputSchemeKind.Gamepad);
            }

            Entry(PrototypeInputNames.ActionMove, InputSchemeKind.KeyboardMouse, "up");
            Entry(PrototypeInputNames.ActionMove, InputSchemeKind.KeyboardMouse, "left");
            Entry(PrototypeInputNames.ActionMove, InputSchemeKind.Gamepad);
            Entry(PrototypeInputNames.ActionAimStick, InputSchemeKind.Gamepad);

            foreach (InputRemapEntry e in _svc.Entries)
            {
                Assert.AreNotEqual(PrototypeInputNames.ActionDebugPanel, e.Action, "DebugPanel yeniden eşlenemez.");
                Assert.AreNotEqual(PrototypeInputNames.ActionAim, e.Action, "Pointer konumu yeniden eşlenemez.");
                Assert.IsFalse(e.IsOverridden, "Taze serviste override olmamalı: " + e.Action);
                Assert.IsFalse(string.IsNullOrEmpty(e.DisplayName), "Görünen ad boş: " + e.Action);
            }
        }

        /// <summary>
        /// WP-069 B5 (S-09 WP-068 dilim 9 T3(d), C-00 30.09): the "default: X" name travels with the entry — the settings
        /// screen may not turn control paths into names itself (ADR-024-2).
        /// </summary>
        [Test]
        public void VarsayilanAdi_OverrideSonrasiDaVarsayilanTusuGosterir()
        {
            InputRemapEntry fire = Entry(PrototypeInputNames.ActionFire, InputSchemeKind.KeyboardMouse);
            Assert.IsFalse(fire.IsOverridden);
            Assert.IsFalse(string.IsNullOrEmpty(fire.DefaultDisplayName), "varsayılan adı boş");
            Assert.AreEqual(fire.DisplayName, fire.DefaultDisplayName, "override yokken varsayılan = etkin");

            Assert.IsTrue(_svc.ApplyOverride(fire.Id, "<Keyboard>/space"));
            InputRemapEntry after = _svc.Find(fire.Id);
            Assert.AreEqual(fire.DefaultDisplayName, after.DefaultDisplayName, "override varsayılanın adını değiştirmez");
            Assert.AreNotEqual(after.DisplayName, after.DefaultDisplayName, "etkin ad yeni tuşun adı");

            foreach (InputRemapEntry e in _svc.Entries)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.DefaultDisplayName), e.Action + "/" + e.Scheme + "/" + e.CompositePart + ": varsayılan adı boş");
            }
        }

        [Test]
        public void Override_Kaydet_Yukle_Sifirla_RoundTrip()
        {
            InputRemapEntry fire = Entry(PrototypeInputNames.ActionFire, InputSchemeKind.KeyboardMouse);
            Assert.AreEqual("<Mouse>/leftButton", fire.DefaultPath);

            Assert.IsTrue(_svc.ApplyOverride(fire.Id, "<Keyboard>/space"));
            InputRemapEntry after = _svc.Find(fire.Id);
            Assert.IsTrue(after.IsOverridden);
            Assert.AreEqual("<Keyboard>/space", after.EffectivePath);
            Assert.AreEqual("<Mouse>/leftButton", after.DefaultPath, "Varsayılan yol korunur.");

            _svc.Save();
            Assert.IsTrue(File.Exists(_file), "input-overrides.json yazılmadı.");
            string json = File.ReadAllText(_file);
            Assert.IsTrue(json.Contains("<Keyboard>/space"), "JSON override'ı içermiyor.");
            Assert.IsFalse(_file.Contains("prototype-profile"), "Override dosyası profil değil.");

            // A fresh service on a fresh clone starts from defaults and loads the file back.
            (_svc as System.IDisposable)?.Dispose();
            _svc = InputRemapService.CreateFromResources(_file);
            Assert.IsFalse(_svc.Find(fire.Id).IsOverridden, "Yeni klon varsayılanla başlar.");
            _svc.Load();
            Assert.AreEqual("<Keyboard>/space", _svc.Find(fire.Id).EffectivePath, "Load override'ı geri getirmedi.");

            _svc.ResetBinding(fire.Id);
            Assert.IsFalse(_svc.Find(fire.Id).IsOverridden);
            Assert.AreEqual("<Mouse>/leftButton", _svc.Find(fire.Id).EffectivePath);

            InputRemapEntry pad = Entry(PrototypeInputNames.ActionReload, InputSchemeKind.Gamepad);
            Assert.IsTrue(_svc.ApplyOverride(pad.Id, "<Gamepad>/buttonNorth"));
            _svc.ResetToDefaults();
            foreach (InputRemapEntry e in _svc.Entries)
            {
                Assert.IsFalse(e.IsOverridden, "ResetToDefaults sonrası override kaldı: " + e.Action);
            }

            Assert.IsFalse(_svc.ExportOverridesJson().Contains("buttonNorth"), "ResetToDefaults sonrası export hâlâ override taşıyor.");
            Assert.IsTrue(File.Exists(_file) && !File.ReadAllText(_file).Contains("buttonNorth"), "ResetToDefaults boş kümeyi diske yazmalı.");
        }

        [Test]
        public void BozukJson_VarsayilaniKorur_BilinmeyenEntry_False()
        {
            _svc.ImportOverridesJson("{ this is not json");
            foreach (InputRemapEntry e in _svc.Entries)
            {
                Assert.IsFalse(e.IsOverridden);
            }

            Assert.IsFalse(_svc.ApplyOverride("no-such-id", "<Keyboard>/x"));
            Assert.IsFalse(_svc.IsRebinding);
            Assert.IsFalse(_svc.StartRebind("no-such-id", null), "Bilinmeyen entry rebind başlatamaz.");
        }
    }
}
