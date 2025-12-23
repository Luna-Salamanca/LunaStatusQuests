using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace LunaStatusQuests.Services
{
    /// <summary>
    /// Service that manages mod configuration and profile visibility logic.
    /// Acts as a wrapper around the BepInEx ConfigFile system.
    /// </summary>
    public interface ISettingsService
    {
        bool Enabled { get; }
        int UpdateIntervalSeconds { get; }
        bool ShowDebugLogs { get; }
        bool ShowInTrader { get; }

        // Debug Menu Settings
        KeyCode ToggleKey { get; }
        int MenuWidth { get; }
        int MenuHeight { get; }

        /// <summary>
        /// Updates the internal list of available profile names found on the server.
        /// </summary>
        void UpdateProfileList(List<string> profileNames);

        /// <summary>
        /// Determines if a specific profile should be displayed based on user configuration.
        /// </summary>
        /// <param name="profileName">The name of the profile to check.</param>
        /// <returns>True if visible, false otherwise.</returns>
        bool IsProfileVisible(string profileName);
    }

    /// <summary>
    /// Concrete implementation of ISettingsService.
    /// Binds properties to the BepInEx configuration system on initialization.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _updateIntervalSeconds;
        private ConfigEntry<string> _visibleProfiles;
        private ConfigEntry<bool> _showDebugLogs;
        private ConfigEntry<bool> _showInTrader;
        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<int> _menuWidth;
        private ConfigEntry<int> _menuHeight;

        private List<string> _availableProfiles = new List<string>();

        public SettingsService(ConfigFile config)
        {
            Init(config);
        }

        public bool Enabled => _enabled.Value;
        public int UpdateIntervalSeconds => _updateIntervalSeconds.Value;
        public bool ShowDebugLogs => _showDebugLogs.Value;
        public bool ShowInTrader => _showInTrader.Value;
        public KeyCode ToggleKey => _toggleKey.Value;
        public int MenuWidth => _menuWidth.Value;
        public int MenuHeight => _menuHeight.Value;

        /// <summary>
        /// Binds the private ConfigEntry fields to the physical config file.
        /// Defines sections, names, default values, and descriptions for the F12 menu.
        /// </summary>
        private void Init(ConfigFile config)
        {
            _enabled = config.Bind(
                "General",
                "Enabled",
                true,
                new ConfigDescription("Enable LunaStatusQuests mod")
            );

            _updateIntervalSeconds = config.Bind(
                "General",
                "UpdateIntervalSeconds",
                60,
                new ConfigDescription(
                    "How often to fetch from server (seconds)",
                    new AcceptableValueRange<int>(1, 60)
                )
            );

            _showDebugLogs = config.Bind(
                "General",
                "ShowDebugLogs",
                false,
                new ConfigDescription("Show debug messages in the console")
            );

            _showInTrader = config.Bind(
                "General",
                "ShowStatusInTrader",
                true,
                new ConfigDescription("Show quest status text within the trader's quest view UI")
            );

            _toggleKey = config.Bind(
                "Debug",
                "ToggleKey",
                KeyCode.F10,
                new ConfigDescription("Key to toggle the on-screen debug status menu")
            );

            _menuWidth = config.Bind(
                "Debug",
                "MenuWidth",
                400,
                new ConfigDescription("Width of the debug overlay")
            );

            _menuHeight = config.Bind(
                "Debug",
                "MenuHeight",
                600,
                new ConfigDescription("Height of the debug overlay")
            );

            _visibleProfiles = config.Bind(
                "Profiles",
                "VisibleProfiles",
                "*",
                new ConfigDescription(
                    "Filter which profiles to display. Use '*' for all. \nExclude: '*,-BotName'. \nInclude only: 'Player1,Player2'."
                )
            );
        }

        public void UpdateProfileList(List<string> profileNames)
        {
            _availableProfiles = profileNames ?? new List<string>();
        }

        /// <summary>
        /// Evaluates the 'VisibleProfiles' config string to filter profiles.
        /// Supports wildcards (*), inclusions (Player1), and exclusions (-Bot1).
        /// </summary>
        public bool IsProfileVisible(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
                return false;

            var visibleStr = _visibleProfiles.Value.Trim();

            if (visibleStr == "*")
                return true;

            var entries = visibleStr
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            // Priority 1: Check for explicit exclusions (e.g., "-BotName").
            foreach (var entry in entries.Where(e => e.StartsWith("-")))
            {
                var excludedName = entry.Substring(1);
                if (profileName.Equals(excludedName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Priority 2: Check for explicit inclusions (white-list mode).
            var namedInclusions = entries.Where(e => !e.StartsWith("-") && e != "*").ToList();

            if (namedInclusions.Count > 0)
            {
                foreach (var entry in namedInclusions)
                {
                    if (profileName.Equals(entry, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // If a white-list is defined and the profile isn't on it, it's hidden.
                return false;
            }

            // Priority 3: Respect wildcard flag.
            bool hasWildcard = entries.Any(e => e == "*");
            if (hasWildcard)
                return true;

            // Default to visible if no specific whitelist was provided.
            return true;
        }
    }
}
