# Dialogue Options + Barter UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the linear "advance through lines" dialogue with an options-based system, then use an `open_shop` option to launch a barter UI where the player buys items from traders.

**Architecture:** Options are defined in JSON blueprints as `dialogOptions: [{id, label, action}]`. After all dialogue lines are read, `DialogueState` transitions to `ShowingOptions` mode — the shell handles Up/Down/Enter navigation directly (same pattern as inventory). Selecting `open_shop` closes dialogue and sets `GameState.ActiveBarter`, which the renderer draws as a centred panel.

**Tech Stack:** C# 12, MonoGame, immutable records with `with`-expressions, pure systems / shell-handled UI state.

---

## File Map

| File | Change |
|---|---|
| `src/Data/Blueprint.cs` | Add `DialogOptionTemplate` record + `DialogOptions` field on `EntityTemplate` |
| `src/Core/Model/Components.cs` | Add `DialogueOption` record + `Options` field on `DialogueComponent` |
| `src/Core/Model/BarterState.cs` | **New** — `BarterState(NpcId, NpcName, SelectedIndex)` |
| `src/Core/Model/GameState.cs` | Add `ActiveBarter` parameter + `IsBarterOpen` property |
| `src/Core/Model/DialogueState.cs` | Add `Options`, `SelectedOption`, `ShowingOptions`; rewrite `Advance()` |
| `src/Core/Events/Events.cs` | Add `ImmutableArray<DialogueOption> Options` to `DialogueOpenedEvent` |
| `src/Core/Reducer.cs` | Pass `opened.Options` when constructing `DialogueState` |
| `src/Core/Systems/SpawnSystem.cs` | Wire `template.DialogOptions` → `DialogueComponent.Options` |
| `src/Core/Systems/DialogueSystem.cs` | Include `npc.Dialogue.Options` in `DialogueOpenedEvent` |
| `src/Shell/MonoRogueGame.cs` | Add options-navigation block, barter-navigation block, `ExecuteOption()` helper |
| `src/Shell/AsciiRenderer.cs` | Options mode in `DrawDialogueBox`; new `DrawBarterBox` method |
| `Content/Data/blueprints/blueprints_village.json` | Add `dialogOptions` to blacksmith, herbalist, trader |

---

## Task 1: Data types — Blueprint + Components

**Files:**
- Modify: `src/Data/Blueprint.cs`
- Modify: `src/Core/Model/Components.cs`

- [ ] **Step 1: Add `DialogOptionTemplate` to Blueprint.cs**

  In `src/Data/Blueprint.cs`, add a new record after `DropEntry` and add the field to `EntityTemplate`:

  ```csharp
  /// <summary>One selectable option shown at the end of an NPC's dialogue.</summary>
  public sealed record DialogOptionTemplate(
      string Id,
      string Label,
      string Action   // "close" | "open_shop"
  );
  ```

  And in `EntityTemplate`, add after `DialogPool = null!`:

  ```csharp
  // Dialogue options shown after all lines are read (null = auto-close)
  DialogOptionTemplate[]? DialogOptions = null
  ```

- [ ] **Step 2: Add `DialogueOption` + update `DialogueComponent` in Components.cs**

  In `src/Core/Model/Components.cs`, add before `DialogueComponent`:

  ```csharp
  /// <summary>A selectable choice presented to the player at the end of dialogue.</summary>
  public sealed record DialogueOption(string Id, string Label, string Action);
  ```

  Update `DialogueComponent` to add `Options`:

  ```csharp
  public sealed record DialogueComponent(
      ImmutableDictionary<string, ImmutableArray<string>> DialogPool,
      ImmutableArray<string>                              KeyOrder,
      ImmutableArray<DialogueOption>                      Options = default
  );
  ```

- [ ] **Step 3: Build to verify no regressions**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

---

## Task 2: BarterState + GameState

**Files:**
- Create: `src/Core/Model/BarterState.cs`
- Modify: `src/Core/Model/GameState.cs`

- [ ] **Step 1: Create BarterState.cs**

  ```csharp
  namespace MonoRogue.Core.Model;

  /// <summary>
  /// Shell-managed UI state for the barter screen.
  /// Null on GameState = barter is closed.
  /// </summary>
  public sealed record BarterState(
      Guid   NpcId,
      string NpcName,
      int    SelectedIndex = 0
  );
  ```

- [ ] **Step 2: Add ActiveBarter to GameState.cs**

  In `src/Core/Model/GameState.cs`, add after `InteractionTargetIndex`:

  ```csharp
  /// <summary>Non-null when the barter screen is open.</summary>
  BarterState? ActiveBarter = null
  ```

  Add a property after `IsInteractionMenuOpen`:

  ```csharp
  public bool IsBarterOpen => ActiveBarter is not null;
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 3: DialogueState options mode

**Files:**
- Modify: `src/Core/Model/DialogueState.cs`

- [ ] **Step 1: Rewrite DialogueState.cs**

  Replace the entire file content:

  ```csharp
  namespace MonoRogue.Core.Model;

  using System.Collections.Immutable;

  /// <summary>
  /// Snapshot of an in-progress NPC conversation.
  /// Two modes:
  ///   ShowingOptions = false  — reading lines (current text + page indicator)
  ///   ShowingOptions = true   — choosing an option from Options list
  /// </summary>
  public sealed record DialogueState(
      Guid                           NpcId,
      string                         NpcName,
      ImmutableArray<string>         Lines,
      int                            CurrentLine    = 0,
      ImmutableArray<DialogueOption> Options        = default,
      int                            SelectedOption = 0,
      bool                           ShowingOptions = false
  )
  {
      public bool HasOptions    => !Options.IsDefaultOrEmpty;
      public bool IsLastLine    => CurrentLine >= Lines.Length - 1;
      public string CurrentText =>
          Lines.IsEmpty ? "" : Lines[Math.Clamp(CurrentLine, 0, Lines.Length - 1)];

      /// <summary>
      /// Advances the dialogue state machine:
      ///   - Reading, not last line  → next line
      ///   - Reading, last line, has options → transition to options mode
      ///   - Reading, last line, no options  → null (close)
      ///   - Showing options → unchanged (shell handles option selection)
      /// </summary>
      public DialogueState? Advance()
      {
          if (ShowingOptions) return this;
          if (IsLastLine && HasOptions)
              return this with { ShowingOptions = true, SelectedOption = 0 };
          if (IsLastLine) return null;
          return this with { CurrentLine = CurrentLine + 1 };
      }
  }
  ```

- [ ] **Step 2: Write a unit test for Advance()**

  In `tests/MonoRogue.Tests/`, add a new file `DialogueStateTests.cs`:

  ```csharp
  using MonoRogue.Core.Model;
  using System.Collections.Immutable;
  using Xunit;

  namespace MonoRogue.Tests;

  public class DialogueStateTests
  {
      private static ImmutableArray<DialogueOption> TwoOptions =>
      [
          new("barter",   "Browse wares.", "open_shop"),
          new("farewell", "Farewell.",     "close"),
      ];

      [Fact]
      public void Advance_MiddleLine_MovesToNextLine()
      {
          var state = new DialogueState(Guid.NewGuid(), "NPC",
              Lines: ["Hello", "Goodbye"]);
          var next = state.Advance();
          Assert.NotNull(next);
          Assert.Equal(1, next!.CurrentLine);
          Assert.False(next.ShowingOptions);
      }

      [Fact]
      public void Advance_LastLineNoOptions_ReturnsNull()
      {
          var state = new DialogueState(Guid.NewGuid(), "NPC",
              Lines: ["Only line"]);
          Assert.Null(state.Advance());
      }

      [Fact]
      public void Advance_LastLineWithOptions_TransitionsToOptionsMode()
      {
          var state = new DialogueState(Guid.NewGuid(), "NPC",
              Lines: ["Only line"], Options: TwoOptions);
          var next = state.Advance();
          Assert.NotNull(next);
          Assert.True(next!.ShowingOptions);
          Assert.Equal(0, next.SelectedOption);
      }

      [Fact]
      public void Advance_WhenShowingOptions_ReturnsUnchanged()
      {
          var state = new DialogueState(Guid.NewGuid(), "NPC",
              Lines: ["line"], Options: TwoOptions,
              ShowingOptions: true, SelectedOption: 1);
          var next = state.Advance();
          Assert.NotNull(next);
          Assert.Equal(1, next!.SelectedOption);
          Assert.True(next.ShowingOptions);
      }
  }
  ```

- [ ] **Step 3: Run tests**

  ```
  dotnet test tests/MonoRogue.Tests/MonoRogue.Tests.csproj --no-restore -v minimal
  ```
  Expected: all tests pass including the four new `DialogueStateTests`.

- [ ] **Step 4: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 4: Events + Reducer

**Files:**
- Modify: `src/Core/Events/Events.cs`
- Modify: `src/Core/Reducer.cs`

- [ ] **Step 1: Update DialogueOpenedEvent in Events.cs**

  Replace the existing `DialogueOpenedEvent`:

  ```csharp
  /// <summary>A conversation with an NPC has started.</summary>
  public sealed record DialogueOpenedEvent(
      Guid                           NpcId,
      string                         NpcName,
      ImmutableArray<string>         Lines,
      ImmutableArray<DialogueOption> Options = default
  ) : GameEvent;
  ```

  Note: `DialogueOption` is in `MonoRogue.Core.Model` — the existing `using MonoRogue.Core.Model;` at the top of Events.cs covers it.

- [ ] **Step 2: Update Reducer.cs DialogueOpenedEvent handler**

  In `src/Core/Reducer.cs`, find:

  ```csharp
  DialogueOpenedEvent opened  => state with
  {
      ActiveDialogue = new DialogueState(
          opened.NpcId, opened.NpcName, opened.Lines)
  },
  ```

  Replace with:

  ```csharp
  DialogueOpenedEvent opened  => state with
  {
      ActiveDialogue = new DialogueState(
          opened.NpcId, opened.NpcName, opened.Lines,
          Options: opened.Options)
  },
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 5: SpawnSystem + DialogueSystem

**Files:**
- Modify: `src/Core/Systems/SpawnSystem.cs`
- Modify: `src/Core/Systems/DialogueSystem.cs`

- [ ] **Step 1: Wire DialogOptions in SpawnSystem.cs**

  In `src/Core/Systems/SpawnSystem.cs`, find the `Dialogue:` property in `BuildEntity`:

  ```csharp
  Dialogue:    t.HasDialogue && t.DialogPool?.Count > 0
                   ? new DialogueComponent(
                       t.DialogPool.ToImmutableDictionary(
                           kvp => kvp.Key,
                           kvp => kvp.Value.ToImmutableArray()),
                       t.DialogPool.Keys.ToImmutableArray())
                   : null,
  ```

  Replace with:

  ```csharp
  Dialogue:    t.HasDialogue && t.DialogPool?.Count > 0
                   ? new DialogueComponent(
                       t.DialogPool.ToImmutableDictionary(
                           kvp => kvp.Key,
                           kvp => kvp.Value.ToImmutableArray()),
                       t.DialogPool.Keys.ToImmutableArray(),
                       t.DialogOptions?.Length > 0
                           ? t.DialogOptions
                               .Select(o => new DialogueOption(o.Id, o.Label, o.Action))
                               .ToImmutableArray()
                           : ImmutableArray<DialogueOption>.Empty)
                   : null,
  ```

- [ ] **Step 2: Include Options in DialogueSystem.cs events**

  In `src/Core/Systems/DialogueSystem.cs`, find both places that emit `DialogueOpenedEvent`.

  In `ProcessTalk` (around line 80), replace:

  ```csharp
  return
  [
      new DialogueOpenedEvent(
          npc.Id,
          npc.Identity?.Name ?? "Stranger",
          lines)
  ];
  ```

  With:

  ```csharp
  return
  [
      new DialogueOpenedEvent(
          npc.Id,
          npc.Identity?.Name ?? "Stranger",
          lines,
          npc.Dialogue.Options)
  ];
  ```

  In `ProcessBumps` (around line 104), replace:

  ```csharp
  result.Add(new DialogueOpenedEvent(npc.Id, npc.Identity?.Name ?? "Stranger", lines));
  ```

  With:

  ```csharp
  result.Add(new DialogueOpenedEvent(npc.Id, npc.Identity?.Name ?? "Stranger", lines, npc.Dialogue.Options));
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 6: Shell — MonoRogueGame options + barter

**Files:**
- Modify: `src/Shell/MonoRogueGame.cs`

Three changes to `Update()` plus one new private method.

- [ ] **Step 1: Add barter to the Escape chain**

  Find the existing escape block (opens with `if (_input.WasPressed(Keys.Escape))`). After the `IsInteractionMenuOpen` guard and before the `IsInventoryOpen` guard, add:

  ```csharp
  if (_state.IsBarterOpen)
  {
      _state = _state with { ActiveBarter = null };
      base.Update(gameTime); return;
  }
  ```

  So the full escape chain becomes (in order):
  1. `IsInteractionMenuOpen` → close
  2. `IsBarterOpen` → close barter  ← NEW
  3. `IsInventoryOpen` → close inventory
  4. `!InDialogue` → Exit()

- [ ] **Step 2: Add dialogue-options navigation block**

  After the inventory navigation block (the `if (_state.IsInventoryOpen)` block that ends around line 208) and before the `ConsumeIntent` call, add:

  ```csharp
  // ── Dialogue options navigation ───────────────────────────────────────────
  if (_state.ActiveDialogue?.ShowingOptions == true)
  {
      var dlg = _state.ActiveDialogue;
      if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
      {
          _state = _state with
          {
              ActiveDialogue = dlg with
              {
                  SelectedOption = Math.Max(0, dlg.SelectedOption - 1)
              }
          };
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
      {
          _state = _state with
          {
              ActiveDialogue = dlg with
              {
                  SelectedOption = Math.Min(dlg.Options.Length - 1, dlg.SelectedOption + 1)
              }
          };
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Enter) || _input.WasPressed(Keys.Space) || _input.WasPressed(Keys.T))
      {
          ExecuteOption(dlg.Options[dlg.SelectedOption], dlg.NpcId, dlg.NpcName);
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Escape))
      {
          _state = _state with { ActiveDialogue = null };
          base.Update(gameTime); return;
      }
      base.Update(gameTime); return;
  }
  ```

- [ ] **Step 3: Add barter navigation block**

  Immediately after the options block (before `ConsumeIntent`), add:

  ```csharp
  // ── Barter screen navigation ──────────────────────────────────────────────
  if (_state.IsBarterOpen)
  {
      var barter = _state.ActiveBarter!;
      _state.Entities.TryGetValue(barter.NpcId, out var traderEnt);
      int itemCount = traderEnt?.Inventory?.Items.Length ?? 0;

      if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
      {
          _state = _state with
          {
              ActiveBarter = barter with { SelectedIndex = Math.Max(0, barter.SelectedIndex - 1) }
          };
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
      {
          _state = _state with
          {
              ActiveBarter = barter with
              {
                  SelectedIndex = Math.Min(Math.Max(0, itemCount - 1), barter.SelectedIndex + 1)
              }
          };
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Enter) && itemCount > 0 && barter.SelectedIndex < itemCount)
      {
          var itemId = traderEnt!.Inventory!.Items[barter.SelectedIndex];
          int prevIdx = barter.SelectedIndex;
          ExecutePlayerIntent(new TradeIntent(_state.PlayerEntityId, barter.NpcId, itemId));
          // Clamp selection after item was removed
          if (_state.IsBarterOpen)
          {
              int newCount = _state.Entities.TryGetValue(barter.NpcId, out var nt)
                  ? nt.Inventory?.Items.Length ?? 0 : 0;
              _state = newCount > 0
                  ? _state with { ActiveBarter = _state.ActiveBarter! with
                      { SelectedIndex = Math.Min(prevIdx, newCount - 1) } }
                  : _state with { ActiveBarter = null };
          }
          base.Update(gameTime); return;
      }
      if (_input.WasPressed(Keys.Escape))
      {
          _state = _state with { ActiveBarter = null };
          base.Update(gameTime); return;
      }
      base.Update(gameTime); return;
  }
  ```

- [ ] **Step 4: Add ExecuteOption helper method**

  Add this private method alongside `ExecutePlayerIntent` and `TryDescend`:

  ```csharp
  private void ExecuteOption(Core.Model.DialogueOption option, Guid npcId, string npcName)
  {
      _state = _state with { ActiveDialogue = null };
      switch (option.Action)
      {
          case "open_shop":
              _state = _state with { ActiveBarter = new BarterState(npcId, npcName) };
              break;
      }
  }
  ```

- [ ] **Step 5: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 7: Renderer — dialogue options mode + barter panel

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs`

- [ ] **Step 1: Rewrite DrawDialogueBox to support options mode**

  Replace the existing `DrawDialogueBox` method entirely:

  ```csharp
  private void DrawDialogueBox(DialogueState dlg, int vpW, int vpH)
  {
      const int margin = 30;
      int boxW   = vpW - margin * 2;
      int innerX = margin + 12;

      if (dlg.ShowingOptions && dlg.HasOptions)
      {
          // ── Options mode ──────────────────────────────────────────────────
          int boxH = Math.Max(130, 64 + dlg.Options.Length * 28);
          int boxY = vpH - boxH - 10;

          DrawRect(margin, boxY,              boxW, boxH, new Color(8, 16, 32, 240));
          DrawRect(margin, boxY,              boxW, 2,    new Color(100, 140, 200, 220));
          DrawRect(margin, boxY + boxH - 2,   boxW, 2,    new Color(100, 140, 200, 220));

          Print(dlg.NpcName, innerX, boxY + 10, new Color(255, 220, 100));
          DrawRect(innerX, boxY + 32, boxW - 24, 1, new Color(80, 100, 140, 180));

          for (int i = 0; i < dlg.Options.Length; i++)
          {
              int lineY    = boxY + 44 + i * 28;
              bool selected = i == dlg.SelectedOption;
              if (selected)
              {
                  DrawRect(margin + 4, lineY - 3, boxW - 8, 24, new Color(40, 70, 120, 180));
                  Print($"> {dlg.Options[i].Label}", innerX - 4, lineY, new Color(180, 220, 255));
              }
              else
              {
                  Print($"  {dlg.Options[i].Label}", innerX, lineY, new Color(200, 200, 180));
              }
          }

          Print("[Up/Down] Select   [SPACE] Choose",
                innerX, boxY + boxH - 24, new Color(120, 120, 140));
      }
      else
      {
          // ── Text mode (original behaviour) ────────────────────────────────
          const int boxH = 130;
          int boxY = vpH - boxH - 10;

          DrawRect(margin, boxY,            boxW, boxH, new Color(8, 16, 32, 240));
          DrawRect(margin, boxY,            boxW, 2,    new Color(100, 140, 200, 220));
          DrawRect(margin, boxY + boxH - 2, boxW, 2,    new Color(100, 140, 200, 220));

          Print(dlg.NpcName, innerX, boxY + 10, new Color(255, 220, 100));
          DrawRect(innerX, boxY + 32, boxW - 24, 1, new Color(80, 100, 140, 180));

          var lines = WrapText(dlg.CurrentText, boxW - 28);
          for (int i = 0; i < Math.Min(lines.Length, 3); i++)
              Print(lines[i], innerX, boxY + 40 + i * (TileH + 2), new Color(220, 220, 200));

          var pageStr = $"[{dlg.CurrentLine + 1}/{dlg.Lines.Length}]";
          var hintStr = dlg.IsLastLine && dlg.HasOptions
              ? "[SPACE] Choose..."
              : dlg.IsLastLine ? "[SPACE] Close" : "[SPACE] Next";
          var hintW = (int)_font.MeasureString(hintStr).X;
          Print(pageStr, innerX, boxY + boxH - 24, new Color(120, 120, 140));
          Print(hintStr, margin + boxW - hintW - 12, boxY + boxH - 24, new Color(160, 200, 160));
      }
  }
  ```

- [ ] **Step 2: Add DrawBarterBox method**

  Add this new method after `DrawDialogueBox`:

  ```csharp
  private void DrawBarterBox(GameState state, int vpW, int vpH)
  {
      var barter = state.ActiveBarter!;
      if (!state.Entities.TryGetValue(barter.NpcId, out var trader)) return;

      const int boxW = 440;
      const int boxH = 400;
      int boxX   = (vpW - boxW) / 2;
      int boxY   = (vpH - boxH) / 2;
      int innerX = boxX + 24;

      // Dim background
      DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 160));
      // Panel
      DrawRect(boxX, boxY,              boxW, boxH, new Color(16, 24, 32, 240));
      DrawRect(boxX, boxY,              boxW, 2,    new Color(200, 140, 100, 220));
      DrawRect(boxX, boxY + boxH - 2,   boxW, 2,    new Color(200, 140, 100, 220));

      CentreText($"WARES — {barter.NpcName.ToUpper()}",
                 boxX + boxW / 2f, boxY + 24, new Color(255, 200, 100));
      DrawRect(innerX, boxY + 44, boxW - 48, 1, new Color(80, 100, 140, 180));

      // Player gold
      var player = state.TryGetPlayer();
      int drawY = boxY + 56;
      Print($"Your gold: {player?.Inventory?.Gold ?? 0}g",
            innerX, drawY, new Color(240, 220, 100));
      drawY += 28;
      DrawRect(innerX, drawY, boxW - 48, 1, new Color(60, 70, 90, 140));
      drawY += 12;

      // Trader items
      var items = trader.Inventory?.Items ?? [];
      if (items.IsEmpty)
      {
          Print("  (Nothing for sale)", innerX, drawY, new Color(120, 120, 120));
      }
      else
      {
          for (int i = 0; i < items.Length; i++)
          {
              if (!state.Entities.TryGetValue(items[i], out var item)) continue;
              var name  = item.Identity?.Name ?? "Item";
              var price = item.Item?.Value ?? 0;
              bool sel  = i == barter.SelectedIndex;

              var priceStr = $"{price}g";
              var priceW   = (int)_font.MeasureString(priceStr).X;

              if (sel)
              {
                  DrawRect(boxX + 4, drawY - 2, boxW - 8, 24, new Color(80, 120, 60, 180));
                  Print($"> {name}", innerX - 4, drawY, new Color(180, 255, 140));
                  Print(priceStr, boxX + boxW - priceW - 24, drawY, new Color(240, 220, 100));
              }
              else
              {
                  Print($"  {name}", innerX, drawY, new Color(220, 220, 220));
                  Print(priceStr, boxX + boxW - priceW - 24, drawY, new Color(180, 160, 80));
              }
              drawY += 26;
          }
      }

      Print("[Up/Down] Select   [Enter] Buy   [Esc] Close",
            boxX + 8, boxY + boxH - 24, new Color(140, 140, 140));
  }
  ```

- [ ] **Step 3: Add barter render pass to Draw()**

  In the `Draw` method, after `// Pass 5: inventory box`, add:

  ```csharp
  // Pass 6: barter box
  if (state.IsBarterOpen)
      DrawBarterBox(state, vpW, vpH);
  ```

  (The existing interaction context panel becomes Pass 7 — renumber the comment if desired.)

- [ ] **Step 4: Build**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  ```
  Expected: `Build succeeded.`

---

## Task 8: JSON — add dialogOptions to traders

**Files:**
- Modify: `Content/Data/blueprints/blueprints_village.json`

- [ ] **Step 1: Add dialogOptions to npc_blacksmith**

  In `blueprints_village.json`, inside the `npc_blacksmith` object, add after the closing `}` of `dialogPool`:

  ```json
  "dialogOptions": [
    { "id": "barter",   "label": "What have you got for sale?", "action": "open_shop" },
    { "id": "farewell", "label": "Farewell.",                   "action": "close"     }
  ]
  ```

- [ ] **Step 2: Add dialogOptions to npc_herbalist**

  ```json
  "dialogOptions": [
    { "id": "barter",   "label": "What do you have?",  "action": "open_shop" },
    { "id": "farewell", "label": "Farewell.",           "action": "close"     }
  ]
  ```

- [ ] **Step 3: Add dialogOptions to npc_trader**

  ```json
  "dialogOptions": [
    { "id": "barter",   "label": "Show me your wares.", "action": "open_shop" },
    { "id": "farewell", "label": "Farewell.",            "action": "close"     }
  ]
  ```

- [ ] **Step 4: Final build + test run**

  ```
  dotnet build monogame-rpg.csproj --no-restore -v quiet
  dotnet test tests/MonoRogue.Tests/MonoRogue.Tests.csproj --no-restore -v minimal
  ```
  Expected: build succeeded, all tests green.

---

## Interaction Flow Summary (for QA)

```
Player bumps/T-talks trader
  → DialogueOpenedEvent carries Lines + Options
  → Reducer sets ActiveDialogue{ShowingOptions=false}
  → Renderer: text box with [SPACE] Choose... hint on last line

Player presses SPACE on last line
  → Advance() returns ShowingOptions=true
  → Renderer: options list, first option highlighted

Player Up/Down → shell updates SelectedOption directly
Player SPACE/Enter → ExecuteOption()
  → "open_shop": ActiveDialogue=null, ActiveBarter={NpcId, NpcName}
  → "close": ActiveDialogue=null

Barter open:
  → Renderer: centred panel, trader's items + prices, player gold
  → Up/Down: shell updates SelectedIndex
  → Enter: TradeIntent → EconomySystem validates → gold/item transfer
  → Esc: ActiveBarter=null
```
