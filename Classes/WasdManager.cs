using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapChooserAPI;

namespace MapChooser;

public class WasdManager : IWasdMenuManager
{
    public WasdManager (MapChooser plugin)
    {
        _plugin = plugin;
    }
    private MapChooser _plugin;

    public void OpenMainMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if (player != null && player.IsValid && menu != null)
        {
            if (WASDMenu.Players.TryGetValue(player.Slot, out var pl))
            {
                if (!pl.ActiveMenu)
                    pl.OpenMainMenu((WasdMenu)menu);
            }
        }
    }

    public void CloseMenu(CCSPlayerController? player)
    {
        if (player != null && player.IsValid)
        {
            if (WASDMenu.Players.TryGetValue(player.Slot, out var pl))
            {
                pl.OpenMainMenu(null);
            }
        }
    }

    public void CloseSubMenu(CCSPlayerController? player)
    {
        if (player != null && player.IsValid)
        {
            if (WASDMenu.Players.TryGetValue(player.Slot, out var pl))
            {
                pl.CloseSubMenu();
            }
        }
    }

    public void CloseAllSubMenus(CCSPlayerController? player)
    {
        if (player != null && player.IsValid)
        {
            if (WASDMenu.Players.TryGetValue(player.Slot, out var pl))
            {
                pl.CloseAllSubMenus();
            }
        }
    }

    public void OpenSubMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if (player != null && player.IsValid && menu != null)
        {
            if (WASDMenu.Players.TryGetValue(player.Slot, out var pl))
            {
                pl.OpenSubMenu(menu);
            }
        }
    }
    public void CloseActiveMenu(IWasdMenu? menu)
    {
        if (menu != null)
        {
            foreach (var player in WASDMenu.Players.Values)
            {
                if (player.MainMenu == menu)
                {
                    player.OpenMainMenu(null);
                }
            }
        }
    }
    public void CloseActiveMenu(CCSPlayerController? player)
    {
        CloseMenu(player);
    }
    public IWasdMenu CreateMenu(string title = "", bool freezePlayer = true, bool displayOptionsCount = false)
    {
        WasdMenu menu = new WasdMenu
        {
            Title = title,
            FreezePlayer = freezePlayer,
            DisplayOptionsCount = displayOptionsCount
        };
        return menu;
    }
    public BaseMenu MenuByType(string menuType, string title, BasePlugin plugin)
    {
        return new WasdMenuMM(title, _plugin);
    }

    public BaseMenu MenuByType(Type menuType, string title, BasePlugin plugin)
    {
        return new WasdMenuMM(title, _plugin);
    }
}
