using GameMover.Services;

namespace GameMover.Tests
{
    public class PathRewriteServiceTests
    {
        private readonly PathRewriteService paths = new PathRewriteService();

        [Fact]
        public void BuildsDestinationFromLibraryAndSourceFolderName()
        {
            string destination;
            string? error;
            var ok = paths.TryGetDestination(@"D:\Games\Diablo II Resurrected", @"E:\Games", out destination, out error);

            Assert.True(ok, error);
            Assert.Equal(@"E:\Games\Diablo II Resurrected", destination);
        }

        [Theory]
        [InlineData(@"D:\Games\ABC\game.exe", @"D:\Games\ABC", @"E:\Games\ABC", @"E:\Games\ABC\game.exe")]
        [InlineData(@"d:\games\abc\Game.EXE", @"D:\Games\ABC\", @"E:\Games\ABC", @"E:\Games\ABC\Game.EXE")]
        [InlineData(@"D:/Games/ABC/bin/game.exe", @"D:\Games\ABC", @"E:\Games\ABC", @"E:\Games\ABC\bin\game.exe")]
        [InlineData(@"D:\Games\ABC", @"D:\Games\ABC", @"E:\Library\ABC", @"E:\Library\ABC")]
        public void RewritesPathsUnderRoot(string original, string oldRoot, string newRoot, string expected)
        {
            var outcome = paths.RewritePath(original, oldRoot, newRoot);

            Assert.True(outcome.Changed);
            Assert.Equal(expected, outcome.Result);
            Assert.Null(outcome.Warning);
        }

        [Fact]
        public void DoesNotRewriteSiblingPrefix()
        {
            var outcome = paths.RewritePath(@"D:\Games\ABC2\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(outcome.Changed);
            Assert.Equal(@"D:\Games\ABC2\game.exe", outcome.Result);
            Assert.Null(outcome.Warning);
        }

        [Fact]
        public void PreservesQuotes()
        {
            var outcome = paths.RewritePath(@"""D:\Games\ABC\My Game.exe""", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.True(outcome.Changed);
            Assert.Equal(@"""E:\Games\ABC\My Game.exe""", outcome.Result);
        }

        [Fact]
        public void LeavesEnvironmentVariablesUnchangedAndWarnsWhenTheyContainTheOldRoot()
        {
            var outcome = paths.RewritePath(@"D:\Games\ABC\%MOD%\file.dat", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(outcome.Changed);
            Assert.Equal(@"D:\Games\ABC\%MOD%\file.dat", outcome.Result);
            Assert.NotNull(outcome.Warning);
        }

        [Fact]
        public void LeavesUnrelatedEnvironmentPathsAlone()
        {
            var outcome = paths.RewritePath(@"%ProgramFiles%\Other\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(outcome.Changed);
            Assert.Null(outcome.Warning);
        }

        [Fact]
        public void LeavesRelativeAndVariablePathsAlone()
        {
            var relative = paths.RewritePath(@"game.exe", @"D:\Games\ABC", @"E:\Games\ABC");
            var variable = paths.RewritePath(@"{InstallDir}\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(relative.Changed);
            Assert.Null(relative.Warning);
            Assert.False(variable.Changed);
            Assert.Null(variable.Warning);
        }

        [Fact]
        public void WarnsWhenVariableTextAlsoContainsTheOldRoot()
        {
            var outcome = paths.RewritePath(@"{InstallDir}\D:\Games\ABC\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(outcome.Changed);
            Assert.NotNull(outcome.Warning);
        }

        [Fact]
        public void RewritesQuotedArgumentsAndSkipsSiblings()
        {
            var warnings = new List<string>();
            var rewritten = paths.RewriteArgumentText(
                @"""D:\Games\ABC\game.exe"" -mod ""D:\Games\ABC2\extra"" --data D:\Games\ABC\Data\data.bin",
                @"D:\Games\ABC",
                @"E:\Games\ABC",
                warnings);

            Assert.Equal(@"""E:\Games\ABC\game.exe"" -mod ""D:\Games\ABC2\extra"" --data E:\Games\ABC\Data\data.bin", rewritten);
            Assert.Empty(warnings);
        }

        [Fact]
        public void RewritesExtendedLengthPaths()
        {
            var outcome = paths.RewritePath(@"\\?\D:\Games\ABC\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.True(outcome.Changed);
            Assert.Equal(@"\\?\E:\Games\ABC\game.exe", outcome.Result);
        }

        [Fact]
        public void DoesNotTreatDriveLetterAloneAsTheRoot()
        {
            var outcome = paths.RewritePath(@"D:\Other\game.exe", @"D:\Games\ABC", @"E:\Games\ABC");

            Assert.False(outcome.Changed);
            Assert.Equal(@"D:\Other\game.exe", outcome.Result);
        }
    }
}
