using GameMover.Services;

namespace GameMover.Tests
{
    public class GameProcessServiceTests
    {
        [Fact]
        public void DetectsProcessInsideInstallDirectory()
        {
            var service = new GameProcessService(() => new[] { @"D:\Games\ABC\bin\game.exe" });

            string? description;
            var running = service.IsAssociatedProcessRunning(@"D:\Games\ABC", out description);

            Assert.True(running);
            Assert.Equal(@"D:\Games\ABC\bin\game.exe", description);
        }

        [Fact]
        public void DoesNotTreatSiblingFolderAsTheGame()
        {
            var service = new GameProcessService(() => new[] { @"D:\Games\ABC2\game.exe", @"C:\Windows\notepad.exe" });

            string? description;
            var running = service.IsAssociatedProcessRunning(@"d:\games\abc\", out description);

            Assert.False(running);
            Assert.Null(description);
        }
    }
}
