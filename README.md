# PityLoot

A server mod for SPT 4.1 (Single Player Tushonka) that makes the items you **need** easier to find the longer you go without them.

Like playing hardcore Tarkov, but tired of never finding the items you need for quests or the hideout? Or playing without the flea market and don't want to hoard *every* item you might need later? PityLoot progressively raises the loot odds for items you need, based on the number of raids since you started the task, or on real-world hours since you started it.

Loot stays random; needed items just get more likely over time.

This is a fork of Bakahashi's PityLoot for SPT 3.x, rewritten in C# for SPT 4.1 with the same behavior and config.

## What counts as "needed"

- **Quests:** items for `HandoverItem` / `LeaveItemAtLocation` conditions of quests you have started, minus what you've already handed in. Found-in-raid requirements are respected.
- **Quest keys:** keys needed for started quests (`config/questKeys.json`).
- **Gunsmith parts:** parts for the Gunsmith quests you have started (`config/gunsmith.json`).
- **Hideout:** items for the next level of every hideout area whose other requirements (area levels, skills, trader loyalty) you already meet.

Items already in your stash are counted against these requirements, found-in-raid items first. Money is never boosted.

## How the boost works

- Each started quest and each available hideout upgrade tracks how many raids have passed since it became available.
- An item's spawn weight is multiplied by `1 + raids × dropRateIncreasePerRaid`, capped at `maxDropRateMultiplier`. If several quests or upgrades need the same item, their bonuses stack (`increasesStack`).
- Quest keys get an extra `keysAdditionalMultiplier`.
- Missing quest keys are added to drawers and jackets, and missing Gunsmith parts to weapon boxes and wooden crates, with weight 999 before multipliers.
- Boosts apply to loose loot, container loot, container ammo and scav/boss bot loot, and are recalculated at the start of every raid. PMC bots are not affected.
- The counter resets when the quest is completed or the hideout area is upgraded.

## Installation

1. Download `PityLoot-<version>.zip`.
2. Extract it into your SPT root folder (the folder that contains `SPT_Runtime`).
3. You should now have `SPT_Runtime/user/mods/PityLoot/PityLoot.dll`.
4. Start the server. You should see `[PityLoot] Loot patches enabled` in the console.

## Configuration

Edit `SPT_Runtime/user/mods/PityLoot/config/config.json` and restart the server.

| Option | Default | Description |
|---|---|---|
| `enabled` | `true` | Turn the mod on or off. |
| `debug` | `false` | Log requirements, multipliers, raid counting and boosted containers. |
| `trace` | `false` | Log every single weight change. Very noisy and can cause lag at raid start. |
| `appliesToQuests` | `true` | Boost items needed for started quests (including keys and Gunsmith parts). |
| `appliesToHideout` | `true` | Boost items needed for available hideout upgrades. |
| `includeKeys` | `true` | Boost and inject keys needed for started quests. |
| `includeGunsmith` | `true` | Boost and inject parts needed for Gunsmith quests. |
| `keysAdditionalMultiplier` | `2.5` | Extra multiplier for quest keys. |
| `increasesStack` | `true` | Add up bonuses when several requirements need the same item (otherwise the largest wins). |
| `maxDropRateMultiplier` | `10` | Cap on the pity multiplier. |
| `dropRateIncreaseType` | `"raid"` | `"raid"`: grow per counted raid. `"time"`: grow per hour since the quest/upgrade became available. |
| `dropRateIncreasePerRaid` | `0.25` | Multiplier added per counted raid. |
| `dropRateIncreasePerHour` | `0.05` | Multiplier added per hour in `"time"` mode. |
| `onlyIncreaseOnFailedRaids` | `true` | Only count raids where you died or went MIA. Set to `false` to also count survived raids. |
| `includeScavRaids` | `true` | Count scav raids too. |
| `appliesToWishlist` | `false` | Apply a fixed multiplier to wishlisted items. |
| `wishlistMultipliers` | `5.0` each | Multiplier per wishlist category (`tasks`, `hideout`, `barter`, `equipment`, `other`). |
| `excludeCollector` | `false` | Ignore the Collector quest. |

Your raid counters are stored per profile in `SPT_Runtime/user/mods/PityLoot/database/pityTracker.json`. Delete it to reset all pity.

## Compatibility

- Built for SPT 4.1.x (tested on 4.1.6).
- Does not modify the database or your profile; all changes are applied to per-raid copies of the loot tables.
- Mods that replace bot inventory generation (for example Acid's Progressive Bot System) build bot gear from their own data, so the bot gear boost may have no effect with them installed. Map loot is unaffected.

## Credits

- **EliteOneTube:** SPT 4.1 C# rewrite.
- **Bakahashi:** original PityLoot for SPT 3.x.

## License

MIT, see [LICENSE](LICENSE). The original PityLoot by Bakahashi is also MIT licensed.
