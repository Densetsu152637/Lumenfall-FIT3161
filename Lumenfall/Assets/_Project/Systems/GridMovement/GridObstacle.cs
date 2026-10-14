using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Marks a rectangular tile footprint as impassable. A 1x1 footprint is suitable for an item;
    /// larger footprints are suitable for buildings and furniture.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GridObstacle : MonoBehaviour
    {
        [SerializeField] private GridWorld world;
        [SerializeField] private bool useTransformAsAnchor = true;
        [SerializeField] private Vector2Int anchorCell;
        [SerializeField] private Vector2Int footprintOffset;
        [SerializeField] private Vector2Int footprintSize = Vector2Int.one;
        [SerializeField] private Color gizmoColour = new Color(1f, 0.25f, 0.2f, 0.45f);

        public GridWorld World => world;
        public Vector2Int AnchorCell => useTransformAsAnchor && world != null
            ? world.WorldToCell(transform.position)
            : anchorCell;
        public Vector2Int FootprintOffset => footprintOffset;
        public Vector2Int FootprintSize => footprintSize;
        public void Configure(GridWorld targetWorld, Vector2Int size)
        { world = targetWorld; footprintSize = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)); useTransformAsAnchor = true; RefreshFootprint(); }

        private void Reset()
        {
            world = GetComponentInParent<GridWorld>();
        }

        private void OnEnable()
        {
            ResolveWorld();
            RefreshFootprint();
        }

        private void OnDisable()
        {
            if (world != null)
            {
                world.UnregisterObstacle(this);
            }
        }

        /// <summary>Re-registers the footprint after moving or resizing this obstacle at runtime.</summary>
        public void RefreshFootprint()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResolveWorld();
            if (world == null)
            {
                // Prefab assets may carry an obstacle before they are placed beneath a live GridWorld.
                if (Application.isPlaying) Debug.LogError($"{name} needs a GridWorld to block cells.", this);
                return;
            }

            world.RegisterObstacle(this, EnumerateOccupiedCells());
        }

        public IEnumerable<Vector2Int> EnumerateOccupiedCells()
        {
            Vector2Int firstCell = AnchorCell + footprintOffset;
            for (int x = 0; x < footprintSize.x; x++)
            {
                for (int y = 0; y < footprintSize.y; y++)
                {
                    yield return firstCell + new Vector2Int(x, y);
                }
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
            footprintSize.x = Mathf.Max(1, footprintSize.x);
            footprintSize.y = Mathf.Max(1, footprintSize.y);

            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefreshFootprint();
            }
        }

        // Keep occupied cells visible while authoring so blocked areas can be laid out without
        // selecting every obstacle individually in the Scene view.
        private void OnDrawGizmos()
        {
            ResolveWorld();
            if (world == null)
            {
                return;
            }

            Gizmos.color = gizmoColour;
            foreach (Vector2Int cell in EnumerateOccupiedCells())
            {
                Vector3 centre = world.CellToWorld(cell, 0.03f);
                Vector3 size = world.transform.TransformVector(
                    new Vector3(world.CellSize * 0.9f, 0.05f, world.CellSize * 0.9f));
                Gizmos.DrawCube(centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
            }
        }
    }
}
