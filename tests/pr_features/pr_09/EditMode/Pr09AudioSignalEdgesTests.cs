using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.GameEvents;
using NUnit.Framework;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR09")]
    public sealed class Pr09AudioSignalEdgesTests
    {
        private static GameEventRuntime Runtime() => new GameEventRuntime(new GameEventCatalog(new[] {
            new GameEventDatabaseDefinition { dialogues = new[] { new DialogueDefinition { id = "talk", startNodeId = "n", nodes = new[] { new DialogueNodeDefinition { id = "n", text = "Hello" } } } } }
        }));
        [Test]
        public void EquivalentMapsAndIrrelevantFlagsDoNotEmitContextChanges()
        {
            var runtime = Runtime();
            using (var audio = new GameAudioSignals())
            {
                var changes = new List<AudioContext>();
                audio.ContextChanged += changes.Add;
                audio.Bind(runtime); audio.SetMap("westForest"); audio.SetMap("eastForest");
                runtime.SetFlag("unrelated", true); audio.SetMap("eastForest");
                Assert.That(changes, Is.EqualTo(new[] { AudioContext.Forest }));
            }
        }
        [Test]
        public void NullBindingReleasesActiveDialogueAndOldRuntimeCannotChangeContext()
        {
            var runtime = Runtime();
            using (var audio = new GameAudioSignals())
            {
                audio.SetMap("campus"); audio.Bind(runtime);
                var dialogue = runtime.StartDialogue("talk");
                Assert.That(audio.DialogueActive, Is.True);
                audio.Bind(null);
                Assert.That(audio.DialogueActive, Is.False);
                Assert.That(audio.Context, Is.EqualTo(AudioContext.Campus));
                dialogue.End(); runtime.SetFlag(GameAudioSignals.SleepingFlag, true);
                Assert.That(audio.Context, Is.EqualTo(AudioContext.Campus));
            }
        }
        [Test]
        public void SleepCueRearmsOnlyAfterARealFallingEdge()
        {
            var runtime = Runtime();
            using (var audio = new GameAudioSignals())
            {
                var cues = new List<string>(); audio.CueRequested += cues.Add; audio.Bind(runtime);
                runtime.SetFlag(GameAudioSignals.SleepingFlag, true);
                runtime.SetFlag(GameAudioSignals.SleepingFlag, true);
                runtime.SetFlag(GameAudioSignals.SleepingFlag, false);
                runtime.SetFlag(GameAudioSignals.SleepingFlag, true);
                Assert.That(cues, Is.EqualTo(new[] { "story.sleep", "story.sleep" }));
            }
        }
    }
}
