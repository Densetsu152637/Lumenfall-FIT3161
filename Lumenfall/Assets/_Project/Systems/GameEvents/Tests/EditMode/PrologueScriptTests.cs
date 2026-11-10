using System.Linq;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class PrologueScriptTests
    {
        [Test]
        public void TheCarDrivesWithoutJumpsAndStopsWhereTheSetPutsTheStudent()
        {
            const float step = 1f / 120f;
            Vector3 previous = PrologueScript.CarPosition(PrologueScript.CarDistance(0f), out _);
            for (float t = step; t <= PrologueScript.Duration; t += step)
            {
                Vector3 position = PrologueScript.CarPosition(PrologueScript.CarDistance(t), out Vector3 forward);
                Assert.That(Vector3.Distance(previous, position), Is.LessThanOrEqualTo(PrologueScript.CruiseSpeed * step + 0.001f), $"The car jumps at {t:0.00}s.");
                Assert.That(forward.magnitude, Is.EqualTo(1f).Within(0.001f));
                previous = position;
            }

            Assert.That(PrologueScript.CarSpeed(PrologueScript.StopAt), Is.EqualTo(0f).Within(0.001f));
            Assert.That(PrologueScript.CarDistance(PrologueScript.Duration), Is.EqualTo(PrologueScript.DriveLength).Within(0.001f));
            Vector3 rest = PrologueSet.RestPosition(out Vector3 heading, out Vector3 right);
            Assert.That(Vector3.Distance(rest, PrologueScript.CarPosition(PrologueScript.CarDistance(PrologueScript.DoorOpensAt), out _)), Is.LessThan(0.001f),
                "The car is still moving when its door opens.");
            // West along the dirt road, with the driver's door (on the right) facing the field to the north.
            Assert.That(Vector3.Distance(heading, Vector3.left), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(right, Vector3.forward), Is.LessThan(0.001f));
        }

        [Test]
        public void TheStoryBeatsComeInOrderBeforeTheEnd()
        {
            float[] beats =
            {
                PrologueScript.SwerveAt, PrologueScript.StopAt, PrologueScript.DoorOpensAt, PrologueScript.StepOutAt, PrologueScript.HopAt, PrologueScript.WalkOutAt,
                PrologueScript.ArriveOutAt, PrologueScript.ThrowAt, PrologueScript.YellAt, PrologueScript.SlumpAt, PrologueScript.WalkBackAt,
                PrologueScript.ArriveBackAt, PrologueScript.TwinkleAt, PrologueScript.DiveAt, PrologueScript.ImpactAt, PrologueScript.InkFrom,
                PrologueScript.InkTo, PrologueScript.BlackTo, PrologueScript.Duration
            };
            for (int i = 1; i < beats.Length; i++) Assert.That(beats[i], Is.GreaterThan(beats[i - 1]), $"Beat {i} comes before beat {i - 1}.");

            Assert.That(PrologueScript.Shots[0].From, Is.EqualTo(0f));
            for (int i = 1; i < PrologueScript.Shots.Length; i++)
                Assert.That(PrologueScript.Shots[i].From, Is.GreaterThan(PrologueScript.Shots[i - 1].From));
            Assert.That(PrologueScript.ShotAt(PrologueScript.ImpactAt - 0.1f, out _, out _).Shot, Is.EqualTo(PrologueScript.Shot.CloseUp));
            Assert.That(PrologueScript.ShotAt(PrologueScript.Duration + 5f, out _, out _).Shot, Is.EqualTo(PrologueScript.Shot.WhiteOut));
        }

        [Test]
        public void NotificationsStopAtTheThrowAndNoTwoLinesShareTheScreen()
        {
            float[] arrivals = PrologueScript.Toasts.Select(toast => toast.At).ToArray();
            Assert.That(arrivals, Is.Ordered);
            Assert.That(arrivals.Last(), Is.LessThan(PrologueScript.ThrowAt), "A notification arrives after the phone is thrown.");
            Assert.That(PrologueScript.Toasts.All(toast => !string.IsNullOrEmpty(toast.Sender) && !string.IsNullOrEmpty(toast.Text)), Is.True);

            PrologueScript.Line[] lines = PrologueScript.Lines.Where(line => line.Style != PrologueScript.LineStyle.Clock).ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                Assert.That(lines[i].To, Is.GreaterThan(lines[i].From), lines[i].Text);
                Assert.That(lines[i].To, Is.LessThanOrEqualTo(PrologueScript.ImpactAt), lines[i].Text);
                if (i > 0) Assert.That(lines[i].From, Is.GreaterThanOrEqualTo(lines[i - 1].To), $"\"{lines[i].Text}\" overlaps the line before it.");
                float middle = (lines[i].From + lines[i].To) * 0.5f;
                Assert.That(PrologueScript.LineAlpha(lines[i], middle), Is.EqualTo(1f), lines[i].Text);
                Assert.That(PrologueScript.LineAlpha(lines[i], lines[i].To + 0.01f), Is.EqualTo(0f), lines[i].Text);
            }
        }
    }
}
