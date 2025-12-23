using System;
using EFT.UI;
using HarmonyLib;
using LunaStatusQuests.Services;
using TMPro;

namespace LunaStatusQuests.Patches
{
    /// <summary>
    /// Hooks into the 'TasksScreen.Show' method.
    /// Triggered when the user opens the inventory "Tasks" tab or uses a keyboard shortcut to view tasks.
    /// </summary>
    [HarmonyPatch(typeof(TasksScreen), "Show")]
    public class TasksOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TasksScreen __instance)
        {
            try
            {
                var questService = ServiceContainer.Resolve<IQuestService>();
                Plugin.LogSource.LogInfo(
                    "[LunaStatusQuestsClient] TasksScreen opened - Initializing UI tracking"
                );

                // Store the current instance reference so we can access UI elements later.
                ServiceProvider.CurrentTasksScreen = __instance;

                // Trigger an immediate fetch to ensure we have the most up-to-date quest statuses.
                questService.FetchQuestStatuses(force: true);

                // Enable rich text on all text components in this screen to support our color-coded labels.
                var tmpComponents = __instance.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in tmpComponents)
                {
                    tmp.richText = true;
                }

                // Signal the main plugin to start monitoring for UI changes (selection/hover).
                Plugin.Instance.StartMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(
                    $"[LunaStatusQuestsClient] Error in TasksScreen.Show patch: {ex.Message}"
                );
            }
        }
    }
}
