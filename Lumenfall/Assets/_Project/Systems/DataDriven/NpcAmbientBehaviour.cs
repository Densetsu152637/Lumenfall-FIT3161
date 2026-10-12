using System.Linq;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Keeps a standing NPC from looking frozen. Its idle loop starts at a random point and speed so a group does not
    /// move in sync, it plays a short gesture from its animator every so often, and it turns to face a player who walks
    /// up or talks to it, greeting them, then turns back to where it stood once they leave. <see cref="DynamicNpc"/>
    /// adds it to every NPC. NPCs that walk the grid are left to their movement code for facing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcAmbientBehaviour : MonoBehaviour
    {
        // Slightly beyond DynamicNpc's 2.01 talk range, so the NPC turns as the player arrives.
        private const float NoticeDistance = 2.6f;
        private const float ForgetDistance = 3.6f;
        private const float TurnTime = 0.18f;
        private const float ReturnDelay = 1.5f;
        private const float GreetingCooldown = 30f;
        private const float MaxGestureLength = 3.5f;
        private const float BlendTime = 0.25f;
        // Gesture states are played only if the NPC's controller has them: NPC_Chibi (cast) and NPC_Witch.
        private static readonly string[] Gestures = { "Happy", "Pointing Forward", "idle_02" };
        private static readonly string[] Greetings = { "Happy", "idle_02" };

        [Tooltip("Seconds between idle gestures, picked at random within this range.")]
        [SerializeField] private Vector2 gestureInterval = new Vector2(10f, 20f);

        private Animator animator;
        private GridMover mover;
        private Transform player;
        private Vector3 restEuler;
        private float modelYawOffset;
        private int idleState;
        private float turnVelocity;
        private float lastEngaged = float.NegativeInfinity;
        private float lastGreeting = float.NegativeInfinity;
        private float nextGesture;
        private float gestureEnds;
        private bool gesturing;
        private bool engaged;
        private bool desynced;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            mover = GetComponent<GridMover>();
            restEuler = transform.eulerAngles;
            // The model may sit rotated under this object; face the player with the model, not the root.
            if (animator != null) modelYawOffset = Mathf.DeltaAngle(transform.eulerAngles.y, animator.transform.eulerAngles.y);
            // Gestures must play in place.
            if (animator != null) animator.applyRootMotion = false;
            ScheduleGesture();
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f) return;
            if (!desynced) Desync();

            Transform target = Player();
            float distance = target == null ? float.MaxValue : FlatDistance(target.position, transform.position);
            bool talking = GameEventManager.Instance?.Runtime?.ActiveDialogue != null;
            bool wasEngaged = engaged;
            engaged = mover == null && distance <= (engaged || talking ? ForgetDistance : NoticeDistance);
            if (engaged) lastEngaged = Time.time;

            UpdateFacing(target);
            UpdateGestures(!wasEngaged && engaged && !talking);
        }

        private void Desync()
        {
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return;
            desynced = true;
            // Whatever the controller starts in is this NPC's idle; gestures blend back to it.
            idleState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            animator.Play(idleState, 0, Random.value);
            animator.speed = Random.Range(0.9f, 1.1f);
        }

        private void UpdateFacing(Transform target)
        {
            if (mover != null) return;
            float goal;
            if (engaged && target != null)
            {
                Vector3 toPlayer = target.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude < 0.0001f) return;
                goal = Quaternion.LookRotation(toPlayer).eulerAngles.y - modelYawOffset;
            }
            else if (Time.time - lastEngaged >= ReturnDelay)
            {
                goal = restEuler.y;
            }
            else
            {
                return;
            }

            float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, goal, ref turnVelocity, TurnTime);
            transform.rotation = Quaternion.Euler(restEuler.x, yaw, restEuler.z);
        }

        private void UpdateGestures(bool justNoticed)
        {
            if (animator == null || !animator.isActiveAndEnabled || !desynced) return;

            if (justNoticed && Time.time - lastGreeting >= GreetingCooldown && Greetings.Any(Play))
            {
                lastGreeting = Time.time;
                return;
            }

            if (gesturing)
            {
                if (Time.time < gestureEnds) return;
                animator.CrossFadeInFixedTime(idleState, BlendTime);
                gesturing = false;
                ScheduleGesture();
                return;
            }

            // Idle gestures only while nobody is standing with the NPC.
            if (engaged || Time.time < nextGesture) return;
            if (!Play(Gestures[Random.Range(0, Gestures.Length)])) ScheduleGesture();
        }

        private bool Play(string state)
        {
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash)) return false;
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == state);
            float length = clip == null ? 2f : clip.length / Mathf.Max(animator.speed, 0.01f);
            animator.CrossFadeInFixedTime(hash, BlendTime);
            gesturing = true;
            gestureEnds = Time.time + Mathf.Min(length, MaxGestureLength) - BlendTime;
            return true;
        }

        private void ScheduleGesture() => nextGesture = Time.time + Random.Range(gestureInterval.x, gestureInterval.y);

        private Transform Player()
        {
            if (player == null)
            {
                GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
                player = input == null ? null : input.transform;
            }

            return player;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
