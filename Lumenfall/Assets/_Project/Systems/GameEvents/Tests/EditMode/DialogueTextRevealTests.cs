using NUnit.Framework;
using Lumenfall.DataDriven;

namespace Lumenfall.GameEvents.Tests
{
    public sealed class DialogueTextRevealTests
    {
        [Test]
        public void RichTextTagsDoNotConsumeCharacters()
        {
            Assert.That(DialogueTextReveal.VisibleCharacterCount("<b>Hi</b>!"), Is.EqualTo(3));
            Assert.That(DialogueTextReveal.Reveal("<b>Hi</b>!", 1), Is.EqualTo("<b>H</b>"));
            Assert.That(DialogueTextReveal.Reveal("<b>Hi</b>!", 2), Is.EqualTo("<b>Hi</b>"));
        }

        [Test]
        public void PartialRevealNeverProducesHalfTag()
        {
            Assert.That(DialogueTextReveal.Reveal("A <color=#f00>red</color> line", 2), Is.EqualTo("A "));
            Assert.That(DialogueTextReveal.Reveal("A <color=#f00>red</color> line", 5), Is.EqualTo("A <color=#f00>red</color>"));
            Assert.That(DialogueTextReveal.Reveal("<b><i>Nested</i></b>", 2), Is.EqualTo("<b><i>Ne</i></b>"));
        }

        [Test]
        public void AttributedTagsStayBalancedAtEveryPartialBoundary()
        {
            const string text = "<b><color=#f00>red</color></b> next";
            string[] expected =
            {
                "",
                "<b><color=#f00>r</color></b>",
                "<b><color=#f00>re</color></b>",
                "<b><color=#f00>red</color></b>",
                "<b><color=#f00>red</color></b> "
            };

            for (int visibleCharacters = 0; visibleCharacters < expected.Length; visibleCharacters++)
            {
                Assert.That(
                    DialogueTextReveal.Reveal(text, visibleCharacters),
                    Is.EqualTo(expected[visibleCharacters]),
                    "Unexpected output after revealing " + visibleCharacters + " characters.");
            }

            Assert.That(
                DialogueTextReveal.Reveal(text, DialogueTextReveal.VisibleCharacterCount(text)),
                Is.EqualTo(text));
        }

        [TestCase("<color=#f00>red</color><br/>")]
        [TestCase("Text<b></b>")]
        public void FullRevealPreservesTrailingMarkup(string text)
        {
            int total = DialogueTextReveal.VisibleCharacterCount(text);
            Assert.That(DialogueTextReveal.Reveal(text, total), Is.EqualTo(text));
            Assert.That(DialogueTextReveal.Reveal(text, total + 100), Is.EqualTo(text));
        }

        [Test]
        public void EmptyAndFullRevealAreStable()
        {
            const string text = "Hello";
            Assert.That(DialogueTextReveal.Reveal(text, 0), Is.Empty);
            Assert.That(DialogueTextReveal.Reveal(text, 99), Is.EqualTo(text));
        }

        [Test]
        public void PunctuationAddsPauseWithoutChangingVisibleCount()
        {
            const string text = "One, two!";
            Assert.That(DialogueTextReveal.VisibleCharacterCount(text), Is.EqualTo(text.Length));
            Assert.That(DialogueTextReveal.PunctuationPause(','), Is.EqualTo(0.12f));
            Assert.That(DialogueTextReveal.PunctuationPause(' '), Is.EqualTo(0f));
        }

        [Test]
        public void RevealIsIndependentOfFrameSizeAndCanSkipInstantly()
        {
            const string text = "A <i>long</i> line.";
            int total = DialogueTextReveal.VisibleCharacterCount(text);
            Assert.That(DialogueTextReveal.Reveal(text, total), Is.EqualTo(text));
            Assert.That(DialogueTextReveal.Reveal(text, total + 100), Is.EqualTo(text));
            Assert.That(DialogueTextReveal.VisibleCharacterAt(text, total - 1), Is.EqualTo('.'));
        }
    }
}
