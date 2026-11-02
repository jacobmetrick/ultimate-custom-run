using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

internal static class ModifierVariantUi
{
    internal const string RandomLabelKey = "ultimate_custom_run.variant.random";
    internal const string AnyLabelKey = "ultimate_custom_run.variant.any";
    internal const string NormalLabelKey = "ultimate_custom_run.variant.normal";
    internal const string DraftLabelKey = "ultimate_custom_run.variant.draft";
    internal const string DraftDescriptionKey = "ultimate_custom_run.variant.draft_description";
    internal const string PickAnyLabelKey = "ultimate_custom_run.variant.pick_any";
    internal const string PickAnyDescriptionKey = "ultimate_custom_run.variant.pick_any_description";

    internal enum Family { Specialized, AllStar, Friendship, TwentyTwenty }
    internal enum Mode { Normal, Draft, PickAny }

    internal static readonly Family[] Families = [Family.Specialized, Family.AllStar, Family.Friendship, Family.TwentyTwenty];

    internal sealed class VariantRow
    {
        internal required NRunModifierTickbox ParentRow;
        internal required HBoxContainer Container;
    }

    private sealed class State
    {
        internal Family Family;
        internal NRunModifierTickbox? ParentRow;
        internal NRunModifierTickbox? DraftRow;
        internal NRunModifierTickbox? PickAnyRow;
        internal HBoxContainer? Container;
        internal NRunModifierTickbox[] Choices = [];
        internal Mode Selected = Mode.Normal;
        internal bool ChoicesReady;
        internal bool IsApplying;
        internal Action? LayoutChanged;
    }

    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Dictionary<Family, State>> States = new();
    private static readonly System.Reflection.MethodInfo AfterModifiersChanged =
        AccessTools.Method(typeof(NCustomRunModifiersList), "AfterModifiersChanged");

    internal static bool IsParent(ModifierModel? modifier) =>
        modifier is Specialized or AllStar or Friendship or TwentyTwenty;

    internal static bool IsParent(ModifierModel? modifier, Family family) => family switch
    {
        Family.Specialized => modifier is Specialized,
        Family.AllStar => modifier is AllStar,
        Family.Friendship => modifier is Friendship,
        Family.TwentyTwenty => modifier is TwentyTwenty,
        _ => false
    };

    internal static bool IsDraft(ModifierModel? modifier, Family family) => family switch
    {
        Family.Specialized => modifier is SpecializedDraft,
        Family.AllStar => modifier is AllStarDraft,
        Family.Friendship => modifier is FriendshipDraft,
        Family.TwentyTwenty => modifier is TwentyTwentyDraft,
        _ => false
    };

    internal static bool IsPickAny(ModifierModel? modifier, Family family) =>
        (family == Family.Specialized && modifier is SpecializedPickAny) ||
        (family == Family.TwentyTwenty && modifier is TwentyTwentyAny);

    internal static bool IsHiddenVariant(ModifierModel? modifier) =>
        modifier is SpecializedDraft or SpecializedPickAny or AllStarDraft or FriendshipDraft or TwentyTwentyDraft or TwentyTwentyAny;

    internal static bool BelongsTo(ModifierModel? modifier, Family family) =>
        IsParent(modifier, family) || IsDraft(modifier, family) || IsPickAny(modifier, family);

    internal static VariantRow Create(
        NCustomRunModifiersList list,
        Family family,
        NRunModifierTickbox parentRow,
        NRunModifierTickbox draftRow,
        NRunModifierTickbox? pickAnyRow,
        Action layoutChanged)
    {
        var state = GetState(list, family);
        state.Family = family;
        state.ParentRow = parentRow;
        state.DraftRow = draftRow;
        state.PickAnyRow = pickAnyRow;
        state.LayoutChanged = layoutChanged;

        var container = new HBoxContainer
        {
            Name = family + "Variants",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 52)
        };
        container.AddThemeConstantOverride("separation", 12);
        container.AddChild(new Control
        {
            Name = "Indent",
            CustomMinimumSize = new Vector2(52, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        // The list already holds mutable copies. ToMutable() only accepts the canonical
        // ModelDb instance, and that instance is already mutable by the time this screen builds.
        var choices = new List<NRunModifierTickbox>
        {
            CreateChoice(DisplayCopy(parentRow.Modifier!), 0),
            CreateChoice(DisplayCopy(draftRow.Modifier!), 1)
        };
        if (pickAnyRow?.Modifier != null) choices.Add(CreateChoice(DisplayCopy(pickAnyRow.Modifier), 2));
        state.Choices = choices.ToArray();
        state.Container = container;

        for (var i = 0; i < state.Choices.Length; i++)
        {
            var choice = state.Choices[i];
            choice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            choice.CustomMinimumSize = new Vector2(96, 48);
            container.AddChild(choice);
            // Tickbox _Ready writes the full modifier sentence. Replace it after that.
            choice.Ready += () => SetChoiceLabels(state);
            ConnectChoice(list, state, choice, (Mode)i);
        }

        ConfigureTooltip(state.Choices[0], family == Family.TwentyTwenty ? RandomLabelKey : NormalLabelKey, () => parentRow.Modifier!.Description);
        ConfigureTooltip(state.Choices[1], DraftLabelKey, new LocString("modifiers", DraftDescriptionKey));
        if (state.Choices.Length == 3)
            ConfigureTooltip(state.Choices[2], family == Family.TwentyTwenty ? AnyLabelKey : PickAnyLabelKey, new LocString("modifiers", PickAnyDescriptionKey));
        UpdateInteractionMode(list, state);

        return new VariantRow { ParentRow = parentRow, Container = container };
    }

    internal static void InitializeChoices(NCustomRunModifiersList list)
    {
        foreach (var state in GetStates(list).Values)
        {
            if (state.ParentRow == null || state.Choices.Length == 0) continue;
            state.ChoicesReady = true;
            SetChoiceLabels(state);
        }
        RefreshFromTickboxes(list);
    }

    internal static void ApplyIncomingModifiers(NCustomRunModifiersList list, IReadOnlyCollection<ModifierModel> modifiers)
    {
        foreach (var family in Families)
        {
            var state = GetState(list, family);
            var selected = modifiers.FirstOrDefault(modifier => IsDraft(modifier, family) || IsPickAny(modifier, family))
                ?? modifiers.FirstOrDefault(modifier => IsParent(modifier, family));
            state.Selected = selected switch
            {
                null => Mode.Normal,
                _ when IsDraft(selected, family) => Mode.Draft,
                _ when IsPickAny(selected, family) => Mode.PickAny,
                _ => Mode.Normal
            };
            ApplyEnabledState(state, selected != null);
        }
    }

    internal static void RefreshFromTickboxes(NCustomRunModifiersList list)
    {
        foreach (var state in GetStates(list).Values)
        {
            if (state.ParentRow == null) continue;
            if (state.DraftRow?.IsTicked == true) state.Selected = Mode.Draft;
            else if (state.PickAnyRow?.IsTicked == true) state.Selected = Mode.PickAny;
            ApplyEnabledState(state, state.ParentRow.IsTicked ||
                state.DraftRow?.IsTicked == true || state.PickAnyRow?.IsTicked == true);
        }
    }

    internal static void UpdateInteractionMode(NCustomRunModifiersList list)
    {
        foreach (var state in GetStates(list).Values) UpdateInteractionMode(list, state);
    }

    internal static void TransformSelectedModifiers(NCustomRunModifiersList list, ref List<ModifierModel> modifiers)
    {
        foreach (var (family, state) in GetStates(list))
        {
            if (state.ParentRow == null) continue;
            var selectedChoice = Array.FindIndex(state.Choices, choice => choice.IsTicked);
            if (selectedChoice >= 0) state.Selected = (Mode)selectedChoice;

            var insertionIndex = modifiers.FindIndex(modifier => BelongsTo(modifier, family));
            var enabled = state.ParentRow.IsTicked || state.DraftRow?.IsTicked == true || state.PickAnyRow?.IsTicked == true;
            modifiers.RemoveAll(modifier => BelongsTo(modifier, family));
            if (!enabled) continue;

            var selected = state.Selected switch
            {
                Mode.Draft => state.DraftRow?.Modifier,
                Mode.PickAny => state.PickAnyRow?.Modifier,
                _ => state.ParentRow.Modifier
            };
            if (selected != null)
                modifiers.Insert(insertionIndex < 0 ? modifiers.Count : Math.Min(insertionIndex, modifiers.Count), selected);
        }
    }

    private static NRunModifierTickbox CreateChoice(ModifierModel modifier, int index)
    {
        var choice = NRunModifierTickbox.Create(modifier)
            ?? throw new InvalidOperationException("Could not create a modifier variant checkbox.");
        choice.Name = "ModifierVariant" + index;
        choice.FocusMode = Control.FocusModeEnum.None;
        choice.MouseFilter = Control.MouseFilterEnum.Stop;
        return choice;
    }

    private static ModifierModel DisplayCopy(ModifierModel modifier) =>
        (ModifierModel)modifier.MutableClone();

    private static void ConnectChoice(NCustomRunModifiersList list, State state, NRunModifierTickbox choice, Mode mode)
    {
        choice.Toggled += toggled =>
        {
            if (state.IsApplying) return;
            if (toggled.IsTicked) Select(list, state, mode);
            else if (state.Selected == mode) ApplyState(state);
        };
    }

    private static void SetChoiceLabels(State state)
    {
        var keys = state.Choices.Length == 3
            ? new[] { state.Family == Family.TwentyTwenty ? RandomLabelKey : NormalLabelKey, DraftLabelKey,
                state.Family == Family.TwentyTwenty ? AnyLabelKey : PickAnyLabelKey }
            : new[] { NormalLabelKey, DraftLabelKey };
        for (var i = 0; i < state.Choices.Length; i++)
            state.Choices[i].GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text =
                new LocString("modifiers", keys[i]).GetFormattedText();
    }

    private static void ConfigureTooltip(NRunModifierTickbox choice, string titleKey, LocString description)
        => ConfigureTooltip(choice, titleKey, () => description);

    private static void ConfigureTooltip(NRunModifierTickbox choice, string titleKey, Func<LocString> description)
    {
        choice.MouseEntered += () => ShowTooltip(choice, new HoverTip(new LocString("modifiers", titleKey), description(), null!));
        choice.MouseExited += () => NHoverTipSet.Remove(choice);
    }

    private static void ShowTooltip(Control owner, IHoverTip tip)
    {
        NHoverTipSet.Remove(owner);
        NHoverTipSet.CreateAndShow(owner, tip, HoverTipAlignment.Right);
    }

    private static void Select(NCustomRunModifiersList list, State state, Mode mode)
    {
        if (state.IsApplying || state.ParentRow == null) return;
        state.Selected = mode;
        ApplyEnabledState(state, true);
        AfterModifiersChanged.Invoke(list, [state.ParentRow]);
    }

    private static void ApplyEnabledState(State state, bool enabled)
    {
        var wasApplying = state.IsApplying;
        state.IsApplying = true;
        try
        {
            if (state.ParentRow != null) state.ParentRow.IsTicked = enabled;
            if (state.DraftRow != null) state.DraftRow.IsTicked = false;
            if (state.PickAnyRow != null) state.PickAnyRow.IsTicked = false;
            ApplyState(state);
        }
        finally { state.IsApplying = wasApplying; }
    }

    private static void ApplyState(State state)
    {
        if (state.Container != null)
        {
            var wasVisible = state.Container.Visible;
            state.Container.Visible = state.ParentRow?.IsTicked == true;
            if (!state.Container.Visible)
                foreach (var choice in state.Choices) NHoverTipSet.Remove(choice);
            if (wasVisible != state.Container.Visible) state.LayoutChanged?.Invoke();
        }

        if (!state.ChoicesReady) return;
        var wasApplying = state.IsApplying;
        state.IsApplying = true;
        try
        {
            for (var i = 0; i < state.Choices.Length; i++)
                state.Choices[i].IsTicked = i == (int)state.Selected;
        }
        finally { state.IsApplying = wasApplying; }
    }

    private static void UpdateInteractionMode(NCustomRunModifiersList list, State state)
    {
        if (state.Choices.Length == 0) return;
        var mode = (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!;
        var disabled = mode is not (MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host);
        foreach (var choice in state.Choices)
        {
            choice.MouseFilter = disabled ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
            if (disabled) NHoverTipSet.Remove(choice);
        }
    }

    private static State GetState(NCustomRunModifiersList list, Family family)
    {
        var states = GetStates(list);
        if (!states.TryGetValue(family, out var state)) states.Add(family, state = new State());
        return state;
    }

    private static Dictionary<Family, State> GetStates(NCustomRunModifiersList list) =>
        States.GetOrCreateValue(list);
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "GetModifiersTickedOn")]
internal static class ModifierVariantSelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, ref List<ModifierModel> __result)
    {
        ModifierVariantUi.TransformSelectedModifiers(__instance, ref __result);
        ModifierGroupsUi.RemoveLockedModifiers(__instance, __result);
        var selected = __result;
        selected.RemoveAll(modifier => modifier is SubmodifierModel child && !child.HasParent(selected));
        // Neow runs start bonuses in this list's order.
        StartOrder.Apply(__instance, selected);
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "AfterModifiersChanged")]
internal static class ModifierVariantAfterChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance)
    {
        ModifierVariantUi.RefreshFromTickboxes(__instance);
        ModifierVariantUi.UpdateInteractionMode(__instance);
        ModifierValueUi.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "Initialize")]
internal static class ModifierVariantInitializePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance)
    {
        ModifierVariantUi.UpdateInteractionMode(__instance);
        ModifierGroupsUi.ApplyMode(__instance);
        ModifierValueUi.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "SetTickedModifiers")]
internal static class ModifierVariantSetTickedPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCustomRunModifiersList __instance, IReadOnlyCollection<ModifierModel> __0)
    {
        ModifierValueUi.ApplyIncoming(__instance, __0);
        StartOrder.ApplyIncoming(__instance, __0);
        // The native method emits ModifiersChanged before our postfix runs.
        // Restore the variant choices first so listeners receive the incoming mode.
        ModifierVariantUi.ApplyIncomingModifiers(__instance, __0);
    }

    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, IReadOnlyCollection<ModifierModel> __0)
    {
        ModifierVariantUi.ApplyIncomingModifiers(__instance, __0);
        ModifierGroupsUi.ApplyMode(__instance);
        ModifierValueUi.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), "SyncModifierList")]
internal static class ModifierVariantSyncPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCustomRunModifiersList __instance, IReadOnlyCollection<ModifierModel> __0)
    {
        ModifierValueUi.ApplyIncoming(__instance, __0);
        StartOrder.ApplyIncoming(__instance, __0);
    }

    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, IReadOnlyCollection<ModifierModel> __0)
    {
        ModifierVariantUi.ApplyIncomingModifiers(__instance, __0);
        ModifierGroupsUi.ApplyMode(__instance);
        ModifierValueUi.Refresh(__instance);
    }
}
