using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.Startup
{
    /// <summary>Conservative starting presets; hardware capacity is not a performance benchmark.</summary>
    public static class GraphicsRecommendations
    {
        public static GraphicsPreferences ForCurrentDevice()
        {
            return Recommend(SystemInfo.systemMemorySize, SystemInfo.processorCount,
                SystemInfo.graphicsMemorySize, SystemInfo.graphicsShaderLevel,
                Application.isMobilePlatform, SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null);
        }

        /// <param name="systemMemoryMb">Reported system RAM in megabytes; non-positive means unknown.</param>
        /// <param name="processorCount">Number of logical CPU processors.</param>
        /// <param name="graphicsMemoryMb">Reported graphics memory in megabytes; non-positive means unknown.</param>
        /// <param name="shaderLevel">Unity shader capability level, for example 45 for shader model 4.5.</param>
        /// <param name="isMobile">Retain the existing mobile defaults rather than applying PC heuristics.</param>
        /// <param name="hasGraphicsDevice">False for a headless/null graphics device.</param>
        public static GraphicsPreferences Recommend(int systemMemoryMb, int processorCount,
            int graphicsMemoryMb, int shaderLevel, bool isMobile, bool hasGraphicsDevice)
        {
            if (isMobile)
                return new GraphicsPreferences(false, GraphicsAntialiasing.Off, false, ShadingQuality.High);

            // Allow for reserved memory and approximate driver reports around nominal 8/16 GB RAM
            // and 2/4 GB VRAM capacities. Require every threshold so one strong component cannot
            // hide a smaller CPU, RAM, or graphics-memory budget. Unknown hardware starts at Low.
            bool medium = hasGraphicsDevice && shaderLevel >= 45 && systemMemoryMb >= 7500
                && processorCount >= 4 && graphicsMemoryMb >= 2000;
            bool high = medium && systemMemoryMb >= 15000 && processorCount >= 8 && graphicsMemoryMb >= 4000;
            return new GraphicsPreferences(true,
                high ? GraphicsAntialiasing.SMAA : medium ? GraphicsAntialiasing.FXAA : GraphicsAntialiasing.Off,
                high, high ? ShadingQuality.High : medium ? ShadingQuality.Medium : ShadingQuality.Low);
        }
    }
}
