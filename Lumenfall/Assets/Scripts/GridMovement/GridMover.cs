using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Lumenfall.GridMovement
{
    public enum GridMovementMode
    {
        Walk,
        Sprint
    }

    /// <summary>
    /// Moves any player or NPC exactly one grid cell per accepted command.
    /// The destination is reserved while animating, so two actors cannot enter it together.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class GridMover : MonoBehaviour
    {
        [Header("Grid Position")]
        [SerializeField] private GridWorld world;
        [SerializeField] private bool useTransformAsStartingCell = true;
        [SerializeField] private Vector2Int startingCell;
        [SerializeField] private bool snapToGridOnEnable = true;

        [Header("Movement Speed")]
        [SerializeField] private GridMovementMode movementMode = GridMovementMode.Walk;
        [FormerlySerializedAs("moveDuration")]
        [SerializeField, Min(0f)] private float walkMoveDuration = 0.15f;
        [SerializeField, Min(0f)] private float sprintMoveDuration = 0.075f;

        [Header("Step Animation")]
        [SerializeField] private AnimationCurve movementCurve = null;

        private Coroutine moveRoutine;
        private Vector2Int currentCell;
        private Vector2Int destinationCell;
        private float elevation;
        private bool isMoving;
        private bool movementLocked;
        private GridPortal suppressedPortal;

        public GridWorld World => world;
        public Vector2Int CurrentCell => currentCell;
        public Vector2Int DestinationCell => isMoving ? destinationCell : currentCell;
        public bool IsMoving => isMoving;
        public bool IsMovementLocked => movementLocked;
        public GridMovementMode MovementMode => movementMode;
        public float CurrentStepDuration => movementMode == GridMovementMode.Sprint
            ? sprintMoveDuration
            : walkMoveDuration;

        public event Action<GridMover, Vector2Int> MoveStarted;
        public event Action<GridMover, Vector2Int> MoveCompleted;
        public event Action<GridMover, Vector2Int> MoveBlocked;
        public event Action<GridMover, Vector2Int> Teleported;

        private void Reset()
        {
            world = GetComponentInParent<GridWorld>();
            movementCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        private void Awake()
        {
            if (movementCurve == null || movementCurve.length == 0)
            {
                movementCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
        }

        private void OnEnable()
        {
            if (world == null)
            {
                world = GetComponentInParent<GridWorld>();
            }

            if (world == null)
            {
                world = FindAnyObjectByType<GridWorld>();
            }

            if (world == null)
            {
                Debug.LogError($"{name} needs a GridWorld in order to move.", this);
                enabled = false;
                return;
            }

            currentCell = useTransformAsStartingCell ? world.WorldToCell(transform.position) : startingCell;
            elevation = world.WorldToElevation(transform.position);

            if (!world.TryRegisterMover(this, currentCell))
            {
                Debug.LogError($"{name} cannot start on occupied or invalid grid cell {currentCell}.", this);
                enabled = false;
                return;
            }

            if (snapToGridOnEnable)
            {
                transform.position = world.CellToWorld(currentCell, elevation);
            }
        }

        private void OnDisable()
        {
            if (world != null)
            {
                world.CancelMove(this);
                world.UnregisterMover(this);
            }

            moveRoutine = null;
            isMoving = false;
            movementLocked = false;
        }

        /// <summary>
        /// Attempts one of the eight adjacent directions. Values outside -1..1 are rejected.
        /// </summary>
        public bool TryMove(Vector2Int direction)
        {
            if (!isActiveAndEnabled || world == null || isMoving || movementLocked)
            {
                return false;
            }

            if (!world.TryReserveStep(this, direction, out Vector2Int destination))
            {
                MoveBlocked?.Invoke(this, currentCell + direction);
                return false;
            }

            destinationCell = destination;
            isMoving = true;
            MoveStarted?.Invoke(this, destinationCell);
            moveRoutine = StartCoroutine(AnimateStep(currentCell, destinationCell));
            return true;
        }

        /// <summary>Immediately places this mover on an available cell, including in another GridWorld.</summary>
        public bool TryTeleportTo(GridWorld destinationWorld, Vector2Int cell)
        {
            return TryTeleportTo(destinationWorld, cell, null);
        }

        public void SetMovementLocked(bool locked)
        {
            movementLocked = locked;
        }

        /// <summary>Changes the speed used by the next complete tile step; sprint has no resource cost.</summary>
        public void SetMovementMode(GridMovementMode mode)
        {
            movementMode = mode;
        }

        internal bool TryTeleportTo(GridWorld destinationWorld, Vector2Int cell, GridPortal portalToSuppress)
        {
            if (destinationWorld == null || isMoving)
            {
                return false;
            }

            GridWorld previousWorld = world;
            Vector2Int previousCell = currentCell;

            if (previousWorld == destinationWorld)
            {
                if (!destinationWorld.TryRelocateMover(this, cell))
                {
                    return false;
                }
            }
            else
            {
                if (!destinationWorld.IsCellAvailable(cell))
                {
                    return false;
                }

                if (previousWorld != null)
                {
                    previousWorld.UnregisterMover(this);
                }

                world = destinationWorld;
                currentCell = cell;
                if (!destinationWorld.TryRegisterMover(this, cell))
                {
                    world = previousWorld;
                    currentCell = previousCell;
                    if (previousWorld != null)
                    {
                        previousWorld.TryRegisterMover(this, previousCell);
                    }

                    return false;
                }
            }

            currentCell = cell;
            destinationCell = cell;
            suppressedPortal = portalToSuppress;
            transform.position = destinationWorld.CellToWorld(cell, elevation);
            Teleported?.Invoke(this, cell);
            return true;
        }

        internal void BeginSceneTransition(
            string sceneName,
            string destinationPortalId,
            Vector2Int sourceEntryOffset,
            Vector2Int arrivalOffset)
        {
            if (movementLocked)
            {
                return;
            }

            StartCoroutine(TravelToScene(sceneName, destinationPortalId, sourceEntryOffset, arrivalOffset));
        }

        private IEnumerator AnimateStep(Vector2Int origin, Vector2Int destination)
        {
            Vector3 from = world.CellToWorld(origin, elevation);
            Vector3 to = world.CellToWorld(destination, elevation);
            float stepDuration = CurrentStepDuration;

            if (stepDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < stepDuration)
                {
                    elapsed += Time.deltaTime;
                    float normalisedTime = Mathf.Clamp01(elapsed / stepDuration);
                    float curvedTime = movementCurve.Evaluate(normalisedTime);
                    transform.position = Vector3.LerpUnclamped(from, to, curvedTime);
                    yield return null;
                }
            }

            transform.position = to;
            if (!world.CommitMove(this, origin, destination))
            {
                transform.position = from;
                world.CancelMove(this);
                isMoving = false;
                moveRoutine = null;
                yield break;
            }

            currentCell = destination;
            isMoving = false;
            moveRoutine = null;
            MoveCompleted?.Invoke(this, currentCell);

            GridPortal portal = world.GetPortalAt(currentCell);
            if (portal == null)
            {
                suppressedPortal = null;
            }
            else if (portal != suppressedPortal)
            {
                portal.TryEnter(this);
            }
        }

        private IEnumerator TravelToScene(
            string sceneName,
            string destinationPortalId,
            Vector2Int sourceEntryOffset,
            Vector2Int arrivalOffset)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"Portal destination scene '{sceneName}' is not in Build Settings.", this);
                yield break;
            }

            movementLocked = true;
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            DontDestroyOnLoad(gameObject);
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (loadOperation == null)
            {
                movementLocked = false;
                yield break;
            }

            yield return loadOperation;
            yield return null;

            GridPortal destinationPortal = GridPortal.FindById(destinationPortalId);
            if (destinationPortal == null)
            {
                Debug.LogError(
                    $"Scene '{sceneName}' loaded, but no enabled GridPortal has id '{destinationPortalId}'.",
                    this);
                world = null;
                movementLocked = false;
                yield break;
            }

            world = null;
            currentCell = default;
            if (!TryTeleportTo(
                    destinationPortal.World,
                    destinationPortal.GetArrivalCell(sourceEntryOffset) + arrivalOffset,
                    destinationPortal))
            {
                Debug.LogError(
                    $"Could not place {name} at portal '{destinationPortalId}' because its arrival cell is blocked.",
                    this);
                movementLocked = false;
                yield break;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (gameObject.scene != activeScene && activeScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            }

            movementLocked = false;
        }

        private void OnValidate()
        {
            walkMoveDuration = Mathf.Max(0f, walkMoveDuration);
            sprintMoveDuration = Mathf.Max(0f, sprintMoveDuration);
        }
    }
}
