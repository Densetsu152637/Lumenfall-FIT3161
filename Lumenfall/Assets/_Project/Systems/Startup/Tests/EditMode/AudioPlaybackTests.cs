using Lumenfall.Audio;
using Lumenfall.Audio.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Lumenfall.Startup.Tests
{
    public sealed class AudioPlaybackTests
    {
        [Test]
        public void EditModeRunnerSuppressesPlaybackWithoutChangingMix()
        {
            Assert.That(AudioPlayback.IsSuppressed, Is.True, "The real test runner must arm the playback boundary.");
            float master = AudioListener.volume;
            float sfx = AudioMixSettings.Sfx;
            var host = new GameObject("Suppressed audio regression");
            var clip = AudioClip.Create("Silent test clip", 44100, 1, 44100, false);
            try
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.clip = clip;
                source.volume = 0.37f;
                AudioPlayback.Play(source);
                AudioPlayback.PlayOneShot(source, clip);
                AudioPlayback.UnPause(source);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(0.37f));
                Assert.That(AudioListener.volume, Is.EqualTo(master));
                Assert.That(AudioMixSettings.Sfx, Is.EqualTo(sfx));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void CancelledRunCannotSuppressNextOrdinaryPlaySession()
        {
            bool previous = AudioPlayback.IsSuppressed;
            var original = SceneManager.GetActiveScene();
            var guard = new AudioTestPlaybackGuard();
            try
            {
                guard.Setup();
                AudioTestPlaybackGuard.OnPlayModeChanged(PlayModeStateChange.ExitingEditMode, original.path, original.handle);
                Assert.That(AudioPlayback.IsSuppressed, Is.True, "Prepared test bootstrap must stay suppressed.");
                // Test Runner cancellation restores the prior scene instead of calling postbuild cleanup.
                AudioTestPlaybackGuard.OnPlayModeChanged(PlayModeStateChange.ExitingEditMode,
                    "Assets/RestoredDeveloperScene.unity", original.handle + 1);
                Assert.That(AudioPlayback.IsSuppressed, Is.False, "A different restored scene must allow normal play.");
                guard.Setup();
                AudioTestPlaybackGuard.OnPlayModeChanged(PlayModeStateChange.EnteredEditMode, original.path, original.handle);
                Assert.That(AudioPlayback.IsSuppressed, Is.False, "PlayMode exit/cancel releases suppression.");
            }
            finally
            {
                guard.Setup();
                SessionState.SetBool(AudioPlayback.TestRunSessionKey, previous);
            }
        }

        [Test]
        public void FinishedAndFailedRunsRestoreNormalPlaybackPolicy()
        {
            bool previous = AudioPlayback.IsSuppressed;
            var guard = new AudioTestPlaybackGuard();
            try
            {
                guard.RunStarted(null);
                Assert.That(AudioPlayback.IsSuppressed, Is.True);
                guard.RunFinished(null);
                Assert.That(AudioPlayback.IsSuppressed, Is.False, "Normal Editor playback must be allowed after a completed EditMode run.");
                guard.Setup();
                guard.OnError("Simulated prebuild failure");
                Assert.That(AudioPlayback.IsSuppressed, Is.False);
                guard.Setup(); guard.Cleanup(); guard.Cleanup();
                Assert.That(AudioPlayback.IsSuppressed, Is.False, "Cleanup is idempotent.");
            }
            finally { SessionState.SetBool(AudioPlayback.TestRunSessionKey, previous); }
        }
    }
}
