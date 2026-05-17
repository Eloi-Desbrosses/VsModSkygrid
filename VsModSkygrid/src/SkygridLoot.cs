using System;
using System.Collections.Generic;

namespace VsModSkygrid;

internal readonly record struct LootEntry(string Code, int MinQty, int MaxQty);

internal sealed class LootTable
{
    public required string Name;
    public required LootEntry[] Pool;
    public required int MinPicks;
    public required int MaxPicks;
    public double Weight = 1.0;
}

internal sealed class TierSpec
{
    public required string Name;
    public required string[] BlockCodeChain;
    public required double Weight;
    public required LootTable[] Tables;
    public required TierWeightStep[] WeightSteps;
    /// <summary>True for blocks with UnstableFalling (storagevessel) — the placement code drops a
    /// solid block one Y below so the container doesn't fall into the void.</summary>
    public bool NeedsSupport;
}

internal readonly record struct TierWeightStep(double MinDistance, double Weight);

/// <summary>
/// Loot tables for Skygrid's scattered chests. Each tier defines the container block plus weighted
/// loot tables. Block codes use VS 1.22.2 <c>code:</c> field values (not filenames — e.g.
/// reedchest.json declares code "stationarybasket"). Unresolved codes are logged at startup via
/// <see cref="AllReferencedCodes"/>.
/// </summary>
internal static class SkygridLoot
{
    // ─────────────────────────────────────────────────────────────────────────
    //  TIER 0 — BASKET  (stationarybasket-east, 8 slots)
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly LootTable SurvivalStarter = new()
    {
        Name = "Survival Starter",
        MinPicks = 5, MaxPicks = 8,
        Pool = new LootEntry[]
        {
            new("log-grown-oak-ud",     4, 8),
            new("log-grown-birch-ud",   4, 8),
            new("log-grown-pine-ud",    4, 8),
            new("soil-low-none",        8, 16),
            new("soil-medium-none",     6, 12),
            new("treeseed-oak",         2, 4),
            new("treeseed-birch",       2, 4),
            new("treeseed-pine",        2, 4),
            new("seeds-flax",           2, 4),
            new("seeds-carrot",         2, 4),
            new("flint",                4, 8),
            new("stick",                4, 8),
            new("vegetable-carrot",             2, 4),
            new("vegetable-onion",              2, 4),
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  TIER 1 — CHEST  (chest-east, 16 slots)
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly LootTable ToolCache = new()
    {
        Name = "Tool Cache",
        MinPicks = 6, MaxPicks = 10,
        Pool = new LootEntry[]
        {
            new("axe-flint",            1, 1),
            new("spear-generic-flint",  1, 1),
            new("shovel-flint",         1, 1),
            new("hoe-flint",            1, 1),
            new("firestarter",          1, 2),
            new("hammer-copper",        1, 1),
            new("hammer-tinbronze",     1, 1),
            new("shears-copper",        1, 1),
            new("prospectingpick-copper", 1, 1),
            new("knife-generic-copper", 1, 1),
            new("pickaxe-copper",       1, 1),
        }
    };

    private static readonly LootTable FoodCache = new()
    {
        Name = "Food Cache",
        MinPicks = 6, MaxPicks = 10,
        Pool = new LootEntry[]
        {
            new("vegetable-carrot",             4, 8),
            new("vegetable-onion",              4, 8),
            new("vegetable-parsnip",            4, 8),
            new("vegetable-turnip",             4, 8),
            new("vegetable-cabbage",            2, 6),
            new("legume-soybean",       4, 8),
            new("redmeat-raw",          2, 4),
            new("salt",                 4, 8),
            new("fat",                  4, 8),
            new("hide-raw-medium",      2, 4),
            new("bread-spelt-perfect",  2, 4),
        }
    };

    private static readonly LootTable SeedAndSapling = new()
    {
        Name = "Seeds & Saplings",
        MinPicks = 6, MaxPicks = 10,
        Pool = new LootEntry[]
        {
            new("treeseed-oak",         3, 6),
            new("treeseed-pine",        3, 6),
            new("treeseed-birch",       3, 6),
            new("treeseed-maple",       3, 6),
            new("treeseed-acacia",      2, 4),
            new("treeseed-redwood",     2, 4),
            new("treeseed-kapok",       2, 4),
            new("seeds-flax",           4, 8),
            new("seeds-carrot",         4, 8),
            new("seeds-onion",          4, 8),
            new("seeds-turnip",         4, 8),
            new("seeds-cabbage",        2, 4),
            new("seeds-parsnip",        2, 4),
            new("seeds-soybean",        2, 4),
            new("seeds-pumpkin",        2, 4),
        }
    };

    private static readonly LootTable EarlyMetal = new()
    {
        Name = "Early Metal",
        MinPicks = 6, MaxPicks = 10,
        Pool = new LootEntry[]
        {
            new("nugget-nativecopper",  4, 8),
            new("nugget-cassiterite",   2, 4),
            new("nugget-limonite",      2, 4),
            new("nugget-sphalerite",    2, 4),
            new("ingot-copper",         2, 4),
            new("ingot-tinbronze",      2, 4),
            new("ingot-bismuthbronze",  1, 3),
            new("charcoal",             8, 16),
            new("hammer-copper",        1, 1),
            new("hammer-tinbronze",     1, 1),
            new("tongs",                1, 1),
        }
    };

    private static readonly LootTable BuildingMaterials = new()
    {
        Name = "Building Materials",
        MinPicks = 6, MaxPicks = 10,
        Pool = new LootEntry[]
        {
            new("rock-granite",         16, 32),
            new("rock-andesite",        16, 32),
            new("rock-basalt",          16, 32),
            new("rock-chalk",           16, 32),
            new("rock-sandstone",       16, 32),
            new("log-grown-oak-ud",     8, 16),
            new("log-grown-pine-ud",    8, 16),
            new("planks-aged-ud",       8, 16),
            new("planks-veryaged-ud",   8, 16),
            new("charcoal",             8, 16),
            new("claybricks-good-fire", 8, 16),
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  TIER 2 — TRUNK  (trunk-east, 36 slots)
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly LootTable ForgeKit = new()
    {
        Name = "Forge Kit",
        Weight = 0.5,
        MinPicks = 12, MaxPicks = 20,
        Pool = new LootEntry[]
        {
            new("firepit-cold",         1, 1),
            new("anvil-copper",         1, 1),
            new("anvilpart-base-iron",  1, 2),
            new("ingotmold-brown-fired",2, 4),
            new("hammer-copper",        1, 1),
            new("hammer-tinbronze",     1, 1),
            new("tongs",                1, 2),
            new("ingot-copper",         8, 16),
            new("ingot-tinbronze",      4, 8),
            new("ingot-bismuthbronze",  2, 4),
            new("nugget-nativecopper",  16, 32),
            new("charcoal",             16, 32),
            new("chest-east",           2, 4),
        }
    };

    private static readonly LootTable BloomeryKit = new()
    {
        Name = "Bloomery Kit",
        Weight = 0.5,
        MinPicks = 10, MaxPicks = 16,
        Pool = new LootEntry[]
        {
            new("bloomerybase-east",        1, 1),
            new("helvehammerbase-east",     1, 1),
            new("pulverizerframe-east",     1, 1),
            new("forge",                    1, 1),
            new("clayoven-east",            1, 1),
            new("anvil-iron",               1, 1),
            new("hammer-iron",              1, 1),
            new("ingot-iron",               4, 8),
            new("nugget-limonite",          16, 32),
            new("charcoal",                 24, 48),
            new("ingotmold-brown-fired",    2, 4),
            new("grindingwheel-wood-east",  1, 1),
        }
    };

    private static readonly LootTable StorageKit = new()
    {
        Name = "Storage Kit",
        MinPicks = 10, MaxPicks = 16,
        Pool = new LootEntry[]
        {
            new("chest-east",                3, 6),
            new("labeledchest-east",         1, 2),
            new("trunk-east",                1, 2),
            new("barrel",                    2, 4),
            new("storagevessel-brown-fired", 1, 2),
            new("storagevessel-cream-fired", 1, 2),
            new("stationarybasket-east",     2, 4),
            new("lantern-small-up",          2, 4),
            new("torchholder-brass-empty-north", 2, 4),
            new("sign-wall-north",           4, 6),
            new("signpost",                  2, 4),
            new("door-solid-aged-down-closed-north", 1, 2),
            new("trapdoor-closed-up-east",   1, 2),
            new("tapestry-east",             1, 2),
            new("toolrack-north",            1, 2),
        }
    };

    private static readonly LootTable WorkshopKit = new()
    {
        Name = "Workshop Kit",
        MinPicks = 10, MaxPicks = 16,
        Pool = new LootEntry[]
        {
            new("quern-granite",                    1, 1),
            new("clayplanter-cream-fired",          2, 4),
            new("flowerpot-cream-fired",            2, 4),
            new("displaycase-generic",              1, 2),
            new("scrollrack",                       1, 1),
            new("bookshelf",                        1, 2),
            new("smallberrybush-blueberry-empty",   1, 2),
            new("bigberrybush-redcurrant-empty",    1, 2),
            new("woodbucket",                       1, 2),
            new("trapcrate-wood",                   1, 2),
            new("locustnest-cage",                  1, 2),
            new("ingot-copper",                     4, 8),
            new("ingot-tinbronze",                  2, 4),
            new("clay-blue",                        8, 16),
            new("clay-red",                         8, 16),
            new("clay-fire",                        4, 8),
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  TIER 3 — VESSEL  (storagevessel, 12 slots)
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly LootTable TreasureMetal = new()
    {
        Name = "Treasure: Refined Metal",
        MinPicks = 4, MaxPicks = 8,
        Pool = new LootEntry[]
        {
            new("ingot-iron",                 4, 8),
            new("ingot-meteoriciron",         1, 3),
            new("ingot-steel",                1, 2),
            new("ingot-blistersteel",         1, 2),
            new("ingot-gold",                 1, 2),
            new("ingot-silver",               1, 2),
            new("ingot-electrum",             1, 2),
            new("ingot-cupronickel",          2, 4),
            new("ingot-platinum",             1, 1),
            new("nugget-nativegold",          4, 8),
            new("nugget-nativesilver",        4, 8),
            new("meteorite-iron",             1, 1),
        }
    };

    private static readonly LootTable TreasureGems = new()
    {
        Name = "Treasure: Gems & Rare",
        MinPicks = 4, MaxPicks = 8,
        Pool = new LootEntry[]
        {
            new("gem-diamond-rough",          1, 2),
            new("gem-emerald-rough",          1, 2),
            new("gem-olivine_peridot-rough",  1, 3),
            new("ingot-gold",                 1, 2),
            new("nugget-nativegold",          2, 4),
        }
    };

    private static readonly LootTable TreasureMechPower = new()
    {
        Name = "Treasure: Mechanical Power",
        MinPicks = 4, MaxPicks = 8,
        Pool = new LootEntry[]
        {
            new("waterwheel-3m-east",         1, 1),
            new("windmillrotor-wood-east",    1, 1),
            new("woodenaxle-ud",              4, 8),
            new("spurgear-d",                 4, 8),
            new("angledgears-en",             2, 4),
            new("brake-east",                 1, 2),
            new("clutch-east",                1, 1),
            new("transmission-ns",            1, 2),
            new("largegear3",                 1, 1),
            new("hopper",                     1, 2),
            new("chute-elbow-down-east",      2, 4),
            new("ingot-copper",               8, 16),
            new("ingot-iron",                 4, 8),
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  ANIMAL CREATURES — CHEST  (chest-east, 16 slots)
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly LootTable PassiveAnimalCreatures = new()
    {
        Name = "Animal Creatures: Passive",
        MinPicks = 2, MaxPicks = 4,
        Pool = new LootEntry[]
        {
            new("creature-chicken-hen",                1, 2),
            new("creature-chicken-rooster",            1, 1),
            new("creature-pig-eurasian-adult-female",  1, 2),
            new("creature-pig-eurasian-adult-male",    1, 1),
            new("creature-sheep-bighorn-adult-female", 1, 2),
            new("creature-sheep-bighorn-adult-male",   1, 1),
            new("creature-goat-mountain-adult-female", 1, 2),
            new("creature-goat-mountain-adult-male",   1, 1),
            new("creature-hare-european-adult-female", 1, 2),
            new("creature-hare-european-adult-male",   1, 1),
        }
    };

    private static readonly LootTable AggressiveAnimalCreatures = new()
    {
        Name = "Animal Creatures: Aggressive",
        MinPicks = 2, MaxPicks = 4,
        Pool = new LootEntry[]
        {
            new("creature-wolf-eurasian-adult-male",   1, 1),
            new("creature-wolf-eurasian-adult-female", 1, 1),
            new("creature-hyena-spotted-adult-male",   1, 1),
            new("creature-hyena-spotted-adult-female", 1, 1),
            new("creature-bear-brown-adult-male",      1, 1),
            new("creature-bear-brown-adult-female",    1, 1),
            new("creature-bear-black-adult-male",      1, 1),
            new("creature-bear-black-adult-female",    1, 1),
            new("creature-bear-polar-adult-male",      1, 1),
            new("creature-bear-polar-adult-female",    1, 1),
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  TIER REGISTRY
    // ─────────────────────────────────────────────────────────────────────────
    public static readonly TierSpec[] Tiers =
    {
        new() {
            Name = "Basket",
            Weight = 0.40,
            BlockCodeChain = new[] { "stationarybasket-east" },
            Tables = new[] { SurvivalStarter },
            WeightSteps = new[] { new TierWeightStep(0, 0.30), new TierWeightStep(1000, 0.10) },
        },
        new() {
            Name = "Chest",
            Weight = 0.30,
            BlockCodeChain = new[] { "chest-east" },
            Tables = new[] { ToolCache, FoodCache, SeedAndSapling, EarlyMetal, BuildingMaterials },
            WeightSteps = new[] { new TierWeightStep(0, 0.30), new TierWeightStep(1000, 0.40), new TierWeightStep(2000, 0.10) },
        },
        new() {
            Name = "Trunk",
            Weight = 0.20,
            BlockCodeChain = new[] { "trunk-east", "chest-east" },
            Tables = new[] { ForgeKit, BloomeryKit, StorageKit, WorkshopKit },
            WeightSteps = new[] { new TierWeightStep(0, 0.10), new TierWeightStep(1000, 0.20), new TierWeightStep(2000, 0.10) },
        },
        new() {
            Name = "Animal Chest",
            Weight = 0.10,
            BlockCodeChain = new[] { "chest-east" },
            Tables = new[] { PassiveAnimalCreatures, AggressiveAnimalCreatures },
            WeightSteps = new[] { new TierWeightStep(0, 0.20), new TierWeightStep(1000, 0.10), new TierWeightStep(2000, 0.10) },
        },
        new() {
            Name = "Vessel",
            Weight = 0.10,
            BlockCodeChain = new[] {
                "storagevessel-cream-fired", "storagevessel-brown-fired",
                "storagevessel-blue-fired",  "chest-east"
            },
            Tables = new[] { TreasureMetal, TreasureGems, TreasureMechPower },
            WeightSteps = new[] { new TierWeightStep(0, 0.10), new TierWeightStep(1000, 0.20), new TierWeightStep(2000, 0.60) },
            NeedsSupport = true,
        },
    };

    public static int PickTier(Random rng, double distanceFromSpawn = 0)
    {
        double total = 0;
        for (int i = 0; i < Tiers.Length; i++)
        {
            total += TierWeightAtDistance(Tiers[i], distanceFromSpawn);
        }
        double pick = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < Tiers.Length; i++)
        {
            acc += TierWeightAtDistance(Tiers[i], distanceFromSpawn);
            if (pick < acc) return i;
        }
        return Tiers.Length - 1;
    }

    public static int PickTable(TierSpec tier, Random rng)
    {
        double total = 0;
        foreach (var table in tier.Tables) total += table.Weight;

        double pick = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < tier.Tables.Length; i++)
        {
            acc += tier.Tables[i].Weight;
            if (pick < acc) return i;
        }
        return tier.Tables.Length - 1;
    }

    private static double TierWeightAtDistance(TierSpec tier, double distanceFromSpawn)
    {
        double weight = tier.Weight;
        foreach (var step in tier.WeightSteps)
        {
            if (distanceFromSpawn >= step.MinDistance) weight = step.Weight;
            else break;
        }
        return weight;
    }

    public static readonly LootTable StarterChest = SurvivalStarter;

    /// <summary>Every distinct code referenced anywhere — tier blocks + loot entries.</summary>
    public static IEnumerable<string> AllReferencedCodes()
    {
        var seen = new HashSet<string>();
        foreach (var t in Tiers)
        {
            foreach (var c in t.BlockCodeChain) if (seen.Add(c)) yield return c;
            foreach (var table in t.Tables)
                foreach (var e in table.Pool)
                    if (seen.Add(e.Code)) yield return e.Code;
        }
    }

}
