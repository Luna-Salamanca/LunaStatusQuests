import { DependencyContainer } from "tsyringe";
import type { IPreSptLoadMod } from "@spt/models/external/IPreSptLoadMod";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { StaticRouterModService } from "@spt/services/mod/staticRouter/StaticRouterModService";
import { DatabaseServer } from "@spt/servers/DatabaseServer";
import { ProfileHelper } from "@spt/helpers/ProfileHelper";
import { QuestHelper } from "@spt/helpers/QuestHelper";
import { ConfigServer } from "@spt/servers/ConfigServer";
import { ConfigTypes } from "@spt/models/enums/ConfigTypes";
import type { IQuestConfig } from "@spt/models/spt/config/IQuestConfig";
import type { IPmcData } from "@spt/models/eft/common/IPmcData";
import type { IQuest } from "@spt/models/eft/common/tables/IQuest";
import { QuestStatus } from "@spt/models/enums/QuestStatus";

interface QuestStatusInfo 
{
    status: number;
    lockedReason?: string;
    questName?: string;
}

interface QuestStatusResponse 
{
    [playerName: string]: {
        [questId: string]: QuestStatusInfo;
    };
}

interface PrerequisiteInfo
{
    id: string;
    name: string;
}

interface BlockerInfo 
{
    firstBlockerId: string;
    firstBlockerName: string;
    questsBehind: number;
}

/**
 * LunaStatusQuests Backend for SPT 3.11
 * Provides HTTP endpoint to retrieve real-time quest status for all player profiles.
 * Heavily inspired by the SharedQuests mod for SPT 4.0 by amorijs:
 * https://github.com/amorijs/spt-SharedQuests
 */
export class LunaStatusQuestsBackend implements IPreSptLoadMod 
{
    private logger: ILogger;
    private databaseServer: DatabaseServer;
    private profileHelper: ProfileHelper;
    private questHelper: QuestHelper;
    private questConfig: IQuestConfig;
    
    private questPrerequisites: Map<string, PrerequisiteInfo[]> = new Map();
    private prerequisitesCacheBuilt = false;
    private questPrerequisitesBuiltAt = 0;
    
    private lockedReasonCache: Map<string, string | undefined> = new Map();
    
    private static readonly excludedProfilePrefixes = ["headless_", "bot_"];
    private static readonly cacheTtlMs = 5 * 60 * 1000;
    private static readonly maxQuestDepth = 500;

    public async preSptLoad(container: DependencyContainer): Promise<void> 
    {
        this.logger = container.resolve<ILogger>("WinstonLogger");
        this.databaseServer = container.resolve<DatabaseServer>("DatabaseServer");
        this.profileHelper = container.resolve<ProfileHelper>("ProfileHelper");
        this.questHelper = container.resolve<QuestHelper>("QuestHelper");
        const configServer = container.resolve<ConfigServer>("ConfigServer");
        const staticRouterModService = container.resolve<StaticRouterModService>(
            "StaticRouterModService"
        );

        this.questConfig = configServer.getConfig(ConfigTypes.QUEST);

        staticRouterModService.registerStaticRouter(
            "LunaStatusQuests",
            [
                {
                    url: "/LunaStatusQuests/statuses",
                    action: async (): Promise<string> => 
                    {
                        return this.handleGetQuestStatuses();
                    }
                }
            ],
            "shared-quests"
        );

        this.logger.info("[LunaStatusQuestsServer] Backend module loaded successfully");
    }

    /**
     * Checks if a profile ID should be excluded from processing.
     */
    private isValidProfile(profileId: string): boolean
    {
        return !LunaStatusQuestsBackend.excludedProfilePrefixes.some(prefix => 
            profileId.startsWith(prefix)
        );
    }

    /**
     * Checks if the prerequisite cache needs to be rebuilt.
     */
    private isCacheStale(): boolean
    {
        return !this.prerequisitesCacheBuilt || 
               (Date.now() - this.questPrerequisitesBuiltAt > LunaStatusQuestsBackend.cacheTtlMs);
    }

    private buildPrerequisiteCache(): boolean 
    {
        try 
        {
            if (!this.questHelper) 
            {
                this.logger.warning("[LunaStatusQuestsServer] questHelper not initialized");
                return false;
            }

            const allQuests: IQuest[] = this.questHelper.getQuestsFromDb();
            if (!allQuests || allQuests.length === 0) 
            {
                this.logger.warning("[LunaStatusQuestsServer] No quests found in database");
                this.prerequisitesCacheBuilt = true;
                this.questPrerequisitesBuiltAt = Date.now();
                return true;
            }
            
            const allProfiles = this.profileHelper.getProfiles();
            const firstProfileId = Object.keys(allProfiles).find(id => this.isValidProfile(id));
            const samplePmcData: IPmcData | null = firstProfileId ? allProfiles[firstProfileId].characters?.pmc : null;
            
            if (!samplePmcData)
            {
                this.logger.warning("[LunaStatusQuestsServer] Could not find sample profile. Building cache without quest name resolution.");
            }

            let questsWithPrereqs = 0;

            for (const quest of allQuests) 
            {
                const prerequisites: PrerequisiteInfo[] = []; 
                
                if (quest?.conditions?.AvailableForStart && Array.isArray(quest.conditions.AvailableForStart)) 
                {
                    for (const condition of quest.conditions.AvailableForStart) 
                    {
                        if (condition?.conditionType === "Quest" && condition?.target) 
                        {
                            const targetQuestIds = this.extractTargetStrings(condition.target);

                            for (const targetQuestId of targetQuestIds) 
                            {
                                const resolvedName = samplePmcData 
                                    ? this.getQuestName(targetQuestId, samplePmcData) ?? targetQuestId
                                    : targetQuestId;
                                
                                const prereqInfo: PrerequisiteInfo = { 
                                    id: targetQuestId, 
                                    name: resolvedName
                                };
                                
                                prerequisites.push(prereqInfo);
                            }
                        }
                    }
                }

                if (prerequisites.length > 0) 
                {
                    this.questPrerequisites.set(quest._id, prerequisites);
                    questsWithPrereqs++;
                }
            }

            this.logger.info(`[LunaStatusQuestsServer] Built prerequisite cache for ${questsWithPrereqs} quests`);
            this.prerequisitesCacheBuilt = true;
            this.questPrerequisitesBuiltAt = Date.now();
            return true;
        }
        catch (error) 
        {
            this.logger.error(`[LunaStatusQuestsServer] Error building prerequisite cache: ${error}`);
            this.prerequisitesCacheBuilt = false;
            return false;
        }
    }

    private extractTargetStrings(target: unknown): string[] 
    {
        const results: string[] = [];

        if (!target) return results;

        if (typeof target === "string") 
        {
            return [target];
        }

        if (Array.isArray(target)) 
        {
            return target.filter((item): item is string => typeof item === "string");
        }

        return results;
    }

    private async handleGetQuestStatuses(): Promise<string> 
    {
        try 
        {
            if (this.isCacheStale()) 
            {
                this.buildPrerequisiteCache();
            }

            this.lockedReasonCache.clear();

            const statuses = this.getQuestStatuses();
            return JSON.stringify(statuses);
        }
        catch (error) 
        {
            this.logger.error(`[LunaStatusQuestsServer] Error in handleGetQuestStatuses: ${error}`);
            return JSON.stringify({});
        }
    }

    private getQuestStatuses(): QuestStatusResponse 
    {
        const result: QuestStatusResponse = {};

        try 
        {
            const allProfiles = this.profileHelper.getProfiles();
            const allQuests: IQuest[] = this.questHelper.getQuestsFromDb();
            
            if (Object.keys(allProfiles).length === 0)
            {
                this.logger.info("[LunaStatusQuestsServer] No profiles available");
                return result;
            }

            if (allQuests.length === 0)
            {
                this.logger.info("[LunaStatusQuestsServer] No quests available");
                return result;
            }
            
            this.logger.info(`[LunaStatusQuestsServer] Found ${Object.keys(allProfiles).length} profiles`);

            for (const profileId in allProfiles) 
            {
                if (!this.isValidProfile(profileId))
                {
                    continue;
                }

                const profile = allProfiles[profileId].characters?.pmc;
                
                if (!profile) 
                {
                    continue;
                }

                const playerName = profile.Info?.Nickname;

                if (!playerName) 
                {
                    this.logger.debug(`[LunaStatusQuestsServer] Profile ${profileId} has no nickname`);
                    continue;
                }

                this.logger.debug(`[LunaStatusQuestsServer] Loading profile for player: ${playerName}`);

                const questStatuses: { [questId: string]: QuestStatusInfo } = {};
                
                for (const quest of allQuests) 
                {
                    try
                    {
                        const questStatus = this.questHelper.getQuestStatus(profile, quest._id);
                        
                        if (!this.isValidQuestStatus(questStatus))
                        {
                            this.logger.debug(`[LunaStatusQuestsServer] Invalid quest status ${questStatus} for quest ${quest._id}`);
                        }

                        const questName = this.getQuestName(quest._id, profile);
                        
                        const lockedReason = (questStatus === QuestStatus.Locked) 
                            ? this.getLockedReason(quest._id, profile) 
                            : undefined;
                        
                        questStatuses[quest._id] = {
                            status: questStatus,
                            lockedReason: lockedReason, 
                            questName: questName ?? quest._id
                        };
                    }
                    catch (questError)
                    {
                        this.logger.debug(`[LunaStatusQuestsServer] Failed to get status for quest ${quest._id}: ${questError}`);
                        questStatuses[quest._id] = {
                            status: QuestStatus.Locked,
                            questName: quest._id
                        };
                    }
                }

                result[playerName] = questStatuses;
            }

            this.logger.debug(`[LunaStatusQuestsServer] Processed ${Object.keys(result).length} profiles`);
        }
        catch (error) 
        {
            this.logger.error(`[LunaStatusQuestsServer] Error reading profiles: ${error}`);
        }

        return result;
    }

    /**
     * Validates that a quest status value is a valid QuestStatus enum value.
     */
    private isValidQuestStatus(status: number): boolean
    {
        return Object.values(QuestStatus).includes(status);
    }

    /**
     * Traverses the quest chain to find the oldest uncompleted prerequisite.
     * Includes cycle detection and maximum depth limits to prevent infinite recursion.
     */
    private findFirstBlockerAndCount(
        targetQuestId: string, 
        pmcData: IPmcData, 
        currentDepth: number = 1,
        visitedQuests: Set<string> = new Set()
    ): BlockerInfo | null
    {
        if (currentDepth > LunaStatusQuestsBackend.maxQuestDepth)
        {
            this.logger.warning(`[LunaStatusQuestsServer] Max quest depth (${LunaStatusQuestsBackend.maxQuestDepth}) exceeded for quest ${targetQuestId.substring(0, 12)}`);
            return null;
        }

        if (visitedQuests.has(targetQuestId)) 
        {
            this.logger.warning(`[LunaStatusQuestsServer] Circular dependency detected for quest ${targetQuestId.substring(0, 12)}`);
            return null;
        }
        
        visitedQuests.add(targetQuestId);

        const prerequisites = this.questPrerequisites.get(targetQuestId);

        if (!prerequisites || prerequisites.length === 0) 
        {
            const blockerName = this.getQuestName(targetQuestId, pmcData) ?? targetQuestId;
            return {
                firstBlockerId: targetQuestId,
                firstBlockerName: blockerName,
                questsBehind: currentDepth
            };
        }

        let firstBlocker: BlockerInfo | null = null;

        for (const prereq of prerequisites) 
        {
            const status = this.questHelper.getQuestStatus(pmcData, prereq.id);

            if (status !== QuestStatus.Success) 
            {
                const branchVisited = new Set(visitedQuests);
                
                const chainBlocker = this.findFirstBlockerAndCount(
                    prereq.id, 
                    pmcData, 
                    currentDepth + 1,
                    branchVisited
                );
                
                if (chainBlocker)
                {
                    if (!firstBlocker || chainBlocker.questsBehind > firstBlocker.questsBehind) 
                    {
                        firstBlocker = chainBlocker;
                    }
                }
            }
        }

        return firstBlocker ?? null;
    }

    /**
     * Determines the reason a quest is locked.
     * Uses memoization to avoid redundant calculations.
     * If locked by quest, finds the earliest blocking quest and chain depth.
     */
    private getLockedReason(questId: string, pmcData: IPmcData): string | undefined 
    {
        const cacheKey = `${pmcData._id}_${questId}`;
        
        if (this.lockedReasonCache.has(cacheKey)) 
        {
            return this.lockedReasonCache.get(cacheKey);
        }

        const immediatePrerequisites = this.questPrerequisites.get(questId);
        
        if (!immediatePrerequisites || immediatePrerequisites.length === 0) 
        {
            this.lockedReasonCache.set(cacheKey, undefined);
            return undefined;
        }

        let firstBlockerInfo: BlockerInfo | null = null;
        
        for (const immediatePrereq of immediatePrerequisites)
        {
            const status = this.questHelper.getQuestStatus(pmcData, immediatePrereq.id);
            
            if (status !== QuestStatus.Success)
            {
                const chainBlocker = this.findFirstBlockerAndCount(
                    immediatePrereq.id, 
                    pmcData, 
                    0,
                    new Set()
                );

                if (chainBlocker)
                {
                    if (!firstBlockerInfo || chainBlocker.questsBehind > firstBlockerInfo.questsBehind)
                    {
                        firstBlockerInfo = chainBlocker;
                    }
                }
            }
        }
        
        let result: string | undefined;
        
        if (firstBlockerInfo) 
        {
            const countDisplay = firstBlockerInfo.questsBehind > 1 
                ? ` (${firstBlockerInfo.questsBehind} Quests Behind)` 
                : "";
            
            result = `${firstBlockerInfo.firstBlockerName}${countDisplay}`;
            this.logger.debug(`[LunaStatusQuestsServer] Locked reason for ${questId.substring(0, 12)}: ${result}`);
        }
        else
        {
            const prerequisiteNames = immediatePrerequisites.map(prereq => prereq.name);
            result = prerequisiteNames.join(", ");
        }
        
        this.lockedReasonCache.set(cacheKey, result);
        
        return result;
    }

    /**
     * Retrieves the localized name of a quest.
     * Falls back to the internal QuestName if localization fails.
     */
    private getQuestName(questId: string, pmcData: IPmcData): string | undefined
    {
        try
        {
            const localizedName = this.questHelper.getQuestNameFromLocale(questId);
            
            const invalidNames = [questId, `${questId} Name`, "name", "Name", ""];
            if (localizedName && !invalidNames.includes(localizedName) && localizedName.length > 0)
            {
                return localizedName;
            }
            
            const quest = this.questHelper.getQuestFromDb(questId, pmcData);
            if (quest?.QuestName && quest.QuestName !== "name" && quest.QuestName !== "Name")
            {
                return quest.QuestName;
            }
            
            return undefined;
        }
        catch (error)
        {
            this.logger.error(`[LunaStatusQuestsServer] Error getting quest name for ${questId}: ${error}`);
            return undefined;
        }
    }
}

module.exports = { mod: new LunaStatusQuestsBackend() };