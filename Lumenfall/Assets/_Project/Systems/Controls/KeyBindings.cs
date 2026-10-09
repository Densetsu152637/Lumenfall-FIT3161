using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Lumenfall.Controls
{
    public enum GameAction
    {
        MoveUp, MoveDown, MoveLeft, MoveRight, Sprint, Talk, Interact, SpellWheel, Journal,
        Spell1, Spell2, Spell3, Spell4, Spell5, Spell6, Spell7
    }

    /// <summary>
    /// Player-rebindable keyboard controls, persisted in PlayerPrefs. Gameplay scripts read keys through here
    /// instead of hard-coding them; arrow keys stay as fixed alternates for movement.
    /// </summary>
    public static class KeyBindings
    {
        private const string PrefsPrefix = "lumenfall.keybind.";
        public static readonly GameAction[] AllActions = (GameAction[])Enum.GetValues(typeof(GameAction));

        private static readonly Dictionary<GameAction, Key> Defaults = new Dictionary<GameAction, Key>
        {
            { GameAction.MoveUp, Key.W }, { GameAction.MoveDown, Key.S }, { GameAction.MoveLeft, Key.A }, { GameAction.MoveRight, Key.D },
            { GameAction.Sprint, Key.LeftShift }, { GameAction.Talk, Key.E }, { GameAction.Interact, Key.F },
            { GameAction.SpellWheel, Key.Q }, { GameAction.Journal, Key.Tab },
            { GameAction.Spell1, Key.Digit1 }, { GameAction.Spell2, Key.Digit2 }, { GameAction.Spell3, Key.Digit3 }, { GameAction.Spell4, Key.Digit4 },
            { GameAction.Spell5, Key.Digit5 }, { GameAction.Spell6, Key.Digit6 }, { GameAction.Spell7, Key.Digit7 }
        };

        private static readonly Dictionary<GameAction, Key> FixedAlternates = new Dictionary<GameAction, Key>
        {
            { GameAction.MoveUp, Key.UpArrow }, { GameAction.MoveDown, Key.DownArrow },
            { GameAction.MoveLeft, Key.LeftArrow }, { GameAction.MoveRight, Key.RightArrow }
        };

        private static readonly Dictionary<GameAction, string> DisplayNames = new Dictionary<GameAction, string>
        {
            { GameAction.MoveUp, "Move up" }, { GameAction.MoveDown, "Move down" }, { GameAction.MoveLeft, "Move left" },
            { GameAction.MoveRight, "Move right" }, { GameAction.Sprint, "Sprint" }, { GameAction.Talk, "Talk to NPC" },
            { GameAction.Interact, "Interact / use spell" }, { GameAction.SpellWheel, "Spell wheel" }, { GameAction.Journal, "Journal" },
            { GameAction.Spell1, "Spell slot 1" }, { GameAction.Spell2, "Spell slot 2" }, { GameAction.Spell3, "Spell slot 3" },
            { GameAction.Spell4, "Spell slot 4" }, { GameAction.Spell5, "Spell slot 5" }, { GameAction.Spell6, "Spell slot 6" },
            { GameAction.Spell7, "Spell slot 7" }
        };

        private static Dictionary<GameAction, Key> current;

        private static Dictionary<GameAction, Key> Current
        {
            get
            {
                if (current != null) return current;
                current = new Dictionary<GameAction, Key>(Defaults);
                foreach (GameAction action in AllActions)
                    if (Enum.TryParse(PlayerPrefs.GetString(PrefsPrefix + action, string.Empty), out Key saved) && IsBindable(saved))
                        current[action] = saved;
                return current;
            }
        }

        public static Key GetKey(GameAction action) => Current.TryGetValue(action, out Key key) ? key : Key.None;
        public static string GetDisplayName(GameAction action) => DisplayNames.TryGetValue(action, out string name) ? name : action.ToString();
        public static string GetKeyLabel(GameAction action) => KeyLabel(GetKey(action));

        public static string KeyLabel(Key key)
        {
            if (key == Key.None) return "Unbound";
            KeyControl control = Keyboard.current?[key];
            string label = control != null && !string.IsNullOrWhiteSpace(control.displayName) ? control.displayName : key.ToString();
            return label.Length == 1 ? label.ToUpperInvariant() : label;
        }

        public static bool WasPressed(GameAction action) => Query(action, control => control.wasPressedThisFrame);
        public static bool IsPressed(GameAction action) => Query(action, control => control.isPressed);

        /// <summary>Keyboard movement from the bound keys and arrow keys, as a raw direction vector.</summary>
        public static Vector2 MoveVector()
        {
            float x = (IsPressed(GameAction.MoveRight) ? 1f : 0f) - (IsPressed(GameAction.MoveLeft) ? 1f : 0f);
            float y = (IsPressed(GameAction.MoveUp) ? 1f : 0f) - (IsPressed(GameAction.MoveDown) ? 1f : 0f);
            return new Vector2(x, y);
        }

        /// <summary>Escape cancels rebinding and arrow keys are reserved for movement, so neither can be bound.</summary>
        public static bool IsBindable(Key key)
        {
            if (key == Key.None || key == Key.Escape || key == Key.IMESelected) return false;
            foreach (Key reserved in FixedAlternates.Values) if (key == reserved) return false;
            return true;
        }

        /// <summary>The other action currently bound to <paramref name="key"/>, if any.</summary>
        public static GameAction? ActionUsing(Key key, GameAction except)
        {
            foreach (GameAction other in AllActions)
                if (other != except && GetKey(other) == key) return other;
            return null;
        }

        /// <summary>Binds a key, swapping it with any action that already used it. Returns the swapped action, if any.</summary>
        public static GameAction? SetKey(GameAction action, Key key)
        {
            if (!IsBindable(key)) return null;
            GameAction? swapped = null;
            Key previous = GetKey(action);
            foreach (GameAction other in AllActions)
                if (other != action && GetKey(other) == key) { Current[other] = previous; swapped = other; }
            Current[action] = key;
            Save();
            return swapped;
        }

        public static void ResetToDefaults()
        {
            current = new Dictionary<GameAction, Key>(Defaults);
            Save();
        }

        private static void Save()
        {
            foreach (KeyValuePair<GameAction, Key> pair in Current) PlayerPrefs.SetString(PrefsPrefix + pair.Key, pair.Value.ToString());
            PlayerPrefs.Save();
        }

        private static bool Query(GameAction action, Func<KeyControl, bool> test)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;
            Key key = GetKey(action);
            if (key != Key.None && test(keyboard[key])) return true;
            return FixedAlternates.TryGetValue(action, out Key alternate) && test(keyboard[alternate]);
        }
    }
}
