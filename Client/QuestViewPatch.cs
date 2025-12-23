using System;
using System.Linq;
using System.Reflection;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LunaStatusQuests
{
    /// <summary>
    /// Injects Luna quest status into the trader quest detail view.
    /// </summary>
    [HarmonyPatch(typeof(QuestView), nameof(QuestView.UpdateView))]
    public static class QuestViewUpdateViewPatch
    {
        // Cache reflection lookups to avoid repeated discovery.
        private static readonly FieldInfo QuestField = AccessTools.Field(typeof(QuestView), "_quest");
        private static readonly FieldInfo DescriptionPanelField = AccessTools.Field(typeof(QuestView), "_descriptionPanel");

        [HarmonyPostfix]
        public static void Postfix(QuestView __instance)
        {
            try
            {
                if (!Settings.Enabled.Value || !Settings.ShowInTrader.Value)
                {
                    return;
                }

                var quest = QuestField?.GetValue(__instance) as QuestClass;
                var questId = quest?.Id;

                if (string.IsNullOrEmpty(questId))
                {
                    return;
                }

                // Track current quest so server fetch/monitor stays in sync.
                lock (Plugin.QuestIdLock)
                {
                    if (Plugin.CurrentQuestId != questId)
                    {
                        Plugin.CurrentQuestId = questId;
                    }
                }

                // Refresh data with normal throttling.
                Plugin.FetchQuestStatuses(force: false);

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

                // Store references for the trader monitor and perform an initial inject.
                lock (Plugin.TraderViewLock)
                {
                    Plugin.CurrentTraderQuestView = __instance;
                    Plugin.CurrentTraderDescriptionLabel = descriptionLabel;
                }

                var originalText = descriptionLabel.text;
                var newText = Plugin.InjectStatusText(originalText, questId);

                if (!string.Equals(originalText, newText, StringComparison.Ordinal))
                {
                    descriptionLabel.text = newText;
                    descriptionLabel.ForceMeshUpdate();
                }

                Plugin.StartTraderMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError($"[LunaStatusQuestsClient] Error injecting trader quest status: {ex.Message}");
            }
        }

        private static TextMeshProUGUI FindDescriptionLabel(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps == null || tmps.Length == 0)
            {
                return null;
            }

            // Prefer a TMP named like Description*, otherwise pick the longest text block.
            var byName = tmps.FirstOrDefault(tmp =>
                !string.IsNullOrEmpty(tmp.name) &&
                tmp.name.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0);

            if (byName != null)
            {
                return byName;
            }

            return tmps
                .Where(tmp => !string.IsNullOrEmpty(tmp.text))
                .OrderByDescending(tmp => tmp.text.Length)
                .FirstOrDefault();
        }
    }
}

