using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Owns the coordinate system and authoritative occupancy state for one room or world.
    /// This is a 2.5D grid: grid (x, y) maps to local Unity (x, elevation, z), so gameplay stays
    /// two-dimensional while models, lighting and the camera remain fully 3D.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class GridWorld : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(0.01f)] private float cellSize = 1f;
        [SerializeField] private bool restrictToBounds;
        [SerializeField] private Vector2Int boundsMin = new Vector2Int(-5, -5);
        [SerializeField] private Vector2Int boundsSize = new Vector2Int(10, 10);
        [SerializeField] private bool preventDiagonalCornerCutting = true;

        [Header("Editor Preview")]
        [SerializeField] private bool showGridGizmos = true;
        [SerializeField, Min(1)] private int unboundedPreviewRadius = 5;
        [SerializeField] private Color gridColour = new Color(0.2f, 0.8f, 1f, 0.35f);

        private readonly Dictionary<Vector2Int, int> blockerCounts = new Dictionary<Vector2Int, int>();
        private readonly Dictionary<GridObstacle, List<Vector2Int>> obstacleCells =
            new Dictionary<GridObstacle, List<Vector2Int>>();
        private readonly Dictionary<Vector2Int, GridMover> movers = new Dictionary<Vector2Int, GridMover>();
        private readonly Dictionary<Vector2Int, GridMover> reservations = new Dictionary<Vector2Int, GridMover>();
        private readonly Dictionary<GridMover, Vector2Int> moverReservations =
            new Dictionary<GridMover, Vector2Int>();
        private readonly Dictionary<Vector2Int, GridPortal> portals = new Dictionary<Vector2Int, GridPortal>();
        private readonly Dictionary<GridPortal, List<Vector2Int>> portalCells =
            new Dictionary<GridPortal, List<Vector2Int>>();
        private readonly HashSet<Vector2Int> iceCells = new HashSet<Vector2Int>();

        public float CellSize => cellSize;
        public bool RestrictToBounds => restrictToBounds;
        public Vector2Int BoundsMin => boundsMin;
        public Vector2Int BoundsSize => boundsSize;
        public void ConfigureBounds(int width, int height)
        { boundsMin = Vector2Int.zero; boundsSize = new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height)); restrictToBounds = true; }

        public Vector3 CellToWorld(Vector2Int cell, float elevation = 0f)
        {
            return transform.TransformPoint(new Vector3(cell.x * cellSize, elevation, cell.y * cellSize));
        }

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new Vector2Int(
                Mathf.RoundToInt(local.x / cellSize),
                Mathf.RoundToInt(local.z / cellSize));
        }

        public float WorldToElevation(Vector3 worldPosition)
        {
            return transform.InverseTransformPoint(worldPosition).y;
        }

        public bool IsWithinBounds(Vector2Int cell)
        {
            if (!restrictToBounds)
            {
                return true;
            }

            return cell.x >= boundsMin.x &&
                   cell.y >= boundsMin.y &&
                   cell.x < boundsMin.x + boundsSize.x &&
                   cell.y < boundsMin.y + boundsSize.y;
        }

        /// <summary>Returns true when a new one-cell mover could occupy the cell.</summary>
        public bool IsCellAvailable(Vector2Int cell)
        {
            return IsCellAvailable(cell, null);
        }

        public bool IsCellBlocked(Vector2Int cell)
        {
            return !IsWithinBounds(cell) || (blockerCounts.ContainsKey(cell) && !portals.ContainsKey(cell));
        }

        public GridMover GetMoverAt(Vector2Int cell)
        {
            movers.TryGetValue(cell, out GridMover mover);
            return mover;
        }

        public GridPortal GetPortalAt(Vector2Int cell)
        {
            portals.TryGetValue(cell, out GridPortal portal);
            return portal;
        }

        /// <summary>
        /// Ice cells keep a mover sliding in its step direction until it reaches a non-ice cell or is blocked.
        /// The world is shared by every area, so whoever adds ice must remove it when its area deactivates.
        /// </summary>
        public bool IsIce(Vector2Int cell)
        {
            return iceCells.Contains(cell);
        }

        public void SetIce(Vector2Int cell, bool ice)
        {
            if (ice)
            {
                iceCells.Add(cell);
            }
            else
            {
                iceCells.Remove(cell);
            }
        }

        /// <summary>
        /// Tests an individual pathfinding edge, including optional diagonal corner blocking.
        /// </summary>
        public bool CanTraverse(Vector2Int from, Vector2Int to)
        {
            return CanTraverse(from, to, null);
        }

        internal bool CanTraverse(Vector2Int from, Vector2Int to, GridMover ignoredMover)
        {
            Vector2Int direction = to - from;
            if (!IsAdjacentDirection(direction) || !IsCellAvailable(to, ignoredMover))
            {
                return false;
            }

            if (!preventDiagonalCornerCutting || direction.x == 0 || direction.y == 0)
            {
                return true;
            }

            return IsCellAvailable(from + new Vector2Int(direction.x, 0), ignoredMover) &&
                   IsCellAvailable(from + new Vector2Int(0, direction.y), ignoredMover);
        }

        internal bool TryRegisterMover(GridMover mover, Vector2Int cell)
        {
            if (mover == null || !IsCellAvailable(cell, mover))
            {
                return false;
            }

            movers[cell] = mover;
            return true;
        }

        internal void UnregisterMover(GridMover mover)
        {
            if (mover == null)
            {
                return;
            }

            CancelMove(mover);

            Vector2Int cell = mover.CurrentCell;
            if (movers.TryGetValue(cell, out GridMover occupant) && occupant == mover)
            {
                movers.Remove(cell);
                return;
            }

            // Handles an externally changed cell without leaving stale occupancy behind.
            Vector2Int staleCell = default;
            bool found = false;
            foreach (KeyValuePair<Vector2Int, GridMover> pair in movers)
            {
                if (pair.Value != mover)
                {
                    continue;
                }

                staleCell = pair.Key;
                found = true;
                break;
            }

            if (found)
            {
                movers.Remove(staleCell);
            }
        }

        internal bool TryReserveStep(GridMover mover, Vector2Int direction, out Vector2Int destination)
        {
            destination = default;
            if (mover == null || !IsAdjacentDirection(direction) || moverReservations.ContainsKey(mover))
            {
                return false;
            }

            destination = mover.CurrentCell + direction;

            if (!IsCellAvailable(destination, mover))
            {
                return false;
            }

            if (preventDiagonalCornerCutting && direction.x != 0 && direction.y != 0)
            {
                Vector2Int horizontal = mover.CurrentCell + new Vector2Int(direction.x, 0);
                Vector2Int vertical = mover.CurrentCell + new Vector2Int(0, direction.y);
                if (!IsCellAvailable(horizontal, mover) || !IsCellAvailable(vertical, mover))
                {
                    return false;
                }
            }

            reservations[destination] = mover;
            moverReservations[mover] = destination;
            return true;
        }

        internal bool CommitMove(GridMover mover, Vector2Int origin, Vector2Int destination)
        {
            if (!moverReservations.TryGetValue(mover, out Vector2Int reserved) || reserved != destination)
            {
                return false;
            }

            reservations.Remove(destination);
            moverReservations.Remove(mover);

            if (movers.TryGetValue(origin, out GridMover occupant) && occupant == mover)
            {
                movers.Remove(origin);
            }

            movers[destination] = mover;
            return true;
        }

        internal void CancelMove(GridMover mover)
        {
            if (mover == null || !moverReservations.TryGetValue(mover, out Vector2Int destination))
            {
                return;
            }

            moverReservations.Remove(mover);
            if (reservations.TryGetValue(destination, out GridMover reservedBy) && reservedBy == mover)
            {
                reservations.Remove(destination);
            }
        }

        internal bool TryRelocateMover(GridMover mover, Vector2Int destination)
        {
            if (mover == null || !IsCellAvailable(destination, mover))
            {
                return false;
            }

            CancelMove(mover);
            Vector2Int origin = mover.CurrentCell;
            if (movers.TryGetValue(origin, out GridMover occupant) && occupant == mover)
            {
                movers.Remove(origin);
            }

            movers[destination] = mover;
            return true;
        }

        internal void RegisterObstacle(GridObstacle obstacle, IEnumerable<Vector2Int> cells)
        {
            UnregisterObstacle(obstacle);

            List<Vector2Int> registeredCells = new List<Vector2Int>();
            foreach (Vector2Int cell in cells)
            {
                if (registeredCells.Contains(cell))
                {
                    continue;
                }

                registeredCells.Add(cell);
                blockerCounts.TryGetValue(cell, out int count);
                blockerCounts[cell] = count + 1;

                if (movers.ContainsKey(cell))
                {
                    Debug.LogWarning($"{obstacle.name} blocks {cell}, which is currently occupied by a mover.", obstacle);
                }
            }

            obstacleCells[obstacle] = registeredCells;
        }

        internal void UnregisterObstacle(GridObstacle obstacle)
        {
            if (obstacle == null || !obstacleCells.TryGetValue(obstacle, out List<Vector2Int> cells))
            {
                return;
            }

            foreach (Vector2Int cell in cells)
            {
                if (!blockerCounts.TryGetValue(cell, out int count))
                {
                    continue;
                }

                if (count <= 1)
                {
                    blockerCounts.Remove(cell);
                }
                else
                {
                    blockerCounts[cell] = count - 1;
                }
            }

            obstacleCells.Remove(obstacle);
        }

        internal void RegisterPortal(GridPortal portal, IEnumerable<Vector2Int> cells)
        {
            UnregisterPortal(portal);

            List<Vector2Int> registeredCells = new List<Vector2Int>();
            foreach (Vector2Int cell in cells)
            {
                if (registeredCells.Contains(cell))
                {
                    continue;
                }

                registeredCells.Add(cell);
                if (portals.TryGetValue(cell, out GridPortal existing) && existing != portal)
                {
                    Debug.LogWarning(
                        $"Grid cell {cell} already has portal {existing.name}; {portal.name} will replace it.",
                        portal);
                }

                portals[cell] = portal;
            }

            portalCells[portal] = registeredCells;
        }

        internal void UnregisterPortal(GridPortal portal)
        {
            if (portal == null)
            {
                return;
            }

            if (!portalCells.TryGetValue(portal, out List<Vector2Int> cells))
            {
                return;
            }

            foreach (Vector2Int cell in cells)
            {
                if (portals.TryGetValue(cell, out GridPortal registeredPortal) && registeredPortal == portal)
                {
                    portals.Remove(cell);
                }
            }

            portalCells.Remove(portal);
        }

        private bool IsCellAvailable(Vector2Int cell, GridMover ignoredMover)
        {
            // An active portal is always an enterable surface. Authored portal glyphs can overlap
            // baked wall/threshold obstacles, but the mover must be able to finish a step on the
            // portal cell before GridPortal.TryEnter can perform the area transition.
            if (!IsWithinBounds(cell) || (blockerCounts.ContainsKey(cell) && !portals.ContainsKey(cell)))
            {
                return false;
            }

            if (movers.TryGetValue(cell, out GridMover occupant) && occupant != ignoredMover)
            {
                return false;
            }

            return !reservations.TryGetValue(cell, out GridMover reservedBy) || reservedBy == ignoredMover;
        }

        private static bool IsAdjacentDirection(Vector2Int direction)
        {
            return direction != Vector2Int.zero &&
                   Mathf.Abs(direction.x) <= 1 &&
                   Mathf.Abs(direction.y) <= 1;
        }

        private void OnValidate()
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            boundsSize.x = Mathf.Max(1, boundsSize.x);
            boundsSize.y = Mathf.Max(1, boundsSize.y);
            unboundedPreviewRadius = Mathf.Max(1, unboundedPreviewRadius);
        }

        private void OnDrawGizmos()
        {
            if (!showGridGizmos || cellSize <= 0f)
            {
                return;
            }

            Vector2Int min = restrictToBounds
                ? boundsMin
                : new Vector2Int(-unboundedPreviewRadius, -unboundedPreviewRadius);
            Vector2Int size = restrictToBounds
                ? boundsSize
                : Vector2Int.one * (unboundedPreviewRadius * 2);

            Gizmos.color = gridColour;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            float minX = (min.x - 0.5f) * cellSize;
            float minZ = (min.y - 0.5f) * cellSize;
            float maxX = (min.x + size.x - 0.5f) * cellSize;
            float maxZ = (min.y + size.y - 0.5f) * cellSize;

            for (int x = 0; x <= size.x; x++)
            {
                float lineX = minX + x * cellSize;
                Gizmos.DrawLine(new Vector3(lineX, 0f, minZ), new Vector3(lineX, 0f, maxZ));
            }

            for (int y = 0; y <= size.y; y++)
            {
                float lineZ = minZ + y * cellSize;
                Gizmos.DrawLine(new Vector3(minX, 0f, lineZ), new Vector3(maxX, 0f, lineZ));
            }

            Gizmos.matrix = previousMatrix;
        }
    }
}
