import { DependencyContainer } from "tsyringe";
import type { IPreSptLoadMod } from "@spt/models/external/IPreSptLoadMod";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { StaticRouterModService } from "@spt/services/mod/staticRouter/StaticRouterModService";
import { LunaStatusQuestsService } from "./LunaStatusQuestsService";

/**
 * LunaStatusQuests Backend for SPT 3.11
 * Provides HTTP endpoint to retrieve real-time quest status for all player profiles.
 *
 * Refactored to use Dependency Injection pattern.
 */
export class LunaStatusQuestsBackend implements IPreSptLoadMod 
{
    public async preSptLoad(container: DependencyContainer): Promise<void> 
    {
        const logger = container.resolve<ILogger>("WinstonLogger");
        const staticRouterModService = container.resolve<StaticRouterModService>("StaticRouterModService");

        container.register<LunaStatusQuestsService>("LunaStatusQuestsService", { useClass: LunaStatusQuestsService });

        // Resolve service to trigger any initialization logic
        const questStatusService = container.resolve<LunaStatusQuestsService>("LunaStatusQuestsService");

        staticRouterModService.registerStaticRouter(
            "LunaStatusQuests",
            [
                {
                    url: "/LunaStatusQuests/statuses",
                    action: async (): Promise<string> => 
                    {
                        return questStatusService.handleGetQuestStatuses();
                    }
                }
            ],
            "luna-status-quests"
        );

        logger.info("[LunaStatusQuestsServer] Backend module loaded successfully");
    }
}

module.exports = { mod: new LunaStatusQuestsBackend() };
