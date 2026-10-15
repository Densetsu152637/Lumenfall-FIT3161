using System.Collections.Generic;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// Walks the player to a cell for a cutscene, one ordinary grid step at a time along a path from
    /// <see cref="GridPathfinder"/>, so they use their normal walk and animation and go around whatever is in the way.
    /// While it walks, the player's own movement input is switched off and the movement lock is lifted. A destination
    /// that cannot be stood on is replaced by the nearest cell that can. If the player is held up for long, or no path
    /// exists, they are placed at the destination so the story never stalls.
    /// </summary>
    internal sealed class CutsceneWalk
    {
        private const int ReplanAfterStuckFrames = 20;
        private const int GiveUpAfterStuckFrames = 180;
        private const int NearestCellSearchRadius = 3;

        private readonly GridMover mover;
        private readonly GridPlayerInput input;
        private readonly bool hurry;
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private readonly bool inputWasEnabled;
        private Vector2Int target;
        private bool needsPlan = true;
        private int stuckFrames;

        public CutsceneWalk(GridMover mover, Vector2Int target, bool hurry)
        {
            this.mover = mover;
            this.target = target;
            this.hurry = hurry;
            input = mover.GetComponent<GridPlayerInput>();
            inputWasEnabled = input != null && input.enabled;
        }

        public bool IsFinished { get; private set; }

        /// <summary>Takes the next step when the last one has finished. Call once a frame, before the mover updates.</summary>
        public void Tick()
        {
            if (IsFinished) return;
            if (mover == null || mover.World == null)
            {
                End();
                return;
            }

            if (input != null) input.enabled = false;
            mover.SetMovementLocked(false);
            if (mover.IsMoving) return;
            if (mover.CurrentCell == target)
            {
                End();
                return;
            }

            if (needsPlan && !Plan())
            {
                Arrive();
                return;
            }

            Vector2Int direction = path.Count == 0 ? Vector2Int.zero : path[0] - mover.CurrentCell;
            if (direction == Vector2Int.zero || Mathf.Abs(direction.x) > 1 || Mathf.Abs(direction.y) > 1)
            {
                needsPlan = true;
                return;
            }

            mover.SetMovementMode(hurry ? GridMovementMode.Sprint : GridMovementMode.Walk);
            if (mover.TryMove(direction))
            {
                path.RemoveAt(0);
                stuckFrames = 0;
                return;
            }

            // Something stepped into the way (another mover, usually); look for a way round, then stop waiting.
            stuckFrames++;
            if (stuckFrames % ReplanAfterStuckFrames == 0) needsPlan = true;
            if (stuckFrames >= GiveUpAfterStuckFrames) Arrive();
        }

        /// <summary>Ends the walk now, placing the player at the destination if they are not moving.</summary>
        public void Arrive()
        {
            if (IsFinished) return;
            if (mover != null && mover.World != null && !mover.IsMoving && mover.CurrentCell != target)
                mover.TryTeleportTo(mover.World, target);
            End();
        }

        private bool Plan()
        {
            needsPlan = false;
            GridWorld world = mover.World;
            if (!world.IsCellAvailable(target) && !TryNearestAvailable(world, target, out target)) return false;
            return GridPathfinder.TryFindPath(world, mover.CurrentCell, target, path, true, 8192, mover) && path.Count > 0;
        }

        private static bool TryNearestAvailable(GridWorld world, Vector2Int around, out Vector2Int found)
        {
            found = around;
            float best = float.MaxValue;
            for (int dx = -NearestCellSearchRadius; dx <= NearestCellSearchRadius; dx++)
                for (int dy = -NearestCellSearchRadius; dy <= NearestCellSearchRadius; dy++)
                {
                    Vector2Int cell = around + new Vector2Int(dx, dy);
                    float distance = new Vector2(dx, dy).sqrMagnitude;
                    if (distance >= best || !world.IsCellAvailable(cell)) continue;
                    best = distance;
                    found = cell;
                }
            return best < float.MaxValue;
        }

        private void End()
        {
            IsFinished = true;
            if (input != null) input.enabled = inputWasEnabled;
            if (mover == null) return;
            mover.SetMovementMode(GridMovementMode.Walk);
            // The cutscene is still playing, so the player stays locked until it hands control back.
            mover.SetMovementLocked(true);
        }
    }
}
