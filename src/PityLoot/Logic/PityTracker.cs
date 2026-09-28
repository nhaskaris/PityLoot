using System.Text.Json;
using System.Text.Json.Serialization;

namespace PityLoot.Logic;

// Same on-disk shape as the TypeScript mod's database/pityTracker.json, so existing trackers keep working.

public class HideoutTrackerEntry
{
    [JsonPropertyName("currentLevel")] public int CurrentLevel { get; set; }
    [JsonPropertyName("timeAvailable")] public long TimeAvailable { get; set; }
    [JsonPropertyName("raidsSinceStarted")] public int RaidsSinceStarted { get; set; }
}

public class QuestTrackerEntry
{
    [JsonPropertyName("raidsSinceStarted")] public int RaidsSinceStarted { get; set; }
}

public class UserPityTracker
{
    /// <summary>Keyed by hideout area type (int, stored as string in JSON).</summary>
    [JsonPropertyName("hideout")] public Dictionary<string, HideoutTrackerEntry> Hideout { get; set; } = new();

    /// <summary>Keyed by quest id.</summary>
    [JsonPropertyName("quests")] public Dictionary<string, QuestTrackerEntry> Quests { get; set; } = new();
}

public static class PityTrackerLogic
{
    /// <summary>
    /// Port of updatePityTracker: keep only started quests and currently possible hideout upgrades,
    /// incrementing raid counts when requested, and resetting an area when its next level went up.
    /// </summary>
    public static UserPityTracker Update(
        UserPityTracker old,
        IEnumerable<string> startedQuestIds,
        IEnumerable<HideoutUpgradeInfo> possibleUpgrades,
        bool incrementRaidCount,
        long nowMs)
    {
        var increase = incrementRaidCount ? 1 : 0;
        var result = new UserPityTracker();

        foreach (var qid in startedQuestIds)
        {
            old.Quests.TryGetValue(qid, out var oldStatus);
            result.Quests[qid] = new QuestTrackerEntry { RaidsSinceStarted = (oldStatus?.RaidsSinceStarted ?? 0) + increase };
        }

        foreach (var upgrade in possibleUpgrades)
        {
            var key = upgrade.Area.ToString();
            var oldStatus = old.Hideout.GetValueOrDefault(key)
                            ?? new HideoutTrackerEntry { CurrentLevel = 0, RaidsSinceStarted = 0, TimeAvailable = nowMs };

            // If the next upgrade is higher than what we tracked, the area was upgraded: reset it
            result.Hideout[key] = upgrade.Level > oldStatus.CurrentLevel
                ? new HideoutTrackerEntry { CurrentLevel = upgrade.Level, RaidsSinceStarted = increase, TimeAvailable = nowMs }
                : new HideoutTrackerEntry
                {
                    CurrentLevel = oldStatus.CurrentLevel,
                    TimeAvailable = oldStatus.TimeAvailable,
                    RaidsSinceStarted = oldStatus.RaidsSinceStarted + increase,
                };
        }

        return result;
    }
}

/// <summary>JSON persistence for all profiles' trackers, keyed by profile id.</summary>
public class PityTrackerStore(string filePath)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly Lock _lock = new();

    public string FilePath { get; } = filePath;

    public UserPityTracker Load(string profileId)
    {
        lock (_lock)
        {
            return LoadAll().GetValueOrDefault(profileId) ?? new UserPityTracker();
        }
    }

    public void Save(string profileId, UserPityTracker tracker)
    {
        lock (_lock)
        {
            var all = LoadAll();
            all[profileId] = tracker;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, WriteOptions));
        }
    }

    private Dictionary<string, UserPityTracker> LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, UserPityTracker>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, UserPityTracker>>(File.ReadAllText(FilePath))
               ?? new Dictionary<string, UserPityTracker>();
    }
}
