using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace UltimateCustomRun;

internal static class ModifierValues
{
    internal const string SaveKey = nameof(SpecializedDraft.CustomValue);
    internal const string SealedPoolKey = nameof(SpecializedDraft.SealedPoolSize);
    internal static readonly Spec SealedPoolSpec = new(10, 60, 5, 30, "offers");
    internal sealed record Spec(int Min, int Max, int Step, int Default, string Unit)
    {
        internal int Normalize(int value) => Min + (int)Math.Round(
            (Math.Clamp(value, Min, Max) - Min) / (double)Step, MidpointRounding.AwayFromZero) * Step;
    }
    private sealed class ValueState { internal int Value; internal int PoolSize = 30; }
    private static readonly ConditionalWeakTable<ModifierModel, ValueState> Values = new();

    internal static Spec? For(ModifierModel? modifier) => modifier switch
    {
        Specialized or SpecializedDraft or SpecializedPickAny or AllStar or AllStarDraft or Friendship or FriendshipDraft
            => new(1, 10, 1, 5, "cards"),
        TwentyTwentyModifier => new(1, 10, 1, 1, "cards"),
        Draft => new(5, 20, 5, 10, "cards"),
        SealedDeck => new(5, GetSealedPool(modifier) - 5, 5, 10, "cards"),
        Insanity => new(5, 60, 5, 30, "cards"),
        Handpicked => new(5, 60, 5, 10, "cards"),
        Hoarder => new(1, 5, 1, 2, "extra copies"),
        Midas => new(150, 300, 5, 200, "% gold"),
        DoubleTrouble => new(2, 3, 1, 2, "bosses"),
        Speedrun => new(10, 60, 5, 30, "minutes"),
        Dill => new(1, 20, 1, 1, "initial max HP"),
        Headstart => new(1, 5, 1, 1, "relics"),
        RichLoot => new(1, 3, 1, 1, "extra relics"),
        CardSwarm => new(1, 3, 1, 1, "extra cards"),
        MysteryEvents => new(1, 5, 1, MysteryEvents.EventCount, "events"),
        _ => null
    };

    internal static int Get(ModifierModel modifier)
    {
        var spec = For(modifier) ?? throw new ArgumentException("Modifier has no configurable value.");
        return spec.Normalize(Values.TryGetValue(modifier, out var state) ? state.Value : spec.Default);
    }

    internal static int GetSealedPool(ModifierModel modifier) => Values.TryGetValue(modifier, out var state)
        ? state.PoolSize : SealedPoolSpec.Default;

    internal static void SetSealedPool(ModifierModel modifier, int value)
    {
        modifier.AssertMutable();
        if (modifier is not SealedDeck) throw new ArgumentException("Only Sealed Deck has a configurable pool.");
        var selected = Get(modifier);
        var state = Values.GetOrCreateValue(modifier);
        state.PoolSize = SealedPoolSpec.Normalize(value);
        state.Value = For(modifier)!.Normalize(selected);
    }

    internal static int SealedPoolForPlayer(Player player) => GetSealedPool(
        player.RunState.Modifiers.FirstOrDefault(modifier => modifier is SealedDeck) ?? ModelDb.Modifier<SealedDeck>());

    internal static CardCreationOptions FixedSealedPool(CardCreationOptions options) =>
        options.WithFlags(CardCreationFlags.NoModifyHooks);

    internal static void Set(ModifierModel modifier, int value)
    {
        modifier.AssertMutable();
        var spec = For(modifier) ?? throw new ArgumentException("Modifier has no configurable value.");
        Values.GetOrCreateValue(modifier).Value = spec.Normalize(value);
    }

    internal static int ForPlayer<T>(Player player) where T : ModifierModel =>
        player.RunState.Modifiers.FirstOrDefault(modifier => modifier is T) is { } modifier
            ? Get(modifier) : Get(ModelDb.Modifier<T>());

    internal static int SpecializedCount(Player player) =>
        Get(player.RunState.Modifiers.First(modifier => modifier is Specialized or SpecializedDraft or SpecializedPickAny));

    internal static int FriendshipCount(Player player) =>
        Get(player.RunState.Modifiers.First(modifier => modifier is Friendship or FriendshipDraft));

    internal static int ScaleGold(int gold, ModifierModel modifier) => (int)((long)gold * Get(modifier) / 100);

    internal static LocString WithValue(LocString original, int originalValue, int value)
    {
        var key = original.LocEntryKey + ".ultimate_value_" + value;
        var text = Regex.Replace(original.GetRawText(), @"\[blue\]" + originalValue + @"(%?)\[/blue\]",
            match => "[blue]" + value + match.Groups[1].Value + "[/blue]");
        LocManager.Instance.GetTable(original.LocTable).MergeWith(new Dictionary<string, string> { [key] = text });
        var result = new LocString(original.LocTable, key);
        result.AddVariablesFrom(original);
        return result;
    }

    internal static LocString SealedPrompt(LocString original, Player player) =>
        WithValue(original, 10, ForPlayer<SealedDeck>(player));

    internal static string SealedDescriptionText(string original, int selected, int pool) =>
        Regex.Replace(original, @"\[blue\](10|30)\[/blue]",
            match => "[blue]" + (match.Groups[1].Value == "10" ? selected : pool) + "[/blue]");

    internal static string DillDescriptionText(string original, int initial, int growth) =>
        Regex.Replace(original, @"\[blue\](1|2)\[/blue]",
            match => "[blue]" + (match.Groups[1].Value == "1" ? initial : growth) + "[/blue]");

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.ToSerializable))]
    private static class SavePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ModifierModel __instance, SerializableModifier __result)
        {
            if (!Values.TryGetValue(__instance, out var state)) return;
            __result.Props ??= new SavedProperties();
            __result.Props.ints ??= [];
            __result.Props.ints.RemoveAll(prop => prop.name == SaveKey);
            __result.Props.ints.Add(new(SaveKey, state.Value));
            if (__instance is SealedDeck)
            {
                __result.Props.ints.RemoveAll(prop => prop.name == SealedPoolKey);
                __result.Props.ints.Add(new(SealedPoolKey, state.PoolSize));
            }
        }
    }

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.FromSerializable))]
    private static class LoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SerializableModifier __0, ModifierModel __result)
        {
            if (For(__result) == null) return;
            var pool = __0.Props?.ints?.FirstOrDefault(prop => prop.name == SealedPoolKey);
            if (__result is SealedDeck && pool is { name: SealedPoolKey }) SetSealedPool(__result, pool.Value.value);
            var saved = __0.Props?.ints?.FirstOrDefault(prop => prop.name == SaveKey);
            if (__result is MysteryEvents mystery && mystery.MysteryHistoryFloors < 0)
                mystery.MysteryHistoryFloors = Math.Min(mystery.MysteryStage,
                    saved is { name: SaveKey } ? Math.Clamp(saved.Value.value, 1, 10) : MysteryEvents.EventCount);
            if (saved is { name: SaveKey }) Set(__result, saved.Value.value);
        }
    }

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class ClonePatch
    {
        [HarmonyPostfix]
        private static void Postfix(AbstractModel __instance, AbstractModel __result)
        {
            if (__instance is ModifierModel source && __result is ModifierModel target && Values.TryGetValue(source, out var state))
            {
                if (source is SealedDeck) SetSealedPool(target, state.PoolSize);
                Set(target, state.Value);
            }
        }
    }

    [HarmonyPatch(typeof(ModifierModel), nameof(ModifierModel.Description), MethodType.Getter)]
    private static class DescriptionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ModifierModel __instance, ref LocString __result)
        {
            if (__instance is CustomRunParameters parameters)
            {
                var values = CustomRunParameterValuesStore.Get(parameters);
                var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
                var key = __result.LocEntryKey + $".ultimate_parameters_{values.FloorsPerAct}_{values.BaseHandSize}_{values.BaseEnergy}_{values.EnemyHpPercent}_{values.EnemyDamagePercent}_{values.PlayerHpPercent}";
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string>
                    { [key] = CustomRunParametersUi.DescriptionText(values, russian) });
                __result = new LocString(__result.LocTable, key);
                return;
            }
            if (__instance is RichLoot or CardSwarm)
            {
                var count = Get(__instance);
                var key = __result.LocEntryKey + ".ultimate_reward_" + count;
                var text = __instance is RichLoot ? RichLoot.DescriptionText(count) : CardSwarm.DescriptionText(count);
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string> { [key] = text });
                __result = new LocString(__result.LocTable, key);
                return;
            }
            if (__instance is MysteryEvents)
            {
                var count = Get(__instance);
                var key = __result.LocEntryKey + ".ultimate_mystery_" + count;
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string>
                    { [key] = MysteryEvents.DescriptionText(count) });
                __result = new LocString(__result.LocTable, key);
                return;
            }
            if (__instance is Headstart)
            {
                var count = Get(__instance);
                var key = __result.LocEntryKey + ".ultimate_headstart_" + count;
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string>
                    { [key] = Headstart.DescriptionText(count) });
                __result = new LocString(__result.LocTable, key);
                return;
            }
            if (__instance is Dill dill)
            {
                var key = __result.LocEntryKey + $".ultimate_dill_{Get(dill)}_{dill.MaxHpPerFight}";
                var text = DillDescriptionText(__result.GetRawText(), Get(dill), dill.MaxHpPerFight);
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string> { [key] = text });
                var result = new LocString(__result.LocTable, key);
                result.AddVariablesFrom(__result);
                __result = result;
                return;
            }
            if (For(__instance) is not { } spec || !Values.TryGetValue(__instance, out var state)) return;
            if (__instance is SealedDeck)
            {
                var key = __result.LocEntryKey + $".ultimate_sealed_{Get(__instance)}_{state.PoolSize}";
                var text = SealedDescriptionText(__result.GetRawText(), Get(__instance), state.PoolSize);
                LocManager.Instance.GetTable(__result.LocTable).MergeWith(new Dictionary<string, string> { [key] = text });
                var result = new LocString(__result.LocTable, key);
                result.AddVariablesFrom(__result);
                __result = result;
                return;
            }
            __result = WithValue(__result, spec.Default, state.Value);
        }
    }
}

// Keep the game's rewards, RNG, hooks and animations. Only replace the actual count
// in each async state machine; fail at patch time if a future game update changes it.
[HarmonyPatch]
internal static class NativeModifierCountPatch
{
    internal static readonly (Type Type, string Method, int Default)[] Targets =
    [
        (typeof(Specialized), "ObtainCards", 5), (typeof(AllStar), "ObtainCards", 5),
        (typeof(Draft), "OfferRewards", 10), (typeof(SealedDeck), "ChooseCards", 10),
        (typeof(Insanity), "ObtainCards", 30), (typeof(Hoarder), nameof(Hoarder.AfterCardChangedPiles), 2)
    ];

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Select(target =>
        (MethodBase)AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var target = Targets.Single(target => AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)) == __originalMethod);
        var field = AccessTools.Field(__originalMethod.DeclaringType, target.Type == typeof(Hoarder) ? "<>4__this" : "player")
            ?? throw new InvalidOperationException("Modifier state machine no longer exposes its value owner.");
        var resolver = target.Type == typeof(Hoarder)
            ? AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.Get))
            : AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.ForPlayer)).MakeGenericMethod(target.Type);
        var replacements = 0;
        var poolReplacements = 0;
        foreach (var instruction in instructions)
        {
            if (target.Type == typeof(SealedDeck) && instruction.LoadsConstant(30))
            {
                poolReplacements++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldfld, field);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.SealedPoolForPlayer)));
                continue;
            }
            if (target.Type == typeof(SealedDeck) && instruction.operand is MethodInfo factory &&
                factory.DeclaringType == typeof(MegaCrit.Sts2.Core.Factories.CardFactory) && factory.Name == "CreateForReward")
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.FixedSealedPool)))
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            if (instruction.LoadsConstant(target.Default))
            {
                replacements++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldfld, field);
                yield return new CodeInstruction(OpCodes.Call, resolver);
            }
            else
            {
                yield return instruction;
                if (target.Type == typeof(SealedDeck) && instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(LocString))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, field);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.SealedPrompt)));
                }
            }
        }
        if (replacements != 1) throw new InvalidOperationException($"{target.Type.Name} count changed: expected one value, found {replacements}.");
        if (target.Type == typeof(SealedDeck) && poolReplacements != 1)
            throw new InvalidOperationException($"Sealed Deck pool changed: expected one value, found {poolReplacements}.");
    }
}

[HarmonyPatch(typeof(Midas), nameof(Midas.TryModifyRewardsLate))]
internal static class MidasValuePatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            if (code[i].LoadsConstant(2) && i + 1 < code.Count && code[i + 1].opcode == OpCodes.Mul)
            {
                replacements++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[i]).MoveBlocksFrom(code[i]);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModifierValues), nameof(ModifierValues.ScaleGold)))
                    .MoveLabelsFrom(code[++i]).MoveBlocksFrom(code[i]);
            }
            else yield return code[i];
        }
        if (replacements != 1) throw new InvalidOperationException("Midas gold multiplier changed.");
    }
}
