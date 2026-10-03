using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.World
{
    public sealed class ProductionUiLockPlayModeTests
    {
        [Test] public void MovementOwnersCannotReleaseSiblingOrLegacyTransportLock()
        {
            GameObject host = new GameObject("UI lock test");
            host.SetActive(false);
            try
            {
                GridMover mover = host.AddComponent<GridMover>();
                object dialogue = new object(), wheel = new object();
                mover.SetMovementLocked(true);
                mover.SetMovementLocked(dialogue, true);
                mover.SetMovementLocked(wheel, true);
                mover.SetMovementLocked(wheel, false);
                Assert.That(mover.IsMovementLocked, Is.True);
                mover.SetMovementLocked(false);
                Assert.That(mover.IsMovementLocked, Is.True);
                mover.SetMovementLocked(dialogue, false);
                Assert.That(mover.IsMovementLocked, Is.False);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
