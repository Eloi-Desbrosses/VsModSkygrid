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

## Long description (HTML — paste into the description WYSIWYG)

> Most VS mod DB WYSIWYG editors accept pasted HTML. Copy the block below verbatim.

```html
<p>A port of the iconic Minecraft <strong>Skygrid</strong> worldgen to Vintage Story 1.22.2.</p>

<p>The whole world becomes a 3D grid of random blocks separated by air. You start on a single suspended cell with a basket at your feet and must climb, bridge, and salvage your way across the void to scattered loot containers and the vintage's story structures, now floating as preserved islands.</p>

<blockquote><strong>Server-only — no client install needed.</strong> Drop the zip into your server's <code>Mods/</code> folder and your players can connect with vanilla 1.22.2.</blockquote>

<h2>Features</h2>
<ul>
  <li><strong>3D skygrid worldgen</strong>: 1 block every 4 in each axis, vertical band Y = 30 … 250.</li>
  <li><strong>Navigable palette</strong>: candidate blocks are split into navigable (87 %), decorative (10 %), and liquid (3 %) pools so the grid stays climbable without sloshing.</li>
  <li><strong>Ore-variant balance</strong>: gold / silver / uranium / etc. collapse to a single palette slot, so the ~52 host-rock × grade variants don't drown the pool — one rolls, a random variant is placed.</li>
  <li><strong>5 vanilla story structures preserved</strong> as floating vintage islands: <em>lazaret</em>, <em>village</em>, <em>tobiascave</em>, <em>devastationarea</em>, <em>resonancearchive</em>.</li>
  <li><strong>Translocator-gate network</strong> linking spawn to each structure: 5 bidirectional pairs per leg, all gates pre-repaired, lit by <code>creativelight-79</code> platforms, snapped to sea level.</li>
  <li><strong>Bonus containers everywhere</strong>: 1–128 per chunk-column (avg 64 ≈ 1.8 % of grid cells). 5 tiers — Basket, Chest, Trunk, Animal Chest, Vessel — with distance-weighted bias: baskets / chests near spawn, treasure vessels far away.</li>
  <li><strong>Per-player starter basket</strong> dropped at first join, persisted in savegame data so it's one-shot per player.</li>
</ul>

<h2>Container tiers</h2>
<table>
  <thead>
    <tr><th>#</th><th>Tier</th><th>Block</th><th>Default tables</th></tr>
  </thead>
  <tbody>
    <tr><td>0</td><td><strong>Basket</strong></td><td><code>stationarybasket-east</code></td><td>Survival Starter (food, wood, clay, cattail/papyrus, seeds, flint)</td></tr>
    <tr><td>1</td><td><strong>Chest</strong></td><td><code>chest-east</code></td><td>Tool / Food / Seeds / Early Metal / Building Materials</td></tr>
    <tr><td>2</td><td><strong>Trunk</strong></td><td><code>trunk-east</code></td><td>Forge / Bloomery / Storage / Workshop kits</td></tr>
    <tr><td>3</td><td><strong>Animal Chest</strong></td><td><code>chest-east</code> (creature spawns)</td><td>Passive (chicken/pig/sheep/goat/hare) / Aggressive (wolf/hyena/bear)</td></tr>
    <tr><td>4</td><td><strong>Vessel</strong></td><td><code>storagevessel-*-fired</code></td><td>Refined Metal / Gems &amp; Rare / Mechanical Power</td></tr>
  </tbody>
</table>

<p>Distance-weighted tier rolls:</p>
<ul>
  <li><strong>Near spawn (&lt; 1 000 b)</strong>: baskets, chests, animals dominate.</li>
  <li><strong>Mid-range (1 000–2 000 b)</strong>: chests + trunks (forges, bloomeries, workshops).</li>
  <li><strong>Far (&gt; 2 000 b)</strong>: 60 % chance Vessel — refined metals, gems, mechanical power.</li>
</ul>

<p>Containers also get a distance-based item-count multiplier: 1× near spawn, scaling up to 2× past 5 000 blocks.</p>

<h2>Translocator-gate network</h2>
<p>In a skygrid you can't walk 8 000 blocks of void to reach the lazaret, so the mod wires the canonical progression into a pre-fixed gate network:</p>

<pre><code>spawn ─── lazaret ─── village ─── tobiascave ─── devastationarea
   │
   └─── resonancearchive
</code></pre>

<p><strong>Per leg:</strong> 5 bidirectional pairs (10 gates). All gates are pre-repaired (<code>canTele=true, repairState=4</code>) — walk in, it works. Each gate sits on a 3×3 platform of <code>creativelight-79</code> (max light) for visibility from the void.</p>

<table>
  <thead>
    <tr><th>Code</th><th>Distance rule</th></tr>
  </thead>
  <tbody>
    <tr><td><code>spawn</code></td><td>Fixed ring <strong>200 blocks</strong> from world spawn</td></tr>
    <tr><td><code>lazaret</code></td><td>Fixed ring <strong>100 blocks</strong> from center</td></tr>
    <tr><td><code>resonancearchive</code></td><td><strong>100 blocks beyond bbox edge</strong> (~200×170 underground)</td></tr>
    <tr><td><code>village</code></td><td><strong>100 blocks beyond bbox edge</strong></td></tr>
    <tr><td><code>tobiascave</code></td><td><strong>100 blocks beyond bbox edge</strong></td></tr>
    <tr><td><code>devastationarea</code></td><td><strong>100 blocks beyond bbox edge</strong></td></tr>
  </tbody>
</table>

<h2>Installation</h2>
<ol>
  <li>Download <code>vsmodskygrid-0.2.0.zip</code> from this page (or from the <a href="https://github.com/Eloi-Desbrosses/VsModSkygrid/releases">GitHub release</a>).</li>
  <li>Drop the zip into your VS server's <code>Mods/</code> folder.</li>
  <li><strong>Start a new world</strong> — skygrid only generates new chunks, it does not convert existing terrain.</li>
  <li>Players can connect with a stock Vintage Story 1.22.2 client. No client mod required.</li>
</ol>

<h2>Source &amp; issues</h2>
<ul>
  <li><strong>Source code:</strong> <a href="https://github.com/Eloi-Desbrosses/VsModSkygrid">github.com/Eloi-Desbrosses/VsModSkygrid</a></li>
  <li><strong>Issue tracker:</strong> <a href="https://github.com/Eloi-Desbrosses/VsModSkygrid/issues">github.com/Eloi-Desbrosses/VsModSkygrid/issues</a></li>
  <li><strong>License:</strong> MIT</li>
</ul>
```

---

## Changelog (HTML — paste into the version-notes WYSIWYG)

```html
<h3>v0.2.0 — palette balance + container density (2026-05-18)</h3>

<p><strong>Worldgen palette</strong></p>
<ul>
  <li>Liquid roll dropped 10 % → 3 % (water/lava felt over-represented).</li>
  <li>Block variants now collapse to canonical groups: a single palette slot picks one block-id, then a random variant is chosen at placement. Stops gold/silver/uranium ores (~52 variants each) and the cosmetic axes of fences/stairs/slabs from drowning the pool.</li>
  <li>~50 cosmetic families collapse orientation/state/density axes; <code>leaves</code> / <code>leavesbranchy</code> use an inverted-pattern rule.</li>
</ul>

<p><strong>Bonus container density</strong></p>
<ul>
  <li><code>MaxChestsPerColumn</code> raised 16 → 128 (avg ~64/col, ~1.8 % of grid cells are containers). Old 0.24 % felt too sparse.</li>
  <li>Per-cell match against in-column container list now uses an O(1) <code>Span&lt;int&gt;</code> lookup instead of a linear scan — keeps worldgen fast at the new max.</li>
</ul>

<p><strong>Diagnostics</strong></p>
<ul>
  <li>Palette dump (<code>&lt;data&gt;/Logs/skygrid-palette.txt</code>) gains three new sections: variant groups, synthetic <code>PickBlock</code> distribution sample, per-distance-band container tier shares.</li>
</ul>

<h3>v0.1.0 — initial release (2026-05-17)</h3>
<ul>
  <li>Skygrid worldgen (3D grid, 1 block every 4, Y = 30..250).</li>
  <li>5 vanilla story structures preserved as floating islands.</li>
  <li>Translocator-gate network linking spawn to each structure.</li>
  <li>5-tier scattered loot containers + per-player starter basket.</li>
  <li>Server-only — no client install needed.</li>
</ul>
```

---

## Submission checklist

- [ ] Logged in to <https://mods.vintagestory.at>
- [ ] Click *Add Mod*, paste the **Name / Side / Game version** from the table
- [ ] Switch the description editor to *Source* / *HTML* mode if it offers
      one, then paste the **Long description HTML** block (otherwise paste
      it as-is — most WYSIWYGs accept pasted HTML directly)
- [ ] Set **Source / Issue / License** URLs
- [ ] Pick tags: `worldgen`, `adventure`, `survival`, `loot`, `server`
- [ ] Upload `dist/vsmodskygrid-0.2.0.zip` as the first file
- [ ] In the *Release* section: set version `0.2.0`, paste the **Changelog**,
      mark compatibility with **1.22.2**
- [ ] Attach screenshots (gate platform, story-structure approach, vessel in
      the void)
- [ ] Submit and check that the public page renders Markdown correctly
