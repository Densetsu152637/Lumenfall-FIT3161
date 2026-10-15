using System;
using Lumenfall.GameEvents;

namespace Lumenfall.Audio
{
    /// <summary>Owns captured runtime subscriptions; restore changes context without replaying rewards.</summary>
    public sealed class GameAudioSignals : IDisposable
    {
        public const string MysteryFlag = "audio.mystery";
        public const string SleepingFlag = "audio.sleeping";
        public const string WokeFlag = "audio.woke";
        private GameEventRuntime runtime;
        private string mapId;
        private bool wasSleeping;
        private bool wasWoke;
        public AudioContext Context { get; private set; }
        public string MapId => mapId;
        public bool DialogueActive => runtime?.ActiveDialogue != null;
        public bool Sleeping => runtime != null && (runtime.GetFlag(SleepingFlag) ||
            (runtime.GetFlag("chapter1.sleep") && !runtime.GetFlag("chapter1.complete")));
        public event Action<AudioContext> ContextChanged;
        public event Action<string> CueRequested;
        public void Bind(GameEventRuntime next)
        {
            if (ReferenceEquals(runtime, next)) return;
            Unsubscribe(); runtime = next;
            if (runtime != null)
            {
                runtime.DialogueStarted += OnDialogueStarted; runtime.DialogueEnded += OnDialogueEnded;
                runtime.FlagChanged += OnFlagChanged; runtime.QuestStatusChanged += OnQuestChanged;
                runtime.EventTriggered += OnEvent; runtime.StateRestored += Refresh;
            }
            Refresh();
        }
        public void SetMap(string id) { mapId = id; Refresh(); }
        private void Refresh()
        {
            AudioContext next = AudioContextPolicy.Resolve(mapId, DialogueActive,
                runtime != null && (runtime.GetFlag(MysteryFlag) || (runtime.GetFlag("chapter1.key_handover") &&
                !runtime.GetFlag("chapter1.sleep") && !runtime.GetFlag("chapter1.complete"))), Sleeping);
            wasSleeping = Sleeping;
            wasWoke = runtime != null && (runtime.GetFlag(WokeFlag) || runtime.GetFlag("chapter1.complete"));
            if (next == Context) return; Context = next; ContextChanged?.Invoke(next);
        }
        private void OnDialogueStarted(DialogueSession _) { Refresh(); CueRequested?.Invoke("ui.open"); }
        private void OnDialogueEnded(DialogueSession _) { Refresh(); }
        private void OnFlagChanged(string flag, bool value)
        {
            if (flag != MysteryFlag && flag != SleepingFlag && flag != WokeFlag &&
                flag != "chapter1.key_handover" && flag != "chapter1.sleep" && flag != "chapter1.complete") return;
            bool previousSleep = wasSleeping; bool previousWoke = wasWoke;
            Refresh();
            if (!previousSleep && wasSleeping) CueRequested?.Invoke("story.sleep");
            if (!previousWoke && wasWoke) CueRequested?.Invoke("story.wake");
        }
        private void OnQuestChanged(QuestDefinition _, QuestStatus previous, QuestStatus current)
        { if (current == QuestStatus.Completed && previous != current) CueRequested?.Invoke("quest.complete"); }
        private void OnEvent(GameEventDefinition definition)
        {
            string cue = definition?.metadata.GetValue("audioCue");
            if (!string.IsNullOrWhiteSpace(cue)) CueRequested?.Invoke(cue);
        }
        private void Unsubscribe()
        {
            if (runtime == null) return;
            runtime.DialogueStarted -= OnDialogueStarted; runtime.DialogueEnded -= OnDialogueEnded;
            runtime.FlagChanged -= OnFlagChanged; runtime.QuestStatusChanged -= OnQuestChanged;
            runtime.EventTriggered -= OnEvent; runtime.StateRestored -= Refresh;
        }
        public void Dispose() { Unsubscribe(); runtime = null; Refresh(); }
    }
}
