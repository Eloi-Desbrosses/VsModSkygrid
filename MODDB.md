# Mod DB submission — Skygrid 0.2.0

Paste the content below into the Vintage Story mod DB submission form at
<https://mods.vintagestory.at/show/mod/new>.

---

## Form fields

| Field                 | Value                                                                  |
| --------------------- | ---------------------------------------------------------------------- |
| **Name**              | `Skygrid`                                                              |
| **Modid**             | `vsmodskygrid` (auto-detected from `modinfo.json`)                     |
| **Type**              | `Mod` (code mod)                                                       |
| **Side**              | `Server` (no client install needed)                                    |
| **Game version**      | `1.22.2` (tag the latest 1.22.x your mod was tested on)                |
| **Source code URL**   | `https://github.com/Eloi-Desbrosses/VsModSkygrid`                      |
| **Issue tracker URL** | `https://github.com/Eloi-Desbrosses/VsModSkygrid/issues`               |
| **License**           | `MIT`                                                                  |
| **Tags**              | `worldgen`, `adventure`, `survival`, `loot`, `server`                  |

---

## Short summary (1–2 sentences, shown in mod listings)

> A port of the iconic Minecraft Skygrid worldgen to Vintage Story 1.22.2. The whole world becomes a 3D grid of random blocks separated by air, with the 5 vanilla story structures preserved as vintage islands linked by a translocator-gate network.

---

## Long description (Markdown — paste in the description box)

```markdown
# Skygrid

A port of the iconic Minecraft **Skygrid** worldgen to Vintage Story 1.22.2.

The whole world becomes a 3D grid of random blocks separated by air. You start
on a single suspended cell with a basket at your feet and must climb, bridge,
and salvage your way across the void to scattered loot containers and the
vintage's story structures, now floating as preserved islands.

> 📡 **Server-only — no client install needed.** Drop the zip into your server's
> `Mods/` folder and your players can connect with vanilla 1.22.2.

## Features

- **3D skygrid worldgen**: 1 block every 4 in each axis, vertical band Y = 30 … 250.
- **Navigable palette**: candidate blocks are split into navigable (87 %),
  decorative (10 %), and liquid (3 %) pools so the grid stays climbable
  without sloshing.
- **Ore-variant balance**: gold / silver / uranium / etc. collapse to a single
  palette slot, so the ~52 host-rock × grade variants don't drown the pool —
  one rolls, a random variant is placed.
- **5 vanilla story structures preserved** as floating vintage islands:
  *lazaret*, *village*, *tobiascave*, *devastationarea*, *resonancearchive*.
- **Translocator-gate network** linking spawn to each structure: 5 bidirectional
  pairs per leg, all gates pre-repaired, lit by `creativelight-79` platforms,
  snapped to sea level.
- **Bonus containers everywhere**: 1–128 per chunk-column (avg 64 ≈ 1.8 % of
  grid cells). 5 tiers — Basket, Chest, Trunk, Animal Chest, Vessel — with
  distance-weighted bias: baskets / chests near spawn, treasure vessels far away.
- **Per-player starter basket** dropped at first join, persisted in savegame
  data so it's one-shot per player.

## Container tiers

| #   | Tier             | Block                                    | Default tables                                                       |
| --- | ---------------- | ---------------------------------------- | -------------------------------------------------------------------- |
| 0   | **Basket**       | `stationarybasket-east`                  | Survival Starter (food, wood, clay, cattail/papyrus, seeds, flint)   |
| 1   | **Chest**        | `chest-east`                             | Tool / Food / Seeds / Early Metal / Building Materials               |
| 2   | **Trunk**        | `trunk-east`                             | Forge / Bloomery / Storage / Workshop kits                           |
| 3   | **Animal Chest** | `chest-east` (creature spawns)           | Passive (chicken/pig/sheep/goat/hare) / Aggressive (wolf/hyena/bear) |
| 4   | **Vessel**       | `storagevessel-*-fired`                  | Refined Metal / Gems & Rare / Mechanical Power                       |

Distance-weighted tier rolls:
- **Near spawn (< 1 000 b)**: baskets, chests, animals dominate.
- **Mid-range (1 000–2 000 b)**: chests + trunks (forges, bloomeries, workshops).
- **Far (> 2 000 b)**: 60 % chance Vessel — refined metals, gems, mechanical power.

Containers also get a distance-based item-count multiplier: 1× near spawn,
scaling up to 2× past 5 000 blocks.

## Translocator-gate network

In a skygrid you can't walk 8 000 blocks of void to reach the lazaret, so the
mod wires the canonical progression into a pre-fixed gate network:

```
spawn ─── lazaret ─── village ─── tobiascave ─── devastationarea
   │
   └─── resonancearchive
```

**Per leg:** 5 bidirectional pairs (10 gates). All gates are pre-repaired
(`canTele=true, repairState=4`) — walk in, it works. Each gate sits on a
3×3 platform of `creativelight-79` (max light) for visibility from the void.

| Code               | Distance rule                                              |
| ------------------ | ---------------------------------------------------------- |
| `spawn`            | Fixed ring **200 blocks** from world spawn                 |
| `lazaret`          | Fixed ring **100 blocks** from center                      |
| `resonancearchive` | **100 blocks beyond bbox edge** (~200×170 underground)     |
| `village`          | **100 blocks beyond bbox edge**                            |
| `tobiascave`       | **100 blocks beyond bbox edge**                            |
| `devastationarea`  | **100 blocks beyond bbox edge**                            |

## Installation

1. Download `vsmodskygrid-0.2.0.zip` from this page (or from the
   [GitHub release](https://github.com/Eloi-Desbrosses/VsModSkygrid/releases)).
2. Drop the zip into your VS server's `Mods/` folder.
3. **Start a new world** — skygrid only generates new chunks, it does not
   convert existing terrain.
4. Players can connect with a stock Vintage Story 1.22.2 client. No client
   mod required.

## Source & issues

- **Source code:** <https://github.com/Eloi-Desbrosses/VsModSkygrid>
- **Issue tracker:** <https://github.com/Eloi-Desbrosses/VsModSkygrid/issues>
- **License:** MIT

## Screenshots

*[attach screenshots via the mod DB form — gate platform at sea level, a
storage vessel in the void, an approach to one of the floating story
structures]*
```

---

## Changelog (paste into the version notes box)

```markdown
### v0.2.0 — palette balance + container density (2026-05-18)

**Worldgen palette**
- Liquid roll dropped 10 % → 3 % (water/lava felt over-represented).
- Block variants now collapse to canonical groups: a single palette slot
  picks one block-id, then a random variant is chosen at placement. Stops
  gold/silver/uranium ores (~52 variants each) and the cosmetic axes of
  fences/stairs/slabs from drowning the pool.
- ~50 cosmetic families collapse orientation/state/density axes;
  `leaves` / `leavesbranchy` use an inverted-pattern rule.

**Bonus container density**
- `MaxChestsPerColumn` raised 16 → 128 (avg ~64/col, ~1.8 % of grid cells
  are containers). Old 0.24 % felt too sparse.
- Per-cell match against in-column container list now uses an O(1)
  `Span<int>` lookup instead of a linear scan — keeps worldgen fast at
  the new max.

**Diagnostics**
- Palette dump (`<data>/Logs/skygrid-palette.txt`) gains three new
  sections: variant groups, synthetic `PickBlock` distribution sample,
  per-distance-band container tier shares.

### v0.1.0 — initial release (2026-05-17)

- Skygrid worldgen (3D grid, 1 block every 4, Y = 30..250).
- 5 vanilla story structures preserved as floating islands.
- Translocator-gate network linking spawn to each structure.
- 5-tier scattered loot containers + per-player starter basket.
- Server-only — no client install needed.
```

---

## Submission checklist

- [ ] Logged in to <https://mods.vintagestory.at>
- [ ] Click *Add Mod*, paste the **Name / Side / Game version** from the table
- [ ] Paste the **Long description** Markdown into the description editor
- [ ] Set **Source / Issue / License** URLs
- [ ] Pick tags: `worldgen`, `adventure`, `survival`, `loot`, `server`
- [ ] Upload `dist/vsmodskygrid-0.2.0.zip` as the first file
- [ ] In the *Release* section: set version `0.2.0`, paste the **Changelog**,
      mark compatibility with **1.22.2**
- [ ] Attach screenshots (gate platform, story-structure approach, vessel in
      the void)
- [ ] Submit and check that the public page renders Markdown correctly
