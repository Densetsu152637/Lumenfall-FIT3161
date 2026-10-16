using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.Audio
{
    [Serializable] public sealed class AudioCueDefinition
    {
        public string id;
        public string resourcePath;
        public string approvalStatus;
        public string bus = "Sfx";
        public bool loop;
        public float gain = 1f;
    }
    [Serializable] public sealed class AudioManifest
    {
        public int schemaVersion = 1;
        public AudioCueDefinition[] cues = Array.Empty<AudioCueDefinition>();
    }
    public enum AudioClipStatus { Ready, MissingApprovedMedia, MissingResource, InvalidConfiguration, UnknownCue }

    /// <summary>Loads each approved resource once; reports explicit authoring status without generating media.</summary>
    public sealed class AudioClipCatalog
    {
        private sealed class Entry { public AudioCueDefinition Definition; public AudioClip Clip; public AudioClipStatus Status; }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public IReadOnlyList<string> ConfigurationIssues { get; }
        public AudioClipCatalog(AudioManifest manifest, Func<string, AudioClip> load)
        {
            List<string> issues = new List<string>();
            if (manifest == null || manifest.schemaVersion != 1) issues.Add("Audio manifest missing or unsupported; playback is disabled.");
            else foreach (AudioCueDefinition cue in manifest.cues ?? Array.Empty<AudioCueDefinition>())
            {
                if (cue == null || string.IsNullOrWhiteSpace(cue.id)) { issues.Add("Audio cue has no ID."); continue; }
                if (entries.ContainsKey(cue.id)) { issues.Add("Duplicate audio cue: " + cue.id); continue; }
                Entry entry = new Entry { Definition = cue, Status = AudioClipStatus.MissingApprovedMedia };
                entries.Add(cue.id, entry);
                if (!Enum.TryParse(cue.bus, false, out AudioBus bus) || bus == AudioBus.Master ||
                    (bus != AudioBus.Music && bus != AudioBus.Ambience && bus != AudioBus.Sfx) ||
                    cue.loop != (bus == AudioBus.Music || bus == AudioBus.Ambience) ||
                    float.IsNaN(cue.gain) || float.IsInfinity(cue.gain) || cue.gain < 0f || cue.gain > 1f)
                    entry.Status = AudioClipStatus.InvalidConfiguration;
                else if (string.Equals(cue.approvalStatus, "approved", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(cue.resourcePath))
                { entry.Clip = load?.Invoke(cue.resourcePath); entry.Status = entry.Clip == null ? AudioClipStatus.MissingResource : AudioClipStatus.Ready; }
                if (entry.Status != AudioClipStatus.Ready) issues.Add(cue.id + ": " + entry.Status);
            }
            ConfigurationIssues = issues.AsReadOnly();
        }
        /// <summary>True when at least one cue on <paramref name="bus"/> has a clip ready to play.</summary>
        public bool AnyReady(AudioBus bus)
        {
            foreach (Entry entry in entries.Values)
                if (entry.Status == AudioClipStatus.Ready && Enum.TryParse(entry.Definition.bus, false, out AudioBus cueBus) && cueBus == bus) return true;
            return false;
        }
        public AudioClipStatus GetStatus(string id) => id != null && entries.TryGetValue(id, out Entry entry) ? entry.Status : AudioClipStatus.UnknownCue;
        public bool TryGetReady(string id, out AudioCueDefinition definition, out AudioClip clip)
        {
            definition = null; clip = null;
            if (id == null || !entries.TryGetValue(id, out Entry entry) || entry.Status != AudioClipStatus.Ready) return false;
            definition = entry.Definition; clip = entry.Clip; return true;
        }
    }
}
