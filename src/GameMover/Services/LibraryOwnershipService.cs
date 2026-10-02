using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameMover.Services
{
    public static class LibraryOwnershipService
    {
        public static string? GetMoveRefusal(Game? game, IEnumerable<Plugin>? plugins)
        {
            if (game == null)
            {
                return "No game was selected.";
            }

            // IsCustomGame is PluginId == Guid.Empty: the game is stored by Playnite, not a library plugin.
            if (game.IsCustomGame)
            {
                return null;
            }

            var owner = plugins?.FirstOrDefault(plugin => plugin.Id == game.PluginId);
            var libraryName = (owner as LibraryPlugin)?.Name;
            if (string.IsNullOrWhiteSpace(libraryName))
            {
                libraryName = "a library plugin";
            }

            return "\"" + game.Name + "\" is managed by " + libraryName +
                   ". GameMover only moves manually added games, because library launchers keep their own install records. Move this game with the original launcher.";
        }
    }
}
