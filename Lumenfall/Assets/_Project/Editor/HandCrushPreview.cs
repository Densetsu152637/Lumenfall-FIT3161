using System.IO;
using System.Linq;
using Lumenfall.Story;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Renders key moments of the sequence break's hand crush (<see cref="ScreenCrush"/>) to <c>Logs/HandCrush</c>,
    /// without playing the ending. The picture crushed is the most recent save slot thumbnail. The white flash as it
    /// breaks is drawn over the screen in play and does not appear in these frames; the falling glass does.
    /// </summary>
    public static class HandCrushPreview
    {
        private const string Folder = "Logs/HandCrush";
        // Arriving, contact, each shove, the rush, the break, and the glass falling as the hands draw away.
        private static readonly float[] Moments = { 0.2f, 0.9f, 1.45f, 2.0f, 2.7f, 3.4f, 3.8f, 4.05f, 4.2f, 4.35f, 4.7f, 5.2f };

        [MenuItem("Lumenfall/Secrets/Sequence Break/Render Hand Crush Frames")]
        public static void Render()
        {
            Texture2D still = LoadStill();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            // Single-frame editor renders can reuse one material's constant buffer across draws with the SRP batcher.
            if (urp != null) urp.useSRPBatcher = false;
            ScreenCrush crush = ScreenCrush.TryCreate(still, 1280, 720);
            try
            {
                if (crush == null)
                {
                    Debug.LogError("The crushing hand could not be set up: check SecretRoomArt's Crushing Hand and the Console.");
                    return;
                }
                Directory.CreateDirectory(Folder);
                foreach (string old in Directory.GetFiles(Folder, "crush_*.png")) File.Delete(old);
                foreach (float moment in Moments)
                {
                    crush.Pose(moment);
                    crush.RenderTo(Path.Combine(Folder, $"crush_{moment:0.0}s.png"));
                }
                Debug.Log($"Rendered {Moments.Length} hand crush frames to {Path.GetFullPath(Folder)}.");
            }
            finally
            {
                crush?.Dispose();
                Object.DestroyImmediate(still);
                if (urp != null) urp.useSRPBatcher = batcher;
            }
        }

        // The newest save thumbnail, or a plain test card when there are no saves.
        private static Texture2D LoadStill()
        {
            string newest = Directory.Exists(Application.persistentDataPath)
                ? Directory.GetFiles(Application.persistentDataPath, "*.thumb.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            Texture2D still = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (newest != null && still.LoadImage(File.ReadAllBytes(newest))) return still;
            still.Reinitialize(320, 180);
            for (int y = 0; y < 180; y++)
                for (int x = 0; x < 320; x++)
                    still.SetPixel(x, y, ((x / 20 + y / 20) % 2 == 0) ? new Color(0.25f, 0.55f, 0.3f) : new Color(0.55f, 0.45f, 0.3f));
            still.Apply();
            return still;
        }
    }
}
