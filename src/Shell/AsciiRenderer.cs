namespace MonoRogue.Shell;

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

    private static readonly Color HudTextColor = new(200, 200, 180);

    public AsciiRenderer(SpriteBatch batch, SpriteFont font, GlyphMap glyphMap, GraphicsDevice gfx)
    {
        _batch    = batch;
        _font     = font;
        _glyphMap = glyphMap;
        _gfx      = gfx;

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
        // Dark bar across the top
        DrawRect(0, 0, vpW, HudTopPx, new Color(0, 0, 0, 210));

        var player = state.TryGetPlayer();
        if (player is null) return;

        var hp      = player.Health;
        var lvl     = player.Level;
        var effAtk  = CombatSystem.GetEffectiveAttack(state,  player);
        var effDef  = CombatSystem.GetEffectiveDefense(state, player);

        // HP with colour-coded bar
        var hpColor = HpColor(hp?.Current ?? 0, hp?.Max ?? 1);
        Print($"HP {hp?.Current ?? 0}/{hp?.Max ?? 0}", 6, 6, hpColor);

        // HP bar (narrow strip)
        if (hp is not null && hp.Max > 0)
        {
            int barW  = 80;
            int filled = (int)(barW * (float)hp.Current / hp.Max);
            DrawRect(6,  24, barW, 6, new Color(60, 20, 20));
            DrawRect(6,  24, filled, 6, hpColor);
        }

        Print($"Lv {lvl?.Level ?? 1}  XP {lvl?.Xp ?? 0}/{lvl?.XpToNextLevel ?? 100}",
              100, 6, new Color(180, 180, 255));
        Print($"ATK {effAtk}  DEF {effDef}",
              280, 6, new Color(220, 220, 140));

        var floorText = state.FloorLevel == 0 ? "Overworld" : $"Floor {state.FloorLevel}";
        Print($"{floorText}   Turn {state.TurnNumber}",
              430, 6, HudTextColor);

        var weapName  = EquippedName(state, player.Equipment?.WeaponId);
        var armorName = EquippedName(state, player.Equipment?.ArmorId);
        Print($"[{weapName}]  [{armorName}]", 600, 6, new Color(160, 180, 220));

        // Key hints
        Print("Move:arrows/hjkl  T:talk  G:pickup  C:interact  U:use item  I:inventory  >:descend",
              6, 50, new Color(100, 100, 100));

        // ── Message log (last 3 lines, bottom of screen) ─────────────────────
        var log  = state.MessageLog;
        int show = Math.Min(3, log.Count);
        for (int i = 0; i < show; i++)
        {
            float age   = show - 1 - i;
            var alpha   = (byte)Math.Max(100, 230 - (int)(age * 65));
            var y       = vpH - (show - i) * (TileH + 2) - 4;
            DrawRect(0, y - 2, vpW, TileH + 4, new Color(0, 0, 0, 160));
            var c = new Color(HudTextColor.R, HudTextColor.G, HudTextColor.B, alpha);
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
        DrawRect(margin, boxY,            boxW, boxH, new Color(8, 16, 32, 240));
        DrawRect(margin, boxY,            boxW, 2,    new Color(100, 140, 200, 220));
        DrawRect(margin, boxY + boxH - 2, boxW, 2,    new Color(100, 140, 200, 220));

        // ── NPC name + separator ──────────────────────────────────────────────
        Print(dlg.NpcName, innerX, boxY + 10, new Color(255, 220, 100));
        DrawRect(innerX, boxY + 32, boxW - 24, 1, new Color(80, 100, 140, 180));

        // ── Current message ───────────────────────────────────────────────────
        var wrapped = WrapText(dlg.CurrentText, boxW - 28);
        for (int i = 0; i < Math.Min(wrapped.Length, maxMsgLine); i++)
            Print(wrapped[i], innerX, boxY + textTop + i * lineH, new Color(220, 220, 200));

        // ── Footer: options on last line, page counter otherwise ──────────────
        if (dlg.ShowingOptions)
        {
            DrawRect(innerX, boxY + optSepY, boxW - 24, 1, new Color(80, 100, 140, 180));

            for (int i = 0; i < dlg.Options.Length; i++)
            {
                int  lineY    = boxY + optStartY + i * 28;
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
            var pageStr = $"[{dlg.CurrentLine + 1}/{dlg.Lines.Length}]";
            var hintStr = dlg.IsLastLine ? "[SPACE] Close" : "[SPACE] Next";
            var hintW   = (int)_font.MeasureString(hintStr).X;
            Print(pageStr, innerX, boxY + boxH - 24, new Color(120, 120, 140));
            Print(hintStr, margin + boxW - hintW - 12, boxY + boxH - 24, new Color(160, 200, 160));
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
        DrawRect(boxX, boxY,            boxW, boxH, new Color(16, 24, 32, 240));
        DrawRect(boxX, boxY,            boxW, 2,    new Color(200, 140, 100, 220));
        DrawRect(boxX, boxY + boxH - 2, boxW, 2,    new Color(200, 140, 100, 220));

        CentreText($"WARES - {barter.NpcName.ToUpper()}",
                   boxX + boxW / 2f, boxY + 24, new Color(255, 200, 100));
        DrawRect(innerX, boxY + 44, boxW - 48, 1, new Color(80, 100, 140, 180));

        var player = state.TryGetPlayer();
        int drawY  = boxY + 56;
        Print($"Your gold: {player?.Inventory?.Gold ?? 0}g",
              innerX, drawY, new Color(240, 220, 100));
        drawY += 28;
        DrawRect(innerX, drawY, boxW - 48, 1, new Color(60, 70, 90, 140));
        drawY += 12;

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
                var  name     = item.Identity?.Name ?? "Item";
                var  price    = item.Item?.Value ?? 0;
                bool sel      = i == barter.SelectedIndex;
                var  priceStr = $"{price}g";
                var  priceW   = (int)_font.MeasureString(priceStr).X;

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
        DrawRect(boxX, boxY, boxW, boxH, new Color(16, 24, 32, 240));
        DrawRect(boxX, boxY, boxW, 2, new Color(200, 140, 100, 220)); // top border
        DrawRect(boxX, boxY + boxH - 2, boxW, 2, new Color(200, 140, 100, 220)); // bottom border

        CentreText("INVENTORY", boxX + boxW / 2, boxY + 24, new Color(255, 200, 100));
        DrawRect(innerX, boxY + 44, boxW - 48, 1, new Color(80, 100, 140, 180));

        int drawY = boxY + 60;
        Print($"Gold: {player.Inventory.Gold}g", innerX, drawY, new Color(240, 220, 100));
        drawY += 30;

        // Concrete items (equipable / consumable entity references) — selectable
        Print("Items:", innerX, drawY, new Color(160, 180, 200));
        drawY += 24;

        if (player.Inventory.Items.IsEmpty)
        {
            Print("  (None)", innerX, drawY, new Color(120, 120, 120));
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
                    DrawRect(boxX + 4, drawY - 2, boxW - 8, 22, new Color(80, 120, 60, 180));
                    Print($"> {name}", innerX - 4, drawY, new Color(180, 255, 140));
                }
                else
                {
                    Print($"  {name}", innerX, drawY, new Color(220, 220, 220));
                }
                drawY += 24;
            }
        }

        drawY += 12;

        // Abstract resources — informational only (use C key near interactables)
        Print("Resources:", innerX, drawY, new Color(160, 180, 200));
        drawY += 24;

        if (player.Inventory.Resources is null || player.Inventory.Resources.IsEmpty)
        {
            Print("  (None)", innerX, drawY, new Color(120, 120, 120));
        }
        else
        {
            foreach (var kvp in player.Inventory.Resources)
            {
                var label     = kvp.Key.Replace("item_", "").Replace("_", " ");
                var titleCase = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(label);
                Print($"  {titleCase} x{kvp.Value}", innerX, drawY, new Color(220, 220, 220));
                drawY += 24;
            }
        }

        // Key hints at the bottom
        Print("[Up/Down] Select item   [U/Enter] Use   [I/ESC] Close", boxX + 8, boxY + boxH - 24, new Color(140, 140, 140));
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

        DrawRect(panelX,     panelY,              panelW, panelH, new Color(12, 20, 40, 220));
        DrawRect(panelX,     panelY,              panelW, 2,      new Color(80, 160, 220, 200));
        DrawRect(panelX,     panelY + panelH - 2, panelW, 2,      new Color(80, 160, 220, 200));

        Print($"Interact: {current.Label}", panelX + 10, panelY + 10, new Color(140, 210, 255));

        if (count > 1)
            Print($"({state.InteractionTargetIndex + 1}/{count})  [Up/Down] cycle",
                  panelX + 10, panelY + 30, new Color(160, 160, 190));

        Print("[Enter/C] Confirm   [Esc] Cancel",
              panelX + 10, panelY + panelH - 22, new Color(110, 130, 150));
    }

    // ── Main Menu ────────────────────────────────────────────────────────────

    private void DrawMainMenu(int vpW, int vpH)
    {
        var cx = vpW / 2f;
        var cy = vpH / 2f;

        CentreText("MONOROGUE", cx, cy - 40, new Color(200, 180, 80));
        CentreText("[ENTER] New Game", cx, cy + 20, new Color(140, 200, 140));
    }

    // ── End screen ────────────────────────────────────────────────────────────

    private void DrawEndScreen(GameState state, int vpW, int vpH)
    {
        DrawRect(0, 0, vpW, vpH, new Color(0, 0, 0, 200));

        var (title, sub, titleColor) = state.Phase switch
        {
            PlayerDeadPhase dead => ("YOU DIED", dead.Cause, new Color(220, 60, 60)),
            VictoryPhase         => ("YOU WIN!", "Congratulations, hero!", new Color(80, 220, 80)),
            _                    => ("GAME OVER", "", Color.White)
        };

        var cx = vpW / 2f;
        var cy = vpH / 2f;

        CentreText(title,    cx, cy - 50, titleColor);
        CentreText(sub,      cx, cy,      HudTextColor);
        CentreText($"Floor reached: {state.FloorLevel}   Turns: {state.TurnNumber}",
                   cx, cy + 40, new Color(160, 160, 160));
        CentreText("[R] Restart", cx, cy + 80, new Color(140, 140, 200));
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
