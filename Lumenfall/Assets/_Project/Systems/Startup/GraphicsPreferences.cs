using UnityEngine;

namespace Lumenfall.Startup
{
    public enum GraphicsAntialiasing { Off, FXAA, SMAA }
    public enum ShadingQuality { Low, Medium, High }

    /// <summary>Device preferences, independent of gameplay saves.</summary>
    public readonly struct GraphicsPreferences
    {
        public const string VSyncKey = "lumenfall.graphics.vsync";
        public const string AntialiasingKey = "lumenfall.graphics.antialiasing";
        public const string AmbientOcclusionKey = "lumenfall.graphics.ambient-occlusion";
        public const string ShadingKey = "lumenfall.graphics.shading";

        public bool VSync { get; }
        public GraphicsAntialiasing Antialiasing { get; }
        public bool AmbientOcclusion { get; }
        public ShadingQuality Shading { get; }

        public GraphicsPreferences(bool vSync, GraphicsAntialiasing antialiasing,
            bool ambientOcclusion, ShadingQuality shading)
        {
            VSync = vSync;
            Antialiasing = antialiasing >= GraphicsAntialiasing.Off && antialiasing <= GraphicsAntialiasing.SMAA
                ? antialiasing : GraphicsAntialiasing.Off;
            AmbientOcclusion = ambientOcclusion;
            Shading = shading >= ShadingQuality.Low && shading <= ShadingQuality.High
                ? shading : ShadingQuality.High;
        }

        public static GraphicsPreferences Defaults => GraphicsRecommendations.ForCurrentDevice();

        public static GraphicsPreferences Load()
        {
            return Load(Defaults);
        }

        /// <summary>Persist first-launch defaults and fill missing keys without replacing saved choices.</summary>
        public static GraphicsPreferences LoadOrCreate(GraphicsPreferences defaults)
        {
            GraphicsPreferences preferences = Load(defaults);
            if (!PlayerPrefs.HasKey(VSyncKey) || !PlayerPrefs.HasKey(AntialiasingKey)
                || !PlayerPrefs.HasKey(AmbientOcclusionKey) || !PlayerPrefs.HasKey(ShadingKey))
                preferences.Save();
            return preferences;
        }

        private static GraphicsPreferences Load(GraphicsPreferences defaults)
        {
            return new GraphicsPreferences(
                PlayerPrefs.GetInt(VSyncKey, defaults.VSync ? 1 : 0) == 1,
                (GraphicsAntialiasing)PlayerPrefs.GetInt(AntialiasingKey, (int)defaults.Antialiasing),
                PlayerPrefs.GetInt(AmbientOcclusionKey, defaults.AmbientOcclusion ? 1 : 0) == 1,
                (ShadingQuality)PlayerPrefs.GetInt(ShadingKey, (int)defaults.Shading));
        }

        public void Save()
        {
            PlayerPrefs.SetInt(VSyncKey, VSync ? 1 : 0);
            PlayerPrefs.SetInt(AntialiasingKey, (int)Antialiasing);
            PlayerPrefs.SetInt(AmbientOcclusionKey, AmbientOcclusion ? 1 : 0);
            PlayerPrefs.SetInt(ShadingKey, (int)Shading);
            PlayerPrefs.Save();
        }
    }
}
