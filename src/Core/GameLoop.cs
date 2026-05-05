namespace MonoRogue.Core;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;

/// <summary>
/// Orchestrates the three-phase functional game loop.
///
/// Phase 1: Intents  — collected externally, passed in as-is.
/// Phase 2: Validate — each domain System reads state, emits Events.
///   2a. Movement, Spawning, Inventory, Interaction, Dialogue
///   2b. Combat (AttackIntents), Trade (TradeIntents)
///   2c. Event-listeners: Loot drops, Quest progress (scan Phase-2a/b events)
/// Phase 3: Reduce   — Reducer folds Events into the new GameState.
/// Post:    FOV      — recomputed from the new player position.
///
/// Dialogue intents are handled here but do NOT advance the turn and
/// do NOT trigger FOV recompute — they only mutate ActiveDialogue.
/// </summary>
public static class GameLoop
{
    private const int FovRadius = 10;

    public static (GameState NewState, ImmutableArray<SideEffect> SideEffects) Tick(
        GameState              state,
        ImmutableArray<Intent> intents,
        DataRegistry           registry,
        bool                   advanceTurn = true)
    {
        var allEvents = ImmutableArray.CreateBuilder<GameEvent>();

        // ── Dialogue intents (never advance the world clock) ─────────────────
        var talks    = intents.OfType<TalkIntent>().ToImmutableArray();
        var advances = intents.OfType<AdvanceDialogueIntent>().ToImmutableArray();
        var closes   = intents.OfType<CloseDialogueIntent>().ToImmutableArray();

        allEvents.AddRange(DialogueSystem.ProcessTalk(state,    talks));
        allEvents.AddRange(DialogueSystem.ProcessAdvance(state, advances));
        allEvents.AddRange(DialogueSystem.ProcessClose(state,   closes));

        // ── If currently in dialogue, don't process world intents ────────────
        if (!state.InDialogue || (!talks.IsEmpty && state.InDialogue == false))
        {
            var moves     = intents.OfType<MoveIntent>().ToImmutableArray();
            var spawns    = intents.OfType<SpawnIntent>().ToImmutableArray();
            var pickups   = intents.OfType<PickupIntent>().ToImmutableArray();
            var useItems  = intents.OfType<UseItemIntent>().ToImmutableArray();
            var equips    = intents.OfType<EquipItemIntent>().ToImmutableArray();
            var interacts = intents.OfType<InteractIntent>().ToImmutableArray();
            var attacks   = intents.OfType<AttackIntent>().ToImmutableArray();
            var trades    = intents.OfType<TradeIntent>().ToImmutableArray();

            // Phase 2a: core world systems
            var moveEvents = MovementSystem.Process(state, moves);
            allEvents.AddRange(moveEvents);
            allEvents.AddRange(DialogueSystem.ProcessBumps(state, moveEvents));
            allEvents.AddRange(SpawnSystem.Process(state, spawns, registry));
            allEvents.AddRange(InventorySystem.ProcessPickups(state, pickups));
            allEvents.AddRange(InventorySystem.ProcessUseItem(state, useItems));
            allEvents.AddRange(InventorySystem.ProcessEquipItem(state, equips));
            allEvents.AddRange(InteractionSystem.Process(state, interacts));

            // Phase 2b: combat and economy
            allEvents.AddRange(CombatSystem.ProcessAttackIntents(state, attacks));
            allEvents.AddRange(EconomySystem.ProcessTrades(state, trades));

            // Phase 2c: event-listener pass (reads Phase 2a/b events, not state)
            //   Loot rolls fire for every DiedEvent regardless of kill source.
            //   Quest kill progress fires for every DiedEvent matching a kill objective.
            //   Quest item turn-in fires for every DialogueOpenedEvent with a quest-giver
            //   NPC — this covers both bump-to-talk (MovementSystem) and TalkIntent.
            var worldEvents   = allEvents.ToImmutable();
            var lootRng       = new SeededRandom(HashCode.Combine(state.TurnNumber, "loot"));
            var questingPlayer = state.TryGetPlayer();
            allEvents.AddRange(CombatSystem.RollAllLoot(state, worldEvents, registry, lootRng));
            allEvents.AddRange(QuestSystem.ProcessEvents(state, worldEvents, registry));

            if (questingPlayer is not null)
            {
                foreach (var evt in worldEvents.OfType<DialogueOpenedEvent>())
                {
                    if (!state.Entities.TryGetValue(evt.NpcId, out var npc)) continue;
                    var npcTemplateId = npc.Identity?.TemplateId ?? "";
                    allEvents.AddRange(QuestSystem.CheckItemTurnIn(state, questingPlayer, npcTemplateId, registry));
                }
            }

            if (advanceTurn)
                allEvents.Add(new TurnAdvancedEvent());
        }

        // ── Phase 3: Reduce ─────────────────────────────────────────────────
        var (newState, sideEffects) = Reducer.Reduce(state, allEvents.ToImmutable());

        // ── Post-reduce: recompute FOV (skip if dialogue just opened/advanced) ─
        var player = newState.TryGetPlayer();
        if (player?.Spatial is not null && newState.IsPlaying)
        {
            var visible     = FovSystem.Compute(newState, player.Spatial.Position, FovRadius);
            var allExplored = newState.ExploredTiles.Union(visible);
            newState = newState with
            {
                VisibleTiles  = visible,
                ExploredTiles = allExplored,
            };
        }

        return (newState, sideEffects);
    }
}
