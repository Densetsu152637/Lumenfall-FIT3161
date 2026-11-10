using System.Collections.Generic;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Rendering;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// The NPCs a cutscene moves (the <c>npcWalk</c>, <c>npcPose</c>, <c>npcTurn</c> and <c>npcLeave</c> steps) and the
    /// letter that flutters between characters (<c>letter</c>). NPCs have no movement of their own, so a walk glides the
    /// NPC from cell to cell along a <see cref="GridPathfinder"/> path at the player's walking pace, playing the walk or
    /// run animation and turning to face where they are going. A cell off the map (or with no path) is walked to in a
    /// straight line, so an NPC can walk out of sight through a gate. While a cutscene has them, an NPC's idle gestures
    /// and turning to the player are off; when it ends, they stand where it left them and take that as their resting
    /// place. Everything runs on unscaled time.
    /// </summary>
    internal sealed class CutsceneActors
    {
        public const float WalkCellsPerSecond = 3.2f;
        public const float RunCellsPerSecond = 6.5f;
        private const float Blend = 0.2f;
        private const string IdleState = "Idle";
        private static readonly Color Paper = new Color(0.97f, 0.94f, 0.86f);
        private static readonly Color Seal = new Color(0.72f, 0.14f, 0.16f);

        private sealed class Actor
        {
            public DynamicNpc Npc;
            public Transform Root;
            public Animator Animator;
            public NpcAmbientBehaviour Ambient;
            public GridObstacle Obstacle;
            public float ModelYaw;
            public readonly List<Vector3> Path = new List<Vector3>();
            public float Speed;
            public bool Walking;
            public float TurnFrom, TurnTo, TurnSeconds, TurnElapsed;
            public bool Turning;
            public Vector3 Scale;
            public float LeaveSeconds, LeaveElapsed;
            public bool Leaving, Gone;
            public string Bubble;
            public float BubbleShown, BubbleUntil;
            public bool BubbleWhileWalking;
        }

        private const float BubbleFadeSeconds = 0.15f;
        private Texture2D bubbleBackground;

        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>();
        private readonly List<Vector2Int> cells = new List<Vector2Int>();

        private GameObject letter;
        private Vector3 letterFrom;
        private Transform letterTarget;
        private bool letterToPlayer, letterHeld;
        private float letterSeconds, letterElapsed;

        public bool IsLetterFlying => letter != null && !letterHeld && letterElapsed < letterSeconds;

        public bool IsWalking(string npcId) => actors.TryGetValue(npcId, out Actor actor) && actor.Walking;

        public bool Walk(string npcId, Vector2Int cell, bool hurry)
        {
            Actor actor = Get(npcId);
            GridWorld world = World();
            if (actor == null || world == null) return false;
            // Its own blocker would stand in its way; it is put back where the NPC stops.
            if (actor.Obstacle != null) actor.Obstacle.enabled = false;
            actor.Path.Clear();
            Vector2Int from = world.WorldToCell(actor.Root.position);
            bool pathed = InBounds(world, cell) && GridPathfinder.TryFindPath(world, from, cell, cells, true, 8192) && cells.Count > 0;
            if (pathed) foreach (Vector2Int step in cells) actor.Path.Add(world.CellToWorld(step));
            else actor.Path.Add(world.CellToWorld(cell));
            actor.Speed = (hurry ? RunCellsPerSecond : WalkCellsPerSecond) * world.CellSize;
            actor.Walking = true;
            actor.Turning = false;
            Play(actor, hurry ? "Run" : "Walk");
            return true;
        }

        public void Pose(string npcId, string state)
        {
            Actor actor = Get(npcId);
            if (actor == null) return;
            Play(actor, string.IsNullOrEmpty(state) ? IdleState : state);
        }

        public void Turn(string npcId, Vector2Int direction, float seconds)
        {
            Actor actor = Get(npcId);
            if (actor == null) return;
            Vector3 facing = new Vector3(direction.x, 0f, direction.y);
            if (direction == Vector2Int.zero)
            {
                GridMover player = GridMover.FindActive();
                if (player == null) return;
                facing = player.transform.position - actor.Root.position;
                facing.y = 0f;
            }
            if (facing.sqrMagnitude < 0.0001f) return;
            actor.TurnFrom = actor.Root.eulerAngles.y;
            actor.TurnTo = YawFor(actor, facing);
            actor.TurnSeconds = Mathf.Max(0f, seconds);
            actor.TurnElapsed = 0f;
            actor.Turning = true;
            if (actor.TurnSeconds <= 0f) FinishTurn(actor);
        }

        /// <summary>
        /// A speech bubble over the NPC for <paramref name="seconds"/> (or until they stop walking, with
        /// <paramref name="whileWalking"/>); a new line replaces the last.
        /// </summary>
        public void Say(string npcId, string text, float seconds, bool whileWalking)
        {
            Actor actor = Get(npcId);
            if (actor == null || string.IsNullOrEmpty(text)) return;
            float now = Time.unscaledTime;
            actor.Bubble = text;
            actor.BubbleShown = now;
            actor.BubbleUntil = now + (seconds > 0f ? seconds : 60f);
            actor.BubbleWhileWalking = whileWalking;
        }

        public void Leave(string npcId, float seconds)
        {
            Actor actor = Get(npcId);
            if (actor == null || actor.Gone) return;
            actor.Leaving = true;
            actor.LeaveSeconds = Mathf.Max(0f, seconds);
            actor.LeaveElapsed = 0f;
            actor.Scale = actor.Root.localScale;
            if (actor.LeaveSeconds <= 0f) Vanish(actor);
        }

        public bool StartLetter(string npcId, bool toPlayer, float seconds)
        {
            Actor actor = Get(npcId);
            GridMover player = GridMover.FindActive();
            if (actor == null || player == null) return false;
            if (letter == null) letter = BuildLetter();
            letterFrom = toPlayer ? actor.Root.position + Vector3.up * 0.9f : (letter.activeSelf ? letter.transform.position : HandsOf(player.transform));
            letterTarget = toPlayer ? player.transform : actor.Root;
            letterToPlayer = toPlayer;
            letterHeld = false;
            letterSeconds = Mathf.Max(0.05f, seconds);
            letterElapsed = 0f;
            letter.SetActive(true);
            letter.transform.position = letterFrom;
            return true;
        }

        public void Tick(float deltaTime)
        {
            foreach (Actor actor in actors.Values)
            {
                if (actor.Root == null) continue;
                if (actor.Walking) StepAlong(actor, deltaTime);
                if (actor.Bubble != null && (Time.unscaledTime >= actor.BubbleUntil || (actor.BubbleWhileWalking && !actor.Walking)))
                    actor.Bubble = null;
                if (actor.Turning)
                {
                    actor.TurnElapsed += deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(actor.TurnElapsed / actor.TurnSeconds));
                    actor.Root.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(actor.TurnFrom, actor.TurnTo, t), 0f);
                    if (t >= 1f) FinishTurn(actor);
                }
                if (actor.Leaving)
                {
                    actor.LeaveElapsed += deltaTime;
                    float t = Mathf.Clamp01(actor.LeaveElapsed / actor.LeaveSeconds);
                    actor.Root.localScale = actor.Scale * (1f - t * t);
                    if (t >= 1f) Vanish(actor);
                }
            }
            TickLetter(deltaTime);
        }

        /// <summary>Finishes everything at once: walkers stand at their destinations, the leaving are gone, the letter is put away.</summary>
        public void Finish()
        {
            foreach (Actor actor in actors.Values)
            {
                if (actor.Root == null) continue;
                if (actor.Walking)
                {
                    actor.Root.position = actor.Path[actor.Path.Count - 1];
                    EndWalk(actor);
                }
                if (actor.Turning) FinishTurn(actor);
                if (actor.Leaving) Vanish(actor);
                if (actor.Obstacle != null && !actor.Gone) actor.Obstacle.enabled = true;
                if (actor.Ambient != null && !actor.Gone)
                {
                    actor.Ambient.Rest();
                    actor.Ambient.enabled = true;
                }
            }
            actors.Clear();
            if (bubbleBackground != null) Object.Destroy(bubbleBackground);
            bubbleBackground = null;
            if (letter != null)
            {
                foreach (Renderer part in letter.GetComponentsInChildren<Renderer>(true)) Object.Destroy(part.material);
                Object.Destroy(letter);
            }
            letter = null;
        }

        private void StepAlong(Actor actor, float deltaTime)
        {
            float travel = actor.Speed * deltaTime;
            while (travel > 0f && actor.Path.Count > 0)
            {
                Vector3 target = actor.Path[0];
                Vector3 offset = target - actor.Root.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > 0.001f) actor.Root.rotation = Quaternion.Slerp(actor.Root.rotation,
                    Quaternion.Euler(0f, YawFor(actor, offset), 0f), 1f - Mathf.Exp(-18f * deltaTime));
                if (distance <= travel)
                {
                    actor.Root.position = target;
                    travel -= distance;
                    if (actor.Path.Count == 1) break;
                    actor.Path.RemoveAt(0);
                    continue;
                }
                actor.Root.position += offset / distance * travel;
                travel = 0f;
            }
            if (actor.Path.Count == 1 && (actor.Root.position - actor.Path[0]).sqrMagnitude < 0.000001f) EndWalk(actor);
        }

        private void EndWalk(Actor actor)
        {
            actor.Walking = false;
            if (actor.Obstacle != null)
            {
                actor.Obstacle.enabled = true;
                actor.Obstacle.RefreshFootprint();
            }
            Play(actor, IdleState);
        }

        private static void FinishTurn(Actor actor)
        {
            actor.Turning = false;
            actor.Root.rotation = Quaternion.Euler(0f, actor.TurnTo, 0f);
        }

        private static void Vanish(Actor actor)
        {
            actor.Leaving = false;
            actor.Walking = false;
            actor.Gone = true;
            actor.Root.gameObject.SetActive(false);
        }

        private Actor Get(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            if (actors.TryGetValue(npcId, out Actor known) && known.Root != null) return known;
            DynamicNpc npc = null;
            foreach (DynamicNpc candidate in DynamicNpc.Active)
                if (candidate != null && candidate.Id == npcId && candidate.isActiveAndEnabled) { npc = candidate; break; }
            if (npc == null)
            {
                Debug.LogWarning($"Cutscene '{CutscenePlayer.CurrentId}' cannot move NPC '{npcId}': they are not in this area.");
                return null;
            }
            Actor actor = new Actor
            {
                Npc = npc,
                Root = npc.transform,
                Animator = npc.GetComponentInChildren<Animator>(),
                Ambient = npc.GetComponent<NpcAmbientBehaviour>(),
                Obstacle = npc.GetComponentInChildren<GridObstacle>()
            };
            if (actor.Animator != null) actor.ModelYaw = Mathf.DeltaAngle(actor.Root.eulerAngles.y, actor.Animator.transform.eulerAngles.y);
            if (actor.Ambient != null) actor.Ambient.enabled = false;
            if (actor.Animator != null) actor.Animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            actors[npcId] = actor;
            return actor;
        }

        private static float YawFor(Actor actor, Vector3 facing) => Quaternion.LookRotation(facing).eulerAngles.y - actor.ModelYaw;

        private static void Play(Actor actor, string state)
        {
            Animator animator = actor.Animator;
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return;
            int hash = Animator.StringToHash(state);
            if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, Blend);
            else if (state != IdleState) Debug.LogWarning($"NPC '{actor.Npc.Id}' has no animation state '{state}'.");
        }

        private static GridWorld World()
        {
            GridMover player = GridMover.FindActive();
            return player != null && player.World != null ? player.World : Object.FindAnyObjectByType<GridWorld>();
        }

        private static bool InBounds(GridWorld world, Vector2Int cell)
        {
            if (!world.RestrictToBounds) return true;
            Vector2Int min = world.BoundsMin, size = world.BoundsSize;
            return cell.x >= min.x && cell.y >= min.y && cell.x < min.x + size.x && cell.y < min.y + size.y;
        }

        // ---- Speech bubbles ----

        /// <summary>Draws each NPC's speech bubble above their head, in screen space, with IMGUI.</summary>
        public void DrawBubbles(GUIStyle style)
        {
            Camera view = Camera.main;
            if (view == null || style == null) return;
            if (bubbleBackground == null)
                bubbleBackground = Startup.MenuArt.Rounded(64, 64, 18f, new Color(0.98f, 0.95f, 0.88f), new Color(0.42f, 0.33f, 0.26f), 3f);
            style.normal.background = bubbleBackground;
            float scale = Screen.height / 1080f;
            Color previous = GUI.color;
            foreach (Actor actor in actors.Values)
            {
                if (actor.Bubble == null || actor.Root == null || !actor.Root.gameObject.activeInHierarchy) continue;
                Vector3 screen = view.WorldToScreenPoint(actor.Root.position + Vector3.up * 2.15f);
                if (screen.z <= 0f) continue;
                float age = Time.unscaledTime - actor.BubbleShown;
                float alpha = Mathf.Clamp01(age / BubbleFadeSeconds);
                GUIContent content = new GUIContent(actor.Bubble);
                float width = Mathf.Min(style.CalcSize(content).x, 460f * scale);
                float height = style.CalcHeight(content, width);
                // Rising a little as it appears, and kept on screen.
                float x = Mathf.Clamp(screen.x - width * 0.5f, 8f, Screen.width - width - 8f);
                float y = Mathf.Clamp(Screen.height - screen.y - height + (1f - alpha) * 10f * scale, 8f, Screen.height - height - 8f);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(x, y, width, height), content, style);
            }
            GUI.color = previous;
        }

        // ---- The letter ----

        private static Vector3 HandsOf(Transform holder) => holder.position + Vector3.up * 0.62f + holder.forward * 0.28f;

        private void TickLetter(float deltaTime)
        {
            if (letter == null || !letter.activeSelf) return;
            if (letterTarget == null) { letter.SetActive(false); return; }
            float time = Time.unscaledTime;
            if (letterHeld)
            {
                // Held up in front of them, with a small tremble.
                letter.transform.position = HandsOf(letterTarget) + Vector3.up * Mathf.Sin(time * 3f) * 0.01f;
                letter.transform.rotation = Quaternion.LookRotation(letterTarget.forward) * Quaternion.Euler(-70f, 0f, Mathf.Sin(time * 2.3f) * 4f);
                return;
            }

            letterElapsed += deltaTime;
            float t = Mathf.Clamp01(letterElapsed / letterSeconds);
            Vector3 to = letterToPlayer ? HandsOf(letterTarget) : letterTarget.position + Vector3.up * 0.9f;
            // Caught by the wind: up in a loose arc, swinging side to side and spinning as it drifts.
            Vector3 along = to - letterFrom;
            Vector3 side = Vector3.Cross(Vector3.up, along.sqrMagnitude > 0.0001f ? along.normalized : Vector3.forward);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            Vector3 position = Vector3.Lerp(letterFrom, to, eased) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.4f +
                side * Mathf.Sin(t * Mathf.PI * 3f) * 0.45f * (1f - t);
            letter.transform.position = position;
            letter.transform.rotation = Quaternion.Euler(Mathf.Sin(time * 9f) * 50f, time * 400f % 360f, Mathf.Cos(time * 7f) * 35f);
            if (t < 1f) return;
            if (letterToPlayer) letterHeld = true;
            else letter.SetActive(false);
        }

        private static GameObject BuildLetter()
        {
            GameObject root = new GameObject("Cutscene Letter");
            Part(PrimitiveType.Cube, "Envelope", root.transform, Vector3.zero, new Vector3(0.3f, 0.012f, 0.21f), Paper);
            Part(PrimitiveType.Cube, "Flap", root.transform, new Vector3(0f, 0.008f, 0.04f), new Vector3(0.21f, 0.004f, 0.12f), Paper * 0.92f);
            Part(PrimitiveType.Cylinder, "Seal", root.transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.06f, 0.004f, 0.06f), Seal);
            return root;
        }

        private static void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Color colour)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material material = renderer.material;
            material.color = colour;
            // A little of its own light, so it reads in the dark forest.
            material.SetColor("_EmissionColor", colour * 0.35f);
            material.EnableKeyword("_EMISSION");
        }
    }
}
