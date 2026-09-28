using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace PityLoot;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.eliteonetube.pityloot";
    public string Name { get; init; } = "PityLoot";
    public string Author { get; init; } = "EliteOneTube";
    // Fork of Bakahashi's SPT 3.x PityLoot, rewritten for SPT 4.1
    public List<string>? Contributors { get; init; } = ["Bakahashi (original SPT 3.x mod)"];
    // Set via <Version> in PityLoot.csproj
    public Version Version { get; init; } = new(typeof(ModMetadata).Assembly.GetName().Version!.ToString(3));
    public Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public bool HasPrepatcher { get; init; }
    public string License { get; init; } = "MIT";
}
