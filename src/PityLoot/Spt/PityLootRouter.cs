using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace PityLoot.Spt;

/// <summary>Port of the static router hooks in mod.ts.</summary>
[Injectable(InjectionType.Transient, int.MaxValue)]
public class PityLootRouter(JsonUtil jsonUtil, PityLootService service) : StaticRouter(jsonUtil,
[
    new RouteAction<EmptyRequestData>("/client/game/start", async (_, _, sessionId, output, _) =>
    {
        Run(service, () => service.HandlePityChange(sessionId, false));
        return output!;
    }),
    new RouteAction<EndLocalRaidRequestData>("/client/match/local/end", async (_, info, sessionId, output, _) =>
    {
        Run(service, () => service.HandlePityChange(sessionId, ShouldCountRaid(service, info)));
        return output!;
    }),
    new RouteAction<ItemEventRouterRequest>("/client/game/profile/items/moving", async (_, info, sessionId, output, _) =>
    {
        if (HasPityRelevantAction(info))
        {
            Run(service, () => service.HandlePityChange(sessionId, false));
        }

        return output!;
    }),
])
{
    private static readonly HashSet<string> PityActions =
    [
        "QuestComplete",
        "QuestHandover",
        "HideoutImproveArea",
        "HideoutUpgrade",
        "HideoutUpgradeComplete",
    ];

    private static void Run(PityLootService service, Action action)
    {
        if (!service.Config.Enabled)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            service.Warning($"Failed to update pity tracker: {ex}");
        }
    }

    private static bool ShouldCountRaid(PityLootService service, EndLocalRaidRequestData info)
    {
        var result = info.Results?.Result;
        var failed = result is ExitStatus.KILLED or ExitStatus.MISSINGINACTION;
        var survived = result is ExitStatus.SURVIVED or ExitStatus.RUNNER;
        var isScav = string.Equals(info.Results?.Profile?.Info?.Side, "Savage", StringComparison.OrdinalIgnoreCase);

        var count = (failed || (!service.Config.OnlyIncreaseOnFailedRaids && survived))
                    && (!isScav || service.Config.IncludeScavRaids);
        service.DebugLog($"Raid ended: {result} (scav: {isScav}), counting raid: {count}");
        return count;
    }

    private static bool HasPityRelevantAction(ItemEventRouterRequest info)
    {
        foreach (var body in info.Data ?? [])
        {
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("Action", out var action)
                && action.ValueKind == JsonValueKind.String
                && PityActions.Contains(action.GetString()!))
            {
                return true;
            }
        }

        return false;
    }
}
