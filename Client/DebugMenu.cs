using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LunaStatusQuests
{
    public class DebugMenu : MonoBehaviour
    {
        private static bool _isVisible = false;
        private static Vector2 _scrollPosition = Vector2.zero;

        public static ConfigEntry<KeyCode> ToggleKey { get; private set; }
        public static ConfigEntry<int> MenuWidth { get; private set; }
        public static ConfigEntry<int> MenuHeight { get; private set; }

        // Search filter
        private static string _searchQuery = string.Empty;

        // Track the collapsed state of profiles and quest categories
        private static Dictionary<string, bool> _profileCollapsed = new Dictionary<string, bool>();
        private static Dictionary<string, Dictionary<string, bool>> _categoryCollapsed = new Dictionary<string, Dictionary<string, bool>>();

        public static void Init(ConfigFile config)
        {
            ToggleKey = config.Bind(
                "Debug",
                "ToggleKey",
                KeyCode.F10,
                new ConfigDescription("Key to toggle the debug menu")
            );

            MenuWidth = config.Bind(
                "Debug",
                "MenuWidth",
                400,
                new ConfigDescription("Width of the debug menu")
            );

            MenuHeight = config.Bind(
                "Debug",
                "MenuHeight",
                600,
                new ConfigDescription("Height of the debug menu")
            );

            Plugin.LogSource?.LogInfo("[LunaStatusQuestsDebugMenu] Debug menu initialized");
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey.Value))
            {
                _isVisible = !_isVisible;
            }
        }

        private void OnGUI()
        {
            if (!_isVisible) return;

            GUILayout.BeginArea(new Rect(10, 10, MenuWidth.Value, MenuHeight.Value), GUI.skin.box);
            GUILayout.Label("Debug Menu - Quest Statuses", GUI.skin.label);

            // Search bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search: ", GUILayout.Width(50));
            _searchQuery = GUILayout.TextField(_searchQuery, GUILayout.Width(MenuWidth.Value - 70));
            GUILayout.EndHorizontal();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(MenuWidth.Value - 20), GUILayout.Height(MenuHeight.Value - 80));

            foreach (var profile in Plugin.QuestStatuses)
            {
                // Apply search filter
                if (!string.IsNullOrEmpty(_searchQuery) && !profile.Key.ToLower().Contains(_searchQuery.ToLower()))
                {
                    continue;
                }

                // Toggle profile collapse
                if (!_profileCollapsed.ContainsKey(profile.Key))
                {
                    _profileCollapsed[profile.Key] = false;
                }

                if (GUILayout.Button($"{( _profileCollapsed[profile.Key] ? "▶" : "▼")} Profile: {profile.Key}"))
                {
                    _profileCollapsed[profile.Key] = !_profileCollapsed[profile.Key];
                }

                if (!_profileCollapsed[profile.Key])
                {
                    // Group quests by status
                    var groupedQuests = GroupQuestsByStatus(profile.Value);

                    foreach (var category in groupedQuests)
                    {
                        if (!_categoryCollapsed.ContainsKey(profile.Key))
                        {
                            _categoryCollapsed[profile.Key] = new Dictionary<string, bool>();
                        }

                        if (!_categoryCollapsed[profile.Key].ContainsKey(category.Key))
                        {
                            _categoryCollapsed[profile.Key][category.Key] = false;
                        }

                        // Toggle category collapse
                        if (GUILayout.Button($"  {( _categoryCollapsed[profile.Key][category.Key] ? "▶" : "▼")} {category.Key}"))
                        {
                            _categoryCollapsed[profile.Key][category.Key] = !_categoryCollapsed[profile.Key][category.Key];
                        }

                        if (!_categoryCollapsed[profile.Key][category.Key])
                        {
                            foreach (var quest in category.Value)
                            {
                                GUILayout.Label($"    - {quest.QuestName}: {Plugin.GetStatusName(quest.Status)}");
                            }
                        }
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private Dictionary<string, List<QuestStatusInfo>> GroupQuestsByStatus(Dictionary<string, QuestStatusInfo> quests)
        {
            var grouped = new Dictionary<string, List<QuestStatusInfo>>();

            foreach (var quest in quests.Values)
            {
                var statusName = Plugin.GetStatusName(quest.Status);
                if (!grouped.ContainsKey(statusName))
                {
                    grouped[statusName] = new List<QuestStatusInfo>();
                }

                grouped[statusName].Add(quest);
            }

            return grouped;
        }
    }
}