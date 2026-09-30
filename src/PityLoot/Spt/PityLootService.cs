using System.Reflection;
using PityLoot.Logic;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using Path = System.IO.Path;
#if SPT40
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
#else
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
#endif

namespace PityLoot.Spt;

/// <summary>The loot boost to apply for one player: probability updater plus keys/parts to inject into containers.</summary>
public record PityContext(
    LootProbabilityUpdater Updater,
    HashSet<string> MissingKeys,
    HashSet<string> MissingParts,
    HashSet<string> BoostedTpls);

/// <summary>Ties the pure pity logic to the SPT profile/database. Port of the orchestration in mod.ts.</summary>
[Injectable(InjectionType.Singleton)]
public class PityLootService(
    ISptLogger<PityLootService> logger,
    ProfileHelper profileHelper,
#if SPT40
    DatabaseService databaseService,
#else
    TemplateTable templateTable,
    HideoutTable hideoutTable,
#endif
    ModHelper modHelper)
{
#if SPT40
    private IEnumerable<Quest> DbQuests => databaseService.GetQuests().Values;
    private IEnumerable<HideoutArea> DbHideoutAreas => databaseService.GetHideout().Areas;
#else
    private IEnumerable<Quest> DbQuests => templateTable.Quests.Values;
    private IEnumerable<HideoutArea> DbHideoutAreas => hideoutTable.Areas;
#endif

    private const string LogPrefix = "[PityLoot] ";

    private readonly Dictionary<string, PityContext> _contextBySession = new();
    private readonly Lock _contextLock = new();
    private Dictionary<string, QuestDef>? _quests;

    public PityLootConfig Config { get; private set; } = new();
    public PityTrackerStore Tracker { get; private set; } = null!;
    public Dictionary<string, List<string>> QuestKeys { get; private set; } = new();
    public Dictionary<string, List<string>> Gunsmith { get; private set; } = new();

    public void Load()
    {
        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        Config = PityLootConfig.Load(Path.Combine(modFolder, "config", "config.json"));
        QuestKeys = PityLootConfig.LoadQuestItemMap(Path.Combine(modFolder, "config", "questKeys.json"));
        Gunsmith = PityLootConfig.LoadQuestItemMap(Path.Combine(modFolder, "config", "gunsmith.json"));
        Tracker = new PityTrackerStore(Path.Combine(modFolder, "database", "pityTracker.json"));
        Info($"Loaded config (enabled: {Config.Enabled}), {QuestKeys.Count} quest key mappings, {Gunsmith.Count} gunsmith mappings");
    }

    public void Info(string message) => logger.Info(LogPrefix + message);
    public void Warning(string message) => logger.Warning(LogPrefix + message);
    public void DebugLog(string message)
    {
        if (Config.Debug)
        {
            logger.Info(LogPrefix + message);
        }
    }

    private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private PmcData? GetValidPmc(MongoId sessionId)
    {
        var pmc = profileHelper.GetFullProfile(sessionId)?.CharacterData?.PmcData;
        if (pmc?.Hideout is null)
        {
            Warning("Profile not valid yet, skipping for now");
            return null;
        }

        return pmc;
    }

    private Dictionary<string, QuestDef> Quests =>
        _quests ??= DbQuests.Select(SptMapper.Quest).ToDictionary(q => q.Id);

    private List<HideoutUpgradeInfo> PossibleHideoutUpgrades(PmcData pmc)
    {
        return HideoutPlanner.GetPossibleHideoutUpgrades(
            DbHideoutAreas.Select(SptMapper.HideoutArea),
            SptMapper.HideoutProfile(pmc),
            Warning);
    }

    /// <summary>Port of handlePityChange: refresh the tracker and optionally count a raid.</summary>
    public void HandlePityChange(MongoId sessionId, bool incrementRaidCount)
    {
        var pmc = GetValidPmc(sessionId);
        if (pmc is null)
        {
            return;
        }

        var profileId = sessionId.ToString();
        var old = Tracker.Load(profileId);
        var updated = PityTrackerLogic.Update(
            old,
            SptMapper.StartedQuests(pmc).Select(q => q.QuestId),
            PossibleHideoutUpgrades(pmc),
            incrementRaidCount,
            NowMs);
        Tracker.Save(profileId, updated);
        Invalidate(sessionId);

        if (Config.Debug)
        {
            DebugLog($"Tracker updated (raid counted: {incrementRaidCount}): {updated.Quests.Count} quests, {updated.Hideout.Count} hideout areas");
            foreach (var (qid, q) in updated.Quests)
            {
                DebugLog($"  quest {Quests.GetValueOrDefault(qid)?.Name ?? qid}: {q.RaidsSinceStarted} raids");
            }

            foreach (var (area, h) in updated.Hideout)
            {
                DebugLog($"  hideout area {area} -> level {h.CurrentLevel}: {h.RaidsSinceStarted} raids");
            }
        }
    }

    public void Invalidate(MongoId sessionId)
    {
        lock (_contextLock)
        {
            _contextBySession.Remove(sessionId.ToString());
        }
    }

    /// <summary>Gets (or builds and caches) the boost for this player. Null when there's nothing to do.</summary>
    public PityContext? GetContext(MongoId sessionId)
    {
        if (!Config.Enabled)
        {
            return null;
        }

        var key = sessionId.ToString();
        lock (_contextLock)
        {
            if (_contextBySession.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var context = BuildContext(sessionId);
        if (context is not null)
        {
            lock (_contextLock)
            {
                _contextBySession[key] = context;
            }
        }

        return context;
    }

    private PityContext? BuildContext(MongoId sessionId)
    {
        var pmc = GetValidPmc(sessionId);
        if (pmc is null)
        {
            return null;
        }

        var start = System.Diagnostics.Stopwatch.StartNew();
        var tracker = Tracker.Load(sessionId.ToString());
        var questPlanner = new QuestPlanner(Config, QuestKeys, Gunsmith, DebugLog);
        var manager = new LootProbabilityManager(Config, DebugLog);

        var questRequirements = Config.AppliesToQuests
            ? questPlanner.GetInProgressQuestRequirements(SptMapper.StartedQuests(pmc), Quests, tracker, NowMs / 1000.0)
            : [];
        var hideoutRequirements = Config.AppliesToHideout
            ? HideoutPlanner.GetHideoutRequirements(PossibleHideoutUpgrades(pmc), tracker, NowMs)
            : [];

        var incomplete = manager.GetIncompleteRequirements(
            SptMapper.Inventory(pmc),
            SptMapper.ConditionProgress(pmc),
            questRequirements,
            hideoutRequirements);

        var wishList = SptMapper.WishList(pmc);
        var updater = manager.CreateLootProbabilityUpdater(wishList, incomplete);
        var boostedTpls = manager.GetBoostedTpls(wishList, incomplete);
        var missingKeys = incomplete
            .Where(r => r.Type == RequirementType.QuestKey || r.ItemId == LootProbabilityManager.KeycardId)
            .Select(r => r.ItemId)
            .ToHashSet();
        var missingParts = incomplete
            .Where(r => r.Type == RequirementType.Gunsmith)
            .Select(r => r.ItemId)
            .ToHashSet();

        DebugLog($"Built pity context in {start.ElapsedMilliseconds} ms: {incomplete.Count} incomplete requirements, {boostedTpls.Count} items boosted above x1, {missingKeys.Count} missing keys, {missingParts.Count} missing gunsmith parts");
        return new PityContext(updater, missingKeys, missingParts, boostedTpls);
    }
}
