using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-068 R1-E E4 B1b (S-01 Core, C-00 30.09): the first-scene hand-off tells the UI when the scene is missing.</summary>
    public sealed class BootFirstSceneHandOffTests
    {
        private System.Func<string, bool> _loaderBefore;
        private System.Action<string> _missingBefore;
        private readonly List<string> _missing = new List<string>();
        private readonly List<string> _loaded = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _loaderBefore = PrototypeBootstrapper.FirstSceneLoader;
            _missingBefore = PrototypeBootstrapper.FirstSceneMissing;
            _missing.Clear();
            _loaded.Clear();
            PrototypeBootstrapper.FirstSceneMissing = name => _missing.Add(name);
        }

        [TearDown]
        public void TearDown()
        {
            PrototypeBootstrapper.FirstSceneLoader = _loaderBefore;
            PrototypeBootstrapper.FirstSceneMissing = _missingBefore;
        }

        [Test]
        public void IlkSahne_DerlemedeYok_BootKalir_UiKancasiSahneAdiylaCagrilir_YukleyiciCagrilmaz()
        {
            PrototypeBootstrapper.FirstSceneLoader = name => { _loaded.Add(name); return true; };
            LogAssert.Expect(LogType.Log, "[Boot] Scene 'NoSuchScene' is not in the build settings yet. Staying in Boot.");

            Assert.IsTrue(PrototypeBootstrapper.HandOffFirstScene("NoSuchScene", _ => false));

            CollectionAssert.AreEqual(new[] { "NoSuchScene" }, _missing);
            CollectionAssert.IsEmpty(_loaded);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IlkSahne_DerlemedeYok_KancaYoksa_YalnizLog_HataYok()
        {
            PrototypeBootstrapper.FirstSceneMissing = null;
            LogAssert.Expect(LogType.Log, "[Boot] Scene 'NoSuchScene' is not in the build settings yet. Staying in Boot.");

            Assert.IsTrue(PrototypeBootstrapper.HandOffFirstScene("NoSuchScene", _ => false));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IlkSahne_Var_UiYukleyicisiAlirsa_Durur_EksikKancasiCagrilmaz()
        {
            PrototypeBootstrapper.FirstSceneLoader = name => { _loaded.Add(name); return true; };

            Assert.IsTrue(PrototypeBootstrapper.HandOffFirstScene("MainMenu", _ => true));

            CollectionAssert.AreEqual(new[] { "MainMenu" }, _loaded);
            CollectionAssert.IsEmpty(_missing);
        }

        [Test]
        public void IlkSahne_Var_YukleyiciYokVeyaReddederse_DuzYuklemeyeDuser()
        {
            PrototypeBootstrapper.FirstSceneLoader = null;
            Assert.IsFalse(PrototypeBootstrapper.HandOffFirstScene("MainMenu", _ => true));

            PrototypeBootstrapper.FirstSceneLoader = name => { _loaded.Add(name); return false; };
            Assert.IsFalse(PrototypeBootstrapper.HandOffFirstScene("MainMenu", _ => true));

            CollectionAssert.AreEqual(new[] { "MainMenu" }, _loaded);
            CollectionAssert.IsEmpty(_missing);
        }
    }
}
