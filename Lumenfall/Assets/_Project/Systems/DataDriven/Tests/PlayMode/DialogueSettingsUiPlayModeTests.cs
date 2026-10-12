using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lumenfall.Accessibility;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Startup;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Lumenfall.Tests.World
{
    public sealed class DialogueSettingsUiPlayModeTests
    {
        private const string DialogueId = "stella_first_light_dialogue";
        private const string Body = "Localized dialogue for settings checks";
        private static readonly string[] PreferenceNames =
            { "text-scale", "read-speed", "instant-reveal", "reduced-motion", "high-contrast", "hold-to-sprint" };
        private readonly Dictionary<string, string> savedKeys = new Dictionary<string, string>();
        private readonly object sibling = new object();
        private AccessibilitySnapshot savedSettings;
        private Func<string, string> savedResolver;
        private GameObject host;
        private DialogueOverlay overlay;
        private GridMover mover;
        private StartupMenu startupMenu;
        private bool startupEnabled;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savedSettings = AccessibilitySettings.Current;
            savedResolver = Localization.Resolver;
            foreach (string name in PreferenceNames)
            {
                string key = "lumenfall.accessibility." + name;
                savedKeys[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            }
            // A standalone fixture can run before any authored-world test has dismissed startup.
            float deadline = Time.realtimeSinceStartup + 5f;
            startupMenu = Object.FindAnyObjectByType<StartupMenu>();
            startupEnabled = startupMenu != null && startupMenu.enabled;
            while (startupEnabled && !StartupMenu.IsVisible && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(!startupEnabled || StartupMenu.IsVisible, Is.True, "Startup initialization must finish before testing dialogue.");
            previousTimeScale = Time.timeScale;
            if (startupEnabled) startupMenu.enabled = false;
            Time.timeScale = 1f;
            QuestHintOverlay.CloseSettings();
            GameEventManager.Instance.ReloadDefinitions();
            Localization.Resolver = id => id == "speaker.Stella" ? "Localized Stella" :
                id == Localization.DialogueTextId(DialogueId, "greeting") ? "<color=#b00000>" + Body + "</color>" :
                id == Localization.DialogueChoiceId(DialogueId, "greeting", "accept") ? "Localized choice" : null;
            AccessibilitySettings.Update(new AccessibilitySnapshot(instantReveal: true, reducedMotion: true));

            host = new GameObject("Dialogue settings UI test");
            host.SetActive(false);
            host.AddComponent<GridWorld>();
            var player = new GameObject("Dialogue test player");
            player.transform.SetParent(host.transform, false);
            mover = player.AddComponent<GridMover>();
            player.AddComponent<GridPlayerInput>();
            overlay = host.AddComponent<DialogueOverlay>();
            host.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            QuestHintOverlay.CloseSettings();
            if (mover != null) mover.SetMovementLocked(sibling, false);
            OverlayInputGate.Release(sibling);
            if (host != null) Object.Destroy(host);
            yield return null;
            GameEventManager.Instance.Runtime.ActiveDialogue?.End();
            GameEventManager.Instance.ReloadDefinitions();
            Localization.Resolver = savedResolver;
            AccessibilitySettings.Update(savedSettings);
            foreach (KeyValuePair<string, string> key in savedKeys)
                if (key.Value == null) PlayerPrefs.DeleteKey(key.Key);
                else PlayerPrefs.SetString(key.Key, key.Value);
            savedKeys.Clear();
            PlayerPrefs.Save();
            if (startupMenu != null) startupMenu.enabled = startupEnabled;
            Time.timeScale = previousTimeScale;
        }

        [UnityTest]
        public IEnumerator SettingsBlockToolkitChoicesAndRuntimeReplacementReleasesOnlyDialogueOwnership()
        {
            DialogueSession session = GameEventManager.Instance.StartDialogue(DialogueId);
            Assert.That(session, Is.Not.Null);
            yield return WaitForChoices();
            Assert.That(OverlayInputGate.Owns(overlay), Is.True);
            Assert.That(mover.IsMovementLocked, Is.True);

            QuestHintOverlay.OpenSettings();
            Assert.That(QuestHintOverlay.IsSettingsOpen, Is.True);
            ClickChoice();
            Assert.That(session.CurrentNode.id, Is.EqualTo("greeting"));
            Assert.That(mover.IsMovementLocked, Is.True);
            QuestHintOverlay.CloseSettings();
            ClickChoice();
            Assert.That(session.CurrentNode.id, Is.EqualTo("greeting"), "The closing settings frame must consume pointer input.");
            yield return null;
            ClickChoice();
            Assert.That(session.CurrentNode.id, Is.EqualTo("accept"));
            Assert.That(GameEventManager.Instance.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));

            GameEventRuntime oldRuntime = GameEventManager.Instance.Runtime;
            GameEventManager.Instance.ReloadDefinitions();
            yield return null;
            Assert.That(OverlayInputGate.Owns(overlay), Is.False);
            Assert.That(mover.IsMovementLocked, Is.False);
            Assert.That(Document.rootVisualElement.Q(className: "dialogue-root").ClassListContains("dialogue-root--shown"), Is.False);
            oldRuntime.StartDialogue(DialogueId);
            Assert.That(OverlayInputGate.Owns(overlay), Is.False, "The old runtime must no longer drive this overlay.");
            DialogueSession replacement = GameEventManager.Instance.StartDialogue(DialogueId);
            yield return WaitForChoices();
            Assert.That(Document.rootVisualElement.Q<Label>(className: "dialogue-text").text, Does.Contain(Body));
            ClickChoice();
            Assert.That(replacement.CurrentNode.id, Is.EqualTo("accept"));

            mover.SetMovementLocked(sibling, true);
            OverlayInputGate.Acquire(sibling);
            overlay.enabled = false;
            Assert.That(OverlayInputGate.Owns(overlay), Is.False);
            Assert.That(OverlayInputGate.Owns(sibling), Is.True);
            Assert.That(mover.IsMovementLocked, Is.True, "Disabling dialogue cannot release a sibling's movement lease.");
            mover.SetMovementLocked(sibling, false);
            Assert.That(mover.IsMovementLocked, Is.False);
        }

        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator ToolkitDialogueRendersLocalizedNormalAndAccessiblePreferences()
        {
            Assert.That(Application.isBatchMode, Is.False, "RequiresGraphics must run in a graphical Editor.");
            var cameraHost = new GameObject("Dialogue capture camera");
            cameraHost.transform.SetParent(host.transform, false);
            Camera camera = cameraHost.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.2f);
            GameEventManager.Instance.StartDialogue(DialogueId);
            yield return WaitForChoices();
            yield return WaitForLayout();
            Label body = Document.rootVisualElement.Q<Label>(className: "dialogue-text");
            Assert.That(Document.rootVisualElement.Q<Label>(className: "dialogue-name").text, Is.EqualTo("Localized Stella"));
            Assert.That(Document.rootVisualElement.Q<Label>(className: "dialogue-choice__text").text, Is.EqualTo("Localized choice"));
            Assert.That(body.text, Does.Contain(Body));
            float normalFont = body.resolvedStyle.fontSize;
            Assert.That(normalFont, Is.GreaterThan(0f));
            yield return Capture("dialogue-normal.png");

            AccessibilitySettings.Update(new AccessibilitySnapshot(1.75f, 10f, false, true, true));
            yield return null;
            int initialCharacters = DialogueTextReveal.VisibleCharacterCount(body.text);
            yield return new WaitForSecondsRealtime(0.3f);
            int slowCharacters = DialogueTextReveal.VisibleCharacterCount(body.text);
            Assert.That(slowCharacters, Is.GreaterThan(initialCharacters), "The real label must reveal progressively at the selected read speed.");
            Assert.That(slowCharacters, Is.LessThan(Body.Length), "Slow reveal must not behave like instant reveal.");
            AccessibilitySettings.Update(new AccessibilitySnapshot(1.75f, 10f, true, true, true));
            yield return WaitForChoices();
            yield return WaitForLayout();
            Assert.That(body.text, Is.EqualTo(Body), "Instant high-contrast text must be complete and omit authored color tags.");
            Assert.That(body.resolvedStyle.fontSize, Is.EqualTo(normalFont * 1.75f).Within(0.1f));
            Assert.That(body.resolvedStyle.color, Is.EqualTo(Color.white));
            Assert.That(Document.rootVisualElement.Q(className: "dialogue-box").resolvedStyle.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(Document.rootVisualElement.Q<Label>(className: "dialogue-choice__text").resolvedStyle.color, Is.EqualTo(Color.white));
            Assert.That(body.worldBound.Overlaps(Document.rootVisualElement.Q<ScrollView>(className: "dialogue-content").contentViewport.worldBound), Is.True,
                "Accessible dialogue text must remain visible in its scroll viewport.");
            yield return Capture("dialogue-accessibility.png");
        }

        private UIDocument Document => host.GetComponentInChildren<UIDocument>();

        private IEnumerator WaitForChoices()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((Document == null || Document.rootVisualElement.Q(className: "dialogue-choice") == null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(Document, Is.Not.Null);
            Assert.That(Document.rootVisualElement.Q(className: "dialogue-choice"), Is.Not.Null);
            yield return null; // The opening frame cannot select a choice.
        }

        private IEnumerator WaitForLayout()
        {
            Label body = Document.rootVisualElement.Q<Label>(className: "dialogue-text");
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((body.panel == null || body.worldBound.width <= 0f || float.IsNaN(body.resolvedStyle.fontSize)) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(body.panel, Is.Not.Null);
            Assert.That(body.worldBound.width, Is.GreaterThan(0f));
            yield return null;
        }

        private void ClickChoice()
        {
            VisualElement row = Document.rootVisualElement.Q(className: "dialogue-choice");
            Assert.That(row, Is.Not.Null);
            using (PointerDownEvent click = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
                row.SendEvent(click);
        }

        private IEnumerator Capture(string name)
        {
            string directory = Path.Combine(Application.dataPath, "../Logs/graphics-validation");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
            Document.rootVisualElement.MarkDirtyRepaint();
            yield return new WaitForSecondsRealtime(0.25f);
            yield return null;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!File.Exists(path) || new FileInfo(path).Length < 8) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True, "Graphical acceptance requires a fresh dialogue capture.");
            byte[] png = File.ReadAllBytes(path);
            Assert.That(png.Length, Is.GreaterThan(8));
            CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, new ArraySegment<byte>(png, 0, 8));
        }
    }
}
