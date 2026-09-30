using System.Reflection;
using HarmonyLib;
using PityLoot.Logic;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
#if SPT40
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Services;
#else
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Services.InRaid;
#endif
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Utils.Collections;

namespace PityLoot.Spt;

/// <summary>
/// Holds the boost for the loot generation currently running on this thread.
/// GenerateLocationAndLoot is synchronous, so a thread-static is enough to pass the player's context
/// down to the LocationLootGenerator patches (which don't receive a session id).
/// </summary>
public static class ActiveLootContext
{
    [ThreadStatic] public static PityContext? Current;
    [ThreadStatic] public static int SpawnpointsTouched;
    [ThreadStatic] public static int ContainersTouched;

    /// <summary>(label, container tpl, item tpl) -> number of container instances it was injected into.</summary>
    [ThreadStatic] public static Dictionary<(string Label, string Container, string Item), int>? Injections;

    public static PityLootService? Service;

    public static void Reset(PityContext? context)
    {
        Current = context;
        SpawnpointsTouched = 0;
        ContainersTouched = 0;
        Injections = context is null ? null : new Dictionary<(string, string, string), int>();
    }
}

/// <summary>Sets the player's boost before map loot is generated and clears it afterwards.</summary>
[Injectable(InjectionType.Transient)]
public class GenerateLocationAndLootPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot));
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, string name, bool generateLoot)
    {
        // Rebuild on every raid start: the inventory may have changed (flea/trader purchases) since the last build.
        // The rebuilt context is cached and reused for this raid's bot generation.
        ActiveLootContext.Service?.Invalidate(sessionId);
        ActiveLootContext.Reset(generateLoot ? ActiveLootContext.Service?.GetContext(sessionId) : null);
    }

    [PatchPostfix]
    public static void Postfix(string name)
    {
        var service = ActiveLootContext.Service;
        if (ActiveLootContext.Current is not null && service is not null)
        {
            foreach (var ((label, container, item), count) in ActiveLootContext.Injections ?? [])
            {
                service.DebugLog($"Added {label} {item} to {count} container(s) of type {container} (baseProbability 999)");
            }

            service.DebugLog(
                $"Map {name}: boosted {ActiveLootContext.SpawnpointsTouched} loose loot spawnpoints and {ActiveLootContext.ContainersTouched} containers");
        }

        ActiveLootContext.Reset(null);
    }
}

/// <summary>Scales loose loot item weights. The LooseLoot passed in is a fresh copy (LazyLoad without caching), so mutating it is safe.</summary>
[Injectable(InjectionType.Transient)]
public class GenerateDynamicLootPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLootGenerator), nameof(LocationLootGenerator.GenerateDynamicLoot));
    }

    [PatchPrefix]
    public static void Prefix(LooseLoot dynamicLootDist)
    {
        var context = ActiveLootContext.Current;
        if (context is null)
        {
            return;
        }

        foreach (var spawnpoint in dynamicLootDist.Spawnpoints ?? [])
        {
            var keyToTpl = new Dictionary<string, string>();
            foreach (var item in spawnpoint.Template?.Items ?? [])
            {
                var key = item.ComposedKey ?? item.Id.ToString();
                keyToTpl.TryAdd(key, item.Template.ToString());
            }

            var touched = false;
            foreach (var distribution in spawnpoint.ItemDistribution ?? [])
            {
                var key = distribution.ComposedKey?.Key;
                if (key is null || !keyToTpl.TryGetValue(key, out var tpl))
                {
                    continue;
                }

                var old = distribution.RelativeProbability ?? 0;
                distribution.RelativeProbability = context.Updater(tpl, old, $"spawnpoint {spawnpoint.LocationId} ({spawnpoint.Template?.Id})");
                touched |= context.BoostedTpls.Contains(tpl);
            }

            if (touched)
            {
                ActiveLootContext.SpawnpointsTouched++;
            }
        }
    }
}

/// <summary>Scales static ammo weights. staticAmmoDist is cloned per raid by GenerateLocationLoot.</summary>
[Injectable(InjectionType.Transient)]
public class GenerateStaticContainersPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLootGenerator), nameof(LocationLootGenerator.GenerateStaticContainers));
    }

    [PatchPrefix]
    public static void Prefix(Dictionary<string, IEnumerable<StaticAmmoDetails>> staticAmmoDist)
    {
        var context = ActiveLootContext.Current;
        if (context is null)
        {
            return;
        }

        foreach (var (ammoId, details) in staticAmmoDist)
        {
            foreach (var ammo in details)
            {
                if (ammo.Tpl is null)
                {
                    continue;
                }

                ammo.RelativeProbability = (float)context.Updater(ammo.Tpl.Value.ToString(), ammo.RelativeProbability ?? 0, $"ammo {ammoId}");
            }
        }
    }
}

/// <summary>
/// Re-weights a static container's possible loot, and injects missing gunsmith parts / quest keys into the
/// matching containers (weight 999, as in the TypeScript mod). Works on the returned list, never on DB data.
/// </summary>
[Injectable(InjectionType.Transient)]
public class GetPossibleLootItemsForContainerPatch : AbstractPatch
{
    private const float InjectedProbability = 999;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLootGenerator), "GetPossibleLootItemsForContainer");
    }

    [PatchPostfix]
    public static void Postfix(MongoId containerTypeId, ProbabilityObjectArray<MongoId, float?> __result)
    {
        var context = ActiveLootContext.Current;
        if (context is null)
        {
            return;
        }

        var containerId = containerTypeId.ToString();
        var touched = Inject(__result, containerId, context.MissingParts, LootProbabilityManager.GunsmithContainers, "gunsmith item");
        touched |= Inject(__result, containerId, context.MissingKeys, LootProbabilityManager.QuestKeyContainers, "quest key");

        foreach (var entry in __result)
        {
            var tpl = entry.Key.ToString();
            entry.RelativeProbability = context.Updater(tpl, entry.RelativeProbability ?? 0, $"container {containerId}");
            touched |= context.BoostedTpls.Contains(tpl);
        }

        if (touched)
        {
            ActiveLootContext.ContainersTouched++;
        }
    }

    /// <returns>True when anything was injected.</returns>
    private static bool Inject(
        ProbabilityObjectArray<MongoId, float?> pool,
        string containerId,
        HashSet<string> missing,
        HashSet<string> containers,
        string label)
    {
        if (missing.Count == 0 || !containers.Contains(containerId))
        {
            return false;
        }

        var injected = false;
        var present = pool.Select(p => p.Key.ToString()).ToHashSet();
        foreach (var itemId in missing.Where(id => !present.Contains(id)))
        {
            pool.Add(new ProbabilityObject<MongoId, float?>(new MongoId(itemId), InjectedProbability, null));
            injected = true;
            if (ActiveLootContext.Injections is { } injections)
            {
                var key = (label, containerId, itemId);
                injections[key] = injections.GetValueOrDefault(key) + 1;
            }
        }

        return injected;
    }
}

/// <summary>
/// Scales bot equipment and loot weights. BotGenerator clones the bot template for every bot,
/// so botJsonTemplate is safe to mutate. Note: mods that replace bot inventory generation
/// (e.g. APBS) may ignore some of these weights.
/// </summary>
[Injectable(InjectionType.Transient)]
public class GenerateBotPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotGenerator), "GenerateBot");
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, BotType botJsonTemplate, BotGenerationDetails botGenerationDetails)
    {
        // The TS mod skipped the "usec"/"bear" templates (PMCs) and "gifter". In 4.x PMC roles are named differently, so use IsPmc.
        var role = botGenerationDetails.Role?.ToLowerInvariant() ?? string.Empty;
        if (botGenerationDetails.IsPmc || LootProbabilityManager.BotTypesToIgnore.Contains(role))
        {
            return;
        }

        var context = ActiveLootContext.Service?.GetContext(sessionId);
        var inventory = botJsonTemplate.BotInventory;
        if (context is null || inventory is null)
        {
            return;
        }

        foreach (var (slot, pool) in inventory.Equipment ?? [])
        {
            Scale(pool, context.Updater, $"bot {role} equipment {slot}");
        }

        var items = inventory.Items;
        if (items is null)
        {
            return;
        }

        Scale(items.Backpack, context.Updater, $"bot {role} items Backpack");
        Scale(items.Pockets, context.Updater, $"bot {role} items Pockets");
        Scale(items.SecuredContainer, context.Updater, $"bot {role} items SecuredContainer");
        Scale(items.SpecialLoot, context.Updater, $"bot {role} items SpecialLoot");
        Scale(items.TacticalVest, context.Updater, $"bot {role} items TacticalVest");
    }

    private static void Scale(Dictionary<MongoId, double>? pool, LootProbabilityUpdater updater, string location)
    {
        if (pool is null)
        {
            return;
        }

        foreach (var tpl in pool.Keys.ToList())
        {
            pool[tpl] = updater(tpl.ToString(), pool[tpl], location);
        }
    }
}
