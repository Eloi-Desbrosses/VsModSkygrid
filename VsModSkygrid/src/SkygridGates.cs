using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace VsModSkygrid;

/// <summary>
/// Pre-fixed translocator network linking spawn + the 5 preserved story structures.
/// Each "leg" gets K source gates scattered 50-100 blocks around the source's bbox, all pointing
/// to ONE arrival gate placed 50-100 blocks from the destination's bbox. The arrival gate is
/// itself a return gate, pointing back to a single coord near the source. All gates pre-repaired
/// (canTele=true, repairState=4, findNextChunk=false) so the player can use them on contact.
///
/// Why this exists: in skygrid the player can't walk 8 000 blocks of void to find the lazaret,
/// so we manually wire the lore-canonical progression (spawn → lazaret → village → tobiascave →
/// devastation, with archive as a side branch) into a discoverable gate network.
/// </summary>
internal sealed class SkygridGates
{
    private const int GatesPerLeg = 5;
    /// <summary>Default distance from a structure's bbox at which gates spawn (bbox half-extent +
    /// this buffer). Applied when the code isn't in <see cref="FixedRingDistance"/>.</summary>
    private const int MinDistance = 20;
    private const int MaxDistance = 60;
    /// <summary>Per-code fixed ring radius — distance from the structure's CenterPos at which the
    /// 5 gates are placed in a circle. Used for codes where the CenterPos is the natural anchor
    /// (spawn = no bbox; lazaret = small structure, ring from center is fine).</summary>
    private static readonly Dictionary<string, int> FixedRingDistance = new()
    {
        { "spawn",    200 },
        { "lazaret",  100 },
    };

    /// <summary>Per-code fixed bbox buffer — places gates at bbox edge + this buffer along the
    /// chosen angle. Used for structures where the bbox is the natural anchor (large or
    /// irregular footprint). All listed here use 100 blocks beyond the bbox edge.</summary>
    private static readonly Dictionary<string, int> FixedBboxBuffer = new()
    {
        { "resonancearchive",  100 },
        { "village",           100 },
        { "tobiascave",        100 },
        { "devastationarea",   100 },
    };

    private static readonly string[] Facings = { "north", "east", "south", "west" };

    /// <summary>Each leg: from-code → to-code. "spawn" is a virtual code resolved to world spawn pos.</summary>
    private static readonly (string From, string To)[] Legs =
    {
        ("spawn",       "lazaret"),
        ("spawn",       "resonancearchive"),
        ("lazaret",     "village"),
        ("village",     "tobiascave"),
        ("tobiascave",  "devastationarea"),
    };

    private readonly ICoreServerAPI _sapi;
    private int _translocatorBlockIdNorth;
    /// <summary>Maximally bright block used for the 3x3 gate platform: tries creativelight-79
    /// (white, lightHsv=[0,0,22], glowLevel=255), then paperlantern-on (yellow, light=21), then
    /// rock-granite as plain fallback if neither exists.</summary>
    private int _platformBlockId;

    /// <summary>chunk-key (x|z) → planned gates whose source position falls in that chunk.</summary>
    private readonly Dictionary<long, List<PlannedGate>> _byChunk = new();
    /// <summary>Set of "placed" markers (BlockPos packed as long) — persisted to savegame data.</summary>
    private readonly HashSet<long> _placedKeys = new();
    private const string SaveKey = "vsmodskygrid.gatesplaced";

    private sealed class PlannedGate
    {
        public BlockPos Source = null!;
        public BlockPos Target = null!;
        public string Facing = "north";
        public string Note = "";
    }

    public SkygridGates(ICoreServerAPI sapi) => _sapi = sapi;

    /// <summary>Resolves block ids + computes the planned gate positions. Call after the story
    /// structure registry is loaded (after <c>_storyStructLocations</c> is cached in the parent system).</summary>
    public void Plan(
        Dictionary<string, BlockPos> structureCenters,
        Dictionary<string, Cuboidi?> structureBboxes)
    {
        // Resolve blocks. Translocator code uses {side} variant — we resolve the 4 facings via
        // facing-keyed string and pick the right one per gate. "north" id is the canonical one we
        // store, the others are resolved on demand below.
        _translocatorBlockIdNorth = _sapi.World.GetBlock(new AssetLocation("statictranslocator-normal-north"))?.BlockId ?? 0;
        if (_translocatorBlockIdNorth == 0)
        {
            _sapi.Logger.Warning("[skygrid-gates] statictranslocator block not found — gate network disabled.");
            return;
        }
        // Pick brightest available block for the platform.
        _platformBlockId = _sapi.World.GetBlock(new AssetLocation("creativelight-79"))?.BlockId ?? 0;
        string platformPick = "creativelight-79";
        if (_platformBlockId == 0)
        {
            _platformBlockId = _sapi.World.GetBlock(new AssetLocation("paperlantern-on"))?.BlockId ?? 0;
            platformPick = "paperlantern-on";
        }
        if (_platformBlockId == 0)
        {
            _platformBlockId = _sapi.World.GetBlock(new AssetLocation("rock-granite"))?.BlockId ?? 0;
            platformPick = "rock-granite (no-light fallback)";
        }
        _sapi.Logger.Notification($"[skygrid-gates] platform block = {platformPick} (id={_platformBlockId})");

        // Restore placed markers from savedata.
        var saved = _sapi.WorldManager.SaveGame.GetData<List<long>>(SaveKey);
        if (saved != null) foreach (var k in saved) _placedKeys.Add(k);

        // Deterministic-but-varied per-world seed: derive from world seed.
        long seed = _sapi.World.Seed ^ 0x53_4B_59_47_47_41_54_45L; // "SKYGGATE"
        var rng = new Random((int)(seed ^ (seed >>> 32)));

        int planned = 0;
        foreach (var leg in Legs)
        {
            if (!structureCenters.TryGetValue(leg.From, out var src))
            {
                _sapi.Logger.Warning($"[skygrid-gates] leg '{leg.From}->{leg.To}' skipped: source '{leg.From}' has no center.");
                continue;
            }
            if (!structureCenters.TryGetValue(leg.To, out var dst))
            {
                _sapi.Logger.Warning($"[skygrid-gates] leg '{leg.From}->{leg.To}' skipped: dest '{leg.To}' has no center.");
                continue;
            }

            structureBboxes.TryGetValue(leg.From, out var srcBbox);
            structureBboxes.TryGetValue(leg.To,   out var dstBbox);

            // K paired source/exit gates. Each source has its own dedicated exit, and each exit
            // gate points back to its specific source (full bidirectional pairs). The player can
            // pick any of the 5 source gates near A and arrive at the matching exit near B.
            for (int i = 0; i < GatesPerLeg; i++)
            {
                var sourcePos = PickPositionAround(src, srcBbox, rng, leg.From);
                var exitPos   = PickPositionAround(dst, dstBbox, rng, leg.To);
                AddPlanned(sourcePos, exitPos,   $"{leg.From}->{leg.To} #{i+1}");
                AddPlanned(exitPos,   sourcePos, $"{leg.To}->{leg.From} #{i+1}");
                planned += 2;
            }
        }
        _totalPlanned = planned;
        _sapi.Logger.Notification(
            $"[skygrid-gates] planned {planned} gates across {Legs.Length} legs ({GatesPerLeg} bidirectional pairs each, {GatesPerLeg * 2} per leg). " +
            $"already-placed markers loaded: {_placedKeys.Count}");
    }

    private BlockPos PickPositionAround(BlockPos center, Cuboidi? bbox, Random rng, string code)
    {
        double angle = rng.NextDouble() * Math.PI * 2;

        int dist;
        if (FixedRingDistance.TryGetValue(code, out var fixedDist))
        {
            dist = fixedDist;
        }
        else
        {
            int edgeReach = 0;
            if (bbox != null)
            {
                int halfX = (bbox.MaxX - bbox.MinX) / 2;
                int halfZ = (bbox.MaxZ - bbox.MinZ) / 2;
                edgeReach = (int)Math.Ceiling(halfX * Math.Abs(Math.Cos(angle)) + halfZ * Math.Abs(Math.Sin(angle)));
            }
            int buffer = (bbox != null && FixedBboxBuffer.TryGetValue(code, out var fixedBuffer))
                ? fixedBuffer
                : MinDistance + rng.Next(MaxDistance - MinDistance + 1);
            dist = edgeReach + buffer;
        }

        int dx = (int)Math.Round(Math.Cos(angle) * dist);
        int dz = (int)Math.Round(Math.Sin(angle) * dist);
        // Force every gate to sea level so arrivals are predictable across the whole network (the
        // archive's CenterPos is underground; without this the player lands inside a wall).
        int y = GameMath.Mod(_sapi.World.SeaLevel - VsModSkygridSystem.MinY, VsModSkygridSystem.Spacing);
        y = _sapi.World.SeaLevel - y; // snap down to nearest grid Y at sea level
        return new BlockPos(center.X + dx, y, center.Z + dz, 0);
    }

    private void AddPlanned(BlockPos source, BlockPos target, string note)
    {
        int chunkX = source.X / 32;
        int chunkZ = source.Z / 32;
        long key = ((long)chunkX << 32) | (uint)chunkZ;
        if (!_byChunk.TryGetValue(key, out var list))
        {
            list = new List<PlannedGate>();
            _byChunk[key] = list;
        }
        list.Add(new PlannedGate
        {
            Source = source,
            Target = target,
            Facing = Facings[(Math.Abs(source.X) + Math.Abs(source.Z)) % 4],
            Note = note,
        });
    }

    /// <summary>Chunks we've already asked the load priority queue to fetch — prevents double-queueing.</summary>
    private readonly HashSet<long> _loadRequested = new();
    private int _totalPlanned;
    /// <summary>Trickled per tick to avoid bursting the worldgen queue when force-loading 50 chunks.</summary>
    private const int ForceLoadPerTick = 3;

    /// <summary>Periodic tick: (1) trickle force-load requests at <see cref="ForceLoadPerTick"/>
    /// per tick to avoid worldgen-queue bursts; (2) sweep every unplaced gate, place those whose
    /// chunk is now addressable. Idempotent via savedata-tracked <see cref="_placedKeys"/>.</summary>
    public void TickRetry(float dt)
    {
        if (_translocatorBlockIdNorth == 0) return;
        // All work done — listener can short-circuit forever after.
        if (_placedKeys.Count >= _totalPlanned && _loadRequested.Count >= _byChunk.Count) return;

        // Trickle force-load requests so distant chunks become accessible without bursting worldgen.
        int loadsThisTick = 0;
        foreach (var key in _byChunk.Keys)
        {
            if (loadsThisTick >= ForceLoadPerTick) break;
            if (!_loadRequested.Add(key)) continue;
            int chunkX = (int)(key >> 32);
            int chunkZ = (int)(key & 0xFFFFFFFFL);
            _sapi.WorldManager.LoadChunkColumnPriority(chunkX, chunkZ);
            loadsThisTick++;
        }

        bool dirty = false;
        foreach (var planned in _byChunk.Values)
        {
            foreach (var p in planned)
            {
                long mark = PosKey(p.Source);
                if (_placedKeys.Contains(mark)) continue;
                try
                {
                    if (PlaceOnePlannedGate(p))
                    {
                        _placedKeys.Add(mark);
                        dirty = true;
                        _sapi.Logger.Notification(
                            $"[skygrid-gates] placed {p.Note} at ({p.Source.X},{p.Source.Y},{p.Source.Z}) → ({p.Target.X},{p.Target.Y},{p.Target.Z})");
                    }
                }
                catch (Exception ex)
                {
                    _sapi.Logger.Warning($"[skygrid-gates] place {p.Note} at {p.Source} crashed: {ex.Message}");
                }
            }
        }
        if (dirty) _sapi.WorldManager.SaveGame.StoreData(SaveKey, new List<long>(_placedKeys));
    }

    private bool PlaceOnePlannedGate(PlannedGate p)
    {
        var ba = _sapi.World.BlockAccessor;

        // ChunkColumnLoaded fires before the chunk is registered in the live block accessor, so
        // SetBlock can silently no-op. TickRetry retries until the chunk is addressable.
        if (ba.GetChunkAtBlockPos(p.Source) == null) return false;

        // Platform 3x3 of creativelight-79 at Y-1; verify the write took by reading back the center.
        var platformY = p.Source.Y - 1;
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            ba.SetBlock(_platformBlockId, new BlockPos(p.Source.X + dx, platformY, p.Source.Z + dz, 0));
        }
        if (ba.GetBlock(new BlockPos(p.Source.X, platformY, p.Source.Z, 0))?.BlockId != _platformBlockId) return false;

        // Gate block at center.
        var gateBlock = _sapi.World.GetBlock(new AssetLocation($"statictranslocator-normal-{p.Facing}"));
        int gateId = gateBlock?.BlockId ?? _translocatorBlockIdNorth;
        ba.SetBlock(gateId, p.Source);

        var actual = ba.GetBlock(p.Source);
        if (actual == null || actual.BlockId != gateId)
        {
            _sapi.Logger.Warning(
                $"[skygrid-gates] {p.Note}: gate SetBlock failed. got id={actual?.BlockId ?? -1} ({actual?.Code}) expected {gateId} ({gateBlock?.Code})");
            return false;
        }

        var be = ba.GetBlockEntity(p.Source);
        if (be == null)
        {
            _sapi.Logger.Warning($"[skygrid-gates] {p.Note}: block placed (id={gateId}, code={actual.Code}) but BE is null. EntityClass={actual.EntityClass ?? "(none)"}");
            return false;
        }
        if (be is not BlockEntityStaticTranslocator beTl)
        {
            _sapi.Logger.Warning($"[skygrid-gates] {p.Note}: BE wrong type: {be.GetType().Name}");
            return false;
        }

        var tree = new TreeAttribute();
        beTl.ToTreeAttributes(tree);
        tree.SetBool("canTele", true);
        tree.SetInt("repairState", 4);
        tree.SetBool("findNextChunk", false);
        tree.SetBool("activated", true);
        tree.SetBool("tpLocationIsOffset", false);
        tree.SetInt("teleX", p.Target.X);
        tree.SetInt("teleY", p.Target.Y);
        tree.SetInt("teleZ", p.Target.Z);
        beTl.FromTreeAttributes(tree, _sapi.World);
        // Initialize() only calls setupGameTickers() when FullyRepaired at BE-creation. SetBlock
        // auto-spawns the BE with repairState=0, so the ticker is never registered and the gate
        // is inert. Call it explicitly now that the tree patch has flipped FullyRepaired=true.
        beTl.setupGameTickers();
        beTl.MarkDirty(true);
        return true;
    }

    private static long PosKey(BlockPos p) => ((long)p.X << 40) ^ ((long)p.Z << 8) ^ p.Y;
}
