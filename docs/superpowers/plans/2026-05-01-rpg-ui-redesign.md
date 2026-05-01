# RPG UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the cold navy/blue roguelike UI chrome with a Classic High Fantasy amber/gold palette, add a LocationName to GameState, and reskin every panel, the HUD, main menu, and end screens.

**Architecture:** All visual changes are isolated to `AsciiRenderer.cs`. The only data-model change is adding `LocationName` to `GameState` (a record with-expression field); it is set by `MonoRogueGame` at world/dungeon build time and read by the renderer. No gameplay or systems code is touched.

**Tech Stack:** C# 12, MonoGame 3.8, .NET 8. Build: `dotnet build` from repo root. Run: `dotnet run --project src`.

---

## File Map

| File | Change |
|---|---|
| `src/Core/Model/GameState.cs` | Add `string LocationName` parameter with default `"Unknown Lands"` |
| `src/Shell/MonoRogueGame.cs` | Set `LocationName` in `BuildVillage()` and `BuildDungeonFloor()` |
| `src/Shell/AsciiRenderer.cs` | All visual changes across 7 tasks below |

---

## Task 1: Add LocationName to GameState

**Files:**
- Modify: `src/Core/Model/GameState.cs`

- [ ] **Open `src/Core/Model/GameState.cs`.** The record currently ends with `bool IsCharacterScreenOpen = false`. Add `LocationName` as the last optional parameter:

```csharp
    /// <summary>Is the character screen open?</summary>
    bool IsCharacterScreenOpen = false,
    /// <summary>Display name of the current area shown in the HUD.</summary>
    string LocationName = "Unknown Lands"
)
```

  The `GameState.Empty()` factory does not need updating — the default value covers it.

- [ ] **Build to verify no compilation errors:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Core/Model/GameState.cs
git commit -m "feat: add LocationName to GameState"
```

---

## Task 2: Set LocationName during world/dungeon generation

**Files:**
- Modify: `src/Shell/MonoRogueGame.cs`

- [ ] **In `MonoRogueGame.BuildVillage()`, capture the generated state and apply the location name.** The current last two lines are:

```csharp
        return WorldGenerator.Generate(worldParams, _registry, "background_blacksmith_child", freshPlayer);
    }
```

  Replace them with:

```csharp
        var world = WorldGenerator.Generate(worldParams, _registry, "background_blacksmith_child", freshPlayer);
        return world with { LocationName = "Thornhaven Village" };
    }
```

- [ ] **In `MonoRogueGame.BuildDungeonFloor()`, append `LocationName` to the final `with` expression.** The current last two lines are:

```csharp
        var vis = FovSystem.Compute(state, dungeon.PlayerSpawn, 10);
        return state with { VisibleTiles = vis, ExploredTiles = vis };
```

  Replace them with:

```csharp
        var vis = FovSystem.Compute(state, dungeon.PlayerSpawn, 10);
        return state with { VisibleTiles = vis, ExploredTiles = vis, LocationName = $"Dungeon - Level {floorLevel}" };
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Shell/MonoRogueGame.cs
git commit -m "feat: set LocationName in world and dungeon builders"
```

---

## Task 3: Add palette constants to AsciiRenderer

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs`

- [ ] **Replace the single `HudTextColor` field with the full Classic High Fantasy palette.** Find this line near the top of the class:

```csharp
    private static readonly Color HudTextColor = new(200, 200, 180);
```

  Replace it with:

```csharp
    // ── Classic High Fantasy palette ─────────────────────────────────────────
    private static readonly Color PanelBg      = new(26,  18,  8);   // #1a1208
    private static readonly Color BorderSide   = new(139, 105, 20);  // #8B6914
    private static readonly Color BorderAccent = new(200, 153, 31);  // #C8991F
    private static readonly Color TextPrimary  = new(232, 208, 144); // #E8D090
    private static readonly Color TextSecondary = new(204, 187, 170); // #CCBBAA
    private static readonly Color TextDim      = new(155, 122, 42);  // #9B7A2A
    private static readonly Color Separator    = new(90,  64,  16);  // #5a4010
```

- [ ] **Build** (will fail if `HudTextColor` is still referenced; that gets fixed in Task 4):

```bash
dotnet build
```

  Expected: compiler errors referencing `HudTextColor`. These are fixed in the next task — this step just verifies the new constants parse correctly. If the errors are *only* about `HudTextColor`, proceed.

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: add Classic High Fantasy palette constants to AsciiRenderer"
```

---

## Task 4: Reskin the HUD bar

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs` — `DrawHud` method

- [ ] **Replace the entire `DrawHud` method** (lines 168–223) with:

```csharp
    private void DrawHud(GameState state, int vpW, int vpH)
    {
        // Panel background + bottom accent line
        DrawRect(0, 0, vpW, HudTopPx, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 210));
        DrawRect(0, HudTopPx - 2, vpW, 2, BorderAccent);

        var player = state.TryGetPlayer();
        if (player is null) return;

        var hp     = player.Health;
        var lvl    = player.Level;
        var effAtk = CombatSystem.GetEffectiveAttack(state,  player);
        var effDef = CombatSystem.GetEffectiveDefense(state, player);

        // Vitality with colour-coded bar
        var hpColor = HpColor(hp?.Current ?? 0, hp?.Max ?? 1);
        Print($"Vitality {hp?.Current ?? 0}/{hp?.Max ?? 0}", 6, 6, hpColor);

        if (hp is not null && hp.Max > 0)
        {
            int barW   = 80;
            int filled = (int)(barW * (float)hp.Current / hp.Max);
            DrawRect(6, 24, barW, 6, new Color(60, 20, 20));
            DrawRect(6, 24, filled, 6, hpColor);
        }

        Print($"Lv {lvl?.Level ?? 1}  XP {lvl?.Xp ?? 0}/{lvl?.XpToNextLevel ?? 100}",
              130, 6, new Color(180, 180, 255));
        Print($"Attack {effAtk}  Defence {effDef}",
              310, 6, TextSecondary);
        Print(state.LocationName, 490, 6, TextPrimary);

        var weapName  = EquippedName(state, player.Equipment?.WeaponId);
        var armorName = EquippedName(state, player.Equipment?.ArmorId);
        Print($"[{weapName}]  [{armorName}]", 670, 6, new Color(200, 185, 150));

        // [?] Help hint — far right, dim gold, replaces full key-hint line
        var helpStr = "[?] Help";
        var helpW   = (int)_font.MeasureString(helpStr).X;
        Print(helpStr, vpW - helpW - 6, 50, TextDim);

        // ── Message log (last 3 lines, bottom of screen) ─────────────────────
        var log  = state.MessageLog;
        int show = Math.Min(3, log.Count);
        for (int i = 0; i < show; i++)
        {
            float age  = show - 1 - i;
            var alpha  = (byte)Math.Max(100, 230 - (int)(age * 65));
            var y      = vpH - (show - i) * (TileH + 2) - 4;
            DrawRect(0, y - 2, vpW, TileH + 4, new Color(0, 0, 0, 160));
            var c = new Color(TextPrimary.R, TextPrimary.G, TextPrimary.B, alpha);
            Print(log[log.Count - show + i], 6, y, c);
        }
    }
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)` — `HudTextColor` references are now gone.

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: reskin HUD bar with amber-gold palette and location name"
```

---

## Task 5: Reskin dialogue, inventory, barter, and interaction panels

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs` — `DrawDialogueBox`, `DrawInventoryBox`, `DrawBarterBox`, `DrawInteractionContext`

- [ ] **Replace the chrome in `DrawDialogueBox`.** Find the `// ── Chrome` block (around line 246):

```csharp
        // ── Chrome ────────────────────────────────────────────────────────────
        DrawRect(margin, boxY,            boxW, boxH, new Color(8, 16, 32, 240));
        DrawRect(margin, boxY,            boxW, 2,    new Color(100, 140, 200, 220));
        DrawRect(margin, boxY + boxH - 2, boxW, 2,    new Color(100, 140, 200, 220));

        // ── NPC name + separator ──────────────────────────────────────────────
        Print(dlg.NpcName, innerX, boxY + 10, new Color(255, 220, 100));
        DrawRect(innerX, boxY + 32, boxW - 24, 1, new Color(80, 100, 140, 180));
```

  Replace with:

```csharp
        // ── Chrome ────────────────────────────────────────────────────────────
        DrawRect(margin, boxY,            boxW, boxH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 240));
        DrawRect(margin, boxY,            boxW, 2,    BorderAccent);
        DrawRect(margin, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(margin, boxY,            1,    boxH, BorderSide);
        DrawRect(margin + boxW - 1, boxY, 1,    boxH, BorderSide);

        // ── NPC name + separator ──────────────────────────────────────────────
        Print(dlg.NpcName, innerX, boxY + 10, BorderAccent);
        DrawRect(innerX, boxY + 32, boxW - 24, 1, Separator);
```

- [ ] **Update the dialogue message text and option colours.** Find:

```csharp
            Print(wrapped[i], innerX, boxY + textTop + i * lineH, new Color(220, 220, 200));
```

  Replace with:

```csharp
            Print(wrapped[i], innerX, boxY + textTop + i * lineH, TextSecondary);
```

  Find the option separator:

```csharp
            DrawRect(innerX, boxY + optSepY, boxW - 24, 1, new Color(80, 100, 140, 180));
```

  Replace with:

```csharp
            DrawRect(innerX, boxY + optSepY, boxW - 24, 1, Separator);
```

  Find the selected option highlight:

```csharp
                    DrawRect(margin + 4, lineY - 3, boxW - 8, 24, new Color(40, 70, 120, 180));
                    Print($"> {dlg.Options[i].Label}", innerX - 4, lineY, new Color(180, 220, 255));
```

  Replace with:

```csharp
                    DrawRect(margin + 4, lineY - 3, boxW - 8, 24, new Color(80, 60, 20, 180));
                    Print($"> {dlg.Options[i].Label}", innerX - 4, lineY, TextPrimary);
```

  Find the unselected option:

```csharp
                    Print($"  {dlg.Options[i].Label}", innerX, lineY, new Color(200, 200, 180));
```

  Replace with:

```csharp
                    Print($"  {dlg.Options[i].Label}", innerX, lineY, TextSecondary);
```

  Find the two footer hint lines:

```csharp
            Print("[Up/Down] Select   [SPACE] Choose",
                  innerX, boxY + boxH - 24, new Color(120, 120, 140));
```

  and:

```csharp
            Print(hintStr, margin + boxW - hintW - 12, boxY + boxH - 24, new Color(160, 200, 160));
```

  and:

```csharp
            Print(pageStr, innerX, boxY + boxH - 24, new Color(120, 120, 140));
```

  Replace all three `new Color(...)` hint colours with `TextDim`.

- [ ] **Replace the chrome in `DrawInventoryBox`.** Find:

```csharp
        DrawRect(boxX, boxY, boxW, boxH, new Color(16, 24, 32, 240));
        DrawRect(boxX, boxY, boxW, 2, new Color(200, 140, 100, 220)); // top border
        DrawRect(boxX, boxY + boxH - 2, boxW, 2, new Color(200, 140, 100, 220)); // bottom border

        CentreText("INVENTORY", boxX + boxW / 2, boxY + 24, new Color(255, 200, 100));
        DrawRect(innerX, boxY + 44, boxW - 48, 1, new Color(80, 100, 140, 180));
```

  Replace with:

```csharp
        DrawRect(boxX, boxY, boxW, boxH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 240));
        DrawRect(boxX, boxY,            boxW, 2, BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2, BorderAccent);
        DrawRect(boxX,          boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText("INVENTORY", boxX + boxW / 2, boxY + 24, BorderAccent);
        DrawRect(innerX, boxY + 44, boxW - 48, 1, Separator);
```

  Then update these colour literals inside `DrawInventoryBox`:

  | Find | Replace |
  |---|---|
  | `new Color(240, 220, 100)` (gold line) | `TextPrimary` |
  | `new Color(160, 180, 200)` ("Items:" label) | `TextDim` |
  | `new Color(80, 120, 60, 180)` (selected row) | `new Color(80, 60, 20, 178)` |
  | `new Color(180, 255, 140)` (selected text) | `TextPrimary` |
  | `new Color(220, 220, 220)` (unselected text) | `TextSecondary` |
  | `new Color(120, 120, 120)` ("(None)" dim) | `TextDim` |
  | `new Color(140, 140, 140)` (footer hint) | `TextDim` |

- [ ] **Replace the chrome in `DrawBarterBox`.** Find:

```csharp
        DrawRect(boxX, boxY,            boxW, boxH, new Color(16, 24, 32, 240));
        DrawRect(boxX, boxY,            boxW, 2,    new Color(200, 140, 100, 220));
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    new Color(200, 140, 100, 220));

        CentreText($"WARES - {barter.NpcName.ToUpper()}",
                   boxX + boxW / 2f, boxY + 24, new Color(255, 200, 100));
        DrawRect(innerX, boxY + 44, boxW - 48, 1, new Color(80, 100, 140, 180));
```

  Replace with:

```csharp
        DrawRect(boxX, boxY,            boxW, boxH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 240));
        DrawRect(boxX, boxY,            boxW, 2,    BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(boxX,           boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText($"WARES - {barter.NpcName.ToUpper()}",
                   boxX + boxW / 2f, boxY + 24, BorderAccent);
        DrawRect(innerX, boxY + 44, boxW - 48, 1, Separator);
```

  Then update colour literals inside `DrawBarterBox`:

  | Find | Replace |
  |---|---|
  | `new Color(240, 220, 100)` (gold line) | `TextPrimary` |
  | `new Color(60, 70, 90, 140)` (inner separator) | `Separator` |
  | `new Color(80, 120, 60, 180)` (selected row) | `new Color(80, 60, 20, 180)` |
  | `new Color(180, 255, 140)` (selected name) | `TextPrimary` |
  | `new Color(240, 220, 100)` (selected price) | `TextPrimary` |
  | `new Color(220, 220, 220)` (unselected name) | `TextSecondary` |
  | `new Color(180, 160, 80)` (unselected price) | `TextDim` |
  | `new Color(120, 120, 120)` ("(Nothing)" dim) | `TextDim` |
  | `new Color(140, 140, 140)` (footer hint) | `TextDim` |

- [ ] **Replace the chrome in `DrawInteractionContext`.** Find:

```csharp
        DrawRect(panelX,     panelY,              panelW, panelH, new Color(12, 20, 40, 220));
        DrawRect(panelX,     panelY,              panelW, 2,      new Color(80, 160, 220, 200));
        DrawRect(panelX,     panelY + panelH - 2, panelW, 2,      new Color(80, 160, 220, 200));

        Print($"Interact: {current.Label}", panelX + 10, panelY + 10, new Color(140, 210, 255));

        if (count > 1)
            Print($"({state.InteractionTargetIndex + 1}/{count})  [Up/Down] cycle",
                  panelX + 10, panelY + 30, new Color(160, 160, 190));

        Print("[Enter/C] Confirm   [Esc] Cancel",
              panelX + 10, panelY + panelH - 22, new Color(110, 130, 150));
```

  Replace with:

```csharp
        DrawRect(panelX,     panelY,              panelW, panelH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 220));
        DrawRect(panelX,     panelY,              panelW, 2,      BorderAccent);
        DrawRect(panelX,     panelY + panelH - 2, panelW, 2,      BorderAccent);
        DrawRect(panelX,              panelY, 1, panelH, BorderSide);
        DrawRect(panelX + panelW - 1, panelY, 1, panelH, BorderSide);

        Print($"Interact: {current.Label}", panelX + 10, panelY + 10, TextPrimary);

        if (count > 1)
            Print($"({state.InteractionTargetIndex + 1}/{count})  [Up/Down] cycle",
                  panelX + 10, panelY + 30, TextSecondary);

        Print("[Enter/C] Confirm   [Esc] Cancel",
              panelX + 10, panelY + panelH - 22, TextDim);
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: reskin dialogue, inventory, barter and interaction panels"
```

---

## Task 6: Reskin character screen

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs` — `DrawCharacterScreen`

- [ ] **Replace the outer panel chrome.** Find:

```csharp
        DrawRect(boxX, boxY,            boxW, boxH, new Color(10, 18, 30, 250));
        DrawRect(boxX, boxY,            boxW, 2,    new Color(120, 160, 220, 220));
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    new Color(120, 160, 220, 220));

        CentreText("CHARACTER", boxX + boxW / 2f, boxY + 18, new Color(180, 220, 255));
        DrawRect(boxX + 20, boxY + 38, boxW - 40, 1, new Color(80, 100, 140, 180));
```

  Replace with:

```csharp
        DrawRect(boxX, boxY,            boxW, boxH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 250));
        DrawRect(boxX, boxY,            boxW, 2,    BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(boxX,           boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText("CHARACTER", boxX + boxW / 2f, boxY + 18, BorderAccent);
        DrawRect(boxX + 20, boxY + 38, boxW - 40, 1, Separator);
```

- [ ] **Replace the column divider colours.** Find:

```csharp
        DrawRect(col2X - 16, boxY + 40, 1, boxH - 50, new Color(60, 80, 100, 160));
        DrawRect(col3X - 16, boxY + 40, 1, boxH - 50, new Color(60, 80, 100, 160));
```

  Replace with:

```csharp
        DrawRect(col2X - 16, boxY + 40, 1, boxH - 50, Separator);
        DrawRect(col3X - 16, boxY + 40, 1, boxH - 50, Separator);
```

- [ ] **Update Statistics column header and its sub-separator.** Find:

```csharp
        Print("STATISTICS", col1X, y, new Color(160, 200, 255));
        DrawRect(col1X, y + 18, colInnerW, 1, new Color(60, 80, 100, 140));
```

  Replace with:

```csharp
        Print("STATISTICS", col1X, y, TextPrimary);
        DrawRect(col1X, y + 18, colInnerW, 1, BorderSide);
```

  Then update the stat value colours inside the Statistics block:

  | Find | Replace |
  |---|---|
  | `new Color(220, 220, 200)` (Name) | `TextSecondary` |
  | `new Color(180, 180, 255)` (Level) | `new Color(180, 180, 255)` (keep — XP blue is fine) |
  | `new Color(220, 140, 140)` (Attack) | keep |
  | `new Color(140, 180, 220)` (Defence) | keep |
  | `new Color(240, 220, 100)` (Gold) | `TextPrimary` |
  | `new Color(160, 180, 200)` ("Equipped:" label) | `TextDim` |
  | `new Color(200, 200, 180)` (Weapon/Armor values) | `TextSecondary` |

- [ ] **Update Background column header.** Find:

```csharp
        Print("BACKGROUND", col2X, y, new Color(255, 200, 100));
        DrawRect(col2X, y + 18, colInnerW, 1, new Color(60, 80, 100, 140));
```

  Replace with:

```csharp
        Print("BACKGROUND", col2X, y, TextPrimary);
        DrawRect(col2X, y + 18, colInnerW, 1, BorderSide);
```

  Then update colours in the Background block:

  | Find | Replace |
  |---|---|
  | `new Color(255, 220, 120)` (bg name) | `BorderAccent` |
  | `new Color(180, 180, 160)` (description lines) | `TextSecondary` |
  | `new Color(160, 180, 200)` ("Starting gear:" label) | `TextDim` |
  | `new Color(200, 200, 180)` (gear items) | `TextSecondary` |
  | `new Color(120, 120, 120)` ("No background.") | `TextDim` |

- [ ] **Update Quests column header.** Find:

```csharp
        Print("QUESTS", col3X, y, new Color(140, 220, 140));
        DrawRect(col3X, y + 18, colInnerW, 1, new Color(60, 80, 100, 140));
```

  Replace with:

```csharp
        Print("QUESTS", col3X, y, TextPrimary);
        DrawRect(col3X, y + 18, colInnerW, 1, BorderSide);
```

  Then update colours in the Quests block:

  | Find | Replace |
  |---|---|
  | `new Color(120, 120, 120)` ("No active quests.") | `TextDim` |
  | `new Color(200, 220, 160)` ("Active (N):") | `TextSecondary` |
  | `new Color(180, 230, 140)` (quest name) | `TextPrimary` |
  | `new Color(160, 180, 140)` (objective progress) | `TextSecondary` |
  | `new Color(140, 180, 140)` ("Completed (N):") | `TextDim` |
  | `new Color(120, 160, 120)` ("[done] name") | `TextDim` |

- [ ] **Update footer hint.** Find:

```csharp
        Print("[P / ESC] Close", boxX + 12, boxY + boxH - 22, new Color(120, 120, 140));
```

  Replace with:

```csharp
        Print("[P / ESC] Close", boxX + 12, boxY + boxH - 22, TextDim);
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: reskin character screen with amber-gold palette"
```

---

## Task 7: Redesign main menu

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs` — `DrawMainMenu`

- [ ] **Replace the entire `DrawMainMenu` method:**

```csharp
    private void DrawMainMenu(int vpW, int vpH)
    {
        var cx = vpW / 2f;
        var cy = vpH / 2f;

        // Decorative frame around title block
        const int frameW = 280;
        const int frameH = 52;
        int frameX = (int)(cx - frameW / 2f);
        int frameY = (int)(cy - 56);
        DrawRect(frameX,              frameY,              frameW, frameH, new Color(PanelBg.R, PanelBg.G, PanelBg.B, 220));
        DrawRect(frameX,              frameY,              frameW, 2,      BorderAccent);
        DrawRect(frameX,              frameY + frameH - 2, frameW, 2,      BorderAccent);
        DrawRect(frameX,              frameY,              2,      frameH, BorderSide);
        DrawRect(frameX + frameW - 2, frameY,              2,      frameH, BorderSide);

        CentreText("MONOROGUE",                                      cx, cy - 40, BorderAccent);
        CentreText("An Open-World Adventure",                        cx, cy - 18, TextDim);
        CentreText("A lone traveller steps into an unknown land...", cx, cy + 14, TextSecondary);
        CentreText("[ENTER] Begin Your Journey",                     cx, cy + 44, new Color(140, 202, 140));
    }
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: redesign main menu with fantasy frame and flavour text"
```

---

## Task 8: Redesign death and victory end screens

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs` — `DrawEndScreen`

- [ ] **Replace the entire `DrawEndScreen` method:**

```csharp
    private void DrawEndScreen(GameState state, int vpW, int vpH)
    {
        DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 200));

        var (title, sub, titleColor) = state.Phase switch
        {
            PlayerDeadPhase dead => ("A Hero Falls", dead.Cause, new Color(204, 92, 40)),
            VictoryPhase         => ("Legend Forged", "Congratulations, hero!", new Color(128, 200, 80)),
            _                    => ("GAME OVER", "", Color.White)
        };

        var playerLevel = state.TryGetPlayer()?.Level?.Level ?? 1;
        var statsLine   = state.Phase is PlayerDeadPhase
            ? $"Fallen in: {state.LocationName}   Level {playerLevel}"
            : $"Level {playerLevel} reached   {state.LocationName}";

        var cx = vpW / 2f;
        var cy = vpH / 2f;

        CentreText(title,     cx, cy - 50, titleColor);
        CentreText(sub,       cx, cy,      TextSecondary);
        CentreText(statsLine, cx, cy + 40, TextDim);
        CentreText("[R] Begin Anew", cx, cy + 80, new Color(140, 140, 200));
    }
```

- [ ] **Build:**

```bash
dotnet build
```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**

```bash
git add src/Shell/AsciiRenderer.cs
git commit -m "feat: narrative death and victory end screens"
```

---

## Self-Review Notes

- **Spec coverage:** All items checked — palette, HUD labels, LocationName data model, key hints → `[?] Help`, all modal panels, character screen, main menu, death/victory screens. ✓
- **Placeholder scan:** All colour literals are concrete `new Color(R,G,B)` values or named palette constants. No TBDs. ✓
- **Type consistency:** `LocationName` added in Task 1, read as `state.LocationName` in Task 4 (HUD) and Task 8 (end screen). `PanelBg`, `BorderSide`, `BorderAccent`, `TextPrimary`, `TextSecondary`, `TextDim`, `Separator` all defined in Task 3 and used consistently across Tasks 4–8. ✓
- **Note on `HudTextColor`:** This field is fully replaced by the palette in Task 3 and removed from all usages in Task 4. The build check in Task 3 will surface any remaining references.
