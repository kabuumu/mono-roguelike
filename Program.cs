using System.Linq;

var autoScreenshotPath = args.FirstOrDefault();
using var game = new MonoRogue.MonoRogueGame(autoScreenshotPath);
game.Run();
