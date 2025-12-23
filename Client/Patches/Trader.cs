using System;
using System.Linq;
using System.Reflection;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using LunaStatusQuests.Services;
using TMPro;
using UnityEngine;

namespace LunaStatusQuests.Patches
{
    /// <summary>
    /// Hooks into 'QuestView.UpdateView' to inject Luna quest status text directly into the trader's quest description.
    /// This patch is responsible for the visual integration in the Trader UI.
    /// </summary>
    [HarmonyPatch(typeof(QuestView), nameof(QuestView.UpdateView))]
    public static class TraderPatch
    {
        // Reflection is used to access the private '_quest' and '_descriptionPanel' fields in the QuestView class.
        private static readonly FieldInfo QuestField = AccessTools.Field(
            typeof(QuestView),
            "_quest"
        );
        private static readonly FieldInfo DescriptionPanelField = AccessTools.Field(
            typeof(QuestView),
            "_descriptionPanel"
        );

        [HarmonyPostfix]
        public static void Postfix(QuestView __instance)
        {
            try
            {
                var settings = ServiceContainer.Resolve<ISettingsService>();
                var questService = ServiceContainer.Resolve<IQuestService>();
                var uiService = ServiceContainer.Resolve<IUiService>();

                // Respect the user's mod-wide and trader-specific settings.
                if (!settings.Enabled || !settings.ShowInTrader)
                {
                    return;
                }

                var quest = QuestField?.GetValue(__instance) as QuestClass;
                var questId = quest?.Id;

                if (string.IsNullOrEmpty(questId))
                {
                    return;
                }

                // Sync the current quest ID and ensure we have data.
                questService.CurrentQuestId = questId;
                questService.FetchQuestStatuses(force: false);

                var descriptionPanel = DescriptionPanelField?.GetValue(__instance) as MonoBehaviour;
                if (descriptionPanel == null)
                {
                    return;
                }

                var descriptionLabel = FindDescriptionLabel(descriptionPanel.gameObject);
                if (descriptionLabel == null || descriptionLabel.text == null)
                {
                    return;
                }

                descriptionLabel.richText = true;
                ServiceProvider.CurrentTraderDescriptionLabel = descriptionLabel;

                var originalText = descriptionLabel.text;
                var newText = uiService.InjectStatusText(originalText, questId);

                // Only update the label if the text has actually changed to prevent flickering or performance hits.
                if (!string.Equals(originalText, newText, StringComparison.Ordinal))
                {
                    descriptionLabel.text = newText;
                    descriptionLabel.ForceMeshUpdate();
                }

                // Start the monitor to handle logic that occurs while the UI remains open (e.g. status updates).
                Plugin.Instance.StartTraderMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError(
                    $"[LunaStatusQuestsClient] Error injecting trader quest status: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Heuristic-based search to find the description label in the Unity UI tree.
        /// Searches by object name first, then falls back to the largest text block.
        /// </summary>
        private static TextMeshProUGUI FindDescriptionLabel(GameObject root)
        {
            if (root == null)
                return null;

            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps == null || tmps.Length == 0)
                return null;

            // Strategy 1: Find by object name containing "description".
            var byName = tmps.FirstOrDefault(tmp =>
                !string.IsNullOrEmpty(tmp.name)
                && tmp.name.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
            );

            if (byName != null)
                return byName;

            // Strategy 2: Fallback - pick the largest text block, assuming it's the description.
            return tmps.Where(tmp => !string.IsNullOrEmpty(tmp.text))
                .OrderByDescending(tmp => tmp.text.Length)
                .FirstOrDefault();
        }
    }
}
