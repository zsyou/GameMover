namespace GameMover.Models
{
    public sealed class GameRuntimeState
    {
        public bool IsRunning { get; set; }

        public bool IsLaunching { get; set; }

        public bool IsInstalling { get; set; }

        public bool IsUninstalling { get; set; }

        public string? AssociatedProcess { get; set; }

        public bool IsBusy =>
            IsRunning ||
            IsLaunching ||
            IsInstalling ||
            IsUninstalling ||
            !string.IsNullOrEmpty(AssociatedProcess);
    }
}
