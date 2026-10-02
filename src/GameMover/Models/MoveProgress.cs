namespace GameMover.Models
{
    public static class MoveStage
    {
        public const string Preparing = "Preparing";
        public const string Copying = "Copying";
        public const string Verifying = "Verifying";
        public const string Updating = "Updating Playnite";
        public const string RemovingOriginal = "Removing original folder";
    }

    public sealed class MoveProgress
    {
        public string Stage { get; set; } = string.Empty;

        public string CurrentFile { get; set; } = string.Empty;

        public long BytesCopied { get; set; }

        public long TotalBytes { get; set; }

        public long FilesCopied { get; set; }

        public long TotalFiles { get; set; }
    }
}
