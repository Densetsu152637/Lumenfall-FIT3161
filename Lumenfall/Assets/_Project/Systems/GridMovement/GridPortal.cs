using System.Collections.Generic;
using UnityEngine;
using Lumenfall.DataDriven;
using UnityEngine.Serialization;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// A non-blocking, rectangular set of tiles that transfers a mover after it finishes entering.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GridPortal : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Header("Entrance")]
        [SerializeField] private GridWorld world;
        [Tooltip("Portal cells extending from the centre towards local grid +X. Zero includes only the centre cell.")]
        [SerializeField, Min(0)] private int extendPositiveX;
        [Tooltip("Portal cells extending from the centre towards local grid -X.")]
        [SerializeField, Min(0)] private int extendNegativeX;
        [Tooltip("Portal cells extending from the centre towards local grid +Z (grid Y).")]
        [SerializeField, Min(0)] private int extendPositiveZ;
        [Tooltip("Portal cells extending from the centre towards local grid -Z (grid Y).")]
        [SerializeField, Min(0)] private int extendNegativeZ;

        [FormerlySerializedAs("footprintSize")]
        [SerializeField, HideInInspector] private Vector2Int legacyFootprintSize;

        [Header("Connection")]
        [SerializeField] private PortalContentAsset content;
        [Tooltip("When enabled, preserve the player's relative position inside this portal footprint at the destination.")]
        [SerializeField] private bool preserveEntryOffset = true;

        [Header("Editor Preview")]
        [SerializeField] private Color gizmoColour = new Color(0.65f, 0.15f, 0.95f, 0.55f);
        [SerializeField, Range(0.05f, 1f)] private float gizmoFillOpacity = 0.22f;

        public GridWorld World => world;
        public Vector2Int CentreCell => world == null ? Vector2Int.zero : world.WorldToCell(transform.position);
        public Vector2Int AnchorCell => CentreCell;
        public Vector2Int FirstCell => CentreCell - new Vector2Int(extendNegativeX, extendNegativeZ);
        public Vector2Int Cell => CentreCell;
        public Vector2Int FootprintSize => new Vector2Int(
            extendNegativeX + 1 + extendPositiveX,
            extendNegativeZ + 1 + extendPositiveZ);
        public int ExtendPositiveX => extendPositiveX;
        public int ExtendNegativeX => extendNegativeX;
        public int ExtendPositiveZ => extendPositiveZ;
        public int ExtendNegativeZ => extendNegativeZ;
        public string PortalId => content == null ? string.Empty : content.id;
        public PortalContentAsset Content => content;

        public void Configure(PortalContentAsset portalContent, GridWorld targetWorld)
        {
            content = portalContent;
            world = targetWorld;
            if (isActiveAndEnabled && world != null) world.RegisterPortal(this, EnumeratePortalCells());
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

            world.RegisterPortal(this, EnumeratePortalCells());
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
            if (mover == null || mover.IsMoving || mover.IsMovementLocked || mover.World != world ||
                !ContainsCell(mover.CurrentCell))
            {
                return false;
            }

            if (content != null && content.disabled)
            {
                return false;
            }

            if (content == null || content.destination == null || DataDrivenWorldLoader.Instance == null)
            {
                Debug.LogError($"{name} needs a valid Portal Content asset with a destination.", this);
                return false;
            }

            Vector2Int entryOffset = preserveEntryOffset ? mover.CurrentCell - CentreCell : Vector2Int.zero;
            return DataDrivenWorldLoader.Instance.SwitchArea(content, entryOffset);
        }

        public bool ContainsCell(Vector2Int candidate)
        {
            Vector2Int first = FirstCell;
            Vector2Int size = FootprintSize;
            return candidate.x >= first.x && candidate.y >= first.y &&
                   candidate.x < first.x + size.x &&
                   candidate.y < first.y + size.y;
        }

        public IEnumerable<Vector2Int> EnumeratePortalCells()
        {
            for (int x = -extendNegativeX; x <= extendPositiveX; x++)
            {
                for (int z = -extendNegativeZ; z <= extendPositiveZ; z++)
                {
                    yield return CentreCell + new Vector2Int(x, z);
                }
            }
        }

        public Vector2Int GetArrivalCell(Vector2Int sourceEntryOffset)
        {
            Vector2Int mappedOffset = new Vector2Int(
                Mathf.Clamp(sourceEntryOffset.x, -extendNegativeX, extendPositiveX),
                Mathf.Clamp(sourceEntryOffset.y, -extendNegativeZ, extendPositiveZ));
            return CentreCell + mappedOffset;
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
                world.RegisterPortal(this, EnumeratePortalCells());
            }
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
            MigrateLegacyFootprint();
            extendPositiveX = Mathf.Max(0, extendPositiveX);
            extendNegativeX = Mathf.Max(0, extendNegativeX);
            extendPositiveZ = Mathf.Max(0, extendPositiveZ);
            extendNegativeZ = Mathf.Max(0, extendNegativeZ);

            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefreshFootprint();
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            MigrateLegacyFootprint();
        }

        private void MigrateLegacyFootprint()
        {
            if (legacyFootprintSize.x <= 0 && legacyFootprintSize.y <= 0)
            {
                return;
            }

            // The old footprint expanded from its first cell towards +X/+Z. Keeping that cell as
            // the new centre preserves all existing authored portal cells and offset mappings.
            extendPositiveX = legacyFootprintSize.x > 1 ? legacyFootprintSize.x - 1 : 0;
            extendPositiveZ = legacyFootprintSize.y > 1 ? legacyFootprintSize.y - 1 : 0;
            legacyFootprintSize = Vector2Int.zero;
        }

        private void OnDrawGizmos()
        {
            ResolveWorld();
            if (world == null)
            {
                return;
            }

            Vector2Int first = FirstCell;
            Vector2Int size = FootprintSize;
            Vector2Int last = first + size - Vector2Int.one;
            Vector3 firstWorld = world.CellToWorld(first, 0.06f);
            Vector3 lastWorld = world.CellToWorld(last, 0.06f);
            Vector3 centreWorld = world.CellToWorld(CentreCell, 0.06f);
            float cellSize = Mathf.Max(0.01f, world.CellSize);
            Vector3 footprintCentre = (firstWorld + lastWorld) * 0.5f;
            Vector3 dimensions = new Vector3(size.x * cellSize, 0.08f, size.y * cellSize);

            Color fill = gizmoColour;
            fill.a = Mathf.Clamp01(gizmoFillOpacity);
            Gizmos.color = fill;
            Gizmos.DrawCube(footprintCentre, dimensions);

            Color outline = gizmoColour;
            outline.a = Mathf.Max(outline.a, 0.9f);
            Gizmos.color = outline;
            Gizmos.DrawWireCube(footprintCentre, dimensions);

            // The purple sphere is the portal transmission centre and zero-offset arrival point.
            Gizmos.DrawSphere(centreWorld + Vector3.up * 0.12f, cellSize * 0.12f);

        }
    }
}
