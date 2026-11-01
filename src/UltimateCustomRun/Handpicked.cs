using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class Handpicked : ModifierModel
{
    internal const string DisplayTitle = "Handpicked";
    internal const string DisplayDescription = "Build your starting deck from [blue]10[/blue] cards of your choice, picked from every card your rewards can offer. Copies allowed.";
    private const string PromptKey = "ULTIMATE_CUSTOM_RUN.handpickedPrompt";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/sealed_deck.png");
    public override bool ClearsPlayerDeck => true;
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => ChooseCards(player) : null;

    // Card hooks mutate options in place, so every caller gets a fresh instance.
    private static CardCreationOptions RewardOptions(Player player) =>
        new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter)
            .WithFlags(CardCreationFlags.NoUpgradeRoll | CardCreationFlags.IsCardReward);

    internal static List<CardModel> Candidates(Player player)
    {
        // Card pool modifiers (Character Cards, Colorless Cards' relic) widen reward pools through this hook.
        var options = Hook.ModifyCardRewardCreationOptions(player.RunState, player, RewardOptions(player));
        return FilterCandidates(options.GetPossibleCards(player), player.RunState.Players.Count > 1);
    }

    internal static List<CardModel> FilterCandidates(IEnumerable<CardModel> cards, bool multiplayer) =>
        cards.Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .Where(card => multiplayer
                ? card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly
                : card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
            .DistinctBy(card => card.Id)
            // Use stable model IDs, not localized titles, for synchronized choice indexes.
            .OrderBy(card => card.Rarity).ThenBy(card => card.Id.ToString(), StringComparer.Ordinal).ToList();

    private static async Task ChooseCards(Player player)
    {
        var candidates = Candidates(player);
        if (candidates.Count == 0) throw new InvalidOperationException("Handpicked has no eligible cards.");
        var count = ModifierValues.ForPlayer<Handpicked>(player);
        var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        var context = new BlockingPlayerChoiceContext();
        var choiceId = synchronizer.ReserveChoiceId(player);
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        List<int> indexes;
        try
        {
            if (LocalContext.IsMe(player))
            {
                // Preview-only mutable cards are not registered in the run's card scope.
                var previews = candidates.Select(card =>
                {
                    var preview = card.ToMutable();
                    preview.Owner = player;
                    return preview;
                }).ToList();
                var prefs = new CardSelectorPrefs(Prompt(count), count)
                {
                    Cancelable = false,
                    RequireManualConfirmation = true,
                    UnpoweredPreviews = true,
                    Comparison = CompareCards
                };
                indexes = (await HandpickedUi.Select(previews, prefs, count)).ToIndexes();
                HandpickedSelection.Validate(indexes, count, candidates.Count);
                synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndexes(indexes));
            }
            else indexes = (await synchronizer.WaitForRemoteChoice(player, choiceId)).AsIndexes();
            HandpickedSelection.Validate(indexes, count, candidates.Count);
        }
        finally { await context.SignalPlayerChoiceEnded(); }

        var cards = new List<CardModel>(indexes.Count);
        foreach (var group in indexes.GroupBy(index => index))
        {
            var id = candidates[group.Key].Id;
            // Keep vanilla reward creation hooks; extra copies are clones of that card.
            var card = CardFactory.CreateForReward(player, 1, RewardOptions(player).WithFilter(candidate => candidate.Id == id))
                .First().Card;
            cards.Add(card);
            for (var i = 1; i < group.Count(); i++) cards.Add(player.RunState.CloneCard(card));
        }
        CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(cards, PileType.Deck), 1.2f, CardPreviewStyle.GridLayout);
        // Match Sealed Deck: Pandora's Box has no starter cards to transform.
        foreach (var other in player.RunState.Players) other.RelicGrabBag.Remove<PandorasBox>();
        player.RunState.SharedRelicGrabBag.Remove<PandorasBox>();
    }

    private static LocString Prompt(int count)
    {
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        LocManager.Instance.GetTable("modifiers").MergeWith(new Dictionary<string, string>
        {
            [PromptKey] = russian
                ? $"Выберите [blue]{count}[/blue] карт для колоды. Копии разрешены."
                : $"Choose [blue]{count}[/blue] cards for your deck. Copies allowed."
        });
        return new LocString("modifiers", PromptKey);
    }

    private static int CompareCards(CardModel left, CardModel right)
    {
        var rarity = left.Rarity.CompareTo(right.Rarity);
        return rarity != 0 ? rarity : string.Compare(left.Title, right.Title,
            LocManager.Instance.CultureInfo, System.Globalization.CompareOptions.None);
    }
}

// Copy counts per candidate; synchronized as the candidate index repeated once per copy.
internal sealed class HandpickedSelection(int count, int candidateCount)
{
    private readonly int[] _copies = new int[candidateCount];
    internal int Count => count;
    internal int Total { get; private set; }
    internal bool IsComplete => Total == count;

    internal int CopiesOf(int index) => _copies[index];

    internal bool Add(int index)
    {
        if (Total >= count || index < 0 || index >= _copies.Length) return false;
        _copies[index]++;
        Total++;
        return true;
    }

    internal bool Remove(int index)
    {
        if (index < 0 || index >= _copies.Length || _copies[index] == 0) return false;
        _copies[index]--;
        Total--;
        return true;
    }

    internal List<int> ToIndexes() =>
        Enumerable.Range(0, _copies.Length).SelectMany(index => Enumerable.Repeat(index, _copies[index])).ToList();

    internal static void Validate(IReadOnlyCollection<int> indexes, int count, int candidateCount)
    {
        if (indexes.Count != count || indexes.Any(index => index < 0 || index >= candidateCount))
            throw new InvalidOperationException("Handpicked received an invalid card selection.");
    }
}
