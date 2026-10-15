using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class PlayerVisibilityTests
    {
        private readonly object menu = new object();
        private readonly object arrival = new object();
        private GameObject player;
        private MeshRenderer body;
        private MeshRenderer switchedOff;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("player");
            player.AddComponent<GridMover>();
            player.AddComponent<GridPlayerInput>();
            body = Child("body", true);
            switchedOff = Child("switched off", false);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerVisibility.Show(menu);
            PlayerVisibility.Show(arrival);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void PlayerStaysHiddenUntilEveryOwnerHasShownThem()
        {
            PlayerVisibility.Hide(menu);
            Assert.That(body.enabled, Is.False);
            Assert.That(PlayerVisibility.IsHidden, Is.True);

            // New Game: the arrival takes over before the menu lets go, so the player never flickers into view.
            PlayerVisibility.Hide(arrival);
            PlayerVisibility.Show(menu);
            Assert.That(body.enabled, Is.False);

            PlayerVisibility.Show(arrival);
            Assert.That(body.enabled, Is.True);
            Assert.That(PlayerVisibility.IsHidden, Is.False);
        }

        [Test]
        public void ShowingLeavesPartsTheGameHadSwitchedOffAlone()
        {
            PlayerVisibility.Hide(menu);
            PlayerVisibility.Show(menu);

            Assert.That(body.enabled, Is.True);
            Assert.That(switchedOff.enabled, Is.False);
        }

        [Test]
        public void RefreshHidesPartsSwitchedOnWhileHidden()
        {
            PlayerVisibility.Hide(menu);
            switchedOff.enabled = true;
            PlayerVisibility.Refresh();
            Assert.That(switchedOff.enabled, Is.False);

            PlayerVisibility.Show(menu);
            Assert.That(switchedOff.enabled, Is.True, "A part switched on during the hide is shown with the rest.");
        }

        [Test]
        public void RenderVisibleShowsThePlayerOnlyForTheRender()
        {
            PlayerVisibility.Hide(menu);
            bool visibleDuringRender = false;
            bool reportedHidden = true;

            PlayerVisibility.RenderVisible(() =>
            {
                visibleDuringRender = body.enabled;
                reportedHidden = PlayerVisibility.IsHidden;
            });

            Assert.That(visibleDuringRender, Is.True);
            Assert.That(reportedHidden, Is.False);
            Assert.That(body.enabled, Is.False);
            Assert.That(PlayerVisibility.IsHidden, Is.True);
        }

        private MeshRenderer Child(string name, bool enabled)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(player.transform, false);
            MeshRenderer part = child.AddComponent<MeshRenderer>();
            part.enabled = enabled;
            return part;
        }
    }
}
