using System;
using NUnit.Framework;

namespace Lumenfall.Tests.Camera
{
    public sealed class CameraFramingMathTests
    {
        [TestCase(-10f, 0f)]
        [TestCase(3f, 3f)]
        [TestCase(20f, 9f)]
        public void BoundsKeepAnchorInsideAuthoredCellCenters(float requested, float expected)
        {
            Assert.That(CameraFramingMath.ClampCoordinate(requested, 0, 10, 1f, 0f), Is.EqualTo(expected));
        }

        [Test]
        public void NonzeroOriginAndCellScaleArePreserved()
        {
            Assert.That(CameraFramingMath.ClampCoordinate(-50f, -3, 5, 2f, 1f), Is.EqualTo(-4f));
            Assert.That(CameraFramingMath.ClampCoordinate(50f, -3, 5, 2f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void OversizedInsetCollapsesSmallRoomToCenter()
        {
            Assert.That(CameraFramingMath.ClampCoordinate(100f, 4, 2, 2f, 20f), Is.EqualTo(9f));
            Assert.That(CameraFramingMath.ClampCoordinate(-100f, 4, 2, 2f, 20f), Is.EqualTo(9f));
            Assert.That(CameraFramingMath.ClampCoordinate(100f, 4, 1, 2f, 20f), Is.EqualTo(8f));
        }

        [Test]
        public void NegativeInsetDoesNotExpandMapBounds()
        {
            Assert.That(CameraFramingMath.ClampCoordinate(100f, 0, 5, 1f, -10f), Is.EqualTo(4f));
        }

        [Test]
        public void InvalidBoundsAndNonfiniteCoordinatesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFramingMath.ClampCoordinate(0f, 0, 0, 1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFramingMath.ClampCoordinate(0f, 0, 1, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFramingMath.ClampCoordinate(float.NaN, 0, 5, 1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFramingMath.ClampCoordinate(0f, 0, 5, 1f, float.PositiveInfinity));
        }

        [Test]
        public void TeleportFallbackSnapsAtBoundaryAndCanBeDisabled()
        {
            Assert.That(CameraFramingMath.ShouldSnap(15.99f, 4f), Is.False);
            Assert.That(CameraFramingMath.ShouldSnap(16f, 4f), Is.True);
            Assert.That(CameraFramingMath.ShouldSnap(100f, 0f), Is.False);
            Assert.That(CameraFramingMath.ShouldSnap(float.NaN, 4f), Is.False);
        }
    }
}
