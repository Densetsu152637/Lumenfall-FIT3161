using System.Collections;
using System.IO;
using Lumenfall.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Startup.Tests
{
    public sealed class OptionsPanelPlayModeTests
    {
        private GraphicsPreferences graphics;
        private float volume;
        private GameObject host;
        [SetUp] public void Preserve()
        {
            graphics = GraphicsOptions.Instance.Preferences;
            volume = AudioMixSettings.Master;
        }
        [TearDown] public void Restore()
        {
            GraphicsOptions.Instance.SetPreferences(graphics);
            AudioMixSettings.SetVolume(AudioBus.Master, volume);
            if (host != null) Object.DestroyImmediate(host);
        }
        [Test] public void KeyboardNavigationEditsOnlyTheSelectedTab()
        {
            var panel = new OptionsPanel(); panel.Reset();
            AudioMixSettings.SetVolume(AudioBus.Master, 0.5f);
            panel.HandleNavigation(1, 0, false); // Graphics tab focus, still viewing Main.
            panel.HandleNavigation(1, 0, false); // Quest hints.
            panel.HandleNavigation(1, -1, false); // Master volume.
            Assert.That(AudioMixSettings.Master, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(GraphicsOptions.Instance.Preferences.VSync, Is.EqualTo(graphics.VSync));
            panel.SelectTab(OptionsPanel.Tab.Graphics);
            panel.HandleNavigation(1, 0, false); // Fullscreen.
            panel.HandleNavigation(1, 0, true); // VSync.
            Assert.That(GraphicsOptions.Instance.Preferences.VSync, Is.EqualTo(!graphics.VSync));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(graphics.VSync ? 0 : 1));
            Assert.That(GraphicsPreferences.Load().VSync, Is.EqualTo(!graphics.VSync));
            Assert.That(AudioMixSettings.Master, Is.EqualTo(0.45f).Within(0.001f));
        }
        [Test] public void RecommendedPresetPreservesMainSettingsAndResetReopensMain()
        {
            var panel = new OptionsPanel(); panel.Reset();
            panel.SelectTab(OptionsPanel.Tab.Graphics);
            for (int i = 0; i < 6; i++) panel.HandleNavigation(1, 0, false);
            panel.HandleNavigation(0, 0, true);
            Assert.That(GraphicsOptions.Instance.Preferences.Shading, Is.EqualTo(GraphicsOptions.Instance.RecommendedPreferences.Shading));
            Assert.That(AudioMixSettings.Master, Is.EqualTo(volume));
            panel.Reset();
            Assert.That(panel.SelectedTab, Is.EqualTo(OptionsPanel.Tab.Main));
            Assert.That(panel.FocusedControl, Is.Zero);
        }
        [UnityTest] public IEnumerator MouseTabsRenderAndSwitchInACompactPanel()
        {
            if (Application.isBatchMode) Assert.Ignore("IMGUI painting and screenshots require a graphical Editor test run.");
            host = new GameObject("Settings UI event test");
            var view = host.AddComponent<OptionsPanelGuiProbe>();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (view.Paints == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Paints, Is.GreaterThan(0));
            view.Click(new Vector2(600f, 44f));
            while (view.HasPendingEvents && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Panel.SelectedTab, Is.EqualTo(OptionsPanel.Tab.Graphics), "The visible Graphics tab must respond to pointer events.");
            string directory = Path.Combine(Application.dataPath, "../Logs/graphics-validation");
            Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "settings-graphics.png"));
            yield return null;
            view.Click(new Vector2(180f, 44f));
            while (view.HasPendingEvents && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Panel.SelectedTab, Is.EqualTo(OptionsPanel.Tab.Main));
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "settings-main.png"));
            yield return null;
        }
    }
    public sealed class OptionsPanelGuiProbe : MonoBehaviour
    {
        public readonly OptionsPanel Panel = new OptionsPanel();
        private readonly MenuSkin skin = new MenuSkin();
        private readonly System.Collections.Generic.Queue<Event> events = new System.Collections.Generic.Queue<Event>();
        public int Paints { get; private set; }
        public bool HasPendingEvents => events.Count > 0;
        private void Awake() { Panel.Reset(); }
        public void Click(Vector2 point)
        {
            events.Enqueue(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 });
            events.Enqueue(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 });
        }
        private void OnGUI()
        {
            GUI.depth = -100;
            skin.Ensure();
            Event previous = Event.current;
            bool repaint = previous.type == EventType.Repaint;
            if (repaint && events.Count > 0) Event.current = events.Dequeue();
            GUI.Box(new Rect(12f, 12f, 776f, 576f), GUIContent.none);
            Panel.Draw(new Rect(24f, 24f, 752f, 552f), skin);
            Event.current = previous;
            if (repaint) Paints++;
        }
        private void OnDestroy() { skin.Dispose(); }
    }
}
