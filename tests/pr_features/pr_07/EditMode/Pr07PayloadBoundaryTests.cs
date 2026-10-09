using Lumenfall.GameEvents;
using NUnit.Framework;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR07")]
    public sealed class Pr07PayloadBoundaryTests
    {
        [TestCase("{\"nested\":{\"mapId\":\"campus\"}}", false)]
        [TestCase("{\"mapId\":\"campus\",\"nested\":{\"x\":1}}", true)]
        public void RequiredProviderFieldsMustBelongToRootObject(string json, bool accepted)
        {
            Assert.That(GameSaveManager.IsValidPayloadObject(json, "mapId"), Is.EqualTo(accepted));
        }
        [TestCase("{\"mapId\":\"a\",\"map\\u0049d\":\"b\"}")]
        [TestCase("{\"mapId\":\"a\"} {\"mapId\":\"b\"}")]
        [TestCase("{\"mapId\":NaN}")]
        [TestCase("{\"mapId\":1,}")]
        public void AmbiguousOrNonJsonProviderPayloadsAreRejected(string json)
        {
            Assert.That(GameSaveManager.IsValidPayloadObject(json, "mapId"), Is.False);
        }
        [Test]
        public void ValidEscapesAndNestedArraysRemainAcceptedAtProviderBoundary()
        {
            Assert.That(GameSaveManager.IsValidPayloadObject(" \n{\"map\\u0049d\":\"B04.Ground\",\"objects\":[{\"text\":\"quote\\\"slash\\\\\"},null,true,1.25e-2]}\t", "mapId", "objects"), Is.True);
            Assert.That(GameSaveManager.IsValidPayloadObject("[]"), Is.False);
        }
    }
}
