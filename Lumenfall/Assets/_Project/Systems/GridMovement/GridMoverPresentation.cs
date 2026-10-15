using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Connects a GridMover to a visual model without taking ownership of movement.
    /// The model faces each accepted step and an optional Animator reflects walk/sprint state.
    /// </summary>
    [RequireComponent(typeof(GridMover))]
    [DisallowMultipleComponent]
    public sealed class GridMoverPresentation : MonoBehaviour
    {
        [Header("Model")]
        [SerializeField] private Transform facingRoot;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float rotationSpeed = 720f;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string groundedParameter = "IsGrounded";
        [SerializeField] private string fallingParameter = "IsFalling";
        [SerializeField, Min(0f)] private float animationDampTime = 0.05f;
        [SerializeField, Min(0f)] private float walkAnimationSpeed = 1f;
        [SerializeField, Min(0f)] private float sprintAnimationSpeed = 2f;

        private GridMover mover;
        private Quaternion targetRotation;
        // The turn rate for the current step: at least rotationSpeed, and fast enough to face the new way within
        // TurnStepFraction of the step, so the character never walks sideways or backwards into a cell.
        private float turnSpeed;
        private const float TurnStepFraction = 0.5f;
        private float targetAnimationSpeed;
        private int speedParameterHash;
        private int groundedParameterHash;
        private int fallingParameterHash;
        private bool hasSpeedParameter;
        private bool hasGroundedParameter;
        private bool hasFallingParameter;

        // Setting a parameter on an Animator that is inactive or has no controller (as during a script reload in Play Mode)
        // only logs "Animator is not playing an AnimatorController".
        private bool CanAnimate => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;

        private void Reset()
        {
            facingRoot = transform;
            animator = GetComponentInChildren<Animator>();
        }

        private void Awake()
        {
            mover = GetComponent<GridMover>();
            if (facingRoot == null)
            {
                facingRoot = transform;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            targetRotation = facingRoot.rotation;
            CacheAnimatorParameters();
        }

        private void OnEnable()
        {
            mover.MoveStarted += HandleMoveStarted;
            mover.MoveCompleted += HandleMoveCompleted;
            mover.Teleported += HandleTeleported;

            targetAnimationSpeed = mover.IsMoving ? GetMovementAnimationSpeed() : 0f;
            SetGroundedState();
        }

        private void OnDisable()
        {
            mover.MoveStarted -= HandleMoveStarted;
            mover.MoveCompleted -= HandleMoveCompleted;
            mover.Teleported -= HandleTeleported;
            targetAnimationSpeed = 0f;

            if (CanAnimate && hasSpeedParameter)
            {
                animator.SetFloat(speedParameterHash, 0f);
            }
        }

        private void Update()
        {
            if (facingRoot != null && rotationSpeed > 0f)
            {
                facingRoot.rotation = Quaternion.RotateTowards(
                    facingRoot.rotation,
                    targetRotation,
                    Mathf.Max(rotationSpeed, turnSpeed) * Time.deltaTime);
            }

            if (CanAnimate && hasSpeedParameter)
            {
                animator.SetFloat(
                    speedParameterHash,
                    targetAnimationSpeed,
                    animationDampTime,
                    Time.deltaTime);
            }
        }

        private void HandleMoveStarted(GridMover movingActor, Vector2Int destination)
        {
            Vector3 from = movingActor.World.CellToWorld(movingActor.CurrentCell);
            Vector3 to = movingActor.World.CellToWorld(destination);
            Vector3 direction = to - from;
            direction.y = 0f;

            if (direction.sqrMagnitude > Mathf.Epsilon)
            {
                targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                float turnTime = movingActor.CurrentStepDuration * TurnStepFraction;
                turnSpeed = turnTime > 0f && facingRoot != null
                    ? Quaternion.Angle(facingRoot.rotation, targetRotation) / turnTime
                    : 0f;
            }

            targetAnimationSpeed = GetMovementAnimationSpeed();
        }

        private void HandleMoveCompleted(GridMover movingActor, Vector2Int destination)
        {
            // Held input can chain the next tile from another MoveCompleted
            // subscriber before this callback runs. Keep the locomotion animation
            // active when that next step has already started.
            targetAnimationSpeed = movingActor.IsMoving
                ? GetMovementAnimationSpeed()
                : 0f;
        }

        private void HandleTeleported(GridMover movingActor, Vector2Int destination)
        {
            targetAnimationSpeed = 0f;
        }

        private float GetMovementAnimationSpeed()
        {
            // A slide holds the idle pose; the character glides rather than walks across ice.
            if (mover.IsSliding)
            {
                return 0f;
            }

            return mover.MovementMode == GridMovementMode.Sprint
                ? sprintAnimationSpeed
                : walkAnimationSpeed;
        }

        private void CacheAnimatorParameters()
        {
            if (animator == null)
            {
                return;
            }

            speedParameterHash = Animator.StringToHash(speedParameter);
            groundedParameterHash = Animator.StringToHash(groundedParameter);
            fallingParameterHash = Animator.StringToHash(fallingParameter);

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == speedParameterHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParameter = true;
                }
                else if (parameter.nameHash == groundedParameterHash &&
                         parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasGroundedParameter = true;
                }
                else if (parameter.nameHash == fallingParameterHash &&
                         parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasFallingParameter = true;
                }
            }
        }

        private void SetGroundedState()
        {
            if (animator == null)
            {
                return;
            }

            if (hasGroundedParameter)
            {
                animator.SetBool(groundedParameterHash, true);
            }

            if (hasFallingParameter)
            {
                animator.SetBool(fallingParameterHash, false);
            }
        }

        private void OnValidate()
        {
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            animationDampTime = Mathf.Max(0f, animationDampTime);
            walkAnimationSpeed = Mathf.Max(0f, walkAnimationSpeed);
            sprintAnimationSpeed = Mathf.Max(0f, sprintAnimationSpeed);
        }
    }
}
