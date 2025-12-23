using BepInEx;
using BepInEx.Logging;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        TimeExpired = 9
    }

    /// <summary>
    /// Contains quest status information including status, locked reason, and quest name.
    /// </summary>
    public class QuestStatusInfo
    {
        [JsonProperty("status")]
        public EQuestStatus Status { get; set; }

        [JsonProperty("lockedReason")]
        public string LockedReason { get; set; }

        [JsonProperty("questName")]
        public string QuestName { get; set; }
    }

    [BepInPlugin("com.LunaStatusQuests.client", "LunaStatusQuests", "1.0.0")]
    // Heavily inspired by the SharedQuests mod for SPT 4.0 by amorijs:
    // https://github.com/amorijs/spt-SharedQuests
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static Plugin Instance;
        
        public static Dictionary<string, Dictionary<string, QuestStatusInfo>> QuestStatuses = 
            new Dictionary<string, Dictionary<string, QuestStatusInfo>>();
        
        public static readonly object QuestStatusesLock = new object();
        
        public static DateTime LastFetch = DateTime.MinValue;
        
        private const string STATUS_MARKER_START = "--- Luna Quest Status ---";
        private const string STATUS_MARKER_END = "--------------------------";
        private const float MONITOR_UPDATE_INTERVAL = 0.15f;
        private const float TRADER_MONITOR_UPDATE_INTERVAL = 0.75f;
        private const int MIN_DESCRIPTION_LENGTH = 50;
        
        private static readonly Regex StatusSectionPattern = new Regex(
            @"<color=#\w+>---\s*(Shared|Luna) Quest Status\s*---<\/color>[\s\S]*?<color=#\w+>-{20,}<\/color>\s*",
            RegexOptions.Compiled);
        
        public static string CurrentQuestId = null;
        public static readonly object QuestIdLock = new object();
        
        public static TasksScreen CurrentTasksScreen = null;
        public static readonly object TasksScreenLock = new object();

        public static QuestView CurrentTraderQuestView = null;
        public static TextMeshProUGUI CurrentTraderDescriptionLabel = null;
        public static readonly object TraderViewLock = new object();
        private static readonly object FetchLock = new object();
        
        private static Coroutine _monitorCoroutine = null;
        private static Coroutine _traderMonitorCoroutine = null;
        private static bool _fetchInProgress = false;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;
            LogSource.LogInfo("[LunaStatusQuestsClient] Client loading...");

            try
            {
                Settings.Init(Config);
                DebugMenu.Init(Config);

                var harmony = new Harmony("com.LunaStatusQuests.client");
                harmony.PatchAll();

                gameObject.AddComponent<DebugMenu>();

                LogSource.LogInfo("[LunaStatusQuestsClient] Client loaded successfully!");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"[LunaStatusQuestsClient] ERROR during load: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void Start()
        {
            StartCoroutine(InitialFetch());
        }

        private IEnumerator InitialFetch()
        {
            yield return new WaitForSeconds(3f);
            
            if (Settings.Enabled.Value)
            {
                LogSource.LogInfo("[LunaStatusQuestsClient] Performing initial fetch...");
                FetchQuestStatuses(force: true);
            }
        }

        /// <summary>
        /// Fetches quest statuses from the server with throttling support.
        /// </summary>
        /// <param name="force">If true, bypasses the update interval check.</param>
        /// <returns>True if fetch was successful, false otherwise.</returns>
        public static bool FetchQuestStatuses(bool force = false)
        {
            if (!Settings.Enabled.Value)
                return false;

            var ageSeconds = (DateTime.UtcNow - LastFetch).TotalSeconds;
            if (!force && ageSeconds < Settings.UpdateIntervalSeconds.Value)
            {
                return false;
            }

            lock (FetchLock)
            {
                if (_fetchInProgress)
                {
                    return false;
                }

                _fetchInProgress = true;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    LogSource.LogDebug("[LunaStatusQuestsClient] Fetching from server...");
                    var response = RequestHandler.GetJson("/LunaStatusQuests/statuses");

                    if (!string.IsNullOrEmpty(response))
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, QuestStatusInfo>>>(response);
                        if (data != null && data.Count > 0)
                        {
                            lock (QuestStatusesLock)
                            {
                                QuestStatuses.Clear();
                                foreach (var kvp in data)
                                {
                                    QuestStatuses[kvp.Key] = kvp.Value;
                                }
                            }

                            LastFetch = DateTime.UtcNow;

                        var firstProfileName = data.Keys.FirstOrDefault();
                        if (Settings.ShowDebugLogs.Value && !string.IsNullOrEmpty(firstProfileName))
                        {
                            var firstProfileQuestCount = data[firstProfileName].Count;
                            LogSource.LogDebug($"[LunaStatusQuestsClient] Sample profile: {firstProfileName} has {firstProfileQuestCount} quests");
                        }

                        LogSource.LogInfo($"[LunaStatusQuestsClient] FETCH SUCCESS: {data.Count} profiles, {data.Values.Sum(x => x.Count)} quests");

                            Settings.UpdateProfileList(data.Keys.ToList());
                        }
                        else
                        {
                            LogSource.LogWarning("[LunaStatusQuestsClient] Response deserialized but empty");
                        }
                    }
                    else
                    {
                        LogSource.LogWarning("[LunaStatusQuestsClient] Server returned empty response");
                    }
                }
                catch (Exception ex)
                {
                    LogSource.LogError($"[LunaStatusQuestsClient] FETCH ERROR: {ex.Message}");
                }
                finally
                {
                    lock (FetchLock)
                    {
                        _fetchInProgress = false;
                    }
                }
            });

            return true;
        }

        /// <summary>
        /// Gets the display name for a quest status.
        /// </summary>
        public static string GetStatusName(EQuestStatus status)
        {
            return status switch
            {
                EQuestStatus.Locked => "Locked",
                EQuestStatus.Available => "Available",
                EQuestStatus.Started => "Started",
                EQuestStatus.AvailableForFinish => "Ready!",
                EQuestStatus.Completed => "Completed",
                EQuestStatus.Fail => "Failed",
                EQuestStatus.FailRestartable => "Failed (Retry)",
                EQuestStatus.Fail2 => "Failed",
                EQuestStatus.Expired => "Expired",
                EQuestStatus.TimeExpired => "Timed",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Gets the color code for a quest status.
        /// </summary>
        public static string GetStatusColor(EQuestStatus status)
        {
            return status switch
            {
                EQuestStatus.Locked => "#808080",
                EQuestStatus.Available => "#FFD700",
                EQuestStatus.Started => "#FFA500",
                EQuestStatus.AvailableForFinish => "#00FF00",
                EQuestStatus.Completed => "#32CD32",
                EQuestStatus.Fail => "#FF4444",
                EQuestStatus.FailRestartable => "#FF6600",
                EQuestStatus.Fail2 => "#FF4444",
                EQuestStatus.Expired => "#666666",
                EQuestStatus.TimeExpired => "#87CEEB",
                _ => "#FFFFFF"
            };
        }

        /// <summary>
        /// Builds the status text to be displayed in the quest description.
        /// </summary>
        public static string BuildStatusText(string questId)
        {
            if (!Settings.Enabled.Value)
                return "";

            lock (QuestStatusesLock)
            {
                if (QuestStatuses.Count == 0 || string.IsNullOrEmpty(questId))
                {
                    return $"<color=#9A8866>{STATUS_MARKER_START}</color>\n<color=#888888>Loading...</color>\n<color=#9A8866>{STATUS_MARKER_END}</color>";
                }

                var lines = new List<string>
                {
                    $"<color=#9A8866>{STATUS_MARKER_START}</color>"
                };

                int visibleCount = 0;
                foreach (var kvp in QuestStatuses)
                {
                    var profileName = kvp.Key;
                    
                    if (!Settings.IsProfileVisible(profileName))
                    {
                        continue;
                    }
                    
                    var quests = kvp.Value;
                    EQuestStatus status = EQuestStatus.Locked;
                    string lockedReason = null;
                    
                    if (quests.TryGetValue(questId, out var statusInfo))
                    {
                        status = statusInfo.Status;
                        lockedReason = statusInfo.LockedReason;
                    }
                    
                    var statusName = GetStatusName(status);
                    var statusColor = GetStatusColor(status);
                    
                    string statusDisplay;
                    if (status == EQuestStatus.Locked && !string.IsNullOrEmpty(lockedReason))
                    {
                        bool looksLikeQuestId = Regex.IsMatch(lockedReason, @"^[a-f0-9]{24}$");
                        if (looksLikeQuestId)
                        {
                            LogSource.LogWarning($"[LunaStatusQuestsClient] WARNING: Locked reason for {questId} is a quest ID, not a name. Prerequisite quest may not exist in database: {lockedReason}");
                            statusDisplay = $"<color={statusColor}>{statusName}</color> <color=#FF0000>(Quest not found: {lockedReason.Substring(0, 12)})</color>";
                        }
                        else
                        {
                            statusDisplay = $"<color={statusColor}>{statusName}</color> <color=#666666>({lockedReason})</color>";
                        }
                    }
                    else
                    {
                        statusDisplay = $"<color={statusColor}>{statusName}</color>";
                    }
                    
                    lines.Add($"<color=#CCCCCC>{profileName}:</color> {statusDisplay}");
                    visibleCount++;
                }

                if (visibleCount == 0)
                {
                    lines.Add("<color=#888888>No visible profiles</color>");
                }

                lines.Add($"<color=#9A8866>{STATUS_MARKER_END}</color>");
                return string.Join("\n", lines);
            }
        }

        /// <summary>
        /// Removes the status section from the provided text.
        /// </summary>
        public static string RemoveStatusSection(string text)
        {
            if (string.IsNullOrEmpty(text)) 
                return text;
            
            return StatusSectionPattern.Replace(text, "").TrimStart();
        }

        /// <summary>
        /// Injects status text into the original quest description.
        /// </summary>
        public static string InjectStatusText(string originalText, string questId)
        {
            if (string.IsNullOrEmpty(questId)) 
                return originalText;
            
            var cleanText = RemoveStatusSection(originalText);
            var statusText = BuildStatusText(questId);
            
            if (string.IsNullOrEmpty(statusText))
                return cleanText;
            
            return statusText + "\n\n" + cleanText;
        }

        /// <summary>
        /// Starts the monitor coroutine for updating quest descriptions.
        /// </summary>
        public static void StartMonitor()
        {
            if (Instance == null) 
                return;
            
            StopMonitor();
            _monitorCoroutine = Instance.StartCoroutine(MonitorDescriptionLabel());
            LogSource.LogInfo("[LunaStatusQuestsClient] Monitor started");
        }

        /// <summary>
        /// Stops the monitor coroutine.
        /// </summary>
        public static void StopMonitor()
        {
            if (_monitorCoroutine != null && Instance != null)
            {
                Instance.StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }
        }

        /// <summary>
        /// Starts the trader quest view monitor coroutine.
        /// </summary>
        public static void StartTraderMonitor()
        {
            if (Instance == null)
                return;

            StopTraderMonitor();
            _traderMonitorCoroutine = Instance.StartCoroutine(MonitorTraderDescriptionLabel());
            LogSource.LogInfo("[LunaStatusQuestsClient] Trader monitor started");
        }

        /// <summary>
        /// Stops the trader quest view monitor coroutine.
        /// </summary>
        public static void StopTraderMonitor()
        {
            if (_traderMonitorCoroutine != null && Instance != null)
            {
                Instance.StopCoroutine(_traderMonitorCoroutine);
                _traderMonitorCoroutine = null;
            }
        }

        /// <summary>
        /// Monitors the description label and injects status text when needed.
        /// </summary>
        public static IEnumerator MonitorDescriptionLabel()
        {
            string lastCleanText = "";
            DateTime lastInjectedFetchTime = DateTime.MinValue;
            TextMeshProUGUI descriptionLabel = null;
            
            while (true)
            {
                TasksScreen currentScreen;
                lock (TasksScreenLock)
                {
                    currentScreen = CurrentTasksScreen;
                }
                
                if (currentScreen == null)
                    break;
                
                // Only fetch if the configured interval has elapsed to avoid UI hitches.
                var ageSeconds = (DateTime.UtcNow - LastFetch).TotalSeconds;
                if (ageSeconds >= Settings.UpdateIntervalSeconds.Value)
                {
                    FetchQuestStatuses(force: false);
                }
                
                if (descriptionLabel == null || !descriptionLabel)
                {
                    try
                    {
                        var tmpComponents = currentScreen.GetComponentsInChildren<TextMeshProUGUI>(true);
                        foreach (var tmp in tmpComponents)
                        {
                            if (string.Equals(tmp.name, "DescriptionLabel", StringComparison.Ordinal) && 
                                tmp.text?.Length > MIN_DESCRIPTION_LENGTH)
                            {
                                descriptionLabel = tmp;
                                descriptionLabel.richText = true;
                                LogSource.LogDebug("[LunaStatusQuestsClient] Found DescriptionLabel");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSource.LogDebug($"[LunaStatusQuestsClient] Exception searching for DescriptionLabel: {ex.Message}");
                    }
                }
                
                if (descriptionLabel != null && descriptionLabel.text != null)
                {
                    try
                    {
                        string currentCleanText = RemoveStatusSection(descriptionLabel.text);
                        bool descriptionChanged = currentCleanText != lastCleanText;
                        bool needsMarker = !descriptionLabel.text.Contains(STATUS_MARKER_START);
                        bool dataIsNew = LastFetch > lastInjectedFetchTime;

                        if (needsMarker || dataIsNew || descriptionChanged)
                        {
                            string questId;
                            lock (QuestIdLock)
                            {
                                questId = CurrentQuestId;
                            }
                            
                            if (!string.IsNullOrEmpty(questId))
                            {
                                var newText = InjectStatusText(descriptionLabel.text, questId);
                                
                                if (descriptionLabel.text != newText)
                                {
                                    descriptionLabel.text = newText;
                                    descriptionLabel.ForceMeshUpdate();
                                    
                                    lastCleanText = RemoveStatusSection(newText);
                                    lastInjectedFetchTime = LastFetch;
                                    
                                    if (Settings.ShowDebugLogs.Value)
                                    {
                                        LogSource.LogDebug($"[LunaStatusQuestsClient] Injected status for {questId} (Reason: {(descriptionChanged ? "Desc" : needsMarker ? "Marker" : "New Data")})");
                                    }
                                }
                            }
                            else
                            {
                                descriptionLabel.text = RemoveStatusSection(descriptionLabel.text);
                                lastCleanText = descriptionLabel.text;
                                lastInjectedFetchTime = DateTime.MinValue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSource.LogError($"[LunaStatusQuestsClient] Error updating description label: {ex.Message}");
                    }
                }
                
                yield return new WaitForSeconds(MONITOR_UPDATE_INTERVAL);
            }
            
            LogSource.LogInfo("[LunaStatusQuestsClient] Monitor stopped");
        }

        /// <summary>
        /// Monitors the trader quest description label and injects status text when needed.
        /// Lightweight: reuses the captured label and throttles fetches.
        /// </summary>
        public static IEnumerator MonitorTraderDescriptionLabel()
        {
            string lastCleanText = "";
            DateTime lastInjectedFetchTime = DateTime.MinValue;
            QuestView questView = null;
            TextMeshProUGUI descriptionLabel = null;

            while (true)
            {
                if (!Settings.Enabled.Value || !Settings.ShowInTrader.Value)
                    break;

                lock (TraderViewLock)
                {
                    questView = CurrentTraderQuestView;
                    descriptionLabel = CurrentTraderDescriptionLabel;
                }

                if (questView == null || descriptionLabel == null || !descriptionLabel)
                    break;

                FetchQuestStatuses(force: false);

                try
                {
                    string currentCleanText = RemoveStatusSection(descriptionLabel.text);
                    bool descriptionChanged = currentCleanText != lastCleanText;
                    bool needsMarker = !descriptionLabel.text.Contains(STATUS_MARKER_START);
                    bool dataIsNew = LastFetch > lastInjectedFetchTime;

                    if (needsMarker || dataIsNew || descriptionChanged)
                    {
                        string questId;
                        lock (QuestIdLock)
                        {
                            questId = CurrentQuestId;
                        }

                        if (!string.IsNullOrEmpty(questId))
                        {
                            var newText = InjectStatusText(descriptionLabel.text, questId);
                            if (descriptionLabel.text != newText)
                            {
                                descriptionLabel.text = newText;
                                descriptionLabel.ForceMeshUpdate();

                                lastCleanText = RemoveStatusSection(newText);
                                lastInjectedFetchTime = LastFetch;

                                LogSource.LogDebug($"[LunaStatusQuestsClient] Trader injected status for {questId} (Reason: {(descriptionChanged ? "Desc" : needsMarker ? "Marker" : "New Data")})");
                            }
                        }
                        else
                        {
                            descriptionLabel.text = RemoveStatusSection(descriptionLabel.text);
                            lastCleanText = descriptionLabel.text;
                            lastInjectedFetchTime = DateTime.MinValue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogSource.LogError($"[LunaStatusQuestsClient] Error updating trader description label: {ex.Message}");
                }

                yield return new WaitForSeconds(TRADER_MONITOR_UPDATE_INTERVAL);
            }

            lock (TraderViewLock)
            {
                CurrentTraderQuestView = null;
                CurrentTraderDescriptionLabel = null;
            }

            lock (QuestIdLock)
            {
                CurrentQuestId = null;
            }

            LogSource.LogInfo("[LunaStatusQuestsClient] Trader monitor stopped");
        }
    }

    [HarmonyPatch(typeof(TasksScreen), "Show")]
    class TasksScreenShowPatch
    {
        [HarmonyPostfix]
        static void Postfix(TasksScreen __instance)
        {
            try
            {
                Plugin.LogSource.LogInfo("[LunaStatusQuestsClient] TasksScreen opened");
                lock (Plugin.QuestIdLock)
                {
                    Plugin.CurrentTasksScreen = __instance;
                }
                
                Plugin.FetchQuestStatuses(force: true);
                
                var tmpComponents = __instance.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in tmpComponents)
                {
                    tmp.richText = true;
                }
                
                Plugin.StartMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LunaStatusQuestsClient] Error in TasksScreen.Show: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(TasksScreen), "Close")]
    class TasksScreenClosePatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                Plugin.LogSource.LogInfo("[LunaStatusQuestsClient] TasksScreen closed");
                lock (Plugin.QuestIdLock)
                {
                    Plugin.CurrentTasksScreen = null;
                    Plugin.CurrentQuestId = null;
                }
                
                Plugin.StopMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LunaStatusQuestsClient] Error in TasksScreen.Close: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NotesTask), "method_1")]
    class NotesTaskMethod1Patch
    {
        private static readonly FieldInfo questClassField =
#pragma warning disable IL2065
            typeof(NotesTask).GetField("questClass", BindingFlags.Instance | BindingFlags.NonPublic);
#pragma warning restore IL2065

        [HarmonyPostfix]
        static void Postfix(NotesTask __instance)
        {
            try
            {
                if (questClassField == null)
                {
                    Plugin.LogSource.LogError("[LunaStatusQuestsClient] Failed to locate questClass field via reflection. The game version may be incompatible.");
                    return;
                }

                QuestClass quest = (QuestClass)questClassField.GetValue(__instance);
                
                if (quest?.Template?.Id != null)
                {
                    lock (Plugin.QuestIdLock)
                    {
                        if (Plugin.CurrentQuestId != quest.Template.Id)
                        {
                            Plugin.CurrentQuestId = quest.Template.Id;
                            Plugin.LogSource.LogDebug($"[LunaStatusQuestsClient] Quest changed to: {quest.Template.Id}");
                        }
                    }
                }
                else
                {
                    Plugin.LogSource.LogWarning("[LunaStatusQuestsClient] NotesTask.method_1 patch failed to capture Quest ID.");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LunaStatusQuestsClient] Error in NotesTask.method_1 patch: {ex.Message}");
            }
        }
    }
}