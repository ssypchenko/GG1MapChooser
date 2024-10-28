using CounterStrikeSharp.API.Core;
using MapChooserAPI;

namespace MapChooser;

public class WasdManager : IWasdMenuManager
{
    public void OpenMainMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if(player == null)
            return;
        WASDMenu.Players[player.Slot].OpenMainMenu((WasdMenu?)menu);
    }

    public void CloseMenu(CCSPlayerController? player)
    {
        if(player == null)
            return;
        WASDMenu.Players[player.Slot].OpenMainMenu(null);
    }

    public void CloseSubMenu(CCSPlayerController? player)
    {
        if(player == null)
            return;
        WASDMenu.Players[player.Slot].CloseSubMenu();
    }

    public void CloseAllSubMenus(CCSPlayerController? player)
    {
        if(player == null)
            return;
        WASDMenu.Players[player.Slot].CloseAllSubMenus();
    }

    public void OpenSubMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if (player == null)
            return;
        WASDMenu.Players[player.Slot].OpenSubMenu(menu);
    }

    public IWasdMenu CreateMenu(string title = "")
    {
        WasdMenu menu = new WasdMenu
        {
            Title = title
        };
        return menu;
    }
}