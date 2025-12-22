using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LunaStatusQuests
{
    public static class Settings
    {
        private static ConfigFile _config;
        private static List<string> _availableProfiles = new();

        public static ConfigEntry<bool> Enabled { get; private set; }
        public static ConfigEntry<int> UpdateIntervalSeconds { get; private set; }
        public static ConfigEntry<string> VisibleProfiles { get; private set; }
        public static ConfigEntry<bool> ShowDebugLogs { get; private set; }
        public static ConfigEntry<bool> ShowInTrader { get; private set; }

        public static void Init(ConfigFile config)
        {
            _config = config;

            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                new ConfigDescription("Enable LunaStatusQuests mod")
            );

            UpdateIntervalSeconds = config.Bind(
                "General",
                "UpdateIntervalSeconds",
                60,
                new ConfigDescription(
                    "How often to fetch from server (seconds)",
                    new AcceptableValueRange<int>(1, 60)
                )
            );

            ShowDebugLogs = config.Bind(
                "General",
                "ShowDebugLogs",
                false,
                new ConfigDescription("Show debug messages")
            );

            ShowInTrader = config.Bind(
                "General",
                "ShowStatusInTrader",
                true,
                new ConfigDescription("Show shared quest status in trader quest view")
            );

            VisibleProfiles = config.Bind(
                "Profiles",
                "VisibleProfiles",
                "*",
                new ConfigDescription(
                    "Profile names to show (* = all). Exclude with '-': '*,-BotName' or list specific: 'Player1,Player2'"
                )
            );

            Plugin.LogSource?.LogInfo("[LunaStatusQuestsSettings] Settings initialized");
        }

        public static void UpdateProfileList(List<string> profileNames)
        {
            _availableProfiles = profileNames ?? new List<string>();
            
            if (_availableProfiles.Count > 0)
            {
                Plugin.LogSource?.LogInfo($"[LunaStatusQuestsSettings] Available profiles: {string.Join(", ", _availableProfiles)}");
            }
        }

        public static bool IsProfileVisible(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
                return false;

            var visibleStr = VisibleProfiles.Value.Trim();

            // "*" means show all
            if (visibleStr == "*")
                return true;

            var entries = visibleStr.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            // Check exclusions (prefixed with -)
            foreach (var entry in entries.Where(e => e.StartsWith("-")))
            {
                var excludedName = entry.Substring(1);
                if (profileName.Equals(excludedName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Handle wildcard + exclusions pattern: "*,-Name"
            bool hasWildcard = entries.Any(e => e == "*");

            // Named inclusions (whitelist mode), excluding the wildcard.
            var namedInclusions = entries
                .Where(e => !e.StartsWith("-") && e != "*")
                .ToList();

            // If we have explicit named inclusions, only show those.
            if (namedInclusions.Count > 0)
            {
                foreach (var entry in namedInclusions)
                {
                    if (profileName.Equals(entry, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Not in the explicit whitelist.
                return false;
            }

            // No named inclusions:
            // - If we have a wildcard, show everything except explicit exclusions.
            // - If we only had exclusions, and this profile wasn't excluded, show it.
            if (hasWildcard)
                return true;

            return true;
        }
    }
}