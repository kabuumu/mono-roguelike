namespace MonoRogue.Shell;

using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;

/// <summary>
/// Reads <see cref="GameState"/> and renders every entity as an ASCII glyph.
///
/// Rendering layers (back to front):
///   1. Explored tiles (dim)
///   2. Visible tiles (normal brightness)
///   3. Visible non-tile entities (creatures, items, NPCs)
///   4. HUD overlay (stats, messages)
///   5. Dialogue box when <see cref="GameState.InDialogue"/> is true
/// </summary>
public sealed class AsciiRenderer
{
    // ── Tile metrics ─────────────────────────────────────────────────────────
    public const int TileW = 16;
    public const int TileH = 20;

    // ── HUD metrics ──────────────────────────────────────────────────────────
    public const int HudTopPx = 72;   // pixels reserved at top for status bar

    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly SpriteBatch    _batch;
    private readonly SpriteFont     _font;
    private readonly GlyphMap       _glyphMap;
    private readonly GraphicsDevice _gfx;
    private readonly Texture2D      _pixel;
    private readonly DataRegistry   _registry;

    // ── Classic High Fantasy palette ─────────────────────────────────────────
    private static readonly Color PanelBg      = new(26,  18,  8);   // #1a1208
    private static readonly Color BorderSide   = new(139, 105, 20);  // #8B6914
    private static readonly Color BorderAccent = new(200, 153, 31);  // #C8991F
    private static readonly Color TextPrimary  = new(232, 208, 144); // #E8D090
    private static readonly Color TextSecondary = new(204, 187, 170); // #CCBBAA
    private static readonly Color TextDim      = new(155, 122, 42);  // #9B7A2A
    private static readonly Color Separator    = new(90,  64,  16);  // #5a4010

    public AsciiRenderer(SpriteBatch batch, SpriteFont font, GlyphMap glyphMap, GraphicsDevice gfx, DataRegistry registry)
    {
        _batch    = batch;
        _font     = font;
        _glyphMap = glyphMap;
        _gfx      = gfx;
        _registry = registry;

        _pixel = new Texture2D(gfx, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    // ── Public entry point ───────────────────────────────────────────────────

    public void Draw(GameState state, int vpW, int vpH)
    {
        _batch.Begin(samplerState: SamplerState.PointClamp);

        // Main menu replaces everything
        if (state.IsMainMenu)
        {
            DrawMainMenu(vpW, vpH);
            _batch.End();
            return;
        }

        // End-screen replaces everything
        if (!state.IsPlaying)
        {
            DrawEndScreen(state, vpW, vpH);
            _batch.End();
            return;
        }

        // ── Camera: centre on player, clamp to map bounds ─────────────────────
        int vpTileW = vpW / TileW;
        int vpTileH = (vpH - HudTopPx) / TileH;

        var playerPos = state.TryGetPlayer()?.Spatial?.Position ?? Position.Zero;
        int camX = Math.Clamp(playerPos.X - vpTileW / 2, 0, Math.Max(0, state.MapWidth  - vpTileW));
        int camY = Math.Clamp(playerPos.Y - vpTileH / 2, 0, Math.Max(0, state.MapHeight - vpTileH));

        // Single iteration with viewport culling: reject 99%+ of entities by position,
        // then bucket the visible remainder into tiles vs non-tiles for correct draw order.
        int camRight  = camX + vpTileW;
        int camBottom = camY + vpTileH;
        var visibleNonTiles = new List<Entity>();

        // Pass 1: draw tiles immediately, collect non-tiles for pass 2
        foreach (var entity in state.Entities.Values)
        {
            if (entity.Spatial is null) continue;
            var pos = entity.Spatial.Position;

            // Early viewport bounds rejection (cheapest check)
            if (pos.X < camX || pos.X >= camRight || pos.Y < camY || pos.Y >= camBottom)
                continue;

            if (entity.IsTile())
            {
                if      (state.VisibleTiles.Contains(pos))  DrawEntity(entity, dim: false, camX, camY, vpW, vpH);
                else if (state.ExploredTiles.Contains(pos)) DrawEntity(entity, dim: true,  camX, camY, vpW, vpH);
            }
            else if (state.VisibleTiles.Contains(pos))
            {
                visibleNonTiles.Add(entity);
            }
        }

        // Interaction target highlights (under entities, above tiles)
        if (state.IsInteractionMenuOpen && state.InteractionTargets is { } itTargets)
        {
            foreach (var choice in itTargets)
                HighlightTile(choice.WorldPosition, camX, camY, vpW, vpH, new Color(60, 100, 180, 80));
            var sel = itTargets[state.InteractionTargetIndex];
            HighlightTile(sel.WorldPosition, camX, camY, vpW, vpH, new Color(80, 160, 255, 160));
        }

        // Pass 2: non-tile entities on top
        foreach (var entity in visibleNonTiles)
            DrawEntity(entity, dim: false, camX, camY, vpW, vpH);

        // Pass 3: HUD bar
        DrawHud(state, vpW, vpH);

        // Pass 4: dialogue box (on top of everything)
        if (state.InDialogue && state.ActiveDialogue is { } dlg)
            DrawDialogueBox(dlg, vpW, vpH);

        // Pass 5: inventory box
        if (state.IsInventoryOpen)
            DrawInventoryBox(state, vpW, vpH, state.InventorySelectedIndex);

        // Pass 6: barter box
        if (state.IsBarterOpen)
            DrawBarterBox(state, vpW, vpH);

        // Pass 7: interaction context panel (small, non-blocking)
        if (state.IsInteractionMenuOpen)
            DrawInteractionContext(state, vpW, vpH);

        // Pass 8: character screen (full-screen overlay)
        if (state.IsCharacterScreenOpen)
            DrawCharacterScreen(state, vpW, vpH);

        _batch.End();
    }

    // ── Entity glyph ─────────────────────────────────────────────────────────

    private void DrawEntity(Entity entity, bool dim, int camX, int camY, int vpW, int vpH)
    {
        if (entity.Spatial is null || entity.Render is null) return;

        int sx = (entity.Spatial.Position.X - camX) * TileW;
        int sy = (entity.Spatial.Position.Y - camY) * TileH + HudTopPx;

        // Cull anything outside the visible viewport
        if (sx < 0 || sx >= vpW || sy < HudTopPx || sy >= vpH) return;

        var glyph = _glyphMap.Resolve(entity.Render.SpriteKey);
        var color = ParseColor(glyph.ColorOverride ?? entity.Render.ColorTint, Color.White);
        if (dim) color = new Color(color.R / 5, color.G / 5, color.B / 5);

        _batch.DrawString(_font, glyph.Glyph.ToString(), new Vector2(sx, sy), color);
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    private void DrawHud(GameState state, int vpW, int vpH)
    {
        // Panel background + bottom accent line
        DrawRect(0, 0, vpW, HudTopPx, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 210));
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

    // ── Dialogue box ─────────────────────────────────────────────────────────

    private void DrawDialogueBox(DialogueState dlg, int vpW, int vpH)
    {
        const int margin     = 30;
        const int lineH      = TileH + 2;   // 22 px per wrapped text line
        const int maxMsgLine = 3;
        const int textTop    = 40;
        const int textBottom = textTop + maxMsgLine * lineH;   // 106
        const int optSepY    = textBottom + 6;                  // 112
        const int optStartY  = optSepY + 12;                    // 124

        int boxW   = vpW - margin * 2;
        int innerX = margin + 12;

        // Box height is fixed for the entire conversation so it never resizes mid-dialogue.
        int boxH = dlg.HasOptions
            ? optStartY + dlg.Options.Length * 28 + 34
            : 130;
        int boxY = vpH - boxH - 10;

        // ── Chrome ────────────────────────────────────────────────────────────
        DrawRect(margin, boxY,            boxW, boxH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 240));
        DrawRect(margin, boxY,            boxW, 2,    BorderAccent);
        DrawRect(margin, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(margin,          boxY, 1, boxH, BorderSide);
        DrawRect(margin + boxW - 1, boxY, 1, boxH, BorderSide);

        // ── NPC name + separator ──────────────────────────────────────────────
        Print(dlg.NpcName, innerX, boxY + 10, BorderAccent);
        DrawRect(innerX, boxY + 32, boxW - 24, 1, Separator);

        // ── Current message ───────────────────────────────────────────────────
        var wrapped = WrapText(dlg.CurrentText, boxW - 28);
        for (int i = 0; i < Math.Min(wrapped.Length, maxMsgLine); i++)
            Print(wrapped[i], innerX, boxY + textTop + i * lineH, TextSecondary);

        // ── Footer: options on last line, page counter otherwise ──────────────
        if (dlg.ShowingOptions)
        {
            DrawRect(innerX, boxY + optSepY, boxW - 24, 1, Separator);

            for (int i = 0; i < dlg.Options.Length; i++)
            {
                int  lineY    = boxY + optStartY + i * 28;
                bool selected = i == dlg.SelectedOption;
                if (selected)
                {
                    DrawRect(margin + 4, lineY - 3, boxW - 8, 24, new Color(80, 60, 20, 180));
                    Print($"> {dlg.Options[i].Label}", innerX - 4, lineY, TextPrimary);
                }
                else
                {
                    Print($"  {dlg.Options[i].Label}", innerX, lineY, TextSecondary);
                }
            }

            Print("[Up/Down] Select   [SPACE] Choose",
                  innerX, boxY + boxH - 24, TextDim);
        }
        else
        {
            var pageStr = $"[{dlg.CurrentLine + 1}/{dlg.Lines.Length}]";
            var hintStr = dlg.IsLastLine ? "[SPACE] Close" : "[SPACE] Next";
            var hintW   = (int)_font.MeasureString(hintStr).X;
            Print(pageStr, innerX, boxY + boxH - 24, TextDim);
            Print(hintStr, margin + boxW - hintW - 12, boxY + boxH - 24, TextDim);
        }
    }

    // ── Barter box ────────────────────────────────────────────────────────────

    private void DrawBarterBox(GameState state, int vpW, int vpH)
    {
        var barter = state.ActiveBarter!;
        if (!state.Entities.TryGetValue(barter.NpcId, out var trader)) return;

        const int boxW = 440;
        const int boxH = 400;
        int boxX   = (vpW - boxW) / 2;
        int boxY   = (vpH - boxH) / 2;
        int innerX = boxX + 24;

        DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 160));
        DrawRect(boxX, boxY,            boxW, boxH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 240));
        DrawRect(boxX, boxY,            boxW, 2,    BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(boxX,           boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText($"WARES - {barter.NpcName.ToUpper()}",
                   boxX + boxW / 2f, boxY + 24, BorderAccent);
        DrawRect(innerX, boxY + 44, boxW - 48, 1, Separator);

        var player = state.TryGetPlayer();
        int drawY  = boxY + 56;
        Print($"Your gold: {player?.Inventory?.Gold ?? 0}g",
              innerX, drawY, TextPrimary);
        drawY += 28;
        DrawRect(innerX, drawY, boxW - 48, 1, Separator);
        drawY += 12;

        var items = trader.Inventory?.Items ?? [];
        if (items.IsEmpty)
        {
            Print("  (Nothing for sale)", innerX, drawY, TextDim);
        }
        else
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (!state.Entities.TryGetValue(items[i], out var item)) continue;
                var  name     = item.Identity?.Name ?? "Item";
                var  price    = item.Item?.Value ?? 0;
                bool sel      = i == barter.SelectedIndex;
                var  priceStr = $"{price}g";
                var  priceW   = (int)_font.MeasureString(priceStr).X;

                if (sel)
                {
                    DrawRect(boxX + 4, drawY - 2, boxW - 8, 24, new Color(80, 60, 20, 180));
                    Print($"> {name}", innerX - 4, drawY, TextPrimary);
                    Print(priceStr, boxX + boxW - priceW - 24, drawY, TextPrimary);
                }
                else
                {
                    Print($"  {name}", innerX, drawY, TextSecondary);
                    Print(priceStr, boxX + boxW - priceW - 24, drawY, TextDim);
                }
                drawY += 26;
            }
        }

        Print("[Up/Down] Select   [Enter] Buy   [Esc] Close",
              boxX + 8, boxY + boxH - 24, TextDim);
    }

    // ── Inventory box ────────────────────────────────────────────────────────

    private void DrawInventoryBox(GameState state, int vpW, int vpH, int selectedIndex)
    {
        var player = state.TryGetPlayer();
        if (player?.Inventory is null) return;

        const int boxW   = 400;
        const int boxH   = 500;
        int boxX  = (vpW - boxW) / 2;
        int boxY  = (vpH - boxH) / 2;
        int innerX = boxX + 24;

        // Background panel
        DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 160)); // Darken background slightly
        DrawRect(boxX, boxY, boxW, boxH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 240));
        DrawRect(boxX, boxY,            boxW, 2, BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2, BorderAccent);
        DrawRect(boxX,           boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText("INVENTORY", boxX + boxW / 2, boxY + 24, BorderAccent);
        DrawRect(innerX, boxY + 44, boxW - 48, 1, Separator);

        int drawY = boxY + 60;
        Print($"Gold: {player.Inventory.Gold}g", innerX, drawY, TextPrimary);
        drawY += 30;

        // Concrete items (equipable / consumable entity references) — selectable
        Print("Items:", innerX, drawY, TextDim);
        drawY += 24;

        if (player.Inventory.Items.IsEmpty)
        {
            Print("  (None)", innerX, drawY, TextDim);
            drawY += 24;
        }
        else
        {
            for (int i = 0; i < player.Inventory.Items.Length; i++)
            {
                var itemId  = player.Inventory.Items[i];
                var itemEnt = state.Entities.TryGetValue(itemId, out var ie) ? ie : null;
                var name    = itemEnt?.Identity?.Name ?? "Unknown Item";
                bool sel    = i == selectedIndex;
                if (sel)
                {
                    DrawRect(boxX + 4, drawY - 2, boxW - 8, 22, new Color(80, 60, 20, 178));
                    Print($"> {name}", innerX - 4, drawY, TextPrimary);
                }
                else
                {
                    Print($"  {name}", innerX, drawY, TextSecondary);
                }
                drawY += 24;
            }
        }

        drawY += 12;

        // Abstract resources — informational only (use C key near interactables)
        Print("Resources:", innerX, drawY, TextDim);
        drawY += 24;

        if (player.Inventory.Resources is null || player.Inventory.Resources.IsEmpty)
        {
            Print("  (None)", innerX, drawY, TextDim);
        }
        else
        {
            foreach (var kvp in player.Inventory.Resources)
            {
                var label     = kvp.Key.Replace("item_", "").Replace("_", " ");
                var titleCase = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(label);
                Print($"  {titleCase} x{kvp.Value}", innerX, drawY, TextSecondary);
                drawY += 24;
            }
        }

        // Key hints at the bottom
        Print("[Up/Down] Select item   [U/Enter] Use   [I/ESC] Close", boxX + 8, boxY + boxH - 24, TextDim);
    }

    // ── Interaction target highlight ──────────────────────────────────────────

    private void HighlightTile(Position worldPos, int camX, int camY, int vpW, int vpH, Color color)
    {
        int sx = (worldPos.X - camX) * TileW;
        int sy = (worldPos.Y - camY) * TileH + HudTopPx;
        if (sx >= 0 && sx < vpW && sy >= HudTopPx && sy < vpH)
            DrawRect(sx, sy, TileW, TileH, color);
    }

    // ── Interaction context panel (small, anchored to corner) ─────────────────

    private void DrawInteractionContext(GameState state, int vpW, int vpH)
    {
        var targets = state.InteractionTargets!;
        var current = targets[state.InteractionTargetIndex];
        int count   = targets.Count;

        const int panelW = 300;
        const int panelH = 72;
        int panelX = vpW - panelW - 12;
        int panelY = vpH - panelH - (3 * (TileH + 2) + 12); // above message log

        DrawRect(panelX,     panelY,              panelW, panelH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 220));
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
    }

    // ── Character Screen ──────────────────────────────────────────────────────

    private void DrawCharacterScreen(GameState state, int vpW, int vpH)
    {
        var player = state.TryGetPlayer();
        if (player is null) return;

        // Dim the world behind the panel
        DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 200));

        const int margin = 30;
        int boxX = margin;
        int boxY = margin;
        int boxW = vpW - margin * 2;
        int boxH = vpH - margin * 2;

        DrawRect(boxX, boxY,            boxW, boxH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 250));
        DrawRect(boxX, boxY,            boxW, 2,    BorderAccent);
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    BorderAccent);
        DrawRect(boxX,           boxY, 1, boxH, BorderSide);
        DrawRect(boxX + boxW - 1, boxY, 1, boxH, BorderSide);

        CentreText("CHARACTER", boxX + boxW / 2f, boxY + 18, BorderAccent);
        DrawRect(boxX + 20, boxY + 38, boxW - 40, 1, Separator);

        // Three equal columns
        int col1X    = boxX + 24;
        int col2X    = boxX + boxW / 3 + 12;
        int col3X    = boxX + (boxW * 2) / 3 + 12;
        int colInnerW = boxW / 3 - 48;
        int contentY  = boxY + 52;

        // Column dividers
        DrawRect(col2X - 16, boxY + 40, 1, boxH - 50, Separator);
        DrawRect(col3X - 16, boxY + 40, 1, boxH - 50, Separator);

        // ── Column 1: Statistics ──────────────────────────────────────────────
        int y = contentY;
        Print("STATISTICS", col1X, y, TextPrimary);
        DrawRect(col1X, y + 18, colInnerW, 1, BorderSide);
        y += 30;

        var hp     = player.Health;
        var lvl    = player.Level;
        var effAtk = CombatSystem.GetEffectiveAttack(state,  player);
        var effDef = CombatSystem.GetEffectiveDefense(state, player);
        var inv    = player.Inventory;
        var equip  = player.Equipment;

        Print($"Name:    {player.Identity?.Name ?? "Unknown"}", col1X, y, TextSecondary); y += 24;
        Print($"Level:   {lvl?.Level ?? 1}",                    col1X, y, new Color(180, 180, 255)); y += 24;
        Print($"XP:      {lvl?.Xp ?? 0} / {lvl?.XpToNextLevel ?? 100}",
              col1X, y, new Color(180, 180, 255)); y += 28;

        var hpColor = HpColor(hp?.Current ?? 0, hp?.Max ?? 1);
        Print($"HP:      {hp?.Current ?? 0} / {hp?.Max ?? 0}", col1X, y, hpColor);
        if (hp is not null && hp.Max > 0)
        {
            int barW   = 80;
            int filled = (int)(barW * (float)hp.Current / hp.Max);
            DrawRect(col1X + 92, y + 4, barW, 8, new Color(50, 15, 15));
            DrawRect(col1X + 92, y + 4, filled, 8, hpColor);
        }
        y += 24;

        Print($"Attack:  {effAtk}", col1X, y, new Color(220, 140, 140)); y += 24;
        Print($"Defense: {effDef}", col1X, y, new Color(140, 180, 220)); y += 28;

        Print($"Gold:    {inv?.Gold ?? 0}g", col1X, y, TextPrimary); y += 28;

        Print("Equipped:",                    col1X, y, TextDim); y += 22;
        Print($"  Weapon: {EquippedName(state, equip?.WeaponId)}", col1X, y, TextSecondary); y += 22;
        Print($"  Armor:  {EquippedName(state, equip?.ArmorId)}",  col1X, y, TextSecondary);

        // ── Column 2: Background ──────────────────────────────────────────────
        y = contentY;
        Print("BACKGROUND", col2X, y, TextPrimary);
        DrawRect(col2X, y + 18, colInnerW, 1, BorderSide);
        y += 30;

        var bgId = player.Background?.BackgroundId;
        if (bgId is not null && _registry.Backgrounds.TryGetValue(bgId, out var bg))
        {
            Print(bg.Name, col2X, y, BorderAccent); y += 28;

            foreach (var line in WrapText(bg.Description, colInnerW))
            {
                Print(line, col2X, y, TextSecondary);
                y += 20;
            }
            y += 12;

            if (bg.StartingItems?.Count > 0)
            {
                Print("Starting gear:", col2X, y, TextDim); y += 22;
                foreach (var kvp in bg.StartingItems)
                {
                    var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                        .ToTitleCase(kvp.Key.Replace("item_", "").Replace("_", " "));
                    string qty = kvp.Value > 1 ? $" x{kvp.Value}" : "";
                    Print($"  - {label}{qty}", col2X, y, TextSecondary); y += 20;
                }
                y += 10;
            }

            if (bg.FactionReputation?.Count > 0)
            {
                Print("Reputation:", col2X, y, TextDim); y += 22;
                foreach (var kvp in bg.FactionReputation)
                {
                    var factionName = _registry.Factions.TryGetValue(kvp.Key, out var fac)
                        ? fac.Name : kvp.Key;
                    var repColor = kvp.Value >= 50 ? new Color(140, 220, 140) : new Color(200, 200, 180);
                    Print($"  - {factionName}: {kvp.Value}", col2X, y, repColor); y += 20;
                }
            }
        }
        else
        {
            Print("No background.", col2X, y, TextDim);
        }

        // ── Column 3: Quests ──────────────────────────────────────────────────
        y = contentY;
        Print("QUESTS", col3X, y, TextPrimary);
        DrawRect(col3X, y + 18, colInnerW, 1, BorderSide);
        y += 30;

        var activeQuests    = state.ActiveQuests;
        var completedIds    = state.CompletedQuestIds;

        if (activeQuests is null || activeQuests.Count == 0)
        {
            Print("No active quests.", col3X, y, TextDim);
            y += 22;
        }
        else
        {
            Print($"Active ({activeQuests.Count}):", col3X, y, TextSecondary); y += 22;
            foreach (var quest in activeQuests)
            {
                Print($"> {quest.Name}", col3X, y, TextPrimary); y += 20;

                // Kill objectives
                if (quest.Objectives is { } objectives && !objectives.IsDefault)
                {
                    foreach (var obj in objectives)
                    {
                        int cur   = quest.Progress?.GetValueOrDefault(obj.TargetId, 0) ?? 0;
                        var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                            .ToTitleCase(obj.TargetId.Replace("creature_", "").Replace("_", " "));
                        Print($"  Kill {label}: {cur}/{obj.RequiredCount}",
                              col3X, y, TextSecondary); y += 18;
                    }
                }
                // Item delivery objectives
                else if (quest.RequiredItems?.Count > 0)
                {
                    foreach (var kvp in quest.RequiredItems)
                    {
                        var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                            .ToTitleCase(kvp.Key.Replace("item_", "").Replace("_", " "));
                        int have  = player.Inventory?.Resources?.GetValueOrDefault(kvp.Key, 0) ?? 0;
                        Print($"  {label}: {have}/{kvp.Value}",
                              col3X, y, TextSecondary); y += 18;
                    }
                }
                y += 6;
            }
        }

        if (completedIds is not null && completedIds.Count > 0)
        {
            y += 4;
            Print($"Completed ({completedIds.Count}):", col3X, y, TextDim); y += 22;
            foreach (var qid in completedIds)
            {
                var name = _registry.Quests.TryGetValue(qid, out var qt) ? qt.Name : qid;
                Print($"  [done] {name}", col3X, y, TextDim); y += 20;
            }
        }

        // Footer hint
        Print("[P / ESC] Close", boxX + 12, boxY + boxH - 22, TextDim);
    }

    // ── Main Menu ────────────────────────────────────────────────────────────

    private void DrawMainMenu(int vpW, int vpH)
    {
        var cx = vpW / 2f;
        var cy = vpH / 2f;

        // Decorative frame around title block
        const int frameW = 280;
        const int frameH = 52;
        int frameX = (int)(cx - frameW / 2f);
        int frameY = (int)(cy - 56);
        DrawRect(frameX,              frameY,              frameW, frameH, new Color((int)PanelBg.R, (int)PanelBg.G, (int)PanelBg.B, 220));
        DrawRect(frameX,              frameY,              frameW, 2,      BorderAccent);
        DrawRect(frameX,              frameY + frameH - 2, frameW, 2,      BorderAccent);
        DrawRect(frameX,              frameY,              2,      frameH, BorderSide);
        DrawRect(frameX + frameW - 2, frameY,              2,      frameH, BorderSide);

        CentreText("MONOROGUE",                                      cx, cy - 40, BorderAccent);
        CentreText("An Open-World Adventure",                        cx, cy - 18, TextDim);
        CentreText("A lone traveller steps into an unknown land...", cx, cy + 14, TextSecondary);
        CentreText("[ENTER] Begin Your Journey",                     cx, cy + 44, new Color(140, 202, 140));
    }

    // ── End screen ────────────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Print(string text, float x, float y, Color color) =>
        _batch.DrawString(_font, text, new Vector2(x, y), color);

    private void CentreText(string text, float cx, float cy, Color color)
    {
        var sz = _font.MeasureString(text);
        Print(text, cx - sz.X / 2, cy - sz.Y / 2, color);
    }

    private void DrawRect(int x, int y, int w, int h, Color color) =>
        _batch.Draw(_pixel, new Rectangle(x, y, w, h), color);

    private static Color HpColor(int cur, int max)
    {
        if (max <= 0) return Color.White;
        float r = (float)cur / max;
        return r > 0.6f ? new Color(60, 200, 60)
             : r > 0.3f ? new Color(220, 190, 40)
             :             new Color(220, 50, 50);
    }

    private static string EquippedName(GameState state, Guid? id) =>
        id is null ? "empty"
        : state.Entities.TryGetValue(id.Value, out var e)
            ? e.Identity?.Name ?? "item"
            : "empty";

    /// <summary>Splits <paramref name="text"/> into lines no wider than <paramref name="maxPx"/> pixels.</summary>
    private string[] WrapText(string text, int maxPx)
    {
        var words  = text.Split(' ');
        var lines  = new System.Collections.Generic.List<string>();
        var current = "";

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (_font.MeasureString(candidate).X > maxPx && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return [.. lines];
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return new Color(
                    Convert.ToInt32(hex[0..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));
        }
        catch { /* fall through */ }
        return fallback;
    }
}
