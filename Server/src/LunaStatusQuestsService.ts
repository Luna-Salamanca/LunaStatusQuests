import { inject, injectable } from "tsyringe";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import { ProfileHelper } from "@spt/helpers/ProfileHelper";
import { QuestHelper } from "@spt/helpers/QuestHelper";
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

@injectable()
export class LunaStatusQuestsService 
{
    private questPrerequisites: Map<string, PrerequisiteInfo[]> = new Map();
    private prerequisitesCacheBuilt = false;
    private questPrerequisitesBuiltAt = 0;

    private lockedReasonCache: Map<string, string | undefined> = new Map();

    // Exclude system/bot profiles that don't represent real players
    private static readonly excludedProfilePrefixes = ["headless_", "bot_"];
    // Cache TTL: 5 minutes balances freshness with performance (quest data changes infrequently)
    private static readonly cacheTtlMs = 5 * 60 * 1000;
    // Maximum recursion depth to prevent stack overflow on circular or extremely deep prerequisite chains
    private static readonly maxQuestDepth = 500;

    constructor(
        @inject("WinstonLogger") private logger: ILogger,
        @inject("ProfileHelper") private profileHelper: ProfileHelper,
        @inject("QuestHelper") private questHelper: QuestHelper
    ) 
    {}

    public async handleGetQuestStatuses(): Promise<string> 
    {
        try 
        {
            if (this.isCacheStale()) 
            {
                this.buildPrerequisiteCache();
            }

            // Clear memoization cache each request to ensure fresh calculations per profile
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

            this.logger.debug(`[LunaStatusQuestsServer] Found ${Object.keys(allProfiles).length} profiles`);

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
                            this.logger.debug(
                                `[LunaStatusQuestsServer] Invalid quest status ${questStatus} for quest ${quest._id}`
                            );
                        }

                        const questName = this.getQuestName(quest._id, profile);

                        let lockedReason: string | undefined = undefined;
                        let finalStatus = questStatus;
                        
                        if (questStatus === QuestStatus.Locked) 
                        {
                            lockedReason = this.getLockedReason(quest._id, profile);
                            
                            // If quest is locked but all prerequisites are completed, 
                            // it should actually be Available (the game API may be stale)
                            if (lockedReason === undefined) 
                            {
                                // Check if quest has prerequisites - if it does and they're all completed,
                                // override status to Available since prerequisites are the only quest-based lock
                                const hasPrerequisites = this.questPrerequisites.has(quest._id);
                                
                                if (hasPrerequisites) 
                                {
                                    // Quest has prerequisites - verify all are completed
                                    const prerequisites = this.questPrerequisites.get(quest._id);
                                    if (prerequisites && prerequisites.length > 0) 
                                    {
                                        const allPrereqsCompleted = prerequisites.every((prereq) => 
                                        {
                                            const prereqStatus = this.questHelper.getQuestStatus(profile, prereq.id);
                                            return prereqStatus === QuestStatus.Success;
                                        });
                                        
                                        if (allPrereqsCompleted) 
                                        {
                                            // All prerequisites completed - quest should be Available
                                            // Override the potentially stale Locked status from the API
                                            // Using numeric value 1 which corresponds to QuestStatus.Available
                                            finalStatus = 1; // QuestStatus.Available
                                            lockedReason = undefined;
                                            this.logger.debug(
                                                `[LunaStatusQuestsServer] Quest ${quest._id.substring(0, 12)} overridden to Available (all prerequisites completed)`
                                            );
                                        }
                                        
                                    }
                                }
                                else 
                                {
                                    // No prerequisites - quest is locked by other conditions (level, trader rep, etc.)
                                    // Keep as Locked but don't show a reason since it's not quest-based
                                    this.logger.debug(
                                        `[LunaStatusQuestsServer] Quest ${quest._id.substring(0, 12)} is Locked with no prerequisites. Locked by level/rep/other conditions.`
                                    );
                                }
                            }
                        }
                                
                        questStatuses[quest._id] = {
                            status: finalStatus,
                            lockedReason: lockedReason,
                            questName: questName ?? quest._id
                        };
                    }
                    catch (questError) 
                    {
                        this.logger.debug(
                            `[LunaStatusQuestsServer] Failed to get status for quest ${quest._id}: ${questError}`
                        );
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
     * Checks if a profile ID should be excluded from processing.
     */
    private isValidProfile(profileId: string): boolean 
    {
        return !LunaStatusQuestsService.excludedProfilePrefixes.some((prefix) => profileId.startsWith(prefix));
    }

    /**
     * Checks if the prerequisite cache needs to be rebuilt.
     */
    private isCacheStale(): boolean 
    {
        return (
            !this.prerequisitesCacheBuilt ||
            Date.now() - this.questPrerequisitesBuiltAt > LunaStatusQuestsService.cacheTtlMs
        );
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
            const firstProfileId = Object.keys(allProfiles).find((id) => this.isValidProfile(id));
            const samplePmcData: IPmcData | null = firstProfileId ? allProfiles[firstProfileId].characters?.pmc : null;

            if (!samplePmcData) 
            {
                this.logger.warning(
                    "[LunaStatusQuestsServer] Could not find sample profile. Building cache without quest name resolution."
                );
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
                                    ? (this.getQuestName(targetQuestId, samplePmcData) ?? targetQuestId)
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
        if (currentDepth > LunaStatusQuestsService.maxQuestDepth) 
        {
            this.logger.warning(
                `[LunaStatusQuestsServer] Max quest depth (${LunaStatusQuestsService.maxQuestDepth}) exceeded for quest ${targetQuestId.substring(0, 12)}`
            );
            return null;
        }

        if (visitedQuests.has(targetQuestId)) 
        {
            this.logger.warning(
                `[LunaStatusQuestsServer] Circular dependency detected for quest ${targetQuestId.substring(0, 12)}`
            );
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

                const chainBlocker = this.findFirstBlockerAndCount(prereq.id, pmcData, currentDepth + 1, branchVisited);

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
        let hasIncompletePrerequisites = false;

        for (const immediatePrereq of immediatePrerequisites) 
        {
            const status = this.questHelper.getQuestStatus(pmcData, immediatePrereq.id);

            if (status !== QuestStatus.Success) 
            {
                hasIncompletePrerequisites = true;
                const chainBlocker = this.findFirstBlockerAndCount(immediatePrereq.id, pmcData, 0, new Set());

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

        // Only return a locked reason if there are actually incomplete prerequisites
        if (!hasIncompletePrerequisites) 
        {
            // All prerequisites are completed, so there's no quest-based reason for locking
            this.lockedReasonCache.set(cacheKey, undefined);
            return undefined;
        }

        if (firstBlockerInfo) 
        {
            const countDisplay =
                firstBlockerInfo.questsBehind > 1 ? ` (${firstBlockerInfo.questsBehind} Quests Behind)` : "";

            result = `${firstBlockerInfo.firstBlockerName}${countDisplay}`;
            this.logger.debug(`[LunaStatusQuestsServer] Locked reason for ${questId.substring(0, 12)}: ${result}`);
        }
        else 
        {
            // Fallback: if we have incomplete prerequisites but couldn't find a blocker,
            // list the incomplete prerequisites
            const incompletePrereqs = immediatePrerequisites.filter((prereq) => 
            {
                const status = this.questHelper.getQuestStatus(pmcData, prereq.id);
                return status !== QuestStatus.Success;
            });
            
            if (incompletePrereqs.length > 0) 
            {
                const prerequisiteNames = incompletePrereqs.map((prereq) => prereq.name);
                result = prerequisiteNames.join(", ");
            }
            else 
            {
                // This shouldn't happen, but if it does, return undefined
                result = undefined;
            }
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
