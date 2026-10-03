using System;
using System.Globalization;

namespace Lumenfall.DataDriven
{
    public enum IdentityBadgeShape { Diamond, Bow, Cloud, Triangle, Square, Cross }

    public sealed class CharacterPresentationIdentity
    {
        public string Name { get; }
        public string Symbol { get; }
        public uint AccentRgb { get; }
        public IdentityBadgeShape Shape { get; }
        public string ArtRequirements { get; }
        internal CharacterPresentationIdentity(string name, string symbol, uint rgb, IdentityBadgeShape shape, string requirements)
        { Name = name; Symbol = symbol; AccentRgb = rgb; Shape = shape; ArtRequirements = requirements; }
    }

    /// <summary>Approved identities and neutral placeholder copy. Never owns progression or final art.</summary>
    public static class WorldPresentationCatalog
    {
        private static readonly CharacterPresentationIdentity Player = new CharacterPresentationIdentity(
            "Player", "P", 0xE4BF58, IdentityBadgeShape.Diamond, "Player appearance awaits approval.");
        private static readonly CharacterPresentationIdentity Stella = new CharacterPresentationIdentity(
            "Stella", "S", 0xF097AC, IdentityBadgeShape.Bow, "Neat fashionable outfit; combat boots, red bow, regular glasses.");
        private static readonly CharacterPresentationIdentity Cinna = new CharacterPresentationIdentity(
            "Cinna Cirra", "C", 0xB6A3E0, IdentityBadgeShape.Cloud, "Loose comfortable clothing, long trousers, T-shirt and jacket.");
        private static readonly CharacterPresentationIdentity Orio = new CharacterPresentationIdentity(
            "Orio", "O", 0xEDAA66, IdentityBadgeShape.Triangle, "Tropical summer outfit, flip-flops, relaxed expression.");
        private static readonly CharacterPresentationIdentity Katrina = new CharacterPresentationIdentity(
            "Katrina", "K", 0x81BCF4, IdentityBadgeShape.Square, "Long white skirt; white-and-blue sneakers. Glasses optional.");
        private static readonly CharacterPresentationIdentity Receptionist = new CharacterPresentationIdentity(
            "Receptionist", "R", 0x8DCEAE, IdentityBadgeShape.Cross, "Reception role only; personal appearance awaits approval.");

        public static bool TryCharacter(string id, out CharacterPresentationIdentity identity)
        {
            switch (id)
            {
                case "player": identity = Player; break;
                case "stella": identity = Stella; break;
                case "cinna_cirra_orientation": case "cinna_cirra_trial": case "cinna_cirra": identity = Cinna; break;
                case "orio": identity = Orio; break;
                case "katrina": identity = Katrina; break;
                case "receptionist": identity = Receptionist; break;
                default: identity = null; break;
            }
            return identity != null;
        }

        // Coordinates belong to the current authored world, not the main-branch layout.
        public static string ObjectName(string id)
        {
            switch (id)
            {
                case "relic.stella.power": return "Power relic";
                case "circuit.stella.link": return "Link relic";
                case "orientation.cinna.align": return "Orientation marker";
                case "trial.cinna.ice": return "Trial surface";
                case "mechanism.orio.rotate": return "Rotation mechanism";
                case "keepsake.katrina.hidden": return "Recovered keepsake";
                default: return null;
            }
        }

        public static bool CanShowObject(bool hidden, bool revealed, bool stateAvailable) =>
            stateAvailable && (!hidden || revealed);

        public static string ObjectStateText(string id, bool powered, bool connected, bool frozen, bool aligned,
            float rotation, int cellX, int cellY)
        {
            switch (id)
            {
                case "relic.stella.power": return "Power: " + (powered ? "on" : "off") + " | Link: " + (connected ? "present" : "absent");
                case "circuit.stella.link": return "Link: " + (connected ? "present" : "absent");
                case "orientation.cinna.align": return "Aligned: " + (aligned ? "yes" : "no") + " | Rotation: " + Angle(rotation);
                case "trial.cinna.ice": return (frozen ? "Frozen" : "Unfrozen") + " | Cell: " + cellX + ", " + cellY;
                case "mechanism.orio.rotate": return "Rotation: " + Angle(rotation);
                case "keepsake.katrina.hidden": return "Revealed";
                default: return string.Empty;
            }
        }

        public static string Angle(float rotation)
        {
            if (float.IsNaN(rotation) || float.IsInfinity(rotation)) return "unknown";
            return (((rotation % 360f) + 360f) % 360f).ToString("0.#", CultureInfo.InvariantCulture) + " deg";
        }

        public static string DormitoryRegionName(string regionId)
        {
            switch (regionId)
            {
                case "D01": return "Sirius entrance";
                case "D02": return "Sirius common area";
                case "D03": return "Waypoint alcove | Optional / inactive";
                case "D04": return "Residential key door zone";
                case "D07": return "Group living zone";
                case "D08": return "Player sleeping room";
                case "D05": case "D06": case "D09": case "D10": case "D11": case "D12": case "D13": case "D14":
                    return regionId + " | Reserved";
                default: return null;
            }
        }

        // Six silhouettes supplement names and letters so identity never depends on color alone.
        public static bool BadgePixel(IdentityBadgeShape shape, float x, float y)
        {
            float signedY = y;
            x = Math.Abs(x); y = Math.Abs(y);
            switch (shape)
            {
                case IdentityBadgeShape.Diamond: return x + y <= 0.9f;
                case IdentityBadgeShape.Bow: return x <= 0.9f && y <= 0.75f && y <= x + 0.18f;
                case IdentityBadgeShape.Cloud: return x * x + y * y <= 0.65f || (x < 0.8f && y < 0.35f);
                case IdentityBadgeShape.Triangle: return signedY >= -0.8f && signedY <= 0.8f && x <= (0.8f - signedY) * 0.55f;
                case IdentityBadgeShape.Square: return x <= 0.8f && y <= 0.8f;
                case IdentityBadgeShape.Cross: return (x <= 0.25f && y <= 0.85f) || (y <= 0.25f && x <= 0.85f);
                default: return false;
            }
        }
    }
}
