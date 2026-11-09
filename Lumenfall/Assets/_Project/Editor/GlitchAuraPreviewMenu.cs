using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Story;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Previews the glitch aura (<see cref="GlitchAura"/>) on the receptionist Marigold. The level is kept in the editor's
    /// PlayerPrefs, so it can be set before or during play mode and carries into the next run until set back to Off.
    /// </summary>
    public static class GlitchAuraPreviewMenu
    {
        private const string Root = "Lumenfall/Secrets/Glitch Aura/";
        private static readonly Vector2Int ReceptionCell = new Vector2Int(9, 5);

        [MenuItem(Root + "Off", false, 0)]
        private static void Off() => Set(GlitchAuraLevel.Off);

        [MenuItem(Root + "Hint", false, 1)]
        private static void Hint() => Set(GlitchAuraLevel.Hint);

        [MenuItem(Root + "Lingering", false, 2)]
        private static void Lingering() => Set(GlitchAuraLevel.Lingering);

        [MenuItem(Root + "Full", false, 3)]
        private static void Full() => Set(GlitchAuraLevel.Full);

        [MenuItem(Root + "Off", true)]
        private static bool CheckOff() => Check("Off", GlitchAuraLevel.Off);

        [MenuItem(Root + "Hint", true)]
        private static bool CheckHint() => Check("Hint", GlitchAuraLevel.Hint);

        [MenuItem(Root + "Lingering", true)]
        private static bool CheckLingering() => Check("Lingering", GlitchAuraLevel.Lingering);

        [MenuItem(Root + "Full", true)]
        private static bool CheckFull() => Check("Full", GlitchAuraLevel.Full);

        [MenuItem(Root + "Go To Dorm Reception", false, 20)]
        private static void GoToReception()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            if (loader == null || !loader.TravelTo(SettlingInScene.MapId, ReceptionCell))
                Debug.LogWarning("Could not travel to the dorm reception. Start or load a game first, and stand still.");
        }

        [MenuItem(Root + "Go To Dorm Reception", true)]
        private static bool CanGoToReception() => Application.isPlaying && DataDrivenWorldLoader.Instance != null;

        /// <summary>
        /// Skips the chapter up to the key glitch: Marigold has sent the player after the key, every favour is done, all
        /// four dormmates have been asked and the player has decided to ask her. Then talk to Marigold to play the scene.
        /// Use a throwaway save, since this overwrites the story's progress.
        /// </summary>
        [MenuItem(Root + "Set Up Key Glitch Scene", false, 21)]
        private static void SetUpKeyGlitch()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null)
            {
                Debug.LogWarning("Start or load a game first.");
                return;
            }
            runtime.SetFlag(SettlingInScene.BannMetFlag, true);
            // Before the favours, so finishing them all is not taken as solving everything before visiting the dorm.
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            runtime.SetFlag(NpcIntroductions.MetFlag(SettlingInScene.ReceptionistNpcId), true);
            foreach (string quest in SettlingInScene.ChapterQuestIds)
            {
                if (runtime.GetQuestStatus(quest) == QuestStatus.NotStarted) runtime.StartQuest(quest);
                if (runtime.GetQuestStatus(quest) != QuestStatus.Completed) runtime.CompleteQuest(quest);
            }
            foreach ((string person, _) in SettlingInScene.Dormmates) runtime.SetFlag(SettlingInScene.KeyAskedFlag(person), true);
            runtime.SetFlag(SettlingInScene.ThoughtFlag, true);
            runtime.SetFlag(CutsceneDefinition.PlayedFlagFor(SettlingInScene.ThoughtCutsceneId), true);
            GoToReception();
            Debug.Log("Key glitch ready: talk to Marigold.");
        }

        [MenuItem(Root + "Set Up Key Glitch Scene", true)]
        private static bool CanSetUpKeyGlitch() => CanGoToReception();

        private static void Set(GlitchAuraLevel level)
        {
            GlitchAuraPreview.PreviewLevel = level;
            Debug.Log($"Glitch aura preview on Marigold: {level}.");
        }

        private static bool Check(string item, GlitchAuraLevel level)
        {
            Menu.SetChecked(Root + item, GlitchAuraPreview.PreviewLevel == level);
            return true;
        }
    }
}
