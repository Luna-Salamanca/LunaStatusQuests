using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using EFT.UI;
using HarmonyLib;
using LunaStatusQuests.Services;
using TMPro;
using UnityEngine;

namespace LunaStatusQuests
{
    /// <summary>
    /// Enum representing the various states a quest can be in.
    /// </summary>
    public enum EQuestStatus
    {
        Locked = 0,
        Available = 1,
        Started = 2,
        AvailableForFinish = 3,
        Completed = 4,
        Fail = 5,
        FailRestartable = 6,
        Fail2 = 7,
        Expired = 8,
        TimeExpired = 9,
    }

    /// <summary>
    /// Contains quest status information including status, locked reason, and quest name.
    /// </summary>
    public class QuestStatusInfo
    {
        [Newtonsoft.Json.JsonProperty("status")]
        public EQuestStatus Status { get; set; }

        [Newtonsoft.Json.JsonProperty("lockedReason")]
        public string LockedReason { get; set; }

        [Newtonsoft.Json.JsonProperty("questName")]
        public string QuestName { get; set; }
    }

    [BepInPlugin("com.LunaStatusQuests.client", "LunaStatusQuests", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static Plugin Instance;

        private const float MONITOR_UPDATE_INTERVAL = 0.15f;
        private const float TRADER_MONITOR_UPDATE_INTERVAL = 0.75f;
        private const int MIN_DESCRIPTION_LENGTH = 50;

        private static Coroutine _monitorCoroutine = null;
        private static Coroutine _traderMonitorCoroutine = null;

        private ISettingsService _settingsService;
        private IQuestService _questService;
        private IUiService _uiService;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogSource.LogInfo("[LunaStatusQuestsClient] Client loading...");

            try
            {
                _settingsService = new SettingsService(Config);
                ServiceContainer.Register<ISettingsService>(_settingsService);

                _questService = new QuestService(_settingsService, LogSource);
                ServiceContainer.Register<IQuestService>(_questService);

                _uiService = new UiService(_questService, _settingsService, LogSource);
                ServiceContainer.Register<IUiService>(_uiService);

                var harmony = new Harmony("com.LunaStatusQuests.client");
                harmony.PatchAll();

                gameObject.AddComponent<DebugMenu>();

                LogSource.LogInfo("[LunaStatusQuestsClient] Client loaded successfully with DI!");
            }
            catch (Exception ex)
            {
                LogSource.LogError(
                    $"[LunaStatusQuestsClient] ERROR during load: {ex.Message}\n{ex.StackTrace}"
                );
            }
        }

        private void Start()
        {
            StartCoroutine(InitialFetch());
        }

        private IEnumerator InitialFetch()
        {
            yield return new WaitForSeconds(3f);

            if (_settingsService.Enabled)
            {
                LogSource.LogInfo("[LunaStatusQuestsClient] Performing initial fetch...");
                _questService.FetchQuestStatuses(force: true);
            }
        }

        public void StartMonitor()
        {
            StopMonitor();
            _monitorCoroutine = StartCoroutine(MonitorDescriptionLabel());
            LogSource.LogInfo("[LunaStatusQuestsClient] Monitor started");
        }

        public void StopMonitor()
        {
            if (_monitorCoroutine != null)
            {
                StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }
        }

        public void StartTraderMonitor()
        {
            StopTraderMonitor();
            _traderMonitorCoroutine = StartCoroutine(MonitorTraderDescriptionLabel());
            LogSource.LogInfo("[LunaStatusQuestsClient] Trader monitor started");
        }

        public void StopTraderMonitor()
        {
            if (_traderMonitorCoroutine != null)
            {
                StopCoroutine(_traderMonitorCoroutine);
                _traderMonitorCoroutine = null;
            }
        }

        private IEnumerator MonitorDescriptionLabel()
        {
            string lastCleanText = "";
            DateTime lastInjectedFetchTime = DateTime.MinValue;
            TextMeshProUGUI descriptionLabel = null;

            while (true)
            {
                TasksScreen currentScreen = ServiceProvider.CurrentTasksScreen;

                if (currentScreen == null)
                    break;

                var ageSeconds = (DateTime.UtcNow - _questService.LastFetch).TotalSeconds;
                if (ageSeconds >= _settingsService.UpdateIntervalSeconds)
                {
                    _questService.FetchQuestStatuses(force: false);
                }

                if (descriptionLabel == null || !descriptionLabel)
                {
                    try
                    {
                        var tmpComponents = currentScreen.GetComponentsInChildren<TextMeshProUGUI>(
                            true
                        );
                        foreach (var tmp in tmpComponents)
                        {
                            if (
                                string.Equals(
                                    tmp.name,
                                    "DescriptionLabel",
                                    StringComparison.Ordinal
                                )
                                && tmp.text?.Length > MIN_DESCRIPTION_LENGTH
                            )
                            {
                                descriptionLabel = tmp;
                                descriptionLabel.richText = true;
                                break;
                            }
                        }
                    }
                    catch (Exception) { }
                }

                if (descriptionLabel != null && descriptionLabel.text != null)
                {
                    try
                    {
                        string currentCleanText = _uiService.RemoveStatusSection(
                            descriptionLabel.text
                        );
                        bool descriptionChanged = currentCleanText != lastCleanText;
                        bool needsMarker = !descriptionLabel.text.Contains("Luna Quest Status");
                        bool dataIsNew = _questService.LastFetch > lastInjectedFetchTime;

                        if (needsMarker || dataIsNew || descriptionChanged)
                        {
                            string questId = _questService.CurrentQuestId;

                            if (!string.IsNullOrEmpty(questId))
                            {
                                var newText = _uiService.InjectStatusText(
                                    descriptionLabel.text,
                                    questId
                                );

                                if (descriptionLabel.text != newText)
                                {
                                    descriptionLabel.text = newText;
                                    descriptionLabel.ForceMeshUpdate();

                                    lastCleanText = _uiService.RemoveStatusSection(newText);
                                    lastInjectedFetchTime = _questService.LastFetch;
                                }
                            }
                            else
                            {
                                descriptionLabel.text = _uiService.RemoveStatusSection(
                                    descriptionLabel.text
                                );
                                lastCleanText = descriptionLabel.text;
                                lastInjectedFetchTime = DateTime.MinValue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSource.LogError(
                            $"[LunaStatusQuestsClient] Error updating description label: {ex.Message}"
                        );
                    }
                }

                yield return new WaitForSeconds(MONITOR_UPDATE_INTERVAL);
            }

            LogSource.LogInfo("[LunaStatusQuestsClient] Monitor stopped");
        }

        private IEnumerator MonitorTraderDescriptionLabel()
        {
            string lastCleanText = "";
            DateTime lastInjectedFetchTime = DateTime.MinValue;

            while (true)
            {
                if (!_settingsService.Enabled || !_settingsService.ShowInTrader)
                    break;

                var descriptionLabel = ServiceProvider.CurrentTraderDescriptionLabel;

                if (descriptionLabel == null || !descriptionLabel)
                    break;

                _questService.FetchQuestStatuses(force: false);

                try
                {
                    string currentCleanText = _uiService.RemoveStatusSection(descriptionLabel.text);
                    bool descriptionChanged = currentCleanText != lastCleanText;
                    bool needsMarker = !descriptionLabel.text.Contains("Luna Quest Status");
                    bool dataIsNew = _questService.LastFetch > lastInjectedFetchTime;

                    if (needsMarker || dataIsNew || descriptionChanged)
                    {
                        string questId = _questService.CurrentQuestId;

                        if (!string.IsNullOrEmpty(questId))
                        {
                            var newText = _uiService.InjectStatusText(
                                descriptionLabel.text,
                                questId
                            );
                            if (descriptionLabel.text != newText)
                            {
                                descriptionLabel.text = newText;
                                descriptionLabel.ForceMeshUpdate();

                                lastCleanText = _uiService.RemoveStatusSection(newText);
                                lastInjectedFetchTime = _questService.LastFetch;
                            }
                        }
                        else
                        {
                            descriptionLabel.text = _uiService.RemoveStatusSection(
                                descriptionLabel.text
                            );
                            lastCleanText = descriptionLabel.text;
                            lastInjectedFetchTime = DateTime.MinValue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogSource.LogError(
                        $"[LunaStatusQuestsClient] Error updating trader description label: {ex.Message}"
                    );
                }

                yield return new WaitForSeconds(TRADER_MONITOR_UPDATE_INTERVAL);
            }

            LogSource.LogInfo("[LunaStatusQuestsClient] Trader monitor stopped");
        }
    }

    /// <summary>
    /// Static holder for transient UI state that isn't really a "service" but shared across patches.
    /// </summary>
    public static class ServiceProvider
    {
        public static TasksScreen CurrentTasksScreen { get; set; }
        public static TextMeshProUGUI CurrentTraderDescriptionLabel { get; set; }
    }
}
