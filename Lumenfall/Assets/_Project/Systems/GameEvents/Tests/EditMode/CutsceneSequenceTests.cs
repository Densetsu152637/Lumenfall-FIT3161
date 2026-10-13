using System.Collections.Generic;
using System.Linq;
using Lumenfall.Cutscenes;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class CutsceneSequenceTests
    {
        private sealed class FakeHost : ICutsceneHost
        {
            public readonly Dictionary<string, bool> Flags = new Dictionary<string, bool>();
            public readonly List<string> Calls = new List<string>();
            /// <summary>Dialogue and the name prompt stay open until a test closes them; otherwise they close at once.</summary>
            public bool HoldOpen;
            public bool DialogueActive;
            public bool Gathering;
            public float StarSeconds = -1f;

            public void SetFlag(string key, bool value) { Flags[key] = value; Calls.Add("flag:" + key); }
            public bool StartDialogue(string dialogueId) { Calls.Add("dialogue:" + dialogueId); DialogueActive = HoldOpen; return true; }
            public bool IsDialogueActive => DialogueActive;
            public void EndDialogue() { Calls.Add("endDialogue"); DialogueActive = false; }
            public void FocusCamera(Vector3 offset, float zoom) => Calls.Add($"camera:{offset.x},{offset.z}@{zoom}");
            public void ReleaseCamera() => Calls.Add("cameraRelease");
            public void StartStar(float seconds) { StarSeconds = seconds; Calls.Add("star"); }
            public bool StartArrival() { Calls.Add("arrive"); return true; }
            public bool IsArrivalGathering => Gathering;
            public void RevealPlayer() => Calls.Add("reveal");
            public bool NamePromptOpen;
            public bool StartNamePrompt() { Calls.Add("name"); NamePromptOpen = HoldOpen; return true; }
            public bool IsNamePromptOpen => NamePromptOpen;
            public bool Walking;
            public bool StartWalk(Vector2Int cell, bool hurry, string npcId) { Calls.Add($"walk:{npcId}{cell.x},{cell.y}{(hurry ? "!" : "")}"); Walking = HoldOpen; return true; }
            public bool IsWalking => Walking;
            public bool EnterPortal(Vector2Int cell) { Calls.Add($"enter:{cell.x},{cell.y}"); return true; }
            public void StopEffects() => Calls.Add("stopEffects");
        }

        private static CutsceneDefinition Define(params CutsceneStep[] steps) =>
            new CutsceneDefinition { id = "test", steps = steps };

        [Test]
        public void EveryAuthoredCutsceneParsesAndEndsWithTheScreenClear()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>("Cutscenes");
            Assert.That(assets, Is.Not.Empty);
            foreach (TextAsset asset in assets)
            {
                List<string> problems = new List<string>();
                CutsceneDefinition definition = CutsceneDefinition.Parse(asset.text, problems);
                Assert.That(problems, Is.Empty, asset.name);
                Assert.That(definition.id, Is.EqualTo(asset.name), "A cutscene's id must match its file name, which is how it is loaded.");
                CutsceneSequence sequence = new CutsceneSequence(definition, new FakeHost());
                for (int frame = 0; frame < 4000 && !sequence.IsFinished; frame++) sequence.Advance(0.05f);
                Assert.That(sequence.IsFinished, Is.True, asset.name);
                Assert.That(sequence.OverlayAlpha, Is.EqualTo(0f).Within(1e-4f), asset.name + " should end with the world visible.");
            }
        }

        [Test]
        public void OpeningRevealsThePlayerBehindAFlashOnceTheLightHasGathered()
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + CutscenePlayer.OpeningCutsceneId);
            Assert.That(asset, Is.Not.Null, "Resources/Cutscenes/opening_arrival.json is missing.");
            CutsceneDefinition definition = CutsceneDefinition.Parse(asset.text, new List<string>());
            Assert.That(definition.once, Is.True);
            Assert.That(definition.steps.Any(step => step.Kind == CutsceneStepKind.Arrive), Is.True,
                "Only a cutscene with an arrive step takes over the startup menu's dissolve.");

            FakeHost host = new FakeHost { Gathering = true };
            CutsceneSequence sequence = new CutsceneSequence(definition, host);
            sequence.Advance(0.05f);
            sequence.Advance(3f);
            Assert.That(host.Calls, Does.Contain("arrive"));
            Assert.That(host.Calls, Does.Not.Contain("reveal"), "The flash must wait for the light to gather.");

            host.Gathering = false;
            float alphaAtReveal = -1f;
            for (int frame = 0; frame < 400 && !sequence.IsFinished; frame++)
            {
                bool before = host.Calls.Contains("reveal");
                sequence.Advance(0.02f);
                if (!before && host.Calls.Contains("reveal")) alphaAtReveal = sequence.OverlayAlpha;
            }

            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(alphaAtReveal, Is.GreaterThan(0.8f), "The player should appear while the flash covers the swap.");
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void FallingStarCutsceneStillPlaysTheStarBeforeItsCaptions()
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/falling_star");
            Assert.That(asset, Is.Not.Null);
            CutsceneDefinition definition = CutsceneDefinition.Parse(asset.text, new List<string>());

            FakeHost host = new FakeHost();
            CutsceneSequence sequence = new CutsceneSequence(definition, host);
            bool sawBlackWithCaption = false;
            float elapsed = 0f;
            while (!sequence.IsFinished && elapsed < 120f)
            {
                sequence.Advance(0.05f);
                elapsed += 0.05f;
                if (!string.IsNullOrEmpty(sequence.Caption) && sequence.OverlayAlpha >= 0.999f && sequence.OverlayColor.r < 0.01f)
                    sawBlackWithCaption = true;
            }

            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(host.StarSeconds, Is.GreaterThan(0f), "The star never fell.");
            Assert.That(host.Calls.IndexOf("star"), Is.GreaterThan(host.Calls.FindIndex(c => c.StartsWith("camera:"))));
            Assert.That(host.Calls.IndexOf("cameraRelease"), Is.GreaterThan(host.Calls.IndexOf("star")));
            Assert.That(sawBlackWithCaption, Is.True, "Captions should play over a black screen.");
        }

        [Test]
        public void OpeningEndsWithTheAwakeningDialogueAfterThePlayerAppears()
        {
            CutsceneDefinition definition = CutsceneDefinition.Parse(
                Resources.Load<TextAsset>("Cutscenes/" + CutscenePlayer.OpeningCutsceneId).text, new List<string>());
            FakeHost host = new FakeHost();
            CutsceneSequence sequence = new CutsceneSequence(definition, host);
            for (int frame = 0; frame < 400 && !sequence.IsFinished; frame++) sequence.Advance(0.05f);

            int reveal = host.Calls.IndexOf("reveal");
            int dialogue = host.Calls.IndexOf("dialogue:opening_awakening");
            Assert.That(dialogue, Is.GreaterThan(reveal), "The player's first thoughts come once they have appeared.");
        }

        [Test]
        public void NameStepWaitsForTheAnswer()
        {
            FakeHost host = new FakeHost { HoldOpen = true };
            CutsceneSequence sequence = new CutsceneSequence(Define(
                new CutsceneStep { type = "name" },
                new CutsceneStep { type = "flag", key = "named", value = true }), host);

            sequence.Advance(0.1f);
            sequence.Advance(5f);
            Assert.That(host.Flags.ContainsKey("named"), Is.False);

            host.NamePromptOpen = false;
            sequence.Advance(0.1f);
            Assert.That(host.Flags["named"], Is.True);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void ArriveStepWaitsUntilTheLightHasGathered()
        {
            FakeHost host = new FakeHost { Gathering = true };
            CutsceneSequence sequence = new CutsceneSequence(Define(
                new CutsceneStep { type = "arrive" },
                new CutsceneStep { type = "reveal" }), host);

            sequence.Advance(0.1f);
            sequence.Advance(10f);
            Assert.That(host.Calls, Is.EqualTo(new[] { "arrive" }));

            host.Gathering = false;
            sequence.Advance(0.1f);
            Assert.That(host.Calls, Is.EqualTo(new[] { "arrive", "reveal" }));
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void FadeEasesToItsTargetAndHoldsTheSequenceUntilDone()
        {
            CutsceneSequence sequence = new CutsceneSequence(
                Define(new CutsceneStep { type = "fade", color = "#FFFFFF", alpha = 1f, seconds = 1f }), new FakeHost());

            sequence.Advance(0.5f);
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(sequence.IsFinished, Is.False);

            sequence.Advance(0.5f);
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(sequence.OverlayColor.g, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void TimeLeftOverFromOneStepCarriesIntoTheNext()
        {
            CutsceneSequence sequence = new CutsceneSequence(Define(
                new CutsceneStep { type = "wait", seconds = 0.5f },
                new CutsceneStep { type = "fade", alpha = 1f, seconds = 1f }), new FakeHost());

            sequence.Advance(0.75f);

            // A quarter of the way through the fade, eased by smoothstep.
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(0.15625f).Within(1e-4f));
        }

        [Test]
        public void BackgroundStepsLetTheNextStepsStartAtOnce()
        {
            FakeHost host = new FakeHost();
            CutsceneSequence sequence = new CutsceneSequence(Define(
                new CutsceneStep { type = "shake", amplitude = 1f, seconds = 1f, background = true },
                new CutsceneStep { type = "flag", key = "shaken", value = true },
                new CutsceneStep { type = "wait", seconds = 1f }), host);

            sequence.Advance(0.25f);

            Assert.That(host.Flags.ContainsKey("shaken"), Is.True);
            Assert.That(sequence.ShakeStrength, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(sequence.IsFinished, Is.False);
        }

        [Test]
        public void CaptionFadesInHoldsAndClears()
        {
            CutsceneSequence sequence = new CutsceneSequence(
                Define(new CutsceneStep { type = "caption", text = "A star let go of the sky.", seconds = 4f }), new FakeHost());

            sequence.Advance(0.3f);
            Assert.That(sequence.Caption, Is.EqualTo("A star let go of the sky."));
            Assert.That(sequence.CaptionAlpha, Is.EqualTo(0.5f).Within(1e-4f));

            sequence.Advance(1.7f);
            Assert.That(sequence.CaptionAlpha, Is.EqualTo(1f).Within(1e-4f));

            sequence.Advance(2f);
            Assert.That(sequence.Caption, Is.Empty);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void DialogueStepWaitsUntilTheConversationEnds()
        {
            FakeHost host = new FakeHost { HoldOpen = true };
            CutsceneSequence sequence = new CutsceneSequence(
                Define(new CutsceneStep { type = "dialogue", key = "reception_contradiction" }), host);

            sequence.Advance(0.1f);
            sequence.Advance(5f);
            Assert.That(host.Calls, Does.Contain("dialogue:reception_contradiction"));
            Assert.That(sequence.IsFinished, Is.False);

            host.DialogueActive = false;
            sequence.Advance(0.1f);
            Assert.That(sequence.IsFinished, Is.True);
        }

        [Test]
        public void SkippingSetsLaterFlagsAndReleasesEverything()
        {
            FakeHost host = new FakeHost { HoldOpen = true };
            CutsceneSequence sequence = new CutsceneSequence(Define(
                new CutsceneStep { type = "fade", alpha = 1f },
                new CutsceneStep { type = "star", seconds = 3f, background = true },
                new CutsceneStep { type = "dialogue", key = "narration" },
                new CutsceneStep { type = "flag", key = "intro.done", value = true },
                new CutsceneStep { type = "caption", text = "Later", seconds = 2f }), host);

            sequence.Advance(0.1f);
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(1f));
            sequence.Skip();

            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(sequence.WasSkipped, Is.True);
            Assert.That(host.Flags["intro.done"], Is.True);
            Assert.That(host.Calls, Does.Contain("endDialogue").And.Contain("stopEffects").And.Contain("cameraRelease"));
            Assert.That(sequence.OverlayAlpha, Is.EqualTo(0f));
            Assert.That(sequence.Caption, Is.Empty);
        }

        [Test]
        public void UnskippableCutscenesCannotBeSkippedFromInput()
        {
            CutsceneDefinition definition = Define(new CutsceneStep { type = "wait", seconds = 1f });
            definition.unskippable = true;
            Assert.That(new CutsceneSequence(definition, new FakeHost()).CanSkip, Is.False);
        }

        [Test]
        public void WalkToAnNpcTakesAnOffsetThatMayBeNegative()
        {
            List<string> problems = new List<string>();
            Assert.That(CutsceneDefinition.Parse("{\"id\":\"x\",\"steps\":[{\"type\":\"walk\",\"key\":\"kit\",\"x\":0,\"y\":-2}]}", problems), Is.Not.Null);
            Assert.That(problems, Is.Empty);
            Assert.That(CutsceneDefinition.Parse("{\"id\":\"x\",\"steps\":[{\"type\":\"walk\",\"x\":0,\"y\":-2}]}", problems), Is.Null,
                "Without an NPC, a walk names a map cell, which cannot be negative.");

            FakeHost host = new FakeHost();
            CutsceneSequence sequence = new CutsceneSequence(Define(new CutsceneStep { type = "walk", key = "kit", y = -2 }), host);
            sequence.Advance(0.1f);
            Assert.That(host.Calls, Does.Contain("walk:kit0,-2"));
        }

        [Test]
        public void ParseReportsUnplayableCutscenes()
        {
            List<string> problems = new List<string>();
            Assert.That(CutsceneDefinition.Parse("{\"id\":\"x\",\"steps\":[{\"type\":\"teleport\"}]}", problems), Is.Null);
            Assert.That(problems.Single(), Does.Contain("unknown type 'teleport'"));

            problems.Clear();
            Assert.That(CutsceneDefinition.Parse("{\"steps\":[{\"type\":\"caption\",\"seconds\":1}]}", problems), Is.Null);
            Assert.That(problems, Has.Some.Contains("no id").And.Some.Contains("no text"));

            problems.Clear();
            Assert.That(CutsceneDefinition.Parse("{\"id\":\"x\",\"steps\":[{\"type\":\"fade\",\"alpha\":2}]}", problems), Is.Null);
            Assert.That(problems.Single(), Does.Contain("alpha"));
        }
    }
}
