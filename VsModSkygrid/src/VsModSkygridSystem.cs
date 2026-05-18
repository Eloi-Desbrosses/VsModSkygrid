using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace VsModSkygrid;

public class VsModSkygridSystem : ModSystem
{
    // ── Worldgen config ──────────────────────────────────────────────────────
    public const int Spacing = 4;
    public const int MinY = 30;
    public const int MaxY = 250;
    /// <summary>Every generated chunk-column hosts at least one bonus container.</summary>
    public const double ChestChancePerColumn = 1.0;
    /// <summary>If a column rolls chests, how many it can host (1..MaxChestsPerColumn).</summary>
    public const int MaxChestsPerColumn = 128;

    /// <summary>Horizontal distance (blocks) from spawn at which item-count multiplier hits its cap.</summary>
    public const double LootScaleCapDistance = 5000.0;

    /// <summary>Max picks multiplier when chest is &gt;= LootScaleCapDistance from spawn.</summary>
    public const double LootScaleMaxMultiplier = 2.0;
    /// <summary>Max retries before a SetBlock mismatch is treated as a final failure.</summary>
    public const int ChestPlaceRetries = 3;

    /// <summary>Probability a grid cell uses the "decorative" (non-navigable, non-liquid) pool.</summary>
    public const int DecorativeChancePercent = 10;
    /// <summary>Probability a grid cell uses a liquid (water/lava/etc.). Treated like decorative — a
    /// small accent rather than the bulk of the grid. Remaining fraction goes to the navigable pool.</summary>
    public const int LiquidChancePercent = 3;

    private const int ChunkSize = GlobalConstants.ChunkSize;
    private const string SpawnChestKeyPrefix = "vsmodskygrid.spawnchest.";

    /// <summary>Creative-mod block prefixes (loaded under 'game' domain despite the asset folder).</summary>
    private static readonly string[] CreativeBlockPathPrefixes =
    {
        "creativeblock", "creativeglow", "creativelight",
        "blockideater", "vertexeater",
        "gltftest", "slopetestobj", "texturerotationtest",
    };

    private static readonly HashSet<string> LiquidVariantKeepList = new()
    {
        "water-still-7",
        "saltwater-still-7",
        "boilingwater-still-7",
        "rapidwater-still-7",
        "lava-still-7",
    };

    /// <summary>Whitelist of structure codes whose chunks we DON'T wipe — only these survive as
    /// "vintage islands" inside the skygrid void. Vanilla VS 1.22.2 has 6 story structures total;
    /// we keep the 5 critical to the lore arc (lazaret → village → tobiascave/devastationarea +
    /// resonancearchive) and drop treasurehunter (standalone side-content). All non-story
    /// structures (translocators, tiled dungeons, surface ruins, etc.) get wiped to grid.</summary>
    private static readonly HashSet<string> PreservedStructureCodes = new()
    {
        "lazaret",
        "village",
        "tobiascave",
        "devastationarea",
        "resonancearchive",
    };

    // ── State ────────────────────────────────────────────────────────────────
    private ICoreServerAPI _sapi = null!;
    /// <summary>Full kept pool (after EntityClass/creative/-raw/Unplaceable filters). Used by DumpPalette and as union for splitting.</summary>
    private List<int> _pool = new();
    /// <summary>FullCube + SolidTop, no liquids. Picked by ~(100-DecorativeChancePercent-LiquidChancePercent)% of grid cells.
    /// Each entry is a variant group (ore-* codes with the same canonical mineral collapse to one
    /// group whose elements are the host-rock/grade variants — chosen randomly per placement).</summary>
    private List<int[]> _navigableGroups = new();
    /// <summary>Partial collision + non-liquid NoCollision (fences, plants, deco). Picked by ~DecorativeChancePercent% of cells.</summary>
    private List<int[]> _decorativeGroups = new();
    /// <summary>Liquids only (water/lava/saltwater/rapidwater/boilingwater). Picked by ~LiquidChancePercent% of cells.</summary>
    private List<int[]> _liquidGroups = new();
    /// <summary>Family-name → number of leading dash-segments to retain in the canonical key for
    /// cosmetic-only block families. Everything past the Nth segment is treated as
    /// orientation/cardinal/state/cosmetic-index and collapsed. Picked from per-family variant
    /// counts in the palette dump where variants differ only visually (same gameplay).</summary>
    private static readonly Dictionary<string, int> CosmeticGroupKeepSegments = new()
    {
        // Fences/gates — single block visually × wood/orientation/state
        { "drystonefence",                1 },
        { "woodenfence",                  2 }, { "woodenfencegate",              2 },
        { "roughhewnfence",               2 }, { "roughhewnfencegate",           2 },
        // Roofing — material variants worth keeping, orientation cosmetic
        { "slantedroofing",               2 }, { "slantedroofingbottom",         2 },
        { "slantedroofingcornerinner",    2 }, { "slantedroofingcornerouter",    2 },
        { "slantedroofinghalfleft",       2 }, { "slantedroofinghalfright",      2 },
        { "slantedroofingridge",          2 }, { "slantedroofingridgeend",       2 },
        { "slantedroofingridgehalfleft",  2 }, { "slantedroofingridgehalfright", 2 },
        { "slantedroofingtip",            2 }, { "slantedroofingtop",            2 },
        // Stairs — keep rock/wood/clay-color, drop orientation
        { "cobblestonestairs",            2 }, { "stonebrickstairs",             2 },
        { "brickstairs",                  2 }, { "plankstairs",                  2 },
        { "clayshinglestairs",            2 },
        // Slabs — same as stairs
        { "cobblestoneslab",              2 }, { "polishedrockslab",             2 },
        { "stonebrickslab",               2 }, { "brickslabs",                   2 },
        { "plankslab",                    2 }, { "clayshinglelabs",              2 },
        { "glassslab",                    2 },
        // Ground (rock × cosmetic index)
        { "gravel",                       2 }, { "sand",                         2 },
        { "looseboulders",                2 }, { "looseflints",                  2 },
        { "loosestones",                  2 },
        // Other architectural
        { "cobblestonefan",               2 }, { "metalsheet",                   2 },
        { "brickcourse",                  1 }, { "palisadewall",                 1 },
        { "multiblock",                   2 }, { "caveart",                      2 },
        { "stalagsection",                2 }, { "metalblock",                   2 },
        // Logs — keep state+wood, drop orientation
        { "log",                          3 }, { "logquad",                      3 },
        { "logsection",                   3 }, { "carvedlog",                    3 },
        { "debarkedlog",                  2 },
        // Planks family — keep wood, drop orientation
        { "planks",                       2 }, { "burnedplanks",                 3 },
        { "agedwallpaperplanks",          2 },
        // Daub — keep color, drop state
        { "daub",                         2 },
        // Pure cosmetic indices / textures
        { "devastatedsoil",               1 }, { "dirtygravel",                  2 },
        // Decorative
        { "crystal",                      3 }, { "coral",                        2 },
        { "painting",                     2 }, { "symbols",                      2 },
        { "door",                         2 }, { "ladder",                       2 },
        { "oillamp",                      2 },
    };

    /// <summary>Families whose pattern is "{family}-{state}-{species}" (state in the middle,
    /// meaningful axis at the end). Canonical key = "{family}-{last_segment}" so density variations
    /// collapse but species stay distinct.</summary>
    private static readonly HashSet<string> CosmeticGroupKeepFamilyAndLast = new()
    {
        "leaves",        // leaves-grown-oak, leaves-grown1-oak, ... → leaves-oak
        "leavesbranchy", // same shape as leaves
    };
    private long _seedXor;

    /// <summary>BlockId for each tier (parallel array to <see cref="SkygridLoot.Tiers"/>).</summary>
    private int[] _tierBlockIds = Array.Empty<int>();

    /// <summary>Solid support block placed UNDER falling containers (storagevessel = UnstableFalling).</summary>
    private int _supportBlockId;

    /// <summary>Cached (code, bbox) of every story structure we want to preserve. Populated at
    /// OnReady from <see cref="GenStoryStructures.Structures"/>. Story structs do NOT register in
    /// <see cref="IMapRegion.GeneratedStructures"/> (only tiled dungeons + villages + worldgen
    /// structures do) — they live in a separate runtime registry, so we have to query both.</summary>
    private List<Cuboidi> _storyStructLocations = new();
    /// <summary>Same data keyed by code, used by the gate network planner to look up CenterPos
    /// per story structure (lazaret, village, tobiascave, devastationarea, resonancearchive).</summary>
    private Dictionary<string, BlockPos> _storyStructCenters = new();
    /// <summary>Per-code bbox so the gate planner can push gates OUTSIDE the structure footprint
    /// instead of into it (resonancearchive is 200x170 underground; CenterPos+50 was still inside).</summary>
    private Dictionary<string, Cuboidi?> _storyStructBboxes = new();

    private SkygridGates _gates = null!;

    /// <summary>Pending chests awaiting BE spawn + loot fill on the main tick. Last field = retry count.</summary>
    private readonly ConcurrentQueue<(BlockPos pos, int tierIdx, int tableIdx, int retries)> _pendingChests = new();

    /// <summary>Players whose starter basket couldn't be placed at PlayerJoin (chunk not ready) —
    /// retried on the game tick until SetBlock takes.</summary>
    private readonly ConcurrentQueue<IServerPlayer> _pendingStarterChests = new();

    /// <summary>Per-tier counters for diagnostic logging.</summary>
    private int[] _tierEnqueued = Array.Empty<int>();
    private int[] _tierPlaced = Array.Empty<int>();
    private int[] _tierMismatchOnPlace = Array.Empty<int>();
    private long _lastDiagLogMs;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnReady);
        api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.PreDone, "standard");
        api.Event.PlayerJoin += OnPlayerJoin;
        api.Event.RegisterGameTickListener(DrainPendingChests, 250);
    }

    private void OnReady()
    {
        _pool = ResolveAllBlocks(_sapi);
        _seedXor = _sapi.World.Seed * 2654435761L;
        _tierBlockIds = ResolveTierBlockIds(_sapi);
        _tierEnqueued = new int[SkygridLoot.Tiers.Length];
        _tierPlaced = new int[SkygridLoot.Tiers.Length];
        _tierMismatchOnPlace = new int[SkygridLoot.Tiers.Length];
        _supportBlockId = _sapi.World.GetBlock(new AssetLocation("rock-granite"))?.BlockId ?? 0;
        SplitPoolByNavigability();
        CacheStoryStructureLocations();
        _gates = new SkygridGates(_sapi);
        _gates.Plan(_storyStructCenters, _storyStructBboxes);
        // Throttled tick: each fire trickles 3 force-load requests + tries to place every still-
        // unplaced gate whose chunk is addressable. Spreads the chunk-load burst over time
        // (~17 sec for 50 chunks at 3/tick × 2s tick interval) so worldgen queue stays responsive.
        _sapi.Event.RegisterGameTickListener(dt => _gates?.TickRetry(dt), 2000);

        int navVariants = _navigableGroups.Sum(g => g.Length);
        int decVariants = _decorativeGroups.Sum(g => g.Length);
        int liqVariants = _liquidGroups.Sum(g => g.Length);
        _sapi.Logger.Notification(
            $"[skygrid] ready. pool={_pool.Count} groups: navigable={_navigableGroups.Count}({navVariants}v), " +
            $"decorative={_decorativeGroups.Count}({decVariants}v) @ {DecorativeChancePercent}%, " +
            $"liquid={_liquidGroups.Count}({liqVariants}v) @ {LiquidChancePercent}% — " +
            $"spacing={Spacing} band=[{MinY}..{MaxY}] containers=1..{MaxChestsPerColumn}/col");
        for (int i = 0; i < SkygridLoot.Tiers.Length; i++)
        {
            var t = SkygridLoot.Tiers[i];
            _sapi.Logger.Notification($"[skygrid] tier {i} '{t.Name}' weight={t.Weight:P0} blockId={_tierBlockIds[i]} tables={t.Tables.Length}");
        }

        // Diagnostic: walk every code referenced anywhere in the loot tables; log the ones that
        // don't resolve as block OR item. Helps catch code-name drift between VS versions.
        var unresolved = new List<string>();
        foreach (var code in SkygridLoot.AllReferencedCodes())
        {
            var loc = new AssetLocation(code);
            var block = _sapi.World.GetBlock(loc);
            if (block != null && block.BlockId != 0) continue;
            var item = _sapi.World.GetItem(loc);
            if (item != null) continue;
            unresolved.Add(code);
        }
        if (unresolved.Count > 0)
        {
            _sapi.Logger.Warning($"[skygrid] {unresolved.Count} unresolved loot codes (will be silently skipped in chests):");
            foreach (var c in unresolved) _sapi.Logger.Warning($"[skygrid]   - {c}");
        }
        else
        {
            _sapi.Logger.Notification("[skygrid] all loot codes resolved ✓");
        }

        try { DumpPalette(); }
        catch (Exception ex) { _sapi.Logger.Warning($"[skygrid] palette dump failed: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PALETTE DUMP — diagnostic, runs once at server ready.
    //
    //  Classifies every block currently in _pool on three axes and writes the
    //  result to <data>/Logs/skygrid-palette.txt. Read by humans, not by code.
    //  Used to decide which families to keep for a navigable grid (most blocks
    //  in the raw pool are non-cubes / translucent / plant-like, which makes
    //  the grid unnavigable).
    // ─────────────────────────────────────────────────────────────────────────

    private enum Solidity { FullCube, SolidTop, Partial, NoCollision }

    private static Solidity ClassifySolidity(Block b)
    {
        var boxes = b.CollisionBoxes;
        if (boxes == null || boxes.Length == 0) return Solidity.NoCollision;

        bool fullCubeBox = false;
        if (boxes.Length == 1)
        {
            var c = boxes[0];
            if (c.X1 <= 0.001f && c.Y1 <= 0.001f && c.Z1 <= 0.001f &&
                c.X2 >= 0.999f && c.Y2 >= 0.999f && c.Z2 >= 0.999f)
            {
                fullCubeBox = true;
            }
        }

        if (fullCubeBox && b.SideSolid.All) return Solidity.FullCube;
        if (b.SideSolid[BlockFacing.UP.Index]) return Solidity.SolidTop;
        return Solidity.Partial;
    }

    private static string FamilyKey(string path)
    {
        // First "-" segment. E.g. "log-grown-oak-ud" → "log".
        int dash = path.IndexOf('-');
        return dash < 0 ? path : path.Substring(0, dash);
    }

    private void DumpPalette()
    {
        var logsDir = _sapi.GetOrCreateDataPath("Logs");
        var outPath = Path.Combine(logsDir, "skygrid-palette.txt");

        // (Solidity, Material) → count
        var matrix = new Dictionary<(Solidity, EnumBlockMaterial), int>();
        // family → list of (id, code, solidity, opaque, material)
        var byFamily = new Dictionary<string, List<(int id, string code, Solidity sol, bool opaque, EnumBlockMaterial mat)>>();
        var fullCubeCodes = new List<string>();

        foreach (var id in _pool)
        {
            var b = _sapi.World.Blocks[id];
            if (b == null || b.Code == null) continue;
            var sol = ClassifySolidity(b);
            var mat = b.BlockMaterial;
            bool opaque = b.SideOpaque[BlockFacing.UP.Index];
            var code = b.Code.ToShortString();
            var family = FamilyKey(b.Code.Path);

            matrix.TryGetValue((sol, mat), out var n);
            matrix[(sol, mat)] = n + 1;

            if (!byFamily.TryGetValue(family, out var list))
            {
                list = new();
                byFamily[family] = list;
            }
            list.Add((id, code, sol, opaque, mat));

            if (sol == Solidity.FullCube) fullCubeCodes.Add(code);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Skygrid palette dump — generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine($"# pool size = {_pool.Count} (after EntityClass + creative + -raw + Unplaceable filters)");
        sb.AppendLine();

        // ── Section 1 — Solidity × Material matrix ────────────────────────
        sb.AppendLine("## 1. Solidity × Material matrix (counts)");
        sb.AppendLine();
        var allSol = new[] { Solidity.FullCube, Solidity.SolidTop, Solidity.Partial, Solidity.NoCollision };
        var allMat = Enum.GetValues<EnumBlockMaterial>();
        sb.Append("material".PadRight(12));
        foreach (var sol in allSol) sb.Append(sol.ToString().PadLeft(13));
        sb.AppendLine("    total");
        var matTotals = new Dictionary<EnumBlockMaterial, int>();
        var solTotals = new Dictionary<Solidity, int>();
        foreach (var mat in allMat)
        {
            int rowTotal = 0;
            sb.Append(mat.ToString().PadRight(12));
            foreach (var sol in allSol)
            {
                matrix.TryGetValue((sol, mat), out var n);
                sb.Append(n.ToString().PadLeft(13));
                rowTotal += n;
                solTotals[sol] = (solTotals.TryGetValue(sol, out var s) ? s : 0) + n;
            }
            sb.Append(rowTotal.ToString().PadLeft(9));
            sb.AppendLine();
            matTotals[mat] = rowTotal;
        }
        sb.Append("TOTAL".PadRight(12));
        int grand = 0;
        foreach (var sol in allSol)
        {
            int s = solTotals.TryGetValue(sol, out var sv) ? sv : 0;
            sb.Append(s.ToString().PadLeft(13));
            grand += s;
        }
        sb.Append(grand.ToString().PadLeft(9));
        sb.AppendLine();
        sb.AppendLine();

        // ── Section 2 — Per-family breakdown ──────────────────────────────
        sb.AppendLine("## 2. Per-family breakdown");
        sb.AppendLine("#    fullCube / solidTop / partial / noColl   — sample code");
        sb.AppendLine();
        foreach (var fam in byFamily.OrderByDescending(kv => kv.Value.Count))
        {
            var items = fam.Value;
            int nFull = items.Count(i => i.sol == Solidity.FullCube);
            int nTop  = items.Count(i => i.sol == Solidity.SolidTop);
            int nPart = items.Count(i => i.sol == Solidity.Partial);
            int nNone = items.Count(i => i.sol == Solidity.NoCollision);
            var sample = items[0].code;
            sb.AppendLine($"  {fam.Key,-22} total={items.Count,5}  full={nFull,5} top={nTop,4} part={nPart,4} none={nNone,4}   e.g. {sample}");
        }
        sb.AppendLine();

        // ── Section 3 — Full list of FullCube codes (one per line) ────────
        sb.AppendLine($"## 3. FullCube codes ({fullCubeCodes.Count}) — candidates for a 'solid only' palette");
        sb.AppendLine();
        fullCubeCodes.Sort(StringComparer.Ordinal);
        foreach (var c in fullCubeCodes) sb.AppendLine(c);

        sb.AppendLine();
        sb.AppendLine("## 4. Variant groups (size >= 2) — single palette slot per group, variant chosen at placement");
        sb.AppendLine();
        DumpGroupsSection(sb, "navigable", _navigableGroups);
        DumpGroupsSection(sb, "decorative", _decorativeGroups);
        DumpGroupsSection(sb, "liquid", _liquidGroups);

        DumpPlacementSimulation(sb);
        DumpContainerSimulation(sb);

        File.WriteAllText(outPath, sb.ToString());
        _sapi.Logger.Notification($"[skygrid] palette dump written to {outPath}  ({_pool.Count} blocks, {fullCubeCodes.Count} FullCube)");
    }

    private void DumpGroupsSection(StringBuilder sb, string label, List<int[]> groups)
    {
        var multi = groups.Where(g => g.Length >= 2).ToList();
        int singles = groups.Count - multi.Count;
        int variantsInMulti = multi.Sum(g => g.Length);
        sb.AppendLine($"### {label}: {groups.Count} groups total ({singles} singletons + {multi.Count} multi-groups covering {variantsInMulti} variants)");
        sb.AppendLine();
        foreach (var g in multi.OrderByDescending(x => x.Length))
        {
            var codes = g.Select(id => _sapi.World.Blocks[id]?.Code?.ToShortString() ?? $"<id {id}>").OrderBy(x => x, StringComparer.Ordinal).ToList();
            sb.AppendLine($"  [{g.Length}] {codes[0]}");
            for (int i = 1; i < codes.Count; i++) sb.AppendLine($"       {codes[i]}");
        }
        sb.AppendLine();
    }

    /// <summary>Synthesizes a large sample of <see cref="PickBlock"/> calls and tallies the result
    /// distribution. The simulation is deterministic per world seed (same as actual placement) so
    /// the counts faithfully predict what worldgen will scatter. Useful for spotting families that
    /// remain over-represented after grouping — they show up as the heaviest entries in the dump.</summary>
    private void DumpPlacementSimulation(StringBuilder sb)
    {
        // 400×400×60 block sample → 100×100×15 = 150 000 grid cells (negligible time).
        const int SimRadius = 200, SimVertical = 60;
        var counts = new Dictionary<int, long>();
        for (int x = -SimRadius; x < SimRadius; x += Spacing)
        for (int z = -SimRadius; z < SimRadius; z += Spacing)
        for (int y = MinY; y < MinY + SimVertical; y += Spacing)
        {
            int id = PickBlock(x, y, z);
            counts.TryGetValue(id, out var n);
            counts[id] = n + 1;
        }
        long total = counts.Values.Sum();
        sb.AppendLine($"## 5. Placement simulation — {total:N0} synthetic PickBlock samples over {SimRadius * 2}×{SimRadius * 2}×{SimVertical} blocks");
        sb.AppendLine("#    count   pct%  block-code");
        sb.AppendLine();
        var rows = counts
            .Select(kv => (id: kv.Key, count: kv.Value, code: _sapi.World.Blocks[kv.Key]?.Code?.ToShortString() ?? $"<id {kv.Key}>"))
            .ToList();
        sb.AppendLine($"### 5a. By count (descending) — {rows.Count} distinct blocks placed");
        sb.AppendLine();
        foreach (var r in rows.OrderByDescending(x => x.count))
        {
            double pct = 100.0 * r.count / total;
            sb.AppendLine($"  {r.count,8:N0}  {pct,5:F2}  {r.code}");
        }
        sb.AppendLine();
        sb.AppendLine("### 5b. By name (alphabetical) — same data, easier to scan for non-grouped families");
        sb.AppendLine();
        foreach (var r in rows.OrderBy(x => x.code, StringComparer.Ordinal))
        {
            double pct = 100.0 * r.count / total;
            sb.AppendLine($"  {r.count,8:N0}  {pct,5:F2}  {r.code}");
        }
        sb.AppendLine();
    }

    /// <summary>Simulates the per-column container picker across a range of chunk-columns at
    /// several distance bands from spawn, tallying tier frequencies. Reports the share of grid
    /// cells occupied by bonus containers (all tiers combined) and the per-tier breakdown.</summary>
    private void DumpContainerSimulation(StringBuilder sb)
    {
        // Sample 5000 chunk-columns at 4 distance bands (0, 500, 1500, 3000 blocks from spawn).
        // Each band uses the same seed sequence so cross-band comparison reflects only the
        // distance-weighted tier picker, not RNG variance.
        var bands = new[] { 0.0, 500.0, 1500.0, 3000.0 };
        const int ColsPerBand = 5000;
        int firstY = MinY + GameMath.Mod(-MinY, Spacing);
        int yBuckets = (MaxY - firstY) / Spacing + 1;
        int cellsPerColumn = (ChunkSize / Spacing) * (ChunkSize / Spacing) * yBuckets;

        sb.AppendLine($"## 6. Bonus container distribution");
        sb.AppendLine($"#    Per column: 1..{MaxChestsPerColumn} containers, avg {(1 + MaxChestsPerColumn) / 2.0}");
        sb.AppendLine($"#    Cells per column: {cellsPerColumn:N0}  ({ChunkSize / Spacing}×{ChunkSize / Spacing}×{yBuckets})");
        sb.AppendLine();
        sb.AppendLine("  distance   total-chests  per-col   % of cells   tier-shares-of-chests");
        foreach (var d in bands)
        {
            var tierCount = new long[SkygridLoot.Tiers.Length];
            long totalChests = 0;
            var rng = new Random(unchecked((int)_seedXor) ^ (int)d);
            for (int i = 0; i < ColsPerBand; i++)
            {
                int desired = 1 + rng.Next(MaxChestsPerColumn);
                for (int j = 0; j < desired; j++)
                {
                    int tier = SkygridLoot.PickTier(rng, d);
                    tierCount[tier]++;
                    totalChests++;
                }
            }
            double perCol = (double)totalChests / ColsPerBand;
            double cellPct = 100.0 * totalChests / ((double)cellsPerColumn * ColsPerBand);
            var shares = new List<string>();
            for (int t = 0; t < SkygridLoot.Tiers.Length; t++)
            {
                double tp = totalChests == 0 ? 0 : 100.0 * tierCount[t] / totalChests;
                shares.Add($"{SkygridLoot.Tiers[t].Name}={tp:F1}%");
            }
            sb.AppendLine($"  d={d,5:F0}b   {totalChests,12:N0}   {perCol,6:F2}   {cellPct,8:F3}%   {string.Join("  ", shares)}");
        }
        sb.AppendLine();
    }

    private static int[] ResolveTierBlockIds(ICoreServerAPI api)
    {
        var ids = new int[SkygridLoot.Tiers.Length];
        for (int i = 0; i < SkygridLoot.Tiers.Length; i++)
        {
            var t = SkygridLoot.Tiers[i];
            int id = 0;
            foreach (var code in t.BlockCodeChain)
            {
                var b = api.World.GetBlock(new AssetLocation(code));
                if (b != null && b.BlockId != 0) { id = b.BlockId; break; }
            }
            if (id == 0)
            {
                api.Logger.Warning($"[skygrid] tier '{t.Name}' resolved no block from chain: {string.Join(",", t.BlockCodeChain)}");
            }
            ids[i] = id;
        }
        return ids;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PALETTE
    // ─────────────────────────────────────────────────────────────────────────

    private static List<int> ResolveAllBlocks(ICoreServerAPI api)
    {
        var pool = new List<int>();
        int skippedEntity = 0, skippedCreative = 0, skippedRaw = 0, skippedUnplaceable = 0, skippedLiquidVariant = 0;
        int oreCount = 0, meteoriteCount = 0, looseOreCount = 0;
        foreach (var b in api.World.Blocks)
        {
            if (b == null || b.BlockId == 0 || b.Code == null) continue;
            if (b.EntityClass != null) { skippedEntity++; continue; }

            var path = b.Code.Path;
            if (IsSkippedLiquidVariant(b))
            {
                skippedLiquidVariant++;
                continue;
            }

            bool isCreative = false;
            foreach (var pref in CreativeBlockPathPrefixes)
            {
                if (path.StartsWith(pref)) { isCreative = true; break; }
            }
            if (isCreative) { skippedCreative++; continue; }

            // Skip raw / cracked clay items (storagevessel-*-raw, jug-raw, crock-raw, etc.).
            // Look unfinished in worldgen and many are GroundStorable-only.
            if (path.EndsWith("-raw")) { skippedRaw++; continue; }

            // Skip blocks marked Unplaceable (raw clay vessels, etc.) — they look unfinished and
            // many have GroundStorable-only interactions.
            if (b.HasBehavior<BlockBehaviorUnplaceable>()) { skippedUnplaceable++; continue; }

            pool.Add(b.BlockId);

            if (path.StartsWith("ore-"))      oreCount++;
            if (path.StartsWith("meteorite")) meteoriteCount++;
            if (path.StartsWith("looseores")) looseOreCount++;
        }

        api.Logger.Notification(
            $"[skygrid] palette: kept {pool.Count}, skipped {skippedEntity} entity + {skippedCreative} creative + {skippedRaw} -raw + {skippedUnplaceable} Unplaceable + {skippedLiquidVariant} liquid variants. " +
            $"Coverage: ores={oreCount}, meteorite-variants={meteoriteCount}, looseores={looseOreCount}.");

        var miron = api.World.GetBlock(new AssetLocation("meteorite-core-iron"));
        if (miron != null)
        {
            api.Logger.Notification($"[skygrid] meteoric iron: id={miron.BlockId} in_pool={pool.Contains(miron.BlockId)}");
        }
        return pool;
    }

    private static bool IsSkippedLiquidVariant(Block block)
    {
        if (block.Code == null) return false;
        if (block.LiquidCode == null) return false;
        return !LiquidVariantKeepList.Contains(block.Code.Path);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WORLDGEN HOOK
    // ─────────────────────────────────────────────────────────────────────────

    private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
    {
        if (_pool.Count == 0) return;

        var chunks = request.Chunks;
        if (chunks == null || chunks.Length == 0) return;

        int chunkX = request.ChunkX;
        int chunkZ = request.ChunkZ;
        try
        {
            GenColumn(chunks, chunkX, chunkZ);
        }
        catch (Exception ex)
        {
            // A single chunk failing must not abort the worldgen pass — otherwise the column
            // ships half-wiped/half-filled and you see "holes" in the grid.
            _sapi.Logger.Error($"[skygrid] worldgen failed at chunk ({chunkX},{chunkZ}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void CacheStoryStructureLocations()
    {
        _storyStructLocations = new List<Cuboidi>();
        _storyStructCenters = new Dictionary<string, BlockPos>();
        _storyStructBboxes = new Dictionary<string, Cuboidi?>();
        // "spawn" is a pseudo-structure used by the gate planner to anchor the first leg's source.
        var spawn = _sapi.World.DefaultSpawnPosition;
        _storyStructCenters["spawn"] = new BlockPos((int)spawn.X, (int)spawn.Y, (int)spawn.Z, 0);
        _storyStructBboxes["spawn"] = null; // no bbox — spawn uses fixed distance

        var sys = _sapi.ModLoader.GetModSystem<GenStoryStructures>();
        if (sys?.Structures == null)
        {
            _sapi.Logger.Warning("[skygrid] GenStoryStructures not available — story structures won't be preserved.");
            return;
        }
        foreach (var kv in sys.Structures)
        {
            if (kv.Value?.Location == null) continue;
            if (!PreservedStructureCodes.Contains(kv.Key)) continue;
            _storyStructLocations.Add(kv.Value.Location);
            if (kv.Value.CenterPos != null) _storyStructCenters[kv.Key] = kv.Value.CenterPos;
            _storyStructBboxes[kv.Key] = kv.Value.Location;
            var L = kv.Value.Location;
            _sapi.Logger.Notification(
                $"[skygrid] preserve story struct '{kv.Key}' bbox=({L.MinX},{L.MinY},{L.MinZ})..({L.MaxX},{L.MaxY},{L.MaxZ})");
        }
        _sapi.Logger.Notification($"[skygrid] cached {_storyStructLocations.Count} story structure(s) to preserve.");
    }

    /// <summary>
    /// True if this chunk's footprint overlaps the bounding box of any vanilla-placed structure
    /// (regular: translocators, dungeons, ruins; story: Resonance Archive, Devastation, etc.).
    /// We check the chunk's own region plus 3 corners' regions so a structure whose bbox straddles
    /// a region boundary (and is therefore registered in only one of the four) is still detected.
    /// </summary>
    private bool ChunkIntersectsAnyStructure(int chunkX, int chunkZ)
    {
        int wx0 = chunkX * ChunkSize;
        int wz0 = chunkZ * ChunkSize;
        int wx1 = wx0 + ChunkSize;
        int wz1 = wz0 + ChunkSize;

        // 1) Story structures registry (Resonance Archive, Devastation, Village, Lazaret, Tobias).
        //    Cheap — only a handful of entries.
        foreach (var L in _storyStructLocations)
        {
            if (L.MaxX < wx0 || L.MinX >= wx1) continue;
            if (L.MaxZ < wz0 || L.MinZ >= wz1) continue;
            return true;
        }

        // 2) Per-region GeneratedStructures (tiled dungeons + villages + worldgen structures).
        //    Filtered down to the whitelist inside CheckRegion.
        int regionSize = _sapi.WorldManager.RegionSize;
        if (regionSize <= 0) return false;

        long r00 = Key(wx0, wz0, regionSize);
        long r10 = Key(wx1 - 1, wz0, regionSize);
        long r01 = Key(wx0, wz1 - 1, regionSize);
        long r11 = Key(wx1 - 1, wz1 - 1, regionSize);

        if (CheckRegion(r00, wx0, wz0, wx1, wz1)) return true;
        if (r10 != r00 && CheckRegion(r10, wx0, wz0, wx1, wz1)) return true;
        if (r01 != r00 && r01 != r10 && CheckRegion(r01, wx0, wz0, wx1, wz1)) return true;
        if (r11 != r00 && r11 != r10 && r11 != r01 && CheckRegion(r11, wx0, wz0, wx1, wz1)) return true;
        return false;

        static long Key(int x, int z, int rs)
        {
            int rx = x / rs;
            int rz = z / rs;
            return ((long)rx << 32) | (uint)rz;
        }
    }

    private bool CheckRegion(long key, int wx0, int wz0, int wx1, int wz1)
    {
        int rx = (int)(key >> 32);
        int rz = (int)(key & 0xFFFFFFFFL);
        var region = _sapi.WorldManager.GetMapRegion(rx, rz);
        if (region == null || region.GeneratedStructures == null) return false;
        foreach (var struc in region.GeneratedStructures)
        {
            // Whitelist filter: only preserve chunks for the critical lore-arc structures.
            // Everything else (translocators, tiled dungeons, surface ruins, treasurehunter)
            // gets wiped to skygrid even if it's in the region's structure list.
            if (struc.Code == null || !PreservedStructureCodes.Contains(struc.Code)) continue;
            var L = struc.Location;
            if (L == null) continue;
            // Use MinX/MaxX/MinZ/MaxZ (Cuboidi can have X1>X2 depending on orientation).
            if (L.MaxX < wx0 || L.MinX >= wx1) continue;
            if (L.MaxZ < wz0 || L.MinZ >= wz1) continue;
            return true;
        }
        return false;
    }

    private void GenColumn(IServerChunk[] chunks, int chunkX, int chunkZ)
    {
        int totalY = chunks.Length * ChunkSize;
        int minY = Math.Max(1, MinY);
        int maxY = Math.Min(totalY - 2, MaxY);

        // If this chunk intersects any vanilla-placed structure (translocator, dungeon, ruin,
        // story structure like Resonance Archive / Devastation, etc.), skip the wipe entirely.
        // Those chunks keep vanilla terrain + lore content intact — they appear as "lost islands"
        // of normal world floating in the skygrid void, giving the player a destination to find.
        // We check the 4 corner regions to catch structures whose bbox straddles a region edge.
        if (ChunkIntersectsAnyStructure(chunkX, chunkZ)) return;

        // Wipe column to air. ClearBlocksAndPrepare initialises the chunkdata palette — required
        // before SetBlockUnsafe on chunk-Y slices that vanilla never touched (all-air bands above
        // terrain), which otherwise NRE inside ChunkDataLayer.SetUnsafe.
        // BlockEntities are cleared too: ClearBlocksAndPrepare only nukes the blocks layer, leaving
        // orphan BEs (forges, mech. parts) that crash the client renderer when blocks are gone.
        for (int chunkY = 0; chunkY < chunks.Length; chunkY++)
        {
            var data = chunks[chunkY].Data;
            data.ClearBlocksAndPrepare();
            for (int i = 0; i < data.Length; i++) data.SetFluid(i, 0);
            chunks[chunkY].BlockEntities?.Clear();
        }

        // Reset heightmaps. The light engine uses WorldGenTerrainHeightMap to decide where to STOP
        // dropping sky-light down a column — anything below that height gets sunlight=0 and stays
        // pitch dark unless a light source reaches it. We just wiped the whole column to air, so
        // the vanilla-computed heights (typically ~110 at sea level) are now lying: every wiped
        // cell below sea level renders black, even though it should be flooded with skylight.
        // Zeroing both maps tells the light engine "no terrain here", which lets sky-light
        // propagate all the way down past the sparse grid blocks.
        var mapchunk = chunks[0].MapChunk;
        if (mapchunk != null)
        {
            Array.Clear(mapchunk.WorldGenTerrainHeightMap, 0, mapchunk.WorldGenTerrainHeightMap.Length);
            Array.Clear(mapchunk.RainHeightMap, 0, mapchunk.RainHeightMap.Length);
        }

        // Place grid cells.
        int worldX0 = chunkX * ChunkSize;
        int worldZ0 = chunkZ * ChunkSize;
        int firstX = worldX0 + GameMath.Mod(-worldX0, Spacing);
        int firstZ = worldZ0 + GameMath.Mod(-worldZ0, Spacing);
        int firstY = minY    + GameMath.Mod(-minY, Spacing);

        // Per-column bonus container placement: every generated chunk-column gets at least one.
        long colHash = ColumnHash(chunkX, chunkZ);
        var colRng = new Random(unchecked((int)(colHash ^ (colHash >>> 32))));
        int yBuckets = (maxY - firstY) / Spacing + 1;
        int gridSide = ChunkSize / Spacing;
        // cellChest[i] encodes (tier+1) | (table<<8); 0 = no chest. Indexed by (yi*gridSide + zi)*gridSide + xi.
        // Replaces a linear scan over a 128-entry chestCells span (~225k comparisons/column at
        // MaxChestsPerColumn=128) with O(1) lookup per cell. yBuckets*64 ints ≈ 14 KB on stack.
        Span<int> cellChest = stackalloc int[yBuckets * gridSide * gridSide];
        double distanceFromSpawn = DistanceFromSpawn(worldX0, worldZ0);

        int desired = 1 + colRng.Next(MaxChestsPerColumn);
        for (int i = 0; i < desired; i++)
        {
            int tierIdx = SkygridLoot.PickTier(colRng, distanceFromSpawn);
            if (_tierBlockIds[tierIdx] == 0) continue;
            int tableIdx = SkygridLoot.PickTable(SkygridLoot.Tiers[tierIdx], colRng);
            int cx = colRng.Next(gridSide);
            int cz = colRng.Next(gridSide);
            int cy = colRng.Next(Math.Max(1, yBuckets));
            int cellIdx = (cy * gridSide + cz) * gridSide + cx;
            // First-write-wins to match the old linear-scan break-on-first-match semantics.
            if (cellChest[cellIdx] != 0) continue;
            cellChest[cellIdx] = (tierIdx + 1) | (tableIdx << 8);
        }

        int xi = 0, zi = 0, yi;
        for (int wx = firstX; wx < worldX0 + ChunkSize; wx += Spacing, xi++)
        {
            zi = 0;
            for (int wz = firstZ; wz < worldZ0 + ChunkSize; wz += Spacing, zi++)
            {
                yi = 0;
                for (int wy = firstY; wy <= maxY; wy += Spacing, yi++)
                {
                    int chunkY = wy / ChunkSize;
                    int lY = wy % ChunkSize;
                    int dx = wx - worldX0;
                    int dz = wz - worldZ0;
                    int idx = (ChunkSize * lY + dz) * ChunkSize + dx;

                    int packed = cellChest[(yi * gridSide + zi) * gridSide + xi];
                    if (packed != 0)
                    {
                        int matchedTier = (packed & 0xFF) - 1;
                        int matchedTable = (packed >> 8) & 0xFF;
                        chunks[chunkY].Data.SetBlockUnsafe(idx, 0);
                        _pendingChests.Enqueue((new BlockPos(wx, wy, wz, 0), matchedTier, matchedTable, 0));
                        System.Threading.Interlocked.Increment(ref _tierEnqueued[matchedTier]);
                    }
                    else
                    {
                        chunks[chunkY].Data.SetBlockUnsafe(idx, PickBlock(wx, wy, wz));
                    }
                }
            }
        }

        // Re-flood sunlight. Vanilla runs the sun-flood pass at Vegetation (execute order 0.95) and
        // the neighbour flood at NeighbourSunLightFlood — both BEFORE our PreDone hook. So when we
        // reach this point the chunk's sunlight grid was baked against the old vanilla terrain
        // (mostly solid → sunlight=0 everywhere below sea level). We just replaced that terrain
        // with sparse air-plus-grid; without recomputing, every wiped cell stays pitch black.
        // Calling SunFloodChunkColumnForWorldGen at the end re-propagates skylight against our
        // new block layout. The neighbour flood we leave to vanilla's separate pass.
        _sapi.WorldManager.SunFloodChunkColumnForWorldGen(chunks, chunkX, chunkZ);
        // Gate placement is handled by the ChunkColumnLoaded event (hooked at OnReady) — not here.
        // We can't rely on SetBlock during gen because the chunk isn't yet in the world map.
    }

    private long ColumnHash(int cx, int cz)
    {
        const long Phi = unchecked((long)0x9E3779B97F4A7C15UL);
        long h = (long)cx * 73856093L ^ (long)cz * 19349663L ^ _seedXor ^ Phi;
        h ^= h >>> 33; h *= -49064778989728563L;
        h ^= h >>> 33; h *= -4265267296991594537L;
        h ^= h >>> 33;
        return h;
    }

    private double DistanceFromSpawn(int worldX0, int worldZ0)
    {
        var spawn = _sapi.World.DefaultSpawnPosition;
        double centerX = worldX0 + ChunkSize * 0.5;
        double centerZ = worldZ0 + ChunkSize * 0.5;
        double dx = centerX - spawn.X;
        double dz = centerZ - spawn.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Partitions _pool into three subsets:
    ///   - navigable: FullCube + SolidTop solids (the bulk of grid cells, ~80%)
    ///   - decorative: Partial collision + non-liquid NoCollision (fences, plants, deco — ~10%)
    ///   - liquid: Water/Lava/Salt/Rapid/Boiling water/lava (~10%)
    /// Liquids used to be folded into navigable, but at 350 codes in a 2.7k navigable pool they
    /// were ~12% of cells — too sloshy. Splitting them out gives each accent type the same 10% mix.
    /// </summary>
    private void SplitPoolByNavigability()
    {
        var navByKey = new Dictionary<string, List<int>>();
        var decByKey = new Dictionary<string, List<int>>();
        var liqByKey = new Dictionary<string, List<int>>();
        _navigableGroups = new List<int[]>();
        _decorativeGroups = new List<int[]>();
        _liquidGroups = new List<int[]>();
        foreach (var id in _pool)
        {
            var b = _sapi.World.Blocks[id];
            if (b == null) continue;
            bool isLiquid = b.BlockMaterial == EnumBlockMaterial.Water
                         || b.BlockMaterial == EnumBlockMaterial.Lava;
            Dictionary<string, List<int>> targetKey;
            List<int[]> targetSingletons;
            if (isLiquid) { targetKey = liqByKey; targetSingletons = _liquidGroups; }
            else
            {
                var sol = ClassifySolidity(b);
                bool nav = sol == Solidity.FullCube || sol == Solidity.SolidTop;
                targetKey = nav ? navByKey : decByKey;
                targetSingletons = nav ? _navigableGroups : _decorativeGroups;
            }
            var key = CanonicalOreKey(b) ?? CanonicalCosmeticKey(b);
            if (key == null)
            {
                targetSingletons.Add(new[] { id });
                continue;
            }
            if (!targetKey.TryGetValue(key, out var list)) targetKey[key] = list = new List<int>();
            list.Add(id);
        }
        foreach (var kv in navByKey) _navigableGroups.Add(kv.Value.ToArray());
        foreach (var kv in decByKey) _decorativeGroups.Add(kv.Value.ToArray());
        foreach (var kv in liqByKey) _liquidGroups.Add(kv.Value.ToArray());
    }

    /// <summary>Canonical mineral key for ore-* and looseores-* blocks. Uses the VS SDK's parsed
    /// variant dictionary instead of re-parsing the path, which means we don't need a hard-coded
    /// rock-suffix table or grade-prefix list.</summary>
    private static string? CanonicalOreKey(Block block)
    {
        if (block.Code == null) return null;
        var family = block.FirstCodePart();
        var mineral = family == "ore"       ? block.Variant["type"]
                    : family == "looseores" ? block.Variant["ore"]
                    : null;
        return mineral == null ? null : family + "-" + mineral;
    }

    /// <summary>Canonical cosmetic group key for a block whose family is registered in
    /// <see cref="CosmeticGroupKeepSegments"/> (keep first N segments) or
    /// <see cref="CosmeticGroupKeepFamilyAndLast"/> (keep family + last segment). Returns null
    /// when the family isn't registered or the path is too short to collapse.</summary>
    private static string? CanonicalCosmeticKey(Block block)
    {
        if (block.Code == null) return null;
        var family = block.FirstCodePart();
        if (family == null) return null;
        var parts = block.Code.Path.Split('-');
        if (CosmeticGroupKeepFamilyAndLast.Contains(family))
        {
            return parts.Length < 3 ? null : family + "-" + parts[^1];
        }
        if (!CosmeticGroupKeepSegments.TryGetValue(family, out var keep)) return null;
        return parts.Length <= keep ? null : string.Join('-', parts, 0, keep);
    }

    private int PickBlock(int x, int y, int z)
    {
        long h = (long)x * 73856093L ^ (long)y * 19349663L ^ (long)z * 83492791L ^ _seedXor;
        h ^= h >>> 33; h *= -49064778989728563L;
        h ^= h >>> 33; h *= -4265267296991594537L;
        h ^= h >>> 33;
        ulong u = (ulong)h;
        // Low 100-bucket selects category (deterministic per worldseed × pos). Mid bits pick the
        // group inside the chosen pool, top bits pick the variant inside the group — independent
        // because splitmix64 avalanches every input bit.
        ulong bucket = u % 100UL;
        List<int[]> groups;
        if (bucket < (ulong)LiquidChancePercent && _liquidGroups.Count > 0)
            groups = _liquidGroups;
        else if (bucket < (ulong)(LiquidChancePercent + DecorativeChancePercent) && _decorativeGroups.Count > 0)
            groups = _decorativeGroups;
        else
            groups = _navigableGroups;
        if (groups.Count == 0) return _pool[(int)((u / 100UL) % (ulong)_pool.Count)]; // safety
        var group = groups[(int)((u / 100UL) % (ulong)groups.Count)];
        return group[(int)((u >> 40) % (ulong)group.Length)];
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CHEST BE SPAWN + LOOT FILL (post-worldgen tick)
    // ─────────────────────────────────────────────────────────────────────────

    private void DrainPendingChests(float dt)
    {
        // Retry deferred starter chests for players whose chunk wasn't ready at PlayerJoin.
        // Re-enqueues on miss until the chunk is writable; bails permanently if the player left.
        int starterBudget = 8;
        while (starterBudget-- > 0 && _pendingStarterChests.TryDequeue(out var p))
        {
            if (p.ConnectionState != EnumClientState.Playing) continue;
            if (!TryPlaceStarterChest(p)) _pendingStarterChests.Enqueue(p);
        }

        int budget = 32;
        while (budget-- > 0 && _pendingChests.TryDequeue(out var pending))
        {
            try
            {
                var ba = _sapi.World.BlockAccessor;
                int blockId = _tierBlockIds[pending.tierIdx];
                if (blockId == 0) continue;

                // Tiers flagged NeedsSupport (storagevessel: UnstableFalling) get a solid block
                // one Y below to stop them dropping into the void.
                if (_supportBlockId != 0 && SkygridLoot.Tiers[pending.tierIdx].NeedsSupport)
                {
                    var supportPos = new BlockPos(pending.pos.X, pending.pos.Y - 1, pending.pos.Z, 0);
                    var existing = ba.GetBlock(supportPos);
                    if (existing == null || existing.BlockId == 0)
                    {
                        ba.SetBlock(_supportBlockId, supportPos);
                    }
                }

                ba.SetBlock(blockId, pending.pos);

                var actual = ba.GetBlock(pending.pos);
                if (actual == null || actual.BlockId != blockId)
                {
                    // Likely the chunk wasn't fully ready yet — retry up to ChestPlaceRetries times.
                    if (pending.retries < ChestPlaceRetries)
                    {
                        _pendingChests.Enqueue((pending.pos, pending.tierIdx, pending.tableIdx, pending.retries + 1));
                    }
                    else
                    {
                        System.Threading.Interlocked.Increment(ref _tierMismatchOnPlace[pending.tierIdx]);
                        _sapi.Logger.Debug($"[skygrid] tier {pending.tierIdx} SetBlock at {pending.pos} gave up after {ChestPlaceRetries} retries (got id={actual?.BlockId ?? -1}).");
                    }
                    continue;
                }

                var be = ba.GetBlockEntity(pending.pos);
                if (be is BlockEntityContainer container)
                {
                    var tier = SkygridLoot.Tiers[pending.tierIdx];
                    var table = tier.Tables[pending.tableIdx];
                    FillFromTable(container, pending.pos, table);
                    System.Threading.Interlocked.Increment(ref _tierPlaced[pending.tierIdx]);
                    // Log T2/T3 positions for easier finding during dev. T0/T1 too spammy.
                    if (pending.tierIdx >= 2)
                    {
                        _sapi.Logger.Notification($"[skygrid] placed {tier.Name} at {pending.pos.X},{pending.pos.Y},{pending.pos.Z}");
                    }
                }
                else
                {
                    _sapi.Logger.Debug($"[skygrid] chest at {pending.pos} BE is not BlockEntityContainer ({be?.GetType().Name ?? "null"}).");
                }
            }
            catch (Exception ex)
            {
                _sapi.Logger.Warning($"[skygrid] DrainPendingChests at {pending.pos}: {ex.Message}");
            }
        }

        // Periodically log placement stats so we can see if some tier is being silently dropped.
        long now = _sapi.World.ElapsedMilliseconds;
        if (now - _lastDiagLogMs > 15000)
        {
            _lastDiagLogMs = now;
            int totalEnq = 0; foreach (var v in _tierEnqueued) totalEnq += v;
            if (totalEnq > 0)
            {
                _sapi.Logger.Notification(
                    $"[skygrid] chest placement counters (enqueued/placed/mismatch): " +
                    $"{FormatTierCounters()}, queue_depth={_pendingChests.Count}");
            }
        }
    }

    private string FormatTierCounters()
    {
        var parts = new string[SkygridLoot.Tiers.Length];
        for (int i = 0; i < SkygridLoot.Tiers.Length; i++)
        {
            string name = SkygridLoot.Tiers[i].Name.ToLowerInvariant();
            parts[i] = $"{name} {_tierEnqueued[i]}/{_tierPlaced[i]}/{_tierMismatchOnPlace[i]}";
        }
        return string.Join(", ", parts);
    }

    private void FillFromTable(BlockEntityContainer container, BlockPos pos, LootTable table)
    {
        var inv = container.Inventory;
        if (inv == null) return;

        var rng = new Random(unchecked((int)((long)pos.X * 73856093L ^ (long)pos.Y * 19349663L ^ (long)pos.Z * 83492791L ^ _seedXor)));

        // Distance-based loot scaling: more items the further you are from spawn (cap at LootScaleCapDistance).
        var spawn = _sapi.World.DefaultSpawnPosition;
        double dx = pos.X - spawn.X;
        double dz = pos.Z - spawn.Z;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        double scale = 1.0 + Math.Min(1.0, distance / LootScaleCapDistance) * (LootScaleMaxMultiplier - 1.0);

        int scaledMin = (int)Math.Round(table.MinPicks * scale);
        int scaledMax = (int)Math.Round(table.MaxPicks * scale);
        if (scaledMax < scaledMin) scaledMax = scaledMin;
        int picks = Math.Min(inv.Count, scaledMin + rng.Next(scaledMax - scaledMin + 1));

        // Shuffle-pick distinct entries from the pool.
        var chosen = new HashSet<int>();
        int slot = 0;
        int safety = picks * 8;
        while (chosen.Count < picks && slot < inv.Count && safety-- > 0)
        {
            int idx = rng.Next(table.Pool.Length);
            if (!chosen.Add(idx)) continue;
            var entry = table.Pool[idx];
            int qty = entry.MinQty + rng.Next(entry.MaxQty - entry.MinQty + 1);
            var stack = MakeStack(entry.Code, qty);
            if (stack != null)
            {
                inv[slot].Itemstack = stack;
                inv.MarkSlotDirty(slot);
                slot++;
            }
        }
        container.MarkDirty(true);
    }

    private ItemStack? MakeStack(string code, int qty)
    {
        var loc = new AssetLocation(code);
        var block = _sapi.World.GetBlock(loc);
        if (block != null && block.BlockId != 0) return new ItemStack(block, qty);
        var item = _sapi.World.GetItem(loc);
        if (item != null) return new ItemStack(item, qty);
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PLAYER JOIN — STARTER CHEST  (uses Tier 0 / Basket loot table)
    // ─────────────────────────────────────────────────────────────────────────

    private void OnPlayerJoin(IServerPlayer player)
    {
        if (_tierBlockIds.Length == 0 || _tierBlockIds[0] == 0) return;
        var saved = _sapi.WorldManager.SaveGame.GetData(SpawnChestKeyPrefix + player.PlayerUID);
        if (saved != null && saved.Length > 0 && saved[0] != 0) return;
        // Attempt now; if the player's chunk isn't yet writable, the tick listener retries.
        if (!TryPlaceStarterChest(player)) _pendingStarterChests.Enqueue(player);
    }

    private bool TryPlaceStarterChest(IServerPlayer player)
    {
        var p = player.Entity?.Pos;
        if (p == null) return false;
        var chestPos = new BlockPos((int)Math.Floor(p.X) + 1, (int)Math.Floor(p.Y), (int)Math.Floor(p.Z), 0);
        var ba = _sapi.World.BlockAccessor;
        if (ba.GetChunkAtBlockPos(chestPos) == null) return false;

        ba.SetBlock(_tierBlockIds[0], chestPos);
        if (ba.GetBlockEntity(chestPos) is not BlockEntityContainer container) return false;

        FillFromTable(container, chestPos, SkygridLoot.StarterChest);
        _sapi.WorldManager.SaveGame.StoreData(SpawnChestKeyPrefix + player.PlayerUID, new byte[] { 1 });
        player.SendMessage(GlobalConstants.GeneralChatGroup,
            "Welcome to Skygrid. A starter basket has been placed next to you. Translocator gates ~250 blocks out lead to the lazaret and the resonance archive.",
            EnumChatType.Notification);
        return true;
    }
}
