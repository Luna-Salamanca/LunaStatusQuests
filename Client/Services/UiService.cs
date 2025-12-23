using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using LunaStatusQuests;

namespace LunaStatusQuests.Services
{
    /// <summary>
    /// Service responsible for UI-related logic, specifically formatting and injecting quest status text.
    /// Handles rich text formatting, status name mapping, and color coding.
    /// </summary>
    public interface IUiService
    {
        /// <summary>
        /// Generates a formatted rich-text block containing quest statuses for all visible profiles.
        /// </summary>
        /// <param name="questId">The ID of the quest to build status for.</param>
        /// <returns>A multi-line rich-text string.</returns>
        string BuildStatusText(string questId);

        /// <summary>
        /// Injects the Luna status block into an existing string (usually a quest description).
        /// Prepends the status block to the original text.
        /// </summary>
        /// <param name="originalText">The base description text.</param>
        /// <param name="questId">The quest ID for status lookup.</param>
        /// <returns>The combined text block.</returns>
        string InjectStatusText(string originalText, string questId);

        /// <summary>
        /// Removes any previously injected Luna status blocks from a string.
        /// Uses regex to identify and strip the markers.
        /// </summary>
        string RemoveStatusSection(string text);

        string GetStatusName(EQuestStatus status);
        string GetStatusColor(EQuestStatus status);
    }

    /// <summary>
    /// Concrete implementation of IUiService.
    /// Uses QuestService for data and SettingsService for visibility rules.
    /// </summary>
    public class UiService : IUiService
    {
        private readonly IQuestService _questService;
        private readonly ISettingsService _settingsService;
        private readonly ManualLogSource _logger;

        // Markers used to identify the injected status section in UI text.
        private const string STATUS_MARKER_START = "--- Luna Quest Status ---";
        private const string STATUS_MARKER_END = "--------------------------";

        // Regex to find and remove our status block, including legacy "Shared" markers.
        private static readonly Regex StatusSectionPattern = new Regex(
            @"<color=#\w+>---\s*(Shared|Luna) Quest Status\s*---<\/color>[\s\S]*?<color=#\w+>-{20,}<\/color>\s*",
            RegexOptions.Compiled
        );

        public UiService(
            IQuestService questService,
            ISettingsService settingsService,
            ManualLogSource logger
        )
        {
            _questService = questService;
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Loops through all cached profile data and builds a single string block of their statuses for a specific quest.
        /// </summary>
        public string BuildStatusText(string questId)
        {
            if (!_settingsService.Enabled)
                return "";

            // Capture the current statuses reference to avoid issues if a fetch completes mid-loop.
            var snapshotStatuses = _questService.QuestStatuses;

            if (
                snapshotStatuses == null
                || snapshotStatuses.Count == 0
                || string.IsNullOrEmpty(questId)
            )
            {
                return $"<color=#9A8866>{STATUS_MARKER_START}</color>\n<color=#888888>Loading...</color>\n<color=#9A8866>{STATUS_MARKER_END}</color>";
            }

            var lines = new List<string> { $"<color=#9A8866>{STATUS_MARKER_START}</color>" };

            int visibleCount = 0;
            foreach (var kvp in snapshotStatuses)
            {
                var profileName = kvp.Key;

                // Skip profiles that the user has filtered out in settings (F12).
                if (!_settingsService.IsProfileVisible(profileName))
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
                // Special handling for locked quests to show why they are locked (prerequisites).
                if (status == EQuestStatus.Locked)
                {
                    if (!string.IsNullOrEmpty(lockedReason))
                    {
                        // Check if the reason is just a raw quest ID (indicates potential DB mismatch or missing data).
                        bool looksLikeQuestId = Regex.IsMatch(lockedReason, @"^[a-f0-9]{24}$");
                        if (looksLikeQuestId)
                        {
                            statusDisplay =
                                $"<color={statusColor}>{statusName}</color> <color=#FF0000>(Quest not found: {lockedReason.Substring(0, 12)})</color>";
                        }
                        else
                        {
                            statusDisplay =
                                $"<color={statusColor}>{statusName}</color> <color=#666666>({lockedReason})</color>";
                        }
                    }
                    else
                    {
                        // Quest is locked but has no quest-based reason (likely level, trader rep, or other conditions)
                        statusDisplay =
                            $"<color={statusColor}>{statusName}</color> <color=#888888>(Other conditions)</color>";
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

        /// <summary>
        /// Cleans and prepends the status block to the text.
        /// This is used in Harmony patches to modify descriptions.
        /// </summary>
        public string InjectStatusText(string originalText, string questId)
        {
            if (string.IsNullOrEmpty(questId))
                return originalText;

            // Remove any old Luna blocks to avoid duplication if the patch runs multiple times.
            var cleanText = RemoveStatusSection(originalText);

            var statusText = BuildStatusText(questId);

            if (string.IsNullOrEmpty(statusText))
                return cleanText;

            return statusText + "\n\n" + cleanText;
        }

        /// <summary>
        /// Uses regex to find and remove our signature status block markers from UI text.
        /// </summary>
        public string RemoveStatusSection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return StatusSectionPattern.Replace(text, "").TrimStart();
        }

        // --- Localization/Mapping Mappers ---
        // These map game enum values to human-readable strings and UI colors.

        public string GetStatusName(EQuestStatus status)
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
                _ => "Unknown",
            };
        }

        public string GetStatusColor(EQuestStatus status)
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
                _ => "#FFFFFF",
            };
        }
    }
}
