using System;

namespace GameMover.Services
{
    public static class InstallDirectoryMenuText
    {
        public const string MissingLabel = "(no install directory)";
        public const string MultipleLabel = "(multiple games)";

        public static string FormatMenuLabel(int selectedGameCount, string? installDirectory)
        {
            if (selectedGameCount != 1)
            {
                return selectedGameCount > 1 ? MultipleLabel : MissingLabel;
            }

            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return MissingLabel;
            }

            // Playnite's desktop MenuItem template sets RecognizesAccessKey, so a single
            // underscore would hide the following character. Doubling keeps the path literal.
            return "Install directory: " + installDirectory!.Replace("_", "__");
        }
    }
}
