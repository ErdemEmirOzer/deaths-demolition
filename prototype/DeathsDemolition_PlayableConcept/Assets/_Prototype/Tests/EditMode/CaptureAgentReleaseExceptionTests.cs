using DD.Prototype.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// H12 written exception (C-00 30.09, Codex-audit M-55): PrototypeCaptureAgent stays in release builds as the release
    /// smoke's image-and-exit path, so without -ddCapture it must do nothing: no object, no allocation.
    /// </summary>
    public sealed class CaptureAgentReleaseExceptionTests
    {
        private static readonly string[] BayraksizArgv = { "DeathsDemolition", "-screen-fullscreen", "0", "-ddDirectToRun", "-logFile", "run.log" };
        private static readonly string[] YolsuzBayrakArgv = { "DeathsDemolition", "-ddSize", "1280x720", "-ddCapture" };
        private static byte[] s_yem;

        [Test]
        public void YakalamaAjani_BayrakYok_NesneKurmaz_FalseDoner()
        {
            Assert.IsFalse(PrototypeCaptureAgent.TryStart(BayraksizArgv));
            Assert.IsFalse(PrototypeCaptureAgent.TryStart(YolsuzBayrakArgv), "-ddCapture yolsuz (son argüman) → başlamaz");
            Assert.IsNull(GameObject.Find("PrototypeCaptureAgent"));
        }

        [Test]
        public void YakalamaAjani_BayrakYok_BellekAyirmaz_PozitifKontrolAyirir()
        {
            PrototypeCaptureAgent.TryStart(BayraksizArgv);   // ısınma: JIT / statik ilk erişim ölçüme girmesin
            Assert.That((TestDelegate)(() => { s_yem = new byte[64 * 1024]; }), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "pozitif kontrol: ölçüm bu ortamda ayırmayı görebiliyor olmalı");
            Assert.That((TestDelegate)(() => { PrototypeCaptureAgent.TryStart(BayraksizArgv); }), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That((TestDelegate)(() => { PrototypeCaptureAgent.TryStart(YolsuzBayrakArgv); }), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
