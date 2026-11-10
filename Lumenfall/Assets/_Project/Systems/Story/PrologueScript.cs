using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The prologue's timeline as plain data: when each shot starts, the phone notifications, the inner-voice lines, the
    /// car's drive along the highway and onto the dirt road, and the beats the scene keys its effects to. Times are
    /// seconds from the start of the prologue, and positions are metres in the set's own space (see
    /// <see cref="PrologueSet"/>): the highway runs north along +z, the car keeps to the left lane, and the dirt road
    /// leaves to the west (-x) at z 0. <see cref="PrologueScene"/> plays it; nothing here touches the scene.
    /// </summary>
    internal static class PrologueScript
    {
        public enum Shot { Highway, Inside, Swerve, Throw, Yell, Lean, LookUp, Dive, CloseUp, WhiteOut }

        public readonly struct ShotSpan
        {
            public readonly Shot Shot;
            public readonly float From;
            public ShotSpan(Shot shot, float from) { Shot = shot; From = from; }
        }

        public readonly struct Toast
        {
            public readonly float At;
            public readonly string Sender;
            public readonly string Text;
            public Toast(float at, string sender, string text) { At = at; Sender = sender; Text = text; }
        }

        public enum LineStyle { Thought, Shout, Clock }

        public readonly struct Line
        {
            public readonly float From, To;
            public readonly string Text;
            public readonly LineStyle Style;
            public Line(float from, float to, string text, LineStyle style = LineStyle.Thought) { From = from; To = to; Text = text; Style = style; }
        }

        // ---- Beats ----

        public const float FadeInSeconds = 1.6f;
        /// <summary>The car leaves the highway here, at full speed.</summary>
        public const float SwerveAt = 14f;
        /// <summary>The car has stopped on the dirt road and the engine cuts out.</summary>
        public const float StopAt = SwerveAt + BrakeSeconds;
        public const float DoorOpensAt = 19.3f;
        /// <summary>The student lets go of the wheel and turns in their seat to face the open door.</summary>
        public const float StepOutAt = 19.9f;
        /// <summary>Turned to the door, they hop down out of the car.</summary>
        public const float HopAt = 20.35f;
        public const float WalkOutAt = 20.9f;
        public const float ArriveOutAt = 21.75f;
        /// <summary>The phone leaves the student's hand, and every notification on screen breaks apart.</summary>
        public const float ThrowAt = 22.2f;
        public const float ThrowSeconds = 1.1f;
        public const float YellAt = 23f;
        public const float SlumpAt = 25.6f;
        public const float WalkBackAt = 27f;
        public const float ArriveBackAt = 28.5f;
        public const float LookUpFrom = 29.2f, LookUpTo = 31.8f;
        /// <summary>One star brightens and twinkles, then dives at the student.</summary>
        public const float TwinkleAt = 36f;
        public const float DiveAt = 39f;
        public const float ImpactAt = 43.5f;
        /// <summary>The white of the impact holds, darkens to ink, then to black, where the forest takes over.</summary>
        public const float InkFrom = 45f, InkTo = 46.6f;
        public const float BlackFrom = 46.6f, BlackTo = 47.6f;
        public const float Duration = 48f;

        // ---- The drive ----

        public const float CruiseSpeed = 14f;
        public const float BrakeSeconds = 4.4f;
        public const float LaneX = -1.75f;
        public const float TurnRadius = 9f;
        /// <summary>How far the car has driven by the swerve: it starts this far south of the junction.</summary>
        public const float HighwayLength = CruiseSpeed * SwerveAt;
        public static float TurnLength => TurnRadius * Mathf.PI * 0.5f;
        /// <summary>The whole drive: the highway, then braking hard through the turn and along the dirt road.</summary>
        public static float DriveLength => HighwayLength + CruiseSpeed * BrakeSeconds * 0.5f;

        public static readonly ShotSpan[] Shots =
        {
            new ShotSpan(Shot.Highway, 0f),
            new ShotSpan(Shot.Inside, 7f),
            // Cut outside just before the turn, so the car is seen tearing up to it.
            new ShotSpan(Shot.Swerve, SwerveAt - 0.8f),
            new ShotSpan(Shot.Throw, 19f),
            new ShotSpan(Shot.Yell, YellAt),
            new ShotSpan(Shot.Lean, WalkBackAt),
            new ShotSpan(Shot.LookUp, 31.5f),
            new ShotSpan(Shot.Dive, DiveAt),
            new ShotSpan(Shot.CloseUp, 42.3f),
            new ShotSpan(Shot.WhiteOut, InkFrom),
        };

        // Uni, work, the group chat and home, all at once, faster and faster. Nothing is a real product or service.
        public static readonly Toast[] Toasts =
        {
            new Toast(1.6f, "Uni Portal", "PHYS2041 Assignment 3 is due in 18 minutes"),
            new Toast(3.0f, "Shift Manager", "can u cover 6am tmrw?? pls"),
            new Toast(4.1f, "Group Project (12)", "who's doing the slides"),
            new Toast(5.0f, "Mum", "Missed call"),
            new Toast(5.8f, "Uni Portal", "Quiz 4 closes at 11:59 PM"),
            new Toast(7.6f, "Group Project (27)", "hello??"),
            new Toast(8.4f, "Shift Manager", "need an answer by tonight"),
            new Toast(9.0f, "Bank", "Your balance is low"),
            new Toast(9.5f, "Uni Portal", "Late penalty: 10% per day"),
            new Toast(10.0f, "Group Project (41)", "@you"),
            new Toast(10.4f, "Shift Manager", "??"),
            new Toast(10.7f, "Group Project (58)", "guys"),
            new Toast(11.0f, "Mum", "Missed call (2)"),
            new Toast(11.25f, "Group Project (73)", "GUYS"),
            new Toast(11.5f, "Shift Manager", "I'll take that as a yes"),
            new Toast(12.3f, "Phone", "Storage almost full"),
            new Toast(13.2f, "Uni Portal", "Reminder: Assignment 3 is due in 2 minutes"),
        };

        // The student's inner voice, and the clock card at the start. The wake-up monologue in the forest picks up from the
        // last line: they studied physics, and a star just did something stars do not do.
        public static readonly Line[] Lines =
        {
            new Line(0.8f, 4.2f, "11:42 PM", LineStyle.Clock),
            new Line(8.2f, 10.6f, "Just get home. Just get home."),
            new Line(11.2f, 13.4f, "Why is everything due on the same day?"),
            new Line(14.3f, 16.6f, "Nope. Not tonight."),
            new Line(YellAt, SlumpAt, "AAAAAAAAAAAAAAAAAAAAH!", LineStyle.Shout),
            new Line(26f, 27f, "..."),
            new Line(29.3f, 32f, "When did I last actually look up?"),
            new Line(32.6f, 35.4f, "Huh. They're kind of pretty out here."),
            new Line(39.3f, 41.6f, "...Is that one getting bigger?"),
            new Line(41.9f, ImpactAt, "Wait. Stars don't do th-"),
        };

        /// <summary>The shot playing at <paramref name="t"/>; the last shot holds past the end.</summary>
        public static ShotSpan ShotAt(float t, out float shotTime, out float shotLength)
        {
            int index = 0;
            for (int i = 1; i < Shots.Length; i++)
                if (t >= Shots[i].From) index = i;
            float end = index + 1 < Shots.Length ? Shots[index + 1].From : Duration;
            shotTime = t - Shots[index].From;
            shotLength = Mathf.Max(0.01f, end - Shots[index].From);
            return Shots[index];
        }

        /// <summary>How far along its route the car has driven at <paramref name="t"/>: cruising, then braking to a stop.</summary>
        public static float CarDistance(float t)
        {
            if (t <= SwerveAt) return CruiseSpeed * Mathf.Max(0f, t);
            float braking = Mathf.Min(t - SwerveAt, BrakeSeconds);
            return HighwayLength + CruiseSpeed * braking - CruiseSpeed / (2f * BrakeSeconds) * braking * braking;
        }

        /// <summary>The car's speed at <paramref name="t"/>, in metres per second.</summary>
        public static float CarSpeed(float t)
        {
            if (t < 0f) return 0f;
            if (t <= SwerveAt) return CruiseSpeed;
            return CruiseSpeed * Mathf.Clamp01(1f - (t - SwerveAt) / BrakeSeconds);
        }

        /// <summary>
        /// Where the car is after driving <paramref name="distance"/>: north up the left lane to the junction, a hard left
        /// through a quarter circle, then west along the dirt road. <paramref name="forward"/> is its heading.
        /// </summary>
        public static Vector3 CarPosition(float distance, out Vector3 forward)
        {
            if (distance <= HighwayLength)
            {
                forward = Vector3.forward;
                return new Vector3(LaneX, 0f, distance - HighwayLength);
            }
            float along = distance - HighwayLength;
            Vector3 centre = new Vector3(LaneX - TurnRadius, 0f, 0f);
            if (along <= TurnLength)
            {
                float angle = along / TurnRadius;
                forward = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                return centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * TurnRadius;
            }
            forward = Vector3.left;
            return centre + new Vector3(0f, 0f, TurnRadius) + Vector3.left * (along - TurnLength);
        }

        /// <summary>How hard the car is turning at <paramref name="distance"/>: 1 through the bend, easing in and out of it.</summary>
        public static float TurnAmount(float distance)
        {
            float along = distance - HighwayLength;
            if (along <= -1.5f || along >= TurnLength + 1.5f) return 0f;
            if (along < 0f) return Mathf.SmoothStep(0f, 1f, (along + 1.5f) / 1.5f);
            if (along > TurnLength) return Mathf.SmoothStep(1f, 0f, (along - TurnLength) / 1.5f);
            return 1f;
        }

        /// <summary>0 to 1 over [<paramref name="from"/>, <paramref name="to"/>], eased.</summary>
        public static float Ease(float t, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

        /// <summary>A line's opacity at <paramref name="t"/>: fading in and out over 0.4 seconds, or a quarter of its time.</summary>
        public static float LineAlpha(Line line, float t)
        {
            if (t < line.From || t > line.To) return 0f;
            float fade = Mathf.Min(0.4f, (line.To - line.From) * 0.25f);
            return Mathf.Clamp01(Mathf.Min((t - line.From) / fade, (line.To - t) / fade));
        }
    }
}
