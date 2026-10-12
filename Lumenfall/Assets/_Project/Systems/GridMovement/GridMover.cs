using System;
using System.Collections;
using UnityEngine;
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
        [SerializeField, Min(0f)] private float sprintMoveDuration = 0.1f;

        [Header("Ice Slide")]
        [Tooltip("Seconds per cell while sliding across GridWorld ice cells.")]
        [SerializeField, Min(0f)] private float slideStepDuration = 0.07f;

        [Header("Step Animation")]
        [SerializeField] private AnimationCurve movementCurve = null;

        private Coroutine moveRoutine;
        private Vector2Int currentCell;
        private Vector2Int destinationCell;
        private float elevation;
        private bool isMoving;
        private bool isSliding;
        private bool movementLocked;
        private readonly System.Collections.Generic.HashSet<object> movementLockOwners = new System.Collections.Generic.HashSet<object>();
        private GridPortal suppressedPortal;

        public GridWorld World => world;
        public Vector2Int CurrentCell => currentCell;
        public Vector2Int DestinationCell => isMoving ? destinationCell : currentCell;
        public bool IsMoving => isMoving;
        /// <summary>True while the current step starts or ends on ice; input is ignored until the slide stops.</summary>
        public bool IsSliding => isSliding;
        public bool IsMovementLocked => movementLocked || movementLockOwners.Count > 0;
        public GridMovementMode MovementMode => movementMode;
        public float CurrentStepDuration => movementMode == GridMovementMode.Sprint
            ? sprintMoveDuration
            : walkMoveDuration;

        public event Action<GridMover, Vector2Int> MoveStarted;
        public event Action<GridMover, Vector2Int> MoveCompleted;
        public event Action<GridMover, Vector2Int> MoveBlocked;
        public event Action<GridMover, Vector2Int> Teleported;
        /// <summary>Raised on the cell where a slide stops, before that step's MoveCompleted.</summary>
        public event Action<GridMover, Vector2Int> SlideEnded;

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
            isSliding = false;
            movementLocked = false;
            movementLockOwners.Clear();
        }

        /// <summary>
        /// Attempts one of the eight adjacent directions. Values outside -1..1 are rejected.
        /// </summary>
        public bool TryMove(Vector2Int direction)
        {
            if (!isActiveAndEnabled || world == null || isMoving || IsMovementLocked)
            {
                return false;
            }

            // Ice only allows the four cardinal directions, so a slide always follows a grid line.
            bool diagonal = direction.x != 0 && direction.y != 0;
            if (diagonal && (world.IsIce(currentCell) || world.IsIce(currentCell + direction)))
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
            isSliding = world.IsIce(currentCell) || world.IsIce(destination);
            MoveStarted?.Invoke(this, destinationCell);
            moveRoutine = StartCoroutine(AnimateStep(currentCell, destinationCell));
            return true;
        }

        /// <summary>Immediately places this mover on an available cell, including in another GridWorld.</summary>
        public bool TryTeleportTo(GridWorld destinationWorld, Vector2Int cell)
        {
            return TryTeleportTo(destinationWorld, cell, null);
        }

        public void SetMovementLocked(object owner, bool locked)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (locked) movementLockOwners.Add(owner); else movementLockOwners.Remove(owner);
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
            // Only suppress a portal the mover actually lands inside. When the arrival cell is beside the
            // portal, suppressing it would make the first step back onto it silently do nothing.
            suppressedPortal = portalToSuppress != null && portalToSuppress.ContainsCell(cell)
                ? portalToSuppress
                : null;
            transform.position = destinationWorld.CellToWorld(cell, elevation);
            Teleported?.Invoke(this, cell);
            return true;
        }

        private IEnumerator AnimateStep(Vector2Int origin, Vector2Int destination)
        {
            Vector3 from = world.CellToWorld(origin, elevation);
            Vector3 to = world.CellToWorld(destination, elevation);
            float stepDuration = isSliding ? slideStepDuration : CurrentStepDuration;

            // A diagonal crosses sqrt(2) times the distance of a cardinal step.
            // Scale its duration by the same amount so both directions have the
            // same world-space speed instead of diagonals moving faster.
            Vector2Int step = destination - origin;
            if (step.x != 0 && step.y != 0)
            {
                stepDuration *= Mathf.Sqrt(2f);
            }

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
                isSliding = false;
                moveRoutine = null;
                yield break;
            }

            currentCell = destination;
            isMoving = false;
            moveRoutine = null;

            // Resolve portals before MoveCompleted: held input chains the next step from that event,
            // and a mover that is already moving again is refused by GridPortal.TryEnter.
            GridPortal portal = world.GetPortalAt(currentCell);
            if (portal == null)
            {
                suppressedPortal = null;
            }
            else if (portal != suppressedPortal && portal.TryEnter(this))
            {
                // The portal raised Teleported; the step's destination is no longer where the mover is.
                isSliding = false;
                yield break;
            }

            // A slide continues while the mover stands on ice. The next step starts before MoveCompleted so
            // held input cannot steer mid-slide; it stops on a normal cell or in front of a blocked cell.
            // A zero-duration slide runs its remaining steps inside TryContinueSlide, so report this step's cell.
            Vector2Int arrivedCell = currentCell;
            if (isSliding && !(world.IsIce(currentCell) && TryContinueSlide(destination - origin)))
            {
                isSliding = false;
                SlideEnded?.Invoke(this, arrivedCell);
            }

            MoveCompleted?.Invoke(this, arrivedCell);
        }

        private bool TryContinueSlide(Vector2Int direction)
        {
            if (IsMovementLocked || !world.TryReserveStep(this, direction, out Vector2Int next))
            {
                return false;
            }

            destinationCell = next;
            isMoving = true;
            MoveStarted?.Invoke(this, destinationCell);
            moveRoutine = StartCoroutine(AnimateStep(currentCell, destinationCell));
            return true;
        }

        private void OnValidate()
        {
            walkMoveDuration = Mathf.Max(0f, walkMoveDuration);
            sprintMoveDuration = Mathf.Max(0f, sprintMoveDuration);
            slideStepDuration = Mathf.Max(0f, slideStepDuration);
        }
    }
}
