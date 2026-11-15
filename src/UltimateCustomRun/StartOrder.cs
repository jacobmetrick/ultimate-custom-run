using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

internal sealed class StartOrderState
{
    // Order keys of the selected start bonuses; variants share their family parent's key.
    internal List<Type> Keys = [];
    // False while Keys equals the native list order, so untouched runs behave exactly as before.
    internal bool Customized;
}

internal static class StartOrder
{
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, StartOrderState> States = new();
    private static readonly HashSet<Type> NoNegatives = [];
    private static readonly Dictionary<Type, bool> OrderableTypes = [];

    internal static StartOrderState For(NCustomRunModifiersList list) => States.GetOrCreateValue(list);

    // Neow offers each modifier's start step in RunState.Modifiers order; only those steps can be reordered.
    internal static bool IsOrderable(ModifierModel? modifier)
    {
        if (modifier == null || ModifierGroups.Classify(modifier, NoNegatives) != ModifierGroup.ImprovedStart) return false;
        var type = modifier.GetType();
        lock (OrderableTypes)
        {
            if (!OrderableTypes.TryGetValue(type, out var orderable))
                OrderableTypes[type] = orderable =
                    type.GetMethod(nameof(ModifierModel.GenerateNeowOption))!.DeclaringType != typeof(ModifierModel);
            return orderable;
        }
    }

    internal static Type KeyOf(ModifierModel modifier)
    {
        foreach (var family in ModifierVariantUi.Families)
        {
            if (!ModifierVariantUi.BelongsTo(modifier, family)) continue;
            return family switch
            {
                ModifierVariantUi.Family.Specialized => typeof(Specialized),
                ModifierVariantUi.Family.AllStar => typeof(AllStar),
                ModifierVariantUi.Family.Friendship => typeof(Friendship),
                _ => typeof(TwentyTwenty)
            };
        }
        return modifier.GetType();
    }

    internal static void Apply(NCustomRunModifiersList list, List<ModifierModel> modifiers) => Apply(For(list), modifiers);

    // Reorders start bonuses within the slots they already occupy, so every other modifier keeps its position.
    internal static void Apply(StartOrderState state, List<ModifierModel> modifiers)
    {
        var slots = Enumerable.Range(0, modifiers.Count).Where(index => IsOrderable(modifiers[index])).ToArray();
        var selected = slots.Select(index => modifiers[index]).ToList();
        Sync(state, selected.Select(KeyOf).Distinct().ToList());
        var ordered = selected.OrderBy(modifier => state.Keys.IndexOf(KeyOf(modifier))).ToList();
        for (var i = 0; i < slots.Length; i++) modifiers[slots[i]] = ordered[i];
    }

    internal static void ApplyIncoming(NCustomRunModifiersList list, IEnumerable<ModifierModel> modifiers) =>
        ApplyIncoming(For(list), modifiers);

    // Presets, host syncs and loaded saves carry the order in their modifier list order.
    internal static void ApplyIncoming(StartOrderState state, IEnumerable<ModifierModel> modifiers)
    {
        state.Keys = modifiers.Where(IsOrderable).Select(KeyOf).Distinct().ToList();
        // The next Apply compares against the native order and clears this when they match.
        state.Customized = true;
    }

    internal static bool Move(StartOrderState state, Type key, int delta)
    {
        var index = state.Keys.IndexOf(key);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= state.Keys.Count) return false;
        (state.Keys[index], state.Keys[target]) = (state.Keys[target], state.Keys[index]);
        state.Customized = true;
        return true;
    }

    private static void Sync(StartOrderState state, List<Type> nativeOrder)
    {
        if (!state.Customized)
        {
            state.Keys = nativeOrder;
            return;
        }
        // Keep the chosen order; newly ticked bonuses run last.
        state.Keys.RemoveAll(key => !nativeOrder.Contains(key));
        state.Keys.AddRange(nativeOrder.Where(key => !state.Keys.Contains(key)));
        if (state.Keys.SequenceEqual(nativeOrder)) state.Customized = false;
    }
}
