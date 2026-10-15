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
        private const string StretchFrame = "dialogue-box--stretch";
        private const string Waiting = "dialogue-box--waiting";
        private const string Selected = "dialogue-choice--selected";

        // The box keeps the frames' 3:1 shape and resizes as a whole, so the painted borders never distort: it shrinks
        // for short pages and grows past the stylesheet size when a long page also carries choices. These match
        // .dialogue-box and .dialogue-content in DialogueBox.uss. Each size has a hidden sizer that lays out the full
        // page and its choice rows at that size's content width, so the fit uses real styled heights, not estimates.
        private const float BaseWidth = 1260f;
        private const float BaseHeight = 420f;
        private const float ContentWidth = 0.66f;
        private const float ContentHeight = 0.57f;
        private const float StretchBaseHeight = 360f;
        private const float StretchContentChrome = 206f; // 114px top + 92px bottom in DialogueBox.uss.
        private static readonly float[] BoxSizes = { 0.72f, 0.8f, 0.88f, 1f, 1.12f, 1.24f };

        private readonly GameObject host;
        private readonly PanelSettings settings;
        private readonly VisualElement root;
        private readonly VisualElement box;
        private readonly VisualElement stretchFrame;
        private readonly VisualElement stretchLeft;
        private readonly VisualElement stretchMiddle;
        private readonly VisualElement stretchRight;
        private readonly VisualElement portrait;
        private readonly VisualElement ribbon;
        private readonly Label nameLabel;
        private readonly ScrollView content;
        private readonly Label text;
        private readonly VisualElement choices;
        private AccessibilitySnapshot accessibility;
        private Texture2D speakerFrame;
        private readonly DialogueTriangle continueArrow;
        private readonly List<VisualElement> choiceRows = new List<VisualElement>();
        private readonly List<FontAsset> fontAssets = new List<FontAsset>();
        private readonly VisualElement[] sizers = new VisualElement[BoxSizes.Length];
        private readonly Label[] sizerTexts = new Label[BoxSizes.Length];
        private readonly VisualElement[] sizerChoices = new VisualElement[BoxSizes.Length];
        private DialogueTextEffects.Effect[] glyphEffects = Array.Empty<DialogueTextEffects.Effect>();
        private bool animatedText;
        private bool fitPending;
        private bool fitHasChoices;
        private bool usesStretchFrame;
        private int fitRequestedFrame;
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
            box.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && !IsScrollbarTarget(e.target as VisualElement)) BoxClicked?.Invoke();
            });
            layer.Add(box);

            stretchFrame = new VisualElement { pickingMode = PickingMode.Ignore };
            stretchFrame.AddToClassList("dialogue-stretch-frame");
            stretchLeft = StretchPiece("dialogue-stretch-frame__left", "Dialogue/Random NPC Left v4");
            stretchMiddle = StretchPiece("dialogue-stretch-frame__middle", "Dialogue/Random NPC Middle v4");
            stretchRight = StretchPiece("dialogue-stretch-frame__right", "Dialogue/Random NPC Right v4");
            stretchFrame.Add(stretchLeft);
            stretchFrame.Add(stretchMiddle);
            stretchFrame.Add(stretchRight);
            box.Add(stretchFrame);

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

            content = new ScrollView(ScrollViewMode.Vertical)
            {
                pickingMode = PickingMode.Position,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
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

            for (int i = 0; i < BoxSizes.Length; i++)
            {
                sizers[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                sizers[i].AddToClassList("dialogue-sizer");
                sizers[i].style.width = BaseWidth * BoxSizes[i] * ContentWidth;
                sizerTexts[i] = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
                sizerTexts[i].AddToClassList("dialogue-text");
                sizers[i].Add(sizerTexts[i]);
                sizerChoices[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                sizerChoices[i].AddToClassList("dialogue-choices");
                sizers[i].Add(sizerChoices[i]);
                layer.Add(sizers[i]);
            }
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
            ribbon.style.display = string.IsNullOrEmpty(speaker) ? DisplayStyle.None : DisplayStyle.Flex;
            ribbon.style.backgroundColor = accessibility?.HighContrast == true ? Color.black : colour;
            portrait.style.borderTopColor = portrait.style.borderBottomColor =
                portrait.style.borderLeftColor = portrait.style.borderRightColor = colour;
            usesStretchFrame = accessibility?.HighContrast != true && frame != null && SetStretchFrame(frame.name);
            box.EnableInClassList(StretchFrame, usesStretchFrame);
            box.style.backgroundImage = accessibility?.HighContrast != true && !usesStretchFrame && frame != null
                ? new StyleBackground(frame)
                : new StyleBackground(StyleKeyword.None);
            if (usesStretchFrame)
            {
                box.style.width = StyleKeyword.Null;
                box.style.height = StyleKeyword.Null;
                fitPending = false;
            }
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
            foreach (Label sizer in sizerTexts) sizer.style.fontSize = 30f * value.TextScale;
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

        private void ApplySpeakerFrame()
        {
            usesStretchFrame = accessibility?.HighContrast != true && speakerFrame != null && SetStretchFrame(speakerFrame.name);
            box.EnableInClassList(StretchFrame, usesStretchFrame);
            box.style.backgroundImage = accessibility?.HighContrast != true && !usesStretchFrame && speakerFrame != null
                ? new StyleBackground(speakerFrame) : new StyleBackground(StyleKeyword.None);
        }

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
        /// will follow it, without the text shrinking. The sizers are laid out by the panel, so the size is chosen on a
        /// later frame.
        /// </summary>
        public void FitTo(string richText, IReadOnlyList<string> choiceLabels)
        {
            content.scrollOffset = Vector2.zero;
            fitHasChoices = choiceLabels != null && choiceLabels.Count > 0;
            for (int i = 0; i < sizers.Length; i++)
            {
                sizerTexts[i].text = richText ?? string.Empty;
                sizerChoices[i].Clear();
                sizerChoices[i].style.display = choiceLabels != null && choiceLabels.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (choiceLabels != null) foreach (string label in choiceLabels) sizerChoices[i].Add(CreateRow(label));
            }
            fitPending = true;
            fitRequestedFrame = Time.frameCount;
        }

        private static bool IsScrollbarTarget(VisualElement target)
        {
            for (VisualElement current = target; current != null; current = current.parent)
                if (current is Scroller) return true;
            return false;
        }

        private void TryFit()
        {
            if (!fitPending || box.panel == null || Time.frameCount <= fitRequestedFrame) return;
            if (usesStretchFrame)
            {
                box.style.width = StyleKeyword.Null;
                if (!fitHasChoices)
                {
                    box.style.height = StyleKeyword.Null;
                    fitPending = false;
                    return;
                }

                // Stretch frames are pinned to the bottom of the screen. Grow their top edge enough to reveal the
                // complete dialogue and its reply rows; if the page is taller than the screen, the existing ScrollView
                // remains the final fallback instead of pushing choices outside the viewport.
                float needed = sizers[sizers.Length - 1].layout.height;
                if (float.IsNaN(needed) || needed <= 0f) return;
                float panelHeight = root.layout.height;
                float maximum = !float.IsNaN(panelHeight) && panelHeight > 0f
                    ? Mathf.Max(StretchBaseHeight, panelHeight - 24f)
                    : 840f;
                box.style.height = Mathf.Clamp(needed + StretchContentChrome + 12f, StretchBaseHeight, maximum);
                fitPending = false;
                return;
            }
            float chosen = BoxSizes[BoxSizes.Length - 1];
            for (int i = 0; i < BoxSizes.Length; i++)
            {
                float needed = sizers[i].layout.height;
                if (float.IsNaN(needed) || needed <= 0f) return;
                if (needed > BaseHeight * BoxSizes[i] * ContentHeight - 2f) continue;
                chosen = BoxSizes[i];
                break;
            }

            box.style.width = BaseWidth * chosen;
            box.style.height = BaseHeight * chosen;
            fitPending = false;
        }

        private static VisualElement StretchPiece(string className, string resource)
        {
            var piece = new VisualElement { pickingMode = PickingMode.Ignore };
            piece.AddToClassList(className);
            Texture2D texture = Resources.Load<Texture2D>(resource);
            piece.style.backgroundImage = texture != null
                ? new StyleBackground(texture)
                : new StyleBackground(StyleKeyword.None);
            return piece;
        }

        private bool SetStretchFrame(string frameName)
        {
            Texture2D left = Resources.Load<Texture2D>($"Dialogue/{frameName} Left v4");
            Texture2D middle = Resources.Load<Texture2D>($"Dialogue/{frameName} Middle v4");
            Texture2D right = Resources.Load<Texture2D>($"Dialogue/{frameName} Right v4");
            if (left == null || middle == null || right == null) return false;

            stretchLeft.style.backgroundImage = new StyleBackground(left);
            stretchMiddle.style.backgroundImage = new StyleBackground(middle);
            stretchRight.style.backgroundImage = new StyleBackground(right);
            return true;
        }

        public bool HasChoices => choiceRows.Count > 0;

        public void SetChoices(IReadOnlyList<string> labels)
        {
            ClearChoices();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                VisualElement row = CreateRow(labels[i]);
                row.Q<Label>().style.fontSize = 27f * (accessibility?.TextScale ?? 1f);
                row.RegisterCallback<PointerEnterEvent>(_ => ChoiceHovered?.Invoke(index));
                row.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); ChoiceClicked?.Invoke(index); });
                Recolour(row);
                choices.Add(row);
                choiceRows.Add(row);
            }
        }

        private static VisualElement CreateRow(string text)
        {
            var row = new VisualElement();
            row.AddToClassList("dialogue-choice");
            var arrow = new DialogueTriangle(false);
            arrow.AddToClassList("dialogue-choice__arrow");
            row.Add(arrow);
            var label = new Label(text) { enableRichText = true, pickingMode = PickingMode.Ignore };
            label.AddToClassList("dialogue-choice__text");
            row.Add(label);
            return row;
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
