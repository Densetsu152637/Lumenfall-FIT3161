using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lumenfall.Startup.Tests")]

namespace Lumenfall.Audio.Editor
{
    /// <summary>Test-run lifetime, including the scene startup before PlayMode RunStarted.</summary>
    [InitializeOnLoad]
    public sealed class AudioTestPlaybackGuard : IPrebuildSetup, IPostBuildCleanup, IErrorCallbacks
    {
        static AudioTestPlaybackGuard()
        {
            // Callback registrations are lost on domain reload; SessionState is not.
            TestRunnerApi.RegisterTestCallback(new AudioTestPlaybackGuard());
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private const string PreparedScenePathKey = "Lumenfall.Audio.TestScenePath";
        private const string PreparedSceneHandleKey = "Lumenfall.Audio.TestSceneHandle";

        public void Setup()
        {
            var scene = SceneManager.GetActiveScene();
            SessionState.SetString(PreparedScenePathKey, scene.path);
            SessionState.SetInt(PreparedSceneHandleKey, scene.handle);
            SetActive(true);
        }
        public void Cleanup() => SetActive(false);
        public void RunStarted(ITestAdaptor testsToRun) => SetActive(true);
        public void RunFinished(ITestResultAdaptor result)
        {
            // PlayMode's runner scene still updates until the Editor exits play.
            if (!EditorApplication.isPlaying) SetActive(false);
        }
        public void OnError(string message)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) SetActive(false);
        }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        internal static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) SetActive(false);
            if (state != PlayModeStateChange.ExitingEditMode || !AudioPlayback.IsSuppressed) return;
            // Cancellation can skip PostBuildCleanup. A subsequent ordinary play session
            // starts from the restored developer scene, not the prepared test scene.
            var scene = SceneManager.GetActiveScene();
            string preparedPath = SessionState.GetString(PreparedScenePathKey, "");
            bool prepared = string.IsNullOrEmpty(preparedPath)
                ? scene.handle == SessionState.GetInt(PreparedSceneHandleKey, -1)
                : scene.path == preparedPath;
            if (!prepared) SetActive(false);
        }

        private static void SetActive(bool active)
        {
            SessionState.SetBool(AudioPlayback.TestRunSessionKey, active);
        }
    }
}
