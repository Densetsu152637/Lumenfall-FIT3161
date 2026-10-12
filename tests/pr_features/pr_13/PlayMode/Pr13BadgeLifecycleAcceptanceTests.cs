using System.Collections;
using System.Reflection;
using Lumenfall.DataDriven;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR13")]
    public sealed class Pr13BadgeLifecycleAcceptanceTests
    {
        private GameObject host;
        private Texture2D badge;

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (host != null) Object.Destroy(host);
            if (badge != null) Object.Destroy(badge);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingAnActiveLabelReleasesItsOwnedNativeTexture()
        {
            host = new GameObject("PR13 badge lifecycle");
            var label = host.AddComponent<WorldPresentationLabel>();
            Assert.That(WorldPresentationCatalog.TryCharacter("stella", out var identity), Is.True);
            label.Configure("Stella", identity.AccentRgb, Vector3.up, identity);
            badge = (Texture2D)typeof(WorldPresentationLabel)
                .GetField("badge", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(label);
            Assert.That(badge != null, Is.True);
            yield return null; // Normal MonoBehaviour lifecycle must run before destruction.
            Object.Destroy(host);
            yield return null;
            yield return null; // OnDestroy schedules the texture's own deferred destruction.
            Assert.That(badge == null, Is.True, "Destroying the label must release its owned texture.");
        }
    }
}
