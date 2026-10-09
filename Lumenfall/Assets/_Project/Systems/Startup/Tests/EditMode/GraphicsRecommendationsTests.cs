using NUnit.Framework;

namespace Lumenfall.Startup.Tests
{
    public sealed class GraphicsRecommendationsTests
    {
        [TestCase(4096, 2, 1024, 45, true, ShadingQuality.Low)]
        [TestCase(7500, 4, 2000, 45, true, ShadingQuality.Medium)]
        [TestCase(15000, 8, 4000, 45, true, ShadingQuality.High)]
        [TestCase(32768, 16, 8192, 50, true, ShadingQuality.High)]
        [TestCase(14999, 8, 4000, 45, true, ShadingQuality.Medium)]
        [TestCase(15000, 7, 4000, 45, true, ShadingQuality.Medium)]
        [TestCase(15000, 8, 3999, 45, true, ShadingQuality.Medium)]
        [TestCase(7499, 8, 4000, 45, true, ShadingQuality.Low)]
        [TestCase(15000, 3, 4000, 45, true, ShadingQuality.Low)]
        [TestCase(15000, 8, 1999, 45, true, ShadingQuality.Low)]
        [TestCase(15000, 8, 4000, 44, true, ShadingQuality.Low)]
        [TestCase(15000, 8, 4000, 45, false, ShadingQuality.Low)]
        [TestCase(0, 0, 0, 0, true, ShadingQuality.Low)]
        [TestCase(-1, 8, 4000, 45, true, ShadingQuality.Low)]
        [TestCase(15000, 0, 4000, 45, true, ShadingQuality.Low)]
        [TestCase(15000, 8, -1, 45, true, ShadingQuality.Low)]
        public void PcPresetUsesEveryHardwareThreshold(int ram, int processors, int vram,
            int shader, bool hasGraphicsDevice, ShadingQuality expected)
        {
            GraphicsPreferences actual = GraphicsRecommendations.Recommend(ram, processors, vram, shader,
                false, hasGraphicsDevice);
            Assert.That(actual.Shading, Is.EqualTo(expected));
            Assert.That(actual.VSync, Is.True);
            Assert.That(actual.AmbientOcclusion, Is.EqualTo(expected == ShadingQuality.High));
            Assert.That(actual.Antialiasing, Is.EqualTo(expected == ShadingQuality.High
                ? GraphicsAntialiasing.SMAA : expected == ShadingQuality.Medium
                    ? GraphicsAntialiasing.FXAA : GraphicsAntialiasing.Off));
        }

        [Test]
        public void PcHeuristicsDoNotReplaceMobileDefaults()
        {
            GraphicsPreferences actual = GraphicsRecommendations.Recommend(32768, 16, 8192, 50, true, true);
            Assert.That(actual.VSync, Is.False);
            Assert.That(actual.Antialiasing, Is.EqualTo(GraphicsAntialiasing.Off));
            Assert.That(actual.AmbientOcclusion, Is.False);
            Assert.That(actual.Shading, Is.EqualTo(ShadingQuality.High));
        }
    }
}
