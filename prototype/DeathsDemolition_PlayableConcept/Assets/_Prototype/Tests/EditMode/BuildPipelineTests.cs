using System;
using System.IO;
using DD.Prototype.Core.Build;
using System.Reflection;
using DD.Prototype.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-025 · version stamp, crash log masking and file output.</summary>
    [TestFixture]
    public sealed class BuildPipelineTests
    {
        // The Editor tool lives in DD.Prototype.Core.Editor, which this test assembly does not
        // reference; the two helpers below reach it by reflection so the test asmdef stays as is.
        private static Type StampType => Type.GetType("DD.Prototype.Core.EditorTools.BuildStamp, DD.Prototype.Core.Editor");

        private static object CallStamp(string method, params object[] args)
        {
            Type t = StampType;
            Assert.IsNotNull(t, "BuildStamp (DD.Prototype.Core.Editor) bulunmalı");
            MethodInfo m = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, method);
            return m.Invoke(null, args);
        }

        public static string PlaceholderJson() => new BuildInfo().ToJson();

        public static string StampAbsolutePath() => (string)StampType.GetProperty("AbsolutePath", BindingFlags.Public | BindingFlags.Static).GetValue(null);

        [Test]
        public void BuildInfo_YerTutucu_StampedFalse_LabelDevUnstamped()
        {
            BuildInfo placeholder = BuildInfo.Parse(PlaceholderJson());
            Assert.IsNotNull(placeholder);
            Assert.IsFalse(placeholder.stamped);
            Assert.AreEqual("dev-unstamped", placeholder.Label);
            Assert.IsNull(BuildInfo.Parse(""), "boş metin null");
            Assert.IsNull(BuildInfo.Parse("{not json"), "bozuk JSON null");
        }

        [Test]
        public void BuildStamp_Generate_GitAlanlariDoluVeJsonGidipGelir()
        {
            BuildInfo info = (BuildInfo)CallStamp("Generate", "test");
            Assert.IsTrue(info.stamped);
            Assert.AreNotEqual("unknown", info.describe, "git describe çalışmalı (repo kökü bulunur)");
            Assert.AreNotEqual("unknown", info.commit);
            Assert.AreEqual(12, info.commit.Length, "kısa commit 12 hex");
            Assert.AreEqual("test", info.mode);
            Assert.AreEqual(Application.unityVersion, info.unity);
            StringAssert.IsMatch(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", info.date);

            BuildInfo back = BuildInfo.Parse(info.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(info.describe, back.describe);
            Assert.AreEqual(info.commit, back.commit);
            StringAssert.Contains(info.describe + " · test · " + info.date.Substring(0, 10), back.Label);
        }

        [Test]
        public void BuildStamp_SevkiyatDosyasi_YerTutucuIleAyni()
        {
            // The committed build-info.json must be the placeholder: a build stamps it and
            // restores it, so a stale stamp in the tree means a build was interrupted.
            string committed = File.ReadAllText(StampAbsolutePath());
            BuildInfo info = BuildInfo.Parse(committed);
            Assert.IsNotNull(info, "build-info.json okunabilir olmalı");
            Assert.IsFalse(info.stamped, "depodaki build-info.json yer tutucu olmalı (stamped=false); bir build yarım kalmış");
            Assert.AreEqual("dev-unstamped", info.describe);
        }

        [Test]
        public void CrashLog_Mask_EvDiziniVeKullaniciAdiGizlenir()
        {
            string masked = CrashLogReporter.Mask(
                "at /Users/erdem/Library/Application Support/x/y.json (erdem) C:\\Users\\erdem\\z /home/erdem/w erdemli",
                "/Users/erdem", "erdem");
            StringAssert.DoesNotContain("/Users/erdem", masked);
            StringAssert.DoesNotContain("/home/erdem", masked);
            StringAssert.DoesNotContain("Users\\erdem", masked);
            StringAssert.Contains("~/Library/Application Support/x/y.json", masked);
            StringAssert.Contains("(<user>)", masked);
            StringAssert.Contains("erdemli", masked, "kelime sınırı: ad bir kelimenin parçasıysa dokunulmaz");
            Assert.AreEqual("", CrashLogReporter.Mask(null, "/Users/a", "a"));
            Assert.AreEqual("plain", CrashLogReporter.Mask("plain", null, null));
        }

        [Test]
        public void CrashLog_Kayit_DosyaYazilir_TekrarBastirilir_YolMaskelenir()
        {
            string root = Path.Combine(Path.GetTempPath(), "dd-crashlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                CrashLogReporter.Install(root);
                string home = Environment.GetEnvironmentVariable("HOME") ?? "/Users/nobody";
                CrashLogReporter.Record("Exception", "NullReferenceException: boom at " + home + "/secret.txt", "at X.Y () in " + home + "/Proj/A.cs:12");
                CrashLogReporter.Record("Exception", "NullReferenceException: boom at " + home + "/secret.txt", "dup");
                CrashLogReporter.Record("Error", "second kind", null);
                Assert.AreEqual(2, CrashLogReporter.Entries, "aynı mesaj ikinci kez yazılmaz");
                string file = CrashLogReporter.CurrentFile;
                Assert.IsTrue(File.Exists(file), "dosya oluşmalı: " + file);
                StringAssert.StartsWith(Path.Combine(root, CrashLogReporter.LogsFolder), file);
                string text = File.ReadAllText(file);
                StringAssert.Contains("Deaths Demolition crash log", text);
                StringAssert.Contains("build=describe=", text);
                StringAssert.Contains("run=", text);
                StringAssert.Contains("second kind", text);
                StringAssert.DoesNotContain(home + "/secret.txt", text, "ev dizini maskelenmeli");
                StringAssert.Contains("~/secret.txt", text);
                Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, CrashLogReporter.LogsFolder)).Length);
            }
            finally
            {
                CrashLogReporter.Uninstall();
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }
    }
}
