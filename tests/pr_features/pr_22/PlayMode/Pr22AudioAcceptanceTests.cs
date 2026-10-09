using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumenfall.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR22")]
    public sealed class Pr22AudioAcceptanceTests
    {
        private GameObject owner; private AudioClip clip; private AudioClip replacement;
        private readonly List<object> channels = new List<object>();
        private static readonly Type Channel = typeof(AreaAudioController).GetNestedType("LoopChannel", BindingFlags.NonPublic);
        [SetUp] public void SetUp()
        {
            owner = new GameObject("PR22 isolated audio");
            clip = AudioClip.Create("PR22 loop", 44100, 1, 44100, false);
            replacement = AudioClip.Create("PR22 replacement", 44100, 1, 44100, false);
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(owner); Object.DestroyImmediate(clip); Object.DestroyImmediate(replacement); channels.Clear();
        }
        private object NewChannel() { object channel = Activator.CreateInstance(Channel, new object[] { owner }); channels.Add(channel); return channel; }
        private static void Set(object channel, AudioClip value, float volume, float duration) => Channel.GetMethod("SetClip").Invoke(channel, new object[] { value, volume, null, duration });
        private static void Tick(object channel, float time) => Channel.GetMethod("Tick").Invoke(channel, new object[] { time });
        private AudioSource[] Sources => owner.GetComponents<AudioSource>();
        [Test] public void IdenticalClipInTwoChannelsKeepsIndependentGainsAndStopsIndependently()
        {
            object music = NewChannel(), ambience = NewChannel();
            Set(music, clip, 0.8f, 0); Set(ambience, clip, 0.2f, 0);
            Assert.That(Sources.Count(s => s.clip == clip), Is.EqualTo(2));
            Assert.That(Sources.Where(s => s.clip == clip).Select(s => s.volume).OrderBy(v => v), Is.EqualTo(new[] { 0.2f, 0.8f }));
            Channel.GetMethod("Stop").Invoke(music, null);
            Assert.That(Sources.Single(s => s.clip != null).volume, Is.EqualTo(0.2f));
            Assert.That(Sources.Single(s => s.clip != null).isPlaying, Is.True);
        }
        [Test] public void LiveGainChangeReusesPlayingSourceAndInterpolatesFromItsCurrentVolume()
        {
            object channel = NewChannel(); Set(channel, clip, 0.8f, 0);
            var source = Sources.Single(s => s.clip == clip); source.timeSamples = 12000;
            Set(channel, clip, 0.2f, 2); Tick(channel, 1);
            Assert.That(Sources.Single(s => s.clip == clip), Is.SameAs(source));
            Assert.That(source.volume, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(source.timeSamples, Is.GreaterThanOrEqualTo(12000));
            Tick(channel, 1); Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(Sources.Count(s => s.clip != null), Is.EqualTo(1));
        }
        [Test] public void NegativeFadeAndGainSwitchImmediatelyWithoutLeavingThePreviousLoop()
        {
            object channel = NewChannel(); Set(channel, clip, 0.8f, 0);
            Set(channel, replacement, -1, -1);
            Assert.That(Sources.All(s => s.clip == null && s.volume == 0), Is.True);
            Set(channel, replacement, 0.6f, -1);
            Assert.That(Sources.Single(s => s.clip != null).clip, Is.SameAs(replacement));
            Assert.That(Sources.Single(s => s.clip != null).volume, Is.EqualTo(0.6f));
        }
        [UnityTest] public IEnumerator DisposingOneChannelLeavesSiblingSourcesPlaying()
        {
            object first = NewChannel(), second = NewChannel();
            Set(first, clip, 0.5f, 0); Set(second, replacement, 0.6f, 0);
            Channel.GetMethod("Dispose").Invoke(first, null);
            yield return null;
            Assert.That(Sources.Length, Is.EqualTo(2));
            Assert.That(Sources.Single(s => s.clip != null).clip, Is.SameAs(replacement));
            Assert.That(Sources.Single(s => s.clip != null).isPlaying, Is.True);
        }
    }
}
