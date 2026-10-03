using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Lumenfall.DataDriven;

namespace Lumenfall.GridMovement
{
    public enum GridPortalDestinationMode
    {
        PortalReference,
        WorldCell,
        ScenePortalId
    }

    /// <summary>
    /// A non-blocking, rectangular set of tiles that transfers a mover after it finishes entering.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GridPortal : MonoBehaviour
    {
        [Header("Entrance")]
        [SerializeField] private GridWorld world;
        [FormerlySerializedAs("useTransformAsCell")]
        [SerializeField] private bool useTransformAsAnchor = true;
        [FormerlySerializedAs("cell")]
        [SerializeField] private Vector2Int anchorCell;
        [SerializeField] private Vector2Int footprintOffset;
        [SerializeField] private Vector2Int footprintSize = Vector2Int.one;
        [SerializeField] private string portalId;

        [Header("Destination")]
        [SerializeField] private GridPortalDestinationMode destinationMode =
            GridPortalDestinationMode.PortalReference;
        [SerializeField] private GridPortal destinationPortal;
        [SerializeField] private GridWorld destinationWorld;
        [SerializeField] private Vector2Int destinationCell;
        [SerializeField] private string destinationScene;
        [SerializeField] private string destinationPortalId;
        [SerializeField] private PortalContentAsset content;
        [Tooltip("Maps the entered tile to the equivalent tile in the destination footprint.")]
        [SerializeField] private bool preserveEntryOffset = true;
        [SerializeField] private Vector2Int arrivalOffset;

        [Header("Editor Preview")]
        [SerializeField] private Color gizmoColour = new Color(0.75f, 0.25f, 1f, 0.75f);

        public GridWorld World => world;
        public Vector2Int AnchorCell => useTransformAsAnchor && world != null
            ? world.WorldToCell(transform.position)
            : anchorCell;
        public Vector2Int FirstCell => AnchorCell + footprintOffset;
        public Vector2Int Cell => FirstCell;
        public Vector2Int FootprintSize => new Vector2Int(Mathf.Max(1, footprintSize.x), Mathf.Max(1, footprintSize.y));
        public string PortalId => portalId;
        public GridPortalDestinationMode DestinationMode => destinationMode;
        public PortalContentAsset Content => content;

        public void Configure(PortalContentAsset portalContent, GridWorld targetWorld)
        {
            if (world != null) world.UnregisterPortal(this);
            content = portalContent;
            world = targetWorld;
            portalId = portalContent == null ? string.Empty : portalContent.id;
            anchorCell = portalContent == null ? Vector2Int.zero : portalContent.cell;
            footprintSize = portalContent == null ? Vector2Int.one : portalContent.ClampedFootprint;
            footprintOffset = Vector2Int.zero;
            useTransformAsAnchor = false;
            destinationMode = GridPortalDestinationMode.PortalReference;
            if (isActiveAndEnabled) RefreshFootprint();
        }

        public void Configure(string id, int width, int height, string scene, string targetId, Vector2Int offset)
        {
            content = null;
            portalId = id; footprintSize = new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
            destinationMode = GridPortalDestinationMode.ScenePortalId; destinationScene = scene;
            destinationPortalId = targetId; arrivalOffset = offset; ResolveWorld();
            if (isActiveAndEnabled) RefreshFootprint();
        }

        private void Reset()
        {
            world = GetComponentInParent<GridWorld>();
        }

        private void OnEnable()
        {
            ResolveWorld();
            if (world == null)
            {
                Debug.LogError($"{name} needs a GridWorld to register its portal tile.", this);
                return;
            }

            RefreshFootprint();
        }

        private void OnDisable()
        {
            if (world != null)
            {
                world.UnregisterPortal(this);
            }
        }

        public bool TryEnter(GridMover mover)
        {
            if (!isActiveAndEnabled || (content != null && content.reserved) ||
                mover == null || mover.IsMoving || mover.IsMovementLocked || mover.World != world ||
                !ContainsCell(mover.CurrentCell))
            {
                return false;
            }

            if (content != null)
            {
                Vector2Int entryOffset = preserveEntryOffset ? mover.CurrentCell - FirstCell : Vector2Int.zero;
                return DataDrivenWorldLoader.Instance != null &&
                    DataDrivenWorldLoader.Instance.SwitchArea(content, entryOffset);
            }

            switch (destinationMode)
            {
                case GridPortalDestinationMode.PortalReference:
                    return EnterReferencedPortal(mover);

                case GridPortalDestinationMode.WorldCell:
                    return EnterWorldCell(mover);

                case GridPortalDestinationMode.ScenePortalId:
                    Vector2Int entryOffset = preserveEntryOffset
                        ? mover.CurrentCell - FirstCell
                        : Vector2Int.zero;
                    if (string.IsNullOrWhiteSpace(destinationScene) ||
                        string.IsNullOrWhiteSpace(destinationPortalId))
                    {
                        Debug.LogError($"{name} needs both a destination scene and portal id.", this);
                        return false;
                    }
                    mover.BeginSceneTransition(
                        destinationScene,
                        destinationPortalId,
                        entryOffset,
                        arrivalOffset);
                    return true;

                default:
                    return false;
            }
        }

        public bool ContainsCell(Vector2Int candidate)
        {
            Vector2Int first = FirstCell;
            return candidate.x >= first.x && candidate.y >= first.y &&
                   candidate.x < first.x + FootprintSize.x &&
                   candidate.y < first.y + FootprintSize.y;
        }

        public IEnumerable<Vector2Int> EnumeratePortalCells()
        {
            Vector2Int first = FirstCell;
            for (int x = 0; x < FootprintSize.x; x++)
            {
                for (int y = 0; y < FootprintSize.y; y++)
                {
                    yield return first + new Vector2Int(x, y);
                }
            }
        }

        public Vector2Int GetArrivalCell(Vector2Int sourceEntryOffset)
        {
            Vector2Int mappedOffset = new Vector2Int(
                Mathf.Clamp(sourceEntryOffset.x, 0, FootprintSize.x - 1),
                Mathf.Clamp(sourceEntryOffset.y, 0, FootprintSize.y - 1));
            return FirstCell + mappedOffset;
        }

        public void RefreshFootprint()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResolveWorld();
            if (world != null)
            {
                world.UnregisterPortal(this);
                if (content == null || !content.reserved) world.RegisterPortal(this, EnumeratePortalCells());
            }
        }

        public static GridPortal FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            GridPortal[] allPortals = FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude);
            foreach (GridPortal portal in allPortals)
            {
                if (portal.isActiveAndEnabled && (portal.Content == null || !portal.Content.reserved) &&
                    string.Equals(portal.portalId, id, StringComparison.Ordinal))
                {
                    return portal;
                }
            }

            return null;
        }

        private bool EnterReferencedPortal(GridMover mover)
        {
            if (destinationPortal == null || !destinationPortal.isActiveAndEnabled || destinationPortal.World == null ||
                (destinationPortal.Content != null && destinationPortal.Content.reserved))
            {
                Debug.LogError($"{name} does not have a valid destination portal.", this);
                return false;
            }

            mover.SetMovementLocked(true);
            Vector2Int sourceOffset = preserveEntryOffset
                ? mover.CurrentCell - FirstCell
                : Vector2Int.zero;
            bool moved = mover.TryTeleportTo(
                destinationPortal.World,
                destinationPortal.GetArrivalCell(sourceOffset) + arrivalOffset,
                destinationPortal);
            mover.SetMovementLocked(false);

            if (!moved)
            {
                Debug.LogWarning($"{name}'s destination cell is blocked.", this);
            }

            return moved;
        }

        private bool EnterWorldCell(GridMover mover)
        {
            if (destinationWorld == null)
            {
                Debug.LogError($"{name} does not have a destination GridWorld.", this);
                return false;
            }

            Vector2Int sourceOffset = preserveEntryOffset
                ? mover.CurrentCell - FirstCell
                : Vector2Int.zero;
            Vector2Int targetCell = destinationCell + sourceOffset + arrivalOffset;
            GridPortal portalAtDestination = destinationWorld.GetPortalAt(targetCell);

            mover.SetMovementLocked(true);
            bool moved = mover.TryTeleportTo(destinationWorld, targetCell, portalAtDestination);
            mover.SetMovementLocked(false);

            if (!moved)
            {
                Debug.LogWarning($"{name}'s destination cell is blocked.", this);
            }

            return moved;
        }

        private void ResolveWorld()
        {
            if (world == null)
            {
                world = GetComponentInParent<GridWorld>();
            }

            if (world == null)
            {
                world = FindAnyObjectByType<GridWorld>();
            }
        }

        private void OnValidate()
        {
            footprintSize.x = Mathf.Max(1, footprintSize.x);
            footprintSize.y = Mathf.Max(1, footprintSize.y);

            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefreshFootprint();
            }
        }

        private void OnDrawGizmosSelected()
        {
            ResolveWorld();
            if (world == null)
            {
                return;
            }

            Gizmos.color = gizmoColour;
            float radius = world.CellSize * 0.35f;
            foreach (Vector2Int portalCell in EnumeratePortalCells())
            {
                Vector3 centre = world.CellToWorld(portalCell, 0.08f);
                Gizmos.DrawWireSphere(centre, radius);
                Gizmos.DrawLine(centre + Vector3.left * radius, centre + Vector3.right * radius);
                Gizmos.DrawLine(centre + Vector3.back * radius, centre + Vector3.forward * radius);
            }
        }
    }
}
