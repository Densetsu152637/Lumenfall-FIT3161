using System;

namespace Lumenfall.Accessibility
{
    /// <summary>Stable IDs can be supplied by a future localization provider; authored English remains the fallback.</summary>
    public static class Localization
    {
        public static Func<string, string> Resolver { get; set; }
        public static string Resolve(string id, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(id) && Resolver != null)
            {
                try
                {
                    string value = Resolver(id);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                catch (Exception) { /* A missing/broken translation provider must not prevent gameplay. */ }
            }
            return fallback ?? id ?? string.Empty;
        }
        public static string DialogueTextId(string dialogueId, string nodeId) => $"dialogue.{dialogueId}.{nodeId}.text";
        public static string DialogueChoiceId(string dialogueId, string nodeId, string choiceId) => $"dialogue.{dialogueId}.{nodeId}.choice.{choiceId}";
    }
}
