using Lumenfall.GameEvents;
using NUnit.Framework;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR15")]
    public sealed class Pr15UiAcceptanceTests
    {
        [Test] public void TwoOwnersRemainBlockedUntilBothCloseThenConsumeTheLastCloseFrame()
        {
            var locks = new OverlayLockSet(); var first = new object(); var second = new object();
            locks.Acquire(first, 1); locks.Acquire(second, 2);
            Assert.That(locks.Blocks(first, 3), Is.True);
            Assert.That(locks.Blocks(second, 3), Is.True);
            locks.Release(first, 4);
            Assert.That(locks.Blocks(second, 4), Is.True);
            Assert.That(locks.Blocks(second, 5), Is.False);
            Assert.That(locks.Blocks(first, 5), Is.True);
            locks.Release(second, 6);
            Assert.That(locks.Blocks(null, 6), Is.True);
            Assert.That(locks.Blocks(null, 7), Is.False);
        }
        [Test] public void DuplicateAcquireDoesNotConsumeANewFrameForAnAlreadyOpenOwner()
        {
            var locks = new OverlayLockSet(); var owner = new object();
            locks.Acquire(owner, 1); locks.Acquire(owner, 8);
            Assert.That(locks.Blocks(owner, 8), Is.False);
            locks.Clear();
            Assert.That(locks.Owns(owner), Is.False);
            Assert.That(locks.Blocks(null, 1), Is.False);
        }
        [Test] public void FocusClampsStaleSelectionBeforeSearchingAndStopsAtFirstEnabledSlot()
        {
            int probes = 0;
            Assert.That(UiFocusIndex.Move(999, -1, 4, i => { probes++; return i == 1; }), Is.EqualTo(1));
            Assert.That(probes, Is.EqualTo(2));
            Assert.That(UiFocusIndex.Move(-999, 0, 4, i => i == 0), Is.Zero);
            Assert.That(UiFocusIndex.Move(0, 99, 1, i => false), Is.EqualTo(-1));
        }
    }
}
