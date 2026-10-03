using System;
using System.Collections.Generic;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEngine;

/// <summary>Follows an actor at a fixed 2.5D angle, with camera-owned map framing and reversible occlusion.</summary>
public class CameraFollow2_5D : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Header("Position")]
    [SerializeField] private Vector3 offset = new Vector3(8f, 10f, -8f);
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 1.5f, 0f);
    [Header("Smoothing")]
    [Min(0f), SerializeField] private float smoothTime = 0.15f;
    [Tooltip("Fallback for transform teleports that bypass GridMover. Zero disables distance detection.")]
    [Min(0f), SerializeField] private float teleportDistance = 4f;
    [SerializeField] private bool reducedMotion;

    [Serializable]
    public sealed class MapFramingProfile
    {
        public string mapId;
        [Tooltip("Scale distance from focus without changing the viewing angle. Also scales orthographic size.")]
        [Min(0.1f)] public float distanceScale = 1f;
        public bool clampToGridBounds = true;
        [Min(0f)] public float insetCells = 2f;
    }

    [Header("Map Framing (provisional)")]
    [Tooltip("Optional explicit loader. Otherwise use the existing loader instance; no map schema changes.")]
    [SerializeField] private DataDrivenWorldLoader worldLoader;
    [SerializeField] private bool clampUnprofiledMaps;
    [SerializeField] private MapFramingProfile[] mapProfiles =
    {
        new MapFramingProfile { mapId = "B01.Ground", distanceScale = 0.75f },
        new MapFramingProfile { mapId = "B02.Ground", distanceScale = 0.75f },
        new MapFramingProfile { mapId = "B03.Ground", distanceScale = 0.75f },
        new MapFramingProfile { mapId = "B04.Ground", distanceScale = 0.75f }
    };

    [Header("Optional Occlusion")]
    [Tooltip("Disabled until area geometry has explicit CameraOccluder markers.")]
    [SerializeField] private bool occlusionEnabled;
    [SerializeField] private LayerMask occlusionLayers = ~0;
    [Min(0.01f), SerializeField] private float occlusionRadius = 0.15f;
    [SerializeField] private Renderer[] protectedRenderers = Array.Empty<Renderer>();

    private Vector3 currentVelocity;
    private Vector3 lastTargetPosition;
    private Transform followedTarget;
    private GridMover followedMover;
    private GridWorld framedWorld;
    private WorldAreaAsset framedArea;
    private MapDefinition framedMap;
    private DataDrivenWorldLoader framedLoader;
    private MapFramingProfile activeProfile;
    private bool needsSnap = true;
    private Camera attachedCamera;
    private float originalOrthographicSize;
    private readonly RaycastHit[] occlusionHits = new RaycastHit[32];
    private readonly HashSet<Renderer> hitRenderers = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, bool> hiddenRenderers = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> restoreList = new List<Renderer>();

    public Transform Target => target;
    public bool ReducedMotion => reducedMotion;

    private void OnEnable()
    {
        attachedCamera = GetComponent<Camera>();
        if (attachedCamera != null) originalOrthographicSize = attachedCamera.orthographicSize;
        UnsubscribeTarget();
        followedTarget = null;
        RefreshTarget();
        needsSnap = true;
    }

    private void OnDisable()
    {
        UnsubscribeTarget();
        followedTarget = null;
        currentVelocity = Vector3.zero;
        needsSnap = true;
        RestoreOcclusion();
        if (attachedCamera != null) attachedCamera.orthographicSize = originalOrthographicSize;
    }

    public void SetTarget(Transform value)
    {
        if (target == value) return;
        target = value;
        RefreshTarget();
        if (isActiveAndEnabled) SnapToTarget();
    }

    /// <summary>Accessibility adapters call this API when their persisted setting changes.</summary>
    public void SetReducedMotion(bool value)
    {
        if (reducedMotion == value) return;
        reducedMotion = value;
        currentVelocity = Vector3.zero;
        needsSnap = true;
        if (isActiveAndEnabled) SnapToTarget();
    }

    public void SetOcclusionEnabled(bool value)
    {
        occlusionEnabled = value;
        if (!value) RestoreOcclusion();
    }

    /// <summary>Reseeds after explicit scene/spawn changes. Does not alter gameplay or overlay input locks.</summary>
    public void SnapToTarget()
    {
        RefreshTarget();
        RefreshFraming();
        RestoreOcclusion();
        currentVelocity = Vector3.zero;
        needsSnap = true;
        if (target == null || !isActiveAndEnabled) return;
        transform.position = GetDesiredPosition();
        ApplyRotation();
        lastTargetPosition = target.position;
        needsSnap = false;
    }

    private void LateUpdate()
    {
        RefreshTarget();
        RefreshFraming();
        if (target == null) { RestoreOcclusion(); return; }
        if (CameraFramingMath.ShouldSnap((target.position - lastTargetPosition).sqrMagnitude, teleportDistance))
        { RestoreOcclusion(); needsSnap = true; }
        Vector3 desired = GetDesiredPosition();
        if (needsSnap || reducedMotion || smoothTime <= 0f)
        {
            transform.position = desired;
            currentVelocity = Vector3.zero;
            needsSnap = false;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref currentVelocity,
                smoothTime, Mathf.Infinity, Time.deltaTime);
        }
        ApplyRotation();
        lastTargetPosition = target.position;
        UpdateOcclusion();
    }

    private void RefreshTarget()
    {
        if (followedTarget == target) return;
        UnsubscribeTarget();
        RestoreOcclusion();
        followedTarget = target;
        followedMover = target == null ? null : target.GetComponentInParent<GridMover>();
        if (isActiveAndEnabled && followedMover != null) followedMover.Teleported += OnTeleported;
        currentVelocity = Vector3.zero;
        needsSnap = true;
    }

    private void UnsubscribeTarget()
    {
        if (followedMover != null) followedMover.Teleported -= OnTeleported;
        followedMover = null;
    }

    private void OnTeleported(GridMover mover, Vector2Int cell) => SnapToTarget();

    private void RefreshFraming()
    {
        DataDrivenWorldLoader loader = worldLoader != null ? worldLoader : DataDrivenWorldLoader.Instance;
        WorldAreaAsset area = loader == null ? null : loader.CurrentArea;
        MapDefinition map = loader == null ? null : loader.LoadedMap;
        GridWorld grid = followedMover == null ? null : followedMover.World;
        if (loader == framedLoader && area == framedArea && map == framedMap && grid == framedWorld) return;
        framedLoader = loader;
        framedArea = area;
        framedMap = map;
        framedWorld = grid;
        activeProfile = null;
        if (map != null)
            foreach (MapFramingProfile profile in mapProfiles ?? Array.Empty<MapFramingProfile>())
                if (profile != null && string.Equals(profile.mapId, map.id, StringComparison.Ordinal))
                { activeProfile = profile; break; }
        RestoreOcclusion();
        currentVelocity = Vector3.zero;
        needsSnap = true;
    }

    private float DistanceScale => activeProfile == null || !CameraFramingMath.IsFinite(activeProfile.distanceScale)
        ? 1f : Mathf.Max(0.1f, activeProfile.distanceScale);

    private Vector3 GetAnchor()
    {
        Vector3 anchor = target.position;
        bool clamp = activeProfile == null ? clampUnprofiledMaps : activeProfile.clampToGridBounds;
        if (!clamp || framedWorld == null || !framedWorld.RestrictToBounds) return anchor;
        // Clamp in grid-local coordinates; TransformPoint honors translation, rotation and scale.
        Vector3 local = framedWorld.transform.InverseTransformPoint(anchor);
        float inset = activeProfile == null || !CameraFramingMath.IsFinite(activeProfile.insetCells) ? 0f : activeProfile.insetCells;
        local.x = CameraFramingMath.ClampCoordinate(local.x, framedWorld.BoundsMin.x, Mathf.Max(1, framedWorld.BoundsSize.x), framedWorld.CellSize, inset);
        local.z = CameraFramingMath.ClampCoordinate(local.z, framedWorld.BoundsMin.y, Mathf.Max(1, framedWorld.BoundsSize.y), framedWorld.CellSize, inset);
        return framedWorld.transform.TransformPoint(local);
    }

    private Vector3 GetDesiredPosition()
    {
        float scale = DistanceScale;
        if (attachedCamera != null && attachedCamera.orthographic)
            attachedCamera.orthographicSize = originalOrthographicSize * scale;
        return GetAnchor() + focusOffset + (offset - focusOffset) * scale;
    }

    private void ApplyRotation()
    {
        Vector3 direction = focusOffset - offset;
        if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private void UpdateOcclusion()
    {
        if (!occlusionEnabled) { RestoreOcclusion(); return; }
        hitRenderers.Clear();
        Vector3 segment = target.position + focusOffset - transform.position;
        float distance = segment.magnitude;
        if (distance > 0.001f)
        {
            int count = Physics.SphereCastNonAlloc(transform.position, Mathf.Max(0.01f, occlusionRadius),
                segment / distance, occlusionHits, distance, occlusionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!occlusionHits[i].collider.TryGetComponent(out CameraOccluder occluder) || !occluder.isActiveAndEnabled) continue;
                foreach (Renderer renderer in occluder.Renderers)
                {
                    if (renderer == null || renderer.transform.IsChildOf(target) ||
                        (followedMover != null && renderer.transform.IsChildOf(followedMover.transform)) || IsProtected(renderer)) continue;
                    hitRenderers.Add(renderer);
                    if (!hiddenRenderers.ContainsKey(renderer)) hiddenRenderers.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
        }
        restoreList.Clear();
        foreach (KeyValuePair<Renderer, bool> entry in hiddenRenderers)
            if (entry.Key == null || !hitRenderers.Contains(entry.Key)) restoreList.Add(entry.Key);
        foreach (Renderer renderer in restoreList)
        {
            if (renderer != null) renderer.forceRenderingOff = hiddenRenderers[renderer];
            hiddenRenderers.Remove(renderer);
        }
    }

    private bool IsProtected(Renderer renderer)
    {
        foreach (Renderer item in protectedRenderers ?? Array.Empty<Renderer>()) if (item == renderer) return true;
        return false;
    }

    private void RestoreOcclusion()
    {
        foreach (KeyValuePair<Renderer, bool> entry in hiddenRenderers)
            if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
        hiddenRenderers.Clear();
        hitRenderers.Clear();
        restoreList.Clear();
    }
}
