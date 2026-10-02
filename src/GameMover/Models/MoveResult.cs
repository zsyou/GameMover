using System;

namespace GameMover.Models
{
    public enum MoveStatus
    {
        Success,
        Cancelled,
        Failed
    }

    public enum MoveFailureKind
    {
        None,
        InvalidRequest,
        SourceMissing,
        LibraryMissing,
        SameLocation,
        NestedLocation,
        TargetExists,
        NotEnoughSpace,
        GameRunning,
        RuntimeCheckFailed,
        CopyFailed,
        VerifyFailed,
        MetadataFailed,
        SourceDeleteFailed
    }

    public sealed class MoveResult
    {
        public MoveStatus Status { get; set; }

        public MoveFailureKind Failure { get; set; }

        public string Message { get; set; } = string.Empty;

        public string? Destination { get; set; }

        public bool DestinationCreated { get; set; }

        public bool MetadataUpdated { get; set; }

        public bool SourceDeleted { get; set; }

        public int PathWarnings { get; set; }

        public Exception? Error { get; set; }

        public static MoveResult Fail(MoveFailureKind kind, string message, string? destination = null, Exception? error = null, bool destinationCreated = false)
        {
            return new MoveResult
            {
                Status = MoveStatus.Failed,
                Failure = kind,
                Message = message,
                Destination = destination,
                DestinationCreated = destinationCreated,
                Error = error
            };
        }
    }
}
