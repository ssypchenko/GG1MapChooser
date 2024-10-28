using CounterStrikeSharp.API.Core;
using MapChooserAPI;
namespace MapChooser;

public class WasdMenuOption : IWasdMenuOption
{
    public IWasdMenu? Parent { get; set; }
    public string? OptionDisplay { get; set; }
    public Action<CCSPlayerController, IWasdMenuOption>? OnChoose { get; set; }
    public int Index { get; set; }
}