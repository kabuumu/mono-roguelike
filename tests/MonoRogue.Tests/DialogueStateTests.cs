namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Model;
using Xunit;

/// <summary>
/// Unit tests for the DialogueState record — pure model, no system calls needed.
/// </summary>
public sealed class DialogueStateTests
{
    private static DialogueState Make(params string[] lines) =>
        new(Guid.NewGuid(), "TestNpc", lines.ToImmutableArray(), CurrentLine: 0);

    [Fact]
    public void CurrentText_returns_first_line_at_index_0()
    {
        var d = Make("Hello!", "Goodbye.");

        Assert.Equal("Hello!", d.CurrentText);
    }

    [Fact]
    public void CurrentText_returns_correct_line_after_advance()
    {
        var d     = Make("Line A", "Line B", "Line C");
        var after = d.Advance()!; // advances to index 1

        Assert.Equal("Line B", after.CurrentText);
    }

    [Fact]
    public void Advance_returns_null_on_last_line()
    {
        var d = Make("Only line");

        Assert.True(d.IsLastLine);
        Assert.Null(d.Advance());
    }

    [Fact]
    public void IsLastLine_true_when_current_equals_length_minus_one()
    {
        var d     = Make("A", "B");
        var last  = d.Advance()!;

        Assert.True(last.IsLastLine);
    }

    [Fact]
    public void CurrentText_is_empty_string_when_no_lines()
    {
        var d = new DialogueState(Guid.NewGuid(), "NPC",
            ImmutableArray<string>.Empty, CurrentLine: 0);

        // Should not throw and should return empty string
        Assert.Equal("", d.CurrentText);
    }

    [Fact]
    public void Advance_through_all_lines_then_returns_null()
    {
        var d = Make("1", "2", "3");

        d = d.Advance()!; // → index 1
        d = d.Advance()!; // → index 2 (last)
        Assert.True(d.IsLastLine);

        var final = d.Advance();
        Assert.Null(final);
    }
}
