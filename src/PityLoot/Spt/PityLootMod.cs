using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace PityLoot.Spt;

/// <summary>Entry point: loads config and enables the Harmony patches.</summary>
[Injectable(InjectionType.Transient, int.MaxValue, TypePriority = 100000)]
public class PityLootMod(
    PityLootService service,
    GenerateLocationAndLootPatch generateLocationAndLootPatch,
    GenerateDynamicLootPatch generateDynamicLootPatch,
    GenerateStaticContainersPatch generateStaticContainersPatch,
    GetPossibleLootItemsForContainerPatch getPossibleLootItemsForContainerPatch,
    GenerateBotPatch generateBotPatch) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        service.Load();
        if (!service.Config.Enabled)
        {
            service.Info("Disabled in config.json");
            return Task.CompletedTask;
        }

        ActiveLootContext.Service = service;
        generateLocationAndLootPatch.Enable();
        generateDynamicLootPatch.Enable();
        generateStaticContainersPatch.Enable();
        getPossibleLootItemsForContainerPatch.Enable();
        generateBotPatch.Enable();
        service.Info("Loot patches enabled");
        return Task.CompletedTask;
    }
}
