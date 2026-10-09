using NUnit.Framework;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR18")]
    public sealed class Pr18FramingAcceptanceTests
    {
        [Test] public void FractionalInsetUsesScaledCellCentersAndIsIdempotent()
        {
            float result = CameraFramingMath.ClampCoordinate(-100, -4, 8, 0.5f, 0.25f);
            Assert.That(result, Is.EqualTo(-1.875f));
            Assert.That(CameraFramingMath.ClampCoordinate(result, -4, 8, 0.5f, 0.25f), Is.EqualTo(result));
            Assert.That(CameraFramingMath.ClampCoordinate(100, -4, 8, 0.5f, 0.25f), Is.EqualTo(1.375f));
        }
        [Test] public void TranslatingRoomAndAnchorByWholeCellsTranslatesTheResult()
        {
            float first = CameraFramingMath.ClampCoordinate(20, 0, 6, 2, 1);
            float shifted = CameraFramingMath.ClampCoordinate(6, -7, 6, 2, 1);
            Assert.That(shifted, Is.EqualTo(first - 14));
        }
        [Test] public void SnapRejectsNonfiniteThresholdsAndNegativeDistance()
        {
            Assert.That(CameraFramingMath.ShouldSnap(16, float.NaN), Is.False);
            Assert.That(CameraFramingMath.ShouldSnap(16, float.PositiveInfinity), Is.False);
            Assert.That(CameraFramingMath.ShouldSnap(-1, 0.5f), Is.False);
            Assert.That(CameraFramingMath.ShouldSnap(float.PositiveInfinity, 4), Is.False);
        }
    }
}
