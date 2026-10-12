using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Lumenfall.DataDriven;

namespace Lumenfall.GameEvents.Tests
{
    public sealed class DialoguePagingTests
    {
        private static string Visible(string rich) => Regex.Replace(rich, "<[^>]+>", string.Empty);

        [Test]
        public void ShortTextIsOnePage()
        {
            Assert.That(DialoguePaging.Split("Hello there.", 120), Is.EqualTo(new[] { "Hello there." }));
        }

        [Test]
        public void PagesBreakAfterSentences()
        {
            var pages = DialoguePaging.Split("First line here. Second line here. Third line here.", 36);
            Assert.That(pages, Is.EqualTo(new[] { "First line here. Second line here.", "Third line here." }));
        }

        [Test]
        public void EveryPageFitsAndNoTextIsLost()
        {
            const string text = "Finish this reminder to learn Rotara if you left the lesson early. Select Rotara on the spell wheel and cast near " +
                                "the F04 forest gear. Each cast adds 90 degrees clockwise: 180, 270, 0, 90. If you overshoot, keep turning " +
                                "until the pointer reaches the teal 90-degree notch, then report back.";
            var pages = DialoguePaging.Split(text, 130);
            Assert.That(pages.Count, Is.GreaterThan(1));
            Assert.That(pages.All(p => Visible(p).Length <= 130));
            Assert.That(string.Join(" ", pages.Select(Visible)), Is.EqualTo(text));
        }

        [Test]
        public void ALongSentenceBreaksBetweenWords()
        {
            var pages = DialoguePaging.Split("one two three four five six seven eight nine ten", 15);
            Assert.That(pages.All(p => Visible(p).Length <= 15));
            Assert.That(pages.All(p => !p.StartsWith(" ") && !p.EndsWith(" ")));
            Assert.That(string.Join(" ", pages), Is.EqualTo("one two three four five six seven eight nine ten"));
        }

        [Test]
        public void TagsOpenAcrossABreakAreClosedAndReopened()
        {
            var pages = DialoguePaging.Split("<b>One sentence here. Another one here.</b> Done.", 24);
            Assert.That(pages[0], Is.EqualTo("<b>One sentence here.</b>"));
            Assert.That(pages[1], Does.StartWith("<b>Another one here.</b>"));
        }

        [Test]
        public void DecimalsAndNumbersDoNotBreakSentences()
        {
            var pages = DialoguePaging.Split("Turn it to 0.5 then stop. Next.", 26);
            Assert.That(pages[0], Is.EqualTo("Turn it to 0.5 then stop."));
        }
    }
}
