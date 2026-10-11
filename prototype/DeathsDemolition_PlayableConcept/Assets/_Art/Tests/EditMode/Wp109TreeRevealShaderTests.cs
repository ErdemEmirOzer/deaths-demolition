using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.CameraSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-109 (R4) · the top-down crown reveal's contract: DD/Foliage Wind compiles; ForwardLit, DepthOnly and DepthNormals drop
    /// the revealed crown pixels (a depth prepass must agree with the colour) under _ALPHATEST_ON only; the ShadowCaster never does
    /// (the crown keeps its shade); the reveal is a global, outside UnityPerMaterial (the SRP Batcher and static batches stay);
    /// and the camera rig carries TreeCanopyReveal. The pixels are measured in Wp109AgacSaydamlikTdTests (PlayMode, targeted).
    /// </summary>
    public sealed class Wp109TreeRevealShaderTests
    {
        private const string ShaderPath = "Assets/_Art/Shaders/DD_Foliage_Wind.shader";
        private const string IncludePath = "Assets/_Art/Shaders/DD_TreeReveal.hlsl";

        private static string PassBody(string source, string name)
        {
            Match m = Regex.Match(source, "Name \"" + name + "\"(.*?)ENDHLSL", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, name + " pass not found");
            return m.Groups[1].Value;
        }

        [Test]
        public void FoliageWind_Compiles_WithTheRevealInclude()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            Assert.AreEqual("DD/Foliage Wind", shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "DD/Foliage Wind has compile errors");
            Assert.IsTrue(File.Exists(IncludePath), IncludePath);
            StringAssert.Contains("#include \"DD_TreeReveal.hlsl\"", File.ReadAllText(ShaderPath));
        }

        [Test]
        public void Reveal_InColourAndDepthPasses_UnderAlphaTest_NeverInTheShadow()
        {
            string source = File.ReadAllText(ShaderPath);
            foreach (string pass in new[] { "ForwardLit", "DepthOnly", "DepthNormals" })
            {
                string body = PassBody(source, pass);
                int call = body.IndexOf("DD_TreeRevealClip(input.positionWS, input.positionCS)", System.StringComparison.Ordinal);
                Assert.GreaterOrEqual(call, 0, pass + ": no reveal clip");
                int test = body.LastIndexOf("#if defined(_ALPHATEST_ON)", call, System.StringComparison.Ordinal);
                int end = body.LastIndexOf("#endif", call, System.StringComparison.Ordinal);
                Assert.Greater(test, end, pass + ": the reveal clip must sit inside #if defined(_ALPHATEST_ON) (cutout crowns only)");
            }
            StringAssert.DoesNotContain("DD_TreeRevealClip", PassBody(source, "ShadowCaster"), "the crown's shadow must not thin");
        }

        [Test]
        public void Reveal_IsAGlobal_NotAMaterialProperty()
        {
            string include = File.ReadAllText(IncludePath);
            StringAssert.Contains("float4 _DD_TreeReveal;", include);
            StringAssert.Contains("float4 _DD_TreeRevealShape;", include);
            string source = File.ReadAllText(ShaderPath);
            Match cbuffer = Regex.Match(source, "CBUFFER_START\\(UnityPerMaterial\\)(.*?)CBUFFER_END", RegexOptions.Singleline);
            Assert.IsTrue(cbuffer.Success);
            StringAssert.DoesNotContain("_DD_TreeReveal", cbuffer.Groups[1].Value, "a per-material reveal would break the SRP Batcher");
        }

        [Test]
        public void Shape_IsALightTouch()
        {
            Assert.That(TreeCanopyReveal.DroppedShare, Is.InRange(.4f, .8f), "hafif: the crown still reads");
            Assert.That(TreeCanopyReveal.Radius, Is.InRange(1.5f, 4f));
            Assert.Less(TreeCanopyReveal.SoftRim, TreeCanopyReveal.Radius);
            Assert.That(TreeCanopyReveal.FadeSeconds, Is.InRange(.05f, .5f));
        }

        [Test]
        public void CameraRigFactory_AddsTheReveal()
        {
            string factory = File.ReadAllText("Assets/_Prototype/Camera/CameraRigFactory.cs");
            StringAssert.Contains("AddComponent<TreeCanopyReveal>()", factory);
        }
    }
}
