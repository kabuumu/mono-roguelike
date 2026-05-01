# RPG UI Redesign — Design Spec

**Date:** 2026-05-01
**Scope:** Visual and structural refinement of the game UI from roguelike to open-world RPG style.

---

## Goal

The game has open-world RPG content (village, wilderness biomes, quests, factions, NPCs) but the UI speaks pure roguelike. This pass aligns the visual chrome and information display with a Classic High Fantasy open-world aesthetic.

---

## Decisions

| Topic | Decision |
|---|---|
| Aesthetic direction | Classic High Fantasy (warm amber/gold, parchment tones, ornate borders) |
| HUD position | Top bar — same position, reskinned |
| Key hints | Remove full hint line; replace with `[?] Help` placeholder in dim gold |
| Floor/turn display | Remove turn counter; replace with Location Name |
| Death screen | Narrative layout ("A Hero Falls") |

---

## 1. Colour Palette

All panels (HUD, dialogue, inventory, barter, character screen, interaction panel) switch from the current cold navy/blue scheme to this palette:

| Role | Hex | Usage |
|---|---|---|
| Panel background | `#1a1208` + ~94% alpha | All panel fills |
| Border sides | `#8B6914` | Left/right/outer borders |
| Border accent | `#C8991F` | Top and bottom 2px accent lines |
| Primary text | `#E8D090` | Body text, values |
| Secondary text | `#CCBBAA` | Supporting text (unchanged from current) |
| Label / dim text | `#9B7A2A` | Uppercase section labels, footer hints |
| Selected highlight | `#503C14` ~70% alpha | Selected row in lists |
| Separator lines | `#5a4010` | Horizontal dividers inside panels |

The HUD bar bottom edge gets a 2px `#C8991F` accent line.

---

## 2. HUD Bar

Same position (top of screen, 72px height). Changes:

- **Background**: `#1a1208` ~82% alpha; 2px bright-gold bottom accent.
- **"HP"** → **"Vitality"** (label rename; colour-coded bar logic unchanged).
- **"ATK" / "DEF"** → **"Attack" / "Defence"** (spelt out).
- **`Floor X   Turn Y` slot** → **Location Name** (e.g. `Thornhaven Village`, `The Wildlands`, `Dungeon — Level 2`). Sourced from a new `LocationName` field on `GameState`.
- **Key-hint line** (second row of HUD, y=50) → replaced with a single `[?] Help` in dim gold (`#9B7A2A`) at the far right. No functional help overlay in this pass — purely visual.
- **"Equipped items"** label → **"Weapon / Armour"**.

### LocationName — data model change

Add `string LocationName` to the `GameState` record with a default of `"Unknown Lands"`.

Set it at generation time:
- `WorldGenerator.Generate()` → `"Thornhaven Village"` via `with { LocationName = "Thornhaven Village" }` on the returned state
- `MonoRogueGame.BuildDungeonFloor()` → `"Dungeon — Level {n}"` via `with { LocationName = $"Dungeon — Level {floorLevel}" }`

`BspGenerator` itself does not need to change.

---

## 3. Overlay Panels

All panels receive the palette from section 1. Structural changes are minimal.

### Dialogue Box
- Amber-gold chrome replaces blue. NPC name in bright gold (`#C8991F`). Separator below name in `#5a4010`. Footer hints in dim gold (`#9B7A2A`). No layout changes.

### Inventory Box
- Chrome swap. "Items:" / "Resources:" section labels in dim gold uppercase. Selected row highlight: amber `#503C14` ~70% alpha. No layout changes.

### Barter Box
- Chrome swap. Price values stay warm gold. Selected row: amber highlight. No layout changes.

### Interaction Context Panel
- Chrome swap. Stays anchored bottom-right above message log.

### Character Screen
- Chrome swap. Column dividers change to `#5a4010`. Column headers ("STATISTICS", "BACKGROUND", "QUESTS") each get a short `DrawRect` underline in amber (`#8B6914`) — the spritefont only covers ASCII 32–126, so Unicode box-drawing characters are unavailable. Screen title "CHARACTER" in bright gold with a full-width `DrawRect` separator beneath it.

---

## 4. Main Menu

Current: title centred, `[ENTER] New Game`.

New layout (top to bottom, centred):
1. Decorative border frame drawn with `DrawRect` calls (amber `#8B6914`, top/bottom `#C8991F` accent) surrounding the title block
2. Game title in bright gold: `MONOROGUE`
3. Subtitle in dim gold: `An Open-World Adventure`
4. Flavour text in muted parchment (`#CCBBAA`): `A lone traveller steps into an unknown land...`
5. Start prompt in muted green (`#8CCA8C`): `[ENTER] Begin Your Journey`

Note: Unicode box-drawing characters (`╔`, `╗` etc.) are outside the spritefont's ASCII 32–126 range and cannot be used. The frame is rendered with `DrawRect`.

---

## 5. Death / Victory Screens

### Death
| | Current | New |
|---|---|---|
| Title | `YOU DIED` | `A Hero Falls` |
| Colour | Red `#DC3C3C` | Red-amber `#CC5C28` |
| Sub-line | cause string | cause string (unchanged) |
| Stats line | `Floor reached: X   Turns: Y` | `Fallen in: {LocationName}   Level {n}` |
| Restart | `[R] Restart` | `[R] Begin Anew` |

### Victory
| | Current | New |
|---|---|---|
| Title | `YOU WIN!` | `Legend Forged` |
| Colour | Green `#50DC50` | Green-gold `#80C850` |
| Sub-line | `Congratulations, hero!` | `Congratulations, hero!` (unchanged) |
| Stats line | `Floor reached: X   Turns: Y` | `Level {n} reached   {LocationName}` |
| Restart | `[R] Restart` | `[R] Begin Anew` |

---

## Files Touched

| File | Change |
|---|---|
| `src/Core/Model/GameState.cs` | Add `string LocationName` field with default `"Unknown Lands"` |
| `src/Core/Generation/WorldGenerator.cs` | Set `LocationName = "Thornhaven Village"` on generated state |
| `src/Shell/MonoRogueGame.cs` | Set `LocationName` on state in `BuildVillage()` and `BuildDungeonFloor()` |
| `src/Shell/AsciiRenderer.cs` | All visual changes — palette, labels, layout, main menu, end screens |

---

## Out of Scope

- Functional help overlay for `[?]`
- Time-of-day system
- Save/load (restart remains the only option after death)
- Sprite/tile art changes — ASCII glyphs unchanged
- Any gameplay or systems changes
