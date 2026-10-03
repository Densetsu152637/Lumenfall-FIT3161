using System;
using UnityEngine;

namespace Lumenfall.Spells
{
    public enum SpellCastOutcome { Applied, LockedSpell, NoTarget, UnknownTarget, OutOfRange, IncompatibleTarget, InputBlocked, WorldUnavailable, CasterUnavailable, MovementBlocked, NoChange }
    public enum ObjectFeedbackState { Idle, Valid, Invalid, Solved, Restored }
    public sealed class SpellCastFeedback
    {
        public readonly SpellId Spell;
        public readonly string TargetId;
        public readonly SpellCastOutcome Outcome;
        public bool Changed => Outcome == SpellCastOutcome.Applied;
        public SpellCastFeedback(SpellId spell, string targetId, SpellCastOutcome outcome)
        { Spell = spell; TargetId = targetId; Outcome = outcome; }
        public string Message => Outcome switch
        {
            SpellCastOutcome.Applied => Spell + " applied.",
            SpellCastOutcome.LockedSpell => "Learn " + Spell + " before casting it.",
            SpellCastOutcome.NoTarget => "No puzzle object nearby.",
            SpellCastOutcome.UnknownTarget => "That target is no longer in this area.",
            SpellCastOutcome.OutOfRange => "Move closer to the target.",
            SpellCastOutcome.IncompatibleTarget => "This object does not accept " + Spell + ".",
            SpellCastOutcome.InputBlocked => "Close the open menu or conversation to cast.",
            SpellCastOutcome.WorldUnavailable => "Wait for the area to finish loading.",
            SpellCastOutcome.CasterUnavailable => "Casting requires a player grid position.",
            SpellCastOutcome.MovementBlocked => "The object's path is blocked.",
            _ => Spell + " made no change. Check the puzzle objective."
        };
    }
    public sealed class WorldObjectFeedback
    {
        public readonly string ObjectId;
        public readonly ObjectFeedbackState State;
        public readonly bool IsSolved;
        public WorldObjectFeedback(string objectId, ObjectFeedbackState state, bool solved)
        { ObjectId = objectId; State = state; IsSolved = solved; }
    }
    /// Authored spell tags take precedence over category aliases. Untagged objects reject spells.
    public static class SpellTargetRules
    {
        public static bool Accepts(WorldObjectSnapshot target, SpellId spell)
        {
            if (target == null || !target.spellCompatible || !Enum.IsDefined(typeof(SpellId), spell)) return false;
            bool hasSpellTag = false;
            foreach (string tag in target.tags ?? Array.Empty<string>())
                foreach (SpellId candidate in Enum.GetValues(typeof(SpellId)))
                    if (IsSpellTag(tag, candidate)) hasSpellTag = true;
            if (hasSpellTag) return Array.Exists(target.tags ?? Array.Empty<string>(), tag => IsSpellTag(tag, spell));
            return spell switch
            {
                SpellId.Ampere => Has(target, "power-source") || Has(target, "relic"),
                SpellId.Weave => Has(target, "circuit") || Has(target, "power-source") || Prefix(target, "weave-with:"),
                SpellId.Gravitate => Has(target, "movable") || Has(target, "slidable"),
                SpellId.Rotara => Has(target, "rotatable") || Has(target, "mechanism"),
                SpellId.Glaciate => Has(target, "slidable") || Prefix(target, "slide-goal:"),
                SpellId.Vis => Has(target, "orientation"),
                SpellId.LostEchoes => Has(target, "hidden"),
                _ => false
            };
        }
        public static int Distance(Vector2Int origin, WorldObjectSnapshot target)
        {
            int maxX = target.cell.x + Mathf.Max(1, target.size.x) - 1;
            int maxY = target.cell.y + Mathf.Max(1, target.size.y) - 1;
            return Mathf.Abs(origin.x - Mathf.Clamp(origin.x, target.cell.x, maxX)) + Mathf.Abs(origin.y - Mathf.Clamp(origin.y, target.cell.y, maxY));
        }
        private static bool IsSpellTag(string tag, SpellId spell) => string.Equals(tag, spell.ToString(), StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "spell:" + spell, StringComparison.OrdinalIgnoreCase) || (spell == SpellId.LostEchoes && string.Equals(tag, "lost-echoes", StringComparison.OrdinalIgnoreCase));
        private static bool Has(WorldObjectSnapshot target, string tag) => Array.Exists(target.tags ?? Array.Empty<string>(), t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        private static bool Prefix(WorldObjectSnapshot target, string prefix) => Array.Exists(target.tags ?? Array.Empty<string>(), t => t != null && t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
