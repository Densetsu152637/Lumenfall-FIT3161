using System;
using System.Collections.Generic;
using Lumenfall.DataDriven;

// Executable deterministic presentation contract checks, without Unity native calls.
internal static class WorldPresentationOfflineTests
{
    private static int checks;
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
    public static int Main()
    {
        try
        {
            var names = new HashSet<string>(); var symbols = new HashSet<string>(); var shapes = new HashSet<IdentityBadgeShape>();
            var fingerprints = new HashSet<string>();
            foreach (string id in new[] { "player", "stella", "cinna_cirra_orientation", "orio", "katrina", "receptionist" })
            {
                Check(WorldPresentationCatalog.TryCharacter(id, out CharacterPresentationIdentity identity), "Known identity missing: " + id);
                Check(names.Add(identity.Name) && symbols.Add(identity.Symbol) && shapes.Add(identity.Shape), "Identity relies on color only: " + id);
                string pixels = "";
                for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) pixels += WorldPresentationCatalog.BadgePixel(identity.Shape, (x-11.5f)/12, (y-11.5f)/12) ? "1" : "0";
                Check(fingerprints.Add(pixels) && pixels.Contains("1") && pixels.Contains("0"), "Badge not distinct: " + id);
                Check(identity.ArtRequirements.Length > 15, "Replacement brief missing: " + id);
            }
            WorldPresentationCatalog.TryCharacter("cinna_cirra_orientation", out CharacterPresentationIdentity before);
            WorldPresentationCatalog.TryCharacter("cinna_cirra_trial", out CharacterPresentationIdentity after);
            Check(ReferenceEquals(before, after), "Cinna's two placements must share identity");
            Check(!WorldPresentationCatalog.TryCharacter(null, out _) && !WorldPresentationCatalog.TryCharacter("katrina_fake", out _), "Unknown identities must not impersonate the cast");
            Check(!WorldPresentationCatalog.CanShowObject(true, false, true), "Hidden item leaked before reveal");
            Check(!WorldPresentationCatalog.CanShowObject(true, true, false), "Inactive map state leaked");
            Check(WorldPresentationCatalog.CanShowObject(true, true, true), "Revealed object should be visible");
            Check(WorldPresentationCatalog.CanShowObject(false, false, true), "Ordinary props should be visible");
            Check(WorldPresentationCatalog.Angle(-90) == "270 deg" && WorldPresentationCatalog.Angle(450) == "90 deg", "Rotation normalization");
            Check(WorldPresentationCatalog.Angle(float.NaN) == "unknown", "Invalid rotation feedback");
            Check(WorldPresentationCatalog.ObjectStateText("trial.cinna.ice", false, false, true, false, 0, 34, 67).Contains("34, 67"), "Trial feedback must use current authored cell");
            Check(WorldPresentationCatalog.ObjectStateText("relic.stella.power", false, true, false, false, 0, 0, 0).Contains("Power: off | Link: present"), "Power/link state must remain independent");
            Check(WorldPresentationCatalog.ObjectName("keepsake.katrina.hidden") == "Recovered keepsake", "Neutral keepsake category");
            Check(WorldPresentationCatalog.ObjectName("unknown") == null, "Unknown props should not be invented");
            Check(WorldPresentationCatalog.DormitoryRegionName("D05").Contains("Reserved") && WorldPresentationCatalog.DormitoryRegionName("D03").Contains("inactive"), "Reserved/optional dorm content");
            Check(WorldPresentationCatalog.BadgePixel(IdentityBadgeShape.Triangle, .5f, -.6f) && !WorldPresentationCatalog.BadgePixel(IdentityBadgeShape.Triangle, .5f, .6f), "Triangle direction");
            Console.WriteLine("PASS: " + checks + " offline presentation checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
