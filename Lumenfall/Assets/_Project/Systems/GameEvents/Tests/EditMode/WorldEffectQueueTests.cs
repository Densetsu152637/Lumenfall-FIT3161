using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class WorldEffectQueueTests
    {
        [Test]
        public void AcknowledgedEffectCanBeQueuedAgainButDuplicatePendingEffectCannot()
        {
            GameObject host = new GameObject("isolated effect queue");
            host.SetActive(false);
            try
            {
                WorldStateService state = host.AddComponent<WorldStateService>();
                state.BeginMapRebuild("effect-queue-test");
                state.EndMapRebuild();
                int queued = 0;
                state.EffectQueued += _ => queued++;
                state.QueueEffect("lightning:lamp:on");
                state.QueueEffect("lightning:lamp:on");
                state.QueueEffect(" ");
                Assert.That(queued, Is.EqualTo(1));
                Assert.That(state.AcknowledgeEffect("lightning:lamp:on"), Is.True);
                Assert.That(state.AcknowledgeEffect("lightning:lamp:on"), Is.False);
                state.QueueEffect("lightning:lamp:on");
                Assert.That(queued, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
