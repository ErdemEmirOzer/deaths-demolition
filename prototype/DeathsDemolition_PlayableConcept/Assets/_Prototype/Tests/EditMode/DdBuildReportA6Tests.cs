using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-078 A6 parity (S-10, C-00 30.09): DdBuild's build-report.json carries development, sceneList (build order,
    /// path + guid) and scriptingBackend. DdBuild lives in DD.Prototype.Core.Editor (not referenced here) → reflection.</summary>
    public sealed class DdBuildReportA6Tests
    {
        [Serializable] private sealed class SceneRow { public string path; public string guid; }
        [Serializable] private sealed class Report
        {
            public string result; public string mode; public int scenes; public bool development; public string scriptingBackend;
            public SceneRow[] sceneList; public long totalSize;
        }

        private static string Json(bool development, string[] scenes, string backend)
        {
            Type t = Type.GetType("DD.Prototype.Core.EditorTools.DdBuild, DD.Prototype.Core.Editor");
            Assert.IsNotNull(t, "DdBuild (DD.Prototype.Core.Editor) bulunmalı");
            MethodInfo m = t.GetMethod("ReportJson", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "DdBuild.ReportJson");
            return (string)m.Invoke(null, new object[] { "Succeeded", development ? "dev" : "release", null, "lz4hc", "/tmp/x.app", scenes,
                development, backend, 1181216746UL, 0, 3, 12.5, "{\n  \"describe\": \"t\"\n}" });
        }

        [Test]
        public void Rapor_A6Alanlari_GelistirmeSahneListesiBetikArkaUcu_SiraVeGuidDogru()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Assert.Greater(scenes.Length, 0, "Build Settings'te etkin sahne olmalı");
            string backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString();

            Report r = JsonUtility.FromJson<Report>(Json(false, scenes, backend));
            Assert.AreEqual("Succeeded", r.result);
            Assert.AreEqual("release", r.mode);
            Assert.IsFalse(r.development);
            Assert.AreEqual(backend, r.scriptingBackend);
            Assert.AreEqual(scenes.Length, r.scenes, "eski okuyucular için sayı kalır");
            Assert.AreEqual(1181216746L, r.totalSize);
            CollectionAssert.AreEqual(scenes, r.sceneList.Select(x => x.path).ToArray(), "EditorBuildSettings sırası");
            for (int i = 0; i < scenes.Length; i++)
            {
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(scenes[i]), r.sceneList[i].guid, scenes[i]);
                StringAssert.IsMatch("^[0-9a-f]{32}$", r.sceneList[i].guid, scenes[i]);
            }

            Assert.IsTrue(JsonUtility.FromJson<Report>(Json(true, scenes, "IL2CPP")).development);
        }

        [Test]
        public void Rapor_SahneYok_BosListeGecerliJson_EskiAnahtarBicimiAyni()
        {
            string json = Json(false, new string[0], "Mono2x");
            Report r = JsonUtility.FromJson<Report>(json);
            Assert.AreEqual(0, r.scenes);
            Assert.AreEqual(0, r.sceneList.Length);
            StringAssert.Contains("\"result\": \"Succeeded\",", json, "build.sh bu satırı grep'ler");
            StringAssert.Contains("\"mode\": \"release\",", json);
            StringAssert.Contains("\"compression\": \"lz4hc\",", json);
        }
    }
}
