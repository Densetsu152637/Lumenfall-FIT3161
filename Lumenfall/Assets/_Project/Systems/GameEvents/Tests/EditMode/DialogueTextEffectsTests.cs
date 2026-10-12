using System.Linq;
using NUnit.Framework;
using Lumenfall.DataDriven;
using Effect = Lumenfall.DataDriven.DialogueTextEffects.Effect;

namespace Lumenfall.GameEvents.Tests
{
    public sealed class DialogueTextEffectsTests
    {
        [Test]
        public void PlainTextPassesThroughWithoutEffects()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("A quiet morning.");
            Assert.That(prepared.RichText, Is.EqualTo("A quiet morning."));
            Assert.That(prepared.GlyphEffects.Length, Is.EqualTo("Aquietmorning.".Length), "One glyph per non-whitespace character.");
            Assert.That(prepared.GlyphEffects.All(e => e == Effect.None));
        }

        [Test]
        public void EffectTagsAreStrippedAndCoverTheirGlyphs()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("so <wave>very</wave> <shake>cold</shake>");
            Assert.That(prepared.RichText, Is.EqualTo("so very cold"));
            Effect[] expected =
            {
                Effect.None, Effect.None,
                Effect.Wave, Effect.Wave, Effect.Wave, Effect.Wave,
                Effect.Shake, Effect.Shake, Effect.Shake, Effect.Shake
            };
            Assert.That(prepared.GlyphEffects, Is.EqualTo(expected));
        }

        [Test]
        public void StandardRichTextTagsAreKeptAndDoNotCountAsGlyphs()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("<b>Hi</b> there");
            Assert.That(prepared.RichText, Is.EqualTo("<b>Hi</b> there"));
            Assert.That(prepared.GlyphEffects.Length, Is.EqualTo(7));
        }

        [Test]
        public void SpellNamesAreHighlightedAsWholeWordsOnly()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("Cast Ampere, then visit Vis.");
            Assert.That(prepared.RichText, Does.Contain($"<color={DialogueTextEffects.SpellColour}><b>Ampere</b></color>"));
            Assert.That(prepared.RichText, Does.Contain($"<color={DialogueTextEffects.SpellColour}><b>Vis</b></color>."));
            Assert.That(prepared.RichText, Does.Contain("visit"), "Words that merely contain a spell name are left alone.");
        }

        [Test]
        public void SpellNamesInsideEffectTagsAreStillHighlighted()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("Use <wave>Weave</wave> now");
            Assert.That(prepared.RichText, Is.EqualTo($"Use <color={DialogueTextEffects.SpellColour}><b>Weave</b></color> now"));
            Assert.That(prepared.GlyphEffects.Count(e => e == Effect.Wave), Is.EqualTo(5));
        }

        [Test]
        public void ExplicitSpellTagsAreStyledLikeSpellNames()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("Try <spell>Lost Echoes</spell> here.");
            Assert.That(prepared.RichText, Is.EqualTo($"Try <color={DialogueTextEffects.SpellColour}><b>Lost Echoes</b></color> here."));
        }

        [Test]
        public void TheWordBeforeAnExclamationWaves()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("Oh no! Fine.");
            Effect[] expected = { Effect.None, Effect.None, Effect.Wave, Effect.Wave, Effect.Wave, Effect.None, Effect.None, Effect.None, Effect.None, Effect.None };
            Assert.That(prepared.GlyphEffects, Is.EqualTo(expected));
        }

        [Test]
        public void PreparedTextRevealsCleanly()
        {
            DialogueTextEffects.Prepared prepared = DialogueTextEffects.Prepare("Use <wave>Weave</wave>!");
            int count = DialogueTextReveal.VisibleCharacterCount(prepared.RichText);
            Assert.That(count, Is.EqualTo("Use Weave!".Length));
            Assert.That(DialogueTextReveal.Reveal(prepared.RichText, count), Is.EqualTo(prepared.RichText));
        }
    }
}
