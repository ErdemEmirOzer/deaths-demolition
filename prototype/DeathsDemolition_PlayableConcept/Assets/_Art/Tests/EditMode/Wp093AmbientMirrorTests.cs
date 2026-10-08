using DD.Art;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-093 Faz 1b · the ambient layer mirrors two DD.Art facts it cannot reference (DD.Art references the runtime): the plaza
    /// rects the puddle rings fall in, and the plaza's object name. A world edit that moves the plaza must move the mirror too.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093AmbientMirrorTests
    {
        [Test]
        public void MeydanDikdortgenleri_V6IleAyni()
        {
            Vector4[] mirror = AmbientVfxSystem.PlazaRects, art = Ap005SurfaceTags.V6PlazaRects, editor = WorldV5Ground.PlazaRects;
            Assert.AreEqual(art.Length, mirror.Length, "meydan dikdörtgeni sayısı");
            Assert.AreEqual(editor.Length, mirror.Length);
            for (int i = 0; i < mirror.Length; i++)
            {
                Assert.AreEqual(art[i], mirror[i], "dikdörtgen " + i + " (Ap005SurfaceTags.V6PlazaRects)");
                Assert.AreEqual(editor[i], mirror[i], "dikdörtgen " + i + " (WorldV5Ground.PlazaRects)");
            }
        }

        [Test]
        public void MeydanAdi_V5ZeminIleAyni()
        {
            Assert.AreEqual(WorldV5Ground.PlazaName, AtmosphereSystem.PlazaName);
        }
    }
}
