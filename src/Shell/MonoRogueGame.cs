namespace MonoRogue;

using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoRogue.Core;
using MonoRogue.Core.Generation;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;
using MonoRogue.Shell;

public sealed class MonoRogueGame : Game
{
    // ── Graphics ─────────────────────────────────────────────────────────────
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont  _font        = null!;

    // ── Shell subsystems ──────────────────────────────────────────────────────
    private InputHandler  _input    = null!;
    private AsciiRenderer _renderer = null!;

    // ── Core state ────────────────────────────────────────────────────────────
    private GameState    _state    = null!;
    private DataRegistry _registry = null!;
    private GlyphMap     _glyphMap = null!;
    private SeededRandom _rng      = null!;

    // ── Map dimensions (computed from display at load time) ───────────────────
    private int _mapWidth;
    private int _mapHeight;

    private const int VictoryFloor = 6;
    private readonly string? _autoScreenshotPath;
    private bool _screenshotTaken;

    public MonoRogueGame(string? autoScreenshotPath = null)
    {
        _autoScreenshotPath = autoScreenshotPath;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible        = false;
        Window.Title          = "MonoRogue";
    }

    protected override void Initialize()
    {
        // Full-screen using native monitor resolution
        _graphics.PreferredBackBufferWidth  =
            GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
        _graphics.PreferredBackBufferHeight =
            GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
        _graphics.IsFullScreen = true;
        _graphics.ApplyChanges();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font        = Content.Load<SpriteFont>("Fonts/Ascii");

        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content", "Data");
        _registry      = DataRegistry.LoadFrom(contentDir);
        _glyphMap      = GlyphMap.LoadFrom(Path.Combine(contentDir, "glyphs", "glyph_map.json"));

        _input    = new InputHandler();
        _renderer = new AsciiRenderer(_spriteBatch, _font, _glyphMap, GraphicsDevice, _registry);

        // Map fills the display, minus the HUD strip at the top
        var vp     = GraphicsDevice.Viewport;
        _mapWidth  = vp.Width  / AsciiRenderer.TileW;
        _mapHeight = (vp.Height - AsciiRenderer.HudTopPx) / AsciiRenderer.TileH;

        _rng   = new SeededRandom(Environment.TickCount);
        _state = GameState.Empty(_mapWidth, _mapHeight) with { Phase = new MainMenuPhase() };
    }

    // ── Update ────────────────────────────────────────────────────────────────

    protected override void Update(GameTime gameTime)
    {
        _input.Update();

        // ── Escape: close menus in order of priority ──────────────────────────
        if (_input.WasPressed(Keys.Escape))
        {            if (_state.IsCharacterScreenOpen)
            {
                _state = _state with { IsCharacterScreenOpen = false };
                base.Update(gameTime); return;
            }            if (_state.IsInteractionMenuOpen)
            {
                _state = _state with { InteractionTargets = null, InteractionTargetIndex = 0 };
                base.Update(gameTime); return;
            }
            if (_state.IsBarterOpen)
            {
                _state = _state with { ActiveBarter = null };
                base.Update(gameTime); return;
            }
            if (_state.IsInventoryOpen)
            {
                _state = _state with { IsInventoryOpen = false };
                base.Update(gameTime); return;
            }
            if (!_state.InDialogue)
            {
                Exit(); return;
            }
        }

        // Main Menu transition
        if (_state.IsMainMenu)
        {
            if (_input.WasPressed(Keys.Enter) || _input.WasPressed(Keys.Space))
            {
                _rng   = new SeededRandom(Environment.TickCount);
                _state = BuildVillage();
            }
            base.Update(gameTime); return;
        }

        // Restart when dead/victorious
        if (!_state.IsPlaying)
        {
            if (_input.WasPressed(Keys.R))
            {
                _rng   = new SeededRandom(Environment.TickCount);
                _state = BuildVillage();
            }
            base.Update(gameTime); return;
        }

        // Toggle fullscreen with F11
        if (_input.WasPressed(Keys.F11))
        {
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }

        // Toggle inventory with I (only when no other menu is open)
        if (_input.WasPressed(Keys.I) && _state.PlayerEntityId != Guid.Empty
            && !_state.InDialogue && !_state.IsInteractionMenuOpen)
        {
            _state = _state with { IsInventoryOpen = !_state.IsInventoryOpen, InventorySelectedIndex = 0 };
            base.Update(gameTime); return;
        }

        // Toggle character screen with P (only when no other menu is open)
        if (_input.WasPressed(Keys.P) && _state.PlayerEntityId != Guid.Empty
            && !_state.InDialogue && !_state.IsInteractionMenuOpen
            && !_state.IsInventoryOpen && !_state.IsBarterOpen)
        {
            _state = _state with { IsCharacterScreenOpen = !_state.IsCharacterScreenOpen };
            base.Update(gameTime); return;
        }

        // Swallow all other input while character screen is open
        if (_state.IsCharacterScreenOpen) { base.Update(gameTime); return; }

        // ── Interaction target choice menu ────────────────────────────────────
        if (_state.IsInteractionMenuOpen)
        {
            var targets = _state.InteractionTargets!;
            int count   = targets.Count;

            if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
            {
                _state = _state with { InteractionTargetIndex = Math.Max(0, _state.InteractionTargetIndex - 1) };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
            {
                _state = _state with { InteractionTargetIndex = Math.Min(count - 1, _state.InteractionTargetIndex + 1) };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Enter) || _input.WasPressed(Keys.C))
            {
                var chosen = targets[_state.InteractionTargetIndex];
                _state = _state with { InteractionTargets = null, InteractionTargetIndex = 0 };
                ExecutePlayerIntent(new InteractIntent(_state.PlayerEntityId, chosen.EntityId));
                base.Update(gameTime); return;
            }
            // Swallow all other input while menu is open
            base.Update(gameTime); return;
        }

        // ── Inventory navigation ──────────────────────────────────────────────
        if (_state.IsInventoryOpen)
        {
            var player   = _state.TryGetPlayer();
            int itemCount = player?.Inventory?.Items.Length ?? 0;

            if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
            {
                _state = _state with { InventorySelectedIndex = Math.Max(0, _state.InventorySelectedIndex - 1) };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
            {
                _state = _state with { InventorySelectedIndex = Math.Min(Math.Max(0, itemCount - 1), _state.InventorySelectedIndex + 1) };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.U) || _input.WasPressed(Keys.Enter))
            {
                if (player?.Inventory != null && _state.InventorySelectedIndex < itemCount)
                {
                    var selectedId = player.Inventory.Items[_state.InventorySelectedIndex];
                    if (_state.Entities.TryGetValue(selectedId, out var selectedItem) &&
                        selectedItem.Item?.Type == ItemType.Consumable)
                    {
                        _state = _state with { IsInventoryOpen = false };
                        ExecutePlayerIntent(new UseItemIntent(_state.PlayerEntityId, selectedId));
                    }
                    else
                    {
                        _state = _state.AppendMessage("You can't use that here.");
                    }
                }
                base.Update(gameTime); return;
            }
            // Inventory is open — swallow all other input so the world doesn't move
            base.Update(gameTime); return;
        }

        // ── Dialogue options navigation ───────────────────────────────────────
        if (_state.ActiveDialogue?.ShowingOptions == true)
        {
            var dlg = _state.ActiveDialogue;
            if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
            {
                _state = _state with { ActiveDialogue = dlg with { SelectedOption = Math.Max(0, dlg.SelectedOption - 1) } };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
            {
                _state = _state with { ActiveDialogue = dlg with { SelectedOption = Math.Min(dlg.Options.Length - 1, dlg.SelectedOption + 1) } };
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

        // ── Barter screen navigation ──────────────────────────────────────────
        if (_state.IsBarterOpen)
        {
            var barter = _state.ActiveBarter!;
            _state.Entities.TryGetValue(barter.NpcId, out var traderEnt);
            int itemCount = traderEnt?.Inventory?.Items.Length ?? 0;

            if (_input.WasPressed(Keys.Up) || _input.WasPressed(Keys.K))
            {
                _state = _state with { ActiveBarter = barter with { SelectedIndex = Math.Max(0, barter.SelectedIndex - 1) } };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Down) || _input.WasPressed(Keys.J))
            {
                _state = _state with { ActiveBarter = barter with { SelectedIndex = Math.Min(Math.Max(0, itemCount - 1), barter.SelectedIndex + 1) } };
                base.Update(gameTime); return;
            }
            if (_input.WasPressed(Keys.Enter) && itemCount > 0 && barter.SelectedIndex < itemCount)
            {
                var itemId  = traderEnt!.Inventory!.Items[barter.SelectedIndex];
                int prevIdx = barter.SelectedIndex;
                ExecutePlayerIntent(new TradeIntent(_state.PlayerEntityId, barter.NpcId, itemId));
                // Clamp selection after item was removed from trader's inventory
                if (_state.IsBarterOpen)
                {
                    int newCount = _state.Entities.TryGetValue(barter.NpcId, out var nt)
                        ? nt.Inventory?.Items.Length ?? 0 : 0;
                    _state = newCount > 0
                        ? _state with { ActiveBarter = _state.ActiveBarter! with { SelectedIndex = Math.Min(prevIdx, newCount - 1) } }
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

        // ── Collect intent (dialogue-aware) ───────────────────────────────────
        var rawIntent = _state.PlayerEntityId != Guid.Empty
            ? _input.ConsumeIntent(_state.PlayerEntityId, _state.InDialogue)
            : null;

        if (rawIntent is null) { base.Update(gameTime); return; }

        // Descent is handled imperatively in the Shell
        if (rawIntent is DescendIntent) { TryDescend(); base.Update(gameTime); return; }

        // ── Resolve UseFirstConsumable: 0 → message, 1 → direct, >1 → inventory
        Intent playerIntent = rawIntent;
        if (rawIntent is UseFirstConsumableIntent marker)
        {
            var player = _state.TryGetPlayer();
            if (player?.Inventory is null)
            {
                _state = _state.AppendMessage("You have no items to use.");
                base.Update(gameTime); return;
            }

            var consumables = player.Inventory.Items
                .Where(id => _state.Entities.TryGetValue(id, out var it) &&
                             it.Item?.Type == ItemType.Consumable)
                .ToImmutableArray();

            if (consumables.IsEmpty)
            {
                _state = _state.AppendMessage("You have no items to use.");
                base.Update(gameTime); return;
            }
            else if (consumables.Length == 1)
            {
                // Only one consumable — use it directly, no choice needed
                playerIntent = new UseItemIntent(marker.EntityId, consumables[0]);
            }
            else
            {
                // Multiple consumables — open inventory focused on the first one
                int firstIdx = Array.IndexOf(player.Inventory.Items.ToArray(), consumables[0]);
                _state = _state with { IsInventoryOpen = true, InventorySelectedIndex = Math.Max(0, firstIdx) };
                base.Update(gameTime); return;
            }
        }

        // ── Resolve InteractIntent: scan neighbours, build choice menu if needed
        if (rawIntent is InteractIntent { TargetEntityId: null } interactRaw)
        {
            var player = _state.TryGetPlayer();
            if (player != null)
            {
                var neighbors = InteractionSystem.FindInteractableNeighbors(_state, player);

                if (neighbors.IsEmpty)
                {
                    _state = _state.AppendMessage("There is nothing to interact with here.");
                    base.Update(gameTime); return;
                }
                else if (neighbors.Length == 1)
                {
                    // Single target — proceed directly, no menu needed
                    playerIntent = new InteractIntent(interactRaw.EntityId, neighbors[0].EntityId);
                }
                else
                {
                    // Multiple targets — show the choice menu with world positions for highlighting
                    var choices = neighbors
                        .Select(n =>
                        {
                            var worldPos = _state.Entities.TryGetValue(n.EntityId, out var ent)
                                ? ent.Spatial?.Position ?? Position.Zero
                                : Position.Zero;
                            return new InteractionChoice(n.EntityId, n.Label, worldPos);
                        })
                        .ToImmutableList();
                    _state = _state with { InteractionTargets = choices, InteractionTargetIndex = 0 };
                    base.Update(gameTime); return;
                }
            }
        }

        // ── Player tick ───────────────────────────────────────────────────────
        ExecutePlayerIntent(playerIntent);

        base.Update(gameTime);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(8, 10, 14));
        _renderer.Draw(_state, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        base.Draw(gameTime);

        if (_autoScreenshotPath != null && !_screenshotTaken)
        {
            _screenshotTaken = true;
            using var stream = System.IO.File.OpenWrite(_autoScreenshotPath);
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
            using var tex = new Texture2D(GraphicsDevice, w, h);
            var colorData = new Color[w * h];
            GraphicsDevice.GetBackBufferData(colorData);
            tex.SetData(colorData);
            tex.SaveAsPng(stream, w, h);
            Exit();
        }
    }

    // ── Player + AI tick helper ───────────────────────────────────────────────

    private void ExecutePlayerIntent(Intent intent)
    {
        var (stateAfterPlayer, fx1) =
            GameLoop.Tick(_state, [intent], _registry, advanceTurn: true);

        GameState finalState;
        ImmutableArray<Core.Events.SideEffect> fx2 = [];

        if (stateAfterPlayer.IsPlaying && !stateAfterPlayer.InDialogue)
        {
            var aiRng     = _rng.Split(stateAfterPlayer.TurnNumber);
            var aiIntents = EnemyAiSystem.GenerateIntents(stateAfterPlayer, aiRng);
            if (!aiIntents.IsEmpty)
                (finalState, fx2) = GameLoop.Tick(stateAfterPlayer, aiIntents, _registry, advanceTurn: false);
            else
                finalState = stateAfterPlayer;
        }
        else
        {
            finalState = stateAfterPlayer;
        }

        _state = finalState;

        foreach (var fx in fx1.AddRange(fx2))
            if (fx is Core.Events.DialogEvent de)
                _state = _state.AppendMessage(de.Text);
    }

    // ── Descent ───────────────────────────────────────────────────────────────

    private void TryDescend()
    {
        var player = _state.TryGetPlayer();
        if (player?.Spatial is null) return;

        bool onStairs = _state.EntitiesAt(player.Spatial.Position).Any(e => e.IsStairsDown());
        if (!onStairs) { _state = _state.AppendMessage("No stairs here."); return; }

        var next = _state.FloorLevel + 1;
        if (next > VictoryFloor) { _state = _state with { Phase = new VictoryPhase() }; return; }

        _state = _state.AppendMessage($"You descend to floor {next}...");
        _state = BuildDungeonFloor(next, player);
    }

    // ── World builders ────────────────────────────────────────────────────────

    /// <summary>Generates the starting world (village at centre + surrounding biomes).</summary>
    private GameState BuildVillage()
    {
        var freshPlayer = CreateFreshPlayer();
        var worldParams = new WorldGenParams(
            Seed:                _rng.Split(0).Seed,
            ViewportWidth:       _mapWidth,
            ViewportHeight:      _mapHeight,
            VillageWidthMul:     2.0f,
            VillageHeightMul:    2.0f,
            TargetBuildingCount: 10,
            MinBuildingSize:     4,
            MaxBuildingSize:     6,
            GuaranteedNearCount: 4,
            NearZoneRadius:      14
        );
        var world = WorldGenerator.Generate(worldParams, _registry, "background_blacksmith_child", freshPlayer);
        return world with { LocationName = "Thornhaven Village" };
    }

    /// <summary>Generates a dungeon floor (floors 1+), preserving player state.</summary>
    private GameState BuildDungeonFloor(int floorLevel, Entity existingPlayer)
    {
        var rng     = _rng.Split(floorLevel * 1000);
        var dungeon = BspGenerator.Generate(_mapWidth, _mapHeight, rng, floorLevel);
        var spawns  = SpawnPlacer.GetSpawns(dungeon, rng.Split(999), floorLevel);

        var spawnBatch = ImmutableArray.CreateBuilder<Intent>();
        for (int y = 0; y < _mapHeight; y++)
            for (int x = 0; x < _mapWidth; x++)
                spawnBatch.Add(new SpawnIntent(
                    dungeon.IsFloor(x, y) ? "tile_floor" : "tile_wall",
                    new Position(x, y)));

        if (floorLevel < VictoryFloor)
            spawnBatch.Add(new SpawnIntent("tile_stairs_down", dungeon.ExitPosition));

        foreach (var s in spawns)
            spawnBatch.Add(new SpawnIntent(s.TemplateId, s.Position));

        var baseState  = GameState.Empty(_mapWidth, _mapHeight) with { FloorLevel = floorLevel };
        var (state, _) = GameLoop.Tick(baseState, spawnBatch.ToImmutable(), _registry,
                             advanceTurn: false);

        // Place player at spawn
        var p = existingPlayer.MoveTo(dungeon.PlayerSpawn);
        state = state with
        {
            Entities       = state.Entities.SetItem(p.Id, p),
            PlayerEntityId = p.Id,
            FloorLevel     = floorLevel,
        };

        // Carry inventory/equipment items
        if (_state is not null)
        {
            foreach (var id in existingPlayer.Inventory?.Items ?? [])
                if (_state.Entities.TryGetValue(id, out var it))
                    state = state with { Entities = state.Entities.SetItem(id, it) };

            if (existingPlayer.Equipment?.WeaponId is { } wId &&
                _state.Entities.TryGetValue(wId, out var w))
                state = state with { Entities = state.Entities.SetItem(wId, w) };
            if (existingPlayer.Equipment?.ArmorId is { } aId &&
                _state.Entities.TryGetValue(aId, out var a))
                state = state with { Entities = state.Entities.SetItem(aId, a) };
        }

        state = state.AppendMessage($"Floor {floorLevel} - you are level {existingPlayer.Level?.Level ?? 1}.");

        var vis = FovSystem.Compute(state, dungeon.PlayerSpawn, 10);
        return state with { VisibleTiles = vis, ExploredTiles = vis, LocationName = $"Dungeon - Level {floorLevel}" };
    }

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

    private Entity CreateFreshPlayer() => new(
        Id:          Guid.NewGuid(),
        Identity:    new IdentityComponent("Adventurer", "player"),
        Spatial:     new SpatialComponent(Position.Zero, BlocksMovement: true),
        Render:      new RenderComponent("creature_player", "#00FF88"),
        Health:      new HealthComponent(30, 30),
        Player:      new PlayerTag(),
        CombatStats: new CombatStatsComponent(Attack: 3, Defense: 0, XpValue: 0),
        Level:       new LevelComponent(Level: 1, Xp: 0, XpToNextLevel: 100),
        Inventory:   new InventoryComponent([], MaxSlots: 10),
        Equipment:   new EquipmentComponent()
    );
}
