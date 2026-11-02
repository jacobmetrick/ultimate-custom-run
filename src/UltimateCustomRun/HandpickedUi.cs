using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.addons.mega_text;

namespace UltimateCustomRun;

// Reuses the native Sealed Deck grid; clicks count copies instead of toggling a single selection.
internal static class HandpickedUi
{
    internal sealed class State
    {
        internal required IReadOnlyList<CardModel> Cards;
        internal required HandpickedSelection Selection;
        // Grid holders are pooled and reused by other card grids, such as the deck view.
        internal readonly List<(NGridCardHolder Holder, Label Badge, Control.GuiInputEventHandler Input)> Badges = [];
    }
    private static readonly ConditionalWeakTable<NSimpleCardSelectScreen, State> Screens = new();
    private static readonly Color BadgeColor = Color.FromHtml("fff6e2");

    internal static async Task<HandpickedSelection> Select(IReadOnlyList<CardModel> cards, CardSelectorPrefs prefs, int count)
    {
        var overlays = NOverlayStack.Instance ?? throw new InvalidOperationException("Handpicked requires the run overlay stack.");
        var screen = NSimpleCardSelectScreen.Create(cards, prefs);
        screen.Name = "HandpickedCardSelection";
        var state = new State { Cards = cards, Selection = new HandpickedSelection(count, cards.Count) };
        Screens.Add(screen, state);
        overlays.Push(screen);
        await screen.CardsSelected();
        return state.Selection;
    }

    internal static bool TryGet(NCardGridSelectionScreen screen, out State state)
    {
        state = null!;
        return screen is NSimpleCardSelectScreen simple && Screens.TryGetValue(simple, out state!);
    }

    internal static void Initialize(NSimpleCardSelectScreen screen)
    {
        if (!Screens.TryGetValue(screen, out var state)) return;
        var tree = screen.GetTree();
        // Grid holders are pooled and rebound while scrolling; keep their badges on the right card.
        void SyncBadges()
        {
            if (GodotObject.IsInstanceValid(screen)) UpdateBadges(Grid(screen), screen, state);
        }
        tree.ProcessFrame += SyncBadges;
        screen.TreeExiting += () =>
        {
            tree.ProcessFrame -= SyncBadges;
            RemoveBadges(state);
        };
        Refresh(screen, state);
    }

    // Pooled holders outlive this screen; strip our badges and input handlers before they are reused.
    private static void RemoveBadges(State state)
    {
        foreach (var (holder, badge, input) in state.Badges)
        {
            if (GodotObject.IsInstanceValid(holder)) holder.GuiInput -= input;
            if (!GodotObject.IsInstanceValid(badge)) continue;
            badge.GetParent()?.RemoveChild(badge);
            badge.QueueFree();
        }
        state.Badges.Clear();
    }

    private static NCardGrid Grid(NSimpleCardSelectScreen screen) =>
        (NCardGrid)AccessTools.Field(typeof(NCardGridSelectionScreen), "_grid").GetValue(screen)!;

    internal static void Change(NSimpleCardSelectScreen screen, State state, CardModel card, int delta)
    {
        var index = IndexOf(state, card);
        if (index < 0 || !(delta > 0 ? state.Selection.Add(index) : state.Selection.Remove(index))) return;
        Refresh(screen, state);
    }

    internal static int IndexOf(State state, CardModel? card)
    {
        for (var i = 0; i < state.Cards.Count; i++)
            if (ReferenceEquals(state.Cards[i], card)) return i;
        return -1;
    }

    internal static void Refresh(NSimpleCardSelectScreen screen, State state)
    {
        var grid = Grid(screen);
        for (var i = 0; i < state.Cards.Count; i++)
        {
            if (state.Selection.CopiesOf(i) > 0) grid.HighlightCard(state.Cards[i]);
            else grid.UnhighlightCard(state.Cards[i]);
        }
        var confirm = (NConfirmButton?)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_confirmButton").GetValue(screen);
        if (state.Selection.IsComplete) confirm?.Enable();
        else confirm?.Disable();
        if (AccessTools.Field(typeof(NSimpleCardSelectScreen), "_infoLabel").GetValue(screen) is MegaRichTextLabel label)
            label.Text = InfoText(state.Selection.Total, state.Selection.Count,
                LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru");
        UpdateBadges(grid, screen, state);
    }

    internal static string InfoText(int total, int count, bool russian) => russian
        ? $"Выбрано [blue]{total}[/blue]/{count}. Нажмите на карту, чтобы добавить копию; ПКМ или X — убрать."
        : $"Chosen [blue]{total}[/blue]/{count}. Click a card to add a copy; right-click or X removes one.";

    internal static void UpdateBadges(NCardGrid grid, NSimpleCardSelectScreen screen, State state)
    {
        // A holder scrolled out returns to the pool and may be shown by another grid (the deck view).
        foreach (var (holder, badge, _) in state.Badges)
            if (GodotObject.IsInstanceValid(badge) && (!GodotObject.IsInstanceValid(holder) || !grid.IsAncestorOf(holder)))
                badge.Visible = false;
        foreach (var holder in grid.CurrentlyDisplayedCardHolders)
        {
            var badge = holder.GetNodeOrNull<Label>("HandpickedCopies") ?? CreateBadge(holder, screen, state);
            var index = IndexOf(state, holder.CardModel);
            var copies = index < 0 ? 0 : state.Selection.CopiesOf(index);
            badge.Visible = copies > 0;
            badge.Text = $"×{copies}";
        }
    }

    private static Label CreateBadge(NGridCardHolder holder, NSimpleCardSelectScreen screen, State state)
    {
        // Controller remove button. Focused-control input runs before the game's hotkeys,
        // so accepting it keeps X from also triggering the potion shortcut on this screen.
        Control.GuiInputEventHandler remove = input =>
        {
            if (!input.IsActionPressed(MegaInput.topPanel) || input.IsEcho() || !GodotObject.IsInstanceValid(screen) ||
                !Grid(screen).IsAncestorOf(holder)) return;
            Change(screen, state, holder.CardModel!, -1);
            holder.AcceptEvent();
        };
        holder.GuiInput += remove;
        // Holders are centered on their card; place the count over the card's top-right corner.
        var half = NCard.defaultSize / 2;
        var badge = new Label
        {
            Name = "HandpickedCopies", MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Position = new Vector2(half.X - 150, -half.Y - 12), Size = new Vector2(150, 72), ZIndex = 5
        };
        badge.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        badge.AddThemeFontSizeOverride("font_size", 56);
        badge.AddThemeColorOverride("font_color", BadgeColor);
        badge.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        badge.AddThemeConstantOverride("outline_size", 14);
        holder.AddChild(badge);
        state.Badges.Add((holder, badge, remove));
        return badge;
    }
}

[HarmonyPatch(typeof(NSimpleCardSelectScreen), nameof(NSimpleCardSelectScreen._Ready))]
internal static class HandpickedReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NSimpleCardSelectScreen __instance) => HandpickedUi.Initialize(__instance);
}

[HarmonyPatch(typeof(NSimpleCardSelectScreen), "OnCardClicked")]
internal static class HandpickedClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NSimpleCardSelectScreen __instance, CardModel __0)
    {
        if (!HandpickedUi.TryGet(__instance, out var state)) return true;
        HandpickedUi.Change(__instance, state, __0, 1);
        return false;
    }
}

[HarmonyPatch(typeof(NCardGridSelectionScreen), "ShowCardDetail")]
internal static class HandpickedRightClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCardGridSelectionScreen __instance, CardModel __0)
    {
        // Right-click removes a copy; on cards with no copies it keeps the native card inspection.
        if (!HandpickedUi.TryGet(__instance, out var state)) return true;
        var index = HandpickedUi.IndexOf(state, __0);
        if (index < 0 || state.Selection.CopiesOf(index) == 0) return true;
        HandpickedUi.Change((NSimpleCardSelectScreen)__instance, state, __0, -1);
        return false;
    }
}

[HarmonyPatch(typeof(NSimpleCardSelectScreen), "CompleteSelection")]
internal static class HandpickedConfirmPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NSimpleCardSelectScreen __instance) =>
        !HandpickedUi.TryGet(__instance, out var state) || state.Selection.IsComplete;
}
