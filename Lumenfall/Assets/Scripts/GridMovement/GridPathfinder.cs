using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>A* pathfinding over a GridWorld's current static, mover and reservation occupancy.</summary>
    public static class GridPathfinder
    {
        private static readonly Vector2Int[] OrthogonalDirections =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        private static readonly Vector2Int[] DiagonalDirections =
        {
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1),
            new Vector2Int(-1, 1)
        };

        /// <summary>
        /// Fills path with cells after start, including target. Returns false when the target is
        /// blocked, unreachable, or the search exceeds maxExpandedNodes.
        /// </summary>
        public static bool TryFindPath(
            GridWorld world,
            Vector2Int start,
            Vector2Int target,
            List<Vector2Int> path,
            bool allowDiagonal = true,
            int maxExpandedNodes = 4096,
            GridMover ignoredMover = null)
        {
            if (path == null)
            {
                return false;
            }

            path.Clear();
            if (world == null || maxExpandedNodes < 1)
            {
                return false;
            }

            if (start == target)
            {
                return true;
            }

            if (!world.IsCellAvailable(target))
            {
                return false;
            }

            List<Vector2Int> open = new List<Vector2Int> { start };
            HashSet<Vector2Int> openSet = new HashSet<Vector2Int> { start };
            HashSet<Vector2Int> closed = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            Dictionary<Vector2Int, int> costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            int expandedNodes = 0;

            while (open.Count > 0 && expandedNodes < maxExpandedNodes)
            {
                int bestIndex = FindBestOpenIndex(open, costs, target, allowDiagonal);
                Vector2Int current = open[bestIndex];
                open.RemoveAt(bestIndex);
                openSet.Remove(current);

                if (current == target)
                {
                    ReconstructPath(start, target, cameFrom, path);
                    return true;
                }

                closed.Add(current);
                expandedNodes++;

                EvaluateNeighbours(
                    world,
                    current,
                    OrthogonalDirections,
                    open,
                    openSet,
                    closed,
                    cameFrom,
                    costs,
                    ignoredMover);

                if (allowDiagonal)
                {
                    EvaluateNeighbours(
                        world,
                        current,
                        DiagonalDirections,
                        open,
                        openSet,
                        closed,
                        cameFrom,
                        costs,
                        ignoredMover);
                }
            }

            return false;
        }

        private static void EvaluateNeighbours(
            GridWorld world,
            Vector2Int current,
            IReadOnlyList<Vector2Int> directions,
            List<Vector2Int> open,
            HashSet<Vector2Int> openSet,
            HashSet<Vector2Int> closed,
            Dictionary<Vector2Int, Vector2Int> cameFrom,
            Dictionary<Vector2Int, int> costs,
            GridMover ignoredMover)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighbour = current + direction;
                if (closed.Contains(neighbour) || !world.CanTraverse(current, neighbour, ignoredMover))
                {
                    continue;
                }

                int stepCost = direction.x != 0 && direction.y != 0 ? 14 : 10;
                int candidateCost = costs[current] + stepCost;
                if (costs.TryGetValue(neighbour, out int knownCost) && candidateCost >= knownCost)
                {
                    continue;
                }

                cameFrom[neighbour] = current;
                costs[neighbour] = candidateCost;
                if (openSet.Add(neighbour))
                {
                    open.Add(neighbour);
                }
            }
        }

        private static int FindBestOpenIndex(
            IReadOnlyList<Vector2Int> open,
            IReadOnlyDictionary<Vector2Int, int> costs,
            Vector2Int target,
            bool allowDiagonal)
        {
            int bestIndex = 0;
            int bestHeuristic = Heuristic(open[0], target, allowDiagonal);
            int bestScore = costs[open[0]] + bestHeuristic;

            for (int i = 1; i < open.Count; i++)
            {
                int heuristic = Heuristic(open[i], target, allowDiagonal);
                int score = costs[open[i]] + heuristic;
                if (score < bestScore || score == bestScore && heuristic < bestHeuristic)
                {
                    bestIndex = i;
                    bestScore = score;
                    bestHeuristic = heuristic;
                }
            }

            return bestIndex;
        }

        private static int Heuristic(Vector2Int from, Vector2Int to, bool allowDiagonal)
        {
            int xDistance = Mathf.Abs(to.x - from.x);
            int yDistance = Mathf.Abs(to.y - from.y);
            if (!allowDiagonal)
            {
                return (xDistance + yDistance) * 10;
            }

            int diagonalSteps = Mathf.Min(xDistance, yDistance);
            int straightSteps = Mathf.Max(xDistance, yDistance) - diagonalSteps;
            return diagonalSteps * 14 + straightSteps * 10;
        }

        private static void ReconstructPath(
            Vector2Int start,
            Vector2Int target,
            IReadOnlyDictionary<Vector2Int, Vector2Int> cameFrom,
            List<Vector2Int> path)
        {
            Vector2Int current = target;
            while (current != start)
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Reverse();
        }
    }
}
