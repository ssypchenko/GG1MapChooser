using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using MapChooserAPI;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace MapChooser;

internal sealed class MapVoteHud
{
    internal const int Rows = 6;
    private const string Root = "ggmc-vote";
    private readonly ConditionalWeakTable<WasdMenuPlayer, PlayerState> players = new();
    private CCSCustomHudLayout? layout;

    private sealed class PlayerState
    {
        public string?[] PreviewClasses { get; } = new string?[Rows];
        public int?[] Counts { get; } = new int?[Rows];
    }

    public bool Draw(WasdMenuPlayer menu, MapChooser plugin, PanoramaMenu options, string controls)
    {
        if (menu.player is not { IsValid: true } player)
            return false;

        if (layout is not { IsValid: true })
        {
            players.Clear();
            layout = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (layout is not { IsValid: true })
                return false;

            layout.StrLayout = "panorama/layout/custom_game/ggmc_map_vote.xml";
            layout.DispatchSpawn();
            if (!layout.IsValid)
                return false;
        }

        void Text(string id, string value) => layout.SetDialogVariableStringForPlayer(player, id, "value", value);
        void Class(string id, string name, bool enabled) => layout.SetHasClassForPlayer(player, id, name, enabled);

        Text("ggmc-title", options.Title);
        Text("ggmc-controls", controls);
        Class(Root, "has-title", !string.IsNullOrEmpty(options.Title));
        Class(Root, "show-counts", options.DisplayOptionsCount);
        bool showPreviews = options.ShowMapPreviews && plugin.Config.MenuSettings.ShowMapThumbnails;
        Class(Root, "show-previews", showPreviews);

        var state = players.GetValue(menu, static _ => new PlayerState());
        var option = menu.MenuStart;
        for (int i = 0; i < Rows; i++, option = option?.Next)
        {
            string row = $"ggmc-row-{i}";
            Class(row, "shown", option != null);
            string? mapKey = showPreviews && option?.Value.Key is { } key && key is not ("extend.map" or "novote.line")
                ? key
                : null;
            Class(row, "has-preview", mapKey != null);
            string? previewClass = mapKey != null && plugin.GetPanoramaPreview(mapKey) is { } preview
                ? "preview-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preview.AbsoluteUri)))
                : null;
            if (state.PreviewClasses[i] != previewClass)
            {
                if (state.PreviewClasses[i] is { } oldPreview)
                    Class($"{row}-preview", oldPreview, false);
                if (previewClass != null)
                    Class($"{row}-preview", previewClass, true);
                state.PreviewClasses[i] = previewClass;
            }

            if (option == null)
                continue;

            Class(row, "selected", option == menu.CurrentChoice);
            Class(row, "disabled", option.Value.DisableOption != DisableOption.None);
            Text($"{row}-number", (option.Value.Index + 1).ToString());
            Text($"{row}-name", option.Value.OptionDisplay ?? "");
        }

        if (options.DisplayOptionsCount)
            RefreshCounts(menu);
        Class(Root, "shown", true);
        return true;
    }

    public void RefreshCounts(WasdMenuPlayer menu)
    {
        if (layout is not { IsValid: true } || menu.player is not { IsValid: true } player ||
            !players.TryGetValue(menu, out var state))
            return;

        var option = menu.MenuStart;
        for (int i = 0; i < Rows && option != null; i++, option = option.Next)
        {
            int count = option.Value.Count;
            if (state.Counts[i] == count)
                continue;

            layout.SetDialogVariableStringForPlayer(player, $"ggmc-row-{i}-count", "value", count.ToString());
            state.Counts[i] = count;
        }
    }

    public void Hide(WasdMenuPlayer menu)
    {
        players.Remove(menu, out var state);
        if (layout is not { IsValid: true } || menu.player is not { IsValid: true } player)
            return;

        layout.SetHasClassForPlayer(player, Root, "shown", false);
        if (state == null)
            return;

        for (int i = 0; i < Rows; i++)
        {
            if (state.PreviewClasses[i] is { } preview)
                layout.SetHasClassForPlayer(player, $"ggmc-row-{i}-preview", preview, false);
        }
    }

    public void Remove()
    {
        if (layout is { IsValid: true })
            layout.Remove();
        layout = null;
        players.Clear();
    }
}
