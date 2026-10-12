using System;
using Lumenfall.Accessibility;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using AtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using UnityEngine.UIElements;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// The UI Toolkit dialogue box: the speaker's painted frame, a name ribbon, an optional live portrait, the text,
    /// parchment choice rows and a bouncing continue arrow. Layout and styling live in
    /// <c>Resources/Dialogue/DialogueBox.uss</c>; the panel scales with screen height from a 1920x1080 reference.
    /// <see cref="DialogueOverlay"/> owns the behaviour and drives this view.
    /// </summary>
    internal sealed class DialogueView : IDisposable
    {
        private const string Shown = "dialogue-root--shown";
        private const string HasPortrait = "dialogue-box--portrait";
        private const string Waiting = "dialogue-box--waiting";
        private const string Selected = "dialogue-choice--selected";

        // The box keeps the frames' 3:1 shape and shrinks as a whole for short pages, so the painted borders never
        // distort. These match .dialogue-box and .dialogue-content in DialogueBox.uss.
        private const float BaseWidth = 1260f;
        private const float BaseHeight = 420f;
        private const float ContentWidth = 0.66f;
        private const float ContentHeight = 0.57f;
        private static readonly float[] BoxSizes = { 0.72f, 0.8f, 0.88f, 1f };

        private readonly GameObject host;
        private readonly PanelSettings settings;
        private readonly VisualElement root;
        private readonly VisualElement box;
        private readonly VisualElement portrait;
        private readonly VisualElement ribbon;
        private readonly Label nameLabel;
        private readonly Label text;
        private readonly VisualElement choices;
        private readonly ScrollView content;
        private AccessibilitySnapshot accessibility;
        private Texture2D speakerFrame;
        private readonly DialogueTriangle continueArrow;
        private readonly List<VisualElement> choiceRows = new List<VisualElement>();
        private readonly List<FontAsset> fontAssets = new List<FontAsset>();
        private DialogueTextEffects.Effect[] glyphEffects = Array.Empty<DialogueTextEffects.Effect>();
        private bool animatedText;
        private string fitText;
        private IReadOnlyList<string> fitChoices;
        private bool fitPending;
        private Color accent = Color.gray;

        public event Action<int> ChoiceHovered;
        public event Action<int> ChoiceClicked;
        public event Action BoxClicked;

        public DialogueView(Transform parent)
        {
            host = new GameObject("Dialogue UI");
            host.transform.SetParent(parent, false);
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Dialogue Panel Settings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Dialogue/DialogueTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.sortingOrder = 50;
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;

            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            StyleSheet sheet = Resources.Load<StyleSheet>("Dialogue/DialogueBox");
            if (sheet != null) root.styleSheets.Add(sheet);
            // Fredoka (OFL) in static Medium and SemiBold cuts: rounded and heavy enough to read on the pale frames.
            FontDefinition body = SdfFont("Dialogue/Fredoka-Medium");
            FontDefinition heading = SdfFont("Dialogue/Fredoka-SemiBold");
            root.style.unityFontDefinition = body;

            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("dialogue-root");
            root.Add(layer);
            var dim = new VisualElement { pickingMode = PickingMode.Ignore };
            dim.AddToClassList("dialogue-dim");
            layer.Add(dim);

            box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("dialogue-box");
            box.RegisterCallback<PointerDownEvent>(_ => BoxClicked?.Invoke());
            layer.Add(box);

            portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            portrait.AddToClassList("dialogue-portrait");
            box.Add(portrait);

            ribbon = new VisualElement { pickingMode = PickingMode.Ignore };
            ribbon.AddToClassList("dialogue-ribbon");
            nameLabel = new Label { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("dialogue-name");
            nameLabel.style.unityFontDefinition = heading;
            ribbon.Add(nameLabel);
            box.Add(ribbon);

            content = new ScrollView(ScrollViewMode.Vertical);
            content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            content.verticalScrollerVisibility = ScrollerVisibility.Auto;
            content.AddToClassList("dialogue-content");
            box.Add(content);
            text = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
            text.AddToClassList("dialogue-text");
            text.PostProcessTextVertices = AnimateGlyphs;
            content.Add(text);
            choices = new VisualElement { pickingMode = PickingMode.Ignore };
            choices.AddToClassList("dialogue-choices");
            content.Add(choices);

            continueArrow = new DialogueTriangle(true);
            continueArrow.AddToClassList("dialogue-continue");
            box.Add(continueArrow);
        }

        public void Show(bool visible)
        {
            root.Q(className: "dialogue-root").EnableInClassList(Shown, visible);
            box.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
            if (!visible) ClearChoices();
        }

        public void SetSpeaker(string speaker, Color colour, Texture2D frame)
        {
            accent = colour;
            speakerFrame = frame;
            nameLabel.text = speaker;
            ribbon.style.backgroundColor = accessibility?.HighContrast == true ? Color.black : colour;
            portrait.style.borderTopColor = portrait.style.borderBottomColor =
                portrait.style.borderLeftColor = portrait.style.borderRightColor = colour;
            ApplySpeakerFrame();
            continueArrow.Fill = accessibility?.HighContrast == true ? Color.white : colour;
            foreach (VisualElement row in choiceRows) Recolour(row);
        }

        /// <summary>Applies shared preferences to the actual Toolkit elements, including existing choice rows.</summary>
        public void SetAccessibility(AccessibilitySnapshot value)
        {
            accessibility = value;
            root.EnableInClassList("dialogue-high-contrast", value.HighContrast);
            root.EnableInClassList("dialogue-reduced-motion", value.ReducedMotion);
            nameLabel.style.fontSize = 30f * value.TextScale;
            text.style.fontSize = 30f * value.TextScale;
            foreach (VisualElement row in choiceRows)
            {
                row.Q<Label>().style.fontSize = 27f * value.TextScale;
                Recolour(row);
            }
            ribbon.style.backgroundColor = value.HighContrast ? Color.black : accent;
            continueArrow.Fill = value.HighContrast ? Color.white : accent;
            ApplySpeakerFrame();
            fitPending = true;
            text.MarkDirtyRepaint();
        }

        private void ApplySpeakerFrame() => box.style.backgroundImage = accessibility?.HighContrast != true && speakerFrame != null
            ? new StyleBackground(speakerFrame) : new StyleBackground(StyleKeyword.None);

        public void SetContinuePrompt(string value) => continueArrow.tooltip = value;

        public void SetPortrait(Texture texture)
        {
            box.EnableInClassList(HasPortrait, texture != null);
            portrait.style.backgroundImage = texture is RenderTexture rt
                ? new StyleBackground(Background.FromRenderTexture(rt))
                : new StyleBackground(StyleKeyword.None);
        }

        public void SetText(string richText, DialogueTextEffects.Effect[] effects)
        {
            glyphEffects = effects ?? Array.Empty<DialogueTextEffects.Effect>();
            animatedText = Array.Exists(glyphEffects, e => e != DialogueTextEffects.Effect.None);
            text.text = richText;
        }

        public void SetWaiting(bool waiting) => box.EnableInClassList(Waiting, waiting);

        /// <summary>
        /// Sizes the box for a page: the smallest size whose text area holds the whole page, plus the choices that
        /// will follow it, without the text shrinking. Measured once styles have resolved.
        /// </summary>
        public void FitTo(string richText, IReadOnlyList<string> choiceLabels)
        {
            fitText = richText ?? string.Empty;
            fitChoices = choiceLabels;
            fitPending = true;
            TryFit();
        }

        private void TryFit()
        {
            if (!fitPending || text.panel == null || text.resolvedStyle.fontSize < 1f) return;
            if (fitText.Length > 0 && Measure(fitText, 1000f) <= 0f) return;

            float chosen = 1f;
            foreach (float size in BoxSizes)
            {
                float width = BaseWidth * size * ContentWidth;
                float needed = Measure(fitText, width);
                if (fitChoices != null && fitChoices.Count > 0)
                {
                    needed += 10f;
                    foreach (string choice in fitChoices) needed += Measure(choice, width - 48f) + 20f;
                }

                if (needed > BaseHeight * size * ContentHeight - 6f) continue;
                chosen = size;
                break;
            }

            box.style.width = BaseWidth * chosen;
            box.style.height = BaseHeight * chosen;
            fitPending = false;
        }

        private float Measure(string richText, float width) =>
            text.MeasureTextSize(richText, width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined).y;

        public bool HasChoices => choiceRows.Count > 0;

        public void SetChoices(IReadOnlyList<string> labels)
        {
            ClearChoices();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("dialogue-choice");
                var arrow = new DialogueTriangle(false);
                arrow.AddToClassList("dialogue-choice__arrow");
                row.Add(arrow);
                var label = new Label(labels[i]) { enableRichText = true, pickingMode = PickingMode.Ignore };
                label.AddToClassList("dialogue-choice__text");
                label.style.fontSize = 27f * (accessibility?.TextScale ?? 1f);
                row.Add(label);
                row.RegisterCallback<PointerEnterEvent>(_ => ChoiceHovered?.Invoke(index));
                row.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); ChoiceClicked?.Invoke(index); });
                Recolour(row);
                choices.Add(row);
                choiceRows.Add(row);
            }
        }

        public void ClearChoices()
        {
            choices.Clear();
            choiceRows.Clear();
        }

        public void SetSelected(int index)
        {
            for (int i = 0; i < choiceRows.Count; i++)
            {
                choiceRows[i].EnableInClassList(Selected, i == index);
                Recolour(choiceRows[i]);
            }
            if (index >= 0 && index < choiceRows.Count) content.ScrollTo(choiceRows[index]);
        }

        /// <summary>Per-frame animation: the continue arrow bobs, and text with effects is redrawn.</summary>
        public void Tick()
        {
            TryFit();
            continueArrow.style.translate = new Translate(0, accessibility?.ReducedMotion == true ? 0f : Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.2f)) * -7f);
            if (animatedText && accessibility?.ReducedMotion != true) text.MarkDirtyRepaint();
        }

        private void Recolour(VisualElement row)
        {
            bool selected = row.ClassListContains(Selected);
            Color border = accessibility?.HighContrast == true ? (selected ? Color.yellow : Color.white) : selected ? accent : new Color(0.47f, 0.35f, 0.2f, 0.3f);
            row.style.borderTopColor = row.style.borderBottomColor = row.style.borderLeftColor = row.style.borderRightColor = border;
            if (row.ElementAt(0) is DialogueTriangle arrow) arrow.Fill = accessibility?.HighContrast == true ? Color.yellow : accent;
        }

        /// <summary>Offsets glyphs that carry a wave or shake effect. Glyphs arrive in visible-character order.</summary>
        private void AnimateGlyphs(TextElement.GlyphsEnumerable glyphs)
        {
            if (!animatedText || accessibility?.ReducedMotion == true) return;
            float time = Time.unscaledTime;
            int index = 0;
            foreach (TextElement.Glyph glyph in glyphs)
            {
                DialogueTextEffects.Effect effect = index < glyphEffects.Length ? glyphEffects[index] : DialogueTextEffects.Effect.None;
                if (effect != DialogueTextEffects.Effect.None)
                {
                    Vector3 offset = effect == DialogueTextEffects.Effect.Wave
                        ? new Vector3(0f, Mathf.Sin(time * 7f + index * 0.6f) * 3.5f, 0f)
                        : new Vector3((Mathf.PerlinNoise(time * 22f, index) - 0.5f) * 3.5f, (Mathf.PerlinNoise(index, time * 22f) - 0.5f) * 3.5f, 0f);
                    var vertices = glyph.vertices;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        Vertex vertex = vertices[v];
                        vertex.position += offset;
                        vertices[v] = vertex;
                    }
                }

                index++;
            }
        }

        /// <summary>
        /// Builds a signed-distance-field font asset from a TTF so text stays sharp at every resolution. A plain Font
        /// is rasterised into a small bitmap atlas and scaled up, which pixelates at 1080p and above.
        /// </summary>
        private FontDefinition SdfFont(string resource)
        {
            Font font = Resources.Load<Font>(resource);
            if (font == null) return new FontDefinition();
            FontAsset asset = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (asset == null) return FontDefinition.FromFont(font);
            fontAssets.Add(asset);
            return FontDefinition.FromSDFFont(asset);
        }

        public void Dispose()
        {
            if (host != null) UnityEngine.Object.Destroy(host);
            if (settings != null) UnityEngine.Object.Destroy(settings);
            foreach (FontAsset asset in fontAssets) if (asset != null) UnityEngine.Object.Destroy(asset);
        }
    }

    /// <summary>A filled triangle used for the continue and choice arrows.</summary>
    internal sealed class DialogueTriangle : VisualElement
    {
        private readonly bool pointsDown;
        private Color fill = Color.gray;

        public Color Fill
        {
            get => fill;
            set { fill = value; MarkDirtyRepaint(); }
        }

        public DialogueTriangle(bool pointsDown)
        {
            this.pointsDown = pointsDown;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect r = contentRect;
            Painter2D painter = context.painter2D;
            painter.fillColor = fill;
            painter.BeginPath();
            if (pointsDown)
            {
                painter.MoveTo(new Vector2(0f, 0f));
                painter.LineTo(new Vector2(r.width, 0f));
                painter.LineTo(new Vector2(r.width * 0.5f, r.height));
            }
            else
            {
                painter.MoveTo(new Vector2(0f, 0f));
                painter.LineTo(new Vector2(r.width, r.height * 0.5f));
                painter.LineTo(new Vector2(0f, r.height));
            }

            painter.ClosePath();
            painter.Fill();
        }
    }
}
