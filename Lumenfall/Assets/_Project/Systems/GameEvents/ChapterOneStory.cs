using System;
using System.Collections.Generic;

namespace Lumenfall.GameEvents
{
    public enum ChapterOneStage
    {
        Arrival, FindingFriends, ReturnToReception, DiscussContradiction,
        FollowToSuite, SuiteEntered, ApproachRoom, ReadyForSleep, Sleeping, Complete
    }

    /// <summary>Chapter progress is derived entirely from the existing saved event state.</summary>
    public sealed class ChapterOneStory
    {
        public const string Prefix = "chapter1.";
        public const string CompleteEventId = Prefix + "complete";
        public const string KeyFlag = "inventory.keyitem.dormitory-key";
        private readonly GameEventRuntime runtime;
        private string authorizedEvent;
        private readonly List<Func<string, bool>> transitionPermissions = new List<Func<string, bool>>();
        public void AddTransitionPermission(Func<string, bool> permission)
        { if (permission != null && !transitionPermissions.Contains(permission)) transitionPermissions.Add(permission); }
        public void RemoveTransitionPermission(Func<string, bool> permission) { transitionPermissions.Remove(permission); }
        public string CurrentObjective
        {
            get
            {
                switch (Stage)
                {
                    case ChapterOneStage.Arrival: return "Check your accommodation assignment at reception in the arrival building.";
                    case ChapterOneStage.FindingFriends: return "Meet Stella, Cinna Cirra, Orio and Katrina; finish their six introductory quests.";
                    case ChapterOneStage.ReturnToReception: return "Return to reception with your four dorm-mates to ask for the Sirius key.";
                    case ChapterOneStage.DiscussContradiction: return "Meet the group in the Sirius common area and compare the receptionist's accounts.";
                    case ChapterOneStage.FollowToSuite: return "Bring the suite key to the residential door, southeast of the Sirius common area.";
                    case ChapterOneStage.SuiteEntered: return "Listen to Stella's directions to your sleeping room.";
                    case ChapterOneStage.ApproachRoom: return "Follow Stella's directions east to your sleeping room.";
                    case ChapterOneStage.ReadyForSleep: return "Rest at your bed when you're ready.";
                    case ChapterOneStage.Sleeping: return "Rest until morning.";
                    default: return "Chapter 1 complete. The receptionist's contradiction remains unanswered.";
                }
            }
        }
        private readonly List<Func<string, bool>> dialoguePermissions = new List<Func<string, bool>>();
        public void AddDialoguePermission(Func<string, bool> permission)
        { if (permission != null && !dialoguePermissions.Contains(permission)) dialoguePermissions.Add(permission); }
        public void RemoveDialoguePermission(Func<string, bool> permission) { dialoguePermissions.Remove(permission); }
        private readonly Dictionary<string, string> friendDialogues = new Dictionary<string, string>
        {
            { "stella_first_light_dialogue", "stella" },
            { "stella_string_them_together_dialogue", "stella" },
            { "cinna_out_of_alignment_dialogue", "cinna" },
            { "cinna_cold_feet_warm_idea_dialogue", "cinna" },
            { "orio_patch_notes_dialogue", "orio" },
            { "katrina_something_borrowed_dialogue", "katrina" }
        };
        private static readonly string[] RequiredFriends = { "stella", "cinna", "orio", "katrina" };
        private static readonly string[] RequiredQuests =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };
        public ChapterOneStory(GameEventRuntime runtime)
        {
            this.runtime = runtime;
            runtime.RegisterConditionHandler("ChapterOne", (condition, _) =>
                authorizedEvent == condition.key && CanAdvance(condition.key));
            runtime.DialogueEnded += OnDialogueEnded;
        }
        public bool IsAvailable => runtime.Catalog.Events.ContainsKey(Prefix + "first_account");
        public bool HasKey => runtime.GetFlag(KeyFlag) && Done("key_handover");
        public bool CanEnterSuite => HasKey && Done("group_discussion");
        public ChapterOneStage Stage
        {
            get
            {
                if (Done("complete")) return ChapterOneStage.Complete;
                if (Done("sleep")) return ChapterOneStage.Sleeping;
                if (Done("room_arrival")) return ChapterOneStage.ReadyForSleep;
                if (Done("room_guidance")) return ChapterOneStage.ApproachRoom;
                if (Done("suite_entered")) return ChapterOneStage.SuiteEntered;
                if (Done("group_discussion")) return ChapterOneStage.FollowToSuite;
                if (Done("key_handover")) return ChapterOneStage.DiscussContradiction;
                if (!Done("first_account")) return ChapterOneStage.Arrival;
                return AllFriendsReady ? ChapterOneStage.ReturnToReception : ChapterOneStage.FindingFriends;
            }
        }
        public bool AllFriendsReady
        {
            get
            {
                foreach (string quest in RequiredQuests)
                    if (runtime.GetQuestStatus(quest) != QuestStatus.Completed) return false;
                foreach (string friend in RequiredFriends)
                    if (!runtime.GetFlag(Prefix + "met." + friend)) return false;
                return true;
            }
        }
        public bool CanStartDialogue(string id)
        {
            if (!IsAvailable || id == null || !id.StartsWith(Prefix, StringComparison.Ordinal)) return true;
            foreach (var permission in dialoguePermissions) if (!permission(id)) return false;
            switch (id)
            {
                case Prefix + "reception_first": return Stage == ChapterOneStage.Arrival;
                case Prefix + "reception_handover": return Stage == ChapterOneStage.ReturnToReception;
                case Prefix + "group": return Stage == ChapterOneStage.DiscussContradiction;
                case Prefix + "guide": return Stage == ChapterOneStage.SuiteEntered;
                case Prefix + "sleep_prompt": return Stage == ChapterOneStage.ReadyForSleep;
                case Prefix + "wake": return Stage == ChapterOneStage.Complete;
                default: return true;
            }
        }
        /// <summary>Null means use the NPC's existing quest dialogue selector.</summary>
        public string SelectNpcDialogue(string npcId)
        {
            if (!IsAvailable) return null;
            if (npcId == "receptionist")
                return Stage == ChapterOneStage.Arrival ? Prefix + "reception_first" :
                    Stage == ChapterOneStage.ReturnToReception ? Prefix + "reception_handover" :
                    HasKey ? Prefix + "reception_after" : Prefix + "reception_reminder";
            if (npcId == "sirius_group")
                return Stage == ChapterOneStage.DiscussContradiction ? Prefix + "group" :
                    Stage == ChapterOneStage.SuiteEntered ? Prefix + "guide" :
                    CanEnterSuite ? Prefix + "group_after" : Prefix + "group_wait";
            if (Stage == ChapterOneStage.Arrival &&
                (npcId == "stella" || npcId == "orio" || npcId == "katrina" ||
                 npcId == "cinna_cirra_orientation" || npcId == "cinna_cirra_trial"))
                return Prefix + "find_reception";
            return null;
        }
        public bool TryEnterSuite() => Advance("suite_entered");
        public bool TryReachRoom() => Advance("room_arrival");
        public bool TrySleep() => Advance("sleep");
        public bool TryWake() => Advance("complete");
        private bool Done(string suffix) => runtime.HasEventTriggered(Prefix + suffix);
        private bool CanAdvance(string id)
        {
            foreach (var permission in transitionPermissions) if (!permission(id)) return false;
            switch (id)
            {
                case Prefix + "first_account": return Stage == ChapterOneStage.Arrival;
                case Prefix + "key_handover": return Stage == ChapterOneStage.ReturnToReception && !HasKey;
                case Prefix + "group_discussion": return Stage == ChapterOneStage.DiscussContradiction && HasKey;
                case Prefix + "suite_entered": return Stage == ChapterOneStage.FollowToSuite && CanEnterSuite;
                case Prefix + "room_guidance": return Stage == ChapterOneStage.SuiteEntered;
                case Prefix + "room_arrival": return Stage == ChapterOneStage.ApproachRoom && CanEnterSuite;
                case Prefix + "sleep": return Stage == ChapterOneStage.ReadyForSleep && CanEnterSuite;
                case Prefix + "complete": return Stage == ChapterOneStage.Sleeping && CanEnterSuite;
                default: return false;
            }
        }
        private bool Advance(string suffix)
        {
            string id = Prefix + suffix;
            if (!IsAvailable || !CanAdvance(id)) return false;
            authorizedEvent = id;
            try { return runtime.TriggerEvent(id).Succeeded; }
            finally { authorizedEvent = null; }
        }
        private void OnDialogueEnded(DialogueSession session)
        {
            if (!IsAvailable || !session.IsCompleted) return;
            string id = session.Definition.id;
            if (friendDialogues.TryGetValue(id, out string friend) && Done("first_account"))
                runtime.SetFlag(Prefix + "met." + friend, true);
            switch (id)
            {
                case Prefix + "reception_first": Advance("first_account"); break;
                case Prefix + "reception_handover": Advance("key_handover"); break;
                case Prefix + "group": Advance("group_discussion"); break;
                case Prefix + "guide": Advance("room_guidance"); break;
                case Prefix + "sleep_prompt":
                    if (session.CurrentNode.id == "rest") TrySleep();
                    break;
            }
        }
    }
}
