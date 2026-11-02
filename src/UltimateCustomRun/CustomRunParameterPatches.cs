using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

[HarmonyPatch(typeof(RunState), "CreateShared")]
internal static class CustomRunStartingHealthPatch
{
    private static readonly MethodInfo SetMaxHp = AccessTools.Method(typeof(Creature), "set_MaxHp")!;
    private static readonly MethodInfo SetCurrentHp = AccessTools.Method(typeof(Creature), "set_CurrentHp")!;

    [HarmonyPostfix]
    private static void Postfix(RunState __result)
    {
        var percent = CustomRunParameterValuesStore.From(__result.Modifiers).PlayerHpPercent;
        if (percent == 100) return;

        foreach (var player in __result.Players)
        {
            var creature = player.Creature;
            if (creature == null) continue;
            var maxHp = ScaleHp(creature.MaxHp, percent);
            SetMaxHp.Invoke(creature, [maxHp]);
            SetCurrentHp.Invoke(creature, [Math.Min(maxHp, ScaleHp(creature.CurrentHp, percent))]);
        }
    }

    private static int ScaleHp(int hp, int percent) =>
        Math.Max(1, (int)Math.Round(hp * percent / 100d, MidpointRounding.AwayFromZero));
}

[HarmonyPatch(typeof(Player), "get_MaxEnergy")]
internal static class CustomRunBaseEnergyPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance, ref int __result)
    {
        if (__instance.RunState is not { } runState) return;
        var configured = CustomRunParameterValuesStore.From(runState.Modifiers).BaseEnergy;
        if (configured >= 0) __result = configured;
    }
}

[HarmonyPatch]
internal static class CustomRunBaseHandSizePatch
{
    private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
        AccessTools.Method(typeof(CombatManager), "SetupPlayerTurn")
        ?? throw new MissingMethodException(typeof(CombatManager).FullName, "SetupPlayerTurn"));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var playerField = AccessTools.Field(__originalMethod.DeclaringType, "player")
            ?? throw new InvalidOperationException("Combat turn setup no longer exposes its player.");
        var resolver = AccessTools.Method(typeof(CustomRunBaseHandSizePatch), nameof(ResolveBaseHandSize));
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (!instruction.LoadsConstant(5))
            {
                yield return instruction;
                continue;
            }

            replacements++;
            yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            yield return new CodeInstruction(OpCodes.Ldfld, playerField);
            yield return new CodeInstruction(OpCodes.Call, resolver);
        }
        if (replacements != 1)
            throw new InvalidOperationException($"Base hand draw changed: expected one default value, found {replacements}.");
    }

    private static int ResolveBaseHandSize(Player player)
    {
        if (player.RunState is { } runState)
        {
            var configured = CustomRunParameterValuesStore.From(runState.Modifiers).BaseHandSize;
            if (configured >= 0) return configured;
        }
        return 5;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
internal static class CustomRunEnemyHealthPatch
{
    private static readonly MethodInfo SetMaxHp = AccessTools.Method(typeof(Creature), "set_MaxHp")!;
    private static readonly MethodInfo SetCurrentHp = AccessTools.Method(typeof(Creature), "set_CurrentHp")!;

    [HarmonyPostfix]
    private static void Postfix(Creature __instance)
    {
        if (!__instance.IsMonster || __instance.CombatState is not { } combatState) return;
        var percent = CustomRunParameterValuesStore.From(combatState.Modifiers).EnemyHpPercent;
        if (percent == 100) return;

        var maxHp = Math.Max(1, (int)Math.Round(__instance.MaxHp * percent / 100d, MidpointRounding.AwayFromZero));
        var currentHp = __instance.CurrentHp == 0
            ? 0
            : Math.Max(1, (int)Math.Round(__instance.CurrentHp * percent / 100d, MidpointRounding.AwayFromZero));
        SetMaxHp.Invoke(__instance, [maxHp]);
        SetCurrentHp.Invoke(__instance, [Math.Min(maxHp, currentHp)]);
    }
}

[HarmonyPatch]
internal static class CustomRunEnemyDamagePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(Hook), "ModifyDamage")
        ?? throw new MissingMethodException(typeof(Hook).FullName, "ModifyDamage");

    [HarmonyPostfix]
    internal static void Postfix(IRunState runState, Creature? dealer, Creature? target, ref decimal __result)
    {
        // Event HP loss and other source-less damage have no dealer. Card damage previews have no
        // target, and Osty is a monster on the player's side, so check the dealer's own side.
        if (dealer is not { IsMonster: true, Side: CombatSide.Enemy } || target?.Side == CombatSide.Enemy) return;
        var percent = CustomRunParameterValuesStore.From(runState.Modifiers).EnemyDamagePercent;
        if (percent != 100) __result *= percent / 100m;
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.CreateMap))]
internal static class CustomRunActMapPatch
{
    private sealed class FloorOverride { internal int Count; }
    private static readonly ConditionalWeakTable<ActModel, FloorOverride> FloorOverrides = new();
    private static readonly MethodInfo SetActs = AccessTools.Method(typeof(RunState), "set_Acts")!;

    [HarmonyPrefix]
    private static void Prefix(RunState __0) => Configure(__0);

    internal static void Configure(RunState runState)
    {
        var floors = CustomRunParameterValuesStore.From(runState.Modifiers).FloorsPerAct;
        foreach (var original in runState.Acts.ToArray())
        {
            if (floors < 0)
            {
                FloorOverrides.Remove(original);
                continue;
            }
            var act = MakeMutableForRun(original, runState);
            FloorOverrides.Remove(act);
            FloorOverrides.Add(act, new FloorOverride { Count = floors });
        }
    }

    private static ActModel MakeMutableForRun(ActModel act, RunState runState)
    {
        if (!ReferenceEquals(act, act.CanonicalInstance)) return act;

        var acts = runState.Acts.ToArray();
        var index = Array.FindIndex(acts, candidate => ReferenceEquals(candidate, act));
        if (index < 0) return act.ToMutable();

        var mutable = act.ToMutable();
        acts[index] = mutable;
        SetActs.Invoke(runState, [acts]);
        return mutable;
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.GetNumberOfRooms))]
    private static class FloorCountPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ActModel __instance, ref int __result)
        {
            if (FloorOverrides.TryGetValue(__instance, out var floorOverride))
                // The native map and encounter pools use room count. Floors also include the Ancient and boss.
                __result = floorOverride.Count - 2;
        }
    }
}
