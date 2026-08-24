using System;

namespace Lumenfall.GameEvents
{
    public static class GameEventMetadataExtensions
    {
        /// <summary>Returns a metadata value without making callers repeat a linear lookup.</summary>
        public static string GetValue(
            this GameMetadataEntry[] metadata,
            string key,
            string fallback = null)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            foreach (GameMetadataEntry entry in metadata)
            {
                if (entry != null && string.Equals(entry.key, key, StringComparison.Ordinal))
                {
                    return entry.value;
                }
            }

            return fallback;
        }
    }
}
