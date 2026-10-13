using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// A cutscene authored as JSON under <c>Resources/Cutscenes</c>: an ordered list of steps that
    /// <see cref="CutsceneSequence"/> plays one after another.
    /// </summary>
    [Serializable]
    public sealed class CutsceneDefinition
    {
        public string id;
        /// <summary>Plays at most once per game; finishing or skipping sets <c>cutscene.played.&lt;id&gt;</c>.</summary>
        public bool once;
        /// <summary>Set to stop Escape skipping the cutscene.</summary>
        public bool unskippable;
        public CutsceneStep[] steps = Array.Empty<CutsceneStep>();

        public string PlayedFlag => PlayedFlagFor(id);

        public static string PlayedFlagFor(string cutsceneId) => "cutscene.played." + cutsceneId;

        /// <summary>Reads a cutscene from JSON. Returns null, with the problems listed, if it cannot be played.</summary>
        public static CutsceneDefinition Parse(string json, List<string> problems)
        {
            CutsceneDefinition definition;
            try
            {
                definition = JsonUtility.FromJson<CutsceneDefinition>(json);
            }
            catch (ArgumentException exception)
            {
                problems?.Add("Invalid cutscene JSON: " + exception.Message);
                return null;
            }

            if (definition == null)
            {
                problems?.Add("The cutscene JSON is empty.");
                return null;
            }

            int before = problems?.Count ?? 0;
            Validate(definition, problems);
            return problems == null || problems.Count == before ? definition : null;
        }

        /// <summary>Loads <c>Resources/Cutscenes/&lt;id&gt;.json</c>. Returns null and logs a warning if it is missing or invalid.</summary>
        public static CutsceneDefinition Load(string cutsceneId)
        {
            if (string.IsNullOrWhiteSpace(cutsceneId)) return null;
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + cutsceneId.Trim());
            if (asset == null)
            {
                Debug.LogWarning($"Cutscene '{cutsceneId}' was not found in Resources/Cutscenes.");
                return null;
            }

            List<string> problems = new List<string>();
            CutsceneDefinition definition = Parse(asset.text, problems);
            if (definition == null) Debug.LogWarning($"Cutscene '{cutsceneId}' cannot play: {string.Join(" ", problems)}");
            return definition;
        }

        private static void Validate(CutsceneDefinition definition, List<string> problems)
        {
            if (problems == null) return;
            if (string.IsNullOrWhiteSpace(definition.id)) problems.Add("The cutscene has no id.");
            if (definition.steps == null || definition.steps.Length == 0)
            {
                problems.Add("The cutscene has no steps.");
                return;
            }

            for (int i = 0; i < definition.steps.Length; i++)
            {
                CutsceneStep step = definition.steps[i];
                string label = $"Step {i + 1}";
                if (step == null || !CutsceneStep.IsKnownType(step.type))
                {
                    problems.Add($"{label} has an unknown type '{step?.type}'.");
                    continue;
                }
                if (step.seconds < 0f) problems.Add($"{label} has negative seconds.");
                switch (step.Kind)
                {
                    case CutsceneStepKind.Fade:
                        if (step.alpha < 0f || step.alpha > 1f) problems.Add($"{label} fades to an alpha outside 0-1.");
                        if (!string.IsNullOrEmpty(step.color) && !ColorUtility.TryParseHtmlString(step.color, out _))
                            problems.Add($"{label} has an unreadable colour '{step.color}'.");
                        break;
                    case CutsceneStepKind.Caption:
                        if (string.IsNullOrWhiteSpace(step.text)) problems.Add($"{label} is a caption with no text.");
                        break;
                    case CutsceneStepKind.Dialogue:
                    case CutsceneStepKind.Flag:
                        if (string.IsNullOrWhiteSpace(step.key)) problems.Add($"{label} needs a key.");
                        break;
                    case CutsceneStepKind.Star:
                        if (step.seconds <= 0f) problems.Add($"{label} is a star with no duration.");
                        break;
                    case CutsceneStepKind.Walk:
                    case CutsceneStepKind.Enter:
                        // A walk to an NPC takes an offset, which may be negative.
                        bool offset = step.Kind == CutsceneStepKind.Walk && !string.IsNullOrWhiteSpace(step.key);
                        if (!offset && (step.x < 0f || step.y < 0f)) problems.Add($"{label} names a cell outside the map.");
                        break;
                }
            }
        }
    }

    public enum CutsceneStepKind { Unknown, Wait, Fade, Caption, Star, Arrive, Reveal, Shake, Camera, CameraRelease, Dialogue, Name, Walk, Enter, Flag }

    /// <summary>
    /// One cutscene step. Fields a step type does not use are ignored:
    /// <list type="bullet">
    /// <item><c>wait</c>: pause for <c>seconds</c>.</item>
    /// <item><c>fade</c>: ease the screen overlay to <c>alpha</c> (and <c>color</c>, if given) over <c>seconds</c>.</item>
    /// <item><c>caption</c>: show <c>text</c> centred on screen for <c>seconds</c>, fading in and out.</item>
    /// <item><c>star</c>: a falling star dives at the player and lands after <c>seconds</c>.</item>
    /// <item><c>arrive</c>: the player is hidden while light gathers on them (continuing the startup menu's dissolve when
    /// the cutscene follows New Game); waits until it has gathered.</item>
    /// <item><c>reveal</c>: show the hidden player with a burst of light; usually just after a flash.</item>
    /// <item><c>shake</c>: shake the camera by <c>amplitude</c>, dying away over <c>seconds</c>.</item>
    /// <item><c>camera</c>: hold the camera on the player plus <c>x</c>, <c>y</c>, <c>z</c> at <c>zoom</c>.</item>
    /// <item><c>cameraRelease</c>: hand the camera back to normal following.</item>
    /// <item><c>dialogue</c>: open dialogue <c>key</c> and wait until it ends.</item>
    /// <item><c>name</c>: ask the player for their name (blank gives the default) and wait for the answer.</item>
    /// <item><c>walk</c>: the game walks the player to cell <c>x</c>, <c>y</c> on the current map and waits until they
    /// arrive; <c>value</c> true makes them hurry. With <c>key</c> set to an NPC id, <c>x</c>, <c>y</c> is instead an
    /// offset from wherever that NPC stands, such as 0, -2 for two cells in front of them.</item>
    /// <item><c>enter</c>: take the player through the portal at cell <c>x</c>, <c>y</c>.</item>
    /// <item><c>flag</c>: set GameEvents flag <c>key</c> to <c>value</c>. Skipping still applies it.</item>
    /// </list>
    /// <c>background</c> lets a timed step (wait, fade, caption, star, shake) run on while the next steps start.
    /// </summary>
    [Serializable]
    public sealed class CutsceneStep
    {
        public string type;
        public float seconds;
        public float alpha;
        public string color;
        public string text;
        public string key;
        public bool value;
        public float x;
        public float y;
        public float z;
        public float zoom;
        public float amplitude;
        public bool background;

        public CutsceneStepKind Kind => KindOf(type);

        public Vector3 Offset => new Vector3(x, y, z);

        /// <summary>The map cell a walk or enter step names.</summary>
        public Vector2Int Cell => new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));

        /// <summary>The camera zoom, as a fraction of the normal follow distance; zero or missing means 1.</summary>
        public float CameraZoom => zoom <= 0f ? 1f : zoom;

        public static bool IsKnownType(string type) => KindOf(type) != CutsceneStepKind.Unknown;

        public static CutsceneStepKind KindOf(string type)
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wait": return CutsceneStepKind.Wait;
                case "fade": return CutsceneStepKind.Fade;
                case "caption": return CutsceneStepKind.Caption;
                case "star": return CutsceneStepKind.Star;
                case "arrive": return CutsceneStepKind.Arrive;
                case "reveal": return CutsceneStepKind.Reveal;
                case "shake": return CutsceneStepKind.Shake;
                case "camera": return CutsceneStepKind.Camera;
                case "camerarelease": return CutsceneStepKind.CameraRelease;
                case "dialogue": return CutsceneStepKind.Dialogue;
                case "name": return CutsceneStepKind.Name;
                case "walk": return CutsceneStepKind.Walk;
                case "enter": return CutsceneStepKind.Enter;
                case "flag": return CutsceneStepKind.Flag;
                default: return CutsceneStepKind.Unknown;
            }
        }
    }
}
