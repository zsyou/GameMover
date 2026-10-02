using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameMover.Models;

namespace GameMover.Services
{
    public sealed class GameMoveService
    {
        private readonly FileCopyService fileCopy;
        private readonly IVerificationService verification;
        private readonly PathRewriteService paths;
        private readonly IMoveLog log;
        private readonly Func<string, long?> freeSpace;

        public GameMoveService(FileCopyService fileCopy, IVerificationService verification, PathRewriteService paths, IMoveLog log)
            : this(fileCopy, verification, paths, log, null)
        {
        }

        public GameMoveService(
            FileCopyService fileCopy,
            IVerificationService verification,
            PathRewriteService paths,
            IMoveLog log,
            Func<string, long?>? freeSpace)
        {
            this.fileCopy = fileCopy ?? throw new ArgumentNullException(nameof(fileCopy));
            this.verification = verification ?? throw new ArgumentNullException(nameof(verification));
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.log = log ?? NullMoveLog.Instance;
            this.freeSpace = freeSpace ?? ProbeFreeSpace;
        }

        public async Task<MoveResult> ExecuteAsync(
            MoveRequest request,
            Func<GameRuntimeState> runtimeProbe,
            Func<string, string, long, int> updateMetadata,
            IProgress<MoveProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (runtimeProbe == null)
            {
                throw new ArgumentNullException(nameof(runtimeProbe));
            }

            if (updateMetadata == null)
            {
                throw new ArgumentNullException(nameof(updateMetadata));
            }

            string destination;
            string? planError;
            if (!paths.TryGetDestination(request.SourceDirectory, request.LibraryDirectory, out destination, out planError))
            {
                log.Warn("Move refused: " + planError);
                return MoveResult.Fail(MoveFailureKind.InvalidRequest, planError ?? "The move path is not valid.");
            }

            log.Info("Move requested for '" + request.GameName + "' (" + request.GameId + ") from '" + request.SourceDirectory + "' to '" + destination + "'.");
            Report(progress, MoveStage.Preparing, string.Empty, 0, 0, 0, 0);

            var pathFailure = ValidatePaths(request.SourceDirectory, request.LibraryDirectory, destination);
            if (pathFailure != null)
            {
                log.Warn(pathFailure.Message);
                return pathFailure;
            }

            GameRuntimeState runtime;
            try
            {
                runtime = runtimeProbe() ?? new GameRuntimeState();
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to check whether the game is running.");
                return MoveResult.Fail(MoveFailureKind.RuntimeCheckFailed, "Couldn't check whether the game is running. Quit it and try again.", destination, ex);
            }

            var running = DescribeRunning(request.GameName, runtime);
            if (running != null)
            {
                log.Warn(running);
                return MoveResult.Fail(MoveFailureKind.GameRunning, running, destination);
            }

            DirectoryInventory inventory;
            try
            {
                inventory = DirectoryInventoryScanner.Scan(request.SourceDirectory, log, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                log.Info("Move cancelled before copy started.");
                return Cancelled(destination, false);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to read the install folder.");
                return MoveResult.Fail(MoveFailureKind.SourceMissing, "Couldn't read the install folder: " + ex.Message, destination, ex);
            }

            log.Info("Source contains " + inventory.Files.Count + " file(s), " + inventory.TotalBytes + " byte(s).");
            var available = freeSpace(destination);
            if (available == null)
            {
                log.Warn("Free space could not be checked for " + destination + ". Continuing.");
            }
            else if (available.Value < inventory.TotalBytes)
            {
                var spaceMessage = "The target drive does not have enough free space. Need " +
                                   ByteSize.Format(inventory.TotalBytes) + ", available " + ByteSize.Format(available.Value) + ".";
                log.Warn(spaceMessage);
                return MoveResult.Fail(MoveFailureKind.NotEnoughSpace, spaceMessage, destination);
            }
            else
            {
                log.Info("Free space on target: " + ByteSize.Format(available.Value) + ".");
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                return MoveResult.Fail(MoveFailureKind.TargetExists, "Target folder already exists.", destination);
            }

            var copy = await fileCopy.CopyAsync(inventory, destination, progress, cancellationToken).ConfigureAwait(false);
            if (copy.Status == FileCopyStatus.Cancelled)
            {
                return Cancelled(destination, copy.DestinationCreated);
            }

            if (copy.Status != FileCopyStatus.Success)
            {
                return MoveResult.Fail(MoveFailureKind.CopyFailed, "Copy failed: " + copy.Message, destination, copy.Error, copy.DestinationCreated);
            }

            try
            {
                runtime = runtimeProbe() ?? new GameRuntimeState();
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to recheck whether the game is running.");
                return MoveResult.Fail(MoveFailureKind.RuntimeCheckFailed, "Couldn't check whether the game is running after the copy. The original folder was kept.", destination, ex, true);
            }

            running = DescribeRunning(request.GameName, runtime);
            if (running != null)
            {
                log.Warn("Game started during copy. " + running);
                return MoveResult.Fail(MoveFailureKind.GameRunning, running + " The original folder was kept.", destination, null, true);
            }

            Report(progress, MoveStage.Verifying, string.Empty, inventory.TotalBytes, inventory.TotalBytes, inventory.Files.Count, inventory.Files.Count);
            VerificationResult verified;
            try
            {
                verified = await verification.VerifyAsync(request.SourceDirectory, destination, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                log.Info("Move cancelled during verification. Source was kept.");
                return Cancelled(destination, true);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Verification failed.");
                return MoveResult.Fail(MoveFailureKind.VerifyFailed, "Verification failed: " + ex.Message, destination, ex, true);
            }

            if (!verified.Match)
            {
                return MoveResult.Fail(MoveFailureKind.VerifyFailed, verified.Message + " The original folder was kept.", destination, null, true);
            }

            Report(progress, MoveStage.Updating, string.Empty, inventory.TotalBytes, inventory.TotalBytes, inventory.Files.Count, inventory.Files.Count);
            int pathWarnings;
            try
            {
                pathWarnings = updateMetadata(Path.GetFullPath(request.SourceDirectory), Path.GetFullPath(destination), inventory.TotalBytes);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Playnite metadata update failed. Source was kept.");
                return MoveResult.Fail(MoveFailureKind.MetadataFailed, "Playnite could not save the new folder: " + ex.Message + " The original folder was kept.", destination, ex, true);
            }

            Report(progress, MoveStage.RemovingOriginal, string.Empty, inventory.TotalBytes, inventory.TotalBytes, inventory.Files.Count, inventory.Files.Count);
            try
            {
                // Source is removed only after the copy verified and Playnite was updated.
                fileCopy.DeleteTree(request.SourceDirectory);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Copied and updated Playnite, but could not delete the original folder.");
                return new MoveResult
                {
                    Status = MoveStatus.Failed,
                    Failure = MoveFailureKind.SourceDeleteFailed,
                    Message = "The game now points at " + destination + ", but the original folder could not be deleted: " + ex.Message,
                    Destination = destination,
                    DestinationCreated = true,
                    MetadataUpdated = true,
                    SourceDeleted = false,
                    PathWarnings = pathWarnings,
                    Error = ex
                };
            }

            var message = "Moved " + request.GameName + " to " + destination + ".";
            if (inventory.SkippedReparsePoints > 0)
            {
                message += " Skipped " + inventory.SkippedReparsePoints + " junction(s) or symlink(s); see the log.";
            }

            log.Info(message);
            return new MoveResult
            {
                Status = MoveStatus.Success,
                Message = message,
                Destination = destination,
                DestinationCreated = true,
                MetadataUpdated = true,
                SourceDeleted = true,
                PathWarnings = pathWarnings
            };
        }

        private MoveResult? ValidatePaths(string source, string library, string destination)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return MoveResult.Fail(MoveFailureKind.InvalidRequest, "This game has no install directory.");
            }

            if (!Directory.Exists(source))
            {
                return MoveResult.Fail(MoveFailureKind.SourceMissing, "The install folder does not exist: " + source);
            }

            if (string.IsNullOrWhiteSpace(library) || !Directory.Exists(library))
            {
                return MoveResult.Fail(MoveFailureKind.LibraryMissing, "The target library folder does not exist: " + library, destination);
            }

            string normalizedSource;
            string normalizedDestination;
            if (!PathRewriteService.TryNormalizeAbsolute(source, out normalizedSource) ||
                !PathRewriteService.TryNormalizeAbsolute(destination, out normalizedDestination))
            {
                return MoveResult.Fail(MoveFailureKind.InvalidRequest, "Source and destination must be absolute paths.", destination);
            }

            if (normalizedSource.Equals(normalizedDestination, StringComparison.OrdinalIgnoreCase))
            {
                return MoveResult.Fail(MoveFailureKind.SameLocation, "The target folder is the same as the current install folder.", destination);
            }

            if (paths.IsUnderRoot(destination, source) || paths.IsUnderRoot(source, destination))
            {
                return MoveResult.Fail(MoveFailureKind.NestedLocation, "The target folder cannot be inside the current install folder, or the other way around.", destination);
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                return MoveResult.Fail(MoveFailureKind.TargetExists, "Target folder already exists.", destination);
            }

            return null;
        }

        private long? ProbeFreeSpace(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root))
                {
                    log.Warn("Could not determine the drive for " + path + ".");
                    return null;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    log.Warn("Drive is not ready: " + root);
                    return null;
                }

                return drive.AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                log.Warn("Could not read free space for " + path + ": " + ex.Message);
                return null;
            }
        }

        private static string? DescribeRunning(string gameName, GameRuntimeState runtime)
        {
            if (!runtime.IsBusy)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(runtime.AssociatedProcess))
            {
                return "Quit " + gameName + " before moving it. A running process was detected: " + runtime.AssociatedProcess;
            }

            if (runtime.IsInstalling)
            {
                return "Quit the installer for " + gameName + " before moving it.";
            }

            if (runtime.IsUninstalling)
            {
                return gameName + " is being uninstalled. Wait for that to finish.";
            }

            return "Quit " + gameName + " before moving it. Playnite still marks it as running.";
        }

        private static MoveResult Cancelled(string destination, bool destinationCreated)
        {
            return new MoveResult
            {
                Status = MoveStatus.Cancelled,
                Failure = MoveFailureKind.None,
                Message = "Move cancelled. The original folder was kept.",
                Destination = destination,
                DestinationCreated = destinationCreated
            };
        }

        private static void Report(IProgress<MoveProgress>? progress, string stage, string currentFile, long copied, long total, long filesCopied, long totalFiles)
        {
            progress?.Report(new MoveProgress
            {
                Stage = stage,
                CurrentFile = currentFile,
                BytesCopied = copied,
                TotalBytes = total,
                FilesCopied = filesCopied,
                TotalFiles = totalFiles
            });
        }
    }
}
