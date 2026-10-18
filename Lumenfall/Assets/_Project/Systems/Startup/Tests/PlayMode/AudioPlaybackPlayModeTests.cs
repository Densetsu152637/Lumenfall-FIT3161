using System.Collections;
using Lumenfall.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Startup.Tests
{
    public sealed class AudioPlaybackPlayModeTests
    {
        private static bool suppressedBeforeSceneLoad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureBootstrapPolicy() => suppressedBeforeSceneLoad = AudioPlayback.IsSuppressed;

        [UnityTest]
        public IEnumerator PlayModeBootstrapAndNativePlaybackStaySilent()
        {
            Assert.That(suppressedBeforeSceneLoad, Is.True, "Suppression must precede service/scene bootstrap.");
            Assert.That(AudioPlayback.IsSuppressed, Is.True, "Prebuild suppression must survive entering PlayMode.");
            Assert.That(AudioFeedbackService.Instance, Is.Not.Null);
            var host = new GameObject("Suppressed playback regression");
            var clip = AudioClip.Create("Silent test clip", 44100, 1, 44100, false);
            try
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false; source.clip = clip; source.volume = 0.37f;
                AudioPlayback.Play(source); AudioPlayback.PlayOneShot(source, clip); AudioPlayback.UnPause(source);
                yield return null;
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(0.37f));
                foreach (AudioSource owned in AudioFeedbackService.Instance.GetComponentsInChildren<AudioSource>())
                    Assert.That(owned.isPlaying, Is.False, owned.name + " must not start during tests.");
            }
            finally { Object.Destroy(host); Object.Destroy(clip); }
        }
    }
}
