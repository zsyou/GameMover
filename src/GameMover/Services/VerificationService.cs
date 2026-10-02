using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameMover.Services
{
    public enum VerificationMode
    {
        FileCountAndSize = 0
        // Crc32, XxHash64 and Sha256 can be added later without changing callers.
    }

    public sealed class VerificationResult
    {
        public bool Match { get; set; }

        public long SourceFileCount { get; set; }

        public long DestinationFileCount { get; set; }

        public long SourceBytes { get; set; }

        public long DestinationBytes { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    public interface IVerificationService
    {
        string Name { get; }

        VerificationMode Mode { get; }

        Task<VerificationResult> VerifyAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken);
    }

    public static class VerificationServices
    {
        public static IVerificationService Create(VerificationMode mode)
        {
            switch (mode)
            {
                case VerificationMode.FileCountAndSize:
                    return new FileCountAndSizeVerificationService();
                default:
                    throw new NotSupportedException("Verification mode " + mode + " is not implemented.");
            }
        }
    }

    public sealed class FileCountAndSizeVerificationService : IVerificationService
    {
        private readonly IMoveLog log;

        public FileCountAndSizeVerificationService()
            : this(NullMoveLog.Instance)
        {
        }

        public FileCountAndSizeVerificationService(IMoveLog log)
        {
            this.log = log ?? NullMoveLog.Instance;
        }

        public string Name => "File count and size";

        public VerificationMode Mode => VerificationMode.FileCountAndSize;

        public Task<VerificationResult> VerifyAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => Verify(sourceDirectory, destinationDirectory, cancellationToken), cancellationToken);
        }

        private VerificationResult Verify(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            var source = DirectoryInventoryScanner.Scan(sourceDirectory, log, cancellationToken);
            var destination = DirectoryInventoryScanner.Scan(destinationDirectory, log, cancellationToken);
            var sourcePaths = ToMap(source.Files);
            var destinationPaths = ToMap(destination.Files);
            var samePaths = sourcePaths.Count == destinationPaths.Count;
            if (samePaths)
            {
                foreach (var pair in sourcePaths)
                {
                    long destinationLength;
                    if (!destinationPaths.TryGetValue(pair.Key, out destinationLength) || destinationLength != pair.Value)
                    {
                        samePaths = false;
                        break;
                    }
                }
            }

            var countMatches = source.Files.Count == destination.Files.Count;
            var sizeMatches = source.TotalBytes == destination.TotalBytes;
            var match = countMatches && sizeMatches && samePaths;
            var message = match
                ? "Verification matched: " + source.Files.Count + " file(s), " + source.TotalBytes + " byte(s)."
                : "Verification failed. Source " + source.Files.Count + " file(s) / " + source.TotalBytes +
                  " byte(s), destination " + destination.Files.Count + " file(s) / " + destination.TotalBytes + " byte(s).";

            if (match)
            {
                log.Info(message);
            }
            else
            {
                log.Error(message);
            }

            return new VerificationResult
            {
                Match = match,
                SourceFileCount = source.Files.Count,
                DestinationFileCount = destination.Files.Count,
                SourceBytes = source.TotalBytes,
                DestinationBytes = destination.TotalBytes,
                Message = message
            };
        }

        private static Dictionary<string, long> ToMap(IReadOnlyList<FileEntry> files)
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                map[file.RelativePath.Replace('/', '\\')] = file.Length;
            }

            return map;
        }
    }
}
