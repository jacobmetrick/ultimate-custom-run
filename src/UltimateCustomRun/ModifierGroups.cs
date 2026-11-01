using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;

namespace UltimateCustomRun;

internal enum ModifierGroup { ImprovedStart, RunParameters, Modifiers, CardPool, Ascension, Negatives, Disabled }

internal static class ModifierGroups
{
    internal static readonly (ModifierGroup Group, string Title)[] Sections =
    [
        (ModifierGroup.ImprovedStart, "Improved Start"),
        (ModifierGroup.RunParameters, "Run Parameters"),
        (ModifierGroup.Modifiers, "Modifiers"),
        (ModifierGroup.CardPool, "Card Pool"),
        (ModifierGroup.Ascension, "Ascension"),
        (ModifierGroup.Negatives, "Negatives"),
        (ModifierGroup.Disabled, "Disabled")
    ];

    internal static bool IsSingleplayerDisabled(ModifierModel? modifier) =>
        modifier is Friendship or FriendshipDraft;

    internal static ModifierGroup Classify(ModifierModel modifier, IReadOnlySet<Type> negativeTypes)
    {
        if (modifier is AscensionModifier) return ModifierGroup.Ascension;
        if (modifier is MustHave or Speedrun or Dill || negativeTypes.Contains(modifier.GetType())) return ModifierGroup.Negatives;
        if (modifier is CharacterCards or ColorlessCards) return ModifierGroup.CardPool;
        if (modifier is CustomRunParameters) return ModifierGroup.RunParameters;
        if (modifier is TwentyTwentyModifier or NeowStarterChoice or Headstart or MysteryEvents or Specialized or SpecializedPickAny or SpecializedDraft or Draft or SuperDraft or Handpicked or UltimateStarter or SealedDeck or Insanity or AllStar or AllStarDraft or Friendship or FriendshipDraft)
            return ModifierGroup.ImprovedStart;
        return ModifierGroup.Modifiers;
    }
}
