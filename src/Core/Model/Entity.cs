namespace MonoRogue.Core.Model;

/// <summary>
/// A game entity is a bag of optional components keyed by a unique ID.
/// Use <c>with</c> expressions to produce mutations — never modify in place.
/// </summary>
public sealed record Entity(
    Guid Id,
    IdentityComponent?   Identity    = null,
    SpatialComponent?    Spatial     = null,
    RenderComponent?     Render      = null,
    HealthComponent?     Health      = null,
    PlayerTag?           Player      = null,
    TileTag?             Tile        = null,
    CombatStatsComponent? CombatStats = null,
    LevelComponent?      Level       = null,
    ItemComponent?       Item        = null,
    InventoryComponent?  Inventory   = null,
    EquipmentComponent?  Equipment   = null,
    AiComponent?         Ai          = null,
    StairsDownTag?       StairsDown  = null,
    DialogueComponent?   Dialogue    = null,
    PeacefulTag?         Peaceful    = null,
    BackgroundComponent? Background  = null,
    SkillsComponent?     Skills      = null
);

/// <summary>Pure helper queries over an entity — no mutation.</summary>
public static class EntityExtensions
{
    public static bool IsPlayer(this Entity e)       => e.Player is not null;
    public static bool IsTile(this Entity e)         => e.Tile is not null;
    public static bool BlocksMovement(this Entity e) => e.Spatial?.BlocksMovement ?? false;
    public static bool IsAlive(this Entity e)        => e.Health is { Current: > 0 };
    public static bool IsItem(this Entity e)         => e.Item is not null;
    public static bool IsEnemy(this Entity e)        => e.Ai is not null;
    public static bool IsStairsDown(this Entity e)   => e.StairsDown is not null;
    public static bool IsNpc(this Entity e)          => e.Dialogue is not null;
    public static bool IsPeaceful(this Entity e)     => e.Peaceful is not null;

    /// <summary>
    /// Produces a new Entity with the SpatialComponent updated to <paramref name="pos"/>.
    /// Returns the entity unchanged if it has no SpatialComponent.
    /// </summary>
    public static Entity MoveTo(this Entity e, Position pos) =>
        e.Spatial is null ? e : e with { Spatial = e.Spatial with { Position = pos } };
}
