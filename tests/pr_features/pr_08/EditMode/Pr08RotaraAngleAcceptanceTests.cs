using Lumenfall.Quests;
using NUnit.Framework;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR08")]
    public sealed class Pr08RotaraAngleAcceptanceTests
    {
        [TestCase(-450f, 0f)]
        [TestCase(-90f, 0f)]
        [TestCase(360f, 90f)]
        [TestCase(810f, 180f)]
        public void NextTurnNormalizesWholeRevolutionsBeforeAdvancing(float angle, float expected)
        {
            Assert.That(RotaraGearMechanism.NextQuarterTurn(angle), Is.EqualTo(expected));
        }

        [TestCase(44f, 90f)]
        [TestCase(46f, 180f)]
        [TestCase(314f, 0f)]
        [TestCase(316f, 90f)]
        public void ImportedOffAxisAngleSnapsToNearestQuarterBeforeAdvancing(float angle, float expected)
        {
            Assert.That(RotaraGearMechanism.NextQuarterTurn(angle), Is.EqualTo(expected));
        }

        [TestCase(-90f, 270)]
        [TestCase(450f, 90)]
        [TestCase(0f, 720)]
        public void SolvedAngleAcceptsEquivalentRevolutions(float angle, int required)
        {
            Assert.That(RotaraGearMechanism.RotationMatches(angle, required), Is.True);
            Assert.That(RotaraGearMechanism.RotationMatches(angle + 90f, required), Is.False);
        }

        [Test] public void NonFiniteAnglesCannotSatisfyTheMechanism()
        {
            Assert.That(RotaraGearMechanism.RotationMatches(float.NaN, 90), Is.False);
            Assert.That(RotaraGearMechanism.RotationMatches(float.PositiveInfinity, 90), Is.False);
            Assert.That(RotaraGearMechanism.RotationMatches(float.NegativeInfinity, 90), Is.False);
        }

        [Test] public void FourTurnsReturnToStartAndVisitEachOtherQuarterExactlyOnce()
        {
            float angle = 270;
            var visited = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 4; i++) { angle = RotaraGearMechanism.NextQuarterTurn(angle); visited.Add(angle); }
            Assert.That(visited, Is.EqualTo(new[] { 0f, 90f, 180f, 270f }));
        }
    }
}
