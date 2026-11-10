using System.Collections.Generic;
using System.Linq;
using Lumenfall.Audio;
using Lumenfall.GameEvents;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SecretRoomMoodTests
    {
        private static GameEventCatalog Events()
        {
            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("GameEvents"))
            {
                Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
                databases.Add(database);
            }
            return new GameEventCatalog(databases);
        }

        [Test]
        public void TheTwoQuietRoomsPlayNoMusicAndTheOtherSecretRoomsKeepTheirs()
        {
            Assert.That(AudioContextPolicy.MapMusicCue(SecretRooms.ForgottenRoomId), Is.Null);
            Assert.That(AudioContextPolicy.MapMusicCue(SecretRooms.DogRoomId), Is.Null);
            Assert.That(AudioContextPolicy.MapMusicCue(SecretRooms.DevRoomId), Is.EqualTo("music.campus"));
            Assert.That(AudioContextPolicy.MapMusicCue(SecretRooms.ClosetId), Is.EqualTo("music.campus"));
            Assert.That(AudioContextPolicy.MapMusicCue("campus"), Is.EqualTo("music.campus"));
        }

        [Test]
        public void EverythingToLookAtIsNarratedAndTheLeftEggCanOnlyBeTakenOnce()
        {
            GameEventCatalog events = Events();
            foreach (string id in SecretRooms.InspectDialogues)
            {
                Assert.That(events.TryGetDialogue(id, out DialogueDefinition dialogue), Is.True, id);
                Assert.That(dialogue.nodes, Is.Not.Empty, id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                {
                    Assert.That(node.speaker, Is.Empty, id + "/" + node.id + " is narration, not a character.");
                    Assert.That(node.text, Is.Not.Empty, id + "/" + node.id);
                }
            }

            Assert.That(events.TryGetDialogueNode("secret_forgotten_egg", "n1", out DialogueNodeDefinition egg), Is.True);
            DialogueChoiceDefinition take = egg.choices.Single(choice => choice.id == "take");
            Assert.That(take.actions.Any(action => action.key == "found_misc_egg"), Is.True);
            Assert.That(take.conditions.Any(condition => condition.key == SecretRoomMood.EggTakenFlag && condition.negate), Is.True,
                "Once the egg is taken it cannot be taken again.");
        }

        [Test]
        public void TheBlackoutGoesOutStuttersAndComesBackAndTheFailingLampMostlyHolds()
        {
            Assert.That(SecretRoomMood.BlackoutLevel(-1f), Is.EqualTo(1f));
            Assert.That(SecretRoomMood.BlackoutLevel(0.05f), Is.EqualTo(0f));
            Assert.That(SecretRoomMood.BlackoutLevel(0.17f), Is.GreaterThan(0f), "A stutter of light in the middle.");
            Assert.That(SecretRoomMood.BlackoutLevel(0.6f), Is.EqualTo(0f), "Dark when he goes, at 0.35 s.");
            Assert.That(SecretRoomMood.BlackoutLevel(2f), Is.EqualTo(1f));

            int dark = 0, steady = 0, samples = 0;
            for (float t = 0f; t < 600f; t += 0.05f, samples++)
            {
                float level = SecretRoomMood.FailingLampLevel(t);
                Assert.That(level, Is.InRange(0f, 1f));
                if (level < 0.3f) dark++;
                if (level > 0.8f) steady++;
            }
            Assert.That(dark, Is.GreaterThan(0), "The failing lamp does fail now and then.");
            Assert.That(steady, Is.GreaterThan(samples * 0.7f), "It is mostly lit.");
        }

        [Test]
        public void TheDogBreathesInTimeWithItsSnore()
        {
            Assert.That(SecretRoomAudio.Breath(0f), Is.EqualTo(0f));
            Assert.That(SecretRoomAudio.Breath(1.5f), Is.EqualTo(1f), "Full between breathing in and out.");
            Assert.That(SecretRoomAudio.Breath(3f), Is.EqualTo(0f));
            for (float t = 0f; t < SecretRoomAudio.SnoreSeconds; t += 0.1f)
            {
                Assert.That(SecretRoomAudio.Breath(t), Is.InRange(0f, 1f));
                Assert.That(SecretRoomAudio.Breath(t + SecretRoomAudio.SnoreSeconds), Is.EqualTo(SecretRoomAudio.Breath(t)).Within(0.0001f));
            }
        }

        [Test]
        public void TheManGoesOnlyOnceThePlayerHasWalkedWellAwayFromHim()
        {
            Assert.That(SecretRooms.TryGetRoom(SecretRooms.ForgottenRoomId, out SecretRooms.Room room), Is.True);
            int manRow = room.Height - 2;
            Assert.That(manRow - SecretRoomMood.VanishBelowRow, Is.GreaterThanOrEqualTo(4), "He should be well behind the player, in the dark.");
            Assert.That(SecretRoomMood.VanishBelowRow, Is.GreaterThan(room.Arrival.y), "It happens before they reach the door.");
            Assert.That(SecretRooms.PhotoRow, Is.InRange(SecretRoomMood.VanishBelowRow + 1, manRow), "The photograph is reached from beside him.");

            Assert.That(SecretRooms.TryGetRoom(SecretRooms.DogRoomId, out SecretRooms.Room dogRoom), Is.True);
            RectInt inside = new RectInt(0, 0, dogRoom.Width, dogRoom.Height);
            Assert.That(inside.Contains(SecretRoomMood.DogBedCell) && inside.Contains(SecretRoomMood.HoleCell), Is.True);
        }
    }
}
