using System;
using HarmonyLib;
using LunaStatusQuests.Services;

namespace LunaStatusQuests.Patches
{
    /// <summary>
    /// Hooks into quest status changes to trigger immediate refreshes.
    /// We target 'LocalQuestControllerClass.SetConditionalStatus' because it is called
    /// whenever a quest becomes accepted, completed, or failed.
    /// USES STRING-BASED PATCHING to avoid compile-time dependency on game types.
    /// </summary>
    [HarmonyPatch]
    public static class QuestActionPatch
    {
        [HarmonyTargetMethod]
        public static System.Reflection.MethodBase TargetMethod()
        {
            // Specifically target LocalQuestControllerClass which is the active controller in-game.
            // Using the string name for maximum compatibility with different assembly versions.
            return AccessTools.Method("LocalQuestControllerClass:SetConditionalStatus") 
                ?? AccessTools.Method("EFT.Quests.QuestController:AcceptQuest"); // Fallback for safety
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var settings = ServiceContainer.Resolve<ISettingsService>();
                var questService = ServiceContainer.Resolve<IQuestService>();

                if (settings != null && settings.Enabled && questService != null)
                {
                    Plugin.LogSource?.LogDebug("[LunaStatusQuestsClient] Quest status change detected - Triggering forced status fetch");
                    questService.FetchQuestStatuses(force: true);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError($"[LunaStatusQuestsClient] Error triggering force fetch after quest action: {ex.Message}");
            }
        }
    }
}
