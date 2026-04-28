namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;

public static class InteractionSystem
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all adjacent entities the actor can interact with, with display labels.
    /// Used by the shell to build a choice menu before emitting an InteractIntent.
    /// </summary>
    public static ImmutableArray<(Guid EntityId, string Label)> FindInteractableNeighbors(
        GameState state, Entity actor)
    {
        if (actor.Spatial is null) return [];

        var results = ImmutableArray.CreateBuilder<(Guid, string)>();
        foreach (var pos in NeighborPositions(actor.Spatial.Position))
        {
            if (!state.InBounds(pos)) continue;
            foreach (var target in state.EntitiesAt(pos))
            {
                var label = InteractableLabel(target);
                if (label is not null)
                    results.Add((target.Id, label));
            }
        }
        return results.ToImmutable();
    }

    public static ImmutableArray<GameEvent> Process(
        GameState state,
        ImmutableArray<InteractIntent> intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var entity)) continue;
            if (entity.Spatial is null) continue;

            if (intent.TargetEntityId.HasValue)
            {
                // Targeted: the player already chose a specific entity
                if (state.Entities.TryGetValue(intent.TargetEntityId.Value, out var specificTarget))
                    events.AddRange(ProcessTarget(state, intent.EntityId, entity, specificTarget));
            }
            else
            {
                // Auto-scan: first interactable neighbour wins (legacy path)
                bool interacted = false;
                foreach (var pos in NeighborPositions(entity.Spatial.Position))
                {
                    if (!state.InBounds(pos)) continue;
                    foreach (var target in state.EntitiesAt(pos))
                    {
                        var targetEvents = ProcessTarget(state, intent.EntityId, entity, target);
                        if (!targetEvents.IsEmpty)
                        {
                            events.AddRange(targetEvents);
                            interacted = true;
                            break;
                        }
                    }
                    if (interacted) break;
                }
            }
        }

        return events.ToImmutable();
    }

    // ── Per-target logic ──────────────────────────────────────────────────────

    private static ImmutableArray<GameEvent> ProcessTarget(
        GameState state, Guid actorId, Entity actor, Entity target)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        switch (target.Identity?.TemplateId)
        {
            case "env_river_tile":
            {
                bool hasBucketResource = HasResource(actor, "item_empty_bucket");
                Guid? bucketEntityId   = FindItemByTemplate(state, actor, "item_empty_bucket");

                if (hasBucketResource || bucketEntityId.HasValue)
                {
                    if (hasBucketResource)
                        events.Add(new InventoryChangedEvent(actorId, "item_empty_bucket", -1));
                    else
                        events.Add(new ItemUsedEvent(actorId, bucketEntityId!.Value, 0));

                    events.Add(new InventoryChangedEvent(actorId, "item_water_bucket", 1));

                    events.Add(new MessageLoggedEvent("You fill the bucket with cool river water."));
                }
                else
                {
                    events.Add(new MessageLoggedEvent("You need an empty bucket to fetch water."));
                }
                break;
            }

            case "env_tree":
            {
                if (HasItem(state, actor, "item_axe"))
                {
                    events.Add(new InventoryChangedEvent(actorId, "item_log", 1));
                    events.Add(new MessageLoggedEvent("You chop some wood from the tree."));
                }
                else
                {
                    events.Add(new MessageLoggedEvent("You need an axe to chop this tree."));
                }
                break;
            }

            case "env_forge":
            {
                if (HasResource(actor, "item_log"))
                {
                    events.Add(new InventoryChangedEvent(actorId, "item_log", -1));
                    events.Add(new InventoryChangedEvent(actorId, "item_plank", 2));
                    events.Add(new MessageLoggedEvent("You turn a log into planks at the forge."));
                }
                else
                {
                    events.Add(new MessageLoggedEvent("The forge is cold. Bring wood logs to smelt."));
                }
                break;
            }
        }

        return events.ToImmutable();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a friendly display label if this entity is interactable, null otherwise.</summary>
    private static string? InteractableLabel(Entity entity) =>
        entity.Identity?.TemplateId switch
        {
            "env_river_tile" => entity.Identity?.Name ?? "River",
            "env_tree"       => entity.Identity?.Name ?? "Tree",
            "env_forge"      => entity.Identity?.Name ?? "Forge",
            _                => null,
        };

    private static Position[] NeighborPositions(Position p) =>
    [
        p + Position.North,
        p + Position.South,
        p + Position.East,
        p + Position.West,
        p + Position.North + Position.East,
        p + Position.North + Position.West,
        p + Position.South + Position.East,
        p + Position.South + Position.West,
    ];

    private static bool HasResource(Entity entity, string templateId)
    {
        if (entity.Inventory?.Resources == null) return false;
        return entity.Inventory.Resources.TryGetValue(templateId, out var count) && count > 0;
    }

    private static Guid? FindItemByTemplate(GameState state, Entity entity, string templateId)
    {
        if (entity.Inventory is null) return null;
        foreach (var id in entity.Inventory.Items)
            if (state.Entities.TryGetValue(id, out var it) && it.Identity?.TemplateId == templateId)
                return id;
        return null;
    }

    private static bool HasItem(GameState state, Entity entity, string templateId)
    {
        if (entity.Inventory == null) return false;
        foreach (var id in entity.Inventory.Items)
            if (state.Entities.TryGetValue(id, out var it) && it.Identity?.TemplateId == templateId)
                return true;
        if (entity.Equipment?.WeaponId is { } wid &&
            state.Entities.TryGetValue(wid, out var w) &&
            w.Identity?.TemplateId == templateId)
            return true;
        return false;
    }

}
