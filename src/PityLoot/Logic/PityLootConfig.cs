using System.Text.Json;

namespace PityLoot.Logic;

/// <summary>Mirrors config/config.json from the TypeScript mod, same keys and defaults.</summary>
public class PityLootConfig
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; }
    public bool Trace { get; set; }
    public bool AppliesToQuests { get; set; } = true;
    public bool IncludeGunsmith { get; set; } = true;
    public bool AppliesToHideout { get; set; } = true;
    public bool IncreasesStack { get; set; } = true;
    public bool IncludeScavRaids { get; set; } = true;
    public bool OnlyIncreaseOnFailedRaids { get; set; } = true;
    public bool IncludeKeys { get; set; } = true;
    public double KeysAdditionalMultiplier { get; set; } = 2.5;
    public double MaxDropRateMultiplier { get; set; } = 10;
    public string DropRateIncreaseType { get; set; } = "raid";
    public double DropRateIncreasePerRaid { get; set; } = 0.25;
    public double DropRateIncreasePerHour { get; set; } = 0.05;
    public bool AppliesToWishlist { get; set; }
    public Dictionary<string, double> WishlistMultipliers { get; set; } = new()
    {
        ["tasks"] = 5.0,
        ["equipment"] = 5.0,
        ["barter"] = 5.0,
        ["hideout"] = 5.0,
        ["other"] = 5.0,
    };
    public bool ExcludeCollector { get; set; }

    public bool IsRaidBased => !string.Equals(DropRateIncreaseType, "time", StringComparison.OrdinalIgnoreCase);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PityLootConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new PityLootConfig();
        }

        return JsonSerializer.Deserialize<PityLootConfig>(File.ReadAllText(path), JsonOptions) ?? new PityLootConfig();
    }

    /// <summary>Loads a questId -> item tpl list file (questKeys.json / gunsmith.json).</summary>
    public static Dictionary<string, List<string>> LoadQuestItemMap(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, List<string>>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path), JsonOptions)
               ?? new Dictionary<string, List<string>>();
    }
}
