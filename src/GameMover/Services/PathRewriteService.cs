using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GameMover.Services
{
    public sealed class PathRewriteOutcome
    {
        public string? Original { get; private set; }

        public string? Result { get; private set; }

        public bool Changed { get; private set; }

        public string? Warning { get; private set; }

        public static PathRewriteOutcome Unchanged(string? original)
        {
            return new PathRewriteOutcome
            {
                Original = original,
                Result = original,
                Changed = false
            };
        }

        public static PathRewriteOutcome ChangedPath(string? original, string result)
        {
            return new PathRewriteOutcome
            {
                Original = original,
                Result = result,
                Changed = true
            };
        }

        public static PathRewriteOutcome Warn(string? original, string warning)
        {
            return new PathRewriteOutcome
            {
                Original = original,
                Result = original,
                Changed = false,
                Warning = warning
            };
        }
    }

    /// <summary>
    /// Rewrites Windows paths from an old directory root to a new one.
    /// Matching is case-insensitive and requires a path-segment boundary, so
    /// D:\Games\ABC does not match D:\Games\ABC2.
    /// </summary>
    public sealed class PathRewriteService
    {
        private static readonly Regex EnvironmentVariablePattern = new Regex(@"%[^%\r\n\\/]+%", RegexOptions.Compiled);
        private static readonly Regex PlayniteVariablePattern = new Regex(@"\{[A-Za-z][A-Za-z0-9_]*\}", RegexOptions.Compiled);

        public bool TryGetDestination(string? sourceDirectory, string? libraryDirectory, out string destination, out string? error)
        {
            destination = string.Empty;
            if (!TryNormalizeAbsolute(sourceDirectory, out var source))
            {
                error = "Install directory is not an absolute path.";
                return false;
            }

            if (IsDriveRoot(source))
            {
                error = "Refusing to move a drive root.";
                return false;
            }

            if (!TryNormalizeAbsolute(libraryDirectory, out var library))
            {
                error = "Target library is not an absolute path.";
                return false;
            }

            var separatorIndex = Math.Max(source.LastIndexOf('\\'), source.LastIndexOf('/'));
            var name = separatorIndex >= 0 ? source.Substring(separatorIndex + 1) : source;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Could not determine the game folder name.";
                return false;
            }

            var join = library.IndexOf('\\') >= 0 || source.IndexOf('\\') >= 0 ? '\\' : Path.DirectorySeparatorChar;
            destination = library.TrimEnd('\\', '/') + join + name;
            error = null;
            return true;
        }

        public bool IsUnderRoot(string? path, string? root)
        {
            if (!TryNormalizeAbsolute(path, out var normalizedPath) || !TryNormalizeAbsolute(root, out var normalizedRoot))
            {
                return false;
            }

            return IsNormalizedUnderRoot(normalizedPath, normalizedRoot);
        }

        public PathRewriteOutcome RewritePath(string? originalPath, string? oldRoot, string? newRoot)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return PathRewriteOutcome.Unchanged(originalPath);
            }

            var sourcePath = originalPath!;

            if (!TryNormalizeAbsolute(oldRoot, out var normalizedOld))
            {
                return PathRewriteOutcome.Warn(originalPath, "Left unchanged because the old folder is not an absolute path.");
            }

            if (!TryNormalizeAbsolute(newRoot, out var normalizedNew))
            {
                return PathRewriteOutcome.Warn(originalPath, "Left unchanged because the new folder is not an absolute path.");
            }

            var trimmed = sourcePath.Trim();
            char? quote = null;
            var body = trimmed;
            if (trimmed.Length >= 2 &&
                ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                 (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
            {
                quote = trimmed[0];
                body = trimmed.Substring(1, trimmed.Length - 2);
            }

            var bodyTrimmed = body.Trim();
            if (ContainsEnvironmentVariable(bodyTrimmed))
            {
                if (ContainsOldRoot(bodyTrimmed, normalizedOld))
                {
                    return PathRewriteOutcome.Warn(originalPath, "Left unchanged because it mixes the old folder with an environment variable: " + originalPath);
                }

                return PathRewriteOutcome.Unchanged(originalPath);
            }

            if (!TryNormalizeAbsolute(bodyTrimmed, out var normalizedBody, out var extendedPrefix))
            {
                if (ContainsOldRoot(bodyTrimmed, normalizedOld) || PlayniteVariablePattern.IsMatch(bodyTrimmed))
                {
                    if (ContainsOldRoot(bodyTrimmed, normalizedOld))
                    {
                        return PathRewriteOutcome.Warn(originalPath, "Left unchanged because it is not a plain absolute path: " + originalPath);
                    }
                }

                return PathRewriteOutcome.Unchanged(originalPath);
            }

            if (PlayniteVariablePattern.IsMatch(bodyTrimmed))
            {
                return PathRewriteOutcome.Warn(originalPath, "Left unchanged because it contains a Playnite variable: " + originalPath);
            }

            if (!IsNormalizedUnderRoot(normalizedBody, normalizedOld))
            {
                return PathRewriteOutcome.Unchanged(originalPath);
            }

            var relative = normalizedBody.Substring(normalizedOld.Length).TrimStart('\\');
            var rewritten = relative.Length == 0
                ? normalizedNew
                : normalizedNew.TrimEnd('\\') + "\\" + relative;
            if (!string.IsNullOrEmpty(extendedPrefix))
            {
                rewritten = extendedPrefix + rewritten;
            }

            if (quote != null)
            {
                rewritten = quote.Value + rewritten + quote.Value;
            }

            return PathRewriteOutcome.ChangedPath(originalPath, rewritten);
        }

        public string? RewriteArgumentText(string? text, string? oldRoot, string? newRoot, ICollection<string> warnings)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var argumentText = text!;

            if (!TryNormalizeAbsolute(oldRoot, out var normalizedOld))
            {
                warnings.Add("Left argument text unchanged because the old folder is not an absolute path.");
                return text;
            }

            var builder = new StringBuilder(argumentText.Length);
            var changed = false;
            var warningsBefore = warnings.Count;
            for (var i = 0; i < argumentText.Length;)
            {
                if (argumentText[i] == '"')
                {
                    var end = argumentText.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        builder.Append(argumentText, i, argumentText.Length - i);
                        break;
                    }

                    var inner = argumentText.Substring(i + 1, end - i - 1);
                    var outcome = RewritePath(inner, oldRoot, newRoot);
                    if (outcome.Warning != null)
                    {
                        warnings.Add(outcome.Warning);
                    }

                    if (outcome.Changed && outcome.Result != null)
                    {
                        changed = true;
                        builder.Append('"').Append(outcome.Result).Append('"');
                    }
                    else
                    {
                        builder.Append(argumentText, i, end - i + 1);
                    }

                    i = end + 1;
                    continue;
                }

                if (StartsAbsoluteAt(argumentText, i))
                {
                    var end = i;
                    while (end < argumentText.Length && !char.IsWhiteSpace(argumentText[end]) && argumentText[end] != '"')
                    {
                        end++;
                    }

                    var token = argumentText.Substring(i, end - i);
                    var outcome = RewritePath(token, oldRoot, newRoot);
                    if (outcome.Warning != null)
                    {
                        warnings.Add(outcome.Warning);
                    }

                    if (outcome.Changed && outcome.Result != null)
                    {
                        changed = true;
                        builder.Append(outcome.Result);
                    }
                    else
                    {
                        builder.Append(token);
                    }

                    i = end;
                    continue;
                }

                builder.Append(argumentText[i]);
                i++;
            }

            if (!changed && warnings.Count == warningsBefore && ContainsOldRoot(argumentText, normalizedOld))
            {
                warnings.Add("Left unchanged because the old folder appears in text that was not a safe absolute path.");
            }

            return builder.ToString();
        }

        public static bool TryNormalizeAbsolute(string? path, out string normalized)
        {
            return TryNormalizeAbsolute(path, out normalized, out _);
        }

        private static bool TryNormalizeAbsolute(string? path, out string normalized, out string? extendedPrefix)
        {
            normalized = string.Empty;
            extendedPrefix = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var rawPath = path!;
            var value = rawPath.Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }

            var unquoted = value;

            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                extendedPrefix = value.Substring(0, 8);
                value = @"\\" + value.Substring(8);
            }
            else if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                extendedPrefix = @"\\?\";
                value = value.Substring(4);
            }

            value = value.Replace('/', '\\');
            if (value.Length == 2 && value[1] == ':')
            {
                value += "\\";
            }

            if (IsAbsoluteWindowsPath(value))
            {
                if (!IsDriveRoot(value))
                {
                    value = value.TrimEnd('\\');
                }

                normalized = value;
                return true;
            }

            extendedPrefix = null;
            if (!Path.IsPathRooted(unquoted))
            {
                return false;
            }

            try
            {
                var full = Path.GetFullPath(unquoted);
                var root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) &&
                    string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    normalized = root;
                    return true;
                }

                normalized = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsAbsoluteWindowsPath(string value)
        {
            if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '\\')
            {
                return true;
            }

            return value.StartsWith(@"\\", StringComparison.Ordinal) && value.Length > 2;
        }

        private static bool IsDriveRoot(string normalized)
        {
            return normalized.Length == 3 &&
                   char.IsLetter(normalized[0]) &&
                   normalized[1] == ':' &&
                   normalized[2] == '\\';
        }

        private static bool IsNormalizedUnderRoot(string path, string root)
        {
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var separator = root.IndexOf('\\') >= 0 || path.IndexOf('\\') >= 0 ? '\\' : '/';
            var prefix = root.EndsWith(separator.ToString(), StringComparison.Ordinal) ? root : root + separator;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsEnvironmentVariable(string value)
        {
            return EnvironmentVariablePattern.IsMatch(value) ||
                   value.IndexOf("$env:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsOldRoot(string text, string normalizedOld)
        {
            var normalized = text.Replace('/', '\\');
            var index = 0;
            while (index < normalized.Length)
            {
                var found = normalized.IndexOf(normalizedOld, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                var end = found + normalizedOld.Length;
                var beforeOk = found == 0 || IsSeparator(normalized[found - 1]) || normalized[found - 1] == '"' || normalized[found - 1] == '\'';
                var afterOk = end >= normalized.Length || IsSeparator(normalized[end]) || normalized[end] == '"' || normalized[end] == '\'';
                if (beforeOk && afterOk)
                {
                    return true;
                }

                index = found + 1;
            }

            return false;
        }

        private static bool IsSeparator(char value)
        {
            return value == '\\' || value == '/' || char.IsWhiteSpace(value);
        }

        private static bool StartsAbsoluteAt(string text, int index)
        {
            if (index > 0)
            {
                var previous = text[index - 1];
                if (!char.IsWhiteSpace(previous) && previous != '=' && previous != ',' && previous != ';')
                {
                    return false;
                }
            }

            if (index + 2 < text.Length &&
                char.IsLetter(text[index]) &&
                text[index + 1] == ':' &&
                (text[index + 2] == '\\' || text[index + 2] == '/'))
            {
                return true;
            }

            if (index + 1 < text.Length && text[index] == '\\' && text[index + 1] == '\\')
            {
                return true;
            }

            return index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/';
        }
    }
}
