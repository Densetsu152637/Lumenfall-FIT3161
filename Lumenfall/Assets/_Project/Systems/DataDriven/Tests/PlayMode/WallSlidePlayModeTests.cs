using System.Reflection;
using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.World
{
    /// <summary>
    /// Playtesters kept walking into the same wall: a diagonal held into a wall or round a corner stopped dead. It now
    /// carries on along whichever side is open, except on ice, where slides keep to the four grid directions. Each case
    /// builds its own small grid away from the authored world and reads where the first step heads.
    /// </summary>
    public sealed class WallSlidePlayModeTests
    {
        private static readonly Vector2Int Start = new Vector2Int(2, 2), UpRight = new Vector2Int(1, 1);

        [Test]
        public void AHeldDiagonalIntoACornerSlidesAlongTheOpenSide()
        {
            Assert.That(Step(new[] { new Vector2Int(3, 3) }, ice: false), Is.EqualTo(new Vector2Int(3, 2)),
                "The diagonal tile is blocked, so the player slides right.");
            Assert.That(Step(new[] { new Vector2Int(3, 3), new Vector2Int(3, 2) }, ice: false), Is.EqualTo(new Vector2Int(2, 3)),
                "With the right side walled too, the player slides up instead.");
            Assert.That(Step(new Vector2Int[0], ice: false), Is.EqualTo(new Vector2Int(3, 3)), "An open diagonal is still taken.");
        }

        [Test]
        public void IceKeepsItsSlidesToTheFourGridDirections()
        {
            Assert.That(Step(new[] { new Vector2Int(3, 3) }, ice: true), Is.EqualTo(Start), "On ice a blocked diagonal does not turn into a slide.");
        }

        // Holds up-right from the start cell and returns the cell the player starts moving to (the start if none).
        private static Vector2Int Step(Vector2Int[] walls, bool ice)
        {
            GameObject root = new GameObject("wall-slide-test-grid");
            root.transform.position = new Vector3(500f, 0f, 500f);
            try
            {
                GridWorld grid = root.AddComponent<GridWorld>();
                grid.ConfigureBounds(8, 8);
                foreach (Vector2Int wall in walls)
                {
                    GameObject blocker = new GameObject("wall " + wall);
                    blocker.transform.SetParent(root.transform, false);
                    blocker.transform.localPosition = new Vector3(wall.x, 0f, wall.y);
                    blocker.AddComponent<GridObstacle>().Configure(grid, Vector2Int.one);
                }
                if (ice) grid.SetIce(Start, true);

                GameObject player = new GameObject("wall-slide-test-player");
                player.transform.SetParent(root.transform, false);
                player.transform.localPosition = new Vector3(Start.x, 0f, Start.y);
                GridMover mover = player.AddComponent<GridMover>();
                GridPlayerInput input = player.AddComponent<GridPlayerInput>();
                Assert.That(mover.CurrentCell, Is.EqualTo(Start));

                typeof(GridPlayerInput).GetField("heldDirection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(input, UpRight);
                typeof(GridPlayerInput).GetMethod("TryMoveHeldDirection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
                return mover.IsMoving ? mover.DestinationCell : mover.CurrentCell;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
