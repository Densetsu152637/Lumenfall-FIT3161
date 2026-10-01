using System;
using UnityEngine;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.DataDriven
{
    /// <summary>Stores puzzle-object identity and authored cell metadata on baked area objects.</summary>
    public sealed class WorldObjectRegistration : MonoBehaviour
    {
        [SerializeField] private string objectId;
        [SerializeField] private Vector2Int cell;
        [SerializeField] private Vector2Int size = Vector2Int.one;
        [SerializeField] private bool spellCompatible = true;
        [SerializeField] private string[] tags = Array.Empty<string>();
        private Vector3 authoredPosition;
        private Quaternion authoredRotation;
        public string ObjectId => objectId;
        public Vector2Int Cell => cell;
        public Vector2Int Size => size;
        public string[] Tags => tags;
        public void Configure(string id, Vector2Int at, Vector2Int footprint, bool compatible, string[] objectTags)
        { objectId = id; cell = at; size = footprint; spellCompatible = compatible; tags = objectTags ?? Array.Empty<string>(); }
        private void Awake()
        { authoredPosition = transform.localPosition; authoredRotation = transform.localRotation; }
        private void OnEnable()
        {
            // Area instances are reused, so spells may have moved or turned this object last session. Register from the
            // authored pose; WorldStateService re-applies any saved or archived state when the rebuild ends.
            if (transform.localPosition != authoredPosition || transform.localRotation != authoredRotation)
            {
                transform.localPosition = authoredPosition; transform.localRotation = authoredRotation;
                if (TryGetComponent(out GridObstacle obstacle)) obstacle.RefreshFootprint();
            }
            if (WorldStateService.Instance != null) WorldStateService.Instance.RegisterObject(objectId, gameObject, cell, size, spellCompatible, tags);
        }
    }
}
