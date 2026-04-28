namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;

/// <summary>
/// Pure Phase-2 validation for trade intents.
///
/// A trade transfers one item from a seller's inventory to the buyer's inventory
/// in exchange for gold. Both parties must exist, the seller must own the item,
/// and the buyer must have sufficient gold.
///
/// Emits <see cref="TradeCompletedEvent"/> on success or a
/// <see cref="MessageLoggedEvent"/> explaining the failure.
/// </summary>
public static class EconomySystem
{
    public static ImmutableArray<GameEvent> ProcessTrades(
        GameState                   state,
        ImmutableArray<TradeIntent> intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.BuyerId,  out var buyer))  continue;
            if (!state.Entities.TryGetValue(intent.SellerId, out var seller)) continue;
            if (!state.Entities.TryGetValue(intent.ItemId,   out var item))   continue;

            // Seller must have the item in their inventory
            if (seller.Inventory is null || !seller.Inventory.Items.Contains(intent.ItemId))
            {
                events.Add(new MessageLoggedEvent("That item is not available."));
                continue;
            }

            // Item must have a value
            var unitValue = item.Item?.Value ?? 0;
            var totalCost = unitValue * Math.Max(1, intent.Quantity);

            if (totalCost <= 0)
            {
                events.Add(new MessageLoggedEvent("That item has no price set."));
                continue;
            }

            // Buyer must have enough gold
            var buyerGold = buyer.Inventory?.Gold ?? 0;
            if (buyerGold < totalCost)
            {
                events.Add(new MessageLoggedEvent(
                    $"You need {totalCost} gold. You have {buyerGold}."));
                continue;
            }

            var itemName = item.Identity?.Name ?? "item";
            events.Add(new TradeCompletedEvent(intent.BuyerId, intent.SellerId, intent.ItemId, totalCost));
            events.Add(new MessageLoggedEvent(
                $"You buy {itemName} for {totalCost} gold. ({buyerGold - totalCost} gold remaining)"));
        }

        return events.ToImmutable();
    }
}
