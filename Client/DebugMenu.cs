using System.Collections.Generic;
using LunaStatusQuests.Services;
using UnityEngine;

namespace LunaStatusQuests
{
    public class DebugMenu : MonoBehaviour
    {
        private bool _isVisible = false;
        private Vector2 _scrollPosition = Vector2.zero;

        private ISettingsService _settingsService;
        private IQuestService _questService;
        private IUiService _uiService;

        private string _searchQuery = string.Empty;
        private Dictionary<string, bool> _profileCollapsed = new Dictionary<string, bool>();
        private Dictionary<string, Dictionary<string, bool>> _categoryCollapsed =
            new Dictionary<string, Dictionary<string, bool>>();

        private void Awake()
        {
            try
            {
                _settingsService = ServiceContainer.Resolve<ISettingsService>();
                _questService = ServiceContainer.Resolve<IQuestService>();
                _uiService = ServiceContainer.Resolve<IUiService>();

                Plugin.LogSource?.LogInfo(
                    "[LunaStatusQuestsDebugMenu] Debug menu component initialized with DI"
                );
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogError(
                    $"[LunaStatusQuestsDebugMenu] Failed to resolve services: {ex.Message}"
                );
                Destroy(this);
            }
        }

        private void Update()
        {
            if (_settingsService == null)
                return;

            if (Input.GetKeyDown(_settingsService.ToggleKey))
            {
                _isVisible = !_isVisible;
            }
        }

        private void OnGUI()
        {
            if (_settingsService == null || _questService == null || _uiService == null)
                return;

            if (!_isVisible)
                return;

            var width = _settingsService.MenuWidth;
            var height = _settingsService.MenuHeight;

            GUILayout.BeginArea(new Rect(10, 10, width, height), GUI.skin.box);
            GUILayout.Label("Debug Menu - Quest Statuses", GUI.skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search: ", GUILayout.Width(50));
            _searchQuery = GUILayout.TextField(_searchQuery, GUILayout.Width(width - 70));
            GUILayout.EndHorizontal();

            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Width(width - 20),
                GUILayout.Height(height - 80)
            );

            var snapshotStatuses = _questService.QuestStatuses;

            if (snapshotStatuses != null)
            {
                foreach (var profile in snapshotStatuses)
                {
                    if (
                        !string.IsNullOrEmpty(_searchQuery)
                        && !profile.Key.ToLower().Contains(_searchQuery.ToLower())
                    )
                    {
                        continue;
                    }

                    if (!_profileCollapsed.ContainsKey(profile.Key))
                    {
                        _profileCollapsed[profile.Key] = false;
                    }

                    if (
                        GUILayout.Button(
                            $"{(_profileCollapsed[profile.Key] ? "▶" : "▼")} Profile: {profile.Key}"
                        )
                    )
                    {
                        _profileCollapsed[profile.Key] = !_profileCollapsed[profile.Key];
                    }

                    if (!_profileCollapsed[profile.Key])
                    {
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

                            if (
                                GUILayout.Button(
                                    $"  {(_categoryCollapsed[profile.Key][category.Key] ? "▶" : "▼")} {category.Key}"
                                )
                            )
                            {
                                _categoryCollapsed[profile.Key][category.Key] = !_categoryCollapsed[
                                    profile.Key
                                ][category.Key];
                            }

                            if (!_categoryCollapsed[profile.Key][category.Key])
                            {
                                foreach (var quest in category.Value)
                                {
                                    GUILayout.Label(
                                        $"    - {quest.QuestName}: {_uiService.GetStatusName(quest.Status)}"
                                    );
                                }
                            }
                        }
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private Dictionary<string, List<QuestStatusInfo>> GroupQuestsByStatus(
            Dictionary<string, QuestStatusInfo> quests
        )
        {
            var grouped = new Dictionary<string, List<QuestStatusInfo>>();

            foreach (var quest in quests.Values)
            {
                var statusName = _uiService.GetStatusName(quest.Status);
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
