using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;

namespace Lumenfall.DataDriven
{
    [Serializable] public sealed class ChapterOneWorldBindings
    {
        public string dormMapId;
        public MapRectDefinition door;
        public MapRectDefinition suite;
        public MapRectDefinition room;
        public SpawnDefinition bed;
        public ChapterOneNpcBinding[] npcs = Array.Empty<ChapterOneNpcBinding>();
    }
    [Serializable] public sealed class ChapterOneNpcBinding
    { public string id; public string displayName; public string mapId; public int x; public int y; }

    /// <summary>Data-authored chapter interaction points on the existing live area prefabs.</summary>
    [DisallowMultipleComponent]
    public sealed class ChapterOneInteraction : MonoBehaviour
    {
        private ChapterOneWorldBindings bindings;
        private MapDefinition map;
        private GridWorld world;
        private GameEventRuntime runtime;
        private Func<Vector2Int, bool> permission;
        private Func<string, bool> dialoguePermission;
        private Func<string, bool> transitionPermission;
        private GridMover sleeper;
        private GridMover cachedPlayer;
        private GridMover Player
        {
            get
            {
                if (cachedPlayer != null) return cachedPlayer;
                GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
                if (input != null) return cachedPlayer = input.GetComponent<GridMover>();
                // Bare movers remain supported by offline/EditMode fixtures; authored NPC navigators are excluded.
                foreach (GridMover mover in FindObjectsByType<GridMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (mover.GetComponent<GridNpcNavigator>() == null) return cachedPlayer = mover;
                return null;
            }
        }
        private float elapsed;
        private bool transitioning;
        private float darkness;
        public ChapterOneWorldBindings Bindings => bindings;

        public void BindArea(MapDefinition definition, GameObject root, GridWorld grid)
        {
            EnsureInitialized();
            if (map != definition) CancelSleep();
            if (world != null && permission != null) world.RemoveCellPermission(permission);
            map = definition;
            world = grid;
            if (world != null && bindings != null)
            {
                if (permission == null) permission = IsCellPermitted;
                if (isActiveAndEnabled) world.AddCellPermission(permission);
            }
            if (root == null || bindings == null || world == null || definition == null) return;
            EnsureBedMarker(root);
            foreach (ChapterOneNpcBinding binding in bindings.npcs)
            {
                if (binding.mapId != definition.id) continue;
                bool exists = false;
                foreach (DynamicNpc npc in root.GetComponentsInChildren<DynamicNpc>(true))
                    if (npc.Definition != null && npc.Definition.id == binding.id) { exists = true; break; }
                if (exists) continue;
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                marker.name = binding.displayName + " (Chapter 1 interaction)";
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = world.CellToWorld(new Vector2Int(binding.x, binding.y), 0.7f);
                marker.transform.localScale = new Vector3(0.55f, 0.7f, 0.55f);
                marker.GetComponent<Collider>().isTrigger = true;
                DynamicNpc dynamicNpc = marker.AddComponent<DynamicNpc>();
                dynamicNpc.Configure(new NpcDefinition { id = binding.id, displayName = binding.displayName,
                    mapId = binding.mapId, x = binding.x, y = binding.y }, null);
                GameObject labelObject = new GameObject("Interaction label");
                labelObject.transform.SetParent(marker.transform, false);
                labelObject.transform.localPosition = new Vector3(0, 1.5f, 0);
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.text = binding.displayName;
                label.characterSize = 0.18f;
                label.anchor = TextAnchor.MiddleCenter;
                label.color = Color.white;
            }
        }

        private void EnsureBedMarker(GameObject root)
        {
            if (map.id != bindings.dormMapId || bindings.bed == null || root.transform.Find("Player bed (Chapter 1)") != null) return;
            GameObject bed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bed.name = "Player bed (Chapter 1)";
            bed.transform.SetParent(root.transform, false);
            bed.transform.position = world.CellToWorld(new Vector2Int(bindings.bed.x, bindings.bed.y), 0.25f);
            bed.transform.localScale = new Vector3(1.1f, 0.5f, 1.8f);
            bed.GetComponent<Collider>().isTrigger = true;
            GameObject pillow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillow.name = "Pillow";
            pillow.transform.SetParent(bed.transform, false);
            pillow.transform.localPosition = new Vector3(0, 0.6f, 0.3f);
            pillow.transform.localScale = new Vector3(0.8f, 0.25f, 0.25f);
            pillow.GetComponent<Collider>().isTrigger = true;
        }
        private void EnsureInitialized()
        {
            GameEventRuntime nextRuntime = GameEventManager.Instance.Runtime;
            if (runtime == nextRuntime) return;
            if (runtime != null)
            { runtime.StateRestored -= OnStateRestored; runtime.ChapterOne.RemoveDialoguePermission(dialoguePermission); runtime.ChapterOne.RemoveTransitionPermission(transitionPermission); }
            CancelSleep();
            runtime = nextRuntime;
            if (dialoguePermission == null) dialoguePermission = CanInteractDialogue;
            if (transitionPermission == null) transitionPermission = CanTransition;
            if (isActiveAndEnabled)
            { runtime.StateRestored += OnStateRestored; runtime.ChapterOne.AddDialoguePermission(dialoguePermission); runtime.ChapterOne.AddTransitionPermission(transitionPermission); }
            TextAsset asset = Resources.Load<TextAsset>("ChapterOne/world_story_bindings");
            if (asset != null) bindings = JsonUtility.FromJson<ChapterOneWorldBindings>(asset.text);
            else Debug.LogError("Chapter 1 world interaction bindings are missing.", this);
        }
        public bool IsCellPermitted(Vector2Int cell)
        {
            if (bindings == null || map == null || map.id != bindings.dormMapId || !runtime.ChapterOne.IsAvailable) return true;
            ChapterOneStory story = runtime.ChapterOne;
            if ((Contains(bindings.door, cell) || Contains(bindings.suite, cell) || Contains(bindings.room, cell)) && !story.CanEnterSuite)
                return false;
            if (Contains(bindings.room, cell) && story.Stage < ChapterOneStage.ApproachRoom) return false;
            return true;
        }
        private static bool Contains(MapRectDefinition rect, Vector2Int cell) =>
            rect != null && cell.x >= rect.x && cell.x < rect.x + rect.width &&
            cell.y >= rect.y && cell.y < rect.y + rect.height;
        private void Update()
        {
            EnsureInitialized();
            if (runtime == null || bindings == null || world == null || map == null) return;
            GridMover player = Player;
            if (player == null) return;
            ChapterOneStory story = runtime.ChapterOne;
            if (transitioning)
            {
                if (story.Stage != ChapterOneStage.Sleeping || map.id != bindings.dormMapId ||
                    !Contains(bindings.room, player.CurrentCell) || sleeper != player) { CancelSleep(); return; }
                elapsed += Time.unscaledDeltaTime;
                darkness = elapsed < 0.8f ? Mathf.Clamp01(elapsed / 0.8f) :
                    elapsed < 1.8f ? 1f : 1f - Mathf.Clamp01((elapsed - 1.8f) / 0.8f);
                if (elapsed >= 2.6f)
                {
                    bool woke = story.TryWake();
                    CancelSleep();
                    if (woke) runtime.StartDialogue("chapter1.wake");
                }
                return;
            }
            if (map.id != bindings.dormMapId || player.IsMoving || Time.timeScale <= 0f ||
                InventoryUiState.IsOpen || runtime.ActiveDialogue != null) return;
            if (story.Stage == ChapterOneStage.Sleeping)
            { if (Contains(bindings.room, player.CurrentCell)) BeginSleep(player); return; }
            if (story.Stage == ChapterOneStage.FollowToSuite &&
                (Contains(bindings.door, player.CurrentCell) || Contains(bindings.suite, player.CurrentCell)))
                story.TryEnterSuite();
            if (story.Stage == ChapterOneStage.SuiteEntered)
                runtime.StartDialogue("chapter1.guide");
            if (story.Stage == ChapterOneStage.ApproachRoom && Contains(bindings.room, player.CurrentCell))
                story.TryReachRoom();
            if (story.Stage == ChapterOneStage.ReadyForSleep && NearBed(player) &&
                Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                TryInteractBed(player);
        }
        public bool TryInteractBed(GridMover player)
        {
            EnsureInitialized();
            if (!isActiveAndEnabled || player == null || player != Player || world == null || player.World != world ||
                map == null || bindings == null || map.id != bindings.dormMapId || player.IsMoving ||
                player.IsMovementLocked || InventoryUiState.IsOpen || Time.timeScale <= 0f ||
                runtime.ActiveDialogue != null || runtime.ChapterOne.Stage != ChapterOneStage.ReadyForSleep ||
                !Contains(bindings.room, player.CurrentCell) || !NearBed(player)) return false;
            return runtime.StartDialogue("chapter1.sleep_prompt") != null;
        }
        private bool CanTransition(string id)
        {
            if (id == "chapter1.first_account" || id == "chapter1.key_handover" ||
                id == "chapter1.group_discussion" || id == "chapter1.room_guidance")
            {
                string dialogue = id == "chapter1.first_account" ? "chapter1.reception_first" :
                    id == "chapter1.key_handover" ? "chapter1.reception_handover" :
                    id == "chapter1.group_discussion" ? "chapter1.group" : "chapter1.guide";
                return CanInteractDialogue(dialogue);
            }
            GridMover player = Player;
            if (player == null || bindings == null || map == null || world == null ||
                map.id != bindings.dormMapId || player.World != world || player.IsMoving) return false;
            switch (id)
            {
                case "chapter1.suite_entered":
                    return Contains(bindings.door, player.CurrentCell) || Contains(bindings.suite, player.CurrentCell);
                case "chapter1.room_arrival": return Contains(bindings.room, player.CurrentCell);
                case "chapter1.sleep": return Contains(bindings.room, player.CurrentCell) && NearBed(player);
                case "chapter1.complete": return transitioning && elapsed >= 2.6f && darkness <= 0f &&
                    Contains(bindings.room, player.CurrentCell);
                default: return false;
            }
        }
        private bool CanInteractDialogue(string id)
        {
            if (map == null || bindings == null || world == null || InventoryUiState.IsOpen || Time.timeScale <= 0f) return false;
            GridMover player = Player;
            if (player == null || player.World != world || player.IsMoving) return false;
            if (id == "chapter1.sleep_prompt")
                return map.id == bindings.dormMapId && Contains(bindings.room, player.CurrentCell) && NearBed(player);
            if (id == "chapter1.guide")
                return map.id == bindings.dormMapId && (Contains(bindings.door, player.CurrentCell) || Contains(bindings.suite, player.CurrentCell));
            if (id == "chapter1.wake") return map.id == bindings.dormMapId && Contains(bindings.room, player.CurrentCell);
            string npcId = id == "chapter1.reception_first" || id == "chapter1.reception_handover" ? "receptionist" :
                id == "chapter1.group" ? "sirius_group" : null;
            if (npcId == null) return true;
            foreach (ChapterOneNpcBinding npc in bindings.npcs)
                if (npc.id == npcId) return map.id == npc.mapId &&
                    Vector2Int.Distance(player.CurrentCell, new Vector2Int(npc.x, npc.y)) <= 2.01f;
            return false;
        }
        private bool NearBed(GridMover player) => bindings.bed != null &&
            Vector2Int.Distance(player.CurrentCell, new Vector2Int(bindings.bed.x, bindings.bed.y)) <= 1.5f;
        private void BeginSleep(GridMover player)
        { sleeper = player; sleeper.SetMovementLocked(true); transitioning = true; elapsed = 0; darkness = 0; }
        private void CancelSleep()
        {
            if (sleeper != null) sleeper.SetMovementLocked(false);
            sleeper = null; transitioning = false; elapsed = 0; darkness = 0;
        }
        private void OnStateRestored() { CancelSleep(); }
        private void OnEnable()
        {
            EnsureInitialized();
            runtime.StateRestored -= OnStateRestored;
            runtime.StateRestored += OnStateRestored;
            runtime.ChapterOne.AddDialoguePermission(dialoguePermission); runtime.ChapterOne.AddTransitionPermission(transitionPermission);
            if (world != null && permission != null) world.AddCellPermission(permission);
        }
        private void OnDisable()
        {
            if (world != null && permission != null) world.RemoveCellPermission(permission);
            if (runtime != null)
            { runtime.StateRestored -= OnStateRestored; runtime.ChapterOne.RemoveDialoguePermission(dialoguePermission); runtime.ChapterOne.RemoveTransitionPermission(transitionPermission); }
            CancelSleep();
        }
        private void OnDestroy()
        { if (runtime != null) { runtime.StateRestored -= OnStateRestored; runtime.ChapterOne.RemoveDialoguePermission(dialoguePermission); runtime.ChapterOne.RemoveTransitionPermission(transitionPermission); } }
        private void OnGUI()
        {
            if (transitioning)
            {
                Color previous = GUI.color;
                GUI.color = new Color(0, 0, 0, darkness);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
                return;
            }
            if (runtime == null || bindings == null || map == null ||
                InventoryUiState.IsOpen || Time.timeScale <= 0f || runtime.ActiveDialogue != null) return;
            GridMover player = Player;
            if (player == null) return;
            GUI.Box(new Rect(20, 70, Mathf.Min(700, Screen.width - 40), 42), runtime.ChapterOne.CurrentObjective);
            if (map.id != bindings.dormMapId) return;
            string hint = null;
            if (!runtime.ChapterOne.CanEnterSuite &&
                Vector2Int.Distance(player.CurrentCell, new Vector2Int(bindings.door.x, bindings.door.y)) <= 3f)
                hint = runtime.ChapterOne.HasKey ? "Talk with the group in the Sirius common area first." : "The residential door needs the Sirius suite key.";
            if (runtime.ChapterOne.Stage == ChapterOneStage.ReadyForSleep && NearBed(player)) hint = "E: Rest until morning";
            if (hint != null) GUI.Box(new Rect((Screen.width - 440) / 2f, 24, 440, 36), hint);
        }
    }
}
