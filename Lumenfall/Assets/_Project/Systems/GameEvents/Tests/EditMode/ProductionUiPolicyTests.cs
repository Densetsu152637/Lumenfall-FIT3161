using System;
using Lumenfall.GameEvents;
using NUnit.Framework;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class ProductionUiPolicyTests
    {
        [Test] public void ReleasingOneOwnerDoesNotReleaseSiblingOrUnknownOwner()
        {
            var locks = new OverlayLockSet(); var dialogue = new object(); var wheel = new object();
            locks.Acquire(wheel, 1); locks.Acquire(dialogue, 2); locks.Release(wheel, 3); locks.Release(new object(), 4);
            Assert.That(locks.Count, Is.EqualTo(1)); Assert.That(locks.Owns(dialogue), Is.True);
            Assert.That(locks.Blocks(null, 4), Is.True); Assert.That(locks.Blocks(dialogue, 4), Is.False);
        }
        [Test] public void OpeningAndClosingFramesConsumeInputButNextFrameAllowsOwner()
        {
            var locks = new OverlayLockSet(); var owner = new object();
            locks.Acquire(owner, 10); Assert.That(locks.Blocks(owner, 10), Is.True);
            Assert.That(locks.Blocks(owner, 11), Is.False); locks.Release(owner, 12);
            Assert.That(locks.Blocks(null, 12), Is.True); Assert.That(locks.Blocks(null, 13), Is.False);
        }
        [Test] public void DuplicateAcquireAndRepeatedCleanupAreIdempotent()
        {
            var locks = new OverlayLockSet(); var owner = new object();
            locks.Acquire(owner, 1); locks.Acquire(owner, 2); Assert.That(locks.Count, Is.EqualTo(1));
            locks.Release(owner, 3); locks.Release(owner, 4); Assert.That(locks.Blocks(null, 4), Is.False);
            locks.Acquire(owner, 5); locks.Clear(); Assert.That(locks.Count, Is.Zero);
            Assert.Throws<ArgumentNullException>(() => locks.Acquire(null, 6));
        }
        [Test] public void FocusWrapsAndSkipsEmptySlotsInBothDirections()
        {
            Assert.That(UiFocusIndex.Move(0, -1, 4, i => i == 2 || i == 3), Is.EqualTo(3));
            Assert.That(UiFocusIndex.Move(3, 1, 4, i => i == 2 || i == 3), Is.EqualTo(2));
            Assert.That(UiFocusIndex.Move(0, 0, 4, i => i == 3), Is.EqualTo(3));
            Assert.That(UiFocusIndex.Move(0, 1, 3, i => false), Is.EqualTo(-1));
            Assert.That(UiFocusIndex.Move(0, 1, 0), Is.EqualTo(-1));
        }
    }
}
