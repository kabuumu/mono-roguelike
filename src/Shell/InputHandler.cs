namespace MonoRogue.Shell;

using Microsoft.Xna.Framework.Input;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;

/// <summary>
/// Pure turn-based input handler.
/// Knows whether dialogue is active and routes accordingly.
/// </summary>
public sealed class InputHandler
{
    private KeyboardState _previous;
    private KeyboardState _current;

    public void Update()
    {
        _previous = _current;
        _current  = Keyboard.GetState();
    }

    /// <returns>
    /// An Intent for this tick, or <c>null</c> if no actionable key was pressed.
    /// When <paramref name="inDialogue"/> is true only dialogue intents are returned.
    /// </returns>
    public Intent? ConsumeIntent(Guid playerEntityId, bool inDialogue)
    {
        // ── Dialogue mode: only advance or close ─────────────────────────────
        if (inDialogue)
        {
            if (WasPressed(Keys.Escape))
                return new CloseDialogueIntent();
            if (WasPressed(Keys.Space) || WasPressed(Keys.Enter) || WasPressed(Keys.T))
                return new AdvanceDialogueIntent();
            return null;
        }

        // ── Normal mode ───────────────────────────────────────────────────────

        // Movement
        if (WasPressed(Keys.Up)    || WasPressed(Keys.K) || WasPressed(Keys.NumPad8))
            return new MoveIntent(playerEntityId, Position.North);
        if (WasPressed(Keys.Down)  || WasPressed(Keys.J) || WasPressed(Keys.NumPad2))
            return new MoveIntent(playerEntityId, Position.South);
        if (WasPressed(Keys.Left)  || WasPressed(Keys.H) || WasPressed(Keys.NumPad4))
            return new MoveIntent(playerEntityId, Position.West);
        if (WasPressed(Keys.Right) || WasPressed(Keys.L) || WasPressed(Keys.NumPad6))
            return new MoveIntent(playerEntityId, Position.East);

        // Diagonals
        if (WasPressed(Keys.NumPad7)) return new MoveIntent(playerEntityId, Position.North + Position.West);
        if (WasPressed(Keys.NumPad9)) return new MoveIntent(playerEntityId, Position.North + Position.East);
        if (WasPressed(Keys.NumPad1)) return new MoveIntent(playerEntityId, Position.South + Position.West);
        if (WasPressed(Keys.NumPad3)) return new MoveIntent(playerEntityId, Position.South + Position.East);

        // Wait
        if (WasPressed(Keys.Space) || WasPressed(Keys.NumPad5) || WasPressed(Keys.OemPeriod))
            return new WaitIntent(playerEntityId);

        // Talk (T = search adjacent NPCs)
        if (WasPressed(Keys.T))
            return new TalkIntent(playerEntityId);

        // Interact / Gather / Craft
        if (WasPressed(Keys.C))
            return new InteractIntent(playerEntityId);

        // Pickup
        if (WasPressed(Keys.G))
            return new PickupIntent(playerEntityId);

        // Use first consumable
        if (WasPressed(Keys.U))
            return new UseFirstConsumableIntent(playerEntityId);

        // Descend stairs
        if (WasPressed(Keys.OemPeriod) && IsHeld(Keys.LeftShift))
            return new DescendIntent(playerEntityId);

        return null;
    }

    public bool WasPressed(Keys key) =>
        _current.IsKeyDown(key) && !_previous.IsKeyDown(key);

    public bool IsHeld(Keys key) => _current.IsKeyDown(key);
}

/// <summary>Shell-only marker; resolved to UseItemIntent before entering GameLoop.</summary>
public sealed record UseFirstConsumableIntent(Guid EntityId) : Intent;
