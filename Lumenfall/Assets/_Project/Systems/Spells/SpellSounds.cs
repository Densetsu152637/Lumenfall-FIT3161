using System.Collections.Generic;
using Lumenfall.Audio;
using UnityEngine;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Sounds for Rotara and Vis. Rotara plays its turn whenever a cast rotates something. Vis rises as it lifts a relic
    /// (or straightens an object) and falls as it sets one down. Secrets that answer a cast themselves, such as the
    /// campus dog, call <see cref="Play"/> directly. A missing clip is simply silent.
    /// </summary>
    public sealed class SpellSounds : MonoBehaviour
    {
        public const string RotaraPath = "Audio/Rotara/Rotara";
        public const string VisUpPath = "Audio/Vis/Vis Up";
        public const string VisDownPath = "Audio/Vis/Vis Down";
        private const float Gain = 0.8f;
        private static SpellSounds instance;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource source;
        private SpellCaster caster;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Spell Sounds]");
            instance = host.AddComponent<SpellSounds>();
            DontDestroyOnLoad(host);
        }

        /// <summary>Plays the clip at <paramref name="path"/> under Resources once, on the effects bus.</summary>
        public static void Play(string path)
        {
            if (instance != null) instance.PlayClip(path);
        }

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (instance == this) instance = null;
        }

        // The player's SpellCaster is added at runtime, so the subscription is refreshed.
        private void Update()
        {
            if (caster != null) return;
            SpellCaster found = FindAnyObjectByType<SpellCaster>();
            if (found == null) return;
            caster = found;
            caster.CastCompleted += OnCastCompleted;
            caster.VisLifted += OnVisLifted;
            caster.VisPlaced += OnVisPlaced;
        }

        private void Unsubscribe()
        {
            if (caster == null) return;
            caster.CastCompleted -= OnCastCompleted;
            caster.VisLifted -= OnVisLifted;
            caster.VisPlaced -= OnVisPlaced;
            caster = null;
        }

        private void OnCastCompleted(SpellId spell, string targetId, bool changed)
        {
            if (!changed || caster == null) return;
            if (spell == SpellId.Rotara) PlayClip(RotaraPath);
            // Lifting has its own event; this is a cast that straightens an object, which rises into place.
            else if (spell == SpellId.Vis && targetId != caster.HeldRelic) PlayClip(VisUpPath);
        }

        private void OnVisLifted(string id) => PlayClip(VisUpPath);

        private void OnVisPlaced(string id, Vector2Int cell, bool delivered) => PlayClip(VisDownPath);

        private void PlayClip(string path)
        {
            if (source == null || string.IsNullOrEmpty(path)) return;
            if (!clips.TryGetValue(path, out AudioClip clip)) clips[path] = clip = Resources.Load<AudioClip>(path);
            if (clip != null) source.PlayOneShot(clip, Gain * AudioMixSettings.Sfx);
        }
    }
}
