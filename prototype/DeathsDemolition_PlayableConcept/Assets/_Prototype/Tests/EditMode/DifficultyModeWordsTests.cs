using DD.Prototype.Difficulty;
using DD.Prototype.UI;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-102 (S-03 R4; S-09 UI): the screens name a run's mode from the pack (difficulty.&lt;id&gt;.name, the ids
    /// DifficultyService.Id writes) - HARD / ZOR, DEMOLITION in both languages - and never label Normal, the classic run.
    /// </summary>
    public sealed class DifficultyModeWordsTests
    {
        private string _language;

        [SetUp]
        public void SetUp()
        {
            _language = UiStrings.Language;
        }

        [TearDown]
        public void TearDown()
        {
            UiStrings.Language = _language;
        }

        [Test]
        public void ModSozcugu_PakettenGelir_NormalBos_TrEn()
        {
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual(string.Empty, ResultsViewModel.DescribeMode(DifficultyMode.Normal), "Normal is never labelled");
            Assert.AreEqual("HARD", ResultsViewModel.DescribeMode(DifficultyMode.Hard));
            Assert.AreEqual("DEMOLITION", ResultsViewModel.DescribeMode(DifficultyMode.Demolition));

            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual(string.Empty, ResultsViewModel.DescribeMode(DifficultyMode.Normal));
            Assert.AreEqual("ZOR", ResultsViewModel.DescribeMode(DifficultyMode.Hard));
            Assert.AreEqual("DEMOLITION", ResultsViewModel.DescribeMode(DifficultyMode.Demolition), "a proper name, not upper-cased by TR rules");
        }
    }
}
