using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Moves an NPC to a requested grid coordinate. A* produces a path, then the navigator queues
    /// that path as ordinary one-tile GridMover commands rather than imposing a patrol routine.
    /// </summary>
    [RequireComponent(typeof(GridMover))]
    [DisallowMultipleComponent]
    public sealed class GridNpcNavigator : MonoBehaviour
    {
        [Header("Optional Initial Destination")]
        [SerializeField] private bool moveOnStart;
        [SerializeField] private Vector2Int initialDestination;

        [Header("Navigation")]
        [SerializeField] private bool allowDiagonalMovement = true;
        [SerializeField, Min(1)] private int maxExpandedNodes = 4096;
        [SerializeField, Min(0.01f)] private float blockedRepathDelay = 0.2f;
        [SerializeField] private GridMovementMode movementMode = GridMovementMode.Walk;

        private readonly Queue<Vector2Int> queuedDirections = new Queue<Vector2Int>();
        private readonly List<Vector2Int> pathBuffer = new List<Vector2Int>();
        private GridMover mover;
        private GridWorld destinationWorld;
        private Vector2Int destination;
        private bool hasDestination;
        private float nextPathAttemptTime;

        public bool HasDestination => hasDestination;
        public Vector2Int Destination => destination;
        public int QueuedStepCount => queuedDirections.Count;

        public event Action<GridNpcNavigator, Vector2Int> DestinationSet;
        public event Action<GridNpcNavigator, Vector2Int> DestinationReached;
        public event Action<GridNpcNavigator, Vector2Int> PathFailed;

        private void Awake()
        {
            mover = GetComponent<GridMover>();
        }

        private void Start()
        {
            if (moveOnStart)
            {
                SetDestination(initialDestination);
            }
        }

        private void OnDisable()
        {
            queuedDirections.Clear();
            hasDestination = false;
        }

        private void Update()
        {
            if (!hasDestination || mover == null || mover.IsMoving || mover.IsMovementLocked ||
                Time.time < nextPathAttemptTime)
            {
                return;
            }

            if (mover.World != destinationWorld)
            {
                FailCurrentPath();
                return;
            }

            if (mover.CurrentCell == destination)
            {
                Vector2Int reachedCell = destination;
                queuedDirections.Clear();
                hasDestination = false;
                DestinationReached?.Invoke(this, reachedCell);
                return;
            }

            if (queuedDirections.Count == 0 && !BuildPath())
            {
                FailCurrentPath();
                return;
            }

            mover.SetMovementMode(movementMode);
            Vector2Int nextDirection = queuedDirections.Peek();
            if (mover.TryMove(nextDirection))
            {
                queuedDirections.Dequeue();
                return;
            }

            // A mover or runtime blocker may have entered the planned route since A* ran.
            queuedDirections.Clear();
            nextPathAttemptTime = Time.time + blockedRepathDelay;
        }

        /// <summary>Finds and begins following a path to an absolute grid (x, y) coordinate.</summary>
        public bool SetDestination(Vector2Int targetCell)
        {
            if (mover == null)
            {
                mover = GetComponent<GridMover>();
            }

            if (mover == null || mover.World == null)
            {
                return false;
            }

            destination = targetCell;
            destinationWorld = mover.World;
            hasDestination = true;
            queuedDirections.Clear();
            nextPathAttemptTime = 0f;
            DestinationSet?.Invoke(this, destination);

            // A currently animating step must finish; its new cell becomes the later A* start.
            if (mover.IsMoving || mover.IsMovementLocked || BuildPath())
            {
                return true;
            }

            FailCurrentPath();
            return false;
        }

        public bool SetDestinationFromWorldPosition(Vector3 worldPosition)
        {
            return mover != null && mover.World != null &&
                   SetDestination(mover.World.WorldToCell(worldPosition));
        }

        /// <summary>Clears queued movement. An already-started tile step still finishes cleanly.</summary>
        public void CancelNavigation()
        {
            queuedDirections.Clear();
            hasDestination = false;
        }

        public void SetMovementMode(GridMovementMode mode)
        {
            movementMode = mode;
        }

        private bool BuildPath()
        {
            queuedDirections.Clear();
            if (!GridPathfinder.TryFindPath(
                    destinationWorld,
                    mover.CurrentCell,
                    destination,
                    pathBuffer,
                    allowDiagonalMovement,
                    maxExpandedNodes,
                    mover))
            {
                return false;
            }

            Vector2Int previousCell = mover.CurrentCell;
            foreach (Vector2Int pathCell in pathBuffer)
            {
                queuedDirections.Enqueue(pathCell - previousCell);
                previousCell = pathCell;
            }

            return true;
        }

        private void FailCurrentPath()
        {
            Vector2Int failedDestination = destination;
            queuedDirections.Clear();
            hasDestination = false;
            PathFailed?.Invoke(this, failedDestination);
        }

        private void OnValidate()
        {
            maxExpandedNodes = Mathf.Max(1, maxExpandedNodes);
            blockedRepathDelay = Mathf.Max(0.01f, blockedRepathDelay);
        }
    }
}
