using System;
using System.Collections.Immutable;
using Xunit;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Events;
using MonoRogue.Core;
using MonoRogue.Data;
using System.IO;

namespace MonoRogue.Tests
{
    public class DialogueCrashTests
    {
        [Fact]
        public void TalkingToBlacksmith_ShouldNotCrash()
        {
            // Assuming we run from project root, or we need to find Content path
            var contentDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Content/Data");
            if (!Directory.Exists(contentDir)) 
                contentDir = "Content/Data";

            var registry = DataRegistry.LoadFrom(contentDir);

            var playerTemplate = registry.Templates["npc_villager"];
            var player = SpawnSystem.BuildEntity(playerTemplate, new Position(0, 0), isPlayer: true);
            player = player with { Background = new BackgroundComponent("blacksmith_child") };

            var blacksmithTemplate = registry.Templates["npc_blacksmith"];
            var blacksmith = SpawnSystem.BuildEntity(blacksmithTemplate, new Position(1, 0), isPlayer: false);

            var state = GameState.Empty(10, 10) with
            {
                TurnNumber = 1,
                Entities = ImmutableDictionary<Guid, Entity>.Empty.Add(player.Id, player).Add(blacksmith.Id, blacksmith),
                PlayerEntityId = player.Id
            };

            // Simulate talking
            var talkIntent = new TalkIntent(player.Id);
            var intents = ImmutableArray.Create<Intent>(talkIntent);

            var (newState, _) = GameLoop.Tick(state, intents, registry);

            Assert.True(newState.InDialogue);
            Assert.NotNull(newState.ActiveDialogue);
        }

        [Fact]
        public void AllDialogueStrings_ShouldOnlyContainAsciiCharacters()
        {
            var contentDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Content/Data");
            if (!Directory.Exists(contentDir)) 
                contentDir = "Content/Data";

            var registry = DataRegistry.LoadFrom(contentDir);

            foreach (var template in registry.Templates.Values)
            {
                if (template.DialogPool == null)
                    continue;

                foreach (var pool in template.DialogPool)
                {
                    foreach (var line in pool.Value)
                    {
                        foreach (char c in line)
                        {
                            // Allow standard printable ASCII (32-126) plus common whitespace
                            bool isAsciiPrintable = c >= 32 && c <= 126;
                            bool isWhitespace = c == '\n' || c == '\r' || c == '\t';
                            
                            Assert.True(isAsciiPrintable || isWhitespace, 
                                $"Template '{template.Id}' DialogPool '{pool.Key}' contains invalid character '{c}' (U+{(int)c:X4}) in line: \"{line}\". " +
                                "SpriteFont requires standard ASCII characters. Replace em-dashes, fancy quotes, or unicode with standard equivalents.");
                        }
                    }
                }
            }
        }
    }
}
