using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Connects a GridMover to a visual model without taking ownership of movement.
    /// The model faces each attempted step and an optional Animator reflects walk/sprint state.
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
        // A facing held by a cutscene (the stare into the camera), turned to at its own pace, over the step facing.
        private bool facingHeld;
        private Quaternion heldRotation;
        private float heldTurnSpeed;
        private Transform heldTarget;
        // The head turns up to the target as the body comes round, after the animator has posed it each frame.
        private const float MaxHeadAngle = 60f;
        private Transform head;
        private bool headSearched;
        private float headWeight, headWeightSpeed;
        private Quaternion headBase, headApplied;
        private bool headPosed;
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
            mover.MoveBlocked += HandleMoveBlocked;
            mover.MoveCompleted += HandleMoveCompleted;
            mover.Teleported += HandleTeleported;

            targetAnimationSpeed = mover.IsMoving ? GetMovementAnimationSpeed() : 0f;
            SetGroundedState();
        }

        private void OnDisable()
        {
            mover.MoveStarted -= HandleMoveStarted;
            mover.MoveBlocked -= HandleMoveBlocked;
            mover.MoveCompleted -= HandleMoveCompleted;
            mover.Teleported -= HandleTeleported;
            targetAnimationSpeed = 0f;

            if (CanAnimate && hasSpeedParameter)
            {
                animator.SetFloat(speedParameterHash, 0f);
            }
        }

        /// <summary>
        /// Turns the model to face <paramref name="target"/> over <paramref name="seconds"/> at an even pace, following it
        /// if it moves, and lifts the head to look straight at it, until <see cref="ReleaseFacing"/>, whatever steps are
        /// attempted meanwhile. Used for the player staring into the camera.
        /// </summary>
        public void HoldFacing(Transform target, float seconds)
        {
            if (facingRoot == null || target == null) return;
            Vector3 direction = target.position - facingRoot.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= Mathf.Epsilon) return;
            heldTarget = target;
            heldRotation = Quaternion.LookRotation(direction, Vector3.up);
            float angle = Quaternion.Angle(facingRoot.rotation, heldRotation);
            heldTurnSpeed = seconds > 0f ? angle / seconds : float.PositiveInfinity;
            headWeight = 0f;
            headWeightSpeed = seconds > 0f ? 1f / seconds : float.PositiveInfinity;
            facingHeld = true;
        }

        /// <summary>Ends <see cref="HoldFacing"/>: the model faces its last step direction again at once, head and all.</summary>
        public void ReleaseFacing()
        {
            if (!facingHeld) return;
            facingHeld = false;
            heldTarget = null;
            headWeight = 0f;
            RestoreHead();
            if (facingRoot != null) facingRoot.rotation = targetRotation;
        }

        private void LateUpdate()
        {
            if (!facingHeld || heldTarget == null || facingRoot == null) return;
            Transform bone = FindHead();
            if (bone == null) return;
            // Without an animator posing the head each frame, start again from its own pose rather than adding up turns.
            if (headPosed && bone.localRotation == headApplied) bone.localRotation = headBase;
            headBase = bone.localRotation;
            headWeight = Mathf.MoveTowards(headWeight, 1f, headWeightSpeed * Time.unscaledDeltaTime);
            Vector3 toTarget = heldTarget.position - bone.position;
            if (toTarget.sqrMagnitude <= Mathf.Epsilon) return;
            // The turn that takes the body's facing onto the target, applied to the head: mostly a lift towards the camera.
            Quaternion turn = Quaternion.FromToRotation(facingRoot.forward, toTarget.normalized);
            turn.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -MaxHeadAngle, MaxHeadAngle) * headWeight;
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
            headApplied = bone.localRotation;
            headPosed = true;
        }

        private void RestoreHead()
        {
            if (head != null && headPosed && head.localRotation == headApplied) head.localRotation = headBase;
            headPosed = false;
        }

        // The head bone: the humanoid one if the rig has it, otherwise the first bone named "Head".
        private Transform FindHead()
        {
            if (headSearched) return head;
            headSearched = true;
            if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
            Transform root = animator != null ? animator.transform : facingRoot;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name == "head" || name.EndsWith(":head") || name.EndsWith("_head") || name.EndsWith(" head") || name == "bip001 head")
                {
                    head = child;
                    break;
                }
            }
            return head;
        }

        private void Update()
        {
            if (facingRoot != null && facingHeld)
            {
                if (heldTarget != null)
                {
                    Vector3 direction = heldTarget.position - facingRoot.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > Mathf.Epsilon) heldRotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                facingRoot.rotation = Quaternion.RotateTowards(facingRoot.rotation, heldRotation, heldTurnSpeed * Time.unscaledDeltaTime);
            }
            else if (facingRoot != null && rotationSpeed > 0f)
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
            FaceDestination(movingActor, destination);
            targetAnimationSpeed = GetMovementAnimationSpeed();
        }

        private void HandleMoveBlocked(GridMover movingActor, Vector2Int destination)
        {
            FaceDestination(movingActor, destination);
        }

        private void FaceDestination(GridMover movingActor, Vector2Int destination)
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
