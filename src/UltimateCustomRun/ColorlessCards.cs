using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace UltimateCustomRun;

public sealed class ColorlessCards : ModifierModel
{
    // Titles stay plain text: the top bar's hover tip does not render BBCode.
    internal const string DisplayTitle = "Colorless Cards";
    internal const string DisplayDescription = "[gold]Colorless[/gold] cards can appear in card rewards.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/character_cards.png");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => ObtainRug(player) : null;

    private static async Task ObtainRug(Player player)
    {
        if (player.Relics.Any(relic => relic is DingyRug)) return;
        await RelicCmd.Obtain<DingyRug>(player);
    }
}
