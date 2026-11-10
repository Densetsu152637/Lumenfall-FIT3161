using Lumenfall.Story;
using UnityEditor;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Plays the 3D prologue (<see cref="PrologueScene"/>) for review before it is part of New Game. Outside Play Mode it
    /// enters Play Mode and starts the prologue over the title menu as soon as play begins; in Play Mode it starts at
    /// once. Esc stops it. It saves nothing and leaves the game as it was.
    /// </summary>
    public static class ProloguePreviewMenu
    {
        private const string PendingKey = "Lumenfall.ProloguePreview.Pending";

        [MenuItem("Lumenfall/Story/Preview Prologue")]
        public static void Preview()
        {
            if (EditorApplication.isPlaying)
            {
                PrologueScene.Play();
                return;
            }
            // Play Mode reloads scripts, so the request is kept in the editor session until play has begun. It is
            // timestamped: if play never starts (compile errors), a later, ordinary Play does not pick it up.
            SessionState.SetFloat(PendingKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        private const float PendingSeconds = 60f;

        [MenuItem("Lumenfall/Story/Stop Prologue Preview")]
        public static void StopPreview() => PrologueScene.Stop();

        [MenuItem("Lumenfall/Story/Stop Prologue Preview", true)]
        private static bool CanStopPreview() => EditorApplication.isPlaying && PrologueScene.IsPlaying;

        [InitializeOnLoadMethod]
        private static void ListenForPlayMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            float requested = SessionState.GetFloat(PendingKey, -1f);
            if (requested < 0f) return;
            SessionState.EraseFloat(PendingKey);
            if (EditorApplication.timeSinceStartup - requested <= PendingSeconds) PrologueScene.Play();
        }
    }
}
