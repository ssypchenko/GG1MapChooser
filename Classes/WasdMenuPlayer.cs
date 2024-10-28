using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;
using MapChooserAPI;

namespace MapChooser;

public class WasdMenuPlayer
{
    public CCSPlayerController player { get; set; } = null!;
    public WasdMenu? MainMenu = null;
    public LinkedListNode<IWasdMenuOption>? CurrentChoice = null;
    public LinkedListNode<IWasdMenuOption>? MenuStart = null;
    public string CenterHtml = "";
    public int VisibleOptions = 5;
    public IStringLocalizer? Localizer = null;
    public PlayerButtons Buttons { get; set; }

    public void OpenMainMenu(WasdMenu? menu)
    {
        if (menu == null)
        {
            MainMenu = null;
            CurrentChoice = null;
            CenterHtml = "";
            return;
        }
        MainMenu = menu;
        VisibleOptions = menu.Title != "" ? 4 : 5;
        CurrentChoice = MainMenu.Options?.First;
        MenuStart = CurrentChoice;
        UpdateCenterHtml();
    }

    public void OpenSubMenu(IWasdMenu? menu)
    {
        if (menu == null)
        {
            CurrentChoice = MainMenu?.Options?.First;
            MenuStart = CurrentChoice;
            UpdateCenterHtml();
            return;
        }

        VisibleOptions = menu.Title != "" ? 4 : 5;
        CurrentChoice = menu.Options?.First;
        MenuStart = CurrentChoice;
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
        UpdateCenterHtml();
    }

    public void CloseSubMenu()
    {
        if(CurrentChoice?.Value.Parent?.Prev == null)
            return;
        GoBackToPrev(CurrentChoice?.Value.Parent.Prev);
    }

    public void CloseAllSubMenus()
    {
        OpenSubMenu(null);
    }
    
    public void Choose()
    {
        CurrentChoice?.Value.OnChoose?.Invoke(player, CurrentChoice.Value);
    }

    public void ScrollDown()
    {
        if(CurrentChoice == null || MainMenu == null)
            return;
        CurrentChoice = CurrentChoice.Next ?? CurrentChoice.List?.First;
        MenuStart = CurrentChoice!.Value.Index >= VisibleOptions ? MenuStart!.Next : CurrentChoice.List?.First;
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
        UpdateCenterHtml();
    }

    private void UpdateCenterHtml()
    {
        if (CurrentChoice == null || MainMenu == null)
            return;

        StringBuilder builder = new StringBuilder();
        int i = 0;
        LinkedListNode<IWasdMenuOption>? option = MenuStart!;
        
        using (new WithTemporaryCulture(player.GetLanguage()))
        {
            if (option.Value.Parent?.Title != "")
            {
                builder.AppendLine($"{Localizer?["menu.title.prefix"]}{option.Value.Parent?.Title}</u><font color='white'><br>");
            }

            while (i < VisibleOptions && option != null )
            {
                if (option == CurrentChoice)
                    builder.AppendLine($"{Localizer?["menu.selection.left"]} {option.Value.OptionDisplay} {Localizer?["menu.selection.right"]} <br>");
                else
                    builder.AppendLine($"{option.Value.OptionDisplay} <br>");
                option = option.Next;
                i++;
            }

            if (option != null) { // more options
                builder.AppendLine(
                    $"{Localizer?["menu.more.options.below"]}");
            }

            builder.AppendLine("<br>" +
                            $"{Localizer?["menu.bottom.text"]}<br>");
            builder.AppendLine("</div>");
        }
        CenterHtml = builder.ToString();
    }
}