using System.Linq;
using Lumenfall.GameEvents;
using Lumenfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class PlayerProfileTests
    {
        [TearDown]
        public void TearDown() => PlayerProfile.Forget();

        [Test]
        public void NewCharacterIsUnknownUntilNamed()
        {
            PlayerProfile.Forget();
            Assert.That(PlayerProfile.HasName, Is.False);
            Assert.That(PlayerProfile.DisplayName, Is.EqualTo("???"));
            Assert.That(PlayerProfile.Substitute("Alright {player}, in you go."), Is.EqualTo("Alright ???, in you go."));
        }

        [Test]
        public void BlankNameBecomesSol()
        {
            PlayerProfile.SetName("   ");
            Assert.That(PlayerProfile.Name, Is.EqualTo(PlayerProfile.DefaultName));
            Assert.That(PlayerProfile.DefaultName, Is.EqualTo("Sol"));
        }

        [Test]
        public void TypedNameIsTidiedBeforeItReachesDialogue()
        {
            PlayerProfile.SetName("  Fac<b>torio{player}\n  ");
            // Brackets that could open rich text or a token, and line breaks, are dropped.
            Assert.That(PlayerProfile.Name, Is.EqualTo("Facbtorioplayer"));
            PlayerProfile.SetName("An extremely long chosen name");
            Assert.That(PlayerProfile.Name.Length, Is.LessThanOrEqualTo(PlayerProfile.MaxNameLength));
            Assert.That(PlayerProfile.Substitute("Alright {player}."), Is.EqualTo($"Alright {PlayerProfile.Name}."));
        }

        [Test]
        public void PlayerSpeakerTokenIsRecognised()
        {
            Assert.That(PlayerProfile.IsPlayerSpeaker("{player}"), Is.True);
            Assert.That(PlayerProfile.IsPlayerSpeaker(" {Player} "), Is.True);
            Assert.That(PlayerProfile.IsPlayerSpeaker("Narrator"), Is.False);
        }

        [Test]
        public void AwakeningIsTheUnnamedPlayersInnerVoiceWithOneWayForward()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/opening_story");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));
            Assert.That(catalog.TryGetDialogue("opening_awakening", out DialogueDefinition dialogue), Is.True);

            foreach (DialogueNodeDefinition node in dialogue.nodes)
            {
                Assert.That(PlayerProfile.IsPlayerSpeaker(node.speaker), Is.True, node.id + " should be the player's own thought.");
                Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), node.id + " should read as inner monologue.");
                Assert.That(node.choices.Length, Is.LessThanOrEqualTo(1), node.id + " should have at most one way forward.");
            }

            DialogueNodeDefinition where = dialogue.nodes.Single(node => node.choices.Length == 1);
            Assert.That(where.text, Does.Contain("Where"));
            Assert.That(where.choices[0].text, Is.EqualTo("Look around"));
        }
    }
}
