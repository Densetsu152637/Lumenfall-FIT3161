using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// The follow camera: it sits due south of the player so the grid lines up with the screen (W moves straight up),
    /// keeps up with a walking step, and lets the player peek past walls by swinging the view a little around them.
    /// </summary>
    public sealed class CameraFollowTests
    {
        private GameObject host, player;
        private CameraFollow2_5D follow;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("camera-test");
            host.SetActive(false);
            follow = host.AddComponent<CameraFollow2_5D>();
            player = new GameObject("camera-test-player");
            Set("target", player.transform);
            CameraFollow2_5D.ClearConversationFrame();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void TheRestingViewSitsDueSouthSoTheGridLinesUpWithTheScreen()
        {
            // Playtesters found W moved up and to the right when the camera sat diagonally south-east of the player.
            Vector3 offset = Get<Vector3>("offset");
            Assert.That(offset.x, Is.EqualTo(0f), "No sideways offset: the camera is due south.");
            Assert.That(offset.z, Is.LessThan(0f), "South of the player, looking north.");
            Assert.That(offset.y, Is.GreaterThan(0f), "Above the player, looking down.");
        }

        [Test]
        public void TheCameraKeepsUpWithAWalkingStep()
        {
            // A lag as long as a step (0.15 s) made the view trail the player and then swing after them.
            Assert.That(Get<float>("smoothTime"), Is.LessThan(0.1f));
        }

        [Test]
        public void PeekingSwingsTheViewAroundThePlayerButNeverFarEnoughToRemapMovement()
        {
            float yawLimit = Get<float>("peekYawLimit");
            Vector2 pitch = Get<Vector2>("peekPitchRange");
            Assert.That(yawLimit, Is.GreaterThan(0f).And.LessThan(45f), "Movement is never remapped, so the swing stays under 45 degrees.");
            Assert.That(pitch.x, Is.LessThan(0f));
            Assert.That(pitch.y, Is.GreaterThan(0f).And.LessThan(45f));

            Vector3 offset = Get<Vector3>("offset");
            Set("peek", Vector2.zero);
            Assert.That(PeekRotation(offset) * offset, Is.EqualTo(offset), "At rest there is no swing.");

            Set("peek", new Vector2(yawLimit, 0f));
            Vector3 swung = PeekRotation(offset) * offset;
            Assert.That(swung.magnitude, Is.EqualTo(offset.magnitude).Within(1e-3f), "The view swings around the player at the same distance.");
            float yaw = Vector3.SignedAngle(new Vector3(offset.x, 0f, offset.z), new Vector3(swung.x, 0f, swung.z), Vector3.up);
            Assert.That(yaw, Is.EqualTo(yawLimit).Within(0.01f));

            Set("peek", new Vector2(0f, pitch.y));
            Vector3 raised = PeekRotation(offset) * offset;
            Assert.That(raised.y, Is.GreaterThan(offset.y), "Peeking up raises the camera to see over walls.");
        }

        [Test]
        public void LettingGoReturnsTheViewAndReducedMotionSnapsItBack()
        {
            follow.SetReducedMotion(true);
            Set("peek", new Vector2(20f, 10f));
            typeof(CameraFollow2_5D).GetMethod("UpdatePeek", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(follow, null);
            Assert.That(Get<Vector2>("peek"), Is.EqualTo(Vector2.zero), "With nothing held, Reduced Motion snaps straight back instead of easing.");
        }

        private Quaternion PeekRotation(Vector3 offset) => (Quaternion)typeof(CameraFollow2_5D)
            .GetMethod("PeekRotation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(follow, new object[] { offset });

        private T Get<T>(string field) => (T)typeof(CameraFollow2_5D).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(follow);

        private void Set(string field, object value) =>
            typeof(CameraFollow2_5D).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(follow, value);
    }
}
