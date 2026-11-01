using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Smoke")]

namespace UltimateCustomRun;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string HarmonyId = "vertopolka.UltimateCustomRun";

    public static void Initialize()
    {
        ApplyPatches();
        GD.Print("[Ultimate Custom Run] Loaded: modifier groups and value sliders, custom run parameters, Neow!!, Headstart, ???, Double Trouble, Specialized variants, All Star – Draft, Friendship variants, Rich Loot, Card Swarm, Ultimate Starter, Super Draft, Handpicked, Must Have, Speedrun, Dill and 20/20.");
    }

    // Game startup discovers ModifierModel subclasses from loaded mod assemblies.
    // Registering them here would construct them again during ModelDb.Init.
    internal static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(ModEntry).Assembly);
}
