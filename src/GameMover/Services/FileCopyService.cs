using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameMover.Models;

namespace GameMover.Services
{
    public enum FileCopyStatus
    {
        Success,
        Cancelled,
        Failed
    }

    public sealed class FileCopyResult
    {
        public FileCopyStatus Status { get; set; }

        public string Message { get; set; } = string.Empty;

        public bool DestinationCreated { get; set; }

        public Exception? Error { get; set; }
    }

    public sealed class FileCopyService
    {
        private const int BufferSize = 256 * 1024;
        private readonly IMoveLog log;

        public FileCopyService(IMoveLog log)
        {
            this.log = log ?? NullMoveLog.Instance;
        }

        public async Task<FileCopyResult> CopyAsync(
            DirectoryInventory inventory,
            string destination,
            IProgress<MoveProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (string.IsNullOrWhiteSpace(destination))
            {
                return Failed("Destination is empty.");
            }

            var destinationFull = Path.GetFullPath(destination);
            if (Directory.Exists(destinationFull) || File.Exists(destinationFull))
            {
                return Failed("Target folder already exists.");
            }

            Directory.CreateDirectory(destinationFull);
            var created = true;
            long copiedBytes = 0;
            long copiedFiles = 0;
            try
            {
                foreach (var directory in inventory.Directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.Combine(destinationFull, directory));
                }

                foreach (var file in inventory.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePath = Path.Combine(inventory.Root, file.RelativePath);
                    var targetPath = Path.Combine(destinationFull, file.RelativePath);
                    var parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    progress?.Report(new MoveProgress
                    {
                        Stage = MoveStage.Copying,
                        CurrentFile = file.RelativePath,
                        BytesCopied = copiedBytes,
                        TotalBytes = inventory.TotalBytes,
                        FilesCopied = copiedFiles,
                        TotalFiles = inventory.Files.Count
                    });

                    await CopyFileAsync(sourcePath, targetPath, (delta) =>
                    {
                        copiedBytes += delta;
                        progress?.Report(new MoveProgress
                        {
                            Stage = MoveStage.Copying,
                            CurrentFile = file.RelativePath,
                            BytesCopied = copiedBytes,
                            TotalBytes = inventory.TotalBytes,
                            FilesCopied = copiedFiles,
                            TotalFiles = inventory.Files.Count
                        });
                    }, cancellationToken).ConfigureAwait(false);

                    copiedFiles++;
                }

                progress?.Report(new MoveProgress
                {
                    Stage = MoveStage.Copying,
                    CurrentFile = string.Empty,
                    BytesCopied = copiedBytes,
                    TotalBytes = inventory.TotalBytes,
                    FilesCopied = copiedFiles,
                    TotalFiles = inventory.Files.Count
                });

                log.Info("Copied " + copiedFiles + " file(s), " + copiedBytes + " byte(s), to " + destinationFull);
                return new FileCopyResult
                {
                    Status = FileCopyStatus.Success,
                    DestinationCreated = true,
                    Message = "Copied " + copiedFiles + " file(s)."
                };
            }
            catch (OperationCanceledException)
            {
                log.Info("Copy cancelled. Source was kept. Partial destination: " + destinationFull);
                return new FileCopyResult
                {
                    Status = FileCopyStatus.Cancelled,
                    DestinationCreated = created,
                    Message = "Copy cancelled."
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log.Error(ex, "Copy failed. Source was kept.");
                return new FileCopyResult
                {
                    Status = FileCopyStatus.Failed,
                    DestinationCreated = created,
                    Message = ex.Message,
                    Error = ex
                };
            }
        }

        public void DeleteTree(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory is empty.", nameof(directory));
            }

            var full = Path.GetFullPath(directory);
            var root = Path.GetPathRoot(full);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedRoot = (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed) ||
                string.Equals(trimmed, trimmedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to delete a drive or share root: " + full);
            }

            if (!Directory.Exists(full))
            {
                return;
            }

            DeleteWithoutFollowingLinks(new DirectoryInfo(full));
            log.Info("Deleted directory " + full);
        }

        private static async Task CopyFileAsync(string source, string destination, Action<long> onBytes, CancellationToken cancellationToken)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    onBytes(read);
                }
            }

            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }

        private static void DeleteWithoutFollowingLinks(DirectoryInfo directory)
        {
            if (IsReparsePoint(directory.Attributes))
            {
                directory.Delete(false);
                return;
            }

            foreach (var child in directory.EnumerateDirectories())
            {
                DeleteWithoutFollowingLinks(child);
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }

                file.Delete();
            }

            directory.Delete(false);
        }

        private static bool IsReparsePoint(FileAttributes attributes)
        {
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        private static FileCopyResult Failed(string message)
        {
            return new FileCopyResult
            {
                Status = FileCopyStatus.Failed,
                Message = message
            };
        }
    }
}
