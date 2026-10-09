using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.CameraEffects
{
    /// <summary>Camera-scoped circular reveal for the patched Polytope vegetation shaders.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerOcclusionReveal : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);
        [Tooltip("Additional camera-right / camera-up offset in world units (one unit is one cell in SampleScene).")]
        [SerializeField] private Vector2 viewOffset;
        [Tooltip("Outer radius as a fraction of camera pixel height.")]
        [SerializeField, Range(0.01f, 0.5f)] private float radius = 0.16f;
        [SerializeField, Range(0.001f, 0.5f)] private float feather = 0.07f;
        [SerializeField, Range(0f, 1f)] private float centerOpacity = 0f;
        [Tooltip("Only vegetation this far in front of the focus plane is revealed.")]
        [SerializeField, Min(0f)] private float depthPadding = 0.15f;
        private Camera view;
        private static readonly int Enabled = Shader.PropertyToID("_LFRevealEnabled");
        private static readonly int Matrix = Shader.PropertyToID("_LFRevealVP");
        private static readonly int Focus = Shader.PropertyToID("_LFRevealFocus");
        private static readonly int Forward = Shader.PropertyToID("_LFRevealForward");
        private static readonly int Settings = Shader.PropertyToID("_LFRevealSettings");
        private static readonly int Pixels = Shader.PropertyToID("_LFRevealPixels");

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view) return;
            Shader.SetGlobalFloat(Enabled, 0f);
            if (target == null) return;
            Vector3 focus = target.position + targetOffset
                + view.transform.right * viewOffset.x + view.transform.up * viewOffset.y;
            if (view.WorldToViewportPoint(focus).z <= view.nearClipPlane) return;
            Shader.SetGlobalMatrix(Matrix, view.projectionMatrix * view.worldToCameraMatrix);
            Shader.SetGlobalVector(Focus, new Vector4(focus.x, focus.y, focus.z, 1f));
            Vector3 forward = view.transform.forward;
            Shader.SetGlobalVector(Forward, new Vector4(forward.x, forward.y, forward.z, depthPadding));
            Shader.SetGlobalVector(Settings, new Vector4(radius, Mathf.Min(feather, radius), centerOpacity, view.aspect));
            Shader.SetGlobalVector(Pixels, new Vector4(view.pixelWidth, view.pixelHeight, 0f, 0f));
            Shader.SetGlobalFloat(Enabled, 1f);
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view) Shader.SetGlobalFloat(Enabled, 0f);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Shader.SetGlobalFloat(Enabled, 0f);
        }
    }
}
