using System;
using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using MapChooserAPI;
using Microsoft.Extensions.Logging;

namespace MapChooser
{
    /// <summary>
    /// Adapter that exposes a CS2-like BaseMenu while delegating behavior to ggmc IWasdMenu.
    /// </summary>
    public class WasdMenuMM : BaseMenu
    {
        public string Title { get; set; }
        public List<ItemOption> ItemOptions { get; } = new();
        public bool ExitButton { get; set; }
        public int MenuTime { get; set; }
        private IMenu? _prevMenu;
        public IMenu? PrevMenu
        {
            get
            {
                return _prevMenu;
            }
            set
            {
                _prevMenu = value;
                var valueAsWasdMenu = value as WasdMenuMM;
                if (_innerWASDMenu != null)
                {
                    _innerWASDMenu.Prev = valueAsWasdMenu?._innerWASDMenu.Options?.First;
//                    _plugin.Logger.LogInformation($"[WasdMenuMM] Set for menu {Title} PrevMenu to {(valueAsWasdMenu == null ? "null" : valueAsWasdMenu.Title)}");
                }
            }
        }
        private readonly IWasdMenu _innerWASDMenu;
        private readonly Dictionary<ItemOption, IWasdMenuOption?> _map = new();
        public BasePlugin Plugin { get; } = null!;
        private MapChooser _plugin;

        public WasdMenuMM(string title, MapChooser plugin)
        {
            _plugin = plugin;
            Title = title;
            _innerWASDMenu = _plugin.wasdMenuManager.CreateMenu(title, freezePlayer: true, displayOptionsCount: false)
                   ?? throw new InvalidOperationException("IWasdMenuManager.CreateMenu returned null.");
        }

        public ItemOption AddItem(string display, Action<CCSPlayerController, ItemOption> onSelect, DisableOption disableOption = DisableOption.None, PostSelectAction postSelectAction = PostSelectAction.Nothing)
        {
            // 1. Create the ItemOption (what the plugin expects to work with)
            var item = new ItemOptionMM(display, disableOption, onSelect, postSelectAction);
            ItemOptions.Add(item);
//            _plugin.Logger.LogInformation($"[WasdMenuMM] AddItem: {display}");

            // 2. Wrap the plugin’s callback into the signature your API expects
            Action<CCSPlayerController, IWasdMenuOption> onChoose = (player, wasdOpt) =>
            {
//                _plugin.Logger.LogInformation($"[WasdMenuMM] execute onChoose: {display}");
                // When your API reports a choice, invoke the plugin’s original callback,
                // passing the ItemOption we just created
                onSelect?.Invoke(player, item);
            };

            // 3. Add into your ggmcAPI menu
            _innerWASDMenu.Add(display, onChoose, key: null, disableOption: disableOption);

            return item;
        }

        public ItemOption AddItem(string display, DisableOption disableOption, PostSelectAction postSelectAction = PostSelectAction.Nothing)
        {
            var item = new ItemOptionMM(display, disableOption, null, postSelectAction);
            ItemOptions.Add(item);
            _innerWASDMenu.AddItem(display, key: null, disableOption: disableOption, postSelectAction: postSelectAction);
            return item;
        }

        public void Display(CCSPlayerController player, int time)
        {
            MenuTime = time;
            if (PrevMenu == null)
            {
                _plugin.wasdMenuManager.OpenMainMenu(player, _innerWASDMenu);
            }
            else
            {
//                _plugin.Logger.LogInformation($"[WasdMenuMM] Display: Opening submenu {Title} with Prev menu {_innerWASDMenu.Prev?.Value.OptionDisplay}");
                _plugin.wasdMenuManager.OpenSubMenu(player, _innerWASDMenu);
            }

            if (MenuTime > 0)
            {
                _plugin.AddTimer(MenuTime, () => _plugin.wasdMenuManager.CloseMenu(player));
            }
        }

        public void DisplayAt(CCSPlayerController player, int firstItem, int time)
        {
            // ggmc API has no "start index" yet; open normally.
            Display(player, time);
        }
        public void DisplayToAll(int time)
        {
            // ggmc API has no "to all" yet; no-op.
        }
        public void DisplayAtToAll(int firstItem, int time)
        {
            // ggmc API has no "to all" yet; no-op.
        }
    }
    public class ItemOptionMM(string display, DisableOption option, Action<CCSPlayerController, ItemOption>? onSelect, PostSelectAction postSelectAction) : ItemOption
    {
        public string Text { get; set; } = display;
        public DisableOption DisableOption { get; set; } = option;
        public PostSelectAction PostSelectAction { get; set; } = postSelectAction;
        public Action<CCSPlayerController, ItemOption>? OnSelect { get; set; } = onSelect;
    }
}
