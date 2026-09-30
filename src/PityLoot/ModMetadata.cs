using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace PityLoot;

#if SPT40
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.eliteonetube.pityloot";
    public override string Name { get; init; } = "PityLoot";
    public override string Author { get; init; } = "EliteOneTube";
    // Fork of Bakahashi's SPT 3.x PityLoot, rewritten for SPT 4.x
    public override List<string>? Contributors { get; init; } = ["Bakahashi (original SPT 3.x mod)"];
    public override Version Version { get; init; } = new(ModVersion.Current);
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/nhaskaris/PityLoot";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
#else
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.eliteonetube.pityloot";
    public string Name { get; init; } = "PityLoot";
    public string Author { get; init; } = "EliteOneTube";
    // Fork of Bakahashi's SPT 3.x PityLoot, rewritten for SPT 4.x
    public List<string>? Contributors { get; init; } = ["Bakahashi (original SPT 3.x mod)"];
    public Version Version { get; init; } = new(ModVersion.Current);
    public Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/nhaskaris/PityLoot";
    public bool HasPrepatcher { get; init; }
    public string License { get; init; } = "MIT";
}
#endif

internal static class ModVersion
{
    /// <summary>The &lt;Version&gt; from the .csproj, e.g. "1.0.0" or "1.0.0-4.0".</summary>
    public static string Current { get; } =
        typeof(ModVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
