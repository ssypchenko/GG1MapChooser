using CounterStrikeSharp.API.Core;
using MapChooserAPI;
namespace MapChooser;

public class WasdMenu : IWasdMenu
{
    public string Title { get; set; } = "";
    public bool FreezePlayer { get; set; } = true;
    public bool DisplayOptionsCount { get; set; } = false;
    public LinkedList<IWasdMenuOption>? Options { get; set; } = new();
    public LinkedListNode<IWasdMenuOption>? Prev { get; set; } = null;
    public LinkedListNode<IWasdMenuOption> Add(string display, Action<CCSPlayerController, IWasdMenuOption> onChoice, string? key = null)
    {
        if (Options == null)
            Options = new();
        WasdMenuOption newOption = new WasdMenuOption
        {
            OptionDisplay = display,
            OnChoose = onChoice,
            Index = Options.Count,
            Count = 0,
            Key = key,
            Parent = this
        };
        return Options.AddLast(newOption);
    }
    /// <summary>
    /// Finds a menu option by its index or key and decrements its vote count by one.
    /// </summary>
    /// <param name="index">The zero-based index of the option to find.</param>
    /// <param name="key">The unique string key of the option to find.</param>
    /// <returns>True if an option was found and its count was successfully retracted; otherwise, false.</returns>
    public bool RetractVote(int? index = null, string? key = null)
    {
        if (Options == null || Options.Count == 0)
        {
            return false;
        }

        if (index == null && key == null)
        {
            return false;
        }

        IWasdMenuOption? optionToUpdate = null;

        if (index.HasValue)
        {
            optionToUpdate = Options.FirstOrDefault(opt => opt.Index == index.Value);
        }
        else if (!string.IsNullOrEmpty(key))
        {
            optionToUpdate = Options.FirstOrDefault(opt => opt.Key == key);
        }

        if (optionToUpdate != null && optionToUpdate.Count > 0)
        {
            optionToUpdate.Count--;
            return true; // Vote retraction was successful.
        }

        // Return false if no matching option was found or its count was already zero.
        return false;
    }
}