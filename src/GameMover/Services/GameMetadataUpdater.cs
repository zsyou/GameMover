using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace GameMover.Services
{
    public sealed class GameMetadataUpdater
    {
        private readonly IPlayniteAPI api;
        private readonly PathRewriteService paths;
        private readonly IMoveLog log;

        public GameMetadataUpdater(IPlayniteAPI api, PathRewriteService paths, IMoveLog log)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.log = log ?? NullMoveLog.Instance;
        }

        public int Update(Guid gameId, string oldRoot, string newRoot, long totalBytes)
        {
            var stored = api.Database.Games.Get(gameId);
            if (stored == null)
            {
                throw new InvalidOperationException("The game is no longer in the Playnite database.");
            }

            var warnings = new List<string>();
            Rewrite(stored.InstallDirectory, value => stored.InstallDirectory = value, "InstallDirectory", oldRoot, newRoot, warnings);
            if (!paths.IsUnderRoot(stored.InstallDirectory, newRoot) &&
                !string.Equals(Normalize(stored.InstallDirectory), Normalize(newRoot), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("InstallDirectory was not rewritten to the new folder.");
            }

            if (stored.GameActions != null)
            {
                foreach (var action in stored.GameActions)
                {
                    var label = "Action '" + (action.Name ?? action.Type.ToString()) + "'";
                    Rewrite(action.Path, value => action.Path = value, label + " Path", oldRoot, newRoot, warnings);
                    Rewrite(action.WorkingDir, value => action.WorkingDir = value, label + " WorkingDir", oldRoot, newRoot, warnings);
                    Rewrite(action.TrackingPath, value => action.TrackingPath = value, label + " TrackingPath", oldRoot, newRoot, warnings);
                    RewriteText(action.Arguments, value => action.Arguments = value, label + " Arguments", oldRoot, newRoot, warnings);
                    RewriteText(action.AdditionalArguments, value => action.AdditionalArguments = value, label + " AdditionalArguments", oldRoot, newRoot, warnings);
                    RewriteText(action.Script, value => action.Script = value, label + " Script", oldRoot, newRoot, warnings);
                }
            }

            if (stored.Roms != null)
            {
                foreach (var rom in stored.Roms)
                {
                    Rewrite(rom.Path, value => rom.Path = value ?? string.Empty, "ROM '" + rom.Name + "'", oldRoot, newRoot, warnings);
                }
            }

            RewriteText(stored.PreScript, value => stored.PreScript = value, "PreScript", oldRoot, newRoot, warnings);
            RewriteText(stored.PostScript, value => stored.PostScript = value, "PostScript", oldRoot, newRoot, warnings);
            RewriteText(stored.GameStartedScript, value => stored.GameStartedScript = value, "GameStartedScript", oldRoot, newRoot, warnings);
            Rewrite(stored.Manual, value => stored.Manual = value, "Manual", oldRoot, newRoot, warnings);
            Rewrite(stored.Icon, value => stored.Icon = value, "Icon", oldRoot, newRoot, warnings);
            Rewrite(stored.CoverImage, value => stored.CoverImage = value, "CoverImage", oldRoot, newRoot, warnings);
            Rewrite(stored.BackgroundImage, value => stored.BackgroundImage = value, "BackgroundImage", oldRoot, newRoot, warnings);

            if (totalBytes >= 0)
            {
                stored.InstallSize = (ulong)totalBytes;
            }

            api.Database.Games.Update(stored);
            log.Info("Saved Playnite database entry for '" + stored.Name + "'. InstallDirectory=" + stored.InstallDirectory);
            foreach (var warning in warnings)
            {
                log.Warn(warning);
            }

            return warnings.Count;
        }

        private void Rewrite(string? current, Action<string?> assign, string label, string oldRoot, string newRoot, ICollection<string> warnings)
        {
            var outcome = paths.RewritePath(current, oldRoot, newRoot);
            if (outcome.Warning != null)
            {
                warnings.Add(label + ": " + outcome.Warning);
            }

            if (outcome.Changed)
            {
                assign(outcome.Result);
                log.Info(label + ": '" + current + "' -> '" + outcome.Result + "'");
            }
        }

        private void RewriteText(string? current, Action<string?> assign, string label, string oldRoot, string newRoot, ICollection<string> warnings)
        {
            var local = new List<string>();
            var rewritten = paths.RewriteArgumentText(current, oldRoot, newRoot, local);
            foreach (var warning in local)
            {
                warnings.Add(label + ": " + warning);
            }

            if (!string.Equals(current, rewritten, StringComparison.Ordinal))
            {
                assign(rewritten);
                log.Info(label + " rewritten.");
            }
        }

        private static string Normalize(string? path)
        {
            string normalized;
            return PathRewriteService.TryNormalizeAbsolute(path, out normalized) ? normalized : path ?? string.Empty;
        }
    }
}
