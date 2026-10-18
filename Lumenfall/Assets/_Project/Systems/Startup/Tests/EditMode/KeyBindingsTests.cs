using System;
using NUnit.Framework;
using Lumenfall.Controls;
using UnityEngine.InputSystem;

namespace Lumenfall.Startup.Tests
{
    public sealed class KeyBindingsTests
    {
        [TestCase(Key.A)]
        [TestCase(Key.F13)]
        public void IsBindableAcceptsSupportedKeys(Key key)
        {
            Assert.That(KeyBindings.IsBindable(key), Is.True);
        }

        [TestCase(Key.None)]
        [TestCase(Key.Escape)]
        [TestCase(Key.UpArrow)]
        [TestCase(Key.DownArrow)]
        [TestCase(Key.LeftArrow)]
        [TestCase(Key.RightArrow)]
        public void IsBindableRejectsReservedKeys(Key key)
        {
            Assert.That(KeyBindings.IsBindable(key), Is.False);
        }

        [Test]
        public void IsBindableRejectsImeSelectionDummyKey()
        {
            Key imeSelectionDummy = (Key)Enum.Parse(typeof(Key), "IMESelected");

            Assert.That(KeyBindings.IsBindable(imeSelectionDummy), Is.False);
        }

        [Test]
        public void IsBindableRejectsUndefinedNumericValues()
        {
            Assert.That(KeyBindings.IsBindable((Key)int.MaxValue), Is.False);
        }

        [TestCase(Key.None)]
        [TestCase(Key.Escape)]
        [TestCase(Key.UpArrow)]
        [TestCase(Key.DownArrow)]
        [TestCase(Key.LeftArrow)]
        [TestCase(Key.RightArrow)]
        public void SetKeyRejectsReservedKeysWithoutChangingBindings(Key key)
        {
            AssertRejectedSetLeavesBindingsUnchanged(key);
        }

        [Test]
        public void SetKeyRejectsImeSelectionDummyWithoutChangingBindings()
        {
            Key imeSelectionDummy = (Key)Enum.Parse(typeof(Key), "IMESelected");

            AssertRejectedSetLeavesBindingsUnchanged(imeSelectionDummy);
        }

        [Test]
        public void SetKeyRejectsUndefinedNumericValuesWithoutChangingBindings()
        {
            AssertRejectedSetLeavesBindingsUnchanged((Key)int.MaxValue);
        }

        private static void AssertRejectedSetLeavesBindingsUnchanged(Key key)
        {
            GameAction[] actions = KeyBindings.AllActions;
            var before = new Key[actions.Length];
            for (int i = 0; i < actions.Length; i++) before[i] = KeyBindings.GetKey(actions[i]);

            Assert.That(KeyBindings.SetKey(GameAction.Talk, key), Is.Null);

            for (int i = 0; i < actions.Length; i++)
                Assert.That(KeyBindings.GetKey(actions[i]), Is.EqualTo(before[i]), actions[i].ToString());
        }
    }
}
