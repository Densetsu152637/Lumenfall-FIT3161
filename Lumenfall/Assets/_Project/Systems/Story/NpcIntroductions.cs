using System;
using System.Collections;
using System.Collections.Generic;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Player;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Whom the player has met. Until someone has introduced themselves, dialogue and the journal show them as
    /// <see cref="UnknownName"/>. The first time the player talks to them, they play their introduction
    /// (<c>intro_&lt;person&gt;</c>, in <c>Resources/GameEvents/introductions.json</c>): they give their name, ask the
    /// player's, and then go straight on to whatever they had to say. Each meeting is a saved flag,
    /// <c>npc.met.&lt;person&gt;</c>, so it happens only once. Scripted first meetings (Prof. Kepler at the gate, Kit's
    /// tour) set the flag in their own dialogue, and the headmistress is known to everyone from the start. Once met,
    /// someone with a general conversation (<c>chat_&lt;person&gt;</c>, in <c>Resources/GameEvents/conversations.json</c>)
    /// opens it instead of their usual lines, unless they have a quest to offer or one under way, or the story has
    /// something for them to say (see <see cref="SettlingInScene.TryGetStoryTurn"/>).
    /// </summary>
    public static class NpcIntroductions
    {
        public const string UnknownName = "???";
        public const string MetFlagPrefix = "npc.met.";
        public const string IntroDialoguePrefix = "intro_";
        public const string ChatDialoguePrefix = "chat_";
        /// <summary>Asks for the player's name, for a player who somehow reaches an introduction still unnamed.</summary>
        public const string AskNameCutsceneId = "ask_player_name";

        /// <summary>People every student already knows by name.</summary>
        public static readonly string[] AlwaysKnown = { "prof_bann" };

        // One person with several catalog records.
        private static readonly Dictionary<string, string> PersonByNpcId = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "cinna_cirra_orientation", "cinna_cirra" },
            { "cinna_cirra_trial", "cinna_cirra" }
        };

        // Dialogue speaker names that differ from the catalog's display names.
        private static readonly Dictionary<string, string> PersonBySpeaker = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Cinna Cirra", "cinna_cirra" }
        };

        // Story flags, older than these meetings, that already mean the player has met someone, so older saves keep them.
        private static readonly Dictionary<string, string> MetByStoryFlag = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "prof_kepler", "story.orientation.kepler_met" },
            { "campus_student", "story.orientation.kit_met" }
        };

        public const string QuestThankedFlagPrefix = "npc.quest-thanked.";

        private static GameEventRuntime pendingRuntime;
        private static DialogueSession pendingSession;
        private static string pendingNpcId;
        private static readonly Queue<string> pendingDialogues = new Queue<string>();
        private static GameEventRuntime thanksRuntime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClearPending();
            if (thanksRuntime != null) thanksRuntime.DialogueEnded -= RecordQuestThanks;
            thanksRuntime = null;
        }

        /// <summary>The person a catalog record belongs to: its own id, except for people with several records.</summary>
        public static string PersonId(string npcId) =>
            npcId != null && PersonByNpcId.TryGetValue(npcId, out string person) ? person : npcId;

        /// <summary>Every catalog record id that belongs to <paramref name="person"/>, the person's own id first.</summary>
        public static IEnumerable<string> RecordIdsOf(string person)
        {
            yield return person;
            foreach (KeyValuePair<string, string> record in PersonByNpcId)
                if (record.Value == person) yield return record.Key;
        }

        public static string MetFlag(string npcId) => MetFlagPrefix + PersonId(npcId);

        public static string IntroDialogueId(string npcId) => IntroDialoguePrefix + PersonId(npcId);

        public static string ChatDialogueId(string npcId) => ChatDialoguePrefix + PersonId(npcId);

        /// <summary>True once the player knows this person's name; also true when there is no game state to ask.</summary>
        public static bool IsKnown(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId)) return true;
            string person = PersonId(npcId);
            if (Array.IndexOf(AlwaysKnown, person) >= 0) return true;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null || runtime.GetFlag(MetFlagPrefix + person)) return true;
            return MetByStoryFlag.TryGetValue(person, out string storyFlag) && runtime.GetFlag(storyFlag);
        }

        public static void MarkMet(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId)) return;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            runtime?.SetFlag(MetFlag(npcId), true);
        }

        /// <summary>How to show a person's <paramref name="name"/>: as it is once met, otherwise <see cref="UnknownName"/>.</summary>
        public static string NameFor(string npcId, string name) => IsKnown(npcId) ? name : UnknownName;

        /// <summary>The name to show for a dialogue speaker: <see cref="UnknownName"/> for someone not met yet.</summary>
        public static string SpeakerLabel(string speaker) =>
            TryFindPerson(speaker, out string person) && !IsKnown(person) ? UnknownName : speaker;

        /// <summary>Set once the player has heard an NPC's quest conversation with that quest finished.</summary>
        public static string QuestThankedFlag(string questId) => QuestThankedFlagPrefix + questId;

        /// <summary>
        /// Starts a conversation with an NPC. It runs, one dialogue straight into the next: a story scene that plays first
        /// (<see cref="SettlingInScene.TryGetPrelude"/>), their introduction if the player has not met them, and then
        /// <paramref name="dialogueId"/>. Someone without an introduction counts as met from this conversation on. Someone
        /// already met opens their general conversation in place of <paramref name="dialogueId"/>, if they have one, unless
        /// <paramref name="questBusiness"/> says their quest dialogue is what they need to say, or the story has a line or a
        /// scene for them (<see cref="SettlingInScene.TryGetStoryTurn"/>).
        /// </summary>
        public static DialogueSession StartConversation(string npcId, string dialogueId, bool questBusiness = false)
        {
            GameEventManager manager = GameEventManager.Instance;
            if (manager == null || manager.Runtime == null) return null;
            GameEventRuntime runtime = manager.Runtime;
            WatchQuestThanks(runtime);
            string person = PersonId(npcId);
            bool known = IsKnown(npcId);
            // The story comes first: a scene of its own, or a story line in place of their usual one.
            bool storyTurn = SettlingInScene.TryGetStoryTurn(runtime, person, out string storyDialogue, out string storyCutscene);
            if (storyTurn && storyCutscene != null)
            {
                MarkMet(npcId);
                return CutscenePlayer.Play(storyCutscene) ? null : manager.StartDialogue(dialogueId);
            }
            if (storyTurn && storyDialogue != null) dialogueId = storyDialogue;
            string chat = ChatDialogueId(npcId);
            if (known && !storyTurn && !questBusiness && Exists(runtime, chat)) dialogueId = chat;

            List<string> chain = new List<string>();
            if (SettlingInScene.TryGetPrelude(runtime, person, out string prelude) && Exists(runtime, prelude)) chain.Add(prelude);
            string introduction = IntroDialogueId(npcId);
            if (!known && Exists(runtime, introduction))
            {
                // They are about to ask the player's name, so the player needs one.
                if (!PlayerProfile.HasName && NameFirst.Begin(npcId, dialogueId)) return null;
                chain.Add(introduction);
            }
            else MarkMet(npcId);
            chain.Add(dialogueId);
            return StartChain(runtime, npcId, chain);
        }

        private static bool Exists(GameEventRuntime runtime, string dialogueId) =>
            !string.IsNullOrWhiteSpace(dialogueId) && runtime.Catalog.TryGetDialogue(dialogueId, out _);

        private static DialogueSession StartChain(GameEventRuntime runtime, string npcId, List<string> chain)
        {
            ClearPending();
            for (int i = 0; i < chain.Count; i++)
            {
                DialogueSession session = runtime.StartDialogue(chain[i]);
                if (session == null) continue;
                if (i < chain.Count - 1)
                {
                    pendingRuntime = runtime;
                    pendingSession = session;
                    pendingNpcId = npcId;
                    for (int next = i + 1; next < chain.Count; next++) pendingDialogues.Enqueue(chain[next]);
                    pendingRuntime.DialogueEnded += OnDialogueEnded;
                }
                return session;
            }
            return null;
        }

        // Each dialogue runs straight on into the next, so the dialogue box stays open between them. Leaving one early
        // drops the rest.
        private static void OnDialogueEnded(DialogueSession session)
        {
            if (session != pendingSession) return;
            GameEventRuntime runtime = pendingRuntime;
            string npcId = pendingNpcId;
            if (session.IsCompleted && session.Definition != null && session.Definition.id == IntroDialogueId(npcId)) MarkMet(npcId);
            if (!session.IsCompleted)
            {
                ClearPending();
                return;
            }
            while (pendingDialogues.Count > 0)
            {
                DialogueSession started = runtime.StartDialogue(pendingDialogues.Dequeue());
                if (started == null) continue;
                pendingSession = started;
                if (pendingDialogues.Count == 0) ClearPending();
                return;
            }
            ClearPending();
        }

        private static void ClearPending()
        {
            if (pendingRuntime != null) pendingRuntime.DialogueEnded -= OnDialogueEnded;
            pendingRuntime = null;
            pendingSession = null;
            pendingNpcId = null;
            pendingDialogues.Clear();
        }

        // Once the player has heard someone's quest conversation with a quest of theirs finished, that quest's thanks
        // has been said, and they move on to general conversation.
        private static void WatchQuestThanks(GameEventRuntime runtime)
        {
            if (runtime == thanksRuntime) return;
            if (thanksRuntime != null) thanksRuntime.DialogueEnded -= RecordQuestThanks;
            thanksRuntime = runtime;
            thanksRuntime.DialogueEnded += RecordQuestThanks;
        }

        private static void RecordQuestThanks(DialogueSession session)
        {
            string dialogueId = session?.Definition?.id;
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcDefinition[] npcs = loader != null && loader.LoadedNpcs != null ? loader.LoadedNpcs.npcs : null;
            if (string.IsNullOrWhiteSpace(dialogueId) || npcs == null || thanksRuntime == null) return;
            foreach (NpcDefinition npc in npcs)
            {
                NpcDialogueDefinition[] entries = npc?.dialogue ?? Array.Empty<NpcDialogueDefinition>();
                if (!Array.Exists(entries, entry => entry != null && entry.dialogueId == dialogueId)) continue;
                foreach (NpcDialogueDefinition entry in entries)
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.questId) && thanksRuntime.GetQuestStatus(entry.questId) == QuestStatus.Completed)
                        thanksRuntime.SetFlag(QuestThankedFlag(entry.questId), true);
            }
        }

        private static bool TryFindPerson(string speaker, out string person)
        {
            person = null;
            if (string.IsNullOrWhiteSpace(speaker)) return false;
            string name = speaker.Trim();
            if (PersonBySpeaker.TryGetValue(name, out person)) return true;
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcDefinition[] npcs = loader != null && loader.LoadedNpcs != null ? loader.LoadedNpcs.npcs : null;
            if (npcs == null) return false;
            foreach (NpcDefinition npc in npcs)
            {
                if (npc == null || !string.Equals(npc.displayName, name, StringComparison.OrdinalIgnoreCase)) continue;
                person = PersonId(npc.id);
                return true;
            }
            return false;
        }

        /// <summary>Asks an unnamed player for their name through a cutscene (which holds gameplay input), then talks.</summary>
        private sealed class NameFirst : MonoBehaviour
        {
            private static NameFirst current;

            /// <summary>False if the name cannot be asked for, in which case the introduction goes ahead without it.</summary>
            public static bool Begin(string npcId, string dialogueId)
            {
                if (current != null) return true;
                if (!CutscenePlayer.Play(AskNameCutsceneId)) return false;
                current = new GameObject("[Name Before Introduction]").AddComponent<NameFirst>();
                current.StartCoroutine(current.Run(npcId, dialogueId));
                return true;
            }

            private IEnumerator Run(string npcId, string dialogueId)
            {
                while (CutscenePlayer.IsPlaying) yield return null;
                if (PlayerProfile.HasName) StartConversation(npcId, dialogueId);
                Destroy(gameObject);
            }

            private void OnDestroy()
            {
                if (current == this) current = null;
            }
        }
    }
}
