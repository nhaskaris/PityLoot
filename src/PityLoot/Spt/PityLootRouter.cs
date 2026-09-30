using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace PityLoot.Spt;

/// <summary>Port of the static router hooks in mod.ts. Hooks run after SPT's own handler and pass its output through.</summary>
[Injectable(InjectionType.Transient)]
public class PityLootRouter(JsonUtil jsonUtil, PityLootService service) : StaticRouter(jsonUtil, Routes(service))
{
    private static List<RouteAction> Routes(PityLootService service) =>
    [
        Route<EmptyRequestData>("/client/game/start", (_, sessionId) =>
            Run(service, () => service.HandlePityChange(sessionId, false))),
        Route<EndLocalRaidRequestData>("/client/match/local/end", (info, sessionId) =>
            Run(service, () => service.HandlePityChange(sessionId, ShouldCountRaid(service, info)))),
        Route<ItemEventRouterRequest>("/client/game/profile/items/moving", (info, sessionId) =>
        {
            if (HasPityRelevantAction(info))
            {
                Run(service, () => service.HandlePityChange(sessionId, false));
            }
        }),
    ];

#if SPT40
    private static RouteAction Route<T>(string url, Action<T, MongoId> handler) where T : IRequestData =>
        new(url, (_, info, sessionId, output) =>
        {
            handler((T)info, sessionId);
            return ValueTask.FromResult<object>(output!);
        }, typeof(T));
#else
    private static RouteAction Route<T>(string url, Action<T, MongoId> handler) where T : class, IRequestData =>
        new RouteAction<T>(url, (_, info, sessionId, output, _) =>
        {
            handler(info, sessionId);
            return ValueTask.FromResult(output!);
        });
#endif

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
#if SPT40
            // 4.0 deserializes the item events into typed request objects
            if (body.Action is not null && PityActions.Contains(body.Action))
#else
            // 4.1 passes the item events through as raw JSON
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("Action", out var action)
                && action.ValueKind == JsonValueKind.String
                && PityActions.Contains(action.GetString()!))
#endif
            {
                return true;
            }
        }

        return false;
    }
}
