using System;
using Lumenfall.GameEvents;
using UnityEngine;

namespace Lumenfall.Player
{
    /// <summary>
    /// The player character's name. A new game starts unnamed, shown as <see cref="UnknownName"/>, until the story asks
    /// for a name (<see cref="PlayerNamePrompt"/>); leaving it blank gives <see cref="DefaultName"/>. Dialogue writes
    /// <see cref="Token"/> as a speaker, in a line or in a choice to mean the player. The name is saved with the game
    /// under the provider key <c>player-profile</c>; a save without one loads as unnamed.
    /// </summary>
    public static class PlayerProfile
    {
        public const string Token = "{player}";
        public const string UnknownName = "???";
        public const string DefaultName = "Sol";
        public const int MaxNameLength = 16;

        private static Provider provider;
        private static bool restoredThisLoad;

        /// <summary>The chosen name, or null while the player is still unnamed.</summary>
        public static string Name { get; private set; }

        public static bool HasName => !string.IsNullOrEmpty(Name);

        /// <summary>What dialogue shows for the player: their name, or <see cref="UnknownName"/> until they have one.</summary>
        public static string DisplayName => HasName ? Name : UnknownName;

        public static event Action NameChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Name = null;
            provider = null;
            restoredThisLoad = false;
            NameChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves == null || provider != null) return;
            provider = new Provider();
            // A reloaded scene keeps the manager, so an earlier registration is replaced rather than duplicated.
            saves.UnregisterProvider(provider.SaveKey);
            saves.RegisterProvider(provider);
            saves.LoadCompleted += OnLoadCompleted;
        }

        /// <summary>Sets the player's name, trimmed and shortened to <see cref="MaxNameLength"/>; a blank name becomes <see cref="DefaultName"/>.</summary>
        public static void SetName(string name)
        {
            string cleaned = Clean(name);
            Apply(string.IsNullOrEmpty(cleaned) ? DefaultName : cleaned);
        }

        /// <summary>Returns the player to unnamed, as at the start of a new game.</summary>
        public static void Forget() => Apply(null);

        /// <summary>Replaces every <see cref="Token"/> in <paramref name="text"/> with <see cref="DisplayName"/>.</summary>
        public static string Substitute(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Token, StringComparison.Ordinal) < 0) return text;
            return text.Replace(Token, DisplayName);
        }

        /// <summary>True when a dialogue speaker means the player.</summary>
        public static bool IsPlayerSpeaker(string speaker) =>
            string.Equals(speaker?.Trim(), Token, StringComparison.OrdinalIgnoreCase);

        /// <summary>Trims, removes line breaks and rich-text brackets, and shortens a typed name.</summary>
        public static string Clean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            System.Text.StringBuilder cleaned = new System.Text.StringBuilder(name.Length);
            foreach (char character in name.Trim())
            {
                if (char.IsControl(character) || character == '<' || character == '>' || character == '{' || character == '}') continue;
                cleaned.Append(character);
            }
            string result = cleaned.ToString().Trim();
            return result.Length > MaxNameLength ? result.Substring(0, MaxNameLength).TrimEnd() : result;
        }

        private static void Apply(string name)
        {
            if (string.Equals(Name, name, StringComparison.Ordinal)) return;
            Name = name;
            NameChanged?.Invoke();
        }

        // Saves made before the profile existed have no entry, so a load that did not restore one leaves the player unnamed.
        private static void OnLoadCompleted(string slot)
        {
            if (!restoredThisLoad) Forget();
            restoredThisLoad = false;
        }

        [Serializable]
        private sealed class State
        {
            public string name;
        }

        private sealed class Provider : ISaveStateProvider
        {
            public string SaveKey => "player-profile";

            public string CaptureStateJson() => JsonUtility.ToJson(new State { name = Name ?? string.Empty });

            public bool RestoreStateJson(string json)
            {
                State state = string.IsNullOrWhiteSpace(json) ? new State() : JsonUtility.FromJson<State>(json);
                string cleaned = Clean(state?.name);
                Apply(string.IsNullOrEmpty(cleaned) ? null : cleaned);
                restoredThisLoad = true;
                return true;
            }
        }
    }
}
