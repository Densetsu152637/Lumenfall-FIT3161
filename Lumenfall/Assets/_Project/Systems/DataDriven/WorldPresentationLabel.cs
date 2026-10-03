using UnityEngine;
using Lumenfall.Inventory;

namespace Lumenfall.DataDriven
{
    /// <summary>Screen-space sign following an authored anchor; no collider, material or state ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldPresentationLabel : MonoBehaviour
    {
        public string Text { get; private set; }
        public bool Visible { get; private set; } = true;
        private Color accent = Color.white;
        private Vector3 anchorOffset = Vector3.up * 1.7f;
        private Texture2D badge;
        private CharacterPresentationIdentity badgeIdentity;
        private GUIStyle style;
        private GUIContent content;
        private Camera viewCamera;
        private const float MaxDistance = 18f;

        public void Configure(string text, uint rgb, Vector3 offset, CharacterPresentationIdentity identity = null)
        {
            Text = text ?? string.Empty;
            content = new GUIContent(Text);
            accent = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            anchorOffset = offset;
            if (badgeIdentity != identity)
            {
                if (badge != null) Release(badge);
                badge = null;
                badgeIdentity = identity;
            }
            if (identity != null && badge == null)
            {
                badge = new Texture2D(24, 24, TextureFormat.RGBA32, false) { name = "Provisional identity badge", filterMode = FilterMode.Point };
                for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
                    badge.SetPixel(x, y, WorldPresentationCatalog.BadgePixel(identity.Shape, (x - 11.5f) / 12f, (y - 11.5f) / 12f) ? accent : Color.clear);
                badge.Apply();
            }
        }

        public void SetVisible(bool visible) { Visible = visible; }
        private void LateUpdate() { if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main; }
        private void OnGUI()
        {
            if (!Visible || string.IsNullOrEmpty(Text) || viewCamera == null || InventoryUiState.IsOpen || DialogueOverlay.IsNpcInteractionBlocked) return;
            Vector3 anchor = transform.position + anchorOffset;
            if ((viewCamera.transform.position - anchor).sqrMagnitude > MaxDistance * MaxDistance) return;
            Vector3 screen = viewCamera.WorldToScreenPoint(anchor);
            if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true, richText = false,
                    padding = new RectOffset(9, 9, 5, 5) };
                style.normal.textColor = Color.white;
            }
            float width = Mathf.Min(300f, Mathf.Max(160f, style.CalcSize(content).x + (badge == null ? 0f : 28f)));
            width = Mathf.Min(width, Screen.width - 12f);
            float height = style.CalcHeight(content, width - (badge == null ? 0f : 30f));
            Rect rect = new Rect(Mathf.Clamp(screen.x - width / 2f, 6f, Screen.width - width - 6f),
                Mathf.Clamp(Screen.height - screen.y - height, 6f, Mathf.Max(6f, Screen.height - height - 6f)), width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.10f, 0.15f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (badge != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + (height - 24f) / 2f, 24f, 24f), badge);
                rect.x += 30f; rect.width -= 30f;
            }
            GUI.Label(rect, content, style);
            GUI.color = previous;
        }
        private void OnDestroy() { if (badge != null) Release(badge); }
        private static void Release(Texture2D texture)
        { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
    }
}
