using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;
using CounterStrikeSharp.API.Modules.Memory;
using MapChooserAPI;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace MapChooser;

public class WasdMenuPlayer
{
    private readonly MapChooser _plugin;

    public WasdMenuPlayer(MapChooser plugin)
    {
        _plugin = plugin;
    }
    public CCSPlayerController player { get; set; } = null!;
    public WasdMenu? MainMenu = null;
    public LinkedListNode<IWasdMenuOption>? CurrentChoice = null;
    public LinkedListNode<IWasdMenuOption>? MenuStart = null;
    public string CenterHtml = "";
    public int VisibleOptions = 5;
    public IStringLocalizer? Localizer = null;
    public PlayerButtons Buttons { get; set; }
    string strSelect = "Select";
    string strMove = "Move";
    string strExit = "Exit";
    string strBack = "Back";
    string bottomMenuLine = "";
    string bottomSubMenuLine = "";
    public bool ActiveMenu
    {
        get
        {
            if (CurrentChoice == null || MainMenu == null)
                return false;
            else
                return true; 
        }
    }
    public void UpdateLocalization()
    {
        if (player != null && Localizer != null)
        {
            using (new WithTemporaryCulture(player.GetLanguage()))
            {
                strSelect = Localizer["menu.select"];
                strMove = Localizer["menu.move"];
                strExit = Localizer["menu.exit"];
                strBack = Localizer["menu.back"];
            }
            bottomMenuLine = $"<font color='#ff3333' class='fontSize-sm'>{strMove}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.ScrollUp}/{_plugin.Config.MenuSettings.ScrollDown}]<font color='#FFFFFF'>|<font color='#ff3333' class='fontSize-m'>{strSelect}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.Choose}]<font color='#FFFFFF'>|<font color='#ff3333'>{strExit}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.Exit}]</font><br>";
            bottomSubMenuLine = $"<font color='#ff3333' class='fontSize-sm'>{strSelect}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.Choose}]<font color='#FFFFFF'>|<font color='#ff3333' class='fontSize-m'>{strBack}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.Back}]<font color='#FFFFFF'>|<font color='#ff3333'>{strExit}:<font color='#f5a142'>[{_plugin.Config.MenuSettings.Exit}]</font><br>";
        }
    }
    public void OpenMainMenu(WasdMenu? menu)
    {
        if (menu == null)
        {
            CurrentChoice = null;
            CenterHtml = "";
            if (MainMenu != null && MainMenu.FreezePlayer && player != null)
            {
                player.UnFreeze();
            }
            MainMenu = null;
            return;
        }
        MainMenu = menu;
        VisibleOptions = menu.Title != "" ? 4 : 5;
        CurrentChoice = MainMenu.Options?.First;
        MenuStart = CurrentChoice;
        
        if (MainMenu.FreezePlayer && player != null)
        {
            _plugin.Logger.LogInformation("*************player freezed");
            player.Freeze();
        }

        if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
        {
            player.ExecuteClientCommand("play Ui/buttonrollover.vsnd_c");
        }
        UpdateCenterHtml();
    }

    public void OpenSubMenu(IWasdMenu? menu)
    {
        if (menu == null)
        {
            CurrentChoice = MainMenu?.Options?.First;
            MenuStart = CurrentChoice;
            if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
            {
                player.ExecuteClientCommand("play Ui/buttonrollover.vsnd_c");
            }
            UpdateCenterHtml();
            return;
        }

        VisibleOptions = menu.Title != "" ? 4 : 5;
        CurrentChoice = menu.Options?.First;
        MenuStart = CurrentChoice;
        if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
        {
            player.ExecuteClientCommand("play Ui/buttonrollover.vsnd_c");
        }
        UpdateCenterHtml();
    }
    public void GoBackToPrev(LinkedListNode<IWasdMenuOption>? menu)
    {
        if (menu == null)
        {
            CurrentChoice = MainMenu?.Options?.First;
            MenuStart = CurrentChoice;
            UpdateCenterHtml();
            return;
        }

        VisibleOptions = menu.Value.Parent?.Title != "" ? 4 : 5;
        CurrentChoice = menu;
        if (CurrentChoice.Value.Index >= 5 )
        {
            MenuStart = CurrentChoice;
            for (int i = 0; i < 4; i++)
            {
                MenuStart = MenuStart?.Previous;
            }
        }
        else
            MenuStart = CurrentChoice.List?.First;
        if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
        {
            player.ExecuteClientCommand("play Ui/buttonrollover.vsnd_c");
        }
        UpdateCenterHtml();
    }

    public void CloseSubMenu()
    {
        if(CurrentChoice?.Value.Parent?.Prev == null)
        {
            if (MainMenu != null && MainMenu.FreezePlayer && player != null)
            {
                player.UnFreeze();
            }
            return;
        }
        GoBackToPrev(CurrentChoice?.Value.Parent.Prev);
    }

    public void CloseAllSubMenus()
    {
        OpenSubMenu(null);
    }

/*    public void CloseMenu()
    {
        if (MainMenu == null)
            return;

        CurrentChoice = null;
        MenuStart = null;
        CenterHtml = "";

        if (player != null)
        {
            if (MainMenu != null && MainMenu.FreezePlayer)
            {
                player.UnFreeze();
            }
            player.PrintToCenterHtml(" ");
        }
        MainMenu = null;
    } */
    
    public void Choose()
    {
        if (player != null)
        {
            if (_plugin.Config.MenuSettings.SoundInMenu)
            {
                player.ExecuteClientCommand("play Ui/buttonrollover.vsnd_c");
            }
            CurrentChoice?.Value.OnChoose?.Invoke(player, CurrentChoice.Value);
        }
    }

    public void ScrollDown()
    {
        if(CurrentChoice == null || MainMenu == null)
            return;
        CurrentChoice = CurrentChoice.Next ?? CurrentChoice.List?.First;
        MenuStart = CurrentChoice!.Value.Index >= VisibleOptions ? MenuStart!.Next : CurrentChoice.List?.First;
        if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
        {
            player.ExecuteClientCommand("play Ui/buttonclick.vsnd_c");
        }
        UpdateCenterHtml();
    }
    
    public void ScrollUp()
    {
        if(CurrentChoice == null || MainMenu == null)
            return;
        CurrentChoice = CurrentChoice.Previous ?? CurrentChoice.List?.Last;
        if (CurrentChoice == CurrentChoice?.List?.Last && CurrentChoice?.Value.Index >= VisibleOptions)
        {
            MenuStart = CurrentChoice;
            for (int i = 0; i < VisibleOptions-1; i++)
                MenuStart = MenuStart?.Previous;
        }
        else
            MenuStart = CurrentChoice!.Value.Index >= VisibleOptions ? MenuStart!.Previous : CurrentChoice.List?.First;
        if (_plugin.Config.MenuSettings.SoundInMenu && player != null)
        {
            player.ExecuteClientCommand("play Ui/buttonclick.vsnd_c");
        }
        UpdateCenterHtml();
    }

    private void UpdateCenterHtml()
    {
        if (CurrentChoice == null || MainMenu == null)
            return;
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("<div>");
        int i = 0;
        LinkedListNode<IWasdMenuOption>? option = MenuStart;        
        if (option != null && option.Value != null && option.Value.Parent != null)
        {
            if (option.Value.Parent.Title != "")
            {
                builder.Append($"<b><font color='red' class='fontSize-m'>{option.Value.Parent.Title}</font></b><br>");
            }
            string leftArrow = "◄";
            string rightArrow = "►";
            while (i < VisibleOptions)
            {
                if (option == CurrentChoice)
                {
                    builder.AppendLine($"<font color='yellow'>{rightArrow}[</font> <font color='#9acd32' class='fontSize-m'>{option.Value.OptionDisplay}</font> <font color='yellow'>]{leftArrow}</font></b><br>");
                }
                else
                {
                    builder.AppendLine($"<font color='white' class='fontSize-m'>{option?.Value.OptionDisplay}</font><br>");
                }
                i++;
                option = option?.Next;
            }
        }

        if (option != null) { // more options
            builder.AppendLine("<img src='https://raw.githubusercontent.com/ssypchenko/GG1MapChooser/main/Resources/arrow.gif' class=''> <img src='https://raw.githubusercontent.com/ssypchenko/GG1MapChooser/main/Resources/arrow.gif' class=''> <img src='https://raw.githubusercontent.com/ssypchenko/GG1MapChooser/main/Resources/arrow.gif' class=''><br>");
        }

        if (MenuStart?.Value.Parent!.Prev != null)
        {
//            builder.AppendLine($"<font color='#ff3333' class='fontSize-sm'>{strSelect}:<font color='#f5a142'>[E]<font color='#FFFFFF'>|<font color='#ff3333' class='fontSize-m'>{strBack}:<font color='#f5a142'>[A]<font color='#FFFFFF'>|<font color='#ff3333'>{strExit}:<font color='#f5a142'>[R]</font><br>");
            builder.AppendLine(bottomSubMenuLine);
        }
        else
        {
//            builder.AppendLine($"<font color='#ff3333' class='fontSize-sm'>{strMove}:<font color='#f5a142'>[W/S]<font color='#FFFFFF'>|<font color='#ff3333' class='fontSize-m'>{strSelect}:<font color='#f5a142'>[E]<font color='#FFFFFF'>|<font color='#ff3333'>{strExit}:<font color='#f5a142'>[R]</font><br>");
            builder.AppendLine(bottomMenuLine);
        }
        builder.AppendLine("</div>");

        CenterHtml = builder.ToString();
    }
}
public static class CCSPlayerControllerExtensions
{
    public static void Freeze(this CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = player.PlayerPawn.Value;

        if (playerPawn == null)
        {
            return;
        }
        playerPawn.ChangeMovetype(MoveType_t.MOVETYPE_OBSOLETE);
    }
    public static void UnFreeze(this CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = player.PlayerPawn.Value;

        if (playerPawn == null)
        {
            return;
        }
        playerPawn.ChangeMovetype(MoveType_t.MOVETYPE_WALK);
    }
    private static void ChangeMovetype(this CBasePlayerPawn pawn, MoveType_t movetype)
    {
        pawn.MoveType = movetype;
        Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", movetype);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }
}