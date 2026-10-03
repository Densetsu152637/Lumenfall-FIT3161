using System;
using UnityEngine;

/// <summary>Explicitly opts a collider into hiding owned by the single gameplay camera. Never put this on important puzzle props.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class CameraOccluder : MonoBehaviour
{
    [Tooltip("Only these renderers may be hidden; collider, gameplay state and shared materials remain unchanged.")]
    [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
    public Renderer[] Renderers => renderers;

    /// <summary>Allows code-authored areas to opt in without a scene search.</summary>
    public void Configure(Renderer[] value) => renderers = value ?? Array.Empty<Renderer>();
}
