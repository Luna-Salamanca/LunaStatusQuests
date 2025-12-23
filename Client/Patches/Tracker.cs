using System;
using System.Reflection;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using LunaStatusQuests.Services;

namespace LunaStatusQuests.Patches
{
    /// <summary>
    /// Hooks into 'NotesTask.method_1' to identify which quest is currently being viewed or hovered in the Tasks UI.
    /// This is crucial because the standard EFT UI doesn't always expose the active quest ID directly to external monitors.
    /// </summary>
    [HarmonyPatch(typeof(NotesTask), "method_1")]
    public class TrackerPatch
    {
        // Reflection is used to access the private 'questClass' field within the NotesTask UI element.
        private static readonly FieldInfo questClassField = typeof(NotesTask).GetField(
            "questClass",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        [HarmonyPostfix]
        public static void Postfix(NotesTask __instance)
        {
            try
            {
                var questService = ServiceContainer.Resolve<IQuestService>();
                if (questClassField == null)
                    return;

                QuestClass quest = (QuestClass)questClassField.GetValue(__instance);

                if (quest?.Template?.Id != null)
                {
                    if (questService.CurrentQuestId != quest.Template.Id)
                    {
                        // Update the shared state so other components (like the descriptions) know which quest we are looking at.
                        questService.CurrentQuestId = quest.Template.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(
                    $"[LunaStatusQuestsClient] Error in NotesTask.method_1 tracker patch: {ex.Message}"
                );
            }
        }
    }
}
