using GameMover.Services;

namespace GameMover.Tests
{
    public class InstallDirectoryMenuTextTests
    {
        [Fact]
        public void ShowsInstallDirectoryForTheSelectedGame()
        {
            var label = InstallDirectoryMenuText.FormatMenuLabel(1, @"D:\Games\Diablo II Resurrected");

            Assert.Equal(@"Install directory: D:\Games\Diablo II Resurrected", label);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UsesPlaceholderWhenInstallDirectoryIsMissing(string? installDirectory)
        {
            var label = InstallDirectoryMenuText.FormatMenuLabel(1, installDirectory);

            Assert.Equal(InstallDirectoryMenuText.MissingLabel, label);
            Assert.Equal("(no install directory)", label);
        }

        [Fact]
        public void UsesPlaceholderWhenNoGameIsSelected()
        {
            Assert.Equal("(no install directory)", InstallDirectoryMenuText.FormatMenuLabel(0, @"D:\Games\Example"));
        }

        [Fact]
        public void UsesPlaceholderWhenSeveralGamesAreSelected()
        {
            Assert.Equal("(multiple games)", InstallDirectoryMenuText.FormatMenuLabel(2, @"D:\Games\Example"));
        }

        [Fact]
        public void EscapesUnderscoresSoThePathStaysVisible()
        {
            var label = InstallDirectoryMenuText.FormatMenuLabel(1, @"D:\Games\Final_Fantasy\__bin");

            Assert.Equal(@"Install directory: D:\Games\Final__Fantasy\____bin", label);
        }
    }
}
