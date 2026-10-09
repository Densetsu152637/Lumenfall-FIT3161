using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR18")]
    public sealed class Pr18CameraLifecycleAcceptanceTests
    {
        private GameObject cameraHost, actor, wall; private float previousTimeScale;
        [SetUp] public void SetUp() { previousTimeScale = Time.timeScale; actor = new GameObject("PR18 actor"); cameraHost = new GameObject("PR18 follower"); }
        [TearDown] public void TearDown() { Time.timeScale = previousTimeScale; Object.DestroyImmediate(cameraHost); Object.DestroyImmediate(actor); if (wall != null) Object.DestroyImmediate(wall); }
        [UnityTest] public IEnumerator ReducedMotionFollowsSmallMotionEvenWhileGameplayIsPaused()
        {
            var follower = cameraHost.AddComponent<CameraFollow2_5D>(); follower.SetTarget(actor.transform); follower.SetReducedMotion(true);
            Time.timeScale = 0;
            actor.transform.position = new Vector3(0.25f, 0, 0.25f);
            yield return null; yield return null;
            Assert.That(Vector3.Distance(cameraHost.transform.position, actor.transform.position + new Vector3(8, 10, -8)), Is.LessThan(0.001f));
            Assert.That(actor.transform.position, Is.EqualTo(new Vector3(0.25f, 0, 0.25f)));
        }
        [Test] public void ClearingTargetRestoresOcclusionImmediatelyAndKeepsCameraPose()
        {
            var follower = cameraHost.AddComponent<CameraFollow2_5D>(); follower.SetTarget(actor.transform);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = (cameraHost.transform.position + Vector3.up * 1.5f) * 0.5f;
            var renderer = wall.GetComponent<Renderer>(); wall.AddComponent<CameraOccluder>().Configure(new[] { renderer });
            follower.SetOcclusionEnabled(true); Physics.SyncTransforms(); follower.SendMessage("LateUpdate");
            Assert.That(renderer.forceRenderingOff, Is.True);
            Vector3 position = cameraHost.transform.position; Quaternion rotation = cameraHost.transform.rotation;
            follower.SetTarget(null);
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(follower.Target, Is.Null);
            Assert.That(cameraHost.transform.position, Is.EqualTo(position));
            Assert.That(cameraHost.transform.rotation, Is.EqualTo(rotation));
        }
    }
}
