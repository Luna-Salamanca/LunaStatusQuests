using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using LunaStatusQuests;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace LunaStatusQuests.Services
{
    /// <summary>
    /// Service responsible for managing quest status data fetched from the server.
    /// Handles asynchronous fetching, thread-safe caching, and profile management.
    /// </summary>
    public interface IQuestService
    {
        /// <summary>
        /// Nested dictionary of quest statuses: [ProfileName][QuestId] => QuestStatusInfo.
        /// </summary>
        Dictionary<string, Dictionary<string, QuestStatusInfo>> QuestStatuses { get; }

        /// <summary>
        /// ID of the quest currently being hovered or inspected in the UI.
        /// </summary>
        string CurrentQuestId { get; set; }

        /// <summary>
        /// Timestamp of the last successful data fetch from the server.
        /// </summary>
        DateTime LastFetch { get; }

        /// <summary>
        /// Fetches updated quest statuses from the server.
        /// </summary>
        /// <param name="force">If true, bypasses the update interval check in settings.</param>
        /// <returns>True if a fetch was initiated, false if skipped due to interval or logic.</returns>
        bool FetchQuestStatuses(bool force = false);
    }

    /// <summary>
    /// Concrete implementation of IQuestService.
    /// Uses Service Locator to access dependencies.
    /// </summary>
    public class QuestService : IQuestService
    {
        private readonly ISettingsService _settingsService;
        private readonly ManualLogSource _logger;

        public Dictionary<string, Dictionary<string, QuestStatusInfo>> QuestStatuses
        {
            get;
            private set;
        } = new();

        public string CurrentQuestId { get; set; }
        public DateTime LastFetch { get; private set; } = DateTime.MinValue;

        // Locks to ensure thread safety during data updates and fetch state management.
        private readonly object _questStatusesLock = new object();
        private readonly object _fetchLock = new object();
        private bool _fetchInProgress = false;

        public QuestService(ISettingsService settingsService, ManualLogSource logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Initiates an asynchronous fetch from the server.
        /// This is the primary entry point for refreshing quest data.
        /// </summary>
        public bool FetchQuestStatuses(bool force = false)
        {
            if (!_settingsService.Enabled)
                return false;

            var ageSeconds = (DateTime.UtcNow - LastFetch).TotalSeconds;
            if (!force && ageSeconds < _settingsService.UpdateIntervalSeconds)
            {
                return false;
            }

            lock (_fetchLock)
            {
                if (_fetchInProgress)
                {
                    return false;
                }

                _fetchInProgress = true;
            }

            // Perform the network request on a background thread to avoid stuttering the game.
            _ = Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug(
                        "[LunaStatusQuestsClient] Requesting quest data from server..."
                    );
                    // RequestHandler is a global SPT class for HTTP calls.
                    var response = RequestHandler.GetJson("/LunaStatusQuests/statuses");

                    if (!string.IsNullOrEmpty(response))
                    {
                        var data = JsonConvert.DeserializeObject<
                            Dictionary<string, Dictionary<string, QuestStatusInfo>>
                        >(response);
                        if (data != null && data.Count > 0)
                        {
                            lock (_questStatusesLock)
                            {
                                QuestStatuses = data;
                            }

                            LastFetch = DateTime.UtcNow;

                            var firstProfileName = data.Keys.FirstOrDefault();
                            if (
                                _settingsService.ShowDebugLogs
                                && !string.IsNullOrEmpty(firstProfileName)
                            )
                            {
                                var firstProfileQuestCount = data[firstProfileName].Count;
                                _logger.LogDebug(
                                    $"[LunaStatusQuestsClient] Sample data received: Profile '{firstProfileName}' has {firstProfileQuestCount} quests"
                                );
                            }

                            _logger.LogInfo(
                                $"[LunaStatusQuestsClient] Successfully fetched status for {data.Count} profiles."
                            );

                            // Sync the profile list back to settings for UI selection.
                            _settingsService.UpdateProfileList(data.Keys.ToList());
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[LunaStatusQuestsClient] Server returned valid JSON but it contained no profile data."
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[LunaStatusQuestsClient] Server returned an empty response."
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        $"[LunaStatusQuestsClient] Critical error during fetch: {ex.Message}"
                    );
                }
                finally
                {
                    lock (_fetchLock)
                    {
                        _fetchInProgress = false;
                    }
                }
            });

            return true;
        }
    }
}
