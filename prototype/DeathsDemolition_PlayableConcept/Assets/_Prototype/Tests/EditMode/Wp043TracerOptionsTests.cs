#if UNITY_EDITOR
using System;
using System.IO;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;

namespace DD.Prototype.Tests.EditMode
{
    [TestFixture]
    [Category("AP043")]
    public sealed class Wp043TracerOptionsTests
    {
        [Test]
        public void NarrowScope_OnlyNormalFlightAndSyntheticSpecialsKeepOriginalMaterials()
        {
            Assert.IsTrue(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, 42, ClassicWeaponId.AssaultRifle));
            Assert.IsTrue(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Pellet, 43, ClassicWeaponId.CombatShotgun));
            Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Rocket, 44, ClassicWeaponId.AssaultRifle));
            Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Orb, 45, ClassicWeaponId.AssaultRifle));
            Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Gel, 46, ClassicWeaponId.AssaultRifle));
            Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, -1, ClassicWeaponId.AssaultRifle), "legacy Emit has no normal-weapon ownership");
            Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, 42, ClassicWeaponId.Rl1, VfxWeaponClass.Launcher), "launcher stays excluded even if a legacy presentation calls its shot Bullet");
            string option = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype/Feedback/Vfx/TracerRibbonRenderer.Ap043.cs"));
            StringAssert.Contains("new Material(baseline)", option, "opt-in must clone, never set the keyword on a shared or baseline material");
            StringAssert.Contains("Material material = layer == 0 ? _ap043CoreMaterial : _ap043GlowMaterial;", option);
            StringAssert.DoesNotContain("ConfigureAp043Material(_tdCoreMaterial", option);
            StringAssert.DoesNotContain("ConfigureAp043Material(_material", option);
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype/Feedback/Vfx/TracerRibbonRenderer.cs"));
            StringAssert.Contains("if (Ap043BulletLooks.Active && r.Ap043Eligible)", source);
            StringAssert.Contains("if (CameraModeService.Current != CameraMode.TopDown", source);
        }

        [Test]
        public void Option_DefaultIsUserSelectedO2AndOnlyExplicitR4KeepsBaseline()
        {
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Selected);
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Parse(null));
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook" }));
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "O4" }));
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "o1" }));
            Assert.AreEqual(Ap043BulletLook.Baseline, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "R4" }));
            Assert.AreEqual(Ap043BulletLook.Baseline, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "Baseline" }));
            Assert.AreEqual(Ap043BulletLook.O1, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "O1" }));
            Assert.AreEqual(Ap043BulletLook.O2, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "O2" }));
            Assert.AreEqual(Ap043BulletLook.O3, Ap043BulletLooks.Parse(new[] { "game", "-ddBulletLook", "O3" }));
        }

        [Test]
        public void NormalWeaponWhitelist_AllNineOwnedIdsAndAllExcludedIdsKeepTheirScope()
        {
            for (int id = 0; id <= 8; id++)
            {
                var weapon = (ClassicWeaponId)id;
                Assert.IsTrue(Ap043TracerDesign.IsNormalWeapon(weapon));
                Assert.IsTrue(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, 712, weapon));
                Assert.IsTrue(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Pellet, 713, weapon));
                Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, -1, weapon));
                Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, 712, weapon, VfxWeaponClass.Launcher));
                foreach (ProjectileKind kind in new[] { ProjectileKind.Rocket, ProjectileKind.Orb, ProjectileKind.Gel, (ProjectileKind)99 })
                    Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(kind, 714, weapon));
            }
            foreach (int id in new[] { -1, -2, 9, 10, 11, 100, int.MinValue, int.MaxValue })
            {
                var weapon = (ClassicWeaponId)id;
                Assert.IsFalse(Ap043TracerDesign.IsNormalWeapon(weapon));
                Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, 715, weapon));
                Assert.IsFalse(Ap043TracerDesign.IsNormalFlight(ProjectileKind.Pellet, 716, weapon));
            }
        }

        [TestCase(Ap043BulletLook.O1)]
        [TestCase(Ap043BulletLook.O2)]
        [TestCase(Ap043BulletLook.O3)]
        public void AbsolutePixelEnvelope_AllClassesAllTiersAndResolutions(Ap043BulletLook option)
        {
            float[] screenHeights = { 842f, 1080f, 1440f };
            float[] pixelsPerMetre = { 12f, 70f, 95f, 150f, 900f };
            foreach (VfxWeaponClass weaponClass in Enum.GetValues(typeof(VfxWeaponClass)))
            {
                foreach (bool pellet in new[] { false, true })
                {
                    foreach (bool tracer in new[] { false, true })
                    {
                        Ap043TracerEnvelope e = Ap043TracerDesign.Envelope(option, weaponClass, pellet, tracer);
                        Assert.That(e.CoreCapPx, NUnit.Framework.Is.LessThanOrEqualTo(3f));
                        Assert.That(e.CoreFloorPx, NUnit.Framework.Is.GreaterThan(0f).And.LessThanOrEqualTo(e.CoreCapPx));
                        Assert.That(e.HaloRatio, NUnit.Framework.Is.InRange(1f, 2f));
                        foreach (float height in screenHeights)
                        {
                            foreach (float ppm in pixelsPerMetre)
                            {
                                float core = Ap043TracerDesign.WidthPixels(in e, ppm, height, false);
                                float halo = Ap043TracerDesign.WidthPixels(in e, ppm, height, true);
                                Assert.That(core, NUnit.Framework.Is.LessThanOrEqualTo(3f * height / 1080f + .0001f));
                                Assert.That(halo, NUnit.Framework.Is.LessThanOrEqualTo(2f * core + .0001f));
                                // Tier conveys strength through detail density. Neither width nor exposure contains a tier scale.
                                float previousDensity = -1f;
                                for (int tier = 1; tier <= 5; tier++)
                                {
                                    float packed = Ap043TracerDesign.PackDetail(3, tier, 1, 437);
                                    Assert.AreEqual(tier, ((int)packed / 8) % 8);
                                    float density = TreeVfxLadder.Ap043DetailDensity(tier);
                                    Assert.That(density, NUnit.Framework.Is.GreaterThan(previousDensity));
                                    previousDensity = density;
                                    Assert.AreEqual(core, Ap043TracerDesign.WidthPixels(in e, ppm, height, false));
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void PixelScale_FlippedRenderTextureHasSameCeilingAndEndOnHasNoWorldFloor()
        {
            foreach (float height in new[] { 842f, 1080f, 1440f })
            {
                float positive = Ap043TracerDesign.PixelWorld(32f, 3.2f, height);
                float flipped = Ap043TracerDesign.PixelWorld(32f, -3.2f, height);
                Assert.AreEqual(positive, flipped);
                Assert.AreEqual(64f / (3.2f * height), positive, 1e-6f);
                Assert.AreEqual(18f / height, Ap043TracerDesign.PixelWorld(32f, -3.2f, height, true, 9f), 1e-6f);
            }

            string shader = File.ReadAllText(Path.Combine(Application.dataPath, "_Art/VFX/Resources/Shaders/DD_VfxRibbon.shader"));
            StringAssert.Contains("#pragma multi_compile_local _ DD_AP043", shader);
            StringAssert.Contains("float size = Ap043Width(head.w, extra, headPixel);", shader,
                "end-on uses exactly the same ceiling; no doubled head diameter or .06 m exemption");
            StringAssert.Contains("abs(UNITY_MATRIX_P._m11)", shader);
            StringAssert.Contains("#else\n                    float dotW = head.w + stepPx * headPixel;", shader,
                "the original dot path remains compiled for baseline/shared ribbon consumers");
        }

        [TestCase(Ap043BulletLook.O1)]
        [TestCase(Ap043BulletLook.O2)]
        [TestCase(Ap043BulletLook.O3)]
        public void ExposureIsShort_ZeroSpeedIsFinite_PelletsStayBelowQuarterMetre(Ap043BulletLook option)
        {
            foreach (VfxWeaponClass weaponClass in Enum.GetValues(typeof(VfxWeaponClass)))
            {
                foreach (bool pellet in new[] { false, true })
                {
                    foreach (bool tracer in new[] { false, true })
                    {
                        Ap043TracerEnvelope e = Ap043TracerDesign.Envelope(option, weaponClass, pellet, tracer);
                        Assert.That(e.ExposureSeconds, NUnit.Framework.Is.GreaterThan(0f).And.LessThan(1f / 300f));
                        Assert.That(e.MaxLength, NUnit.Framework.Is.LessThanOrEqualTo(pellet ? .25f : 1.5f));
                        Assert.That(e.TailOutSeconds, NUnit.Framework.Is.InRange(.02f, .05f));
                        Assert.AreEqual(e.MinLength, e.Trail(0f));
                        Assert.AreEqual(e.MinLength, e.Trail(-10f));
                        Assert.AreEqual(e.MaxLength, e.Trail(100000f));
                    }
                }
            }

            Ap043TracerEnvelope o1 = Ap043TracerDesign.Envelope(option, VfxWeaponClass.Smg, false, false);
            Ap043TracerEnvelope sr = Ap043TracerDesign.Envelope(option, VfxWeaponClass.Sniper, false, true);
            Assert.That(sr.Trail(420f), NUnit.Framework.Is.GreaterThan(o1.Trail(170f)), "SR remains longer and sharper than SMG");
        }

        [Test]
        public void PhysicalOption_LmgOneInFive_CadenceStableForNegativeIdsAndPellets()
        {
            int belt = 0, rifle = 0, smg = 0;
            for (int id = 1; id <= 60; id++)
            {
                if (Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Lmg, id, false)) belt++;
                if (Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.AssaultRifle, id, false)) rifle++;
                if (Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Smg, id, false)) smg++;
                Assert.AreEqual(Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Lmg, id, false),
                    Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Lmg, -id, false));
                Assert.IsFalse(Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Shotgun, id, true));
                Assert.IsFalse(Ap043TracerDesign.IsTracer(Ap043BulletLook.O3, VfxWeaponClass.Sniper, id, false));
                Assert.IsFalse(Ap043TracerDesign.IsTracer(Ap043BulletLook.O1, VfxWeaponClass.Lmg, id, false));
                Assert.IsTrue(Ap043TracerDesign.IsTracer(Ap043BulletLook.O2, VfxWeaponClass.Lmg, id, false));
            }

            Assert.AreEqual(12, belt);
            Assert.AreEqual(15, rifle);
            Assert.AreEqual(10, smg);
        }

        [Test]
        public void WorldBodyDescriptor_IsCentimetresAndIndependentOfOpticalExposure()
        {
            foreach (VfxWeaponClass weaponClass in Enum.GetValues(typeof(VfxWeaponClass)))
            {
                float body = Ap043TracerDesign.MetalBodyLengthMetres(weaponClass, false);
                Assert.That(body, NUnit.Framework.Is.InRange(.023f, .034f));
                Assert.AreEqual(.005f, Ap043TracerDesign.MetalBodyLengthMetres(weaponClass, true));
                foreach (bool pellet in new[] { false, true })
                {
                    float physical = Ap043TracerDesign.MetalBodyLengthMetres(weaponClass, pellet);
                    Assert.AreEqual(physical, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O1, weaponClass, pellet, false));
                    Assert.AreEqual(-physical, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O3, weaponClass, pellet, false));
                    Assert.AreEqual(0f, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O2, weaponClass, pellet, false));
                    foreach (float speed in new[] { 0f, 170f, 420f, 100000f })
                    {
                        Ap043TracerEnvelope exposure = Ap043TracerDesign.Envelope(Ap043BulletLook.O1, weaponClass, pellet, false);
                        Assert.That(exposure.Trail(speed), NUnit.Framework.Is.GreaterThan(physical), "exposure is a separate optical span");
                        Assert.AreEqual(physical, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O1, weaponClass, pellet, false));
                        Assert.AreEqual(-physical, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O3, weaponClass, pellet, false));
                    }
                }

                Assert.AreEqual(.024f, Ap043TracerDesign.WorldDetail(Ap043BulletLook.O3, weaponClass, false, true),
                    "the selected tracer focus has a fixed rearward offset, never a fraction of total exposure");
                Assert.AreEqual(0f, Ap043TracerDesign.WorldDetail(Ap043BulletLook.Baseline, weaponClass, false, true));
            }
        }

        [Test]
        public void MetalCoverage_UsesWorldBodyAndSeparateOpticalElementTail()
        {
            string shader = File.ReadAllText(Path.Combine(Application.dataPath, "_Art/VFX/Resources/Shaders/DD_VfxRibbon.shader"));
            StringAssert.Contains("Ap043BodyMask(behindNose, bodyLength, sensorFootprint)", shader);
            StringAssert.Contains("float sensorFootprint = max(fwidth(behindNose), 1e-5);", shader);
            StringAssert.Contains("(_Ap043Mode > 2.5 && extra.w < 0.0)", shader,
                "O3 ordinary body must use the signed world descriptor, not the normalized exposure Gaussian");
            StringAssert.Contains("alpha = headColor.a * bodyAcross * metalBodyMask;", shader);
            StringAssert.Contains("opticalPremultiplied += tailColor.rgb * elementAlpha", shader,
                "elements compose as separately coloured tail coverage, never a metal alpha refill");
            StringAssert.DoesNotContain("if (metal) rgb = lerp(rgb", shader);
            StringAssert.Contains("else alpha = max(alpha, detailAlpha);", shader,
                "the existing detail alpha union is reserved for nonmetal halo coverage");
        }

        [Test]
        public void ExistingInstanceChannel_HoldsLookTierFusionFormAndStableSeed()
        {
            for (int look = 0; look <= 5; look++)
            {
                for (int tier = 1; tier <= 5; tier++)
                {
                    for (int secondary = 0; secondary <= 4; secondary++)
                    {
                        float packed = Ap043TracerDesign.PackDetail(look, tier, secondary, 874);
                        int whole = (int)packed;
                        Assert.AreEqual(look, whole % 8);
                        Assert.AreEqual(tier, (whole / 8) % 8);
                        Assert.AreEqual(secondary, (whole / 64) % 8);
                        Assert.That(packed - whole, NUnit.Framework.Is.InRange(0f, .999f));
                        Assert.AreEqual(packed, Ap043TracerDesign.PackDetail(look, tier, secondary, 874));
                        float pellet = Ap043TracerDesign.PackDetail(look, tier, secondary, 874, true);
                        Assert.AreEqual(512f, pellet - packed);
                        Assert.AreEqual(look, ((int)pellet) % 8);
                        Assert.AreEqual(tier, ((int)pellet / 8) % 8);
                        Assert.AreEqual(secondary, ((int)pellet / 64) % 8);
                    }
                }
            }
        }

        [Test]
        public void Contours_HaveFragmentKernelAndRearFocusAndPelletExposureFlag()
        {
            string shader = File.ReadAllText(Path.Combine(Application.dataPath, "_Art/VFX/Resources/Shaders/DD_VfxRibbon.shader"));
            StringAssert.Contains("float2 Ap043Contour(float option, float t, bool pellet)", shader);
            StringAssert.Contains("float metalBodyMask = worldBody ? Ap043BodyMask", shader);
            StringAssert.Contains("float kernel =", shader);
            StringAssert.Contains("float focus =", shader);
            StringAssert.Contains("bool pellet = floor(whole / 512.0) > 0.5;", shader);
            StringAssert.Contains("if (pellet) return float2(0.45 * kernel, kernel);", shader,
                "selected O2 pellets are one compact exposure grain within their existing width/length envelope");
            StringAssert.DoesNotContain("float ghosts = pellet ?", shader,
                "the removed multi-grain pellet distribution must not return");
            StringAssert.Contains("float noseWidth = 1.0 - smoothstep(0.86, 1.0, t);", shader);
            StringAssert.Contains("float noseAlpha = 1.0 - smoothstep(0.95, 1.0, t);", shader);
            StringAssert.Contains("return float2((0.04 + 0.60 * kernel) * noseWidth, saturate(kernel + ghosts) * noseAlpha);", shader,
                "the front taper only multiplies the existing contour by bounded 0..1 factors, so width/alpha cannot grow");
            StringAssert.Contains("return clamp(metres, min(floorMetres, capMetres), capMetres);", shader,
                "selected grain/front taper retain the absolute pixel ceiling for every geometry path");
            StringAssert.Contains("rgb = lerp(float3(0.05, 0.06, 0.065), float3(0.48, 0.50, 0.49), grainGlint);", shader,
                "the one-pixel grain focus stays below the former metal RGB ceiling (0.56/0.57/0.58)");
            StringAssert.Contains("float burning =", shader,
                "O3 burning-base proposal has an explicit rearward emission focus distinct from its metal tip");
            // These are static integration checks; contour/element visibility and bloom still need actual Player captures.
        }

        [Test]
        public void SelectedO2_ClassDimensionsAreFrozenAndSharedRandomStateIsUntouched()
        {
            var saved = UnityEngine.Random.state;
            string before = JsonUtility.ToJson(saved);
            try
            {
                for (int id = 0; id < 1000; id++)
                {
                    float a = Ap043TracerDesign.PackDetail(3, 5, 2, id);
                    Assert.AreEqual(a, Ap043TracerDesign.PackDetail(3, 5, 2, id));
                    Assert.IsTrue(Ap043TracerDesign.IsTracer(Ap043BulletLooks.Selected, VfxWeaponClass.AssaultRifle, id, false));
                }
                Assert.AreEqual(before, JsonUtility.ToJson(UnityEngine.Random.state), "presentation must not consume the gameplay RNG");
                Ap043TracerEnvelope ar = Ap043TracerDesign.Envelope(Ap043BulletLooks.Selected, VfxWeaponClass.AssaultRifle, false, true);
                Assert.AreEqual(2.8f, ar.CoreCapPx);
                Assert.AreEqual(1.6f, ar.HaloRatio);
                Assert.AreEqual(.95f, ar.MaxLength);
                Assert.AreEqual(1f / 480f, ar.ExposureSeconds);
                Ap043TracerEnvelope pellet = Ap043TracerDesign.Envelope(Ap043BulletLooks.Selected, VfxWeaponClass.Shotgun, true, false);
                Assert.AreEqual(1.75f, pellet.CoreCapPx);
                Assert.AreEqual(.22f, pellet.MaxLength);
            }
            finally { UnityEngine.Random.state = saved; }
        }

        private static float s_sink;

        private static void SampleSteady()
        {
            float sum = 0f;
            for (int i = 0; i < 256; i++)
            {
                Ap043TracerEnvelope e = Ap043TracerDesign.Envelope(Ap043BulletLooks.Selected, VfxWeaponClass.Lmg, false, true);
                sum += e.Trail(250f) + Ap043TracerDesign.WidthPixels(in e, 70f, 1080f, false)
                    + Ap043TracerDesign.PackDetail(3, 5, 2, i) + TreeVfxLadder.Ap043DetailDensity(5)
                    + Ap043TracerDesign.WorldDetail(Ap043BulletLooks.Selected, VfxWeaponClass.Lmg, false, true)
                    + (Ap043TracerDesign.IsNormalFlight(ProjectileKind.Bullet, i, ClassicWeaponId.Lmg1) ? 1f : 0f)
                    + (Ap043TracerDesign.IsTracer(Ap043BulletLooks.Selected, VfxWeaponClass.Lmg, i, false) ? 1f : 0f);
            }

            s_sink = sum;
        }

        [Test]
        public void PureHotPath_HasAllocationDetectorPositiveControlAndNoAllocations()
        {
            SampleSteady();
            byte[] control = null;
            Assert.That((TestDelegate)(() => control = new byte[4096]), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "detector must see a known allocation; Mono's always-zero byte counter is not evidence");
            Assert.That((TestDelegate)SampleSteady, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.IsNotNull(control);
            Assert.That(s_sink, NUnit.Framework.Is.GreaterThan(0f));
            // Renderer/draw native transitions and full-game frames still require the guarded Player GC/perf window.
        }
    }
}
#endif
