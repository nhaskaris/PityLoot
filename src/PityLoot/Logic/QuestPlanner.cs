namespace PityLoot.Logic;

/// <summary>Port of QuestUtils.ts.</summary>
public class QuestPlanner(
    PityLootConfig config,
    IReadOnlyDictionary<string, List<string>> questKeys,
    IReadOnlyDictionary<string, List<string>> gunsmith,
    Action<string>? debug = null)
{
    public const string CollectorQuestId = "5c51aac186f77432ea65c552";

    public List<ItemRequirement> GetInProgressQuestRequirements(
        IEnumerable<QuestProgress> startedQuests,
        IReadOnlyDictionary<string, QuestDef> quests,
        UserPityTracker tracker,
        double nowSeconds)
    {
        return startedQuests
            .Where(q => q.QuestId != CollectorQuestId || !config.ExcludeCollector)
            .SelectMany(q => GetIncompleteConditionsForQuest(
                quests,
                q,
                tracker.Quests.GetValueOrDefault(q.QuestId)?.RaidsSinceStarted ?? 0,
                nowSeconds))
            .ToList();
    }

    public List<ItemRequirement> GetIncompleteConditionsForQuest(
        IReadOnlyDictionary<string, QuestDef> quests,
        QuestProgress status,
        int raidsSinceStarted,
        double nowSeconds)
    {
        if (!quests.TryGetValue(status.QuestId, out var quest))
        {
            return [];
        }

        // startTime can be 0, so fall back to the Started status timer
        var startTime = status.StartTime > 0 ? status.StartTime : status.StartedStatusTimer;
        var secondsSinceStarted = startTime is > 0 ? Math.Round(nowSeconds - startTime.Value) : 0;

        var allQuestTargets = quest.AvailableForFinish.SelectMany(c => c.Targets).ToHashSet();
        var conditions = quest.AvailableForFinish
            .Where(c => c.ConditionType is "HandoverItem" or "LeaveItemAtLocation")
            .Where(c => !status.CompletedConditions.Contains(c.Id))
            .Where(c => c.Targets.Count > 0 && c.Value is > 0)
            .SelectMany(c => c.Targets.Select(itemId => new ItemRequirement(
                RequirementType.Quest,
                itemId,
                c.Value!.Value,
                secondsSinceStarted,
                raidsSinceStarted,
                c.Id,
                c.OnlyFoundInRaid)))
            .ToList();

        if (config.IncludeKeys && questKeys.TryGetValue(quest.Id, out var keys))
        {
            foreach (var key in keys)
            {
                if (allQuestTargets.Contains(key))
                {
                    debug?.Invoke($"skipping quest key {key} because it's already a requirement of the quest {quest.Name ?? quest.Id}");
                    continue;
                }

                conditions.Add(new ItemRequirement(RequirementType.QuestKey, key, 1, secondsSinceStarted, raidsSinceStarted));
            }
        }

        if (config.IncludeGunsmith && gunsmith.TryGetValue(quest.Id, out var parts))
        {
            conditions.AddRange(parts.Select(part =>
                new ItemRequirement(RequirementType.Gunsmith, part, 1, secondsSinceStarted, raidsSinceStarted)));
        }

        return conditions;
    }
}
