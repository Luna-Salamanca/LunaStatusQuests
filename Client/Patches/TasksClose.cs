using System;
using EFT.UI;
using HarmonyLib;
using LunaStatusQuests.Services;

namespace LunaStatusQuests.Patches
{
    /// <summary>
    /// Hooks into the 'TasksScreen.Close' method.
    /// Triggered when the user leaves the tasks screen.
    /// Handles cleanup to avoid memory leaks or stale UI references.
    /// </summary>
    [HarmonyPatch(typeof(TasksScreen), "Close")]
    public class TasksClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var questService = ServiceContainer.Resolve<IQuestService>();
                Plugin.LogSource.LogInfo(
                    "[LunaStatusQuestsClient] TasksScreen closed - Cleaning up UI references"
                );

                // Clear the cached UI references now that the screen is no longer active.
                ServiceProvider.CurrentTasksScreen = null;
                questService.CurrentQuestId = null;

                // Stop the plugin's monitoring loop to save performance.
                Plugin.Instance.StopMonitor();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(
                    $"[LunaStatusQuestsClient] Error in TasksScreen.Close patch: {ex.Message}"
                );
            }
        }
    }
}
