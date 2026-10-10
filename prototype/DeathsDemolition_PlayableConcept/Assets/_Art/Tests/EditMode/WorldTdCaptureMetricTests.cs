using System.Linq;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World.Vegetation;
using NUnit.Framework;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-092 R3 (10.10) · The bare-ground gate's pixel rule (<see cref="WorldTdCapture.BareMetric"/>): a pixel is bare only when the
    /// id render shows a ground id; a dark covered pixel (a roof tile channel, an eave, a shaded interior taken to 0 by the no-post
    /// render) is cover — the legacy black-threshold counted those as bare. The ground scope is the legacy one (the same four
    /// ground shaders), split natural / paved.
    /// </summary>
    public sealed class WorldTdCaptureMetricTests
    {
        [Test]
        [Category("WP092")]
        public void CiplakZemin_YalnizZeminKimligi_KoyuKapliPikselCiplakDegil()
        {
            Assert.AreEqual(WorldTdCapture.GroundClass.Natural, WorldTdCapture.Classify(WorldTdCapture.NaturalId));
            Assert.AreEqual(WorldTdCapture.GroundClass.Paved, WorldTdCapture.Classify(WorldTdCapture.PavedId));
            Assert.AreEqual(WorldTdCapture.GroundClass.Natural, WorldTdCapture.Classify(new Color32(240, 12, 250, 255)), "kimlik toleransı (±24)");

            // the legacy rule's false bare: black and near-black covered pixels
            Assert.AreEqual(WorldTdCapture.GroundClass.Cover, WorldTdCapture.Classify(new Color32(0, 0, 0, 255)), "siyah = kaplı (çatı oluğu, saçak)");
            Assert.AreEqual(WorldTdCapture.GroundClass.Cover, WorldTdCapture.Classify(new Color32(3, 2, 1, 255)), "neredeyse siyah = kaplı");
            // lit cover and near misses
            Assert.AreEqual(WorldTdCapture.GroundClass.Cover, WorldTdCapture.Classify(new Color32(255, 255, 255, 255)), "su kimliği (beyaz) = kaplı");
            Assert.AreEqual(WorldTdCapture.GroundClass.Cover, WorldTdCapture.Classify(new Color32(60, 120, 40, 255)), "çim = kaplı");
            Assert.AreEqual(WorldTdCapture.GroundClass.Cover, WorldTdCapture.Classify(new Color32(200, 0, 200, 255)), "toleransın dışı = kaplı");

            // the scope is the legacy one: the same four ground shaders, natural and paved disjoint
            CollectionAssert.AreEquivalent(new[] { "DD/GroundMoss", "DD/GroundTerrain", "DD/GroundTiles", "DD/GroundBlend" },
                WorldTdCapture.NaturalGroundShaders.Concat(WorldTdCapture.PavedGroundShaders).ToArray());
            Assert.AreEqual("ground-id-v2", WorldTdCapture.BareMetric);
        }

        /// <summary>C-00 10.10 decision (B): the gate's scope is vegetation ground — the designed bare surfaces (paths, paving, desire
        /// paths, karesansui gravel, the amaochi drip strip, water, under roofs) are not counted.</summary>
        [Test]
        [Category("WP092")]
        public void BitkiZemini_YolDosemeAsinmisRotaKaresansuiSayilmaz()
        {
            ushort ground = (ushort)VegetationGrid.Flag.Ground;
            Assert.IsTrue(WorldTdCapture.IsVegetationGround(ground, (byte)VegetationZone.Lawn), "çim");
            Assert.IsTrue(WorldTdCapture.IsVegetationGround(ground, (byte)VegetationZone.BuildingSurround), "bina çevresi");
            Assert.IsTrue(WorldTdCapture.IsVegetationGround(ground, (byte)VegetationZone.MossCourt), "yosun bahçesi");
            foreach (VegetationGrid.Flag f in new[] { VegetationGrid.Flag.Path, VegetationGrid.Flag.Plaza, VegetationGrid.Flag.Water, VegetationGrid.Flag.Excluded, VegetationGrid.Flag.Roofed })
                Assert.IsFalse(WorldTdCapture.IsVegetationGround((ushort)(ground | (ushort)f), (byte)VegetationZone.Lawn), f + " bayraklı hücre bitki zemini sayıldı");
            foreach (VegetationZone z in new[] { VegetationZone.Auto, VegetationZone.DesirePath, VegetationZone.BareSoil, VegetationZone.Karesansui, VegetationZone.PlazaJoint })
                Assert.IsFalse(WorldTdCapture.IsVegetationGround(ground, (byte)z), z + " (tasarım gereği çıplak) bitki zemini sayıldı");
            Assert.IsFalse(WorldTdCapture.IsVegetationGround(0, (byte)VegetationZone.Lawn), "Ground bayrağı yok");
            Assert.AreEqual(0.25f, WorldTdCapture.VegGateLimit, 1e-6f);
            Assert.AreEqual("veg-ground-v1", WorldTdCapture.VegGateMetric);
        }
    }
}
