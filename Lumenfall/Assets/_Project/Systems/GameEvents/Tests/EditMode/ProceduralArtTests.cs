using Lumenfall.DataDriven;
using Lumenfall.Spells;
using Lumenfall.Startup;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// The dialogue frames, spell icons and startup-menu buttons are drawn in code (ProceduralArt.cs) instead of the
    /// AI-generated images playtesters asked to have replaced. These check the shapes they are drawn from and that each
    /// piece of art fits where the game puts it.
    /// </summary>
    public sealed class ProceduralArtTests
    {
        [Test]
        public void ShapesAreNegativeInsideAndPositiveOutside()
        {
            Assert.That(Sdf.Circle(Vector2.zero, Vector2.zero, 2f), Is.EqualTo(-2f));
            Assert.That(Sdf.Circle(new Vector2(3f, 0f), Vector2.zero, 2f), Is.EqualTo(1f));
            Rect box = new Rect(0f, 0f, 10f, 4f);
            Assert.That(Sdf.RoundedRect(new Vector2(5f, 2f), box, 1f), Is.LessThan(0f));
            Assert.That(Sdf.RoundedRect(new Vector2(12f, 2f), box, 1f), Is.EqualTo(2f).Within(1e-4f));
            Vector2[] square = { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 4f), new Vector2(0f, 4f) };
            Assert.That(Sdf.Polygon(new Vector2(2f, 2f), square), Is.EqualTo(-2f).Within(1e-4f));
            Assert.That(Sdf.Polygon(new Vector2(6f, 2f), square), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(Sdf.Ring(new Vector2(5f, 0f), Vector2.zero, 5f, 1f), Is.EqualTo(-0.5f).Within(1e-4f));
            Assert.That(Sdf.Arc(new Vector2(5f, 0f), Vector2.zero, 5f, 1f, -45f, 90f), Is.LessThan(0f), "Inside the arc's sweep.");
            Assert.That(Sdf.Arc(new Vector2(-5f, 0f), Vector2.zero, 5f, 1f, -45f, 90f), Is.GreaterThan(0f), "Opposite the sweep.");
            Assert.That(Sdf.StarPoints(Vector2.zero, 10f, 4, 0.3f), Has.Length.EqualTo(8));
        }

        [Test]
        public void EverySpeakerHasAFrameCutForTheStretchedDialogueBox()
        {
            Assert.That(DialogueFrameArt.FrameFor("Cinna Cirra"), Is.EqualTo("CinnaCirra"));
            Assert.That(DialogueFrameArt.FrameFor("Prof. Bann"), Is.EqualTo(DialogueFrameArt.DefaultFrame), "Speakers without a frame share the neutral one.");
            foreach (string frame in new[] { "Stella", "CinnaCirra", "Orio", "Katrina", DialogueFrameArt.DefaultFrame })
            {
                Assert.That(DialogueFrameArt.TryGetPieces(frame, out Texture2D left, out Texture2D middle, out Texture2D right), Is.True, frame);
                // Half the 440 / 64 / 590 by 724 slices DialogueBox.uss was laid out against.
                Assert.That(new Vector2Int(left.width, left.height), Is.EqualTo(new Vector2Int(220, 362)), frame + " left cap");
                Assert.That(new Vector2Int(middle.width, middle.height), Is.EqualTo(new Vector2Int(32, 362)), frame + " middle strip");
                Assert.That(new Vector2Int(right.width, right.height), Is.EqualTo(new Vector2Int(295, 362)), frame + " right cap");
                Texture2D full = DialogueFrameArt.Full(frame);
                Assert.That(full.name, Is.EqualTo(frame), "DialogueView finds a frame's pieces by the full frame's name.");
                Assert.That(full.width, Is.EqualTo(1086));

                // The middle strip is stretched across wide screens, so every column must be the same. The uploaded
                // textures are GPU-only, so the pixels are read as drawn.
                ProceduralImage strip = DialogueFrameArt.DrawImages(frame).Middle;
                for (int y = 0; y < strip.Height; y++)
                    Assert.That(strip.Pixels[y * strip.Width + strip.Width - 1], Is.EqualTo(strip.Pixels[y * strip.Width]), frame + " middle strip row " + y);
            }

            Color32[] stella = DialogueFrameArt.DrawImages("Stella").Full.Pixels, orio = DialogueFrameArt.DrawImages("Orio").Full.Pixels;
            int different = 0;
            for (int i = 0; i < stella.Length; i++) if (!stella[i].Equals(orio[i])) different++;
            Assert.That(different, Is.GreaterThan(1000), "Each speaker's frame carries their own colour and emblem.");
        }

        [Test]
        public void EverySpellHasAnIconDrawnOnAClearBackground()
        {
            foreach (SpellId spell in System.Enum.GetValues(typeof(SpellId)))
            {
                Texture2D icon = SpellIconArt.Get(spell);
                Assert.That(icon, Is.Not.Null, spell.ToString());
                Assert.That(new Vector2Int(icon.width, icon.height), Is.EqualTo(new Vector2Int(256, 256)), spell.ToString());
                Assert.That(SpellCaster.GetIcon(spell), Is.SameAs(icon), "The wheel, chip and journal share one drawing.");
                Color32[] pixels = SpellIconArt.Draw(spell).Pixels;
                Assert.That(pixels[0].a, Is.Zero, spell + ": the wheel's own orb shows around the symbol.");
                byte strongest = 0;
                foreach (Color32 pixel in pixels) if (pixel.a > strongest) strongest = pixel.a;
                Assert.That(strongest, Is.EqualTo(255), spell + " draws a solid symbol.");
            }
            Assert.That(Resources.Load<Texture2D>("SpellIcons/Ampere"), Is.Null, "The generated icon images are gone.");
            Assert.That(Resources.Load<Texture2D>("Dialogue/Stella"), Is.Null, "The generated dialogue frames are gone.");
        }

        [Test]
        public void MenuButtonsKeepTheOriginalLayoutWithASocketWhereTheThreadArrives()
        {
            Assert.That(MenuButtonArt.LabelLeft, Is.GreaterThan(0f).And.LessThan(MenuButtonArt.LabelRight));
            Assert.That(MenuButtonArt.LabelRight, Is.LessThan(1f));
            foreach (string key in MenuButtonArt.Keys)
            {
                Texture2D sheet = MenuButtonArt.Sheet(key);
                // StartupMenu sizes buttons from a sheet of four stacked frames; the plaques keep the old 496x148 frames.
                float aspect = sheet.width / (sheet.height * 0.25f);
                Assert.That(aspect, Is.EqualTo(MenuButtonArt.FrameWidth / MenuButtonArt.FrameHeight).Within(0.02f), key);
                Assert.That(sheet.height % 4, Is.Zero, key + " stacks four whole frames.");

                // The menu's Weave thread ends a few pixels in from a button's left edge: the plaque's socket is there.
                Color32[] pixels = MenuButtonArt.DrawSheet(key).Pixels;
                float scale = sheet.width / MenuButtonArt.FrameWidth;
                int x = Mathf.RoundToInt(16f * scale), yFromTop = Mathf.RoundToInt(74f * scale);
                Assert.That(pixels[(sheet.height - 1 - yFromTop) * sheet.width + x].a, Is.EqualTo(255), key + " has a socket at the thread's end.");
            }
            Assert.That(Resources.Load<Texture2D>("StartupButtons/btn_start_sheet4x"), Is.Null, "The generated pixel-art sheets are gone.");
        }
    }
}
