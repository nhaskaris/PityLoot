namespace PityLoot.Logic;

// Plain data types so the pity logic can be unit tested without the SPT server assemblies.

public enum RequirementType
{
    Quest,
    QuestKey,
    Gunsmith,
    Hideout,
}

public record ItemRequirement(
    RequirementType Type,
    string ItemId,
    double AmountRequired,
    double SecondsSinceStarted,
    int RaidsSinceStarted,
    string? ConditionId = null,
    bool FoundInRaid = false);

public record InventoryItem(string Tpl, double Count, bool FoundInRaid);

public record QuestConditionDef(string Id, string ConditionType, IReadOnlyList<string> Targets, bool OnlyFoundInRaid, double? Value);

public record QuestDef(string Id, string? Name, IReadOnlyList<QuestConditionDef> AvailableForFinish);

/// <summary>A started quest from the profile. Times are unix seconds.</summary>
public record QuestProgress(string QuestId, double StartTime, double? StartedStatusTimer, IReadOnlyCollection<string> CompletedConditions);

public record StageRequirementDef(
    string? Type,
    int? AreaType,
    int? RequiredLevel,
    string? SkillName,
    int? SkillLevel,
    string? TraderId,
    int? LoyaltyLevel,
    string? TemplateId,
    int? Count);

public record HideoutAreaDef(string Id, int Type, IReadOnlyDictionary<int, IReadOnlyList<StageRequirementDef>> Stages);

public record RequiredItem(string Id, int Count);

public record HideoutUpgradeInfo(int Area, int Level, IReadOnlyList<RequiredItem> RequiredItems);

/// <summary>What the hideout planner needs to know about a profile.</summary>
public record HideoutProfile(
    IReadOnlyDictionary<int, int> AreaLevels,
    IReadOnlyDictionary<string, double> SkillProgress,
    IReadOnlyDictionary<string, int> TraderLoyalty);
