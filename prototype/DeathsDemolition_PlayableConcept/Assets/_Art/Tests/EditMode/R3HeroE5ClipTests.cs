using System.IO;
using System.Security.Cryptography;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace DD.Art.Tests.EditMode
{
    [TestFixture]
    public sealed class R3HeroE5ClipTests
    {
        [TestCase("AP022_tp_lmg_1_reload_tac", "c6a4a41fd8d22b4e7c83b0b9473fbeb762231972a58640467312d3ddff11b3e3", "0076a6eacfebe4da4b55860ce3596537", -623603649343465320L)]
        [TestCase("AP022_tp_lmg_1_reload_empty", "05016937aa729ffffb50b495e4d0d11862163f440ec4e75516ad1ae94a23b988", "5be42cb1245fc45918070c87a1270b7f", -623603649343465320L)]
        [TestCase("AP022_tp_lmg_2_reload_tac", "be4c805dd4d61bd3e92fe01df251d9ce28a23878a6fb2add3b6f13ae8ea44880", "91d21fc08d3234866bef95abc4b6c473", -623603649343465320L)]
        [TestCase("AP022_tp_lmg_2_reload_empty", "00d4def1ca57b2ecf071cb356b521099906e1d3a4c5529d7c41911709b667e3d", "f9bd52a5a5c7c411c8d407a8a03e3a71", -623603649343465320L)]
        [TestCase("AP022_tp_sw_gel_reload_tac", "a78bf46dfb82d9de94ae3b1abcbd3f2f380a4ddd113e51820e7ce3a249873383", "ace04a1aafa314265be7ba68bb4cdebd", -623603649343465320L)]
        [TestCase("AP022_tp_sw_gel_reload_empty", "456a40f5207abbe779eb146f8f7e51ad697fa9953726848c4e96e54c86de8ff0", "2840dc34b3da8400ebe06a9ed3ed8035", -623603649343465320L)]
        [TestCase("AP022_tp_sw_arc_reload_tac", "4eb86f282980e01376fb8d39129592d0e444b6f76467a3ca91881a3dc3c4a07f", "3760f3dd8d7f8474cb37fd146b03e236", -623603649343465320L)]
        [TestCase("AP022_tp_sw_arc_reload_empty", "81b406c77a76d48567cc5b20f7db96970c4a0a8fad3eb151a3a860c676adc760", "845615704052a435eae9a24b3f348753", -623603649343465320L)]
        public void D1V009_ImportedClipIdentityEventsAndSetReference(string name, string sha, string guid, long id)
        {
            var spec = ArtAnimationClipContract.ManifestAll()[name];
            string path = ArtAnimationClipContract.FbxPath(spec);
            using (var hash = SHA256.Create())
                Assert.AreEqual(sha, System.BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(), "v009 bytes");
            AnimationClip clip = ArtAnimationClipContract.ClipOf(spec);
            Assert.IsNotNull(clip, "canonical clip subasset missing after FBX replacement");
            Assert.AreEqual(name, clip.name);
            Assert.IsTrue(clip.humanMotion, "Humanoid import");
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string actualGuid, out long actualId));
            Assert.AreEqual(guid, actualGuid, "asset GUID must remain stable");
            Assert.AreEqual(id, actualId, "subasset ID must remain stable");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.IsNotNull(importer);
            CollectionAssert.IsEmpty(ArtAnimationClipContract.Validate(importer));
            Assert.AreEqual(spec.seconds, clip.length, 1f / 30f, "duration must preserve gameplay scaling");
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>("Assets/_Art/Characters/Player/CharacterAnimationSet_Player.asset");
            Assert.IsNotNull(set.Find(name));
            Assert.AreSame(clip, set.Find(name).clip, "existing animation set reference must still resolve");
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            foreach (var beat in spec.events)
                Assert.IsTrue(System.Array.Exists(events, e => e.functionName == beat.Key && Mathf.Abs(e.time - beat.Value / 30f) <= 1f / 30f), "event " + beat.Key + "@" + beat.Value);
        }
    }
}
