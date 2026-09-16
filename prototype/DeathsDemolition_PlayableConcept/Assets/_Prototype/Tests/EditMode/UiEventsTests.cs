using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-022B · directive §12: every UI interaction is a named event with a matrix row.
    /// The enum and <c>planning/work-packages/EVENT-COVERAGE-MATRIX-UI.md</c> must be the
    /// same set (an event without a row, or a row without an event, is a DoD FAIL), every
    /// row fills its SFX and VFX cells, and raising never throws into the caller.
    /// </summary>
    [TestFixture]
    [Category("WP022B")]
    public sealed class UiEventsTests
    {
        private static string MatrixPath
        {
            get
            {
                string root = Directory.GetParent(Application.dataPath).Parent.Parent.FullName;
                return Path.Combine(root, "planning", "work-packages", "EVENT-COVERAGE-MATRIX-UI.md");
            }
        }

        [TearDown]
        public void TearDown()
        {
            UiEvents.ResetCounters();
        }

        [Test]
        public void Matris_EnumIleBirebir_HerSatirDolu()
        {
            Assert.IsTrue(File.Exists(MatrixPath), "matris dosyası yok: " + MatrixPath);
            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(MatrixPath))
            {
                if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| UiEvent", StringComparison.Ordinal) || line.StartsWith("|---", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cells = line.Trim().Trim('|').Split('|');
                if (cells.Length < 5)
                {
                    continue;
                }

                rows[cells[0].Trim()] = cells;
            }

            var missing = new List<string>();
            var empty = new List<string>();
            foreach (UiEvent e in Enum.GetValues(typeof(UiEvent)))
            {
                string[] cells;
                if (!rows.TryGetValue(e.ToString(), out cells))
                {
                    missing.Add(e.ToString());
                    continue;
                }

                for (int i = 1; i < 5; i++)
                {
                    if (string.IsNullOrWhiteSpace(cells[i]))
                    {
                        empty.Add(e + " sütun " + i);
                    }
                }
            }

            var extra = new List<string>();
            foreach (string name in rows.Keys)
            {
                if (!Enum.IsDefined(typeof(UiEvent), name))
                {
                    extra.Add(name);
                }
            }

            Assert.IsEmpty(missing, "matriste satırı olmayan UiEvent: " + string.Join(", ", missing));
            Assert.IsEmpty(extra, "enum'da olmayan matris satırı: " + string.Join(", ", extra));
            Assert.IsEmpty(empty, "boş matris hücresi (DoD FAIL): " + string.Join(", ", empty));
            Assert.GreaterOrEqual(rows.Count, 30, "UI olay kapsamı daraldı");
        }

        [Test]
        public void Raise_AboneHatasiCagiranaSizmaz_SayacVeDetay()
        {
            int received = 0;
            Action<UiEvent, string> bad = (e, d) => { received++; throw new InvalidOperationException("abone hatası"); };
            UiEvents.Raised += bad;
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex("abone hatası"));
                UiEvents.Raise(UiEvent.ButtonPress, "Play");
                Assert.AreEqual(1, received);
                Assert.AreEqual(UiEvent.ButtonPress, UiEvents.Last);
                Assert.AreEqual("Play", UiEvents.LastDetail);
                Assert.AreEqual(1, UiEvents.RaisedCount);
            }
            finally
            {
                UiEvents.Raised -= bad;
            }

            UiEvents.Raise(UiEvent.Back);
            Assert.AreEqual(string.Empty, UiEvents.LastDetail, "detay verilmezse boş string, null değil");
        }

        [Test]
        public void UiSound_HerCueBirOlay()
        {
            UiEvents.ResetCounters();
            UiSound.Play(UiSoundKind.Denied);
            Assert.AreEqual(UiEvent.Denied, UiEvents.Last, "Denied cue'su Denied olayıdır (EditMode'da ses çalmasa da olay yükselir)");
            UiSound.Play(UiSoundKind.Reveal);
            Assert.AreEqual(UiEvent.ResultsRevealed, UiEvents.Last);
            Assert.AreEqual(2, UiEvents.RaisedCount);
        }
    }
}
