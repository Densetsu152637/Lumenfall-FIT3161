using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>The world-facing side of a cutscene: everything a step does outside the screen overlay.</summary>
    public interface ICutsceneHost
    {
        void SetFlag(string key, bool value);
        /// <summary>Opens a dialogue; returns false if it could not start, so the cutscene does not wait for it.</summary>
        bool StartDialogue(string dialogueId);
        bool IsDialogueActive { get; }
        void EndDialogue();
        void FocusCamera(Vector3 offset, float zoom);
        void ReleaseCamera();
        void StartStar(float seconds);
        /// <summary>Hides the player and gathers light onto them; returns false if there is nothing to wait for.</summary>
        bool StartArrival();
        bool IsArrivalGathering { get; }
        void RevealPlayer();
        /// <summary>Opens the player-name prompt; returns false if it could not open, so the cutscene does not wait.</summary>
        bool StartNamePrompt();
        bool IsNamePromptOpen { get; }
        /// <summary>
        /// Starts walking the player to a cell on the current map, or, with <paramref name="npcId"/>, to that offset from
        /// where the NPC stands. Returns false if there is no player to walk or no such NPC.
        /// </summary>
        bool StartWalk(Vector2Int cell, bool hurry, string npcId);
        bool IsWalking { get; }
        /// <summary>Takes the player through the portal at a cell, moving them onto it first if needed.</summary>
        bool EnterPortal(Vector2Int cell);
        /// <summary>
        /// Ends the star and the arrival at once, leaving the player visible, accepts an open name prompt, and finishes a
        /// walk by placing the player at its destination.
        /// </summary>
        void StopEffects();
    }

    /// <summary>
    /// Plays a <see cref="CutsceneDefinition"/> step by step. It owns the screen overlay, caption and shake values for
    /// <see cref="CutscenePlayer"/> to draw, and asks an <see cref="ICutsceneHost"/> for everything in the world. It has
    /// no scene dependencies and is driven by <see cref="Advance"/>, so it can be tested without Play Mode.
    /// </summary>
    public sealed class CutsceneSequence
    {
        /// <summary>A caption takes this long to fade in and to fade out, or a quarter of its time if shorter.</summary>
        public const float CaptionFadeSeconds = 0.6f;

        private readonly CutsceneDefinition definition;
        private readonly ICutsceneHost host;
        private int nextStep;
        private float waitRemaining;
        private bool waitingForDialogue;
        private bool waitingForArrival;
        private bool waitingForName;
        private bool waitingForWalk;

        private Color fadeFromColor, fadeToColor;
        private float fadeFromAlpha, fadeToAlpha, fadeSeconds, fadeElapsed;
        private bool fading;

        private float captionSeconds, captionElapsed;
        private float shakeAmplitude, shakeSeconds, shakeElapsed;

        public CutsceneSequence(CutsceneDefinition definition, ICutsceneHost host)
        {
            this.definition = definition;
            this.host = host;
            OverlayColor = Color.black;
        }

        public CutsceneDefinition Definition => definition;
        public bool IsFinished { get; private set; }
        public bool WasSkipped { get; private set; }
        public bool CanSkip => !definition.unskippable && !IsFinished;

        /// <summary>The colour and opacity of the full-screen overlay, drawn over the world.</summary>
        public Color OverlayColor { get; private set; }
        public float OverlayAlpha { get; private set; }

        /// <summary>The caption on screen, and its opacity; empty when there is none.</summary>
        public string Caption { get; private set; } = string.Empty;
        public float CaptionAlpha { get; private set; }

        /// <summary>How hard the camera should shake this frame, in world units.</summary>
        public float ShakeStrength => shakeSeconds <= 0f || shakeElapsed >= shakeSeconds
            ? 0f : shakeAmplitude * (1f - shakeElapsed / shakeSeconds);

        /// <summary>Moves the cutscene on by <paramref name="deltaTime"/> seconds, starting each step as the one before it ends.</summary>
        public void Advance(float deltaTime)
        {
            if (IsFinished) return;
            deltaTime = Mathf.Max(0f, deltaTime);
            TickTracks(deltaTime);

            float remaining = deltaTime;
            // Steps without a wait (camera, flags, background effects) all start in the same frame.
            for (int guard = 0; guard < 1000; guard++)
            {
                if (waitingForDialogue)
                {
                    if (host.IsDialogueActive) return;
                    waitingForDialogue = false;
                }

                if (waitingForArrival)
                {
                    if (host.IsArrivalGathering) return;
                    waitingForArrival = false;
                }

                if (waitingForName)
                {
                    if (host.IsNamePromptOpen) return;
                    waitingForName = false;
                }

                if (waitingForWalk)
                {
                    if (host.IsWalking) return;
                    waitingForWalk = false;
                }

                if (waitRemaining > 0f)
                {
                    if (remaining < waitRemaining)
                    {
                        waitRemaining -= remaining;
                        return;
                    }
                    remaining -= waitRemaining;
                    waitRemaining = 0f;
                }

                if (nextStep >= definition.steps.Length)
                {
                    IsFinished = true;
                    return;
                }

                Begin(definition.steps[nextStep++], remaining);
            }
        }

        /// <summary>
        /// Ends the cutscene at once. Flags from steps that had not run yet are still set, so skipping leaves the game in
        /// the same state as watching; dialogue, effects and the camera are released, the player is shown, and the
        /// overlay is cleared.
        /// </summary>
        public void Skip()
        {
            if (IsFinished) return;
            for (int i = nextStep; i < definition.steps.Length; i++)
            {
                CutsceneStep step = definition.steps[i];
                if (step != null && step.Kind == CutsceneStepKind.Flag) host.SetFlag(step.key.Trim(), step.value);
            }
            nextStep = definition.steps.Length;
            if (waitingForDialogue && host.IsDialogueActive) host.EndDialogue();
            waitingForDialogue = false;
            waitingForArrival = false;
            waitingForName = false;
            waitingForWalk = false;
            waitRemaining = 0f;
            host.StopEffects();
            host.ReleaseCamera();
            fading = false;
            OverlayAlpha = 0f;
            Caption = string.Empty;
            CaptionAlpha = 0f;
            captionSeconds = 0f;
            shakeSeconds = 0f;
            WasSkipped = true;
            IsFinished = true;
        }

        // A step that starts part-way through a frame is already 'elapsed' seconds old, so its effect and its wait stay in step.
        private void Begin(CutsceneStep step, float elapsed)
        {
            float seconds = Mathf.Max(0f, step.seconds);
            float wait = step.background ? 0f : seconds;
            switch (step.Kind)
            {
                case CutsceneStepKind.Wait:
                    waitRemaining = seconds;
                    break;
                case CutsceneStepKind.Fade:
                    fadeFromColor = OverlayColor;
                    fadeFromAlpha = OverlayAlpha;
                    fadeToColor = !string.IsNullOrEmpty(step.color) && ColorUtility.TryParseHtmlString(step.color, out Color parsed)
                        ? new Color(parsed.r, parsed.g, parsed.b, 1f) : OverlayColor;
                    fadeToAlpha = Mathf.Clamp01(step.alpha);
                    fadeSeconds = seconds;
                    fadeElapsed = elapsed;
                    fading = true;
                    ApplyFade(seconds <= 0f ? 1f : Mathf.Clamp01(elapsed / seconds));
                    waitRemaining = wait;
                    break;
                case CutsceneStepKind.Caption:
                    Caption = step.text ?? string.Empty;
                    captionSeconds = seconds;
                    captionElapsed = elapsed;
                    UpdateCaption();
                    waitRemaining = wait;
                    break;
                case CutsceneStepKind.Star:
                    host.StartStar(seconds);
                    waitRemaining = wait;
                    break;
                case CutsceneStepKind.Arrive:
                    waitingForArrival = host.StartArrival() && !step.background;
                    break;
                case CutsceneStepKind.Reveal:
                    host.RevealPlayer();
                    break;
                case CutsceneStepKind.Shake:
                    shakeAmplitude = Mathf.Max(0f, step.amplitude);
                    shakeSeconds = seconds;
                    shakeElapsed = elapsed;
                    waitRemaining = wait;
                    break;
                case CutsceneStepKind.Camera:
                    host.FocusCamera(step.Offset, step.CameraZoom);
                    break;
                case CutsceneStepKind.CameraRelease:
                    host.ReleaseCamera();
                    break;
                case CutsceneStepKind.Dialogue:
                    waitingForDialogue = !string.IsNullOrWhiteSpace(step.key) && host.StartDialogue(step.key.Trim());
                    break;
                case CutsceneStepKind.Name:
                    waitingForName = host.StartNamePrompt();
                    break;
                case CutsceneStepKind.Walk:
                    waitingForWalk = host.StartWalk(step.Cell, step.value, step.key?.Trim()) && !step.background;
                    break;
                case CutsceneStepKind.Enter:
                    host.EnterPortal(step.Cell);
                    break;
                case CutsceneStepKind.Flag:
                    if (!string.IsNullOrWhiteSpace(step.key)) host.SetFlag(step.key.Trim(), step.value);
                    break;
            }
        }

        private void TickTracks(float deltaTime)
        {
            if (fading)
            {
                fadeElapsed += deltaTime;
                ApplyFade(fadeSeconds <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeSeconds));
            }

            if (captionSeconds > 0f)
            {
                captionElapsed += deltaTime;
                UpdateCaption();
            }

            if (shakeSeconds > 0f) shakeElapsed += deltaTime;
        }

        private void UpdateCaption()
        {
            if (captionElapsed >= captionSeconds)
            {
                Caption = string.Empty;
                CaptionAlpha = 0f;
                captionSeconds = 0f;
                return;
            }
            float edge = Mathf.Min(CaptionFadeSeconds, captionSeconds * 0.25f);
            float alpha = edge <= 0f ? 1f : Mathf.Min(captionElapsed / edge, (captionSeconds - captionElapsed) / edge);
            CaptionAlpha = Mathf.Clamp01(alpha);
        }

        private void ApplyFade(float t)
        {
            // Smoothstep so fades ease in and out rather than starting and stopping abruptly.
            float eased = t * t * (3f - 2f * t);
            OverlayColor = Color.Lerp(fadeFromColor, fadeToColor, eased);
            OverlayAlpha = Mathf.Lerp(fadeFromAlpha, fadeToAlpha, eased);
            if (t >= 1f) fading = false;
        }
    }
}
