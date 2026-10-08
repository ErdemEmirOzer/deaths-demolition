using System;
using System.IO;
using DD.Prototype.Diagnostics;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-087 R3 (C-00 07.10) · -ddMoveTrace: no flag, no trace; a value is the file; no value (or another -flag next) puts
    /// movetrace-&lt;utc&gt;.csv beside Player.log, never into the player's data folder.
    /// </summary>
    [Category("WP087")]
    public sealed class Wp087HareketIziArgsTests
    {
        private static readonly DateTime Utc = new DateTime(2026, 10, 7, 8, 30, 5, DateTimeKind.Utc);
        private const string Log = "/Users/x/Library/Logs/DD/Player.log";

        [Test]
        public void BayrakYok_IzYok()
        {
            Assert.IsFalse(MoveTraceRecorder.TryResolvePath(new[] { "app", "-ddQaBot", "/tmp/o" }, Log, Utc, out string p));
            Assert.IsNull(p);
            Assert.IsFalse(MoveTraceRecorder.TryResolvePath(null, Log, Utc, out _));
        }

        [Test]
        public void BayrakDegerli_DosyaO()
        {
            Assert.IsTrue(MoveTraceRecorder.TryResolvePath(new[] { "app", MoveTraceRecorder.Flag, "/tmp/iz.csv" }, Log, Utc, out string p));
            Assert.AreEqual("/tmp/iz.csv", p);
        }

        [Test]
        public void BayrakDegersiz_PlayerLogYaninda()
        {
            string expected = Path.Combine(Path.GetDirectoryName(Log), "movetrace-20261007T083005Z.csv");
            Assert.IsTrue(MoveTraceRecorder.TryResolvePath(new[] { "app", MoveTraceRecorder.Flag }, Log, Utc, out string p), "son belirteç bayrak");
            Assert.AreEqual(expected, p);
            Assert.IsTrue(MoveTraceRecorder.TryResolvePath(new[] { "app", MoveTraceRecorder.Flag, "-ddProbe", "x" }, Log, Utc, out p), "ardından başka bayrak");
            Assert.AreEqual(expected, p);
            Assert.IsTrue(MoveTraceRecorder.TryResolvePath(new[] { "app", MoveTraceRecorder.Flag }, null, Utc, out p), "log yolu yok");
            Assert.AreEqual(Path.Combine(".", "movetrace-20261007T083005Z.csv"), p);
        }

        [Test]
        public void Baslik_YirmiSutun()
        {
            Assert.AreEqual(20, MoveTraceRecorder.Header.Split(',').Length);
        }
    }
}
