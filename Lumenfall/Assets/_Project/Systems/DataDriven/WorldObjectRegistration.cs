using System;
using UnityEngine;
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
        public string ObjectId => objectId;
        public Vector2Int Cell => cell;
        public Vector2Int Size => size;
        public void Configure(string id, Vector2Int at, Vector2Int footprint, bool compatible, string[] objectTags)
        { objectId = id; cell = at; size = footprint; spellCompatible = compatible; tags = objectTags ?? Array.Empty<string>(); }
        private void OnEnable()
        { if (WorldStateService.Instance != null) WorldStateService.Instance.RegisterObject(objectId, gameObject, cell, size, spellCompatible, tags); }
    }
}
