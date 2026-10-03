using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.Camera
{
    public sealed class CameraFollowPlayModeTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject Create(string name)
        {
            var item = new GameObject(name);
            objects.Add(item);
            return item;
        }
        private CameraFollow2_5D CreateFollower(Transform target)
        {
            var follower = Create("camera").AddComponent<CameraFollow2_5D>();
            follower.SetTarget(target);
            return follower;
        }
        private static void Configure(CameraFollow2_5D follower, string field, object value)
        {
            typeof(CameraFollow2_5D).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(follower, value);
        }
        private static void AssertPosition(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.001f));
        }
        [TearDown]
        public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void ReplacementTargetAndReducedMotionReseedImmediately()
        {
            Transform first = Create("first").transform;
            var follower = CreateFollower(first);
            first.position = new Vector3(1f, 0f, 0f);
            follower.SetReducedMotion(true);
            AssertPosition(follower.transform.position, first.position + new Vector3(8f, 10f, -8f));
            Quaternion rotation = follower.transform.rotation;
            Transform replacement = Create("replacement").transform;
            replacement.position = new Vector3(100f, 0f, 100f);
            follower.SetTarget(replacement);
            AssertPosition(follower.transform.position, replacement.position + new Vector3(8f, 10f, -8f));
            Assert.That(Quaternion.Angle(rotation, follower.transform.rotation), Is.LessThan(0.001f));
            follower.SetReducedMotion(false);
            Assert.That(follower.ReducedMotion, Is.False);
        }

        [Test]
        public void GridBoundsHonorTranslationRotationAndScale()
        {
            GridWorld grid = Create("grid").AddComponent<GridWorld>();
            grid.ConfigureBounds(10, 8);
            grid.transform.SetPositionAndRotation(new Vector3(20f, 0f, 30f), Quaternion.Euler(0f, 90f, 0f));
            grid.transform.localScale = new Vector3(2f, 1f, 3f);
            Transform actor = Create("actor").transform;
            actor.SetParent(grid.transform, false);
            actor.gameObject.AddComponent<GridMover>();
            var follower = CreateFollower(actor);
            Configure(follower, "clampUnprofiledMaps", true);
            actor.position = grid.transform.TransformPoint(new Vector3(-5f, 0f, 20f));
            follower.SnapToTarget();
            AssertPosition(follower.transform.position,
                grid.transform.TransformPoint(new Vector3(0f, 0f, 7f)) + new Vector3(8f, 10f, -8f));
        }

        [Test]
        public void SmallGridTeleportSnapsEvenBelowDistanceThresholdAndAfterReenable()
        {
            GridWorld grid = Create("grid").AddComponent<GridWorld>();
            grid.ConfigureBounds(10, 10);
            Transform actor = Create("actor").transform;
            actor.SetParent(grid.transform, false);
            GridMover mover = actor.gameObject.AddComponent<GridMover>();
            var follower = CreateFollower(actor);
            Configure(follower, "teleportDistance", 100f);
            Assert.That(mover.TryTeleportTo(grid, new Vector2Int(1, 0)), Is.True);
            AssertPosition(follower.transform.position, actor.position + new Vector3(8f, 10f, -8f));
            follower.enabled = false;
            Transform other = Create("other").transform;
            other.SetParent(grid.transform, false);
            other.localPosition = new Vector3(3f, 0f, 0f);
            GridMover otherMover = other.gameObject.AddComponent<GridMover>();
            follower.SetTarget(other);
            follower.enabled = true;
            Assert.That(otherMover.TryTeleportTo(grid, new Vector2Int(4, 0)), Is.True);
            AssertPosition(follower.transform.position, other.position + new Vector3(8f, 10f, -8f));
        }

        [UnityTest]
        public IEnumerator TransformDiscontinuitySnapsAndOrdinaryFollowKeepsFixedAngle()
        {
            Transform actor = Create("actor").transform;
            var follower = CreateFollower(actor);
            Quaternion rotation = follower.transform.rotation;
            actor.position = new Vector3(0.5f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(rotation, follower.transform.rotation), Is.LessThan(0.001f));
            actor.position = new Vector3(80f, 0f, 80f);
            yield return null;
            yield return null;
            AssertPosition(follower.transform.position, actor.position + new Vector3(8f, 10f, -8f));
        }

        [Test]
        public void AuthoredMapProfileAppliesThenFallsBackAndRestoresOnTransition()
        {
            GridWorld grid = Create("grid").AddComponent<GridWorld>();
            Transform actor = Create("actor").transform;
            actor.SetParent(grid.transform, false);
            actor.gameObject.AddComponent<GridMover>();
            actor.position = new Vector3(4f, 0f, 4f);
            var follower = CreateFollower(actor);
            var loader = Create("loader").AddComponent<DataDrivenWorldLoader>();
            loader.enabled = false;
            typeof(DataDrivenWorldLoader).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, grid);
            Configure(follower, "worldLoader", loader);
            Configure(follower, "mapProfiles", new[]
            {
                new CameraFollow2_5D.MapFramingProfile { mapId = "test.interior", distanceScale = 0.5f, insetCells = 2f }
            });
            var interior = ScriptableObject.CreateInstance<WorldAreaAsset>();
            var outdoor = ScriptableObject.CreateInstance<WorldAreaAsset>();
            try
            {
                GameObject template = Create("authored area template");
                interior.areaPrefab = template;
                interior.definition = new MapDefinition { id = "test.interior", width = 10, height = 10 };
                outdoor.areaPrefab = template;
                outdoor.definition = new MapDefinition { id = "test.outdoor", width = 20, height = 20 };
                loader.Load(interior, null);
                follower.SnapToTarget();
                AssertPosition(follower.transform.position, new Vector3(8f, 5.75f, 0f));
                Quaternion angle = follower.transform.rotation;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                objects.Add(wall);
                wall.transform.position = (follower.transform.position + actor.position + new Vector3(0f, 1.5f, 0f)) * 0.5f;
                Renderer renderer = wall.GetComponent<Renderer>();
                wall.AddComponent<CameraOccluder>().Configure(new[] { renderer });
                follower.SetOcclusionEnabled(true);
                Physics.SyncTransforms();
                follower.SendMessage("LateUpdate");
                Assert.That(renderer.forceRenderingOff, Is.True);
                loader.Load(outdoor, null);
                follower.SnapToTarget();
                Assert.That(renderer.forceRenderingOff, Is.False);
                AssertPosition(follower.transform.position, actor.position + new Vector3(8f, 10f, -8f));
                Assert.That(Quaternion.Angle(angle, follower.transform.rotation), Is.LessThan(0.001f));
            }
            finally
            {
                loader.Clear();
                Object.DestroyImmediate(interior);
                Object.DestroyImmediate(outdoor);
            }
        }
        [Test]
        public void OcclusionRestoresOnExitReplacementToggleAndDisableWithoutMaterialMutation()
        {
            Transform actor = Create("actor").transform;
            var follower = CreateFollower(actor);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(wall);
            wall.transform.position = (follower.transform.position + new Vector3(0f, 1.5f, 0f)) * 0.5f;
            Renderer renderer = wall.GetComponent<Renderer>();
            Material originalMaterial = renderer.sharedMaterial;
            wall.AddComponent<CameraOccluder>().Configure(new[] { renderer });
            follower.SetOcclusionEnabled(true);
            Physics.SyncTransforms();
            follower.SendMessage("LateUpdate");
            Assert.That(renderer.forceRenderingOff, Is.True);
            wall.transform.position += Vector3.right * 50f;
            Physics.SyncTransforms();
            follower.SendMessage("LateUpdate");
            Assert.That(renderer.forceRenderingOff, Is.False);
            wall.transform.position -= Vector3.right * 50f;
            Physics.SyncTransforms();
            follower.SendMessage("LateUpdate");
            follower.SetTarget(Create("replacement").transform);
            Assert.That(renderer.forceRenderingOff, Is.False);
            follower.SendMessage("LateUpdate");
            follower.SetOcclusionEnabled(false);
            Assert.That(renderer.forceRenderingOff, Is.False);
            follower.SetOcclusionEnabled(true);
            follower.SendMessage("LateUpdate");
            follower.enabled = false;
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
        }

        [Test]
        public void ProtectedRenderersRemainVisibleAndAlreadyHiddenStateIsRestored()
        {
            Transform actor = Create("actor").transform;
            var follower = CreateFollower(actor);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(wall);
            wall.transform.position = (follower.transform.position + new Vector3(0f, 1.5f, 0f)) * 0.5f;
            Renderer renderer = wall.GetComponent<Renderer>();
            wall.AddComponent<CameraOccluder>().Configure(new[] { renderer });
            Configure(follower, "protectedRenderers", new[] { renderer });
            follower.SetOcclusionEnabled(true);
            Physics.SyncTransforms();
            follower.SendMessage("LateUpdate");
            Assert.That(renderer.forceRenderingOff, Is.False);
            Configure(follower, "protectedRenderers", new Renderer[0]);
            renderer.forceRenderingOff = true;
            follower.SendMessage("LateUpdate");
            follower.enabled = false;
            Assert.That(renderer.forceRenderingOff, Is.True);
        }
    }
}
