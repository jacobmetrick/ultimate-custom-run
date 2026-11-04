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
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class Handpicked : ModifierModel
{
    internal const string DisplayTitle = "Handpicked";
    internal const string DisplayDescription = "Build your starting deck from [blue]10[/blue] cards of your choice, picked from every card your rewards can offer.";
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

        // The deck keeps cards in the order they were picked.
        var cards = new List<CardModel>(indexes.Count);
        var created = new Dictionary<int, CardModel>();
        foreach (var index in indexes)
        {
            if (created.TryGetValue(index, out var first))
            {
                // Extra copies are clones of that card.
                cards.Add(player.RunState.CloneCard(first));
                continue;
            }
            var id = candidates[index].Id;
            // Keep vanilla reward creation hooks.
            var card = CardFactory.CreateForReward(player, 1, RewardOptions(player).WithFilter(candidate => candidate.Id == id))
                .First().Card;
            created[index] = card;
            cards.Add(card);
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
                ? $"Выберите [blue]{count}[/blue] карт для колоды."
                : $"Choose [blue]{count}[/blue] cards for your deck."
        });
        return new LocString("modifiers", PromptKey);
    }

    // Compendium order: its pool tabs, then rarity, type, cost and title within each pool.
    internal static int CompareCards(CardModel left, CardModel right)
    {
        var result = PoolRank(left.Pool).CompareTo(PoolRank(right.Pool));
        if (result == 0) result = left.Rarity.CompareTo(right.Rarity);
        if (result == 0) result = left.Type.CompareTo(right.Type);
        if (result == 0) result = left.EnergyCost.GetResolved().CompareTo(right.EnergyCost.GetResolved());
        if (result == 0) result = string.Compare(left.Title, right.Title,
            LocManager.Instance.CultureInfo, System.Globalization.CompareOptions.None);
        return result != 0 ? result : left.Id.CompareTo(right.Id);
    }

    internal static int PoolRank(CardPoolModel pool)
    {
        var compendium = Array.FindIndex(CompendiumPools, type => type.IsInstanceOfType(pool));
        if (compendium >= 0) return compendium;
        // Modded characters follow the native characters; Colorless and other shared pools come last.
        if (pool is ColorlessCardPool) return 1000;
        var character = ModelDb.AllCharacterCardPools.Select(candidate => candidate.Id).ToList().IndexOf(pool.Id);
        return character >= 0 ? CompendiumPools.Length + character : 1001;
    }

    private static readonly Type[] CompendiumPools =
    [
        typeof(IroncladCardPool), typeof(SilentCardPool), typeof(DefectCardPool),
        typeof(RegentCardPool), typeof(NecrobinderCardPool)
    ];
}

// Picks in the order they were made; synchronized as candidate indexes, one per copy.
internal sealed class HandpickedSelection(int count, int candidateCount)
{
    private readonly int[] _copies = new int[candidateCount];
    private readonly List<int> _picks = [];
    internal int Count => count;
    internal int Total => _picks.Count;
    internal bool IsComplete => Total == count;

    internal int CopiesOf(int index) => _copies[index];

    internal bool Add(int index)
    {
        if (Total >= count || index < 0 || index >= _copies.Length) return false;
        _copies[index]++;
        _picks.Add(index);
        return true;
    }

    // Removes the most recent pick of that card, so earlier picks keep their place.
    internal bool Remove(int index)
    {
        if (index < 0 || index >= _copies.Length || _copies[index] == 0) return false;
        _copies[index]--;
        _picks.RemoveAt(_picks.LastIndexOf(index));
        return true;
    }

    internal List<int> ToIndexes() => [.. _picks];

    internal static void Validate(IReadOnlyCollection<int> indexes, int count, int candidateCount)
    {
        if (indexes.Count != count || indexes.Any(index => index < 0 || index >= candidateCount))
            throw new InvalidOperationException("Handpicked received an invalid card selection.");
    }
}
