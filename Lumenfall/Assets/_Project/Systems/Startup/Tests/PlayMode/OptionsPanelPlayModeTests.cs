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
        [Test] public void KeyboardNavigationEditsOnlyTheFocusedSection()
        {
            var panel = new OptionsPanel(); panel.Reset();
            panel.SelectSection(OptionsPanel.Section.Audio);
            AudioMixSettings.SetVolume(AudioBus.Master, 0.5f);
            panel.HandleNavigation(0, 1, false); // Into the Audio rows: Master volume.
            panel.HandleNavigation(0, -1, false); // Master volume down a step.
            Assert.That(AudioMixSettings.Master, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(GraphicsOptions.Instance.Preferences.VSync, Is.EqualTo(graphics.VSync));
            panel.HandleNavigation(-1, 0, false); // Back to the ribbons.
            Assert.That(panel.FocusedRow, Is.EqualTo(-1));
            panel.HandleNavigation(1, 0, false); // Down a ribbon to Display.
            Assert.That(panel.SelectedSection, Is.EqualTo(OptionsPanel.Section.Display));
            panel.HandleNavigation(0, 0, true); // Into the Display rows: Fullscreen.
            panel.HandleNavigation(1, 0, false); // VSync.
            panel.HandleNavigation(0, 0, true); // Toggle VSync.
            Assert.That(GraphicsOptions.Instance.Preferences.VSync, Is.EqualTo(!graphics.VSync));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(graphics.VSync ? 0 : 1));
            Assert.That(GraphicsPreferences.Load().VSync, Is.EqualTo(!graphics.VSync));
            Assert.That(AudioMixSettings.Master, Is.EqualTo(0.45f).Within(0.001f));
        }
        [Test] public void RecommendedPresetPreservesOtherSettingsAndResetReturnsFocusToTheRibbons()
        {
            var panel = new OptionsPanel(); panel.Reset();
            panel.SelectSection(OptionsPanel.Section.Graphics);
            panel.HandleNavigation(0, 0, true); // Into the Graphics rows.
            for (int i = 0; i < 3; i++) panel.HandleNavigation(1, 0, false); // Down to Use recommended.
            panel.HandleNavigation(0, 0, true);
            Assert.That(GraphicsOptions.Instance.Preferences.Shading, Is.EqualTo(GraphicsOptions.Instance.RecommendedPreferences.Shading));
            Assert.That(AudioMixSettings.Master, Is.EqualTo(volume));
            panel.Reset();
            // The chosen section stays selected between openings; focus returns to its ribbon.
            Assert.That(panel.SelectedSection, Is.EqualTo(OptionsPanel.Section.Graphics));
            Assert.That(panel.FocusedRow, Is.EqualTo(-1));
        }
        [UnityTest, Category("RequiresGraphics")] public IEnumerator MouseRibbonsRenderAndSwitchSections()
        {
            Assert.That(Application.isBatchMode, Is.False, "Run RequiresGraphics tests in a graphical Editor, and exclude the category from batch runs.");
            host = new GameObject("Settings UI event test");
            var view = host.AddComponent<OptionsPanelGuiProbe>();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (view.Paints == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Paints, Is.GreaterThan(0));
            // Ribbons are 36 tall with 8 between them, starting at the panel top (y = 24).
            view.Click(new Vector2(100f, 24f + 3 * 44f + 18f));
            while (view.HasPendingEvents && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Panel.SelectedSection, Is.EqualTo(OptionsPanel.Section.Graphics), "The Graphics ribbon must respond to pointer events.");
            string directory = Path.Combine(Application.dataPath, "../Logs/graphics-validation");
            Directory.CreateDirectory(directory);
            yield return CaptureScreenshot(Path.Combine(directory, "settings-graphics.png"));
            view.Click(new Vector2(100f, 24f + 18f));
            deadline = Time.realtimeSinceStartup + 10f;
            while (view.HasPendingEvents && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(view.Panel.SelectedSection, Is.EqualTo(OptionsPanel.Section.Gameplay));
            yield return CaptureScreenshot(Path.Combine(directory, "settings-gameplay.png"));
        }
        private static IEnumerator CaptureScreenshot(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            // Allow the selected section to repaint, then wait for the asynchronous PNG write.
            yield return null;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!File.Exists(path) || new FileInfo(path).Length < 8) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(File.Exists(path), Is.True, "The graphical run must produce a fresh screenshot.");
            byte[] png = File.ReadAllBytes(path);
            Assert.That(png.Length, Is.GreaterThan(8));
            CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                new System.ArraySegment<byte>(png, 0, 8));
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
