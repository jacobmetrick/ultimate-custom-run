using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;
using UltimateCustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using System.Reflection.Emit;
using Friendship = UltimateCustomRun.Friendship;

internal static class Program
{
    private static void Main(string[] args)
    {
        var dataPath = Path.Combine(args.Length > 0 ? args[0]
            : @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2", "data_sts2_windows_x86_64");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(dataPath, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run(args.Contains("--ascension-only"), args.Contains("--boss-chain-only"), args.Contains("--reward-sliders-only"), args.Contains("--mystery-only"), args.Contains("--twenty-twenty-only"), args.Contains("--draft-only"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(bool ascensionOnly, bool bossChainOnly, bool rewardSlidersOnly, bool mysteryOnly, bool twentyTwentyOnly, bool draftOnly)
    {
        var assembly = typeof(ModEntry).Assembly;
        Check(assembly.GetName().Name == "UltimateCustomRun" && typeof(ModEntry).Namespace == "UltimateCustomRun" &&
            ModEntry.HarmonyId == "vertopolka.UltimateCustomRun", "Assembly, namespace and Harmony identity are UltimateCustomRun");
        var initializer = typeof(ModEntry).GetCustomAttribute<ModInitializerAttribute>();
        Check(initializer?.initializerMethod == nameof(ModEntry.Initialize), "Game loader can find Initialize");

        // Real Harmony patching against the installed game, outside the Godot UI.
        var harmony = new Harmony(ModEntry.HarmonyId);
        try
        {
            ModEntry.ApplyPatches();
            Check(!ModelDb.Contains(typeof(NeowStarterChoice)),
                "Mod initializer leaves content registration to game startup");
            var target = typeof(Specialized).GetMethod(nameof(Specialized.GenerateNeowOption))!;
            var patched = Harmony.GetPatchInfo(target);
            Check(patched == null || !patched.Owners.Contains(ModEntry.HarmonyId),
                "Original Specialized is not patched");
            Check(NativeModifierCountPatch.Targets.All(target => Harmony.GetPatchInfo(
                AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)))?.Owners.Contains(ModEntry.HarmonyId) == true),
                "All six native modifier count state machines are patched");

            // Avoid starting a run or constructing native Godot objects.
            var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
            var specialized = (Specialized)RuntimeHelpers.GetUninitializedObject(typeof(Specialized));
            var vanilla = specialized.GenerateNeowOption(neow);
            Check(vanilla.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Uninitialized event retains vanilla callback");

            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
            var unchanged = specialized.GenerateNeowOption(neow);
            Check(unchanged.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Original Specialized retains vanilla random callback");
            var pickAny = (SpecializedPickAny)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedPickAny));
            var draft = (SpecializedDraft)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedDraft));
            Check(pickAny.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly &&
                draft.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly,
                "Both new modifiers provide their own Neow buttons");

            TestNeowModifier();
            TestPresetApplication();
            if (draftOnly)
            {
                TestSpecializedVariants();
                TestModifierValues();
                TestSuperModifiers();
                TestTwentyTwenty();
                Console.WriteLine("PASS: managed draft checks. Flight animation and transition timing require an in-game playtest.");
                return;
            }
            if (twentyTwentyOnly)
            {
                TestTwentyTwenty();
                Console.WriteLine("PASS: managed 20/20 checks. Relic acquisition and campfire visuals require an in-game playtest.");
                return;
            }
            if (mysteryOnly)
            {
                TestMysteryEvents();
                Console.WriteLine("PASS: managed mystery event regression checks. Live transitions require an in-game playtest.");
                return;
            }
            if (rewardSlidersOnly)
            {
                TestModifierValues();
                TestRewardSliders();
                Console.WriteLine("PASS: managed reward slider checks. Slider and relic layout visuals require an in-game playtest.");
                return;
            }
            if (bossChainOnly)
            {
                TestDoubleTrouble();
                Console.WriteLine("PASS: managed boss chain integration checks. Map visuals require an in-game playtest.");
                return;
            }
            if (ascensionOnly)
            {
                TestAscensionModifiers();
                Console.WriteLine("PASS: managed ascension integration checks. Top-bar visuals require an in-game playtest.");
                return;
            }
            TestGroups();
            TestSpecializedVariants();
            TestModifierValues();
            TestRewardSliders();
            TestRadioChains();
            TestColorlessCards();
            TestAscensionModifiers();
            TestDailyIsolation();
            TestSuperModifiers();
            TestSealedSliders();
            TestSpeedrun();
            TestPresetManagement();
            TestHandpicked();
            TestUltimateStarter();
            TestDill();
            TestHeadstart();
            TestTwentyTwenty();
            TestMysteryEvents();
            TestDoubleTrouble();
            TestCustomRunParameters();
            TestCustomRunFloors();
        }
        finally
        {
            harmony.UnpatchAll(ModEntry.HarmonyId);
        }
        Console.WriteLine("PASS: managed integration smoke checks. UI/deck acquisition require an in-game playtest.");
    }

    private static void TestGroups()
    {
        Check(ModifierGroups.Sections.Select(s => s.Title).SequenceEqual(
            new[] { "Improved Start", "Run Parameters", "Modifiers", "Card Pool", "Ascension", "Negatives", "Disabled" }), "Group titles and order match the requested layout");
        var negatives = new HashSet<Type> { typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents),
            typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal) };
        foreach (var type in new[] { typeof(NeowStarterChoice), typeof(Specialized), typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(Draft),
            typeof(SealedDeck), typeof(Insanity), typeof(AllStar), typeof(AllStarDraft) })
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.ImprovedStart,
                type.Name + " belongs to Improved Start");
        Check(ModifierGroups.Classify(ModelDb.Modifier<CharacterCards>(), negatives) == ModifierGroup.CardPool,
            "Character card pools belong to Card Pool");
        foreach (var type in negatives)
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.Negatives,
                type.Name + " belongs to Negatives");
        Check(ModifierGroups.Classify(ModelDb.Modifier<Hoarder>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Vintage>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Flight>(), negatives) == ModifierGroup.Modifiers,
            "Other modifiers stay in Modifiers");
    }

    private static void TestNeowModifier()
    {
        ModelDb.Init([typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized),
            typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards), typeof(NeowStarterChoice),
            typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(AllStarDraft), typeof(ColorlessCards),
            typeof(TwentyTwenty), typeof(TwentyTwentyDraft), typeof(TwentyTwentyAny),
            typeof(MegaCrit.Sts2.Core.Models.Enchantments.Clone), typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth), typeof(PommelStrike),
            typeof(Friendship), typeof(FriendshipDraft), typeof(RichLoot), typeof(CardSwarm), typeof(CustomRunParameters),
            typeof(SuperDraft), typeof(Handpicked), typeof(MustHave), typeof(Speedrun), typeof(UltimateStarter), typeof(Dill), typeof(Headstart), typeof(MysteryEvents), typeof(DoubleTrouble), typeof(CampfiresBetweenBosses),
            .. typeof(ActModel).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ActModel)) && !type.IsAbstract),
            typeof(MegaCrit.Sts2.Core.Models.Encounters.VantomBoss),
            typeof(MegaCrit.Sts2.Core.Models.Encounters.CeremonialBeastBoss),
            typeof(MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss),
            typeof(UltimateStrike), typeof(UltimateDefend),
            typeof(StrikeIronclad), typeof(DefendIronclad), typeof(Bash),
            typeof(StrikeSilent), typeof(DefendSilent), typeof(Neutralize), typeof(Survivor),
            typeof(StrikeRegent), typeof(DefendRegent), typeof(FallingStar), typeof(Venerate),
            typeof(StrikeNecrobinder), typeof(DefendNecrobinder), typeof(Bodyguard), typeof(Unleash),
            typeof(StrikeDefect), typeof(DefendDefect), typeof(Zap), typeof(Dualcast),
            typeof(MegaCrit.Sts2.Core.Models.Relics.DingyRug),
            typeof(MegaCrit.Sts2.Core.Models.Relics.Mango), typeof(MegaCrit.Sts2.Core.Models.Relics.JewelryBox),
            typeof(MegaCrit.Sts2.Core.Models.Relics.BurningBlood), typeof(MegaCrit.Sts2.Core.Models.Relics.Circlet),
            typeof(MegaCrit.Sts2.Core.Models.Relics.DeprecatedRelic),
            typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents), typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad), typeof(MegaCrit.Sts2.Core.Models.Characters.Silent),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Regent), typeof(MegaCrit.Sts2.Core.Models.Characters.Necrobinder), typeof(MegaCrit.Sts2.Core.Models.Characters.Defect),
            typeof(MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool), typeof(Neow),
            typeof(TabletOfTruth), typeof(AromaOfChaos), typeof(SunkenStatue), typeof(DenseVegetation), typeof(UnrestSite),
            typeof(SwarmingElites), typeof(WearyTraveler), typeof(Poverty), typeof(TightBelt), typeof(UltimateCustomRun.AscendersBane),
            typeof(Inflation), typeof(Scarcity), typeof(ToughEnemies), typeof(DeadlyEnemies), typeof(DoubleBoss)]);
        Check(ModelDb.All.Count(model => model is NeowStarterChoice) == 1,
            "Game startup constructs Neow!! once without DuplicateModelException");
        // Fixture for model IDs without invoking the engine-dependent startup logger.
        var cacheType = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache);
        var ids = ModelDb.All.Select(m => ModelDb.GetId(m.GetType())).ToArray();
        var categories = (Dictionary<string, int>)AccessTools.Field(cacheType, "_categoryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Category).Distinct()) categories[id] = categories.Count;
        var entries = (Dictionary<string, int>)AccessTools.Field(cacheType, "_entryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Entry).Distinct()) entries[id] = entries.Count;
        entries[ModelDb.GetId(typeof(TestNeow)).Entry] = entries.Count;
        var categoryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToCategoryNameMap").GetValue(null)!;
        categoryNames.Clear();
        categoryNames.AddRange(categories.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        var entryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToEntryNameMap").GetValue(null)!;
        entryNames.Clear();
        entryNames.AddRange(entries.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        AccessTools.Property(cacheType, "CategoryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(categoryNames.Count)));
        AccessTools.Property(cacheType, "EntryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(entryNames.Count)));
        AccessTools.Field(cacheType, "_initialized").SetValue(null, true);
        foreach (var type in ModelDb.All.Select(model => model.GetType()).Distinct())
            AccessTools.Method(cacheType, "CachePropertiesForType").Invoke(null, [type, null, null]);
        AccessTools.Property(cacheType, "PropertyIdBitSize").SetValue(null,
            (int)Math.Ceiling(Math.Log2(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.MaxPropertyId + 1)));
        ModelDb.InitIds();
        Check(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers).Count(m => m is NeowStarterChoice) == 1,
            "Neow!! is registered once in the modifier list");
        var modifier = ModelDb.Modifier<NeowStarterChoice>();
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is NeowStarterChoice,
            "New modifier round-trips through game save serialization");
        Check(NeowStarterChoice.DisplayTitle == "Neow!!" &&
            NeowStarterChoice.DisplayDescription == "Brings back starter Neow relic choice",
            "Requested title and description are exact");

        var test = new TestNeow();
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        IReadOnlyList<ModifierModel> modifiers = [modifier, ModelDb.Modifier<Specialized>()];
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, modifiers);
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(test, player);
        AccessTools.Method(typeof(AbstractModel), "InitId").Invoke(test, [ModelDb.GetId<Neow>()]);
        Check(NeowRelicChoice.NeedsRelicChoices(test), "Marker enables starter relic choices");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Initial modifier chain remains enabled");
        AccessTools.Method(typeof(EventModel), "SetEventFinished").Invoke(test,
            [new LocString("events", test.Id.Entry + ".pages.DONE.description")]);
        Check(test.OptionsShown?.Count == 3 && !test.IsFinished,
            "Modifier-chain completion shows three relic offers instead of ending event");
        Check(!NeowRelicChoice.NeedsRelicChoices(test), "Relic choice is restored only once");
        Check(ReferenceEquals(run.Modifiers, modifiers) && run.Modifiers.Count == 2,
            "Restoring relics does not remove or replace run modifiers");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Temporary vanilla-generation scope is cleared");

        var failing = new TestNeow();
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(failing, player);
        failing.FailGeneration = true;
        try { NeowRelicChoice.GenerateRelicChoices(failing); throw new Exception("Expected generation failure"); }
        catch (TargetInvocationException) { }
        Check(NeowRelicChoice.NeedsRelicChoices(failing) && NeowRelicChoice.ModifierCount(2, failing) == 2,
            "Failed generation resets restoration state and scope");
    }

    private static void TestSpecializedVariants()
    {
        var pickAny = ModelDb.Modifier<SpecializedPickAny>();
        var draft = ModelDb.Modifier<SpecializedDraft>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is SpecializedPickAny) == 1 &&
            custom.Count(m => m is SpecializedDraft) == 1,
            "Specialized variants appear once in the same mod's modifier list");
        Check(ModifierModel.FromSerializable(pickAny.ToMutable().ToSerializable()) is SpecializedPickAny &&
            ModifierModel.FromSerializable(draft.ToMutable().ToSerializable()) is SpecializedDraft,
            "Both Specialized variants round-trip through game save serialization");
        Check(SpecializedExclusivityPatch.ShouldUntick(pickAny, draft) &&
            SpecializedExclusivityPatch.ShouldUntick(pickAny, ModelDb.Modifier<Specialized>()) &&
            SpecializedExclusivityPatch.ShouldUntick(draft, ModelDb.Modifier<Specialized>()),
            "Three Specialized modes are mutually exclusive in custom runs");
        var allStarDraft = ModelDb.Modifier<AllStarDraft>();
        Check(custom.Count(m => m is AllStarDraft) == 1 &&
            ModifierModel.FromSerializable(allStarDraft.ToMutable().ToSerializable()) is AllStarDraft,
            "All Star draft is registered once and survives save serialization");
        var order = custom.Select(m => m.GetType()).ToList();
        Check(order[order.IndexOf(typeof(AllStar)) + 1] == typeof(AllStarDraft),
            "All Star draft appears next to original All Star");
        Check(order[order.IndexOf(typeof(Specialized)) + 1] == typeof(SpecializedDraft) &&
            order[order.IndexOf(typeof(Specialized)) + 2] == typeof(SpecializedPickAny),
            "Specialized ordering remains normal, draft, any");
        var colorlessPool = ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>();
        var colorlessOptions = AllStarDraft.Options(colorlessPool);
        Check(AllStarDraft.RewardCount == 5 && colorlessOptions.CardPools.Single() == colorlessPool &&
            colorlessOptions.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications),
            "Five All Star draft rewards stay in the Colorless pool");
        Check(SpecializedExclusivityPatch.ShouldUntick(allStarDraft, ModelDb.Modifier<AllStar>()),
            "All Star and All Star draft are mutually exclusive in custom runs");
        Check(SpecializedDraft.DisplayDescription == "Choose [blue]1[/blue] card reward. Add [blue]5[/blue] copies yo your starting deck.",
            "Specialized draft uses requested wording and blue numbers");
        Check(SpecializedDraft.DisplayTitle == "Specialized - Draft" &&
            SpecializedPickAny.DisplayTitle == "Specialized - Pick Any" && AllStarDraft.DisplayTitle == "All Star - Draft",
            "Only variant titles use the requested hyphen");
        const string rowTemplate = "[{color}]{modifier_title}[/{color}]: {modifier_description}";
        var uiTable = new LocTable("main_menu_ui", new Dictionary<string, string> { ["CUSTOM_RUN_SCREEN.MODIFIER_LABEL"] = rowTemplate });
        AccessTools.Method(typeof(ModifierTextPatch), "Postfix").Invoke(null, ["main_menu_ui", uiTable]);
        Check(uiTable.GetRawText("CUSTOM_RUN_SCREEN.MODIFIER_LABEL") == rowTemplate,
            "Native colon between modifier title and description remains unchanged");
        var readyPatches = Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCustomRunModifiersList), "_Ready"));
        Check(readyPatches?.Transpilers.Any(p => p.owner == ModEntry.HarmonyId) == true,
            "Custom modifiers are injected at the actual UI list creation call site");
        Check(SpecializedDraftReward.OfferCount == 3 && SpecializedCardChoices.Copies == 5,
            "Draft offers three cards and acquisition grants five copies");
        var cardType = typeof(CardModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type));
        var first = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var second = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var poolType = typeof(CardPoolModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardPoolModel).IsAssignableFrom(type));
        var pool = (CardPoolModel)RuntimeHelpers.GetUninitializedObject(poolType);
        Check(SpecializedDraftReward.Options(pool).RarityOdds == CardRarityOddsType.Uniform,
            "Draft uses all-rarity uniform reward generation");
        var offers = new MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult[] { new(first), new(second) };
        Check(ReferenceEquals(SpecializedDraftReward.ResolveSelection(offers, 1), second),
            "Draft grants the selected reward rather than the first offer");
        foreach (var index in new int?[] { null, -1, 2 })
        {
            try { SpecializedDraftReward.ResolveSelection(offers, index); throw new Exception("Expected invalid selection rejection"); }
            catch (InvalidOperationException) { }
        }
        Check(true, "Starting drafts reject skipped and invalid selections");
    }

    private static void TestModifierValues()
    {
        var cases = new (ModifierModel Model, int Min, int Max, int Step, int Default)[]
        {
            (ModelDb.Modifier<Specialized>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedPickAny>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStar>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStarDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Friendship>(), 1, 10, 1, 5),
            (ModelDb.Modifier<FriendshipDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Draft>(), 5, 20, 5, 10),
            (ModelDb.Modifier<SealedDeck>(), 5, 25, 5, 10),
            (ModelDb.Modifier<Insanity>(), 5, 60, 5, 30),
            (ModelDb.Modifier<Handpicked>(), 5, 60, 5, 10),
            (ModelDb.Modifier<Hoarder>(), 1, 5, 1, 2),
            (ModelDb.Modifier<Midas>(), 150, 300, 5, 200),
            (ModelDb.Modifier<RichLoot>(), 1, 3, 1, 1),
            (ModelDb.Modifier<CardSwarm>(), 1, 3, 1, 1)
        };
        foreach (var (canonical, min, max, step, defaultValue) in cases)
        {
            var spec = ModifierValues.For(canonical)!;
            Check((spec.Min, spec.Max, spec.Step, spec.Default) == (min, max, step, defaultValue),
                canonical.GetType().Name + " has the requested range, step and vanilla default");
            var model = canonical.ToMutable();
            Check(ModifierValues.Get(model) == defaultValue, "Unconfigured modifier preserves default: " + model.GetType().Name);
            for (var value = min; value <= max; value += step)
            {
                ModifierValues.Set(model, value);
                var serializable = model.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                serializable.Props!.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                var props = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SavedProperties>();
                var fromNetwork = ModifierModel.FromSerializable(new MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier
                    { Id = serializable.Id, Props = props });
                Check(ModifierValues.Get(ModifierModel.FromSerializable(serializable)) == value &&
                    ModifierValues.Get(fromNetwork) == value &&
                    ModifierValues.Get((ModifierModel)model.MutableClone()) == value,
                    $"{model.GetType().Name} value {value} survives save, network properties and clone");
            }
            ModifierValues.Set(model, int.MinValue);
            Check(ModifierValues.Get(model) == min, "Lower bound enforced: " + model.GetType().Name);
            ModifierValues.Set(model, int.MaxValue);
            Check(ModifierValues.Get(model) == max, "Upper bound enforced: " + model.GetType().Name);
            if (step > 1)
            {
                ModifierValues.Set(model, min + 3);
                Check(ModifierValues.Get(model) == min + step, "Off-step values normalized: " + model.GetType().Name);
            }
            Check(ModifierValues.Get(canonical) == defaultValue, "Configuration cannot leak to canonical/daily model: " + model.GetType().Name);
        }
        var midas = ModelDb.Modifier<Midas>().ToMutable();
        ModifierValues.Set(midas, 155);
        Check(ModifierValues.ScaleGold(20, midas) == 31 && ModifierValues.ScaleGold(21, midas) == 32,
            "Midas uses the selected percent with integer gold rounding");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var insanity = ModelDb.Modifier<Insanity>().ToMutable();
        ModifierValues.Set(insanity, 60);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { insanity });
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        Check(ModifierValues.ForPlayer<Insanity>(player) == 60, "Native gameplay resolves the run's selected count");
        var rewards = new List<MegaCrit.Sts2.Core.Rewards.Reward>
            { new MegaCrit.Sts2.Core.Rewards.GoldReward(20, player), new MegaCrit.Sts2.Core.Rewards.GoldReward(21, player) };
        ((Midas)midas).TryModifyRewardsLate(player, rewards, null);
        Check(rewards.Cast<MegaCrit.Sts2.Core.Rewards.GoldReward>().Select(reward => reward.Amount).SequenceEqual(new[] { 31, 32 }),
            "Patched native Midas reward method applies 155 percent instead of its fixed multiplier");
        foreach (var target in NativeModifierCountPatch.Targets)
        {
            var instructions = PatchProcessor.GetCurrentInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)));
            Check(instructions.Count(instruction => instruction.opcode == System.Reflection.Emit.OpCodes.Call &&
                instruction.operand is MethodInfo method && method.DeclaringType == typeof(ModifierValues) &&
                method.Name is nameof(ModifierValues.ForPlayer) or nameof(ModifierValues.Get)) == 1,
                target.Type.Name + " gameplay replaces exactly one count with the configured resolver");
        }
    }

    private static void TestRewardSliders()
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { player });
        var loot = ModelDb.Modifier<RichLoot>().ToMutable();
        var swarm = ModelDb.Modifier<CardSwarm>().ToMutable();
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new[] { loot, swarm });
        for (var count = 1; count <= 3; count++)
        {
            ModifierValues.Set(loot, count);
            ModifierValues.Set(swarm, count);
            Check(CardSwarm.OfferCount(player, 3) == 3 + count && CardSwarm.OfferCount(player, 5) == 5 + count,
                $"Card Swarm adds {count} cards to different base reward sizes");
            for (var players = 1; players <= 4; players++)
            {
                var relics = Enumerable.Repeat(MegaCrit.Sts2.Core.Factories.RelicFactory.FallbackRelic, players).ToList();
                var rng = new MegaCrit.Sts2.Core.Random.Rng(42u);
                RichLootChestPatch.Postfix(relics, run, new RelicGrabBag(), rng);
                Check(relics.Count == players + count, $"Rich Loot adds {count} relic choices to a chest with {players} base choices");
                var expectedRng = new MegaCrit.Sts2.Core.Random.Rng(42u);
                for (var i = 0; i < count; i++) MegaCrit.Sts2.Core.Factories.RelicFactory.RollRarity(expectedRng);
                Check(rng.NextFloat() == expectedRng.NextFloat(), "Extra relics use exactly one native rarity roll each");
            }
            var empty = new List<RelicModel>();
            RichLootChestPatch.Postfix(empty, run, new RelicGrabBag(), new MegaCrit.Sts2.Core.Random.Rng(42u));
            Check(empty.Count == 0, "Rich Loot preserves empty chests");
        }
        Check(RichLoot.DescriptionText(1) == RichLoot.DisplayDescription &&
            CardSwarm.DescriptionText(1) == CardSwarm.DisplayDescription,
            "Default reward descriptions preserve existing behavior");
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        var vanilla = new List<RelicModel> { MegaCrit.Sts2.Core.Factories.RelicFactory.FallbackRelic };
        RichLootChestPatch.Postfix(vanilla, run, new RelicGrabBag(), new MegaCrit.Sts2.Core.Random.Rng(42u));
        Check(vanilla.Count == 1 && CardSwarm.OfferCount(player, 3) == 3,
            "Runs without the reward modifiers retain their original counts");
        for (var count = 5; count <= 7; count++)
        {
            var positions = Enumerable.Range(0, count).Select(index => RichLootHolderPatch.PositionFor(index, count,
                new Godot.Vector2(-148, -180), new Godot.Vector2(12, 44), new Godot.Vector2(-388, -68))).ToArray();
            Check(positions.Distinct().Count() == count && positions.All(position => positions.All(other =>
                position == other || Math.Abs(position.X - other.X) >= 136 || Math.Abs(position.Y - other.Y) >= 136)),
                $"All {count} relic holders have distinct, non-overlapping positions");
        }
    }

    private static void TestColorlessCards()
    {
        var modifier = ModelDb.Modifier<ColorlessCards>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is ColorlessCards) == 1 &&
            ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.CardPool,
            "Colorless Cards appears once in Card Pool");
        Check(custom[0] is ColorlessCards, "Colorless relic is granted before starting card rewards");
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is ColorlessCards,
            "Colorless Cards survives save serialization");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var relics = (List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(player)!;
        // Create the native collection for this engine-free fixture.
        if (relics == null)
        {
            relics = [];
            AccessTools.Field(typeof(Player), "_relics").SetValue(player, relics);
        }
        relics.Add(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DingyRug>());
        var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
        modifier.GenerateNeowOption(neow)!().GetAwaiter().GetResult();
        Check(relics.Count == 1, "Existing Dingy Rug does not get granted twice");
        Check(typeof(ColorlessCards).GetMethod(nameof(ModifierModel.ModifyCardRewardCreationOptions))!.DeclaringType != typeof(ColorlessCards),
            "Colorless card rewards are handled by the native relic, not a duplicate modifier hook");
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(RelicModel), nameof(RelicModel.IsAllowed)));
        Check(patches == null || !patches.Owners.Contains(ModEntry.HarmonyId), "Relic availability is not patched");
        Check(!ColorlessCards.DisplayDescription.Contains("Dingy", StringComparison.OrdinalIgnoreCase),
            "Public description only describes colorless rewards");
    }

    private static void TestRadioChains()
    {
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var start = custom.Where(modifier => ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.ImprovedStart).ToArray();
        Check(start.Take(3).Select(modifier => modifier.GetType()).SequenceEqual(new[] { typeof(Draft), typeof(SealedDeck), typeof(Insanity) }),
            "Draft, Sealed Deck and Insanity are adjacent for their shared chain");
        var linked = Enumerable.Range(1, start.Length - 1).Where(index => LinkedModifierChains.AreLinked(start[index - 1], start[index])).ToArray();
        Check(linked.Length == 9, "Nine adjacent mutually exclusive pairs receive chains");
        Check(start[3] is Handpicked && LinkedModifierChains.AreLinked(start[2], start[3]), "Handpicked joins the deck replacement chain after Insanity");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Draft>(), ModelDb.Modifier<SealedDeck>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SealedDeck>(), ModelDb.Modifier<Insanity>()), "Native deck replacement choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Specialized>(), ModelDb.Modifier<SpecializedDraft>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SpecializedDraft>(), ModelDb.Modifier<SpecializedPickAny>()), "Specialized choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStar>(), ModelDb.Modifier<AllStarDraft>()), "All Star choices are linked");
        Check(!LinkedModifierChains.AreLinked(ModelDb.Modifier<Insanity>(), ModelDb.Modifier<Specialized>()) &&
            !LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStarDraft>(), ModelDb.Modifier<NeowStarterChoice>()), "Independent modifiers are not visually linked");
    }

    private static void TestAscensionModifiers()
    {
        var levels = Enum.GetValues<MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel>().Where(level => (int)level > 0).ToArray();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var effects = custom.OfType<AscensionModifier>().ToArray();
        Check(effects.Select(effect => effect.Level).SequenceEqual(levels), "All ten ascension effects appear once in level order");
        Check(ModifierListPatch.ForCustomRun(custom).OfType<AscensionModifier>().Count() == levels.Length,
            "Repeated custom enumeration does not duplicate ascension modifiers");
        foreach (var effect in effects)
        {
            Check(ModifierGroups.Classify(effect, new HashSet<Type>()) == ModifierGroup.Ascension && ModifierListPatch.IsCustomOnly(effect),
                effect.GetType().Name + " is custom-only and belongs to Ascension");
            var saved = effect.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var network = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>();
            var restored = ModifierModel.FromSerializable(network);
            Check(restored is AscensionModifier ascension && ascension.Level == effect.Level &&
                ModifierModel.FromSerializable(saved).GetType() == effect.GetType(),
                effect.GetType().Name + " survives save and network serialization");
            var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { restored });
            var manager = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0);
            IndependentAscensionPatch.Bind(manager, run);
            foreach (var level in levels)
                Check(manager.HasLevel(level) == (level == effect.Level), effect.GetType().Name + " activates only " + level);
        }
        var combined = new ModifierModel[] { ModelDb.Modifier<DoubleBoss>().ToMutable(), ModelDb.Modifier<TightBelt>().ToMutable(), ModelDb.Modifier<RichLoot>().ToMutable() };
        Check(AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.DoubleBoss) &&
            AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.TightBelt) &&
            !AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.Poverty), "Selected ascension effects combine without enabling intervening levels");
        Check(AscensionModifiers.WithoutAscensions(combined).Single() == combined[2], "Changing ascension removes all independent effects and preserves other modifiers");
        var original = combined.ToArray();
        Check(AscensionModifiersUi.Selected(combined).Select(effect => effect.Level).SequenceEqual(new[]
            { MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.TightBelt, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.DoubleBoss }),
            "Grouped ascension tooltip includes only enabled effects in native level order");
        Check(combined.SequenceEqual(original), "Grouping the top bar preserves every gameplay modifier and its order");
        Check(AscensionModifiersUi.Selected([combined[2]]).Length == 0 &&
            AscensionModifiersUi.Selected([combined[0], combined[0]]).Length == 1 &&
            AscensionModifiersUi.Selected(effects.Reverse()).Select(effect => effect.Level).SequenceEqual(levels),
            "Ascension summary handles zero, duplicate and all ten effects");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NTopBar), "Initialize"))?
            .Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(AscensionModifiersUi)) == true,
            "Top-bar initialization applies the ascension grouping patch");
        Check(AccessTools.Field(typeof(MegaCrit.sts2.Core.Nodes.TopBar.NTopBarModifier), "_modifier")?.FieldType == typeof(ModifierModel) &&
            AccessTools.Field(typeof(MegaCrit.sts2.Core.Nodes.TopBar.NTopBarModifier), "_hoverTip")?.FieldType == typeof(MegaCrit.Sts2.Core.HoverTips.HoverTip),
            "Installed game supports native modifier icons and grouped hover tips");
        var vanilla = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10);
        Check(levels.All(vanilla.HasLevel), "Unbound vanilla Ascension 10 still enables every level");
        foreach (var target in new[] { "OnModifiersListChanged", "AscensionChanged" })
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen), target))?.Owners.Contains(ModEntry.HarmonyId) == true,
                "Ascension/modifier selection synchronization is patched: " + target);
    }

    private static void TestDailyIsolation()
    {
        foreach (var property in new[] { nameof(ModelDb.GoodModifiers), nameof(ModelDb.BadModifiers), nameof(ModelDb.MutuallyExclusiveModifiers) })
        {
            var info = Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(ModelDb), property));
            Check(info == null || !info.Owners.Contains(ModEntry.HarmonyId), "Daily global pool is not patched: " + property);
        }
        Check(!ModelDb.GoodModifiers.Any(ModifierListPatch.IsCustomOnly), "Daily cannot roll any of our custom-only modifiers");
        Check(ModelDb.GoodModifiers.Select(m => m.GetType()).SequenceEqual(new[]
            { typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized), typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards) }),
            "Vanilla good-modifier count and order are preserved");
        string Signature(ulong seed) => string.Join("|", ModifierModel.Pick2Good1Bad(new MegaCrit.Sts2.Core.Random.Rng(seed), [])
            .Select(m => m is CharacterCards cards ? m.Id + "/" + cards.CharacterModel : m.Id.ToString()));
        var withMod = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
        var harmony = new Harmony(ModEntry.HarmonyId);
        harmony.UnpatchAll(ModEntry.HarmonyId);
        try
        {
            var vanilla = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
            Check(withMod.SequenceEqual(vanilla), "100 daily seeds select exactly the same modifiers with and without our patches");
        }
        finally { ModEntry.ApplyPatches(); }
    }

    private static void TestSuperModifiers()
    {
        var custom = ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers));
        foreach (var type in new[] { typeof(SuperDraft), typeof(MustHave) })
        {
            var model = custom.Single(modifier => modifier.GetType() == type);
            Check(ModifierListPatch.IsCustomOnly(model) && ModifierValues.For(model) == null,
                type.Name + " is custom-only, appears once and has no adjustable values");
            Check(ModifierModel.FromSerializable(model.ToSerializable()).GetType() == type,
                type.Name + " survives save serialization");
            var saved = model.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            Check(ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()).GetType() == type,
                type.Name + " survives multiplayer serialization");
        }
        Check(!ModelDb.Modifier<SuperDraft>().ClearsPlayerDeck, "Super Draft adds to the starting deck");
        for (var index = 0; index <= 12; index++)
        {
            var expected = 0.005 * Math.Pow(2, index);
            Check(SuperDraft.CurseRisk(index) == expected, "Curse risk doubles for offer " + (index + 1));
            var guaranteed = (int)Math.Floor(expected);
            Check(SuperDraft.CurseCount(expected, 0) == guaranteed + 1 &&
                SuperDraft.CurseCount(expected, 0.999) == guaranteed, "Whole and fractional Curse chances work at " + expected);
        }
        Check(SuperDraft.CurseCount(1.28, 0.279999) == 2 && SuperDraft.CurseCount(1.28, 0.28) == 1 &&
            SuperDraft.CurseCount(2.56, 0.55) == 3 && SuperDraft.CurseCount(2.56, 0.57) == 2,
            "128 percent and later offers grant guaranteed Curses plus a fractional roll");

        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run,
            new ModifierModel[] { ModelDb.Modifier<MustHave>().ToMutable(), ModelDb.Modifier<CardSwarm>().ToMutable() });
        var reward = (MegaCrit.Sts2.Core.Rewards.CardReward)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rewards.CardReward));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.Reward), "<Player>k__BackingField").SetValue(reward, player);
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "<CanSkip>k__BackingField").SetValue(reward, true);
        Check(!reward.CanSkip && MustHave.IsPending(reward), "Must Have forbids skipping an unclaimed card reward");
        var alternatives = new List<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative>
        {
            Alternative("Skip", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward),
            Alternative("REROLL", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.DoNothing),
            Alternative("Heal", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.EndSelectionAndCompleteReward)
        };
        MustHave.FilterAlternatives(alternatives);
        Check(alternatives.Single().OptionId == "REROLL", "Must Have preserves rerolls and rejects card substitutes");
        var set = (MegaCrit.Sts2.Core.Rewards.RewardsSet)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet), "<Rewards>k__BackingField").SetValue(set,
            new List<MegaCrit.Sts2.Core.Rewards.Reward> { reward, new MegaCrit.Sts2.Core.Rewards.GoldReward(20, player) });
        Check(MustHave.BlocksLeaving(set), "Unclaimed card rewards block leaving a mixed reward set");
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Rewards.Reward), "SuccessfullySelected").SetValue(reward, true);
        Check(!MustHave.BlocksLeaving(set), "Non-card rewards remain optional after taking the required card");
        MustHave.AllowPassing(reward);
        Check(reward.CanSkip && !MustHave.RequiresCard(reward), "Super Draft passing is exempt from Must Have");
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        Check(reward.CanSkip && !MustHave.BlocksLeaving(set), "Runs without Must Have retain optional card rewards");

        static MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative Alternative(
            string id, MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction action)
        {
            // The native constructor initializes Godot input StringNames; keep this fixture engine-free.
            var alternative = (MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative)
                RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative));
            AccessTools.Field(alternative.GetType(), "<OptionId>k__BackingField").SetValue(alternative, id);
            AccessTools.Property(alternative.GetType(), "AfterSelected").SetValue(alternative, action);
            return alternative;
        }
    }

    private static void TestSealedSliders()
    {
        var canonical = ModelDb.Modifier<SealedDeck>();
        Check(ModifierValues.Get(canonical) == 10 && ModifierValues.GetSealedPool(canonical) == 30 &&
            ModifierValues.SealedPoolSpec == new ModifierValues.Spec(10, 60, 5, 30, "offers"),
            "Sealed Deck defaults to 10 of 30; pool slider uses 10..60, step 5");
        for (var pool = 10; pool <= 60; pool += 5)
        {
            var model = canonical.ToMutable();
            ModifierValues.SetSealedPool(model, pool);
            Check(ModifierValues.For(model)!.Max == pool - 5, "Sealed Deck pick maximum is pool minus five: " + pool);
            for (var selected = 5; selected <= pool - 5; selected += 5)
            {
                ModifierValues.Set(model, selected);
                var saved = model.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                saved.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                    ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                    (ModifierModel)model.MutableClone() })
                    Check(ModifierValues.Get(restored) == selected && ModifierValues.GetSealedPool(restored) == pool,
                        $"Sealed Deck {selected} of {pool} survives save, network or clone");
            }
            ModifierValues.Set(model, int.MaxValue);
            Check(ModifierValues.Get(model) == pool - 5, "Pick count clamps to the current pool limit");
        }
        var shrinking = canonical.ToMutable();
        ModifierValues.SetSealedPool(shrinking, 60);
        ModifierValues.Set(shrinking, 55);
        ModifierValues.SetSealedPool(shrinking, 20);
        Check(ModifierValues.Get(shrinking) == 15, "Reducing the pool automatically clamps the selected card count");
        ModifierValues.SetSealedPool(shrinking, int.MinValue);
        Check(ModifierValues.GetSealedPool(shrinking) == 10 && ModifierValues.Get(shrinking) == 5,
            "Minimum pool leaves five selected cards");
        ModifierValues.SetSealedPool(shrinking, int.MaxValue);
        Check(ModifierValues.GetSealedPool(shrinking) == 60 && ModifierValues.Get(shrinking) == 5,
            "Increasing the pool preserves the current selected count and caps at sixty");
        ModifierValues.SetSealedPool(shrinking, 33);
        Check(ModifierValues.GetSealedPool(shrinking) == 35, "Pool values snap to step five");
        var oldSave = canonical.ToMutable();
        ModifierValues.Set(oldSave, 20);
        var savedOld = oldSave.ToSerializable();
        savedOld.Props!.ints!.RemoveAll(property => property.name == ModifierValues.SealedPoolKey);
        var oldRestored = ModifierModel.FromSerializable(savedOld);
        Check(ModifierValues.Get(oldRestored) == 20 && ModifierValues.GetSealedPool(oldRestored) == 30,
            "Older saves without a pool property retain the default pool of thirty");
        Check(ModifierValues.SealedDescriptionText("Choose [blue]10[/blue] from [blue]30[/blue].", 30, 60)
            == "Choose [blue]30[/blue] from [blue]60[/blue].", "Description substitutes both values without confusing equal numbers");
        var stateMachine = AccessTools.AsyncMoveNext(AccessTools.Method(typeof(SealedDeck), "ChooseCards"));
        var instructions = PatchProcessor.GetCurrentInstructions(stateMachine);
        Check(instructions.Count(instruction => instruction.operand is MethodInfo method &&
            method.DeclaringType == typeof(ModifierValues) && method.Name == nameof(ModifierValues.SealedPoolForPlayer)) == 1 &&
            instructions.Count(instruction => instruction.operand is MethodInfo method &&
            method.DeclaringType == typeof(ModifierValues) && method.Name == nameof(ModifierValues.FixedSealedPool)) == 1,
            "Native Sealed Deck resolves the configured pool once and prevents reward hooks changing its size");
        var preset = new ModifierPresetEntry { Id = canonical.Id.ToString(), Value = 55, SealedPoolSize = 60 };
        var restoredPreset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(preset))!;
        Check(restoredPreset.Value == 55 && restoredPreset.SealedPoolSize == 60,
            "Presets preserve both Sealed Deck sliders");
        Check(!ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers).Any(modifier => modifier.GetType().Name == "SuperSealed"),
            "Super Sealed is removed from the Custom Run menu");
        Check(ModifierValues.Get(canonical) == 10 && ModifierValues.GetSealedPool(canonical) == 30,
            "Custom Sealed Deck settings do not leak into canonical Daily defaults");
    }

    private static void TestSpeedrun()
    {
        var canonical = ModelDb.Modifier<Speedrun>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(10, 60, 5, 30, "minutes"),
            "Speedrun limit uses 10..60 minutes, step 5, default 30");
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.Negatives &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<Speedrun>().Count() == 1,
            "Speedrun appears once under Negatives and stays out of Daily pools");
        foreach (var limit in Enumerable.Range(0, 11).Select(index => 10 + index * 5))
        {
            Check(Speedrun.DueMinutes(limit * 60 - 1, limit) == 0 &&
                Speedrun.DueMinutes(limit * 60, limit) == 0 &&
                Speedrun.DueMinutes(limit * 60 + 59, limit) == 0 &&
                Speedrun.DueMinutes((limit + 1) * 60, limit) == 1 &&
                Speedrun.DueMinutes((limit + 4) * 60 + 59, limit) == 4,
                "Speedrun charges only full minutes after limit " + limit);
            var model = canonical.ToMutable();
            ModifierValues.Set(model, limit);
            ((Speedrun)model).SpeedrunPenaltyMinutes = 4;
            var saved = model.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)model.MutableClone() })
                Check(restored is Speedrun speedrun && speedrun.SpeedrunPenaltyMinutes == 4 &&
                    ModifierValues.Get(restored) == limit,
                    "Speedrun limit and paid penalties survive save, network and clone: " + limit);
        }
        var adjustable = canonical.ToMutable();
        ModifierValues.Set(adjustable, 33);
        Check(ModifierValues.Get(adjustable) == 35, "Speedrun limit snaps to five-minute steps");
        ModifierValues.Set(adjustable, int.MinValue);
        Check(ModifierValues.Get(adjustable) == 10, "Speedrun clamps its lower limit");
        ModifierValues.Set(adjustable, int.MaxValue);
        Check(ModifierValues.Get(adjustable) == 60 && ModifierValues.Get(canonical) == 30,
            "Speedrun clamps its upper limit without changing canonical defaults");
        var pulseWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        new NetSpeedrunPenaltyAction { Due = 7 }.Serialize(pulseWriter);
        var pulseReader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        pulseReader.Reset(pulseWriter.Buffer);
        var pulse = new NetSpeedrunPenaltyAction();
        pulse.Deserialize(pulseReader);
        Check(pulse.Due == 7, "Host penalty count survives network serialization");
        var penalties = (Speedrun)canonical.ToMutable();
        var lostHp = 0;
        Task LoseHp() { lostHp += Speedrun.HpLoss; return Task.CompletedTask; }
        penalties.ApplyMinutes(3, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 15 && penalties.SpeedrunPenaltyMinutes == 3,
            "Delayed timer applies five HP for each overdue minute");
        var resumed = (Speedrun)ModifierModel.FromSerializable(penalties.ToSerializable());
        resumed.ApplyMinutes(3, LoseHp, () => false).GetAwaiter().GetResult();
        resumed.ApplyMinutes(2, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 15, "Reloads and repeated or stale pulses do not charge paid minutes again");
        resumed.ApplyMinutes(5, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 25 && resumed.SpeedrunPenaltyMinutes == 5,
            "Resumed timer charges only newly elapsed minutes");
        resumed.ApplyMinutes(8, LoseHp, () => lostHp >= 30).GetAwaiter().GetResult();
        Check(lostHp == 30 && resumed.SpeedrunPenaltyMinutes == 6,
            "Pending penalties stop as soon as the run ends");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.TopBar.NRunTimer), "OnTimerTimeout"))
            ?.Owners.Contains(ModEntry.HarmonyId) == true, "Native one-second run timer is patched");
    }

    private static List<ModifierModel>? _presetNotification;
    private static readonly Dictionary<NTickbox, bool> HeadlessTicks = new(ReferenceEqualityComparer.Instance);

    private static void CapturePresetNotification(NCustomRunModifiersList list) =>
        _presetNotification = list.GetModifiersTickedOn().Select(model => (ModifierModel)model.MutableClone()).ToList();

    private static IEnumerable<CodeInstruction> HeadlessPresetSignal(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        for (var i = 0; i < code.Count; i++)
        {
            // Keep native selection logic and all mod patches; replace only the Godot signal boundary.
            if (i + 5 < code.Count && code[i].opcode == OpCodes.Ldarg_0 &&
                code[i + 1].operand is FieldInfo { Name: "ModifiersChanged" })
            {
                yield return code[i];
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Program), nameof(CapturePresetNotification)));
                i += 4;
            }
            else yield return code[i];
        }
    }

    private static bool HeadlessTickbox(NTickbox __instance, bool value)
    {
        HeadlessTicks[__instance] = value;
        return false;
    }

    private static bool HeadlessTickboxGet(NTickbox __instance, ref bool __result)
    {
        __result = HeadlessTicks.GetValueOrDefault(__instance);
        return false;
    }

    private static void TestPresetApplication()
    {
        var headless = new Harmony("ucr.smoke.preset-ui");
        headless.Patch(AccessTools.PropertySetter(typeof(NTickbox), nameof(NTickbox.IsTicked)),
            prefix: new HarmonyMethod(typeof(Program), nameof(HeadlessTickbox)));
        headless.Patch(AccessTools.PropertyGetter(typeof(NTickbox), nameof(NTickbox.IsTicked)),
            prefix: new HarmonyMethod(typeof(Program), nameof(HeadlessTickboxGet)));
        headless.Patch(AccessTools.Method(typeof(NCustomRunModifiersList), "SetTickedModifiers"),
            transpiler: new HarmonyMethod(typeof(Program), nameof(HeadlessPresetSignal)));
        try
        {
            var list = (NCustomRunModifiersList)RuntimeHelpers.GetUninitializedObject(typeof(NCustomRunModifiersList));
            AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").SetValue(list, MegaCrit.Sts2.Core.Entities.UI.MultiplayerUiMode.Singleplayer);
            NRunModifierTickbox Row(ModifierModel model)
            {
                var row = (NRunModifierTickbox)RuntimeHelpers.GetUninitializedObject(typeof(NRunModifierTickbox));
                AccessTools.Field(typeof(NRunModifierTickbox), "<Modifier>k__BackingField").SetValue(row, model);
                return row;
            }
            var normal = Row(ModelDb.Modifier<Specialized>().ToMutable());
            var draft = Row(ModelDb.Modifier<SpecializedDraft>().ToMutable());
            var any = Row(ModelDb.Modifier<SpecializedPickAny>().ToMutable());
            var sealedDeck = Row(ModelDb.Modifier<SealedDeck>().ToMutable());
            var parameters = Row(ModelDb.Modifier<CustomRunParameters>().ToMutable());
            var dill = Row(ModelDb.Modifier<Dill>().ToMutable());
            AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").SetValue(list,
                new List<NRunModifierTickbox> { normal, draft, any, sealedDeck, parameters, dill });
            var state = AccessTools.Method(typeof(ModifierVariantUi), "GetState").Invoke(null, [list, ModifierVariantUi.Family.Specialized])!;
            AccessTools.Field(state.GetType(), "ParentRow").SetValue(state, normal);
            AccessTools.Field(state.GetType(), "DraftRow").SetValue(state, draft);
            AccessTools.Field(state.GetType(), "PickAnyRow").SetValue(state, any);
            var choices = new[] { Row(normal.Modifier!), Row(draft.Modifier!), Row(any.Modifier!) };
            AccessTools.Field(state.GetType(), "Choices").SetValue(state, choices);
            AccessTools.Field(state.GetType(), "ChoicesReady").SetValue(state, true);
            choices[0].IsTicked = true;
            var savedDraft = ModelDb.Modifier<SpecializedDraft>().ToMutable();
            ModifierValues.Set(savedDraft, 8);
            var savedSealed = ModelDb.Modifier<SealedDeck>().ToMutable();
            ModifierValues.SetSealedPool(savedSealed, 60);
            ModifierValues.Set(savedSealed, 45);
            var savedParameters = (CustomRunParameters)ModelDb.Modifier<CustomRunParameters>().ToMutable();
            savedParameters.FloorsPerAct = 22;
            savedParameters.BaseHandSize = 7;
            savedParameters.BaseEnergy = 5;
            savedParameters.EnemyHpPercent = 150;
            savedParameters.EnemyDamagePercent = 175;
            savedParameters.PlayerHpPercent = 125;
            var savedDill = (Dill)ModelDb.Modifier<Dill>().ToMutable();
            ModifierValues.Set(savedDill, 17);
            savedDill.MaxHpPerFight = 4;
            var preset = System.Text.Json.JsonSerializer.Deserialize<ModifierPreset>(System.Text.Json.JsonSerializer.Serialize(
                ModifierPresetStore.Capture("Regression", [savedDraft, savedSealed, savedParameters, savedDill])))!;
            AccessTools.Method(typeof(ModifierPresetUi), "ApplyPreset").Invoke(null, [list, preset]);
            Check(list.GetModifiersTickedOn().Any(model => model is SpecializedDraft) && choices[1].IsTicked,
                "Loadout visually selects the saved Draft variant");
            Check(_presetNotification?.Any(model => model is SpecializedDraft && ModifierValues.Get(model) == 8) == true,
                "Loadout change notification delivers Draft and its saved slider value to the run");
            var deliveredSealed = _presetNotification!.OfType<SealedDeck>().Single();
            Check(ModifierValues.Get(deliveredSealed) == 45 && ModifierValues.GetSealedPool(deliveredSealed) == 60,
                "Loadout change notification delivers both saved Sealed Deck sliders");
            Check(CustomRunParameterValuesStore.From(_presetNotification) == CustomRunParameterValuesStore.Get(savedParameters),
                "Loadout JSON round-trip delivers all six saved Custom Run Parameters to the run");
            Check(_presetNotification!.OfType<Dill>().Single() is { MaxHpPerFight: 4 } deliveredDill &&
                ModifierValues.Get(deliveredDill) == 17, "Loadout JSON round-trip delivers both Dill sliders");
            foreach (var mode in new[] { any.Modifier!, normal.Modifier!, draft.Modifier! })
            {
                var selected = (ModifierModel)mode.MutableClone();
                ModifierValues.Set(selected, 3);
                var next = ModifierPresetStore.Capture("Switch mode", [selected]);
                AccessTools.Method(typeof(ModifierPresetUi), "ApplyPreset").Invoke(null, [list, next]);
                Check(_presetNotification is { Count: 1 } && _presetNotification[0].GetType() == mode.GetType() &&
                    ModifierValues.Get(_presetNotification[0]) == 3,
                    "Changing loadouts delivers the new mode and value immediately: " + mode.GetType().Name);
            }
            var legacy = System.Text.Json.JsonSerializer.Deserialize<ModifierPreset>(
                "{\"Name\":\"Legacy\",\"Modifiers\":[{\"Id\":\"" + parameters.Modifier!.Id + "\"}]}")!;
            AccessTools.Method(typeof(ModifierPresetUi), "ApplyPreset").Invoke(null, [list, legacy]);
            Check(CustomRunParameterValuesStore.From(_presetNotification) == CustomRunParameterValues.Default,
                "Legacy loadouts without parameters restore defaults instead of inheriting the previous loadout");
            AccessTools.Method(typeof(ModifierPresetUi), "ApplyPreset").Invoke(null, [list, ModifierPresetStore.Empty]);
            Check(_presetNotification is { Count: 0 } && list.GetModifiersTickedOn().Count == 0,
                "Empty loadout clears both the run notification and visual selection");
        }
        finally { headless.UnpatchAll(headless.Id); HeadlessTicks.Clear(); }
    }

    private static void TestPresetManagement()
    {
        var empty = ModifierPresetStore.Empty;
        Check(empty.Name == "Empty" && empty.Modifiers.Count == 0,
            "Built-in Empty preset selects no modifiers");
        empty.Modifiers.Add(new ModifierPresetEntry { Id = "MODIFIER.SPEEDRUN", Value = 30 });
        Check(ModifierPresetStore.Empty.Modifiers.Count == 0,
            "Empty preset is recreated without inheriting modified entries");
        Check(ModifierPresetStore.IsReservedName(" eMpTy ") && !ModifierPresetStore.IsReservedName("Empty deck"),
            "Built-in Empty name is reserved without blocking other preset names");
        var keep = new ModifierPreset
        {
            Name = "Keep", Modifiers = [new ModifierPresetEntry { Id = "MODIFIER.SEALED_DECK", Value = 10, SealedPoolSize = 30 }]
        };
        var presets = new List<ModifierPreset>
        {
            new() { Name = "Remove" }, keep, new() { Name = "Remove another" }
        };
        Check(ModifierPresetStore.Remove(presets, "rEmOvE") && presets.Count == 2 && ReferenceEquals(presets[0], keep),
            "Deleting matches the complete name case-insensitively and preserves other presets");
        var restored = System.Text.Json.JsonSerializer.Deserialize<List<ModifierPreset>>(
            System.Text.Json.JsonSerializer.Serialize(presets))!;
        Check(restored.Select(preset => preset.Name).SequenceEqual(new[] { "Keep", "Remove another" }) &&
            restored[0].Modifiers.Single() is { Value: 10, SealedPoolSize: 30 },
            "Deleted preset stays absent from serialized storage while other slider values survive");
        Check(!ModifierPresetStore.Remove(presets, "missing") && presets.Count == 2,
            "Deleting a missing preset leaves the list intact");
        var legacy = new List<ModifierPreset> { new() { Name = "Empty" } };
        Check(ModifierPresetStore.Remove(legacy, "Empty") && ModifierPresetStore.Empty.Modifiers.Count == 0,
            "Removing a legacy saved Empty does not affect the built-in preset");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NDropdown), "OpenDropdown"))
            ?.Owners.Contains(ModEntry.HarmonyId) == true,
            "Preset remove-button focus is restored after native dropdown navigation setup");
        var removeReady = typeof(ModifierPresetRemoveButton).GetMethod("_Ready", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Check(removeReady != null, "Preset remove button overrides the native Ready method that rejects subclasses");
        var readyCalls = PatchProcessor.GetCurrentInstructions(removeReady!).Select(instruction => instruction.operand).OfType<MethodInfo>().ToArray();
        Check(readyCalls.Any(method => method.Name == "ConnectSignals") && !readyCalls.Any(method => method.Name == "_Ready"),
            "Preset remove button initializes hover, mouse and controller signals without calling native Ready");
    }

    private static void TestHandpicked()
    {
        var model = ModelDb.Modifier<Handpicked>();
        var custom = ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers));
        Check(custom.Count(modifier => modifier is Handpicked) == 1 && ModifierListPatch.IsCustomOnly(model) &&
            ModifierGroups.Classify(model, new HashSet<Type>()) == ModifierGroup.ImprovedStart,
            "Handpicked is custom-only, appears once and belongs to Improved Start");
        Check(model.ClearsPlayerDeck && model.GenerateNeowOption((Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow))) == null,
            "Handpicked replaces the starting deck and needs an owning player for its Neow step");
        foreach (var other in new ModifierModel[] { ModelDb.Modifier<Draft>(), ModelDb.Modifier<SealedDeck>(), ModelDb.Modifier<Insanity>() })
            Check(SpecializedExclusivityPatch.ShouldUntick(model, other) && SpecializedExclusivityPatch.ShouldUntick(other, model),
                "Handpicked and " + other.GetType().Name + " are mutually exclusive");
        Check(!SpecializedExclusivityPatch.ShouldUntick(model, ModelDb.Modifier<Specialized>()) &&
            !SpecializedExclusivityPatch.ShouldUntick(model, ModelDb.Modifier<SuperDraft>()),
            "Handpicked stays compatible with modifiers that add cards to the deck");
        // ModifierValues.WithValue swaps the default count in [blue] tags for the slider value.
        Check(Handpicked.DisplayDescription.Contains($"[blue]{ModifierValues.For(model)!.Default}[/blue]"),
            "Handpicked description exposes its default card count for the slider text");

        var selection = new HandpickedSelection(3, 4);
        Check(selection.Add(2) && selection.Add(2) && selection.Add(0) && !selection.Add(1) && selection.IsComplete,
            "Selections take copies of one card and stop at the chosen count");
        Check(selection.CopiesOf(2) == 2 && selection.ToIndexes().SequenceEqual(new[] { 0, 2, 2 }),
            "Copies are synchronized as repeated candidate indexes");
        Check(selection.Remove(2) && !selection.Remove(1) && !selection.IsComplete && selection.Add(1) && selection.IsComplete,
            "Removing a copy reopens the selection and unpicked cards cannot be removed");
        HandpickedSelection.Validate([0, 0, 3], 3, 4);
        foreach (var invalid in new[] { new[] { 0, 1 }, new[] { 0, 1, 4 }, new[] { -1, 0, 1 } })
        {
            try { HandpickedSelection.Validate(invalid, 3, 4); throw new Exception("Expected invalid Handpicked selection rejection"); }
            catch (InvalidOperationException) { }
        }
        Check(true, "Synchronized selections must match the count and candidate range");

        var registered = ModelDb.All.OfType<CardModel>().Where(card => card.MultiplayerConstraint == CardMultiplayerConstraint.None).ToList();
        var pool = registered.Concat(registered).Reverse().ToList();
        var filtered = Handpicked.FilterCandidates(pool, multiplayer: false);
        Check(registered.Any(card => card.Rarity == CardRarity.Basic) && filtered.Count > 0 &&
            filtered.All(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare) &&
            filtered.DistinctBy(card => card.Id).Count() == filtered.Count &&
            filtered.Select(card => card.Rarity).SequenceEqual(filtered.Select(card => card.Rarity).Order()),
            "Candidates are unique Common, Uncommon and Rare cards in stable rarity order");
        var pools = new CardPoolModel[]
        {
            ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>(),
            (CardPoolModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool)),
            (CardPoolModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool)),
            (CardPoolModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool)),
            (CardPoolModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool)),
            (CardPoolModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool)),
        };
        Check(pools.OrderBy(Handpicked.PoolRank).Select(pool => pool.GetType().Name).SequenceEqual(new[]
            { "IroncladCardPool", "SilentCardPool", "DefectCardPool", "RegentCardPool", "NecrobinderCardPool", "ColorlessCardPool" }),
            "Handpicked lists pools in compendium order: characters by tab, then Colorless");
    }

    private static void TestUltimateStarter()
    {
        var model = ModelDb.Modifier<UltimateStarter>();
        Check(ModifierGroups.Classify(model, new HashSet<Type>()) == ModifierGroup.ImprovedStart &&
            ModifierListPatch.IsCustomOnly(model) && ModifierValues.For(model) == null &&
            !model.ClearsPlayerDeck && model.GenerateNeowOption(null!) == null &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<UltimateStarter>().Count() == 1,
            "Ultimate Starter appears once in Improved Start with a fixed count and no extra Neow prompt");
        var savedModifier = model.ToMutable().ToSerializable();
        var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        savedModifier.Serialize(writer);
        var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        reader.Reset(writer.Buffer);
        Check(ModifierModel.FromSerializable(savedModifier) is UltimateStarter &&
            ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()) is UltimateStarter,
            "Ultimate Starter survives save and multiplayer modifier serialization");

        // Use real native cards and piles without constructing engine-dependent player visuals.
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        var players = new List<Player>();
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
        var characters = new CharacterModel[]
        {
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Silent>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Regent>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Necrobinder>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Defect>()
        };
        foreach (var character in characters)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
            AccessTools.Field(typeof(Player), "<Deck>k__BackingField").SetValue(player, new CardPile(PileType.Deck));
            players.Add(player);
            foreach (var canonical in character.StartingDeck)
            {
                var card = canonical.ToMutable();
                run.AddCard(card, player);
                card.FloorAddedToDeck = 1;
                player.Deck.AddInternal(card, silent: true);
            }
        }
        var original = players.Select(player => player.Deck.Cards.ToArray()).ToArray();
        UltimateStarter.ReplaceStarters(run);
        Check(players.Select((player, index) => player.Deck.Cards.SequenceEqual(original[index])).All(unchanged => unchanged),
            "Starting decks stay untouched without Ultimate Starter selected");
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run,
            new ModifierModel[] { model.ToMutable(), ModelDb.Modifier<Hoarder>().ToMutable() });
        UltimateStarter.ReplaceStarters(run);
        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            var special = original[index].Where(card => !card.IsBasicStrikeOrDefend).ToArray();
            var basics = original[index].Where(card => card.IsBasicStrikeOrDefend).ToArray();
            var deck = player.Deck.Cards;
            Check(deck.OfType<UltimateStrike>().Count() == 3 && deck.OfType<UltimateDefend>().Count() == 3 &&
                deck.Count == special.Length + 6 && special.All(deck.Contains),
                characters[index].GetType().Name + " receives three of each Ultimate card and preserves special starters");
            Check(basics.All(card => card.HasBeenRemovedFromState && !run.ContainsCard(card)) &&
                deck.All(card => card.Owner == player && run.ContainsCard(card)) &&
                deck.Where(card => card is UltimateStrike or UltimateDefend).All(card => card.FloorAddedToDeck == 1 && !card.IsUpgraded),
                "Replaced cards leave the run; Ultimate cards have correct ownership and starting metadata");
            var restored = deck.Select(card => CardModel.FromSerializable(card.ToSerializable())).ToArray();
            Check(restored.OfType<UltimateStrike>().Count() == 3 && restored.OfType<UltimateDefend>().Count() == 3 &&
                restored.Select(card => card.Id).SequenceEqual(deck.Select(card => card.Id)),
                "Resulting starter deck round-trips through native card save serialization");
        }
        var completed = players.Select(player => player.Deck.Cards.ToArray()).ToArray();
        UltimateStarter.ReplaceStarters(run);
        model.ToMutable().OnRunLoaded(run);
        Check(players.Select((player, index) => player.Deck.Cards.SequenceEqual(completed[index])).All(unchanged => unchanged),
            "Repeated startup replacement and modifier reload never duplicate Ultimate starters");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(RunState), nameof(RunState.CreateForNewRun)))
            ?.Owners.Contains(ModEntry.HarmonyId) == true &&
            Harmony.GetPatchInfo(AccessTools.Method(typeof(RunState), nameof(RunState.FromSerializable)))
                ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(UltimateStarterNewRunPatch)) != true,
            "Ultimate Starter patches new-run creation only");
    }

    private static void TestTwentyTwenty()
    {
        TestTwentyTwentyRelic().GetAwaiter().GetResult();
        var models = new ModifierModel[] { ModelDb.Modifier<TwentyTwenty>(),
            ModelDb.Modifier<TwentyTwentyDraft>(), ModelDb.Modifier<TwentyTwentyAny>() };
        var custom = ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers));
        var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
        foreach (var model in models)
        {
            Check(custom.Count(item => item.GetType() == model.GetType()) == 1 &&
                ModifierListPatch.IsCustomOnly(model) &&
                ModifierGroups.Classify(model, new HashSet<Type>()) == ModifierGroup.ImprovedStart &&
                ModifierVariantUi.BelongsTo(model, ModifierVariantUi.Family.TwentyTwenty) &&
                ModifierValues.For(model) == new ModifierValues.Spec(1, 10, 1, 1, "cards") &&
                model.GenerateNeowOption(neow) != null && !model.ClearsPlayerDeck,
                "20/20 modes appear once in Improved Start, with a 1..10 slider and a Neow callback");
            foreach (var other in models.Where(other => other != model))
                Check(SpecializedExclusivityPatch.ShouldUntick(model, other), "20/20 modes are mutually exclusive");
            for (var count = 1; count <= 10; count++)
            {
                var modifier = (ModifierModel)model.ToMutable();
                ModifierValues.Set(modifier, count);
                var saved = modifier.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                saved.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                    ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                    (ModifierModel)modifier.MutableClone() })
                    Check(restored.GetType() == model.GetType() && ModifierValues.Get(restored) == count,
                        "20/20 mode/count survive save, network and clone");
            }
        }
        Check(ModifierVariantUi.IsParent(models[0]) && !ModifierVariantUi.IsHiddenVariant(models[0]) &&
            ModifierVariantUi.IsDraft(models[1], ModifierVariantUi.Family.TwentyTwenty) &&
            ModifierVariantUi.IsPickAny(models[2], ModifierVariantUi.Family.TwentyTwenty) &&
            models.Skip(1).All(ModifierVariantUi.IsHiddenVariant), "20/20 has one parent and two hidden variant rows");
        var preset = new ModifierPresetEntry { Id = models[2].Id.ToString(), Value = 10 };
        var restoredPreset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(preset))!;
        Check(restoredPreset.Id == models[2].Id.ToString() && restoredPreset.Value == 10,
            "20/20 Any ID and count persist in preset JSON");

        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(Player), "<Deck>k__BackingField").SetValue(player, new CardPile(PileType.Deck));
        var relics = new List<RelicModel>();
        AccessTools.Field(typeof(Player), "_relics").SetValue(player, relics);
        var options = new List<MegaCrit.Sts2.Core.Entities.RestSite.RestSiteOption>();
        Check(!TwentyTwentyCards.AddCloneOption(player, options), "No Clone option appears without Clone cards");
        var card = ModelDb.Card<PommelStrike>().ToMutable();
        Check(TwentyTwentyCards.CanOffer(card) && !TwentyTwentyCards.CanOffer(ModelDb.Card<StrikeIronclad>()),
            "20/20 offers reward cards and excludes basic starters");
        run.AddCard(card, player);
        TwentyTwentyCards.Enchant(card);
        player.Deck.AddInternal(card, silent: true);
        Check(card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Clone { Amount: 4 } &&
            !TwentyTwentyCards.CanOffer(card), "20/20 applies Pael's native Clone amount and excludes already enchanted cards");
        var copy = run.CloneCard(card);
        var restoredCard = CardModel.FromSerializable(card.ToSerializable());
        Check(copy.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Clone &&
            restoredCard.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Clone &&
            !ReferenceEquals(copy.Enchantment, card.Enchantment), "Clone enchantment survives native copying and card saves");
        Check(TwentyTwentyCards.AddCloneOption(player, options) &&
            options.Single() is MegaCrit.Sts2.Core.Entities.RestSite.CloneRestSiteOption &&
            !TwentyTwentyCards.AddCloneOption(player, options), "20/20 adds the native campfire cloning action exactly once");
        options.Clear();
        relics.Add(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth>());
        Check(!TwentyTwentyCards.AddCloneOption(player, options), "Pael's Growth supplies its own Clone action without duplicates");
        var pael = (Pael)RuntimeHelpers.GetUninitializedObject(typeof(Pael));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(pael, player);
        var growthOption = (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption));
        foreach (var modifier in models)
        {
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new[] { modifier });
            var choices = new List<EventOption> { growthOption, growthOption };
            TwentyTwentyAncientChoices.AddGrowthOption(choices, growthOption, pael);
            Check(choices.Count == 2, "Every 20/20 mode excludes Growth before Ancient RNG selection");
        }
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        var vanillaChoices = new List<EventOption>();
        TwentyTwentyAncientChoices.AddGrowthOption(vanillaChoices, growthOption, pael);
        Check(vanillaChoices.Single() == growthOption, "Ancient choices retain Growth without 20/20");
        Check(PatchProcessor.GetCurrentInstructions(AccessTools.Method(typeof(Pael), "GenerateInitialOptions"))
            .Any(instruction => instruction.operand is MethodInfo method && method.DeclaringType == typeof(TwentyTwentyAncientChoices)),
            "Native Pael generation uses the 20/20 candidate filter");
    }

    private static async Task TestTwentyTwentyRelic()
    {
        var relic = (MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth));
        var other = (MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsGrowth));
        Task result = null!;
        Check(TwentyTwentyRelic.Prefix(relic, ref result), "Normal Pael's Growth pickup keeps its enchantment selection");
        await TwentyTwentyRelic.WithoutInitialEnchantment(relic, async () =>
        {
            await Task.Yield();
            Check(TwentyTwentyRelic.Prefix(other, ref result), "20/20 suppression does not affect another relic or player");
            // An ownerless relic would fail in the native card selector if the prefix did not skip it.
            await relic.AfterObtained();
            Check(!TwentyTwentyRelic.Prefix(relic, ref result) && result.IsCompletedSuccessfully,
                "20/20 skips the native pickup enchantment across awaits");
        });
        Check(TwentyTwentyRelic.Prefix(relic, ref result), "20/20 clears suppression after successful acquisition");
        try
        {
            await TwentyTwentyRelic.WithoutInitialEnchantment(relic, () => Task.FromException(new InvalidOperationException("test")));
            throw new Exception("Expected acquisition failure");
        }
        catch (InvalidOperationException) { }
        Check(TwentyTwentyRelic.Prefix(relic, ref result), "20/20 clears suppression after failed acquisition");
    }

    private static void TestDill()
    {
        var canonical = ModelDb.Modifier<Dill>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(1, 20, 1, 1, "initial max HP") &&
            Dill.GrowthSpec == new ModifierValues.Spec(1, 5, 1, 2, "max HP per fight") &&
            canonical.MaxHpPerFight == 2,
            "Dill has independent initial-HP and per-fight sliders with the requested ranges and defaults");
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.Negatives &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<Dill>().Count() == 1,
            "Dill appears once under Negatives without entering Daily pools");
        for (var initial = 1; initial <= 20; initial++)
        for (var growth = 1; growth <= 5; growth++)
        {
            var modifier = (Dill)canonical.ToMutable();
            ModifierValues.Set(modifier, initial);
            modifier.MaxHpPerFight = growth;
            var saved = modifier.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)modifier.MutableClone() })
                Check(restored is Dill dill && ModifierValues.Get(dill) == initial && dill.MaxHpPerFight == growth,
                    $"Dill {initial} initial HP and {growth} growth survive save, network and clone");
            Check(ModifierValues.DillDescriptionText(Dill.DisplayDescription, initial, growth) ==
                $"Start the game with [blue]{initial}[/blue] max HP. Each fight increases max HP by [blue]{growth}[/blue].",
                "Dill description updates both independent values without substitution collisions");
        }
        var configurable = (Dill)canonical.ToMutable();
        ModifierValues.Set(configurable, int.MinValue);
        configurable.MaxHpPerFight = int.MinValue;
        Check(ModifierValues.Get(configurable) == 1 && configurable.MaxHpPerFight == 1,
            "Dill sliders clamp at their lower limits");
        ModifierValues.Set(configurable, int.MaxValue);
        configurable.MaxHpPerFight = int.MaxValue;
        Check(ModifierValues.Get(configurable) == 20 && configurable.MaxHpPerFight == 5 &&
            ModifierValues.Get(canonical) == 1 && canonical.MaxHpPerFight == 2,
            "Dill sliders clamp at their upper limits without changing canonical defaults");
        var missingGrowth = configurable.ToSerializable();
        missingGrowth.Props!.ints!.RemoveAll(property => property.name == nameof(Dill.MaxHpPerFight));
        Check(ModifierModel.FromSerializable(missingGrowth) is Dill { MaxHpPerFight: 2 },
            "Dill saves without a growth property retain its default of two");
        var preset = new ModifierPresetEntry { Id = canonical.Id.ToString(), Value = 17, MaxHpPerFight = 4 };
        var restoredPreset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(preset))!;
        Check(restoredPreset is { Value: 17, MaxHpPerFight: 4 }, "Presets preserve both Dill sliders");

        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var players = new List<Player>();
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
        var setMax = AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "MaxHp");
        var setCurrent = AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "CurrentHp");
        for (var index = 0; index < 3; index++)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var creature = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)RuntimeHelpers.GetUninitializedObject(
                typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
            AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, creature);
            AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
            setMax.Invoke(creature, [80]);
            setCurrent.Invoke(creature, [60]);
            players.Add(player);
        }
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        Dill.InitializeHealth(run);
        Check(players.All(player => player.Creature.MaxHp == 80 && player.Creature.CurrentHp == 60),
            "Runs without Dill preserve normal starting HP");
        foreach (var initial in new[] { 1, 7, 20 })
        {
            ModifierValues.Set(configurable, initial);
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { configurable });
            Dill.InitializeHealth(run);
            Check(players.All(player => player.Creature.MaxHp == initial && player.Creature.CurrentHp == initial),
                "Dill sets every co-op player's current and max HP to " + initial);
        }
        configurable.OnRunCreated(run);
        configurable.MaxHpPerFight = 4;
        setCurrent.Invoke(players[2].Creature, [0]);
        var gains = new List<(MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature, decimal Amount)>();
        configurable.GrowAfterVictory((creature, amount) =>
        {
            gains.Add((creature, amount));
            setMax.Invoke(creature, [creature.MaxHp + (int)amount]);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        Check(gains.Count == 2 && gains.All(gain => gain.Amount == 4) &&
            players.Take(2).All(player => player.Creature.MaxHp == 24) && players[2].Creature.MaxHp == 20,
            "Each victory grants the configured max HP once to surviving players without reviving dead teammates");
        configurable.OnRunLoaded(run);
        Check(players.Take(2).All(player => player.Creature.MaxHp == 24),
            "Loading Dill does not reset max HP earned from combat");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(RunManager), "InitializeNewRun"))
            ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(DillStartingHealthPatch)) == true &&
            Harmony.GetPatchInfo(AccessTools.Method(typeof(RunManager), "InitializeSavedRun"))
                ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(DillStartingHealthPatch)) != true &&
            typeof(Dill).GetMethod(nameof(AbstractModel.AfterCombatVictory))!.DeclaringType == typeof(Dill) &&
            typeof(Dill).GetMethod(nameof(AbstractModel.BeforeCombatStart))!.DeclaringType != typeof(Dill),
            "Dill starts at exact HP only on new runs and grows after victories rather than before fights");
    }

    private static void TestCustomRunParameters()
    {
        var canonical = ModelDb.Modifier<CustomRunParameters>();
        Check(typeof(CustomRunParameters).GetProperty("BossesPerAct") == null,
            "Boss count is removed from Custom Run Parameters");
        foreach (var parameter in Enum.GetValues<CustomRunParameter>())
            Check(typeof(CustomRunParameters).GetProperty(parameter.ToString())?.GetCustomAttribute<
                MegaCrit.Sts2.Core.Saves.Runs.SavedPropertyAttribute>() != null &&
                MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.GetNetIdForPropertyName(parameter.ToString()) >= 0,
                "Native startup discovers a replay/network ID for Custom Run parameter " + parameter);

        foreach (var handSize in new[] { -1, 0, 5, 10, 15 })
        {
            var modifier = (CustomRunParameters)canonical.ToMutable();
            modifier.FloorsPerAct = 20;
            modifier.BaseHandSize = handSize;
            modifier.BaseEnergy = 6;
            modifier.EnemyHpPercent = 150;
            modifier.EnemyDamagePercent = 200;
            modifier.PlayerHpPercent = 75;
            var expected = CustomRunParameterValuesStore.Get(modifier);
            Check(expected.BaseHandSize == Math.Clamp(handSize, -1, 10),
                "Base hand size preserves vanilla/zero and clamps to the engine's ten-card limit: " + handSize);
            var saved = modifier.ToSerializable();
            Check(saved.Props?.ints?.All(property => !property.name.StartsWith("CustomRunParameters.")) == true,
                "Custom Run serialization writes only registered property names");
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            // Same native modifier-list serialization used by SerializableRun in replays.
            writer.WriteList(new List<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier> { saved });
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var packet = reader.ReadList<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>().Single();
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved), ModifierModel.FromSerializable(packet),
                (ModifierModel)modifier.MutableClone() })
                Check(restored is CustomRunParameters parameters && CustomRunParameterValuesStore.Get(parameters) == expected,
                    "All six Custom Run parameters survive save, replay/network packet and clone: hand " + handSize);
        }
        var legacy = new MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier
        {
            Id = canonical.Id,
            Props = new MegaCrit.Sts2.Core.Saves.Runs.SavedProperties { ints = [
                new("CustomRunParameters.BossesPerAct", 2),
                new("BossesPerAct", 1),
                new("CustomRunParameters.FloorsPerAct", 24),
                new("CustomRunParameters.BaseHandSize", 15),
                new("CustomRunParameters.EnemyDamagePercent", 175)] }
        };
        var migrated = (CustomRunParameters)ModifierModel.FromSerializable(legacy);
        Check(migrated.FloorsPerAct == 24 && migrated.BaseHandSize == 10 &&
            migrated.EnemyDamagePercent == 175 && migrated.BaseEnergy == -1,
            "Legacy dotted-key saves retain their parameters, clamp oversized hands and preserve missing defaults");
        var migrationWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        migrated.ToSerializable().Serialize(migrationWriter);
        Check(migrationWriter.Buffer.Length > 0, "Migrated Custom Run saves can be serialized into replay packets");
    }

    private static void TestCustomRunFloors()
    {
        var canonicalActs = ModelDb.ActsByIndex.SelectMany(acts => acts).ToArray();
        var modifier = (CustomRunParameters)ModelDb.Modifier<CustomRunParameters>().ToMutable();
        foreach (var floors in Enumerable.Range(8, 23).Prepend(-1))
        foreach (var multiplayer in new[] { false, true })
        {
            modifier.FloorsPerAct = floors;
            var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { modifier });
            AccessTools.Field(typeof(RunState), "<Acts>k__BackingField").SetValue(run,
                floors < 0 ? canonicalActs.Select(act => act.ToMutable()).ToArray() : canonicalActs);
            AccessTools.Field(typeof(RunState), "_players").SetValue(run,
                Enumerable.Range(0, multiplayer ? 2 : 1).Select(_ =>
                    (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player))).ToList());
            AccessTools.Field(typeof(RunState), "<Rng>k__BackingField").SetValue(run,
                RuntimeHelpers.GetUninitializedObject(typeof(RunRngSet)));
            modifier.OnRunCreated(run);
            for (var index = 0; index < run.Acts.Count; index++)
            {
                AccessTools.Field(typeof(RunState), "_currentActIndex").SetValue(run, index);
                var act = run.Act;
                AccessTools.Field(typeof(ActModel), "_rooms").SetValue(act, new MegaCrit.Sts2.Core.Rooms.RoomSet
                {
                    Boss = ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.VantomBoss>(),
                    Ancient = ModelDb.Event<Neow>()
                });
                var expected = floors < 0 ? canonicalActs[index].GetNumberOfFloors(multiplayer) : floors;
                Check(act.GetNumberOfRooms(multiplayer) == expected - 2 && act.GetNumberOfFloors(multiplayer) == expected,
                    $"Room pools and floor count agree before generation: {act.Id}, {floors}, co-op={multiplayer}");
                var map = act.CreateMap(run, false);
                Check(map.GetRowCount() == expected - 1 && map.BossMapPoint.coord.row == expected - 1,
                    $"Native CreateMap uses configured floors including Ancient and boss: {act.Id}, {floors}, co-op={multiplayer}");
                var saved = MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(map);
                var restored = new MegaCrit.Sts2.Core.Map.SavedActMap(saved);
                Check(restored.GetRowCount() == map.GetRowCount() && restored.BossMapPoint.coord == map.BossMapPoint.coord,
                    "Configured map length survives native map serialization");
            }
            // Loaded act instances must regain their overrides before generating a later act or replacement map.
            AccessTools.Field(typeof(RunState), "<Acts>k__BackingField").SetValue(run,
                canonicalActs.Select(act => act.ToMutable()).ToArray());
            modifier.OnRunLoaded(run);
            Check(run.Acts.All(act => act.GetNumberOfFloors(multiplayer) ==
                (floors < 0 ? act.CanonicalInstance.GetNumberOfFloors(multiplayer) : floors)),
                "Loaded acts regain their configured floor counts");
            Check(canonicalActs.All(act => !act.IsMutable) && canonicalActs.All(act =>
                act.GetNumberOfFloors(multiplayer) == act.GetNumberOfRooms(multiplayer) + 2),
                "Configured runs leave canonical acts unchanged");
        }
    }

    private static void TestDoubleTrouble()
    {
        foreach (var lastNormalRow in new[] { 9, 14, 19, 29 })
        foreach (var halfHeights in new[] { new[] { 91.8f, 46f, 91.8f },
            new[] { 91.8f, 91.8f, 91.8f }, new[] { 91.8f, 46f, 91.8f, 46f, 91.8f } })
        foreach (var finalTop in new[] { -2013.75f, -2093.75f })
        {
            var distance = BossChainMapUiPatch.FitRowDistance(lastNormalRow, halfHeights, finalTop);
            var center = 768f - lastNormalRow * distance;
            var previousHalfHeight = 46f;
            foreach (var halfHeight in halfHeights)
            {
                center = BossChainMapUiPatch.NextCenterY(center, previousHalfHeight, halfHeight, distance);
                previousHalfHeight = halfHeight;
            }
            Check(distance > 0f && Math.Abs(center - halfHeights[^1] - finalTop) < 0.002f,
                "Boss chains preserve the native top margin across floor counts and campfires");
        }
        foreach (var rowDistance in new[] { 70f, 116.25f, 149.4643f, 232.5f })
        foreach (var halfHeights in new[] { new[] { 91.8f, 91.8f, 91.8f }, new[] { 91.8f, 46f, 91.8f, 46f, 91.8f } })
        {
            var center = 0f;
            var previousHalfHeight = 46f;
            foreach (var halfHeight in halfHeights)
            {
                var next = BossChainMapUiPatch.NextCenterY(center, previousHalfHeight, halfHeight, rowDistance);
                var visibleGap = (center - previousHalfHeight) - (next + halfHeight);
                Check(Math.Abs(visibleGap - Math.Max(0f, rowDistance - 92f)) < 0.001f,
                    "Every boss/rest gap matches ordinary map icon spacing without overlap: " + rowDistance);
                center = next;
                previousHalfHeight = halfHeight;
            }
        }
        foreach (var viewport in new[] { new Godot.Vector2(1280, 720), new Godot.Vector2(1920, 1080), new Godot.Vector2(2560, 1440) })
        foreach (var offset in new[] { new Godot.Vector2(-200, -1782), new Godot.Vector2(-80, -1900), new Godot.Vector2(-200, -2052) })
        {
            var anchor = new Godot.Vector2(0.5f, 0.5f);
            var attachedPosition = BossChainMapUiPatch.PositionForAnchor(offset, viewport, anchor);
            var preParentPosition = BossChainMapUiPatch.PositionForAnchor(offset, Godot.Vector2.Zero, anchor);
            Check(attachedPosition - viewport * anchor == offset && preParentPosition + viewport * anchor == attachedPosition,
                "Boss/rest positions retain the native centered offsets before and after parenting: " + viewport);
        }
        var canonical = ModelDb.Modifier<DoubleTrouble>();
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.Modifiers &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<DoubleTrouble>().Count() == 1,
            "Double Trouble appears once in Modifiers and stays out of Daily pools");
        Check(ModifierModel.FromSerializable(canonical.ToMutable().ToSerializable()) is DoubleTrouble,
            "Double Trouble survives native modifier save serialization");
        var bosses = new EncounterModel[] {
            ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.VantomBoss>(),
            ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.CeremonialBeastBoss>(),
            ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>() };
        MegaCrit.Sts2.Core.Models.Acts.Overgrowth NewAct(int boss)
        {
            var act = (MegaCrit.Sts2.Core.Models.Acts.Overgrowth)ModelDb.Act<MegaCrit.Sts2.Core.Models.Acts.Overgrowth>().ToMutable();
            AccessTools.Field(typeof(ActModel), "_allBossEncounters").SetValue(act, bosses);
            AccessTools.Field(typeof(ActModel), "_rooms").SetValue(act,
                new MegaCrit.Sts2.Core.Rooms.RoomSet { Boss = bosses[boss], Ancient = ModelDb.Event<Neow>() });
            return act;
        }
        for (var seed = 0; seed < 12; seed++)
        {
            var acts = new ActModel[] { NewAct(0), NewAct(1), NewAct(2) };
            acts[2].SetSecondBossEncounter(bosses[0]);
            DoubleTrouble.AddSecondBosses(acts, new MegaCrit.Sts2.Core.Random.Rng((uint)seed));
            Check(acts.All(act => act.HasSecondBoss && act.BossEncounter.Id != act.SecondBossEncounter!.Id) &&
                acts[2].SecondBossEncounter!.Id == bosses[0].Id,
                "Every act has distinct bosses and the A10 second boss is preserved: " + seed);
            var firstRoll = acts.Select(act => act.SecondBossEncounter!.Id).ToArray();
            DoubleTrouble.AddSecondBosses(acts, new MegaCrit.Sts2.Core.Random.Rng(999u));
            Check(acts.Select(act => act.SecondBossEncounter!.Id).SequenceEqual(firstRoll),
                "Repeated setup preserves exactly two bosses without rerolls");
            foreach (var act in acts)
            {
                var saved = act.ToSave();
                var restored = ActModel.FromSave(saved);
                Check(restored.BossEncounter.Id == act.BossEncounter.Id &&
                    restored.SecondBossEncounter!.Id == act.SecondBossEncounter!.Id,
                    "Both distinct boss identities survive native act saves");
                Check(act.PullNextEncounter(MegaCrit.Sts2.Core.Rooms.RoomType.Boss).Id == act.BossEncounter.Id,
                    "Native boss progression starts with the first boss");
                act.MarkRoomVisited(MegaCrit.Sts2.Core.Rooms.RoomType.Boss);
                Check(act.PullNextEncounter(MegaCrit.Sts2.Core.Rooms.RoomType.Boss).Id == act.SecondBossEncounter!.Id,
                    "Native boss progression advances to the distinct second boss");
            }
        }
        var mapAct = NewAct(0);
        DoubleTrouble.AddSecondBosses([mapAct], new MegaCrit.Sts2.Core.Random.Rng(42u));
        var map = new MegaCrit.Sts2.Core.Map.StandardActMap(new MegaCrit.Sts2.Core.Random.Rng(42u),
            mapAct, false, false, hasSecondBoss: mapAct.HasSecondBoss);
        Check(map.SecondBossMapPoint != null && map.BossMapPoint.Children.Contains(map.SecondBossMapPoint) &&
            map.SecondBossMapPoint.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Boss,
            "Native A10 map generation places a separate second boss after the first");
        var savedMap = MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(map);
        var restoredMap = new MegaCrit.Sts2.Core.Map.SavedActMap(savedMap);
        Check(restoredMap.SecondBossMapPoint != null &&
            restoredMap.BossMapPoint.Children.Contains(restoredMap.SecondBossMapPoint),
            "Both boss nodes and their connection survive native map saves");
        var configured = (DoubleTrouble)canonical.ToMutable();
        foreach (var count in new[] { 2, 3 })
        foreach (var fires in new[] { false, true })
        {
            ModifierValues.Set(configured, count);
            var savedModifier = configured.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            savedModifier.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var packet = new MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier();
            packet.Deserialize(reader);
            Check(ModifierValues.Get(ModifierModel.FromSerializable(packet)) == count &&
                ModifierValues.Get((ModifierModel)configured.MutableClone()) == count,
                "Double Trouble boss count survives native save/network packets and cloning: " + count);
            var act = NewAct(0);
            DoubleTrouble.AddSecondBosses([act], new MegaCrit.Sts2.Core.Random.Rng(42u), count);
            var original = new MegaCrit.Sts2.Core.Map.StandardActMap(new MegaCrit.Sts2.Core.Random.Rng(42u),
                act, false, false, hasSecondBoss: true);
            var expanded = new BossChainActMap(original, count, fires);
            var saved = MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(expanded);
            var mapWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(mapWriter);
            reader.Reset(mapWriter.Buffer);
            var mapPacket = new MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap();
            mapPacket.Deserialize(reader);
            foreach (var chainMap in new MegaCrit.Sts2.Core.Map.ActMap[] { expanded,
                new MegaCrit.Sts2.Core.Map.SavedActMap(saved), new MegaCrit.Sts2.Core.Map.SavedActMap(mapPacket) })
            {
                var chain = BossChainActMap.Chain(chainMap);
                var expectedTypes = Enumerable.Range(0, count).SelectMany(index => index == 0 || !fires
                    ? new[] { MegaCrit.Sts2.Core.Map.MapPointType.Boss }
                    : new[] { MegaCrit.Sts2.Core.Map.MapPointType.RestSite, MegaCrit.Sts2.Core.Map.MapPointType.Boss });
                Check(chain.Select(point => point.PointType).SequenceEqual(expectedTypes) &&
                    ReferenceEquals(chain[^1], chainMap.SecondBossMapPoint) && chain[^1].Children.Count == 0 &&
                    chain.All(point => ReferenceEquals(chainMap.GetPoint(point.coord), point)),
                    $"Boss chain topology and final node survive save/network: {count} bosses, campfires {fires}");
                var state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
                AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(state, new ModifierModel[] { configured });
                AccessTools.PropertySetter(typeof(RunState), nameof(RunState.Map)).Invoke(state, [chainMap]);
                foreach (var point in chain)
                {
                    AccessTools.Field(typeof(RunState), "_visitedMapCoords").SetValue(state, new List<MegaCrit.Sts2.Core.Map.MapCoord> { point.coord });
                    Check(MegaCrit.Sts2.Core.Map.MapTravel.GetTravelablePointsFrom(state, point).SequenceEqual(point.Children),
                        "Boss chain travel follows required campfires and bosses");
                    Check(BossChainMapTravelabilityPatch.LastVisitedChainPoint(state) == (chain.Count > 2 ? point : null) &&
                        Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen),
                            "RecalculateTravelability"))?.Owners.Contains(ModEntry.HarmonyId) == true,
                        "Map UI bypasses native boss shortcuts for every expanded boss/rest chain node, including loaded maps");
                    if (point.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Boss)
                        Check(BossChainRewardsProceedPatch.ContinuationPoint(chainMap.BossMapPoint, state) ==
                            (point.Children.Count > 0 ? point : chainMap.BossMapPoint),
                            "Only intermediate bosses continue through map after rewards");
                }
            }
            var encounters = new List<ModelId>();
            for (var index = 0; index < count; index++)
            {
                encounters.Add(act.PullNextEncounter(MegaCrit.Sts2.Core.Rooms.RoomType.Boss).Id);
                act.MarkRoomVisited(MegaCrit.Sts2.Core.Rooms.RoomType.Boss);
            }
            Check(encounters.Distinct().Count() == count, "Configured boss chain uses distinct encounters: " + count);
            var loaded = ActModel.FromSave(act.ToSave());
            AccessTools.Field(typeof(ActModel), "_allBossEncounters").SetValue(loaded, bosses);
            var loadState = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Field(typeof(RunState), "<Acts>k__BackingField").SetValue(loadState, new ActModel[] { loaded });
            ((DoubleTrouble)ModifierModel.FromSerializable(savedModifier)).OnRunLoaded(loadState);
            if (count == 3)
                Check(loaded.PullNextEncounter(MegaCrit.Sts2.Core.Rooms.RoomType.Boss).Id == encounters[2] &&
                    Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.AssetPaths)))?.Owners.Contains(ModEntry.HarmonyId) == true,
                    "Native load hook restores third boss identity and the map asset preload patch is installed");
        }
        var child = ModelDb.Modifier<CampfiresBetweenBosses>();
        Check(child.HasParent(new[] { configured }) && !child.HasParent(Array.Empty<ModifierModel>()) &&
            ModifierListPatch.IsCustomOnly(child) && ModifierModel.FromSerializable(child.ToMutable().ToSerializable()) is CampfiresBetweenBosses,
            "Campfires is a native saved custom-only submodifier requiring Double Trouble");
        ModifierValues.Set(configured, 1);
        Check(ModifierValues.Get(configured) == 2, "Double Trouble clamps below its two-boss minimum");
        ModifierValues.Set(configured, 4);
        Check(ModifierValues.Get(configured) == 3, "Double Trouble clamps above its three-boss maximum");
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_currentActIndex").SetValue(run, 2);
        AccessTools.Field(typeof(RunState), "<Acts>k__BackingField").SetValue(run, new ActModel[] { NewAct(0), NewAct(1), mapAct });
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        var fixtureHarmony = new Harmony("UltimateCustomRun.Smoke.DoubleTrouble");
        fixtureHarmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet), "TryGenerateTutorialRewards"),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoTutorialRewards)));
        fixtureHarmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet), "GenerateRewardsFor"),
            prefix: new HarmonyMethod(typeof(Program), nameof(TrackBossRewardGeneration)));
        try
        {
            MegaCrit.Sts2.Core.Rewards.RewardsSet Rewards()
            {
                var set = (MegaCrit.Sts2.Core.Rewards.RewardsSet)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet));
                AccessTools.Field(set.GetType(), "<Player>k__BackingField").SetValue(set, player);
                AccessTools.Field(set.GetType(), "<Rewards>k__BackingField").SetValue(set, new List<MegaCrit.Sts2.Core.Rewards.Reward>());
                return set;
            }
            var room = (MegaCrit.Sts2.Core.Rooms.CombatRoom)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rooms.CombatRoom));
            var combat = (MegaCrit.Sts2.Core.Combat.CombatState)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Combat.CombatState));
            AccessTools.Field(room.GetType(), "<CombatState>k__BackingField").SetValue(room, combat);
            AccessTools.Field(combat.GetType(), "_encounter").SetValue(combat, bosses[0]);
            AccessTools.Field(room.GetType(), "_extraRewards").SetValue(room, new Dictionary<Player, List<MegaCrit.Sts2.Core.Rewards.Reward>>());
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
            _bossRewardGenerations = 0;
            Rewards().WithRewardsFromRoom(room);
            Check(_bossRewardGenerations == 0 && DoubleTrouble.RewardActIndex(run) == 2,
                "Vanilla final-act reward suppression remains unchanged without Double Trouble");
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { canonical.ToMutable() });
            Rewards().WithRewardsFromRoom(room);
            AccessTools.Field(combat.GetType(), "_encounter").SetValue(combat, bosses[1]);
            Rewards().WithRewardsFromRoom(room);
            AccessTools.Field(combat.GetType(), "_encounter").SetValue(combat, bosses[2]);
            Rewards().WithRewardsFromRoom(room);
            Check(_bossRewardGenerations == 3 && run.CurrentActIndex == 2,
                "Every final-act boss independently invokes native reward generation without changing act progression");
        }
        finally { fixtureHarmony.UnpatchAll(fixtureHarmony.Id); }
    }

    private static int _bossRewardGenerations;
    private static bool NoTutorialRewards(ref bool __result) { __result = false; return false; }
    private static bool TrackBossRewardGeneration(ref List<MegaCrit.Sts2.Core.Rewards.Reward> __result)
    {
        _bossRewardGenerations++;
        __result = [];
        return false;
    }

    private static void TestMysteryEvents()
    {
        var hpLossDamage = 3m;
        CustomRunEnemyDamagePatch.Postfix(null!, null,
            (MegaCrit.Sts2.Core.Entities.Creatures.Creature)RuntimeHelpers.GetUninitializedObject(
                typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature)), ref hpLossDamage);
        Check(hpLossDamage == 3m,
            "Source-less event damage (Tablet of Truth max-HP loss) neither crashes nor receives enemy scaling");
        // Necrobinder card previews use Osty (a player-side monster) as dealer with no target.
        var osty = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)RuntimeHelpers.GetUninitializedObject(
            typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
        var monsterType = typeof(MonsterModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(MonsterModel).IsAssignableFrom(type));
        AccessTools.Field(osty.GetType(), "<Monster>k__BackingField").SetValue(osty, RuntimeHelpers.GetUninitializedObject(monsterType));
        AccessTools.Field(osty.GetType(), "<Side>k__BackingField").SetValue(osty, MegaCrit.Sts2.Core.Combat.CombatSide.Player);
        var previewDamage = 7m;
        CustomRunEnemyDamagePatch.Postfix(null!, osty, null, ref previewDamage);
        Check(osty.IsMonster && previewDamage == 7m,
            "Target-less card previews dealt by Osty neither crash nor receive enemy scaling");

        var tablet = ModelDb.Event<TabletOfTruth>();
        var aroma = ModelDb.Event<AromaOfChaos>();
        var statue = ModelDb.Event<SunkenStatue>();
        var vegetation = ModelDb.Event<DenseVegetation>();
        var unrest = ModelDb.Event<UnrestSite>();
        Check(MysteryEvents.IsUnconditionalEvent(tablet) && MysteryEvents.IsUnconditionalEvent(aroma) &&
            MysteryEvents.IsUnconditionalEvent(statue) && !MysteryEvents.IsUnconditionalEvent(vegetation) &&
            !MysteryEvents.IsUnconditionalEvent(unrest) && !MysteryEvents.IsUnconditionalEvent(ModelDb.Event<Neow>()),
            "??? excludes conditional and Ancient events while retaining unconditional Tablet of Truth");
        var pool = new EventModel[] { vegetation, tablet, unrest, aroma, statue };
        var seen = new HashSet<ModelId>();
        var selected = new List<ModelId>();
        var cursor = 0;
        for (var floor = 0; floor < 3; floor++)
        {
            var index = MysteryEvents.SelectEventIndex(pool, cursor, seen);
            selected.Add(pool[index].Id);
            seen.Add(pool[index].Id);
            cursor = index + 1;
        }
        Check(selected.SequenceEqual(new[] { tablet.Id, aroma.Id, statue.Id }),
            "Three extra floors skip every conditional event and visit distinct safe events in seeded pool order");
        Check(MysteryEvents.SelectEventIndex(pool, cursor, seen) == -1 &&
            MysteryEvents.SelectEventIndex([vegetation, unrest], 0, seen) == -1 &&
            MysteryEvents.SelectEventIndex([], 0, seen) == -1,
            "Exhausted, conditional-only and empty pools never repeat or select forbidden events");

        var canonical = ModelDb.Modifier<MysteryEvents>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(1, 5, 1, 3, "events") &&
            MysteryEvents.DescriptionText(1) == "Encounter [blue]1[/blue] additional Event after Neow." &&
            MysteryEvents.DescriptionText(3) == MysteryEvents.DisplayDescription,
            "??? slider spans 1..5 events, defaults to three, and uses singular/plural descriptions");
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.ImprovedStart &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<MysteryEvents>().Count() == 1,
            "??? appears once in Improved Start and is isolated from Daily modifiers");
        var modifier = (MysteryEvents)canonical.ToMutable();
        for (var stage = 0; stage <= 4; stage++)
        {
            modifier.MysteryStage = stage;
            var saved = modifier.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)modifier.MutableClone() })
                Check(restored is MysteryEvents mystery && mystery.MysteryStage == stage,
                    "Extra-event stage survives save, network and clone: " + stage);
        }
        modifier.MysteryStage = 1;
        Check(!modifier.RecordReady(10, 0, [10, 20]) && !modifier.RecordReady(10, 1, [10, 20]) &&
            !modifier.RecordReady(10, 1, [10, 20]) && modifier.RecordReady(20, 1, [10, 20]),
            "Extra events wait for every co-op player and reject duplicate/stale proceed actions");
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { modifier });
        AccessTools.Field(typeof(RunState), "_visitedMapCoords").SetValue(run,
            new List<MegaCrit.Sts2.Core.Map.MapCoord> { new(0, 0) });
        run.Map = new MegaCrit.Sts2.Core.Map.MockSinglePointActMap();
        var manager = (RunManager)RuntimeHelpers.GetUninitializedObject(typeof(RunManager));
        AccessTools.PropertySetter(typeof(RunManager), "State").Invoke(manager, [run]);
        modifier.OnRunLoaded(run);
        for (var count = 1; count <= 5; count++)
        {
            var adjustable = (MysteryEvents)canonical.ToMutable();
            ModifierValues.Set(adjustable, count);
            adjustable.MysteryStage = count;
            var saved = adjustable.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)adjustable.MutableClone() })
                Check(restored is MysteryEvents mystery && mystery.EventLimit == count && mystery.MysteryStage == count,
                    "??? count and active floor survive save, network and clone: " + count);
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { adjustable });
            adjustable.OnRunLoaded(run);
            Check(adjustable.NeedsEvents(run) && !adjustable.ShouldProceedToNextMapPoint(),
                "??? keeps travel blocked through the configured last event: " + count);
            adjustable.MysteryStage++;
            Check(!adjustable.NeedsEvents(run) && adjustable.ShouldProceedToNextMapPoint() &&
                MysteryEvents.HistoryIndex(run, 0, 1) == count + 1,
                "??? unlocks the main map and offsets history after exactly the configured count: " + count);
            Check(ModifierModel.FromSerializable(adjustable.ToSerializable()) is MysteryEvents finished &&
                finished.MysteryStage == count + 1 && finished.EventLimit == count,
                "??? completed stage survives saving at count " + count);
        }
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { modifier });
        var clamped = (MysteryEvents)canonical.ToMutable();
        ModifierValues.Set(clamped, int.MinValue);
        Check(clamped.EventLimit == 1, "??? slider clamps its lower bound to one event");
        ModifierValues.Set(clamped, int.MaxValue);
        Check(clamped.EventLimit == 5 && ModifierValues.Get(canonical) == 3,
            "??? slider clamps its upper bound to five without changing canonical defaults");
        for (var stage = 1; stage <= 3; stage++)
        {
            modifier.MysteryStage = stage;
            var roomType = MegaCrit.Sts2.Core.Rooms.RoomType.Monster;
            Check(!MysteryRoomTypePatch.Prefix(manager, MegaCrit.Sts2.Core.Map.MapPointType.Unknown, ref roomType) &&
                roomType == MegaCrit.Sts2.Core.Rooms.RoomType.Event && !modifier.ShouldProceedToNextMapPoint(),
                "Every extra floor is guaranteed to be an Event and blocks main-map travel: " + stage);
        }
        modifier.MysteryStage = 4;
        Check(modifier.ShouldProceedToNextMapPoint() && modifier.IsExtraRoom(run),
            "After exactly three Events, map travel unlocks while the last room remains reloadable");
        Check(MysteryEvents.HistoryIndex(run, 0, 0) == 0 && MysteryEvents.HistoryIndex(run, 0, 1) == 4 &&
            MysteryEvents.HistoryIndex(run, 0, 3) == 6 && MysteryEvents.HistoryIndex(run, 1, 1) == 1,
            "Three extra history floors preserve Neow and offset only the first act's actual map floors");
        run.AddVisitedMapCoord(new(0, 1));
        var normalType = MegaCrit.Sts2.Core.Rooms.RoomType.Monster;
        Check(!modifier.IsExtraRoom(run) &&
            MysteryRoomTypePatch.Prefix(manager, MegaCrit.Sts2.Core.Map.MapPointType.Unknown, ref normalType) &&
            normalType == MegaCrit.Sts2.Core.Rooms.RoomType.Monster,
            "Main-map question marks retain native room odds instead of becoming forced Events");
        var pulseWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        new NetMysteryProceedAction { Stage = 2 }.Serialize(pulseWriter);
        var pulseReader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        pulseReader.Reset(pulseWriter.Buffer);
        var pulse = new NetMysteryProceedAction();
        pulse.Deserialize(pulseReader);
        Check(pulse.Stage == 2, "Co-op proceed actions preserve their expected event stage");
        var roomSet = new MegaCrit.Sts2.Core.Rooms.RoomSet();
        roomSet.events.AddRange(pool);
        Check(MysteryEventPoolPatch.Prefix(roomSet, run) && roomSet.eventsVisited == 0,
            "Ordinary map event selection retains the native pool and cursor");
        AccessTools.Field(typeof(RunState), "_visitedMapCoords").SetValue(run,
            new List<MegaCrit.Sts2.Core.Map.MapCoord> { new(0, 0) });
        AccessTools.Field(typeof(RunState), "_visitedEventIds").SetValue(run, new HashSet<ModelId>());
        AccessTools.Field(typeof(RunState), "_players").SetValue(run,
            new List<Player> { (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player)) });
        modifier.MysteryStage = 1;
        Check(vegetation.IsAllowed(run) && !MysteryEventPoolPatch.Prefix(roomSet, run) && roomSet.NextEvent.Id == tablet.Id,
            "??? excludes an event with spawn conditions even when those conditions currently pass");
        roomSet.eventsVisited = 5;
        AccessTools.Field(typeof(RunState), "_visitedEventIds").SetValue(run, seen);
        var exhaustedRejected = false;
        try { MysteryEventPoolPatch.Prefix(roomSet, run); }
        catch (InvalidOperationException) { exhaustedRejected = true; }
        Check(exhaustedRejected && roomSet.eventsVisited == 5,
            "An exhausted extra-event pool cannot advance to a repeated event");
        TestMysteryProceedLifecycle(player: run.Players[0]);
        foreach (var target in new[] {
            AccessTools.Method(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord)),
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NNormalMapPoint), "UpdateIcon"),
            AccessTools.Method(typeof(RunState), nameof(RunState.GetHistoryEntryFor)) })
            Check(Harmony.GetPatchInfo(target)?.Owners.Contains(ModEntry.HarmonyId) == true,
                "Extra-event reload/history patch applies to the installed game: " + target.Name);
    }

    private static void TestMysteryProceedLifecycle(Player player)
    {
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var modifier = (MysteryEvents)ModelDb.Modifier<MysteryEvents>().ToMutable();
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { modifier });
        modifier.OnRunLoaded(run);
        var enqueued = new List<MegaCrit.Sts2.Core.GameActions.GameAction>();
        void Enqueue(MegaCrit.Sts2.Core.GameActions.GameAction action)
        {
            AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.GameActions.GameAction), "State").Invoke(action,
                [MegaCrit.Sts2.Core.Entities.Actions.GameActionState.WaitingForExecution]);
            enqueued.Add(action);
        }
        var first = new MysteryProceedAction(player, 0);
        modifier.RequestProceed(first, Enqueue);
        modifier.RequestProceed(new MysteryProceedAction(player, 0), Enqueue);
        Check(enqueued.Count == 1, "Proceed suppresses duplicates while an action is queued");
        AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.GameActions.GameAction), "State").Invoke(first,
            [MegaCrit.Sts2.Core.Entities.Actions.GameActionState.Canceled]);
        var retry = new MysteryProceedAction(player, 0);
        modifier.RequestProceed(retry, Enqueue);
        Check(enqueued.Count == 2, "A canceled Proceed action can be retried without reloading");
        AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.GameActions.GameAction), "State").Invoke(retry,
            [MegaCrit.Sts2.Core.Entities.Actions.GameActionState.Finished]);
        modifier.RequestProceed(new MysteryProceedAction(player, 0), Enqueue);
        Check(enqueued.Count == 3, "A completed no-op Proceed action releases the retry guard");

        var reveal = new TaskCompletionSource();
        var order = new List<string>();
        var transition = MysteryEvents.EnterEvent(
            () => { order.Add("out"); return Task.CompletedTask; },
            () => { order.Add("enter"); return Task.CompletedTask; },
            () => { order.Add("in"); return reveal.Task; });
        Check(transition.IsCompletedSuccessfully && !reveal.Task.IsCompleted &&
            order.SequenceEqual(new[] { "out", "enter", "in" }),
            "Room loading waits for fade-out, but an unfinished fade-in cannot block the action queue");
        reveal.SetResult();

        var act = ModelDb.Act<MegaCrit.Sts2.Core.Models.Acts.Overgrowth>().ToMutable();
        var pool = new MegaCrit.Sts2.Core.Rooms.RoomSet();
        pool.events.Add(ModelDb.Event<AromaOfChaos>());
        AccessTools.Field(typeof(ActModel), "_rooms").SetValue(act, pool);
        AccessTools.Field(typeof(RunState), "<Acts>k__BackingField").SetValue(run, new[] { act });
        AccessTools.Field(typeof(RunState), "_visitedEventIds").SetValue(run, new HashSet<ModelId> { pool.events[0].Id });
        modifier.MysteryStage = 1;
        Check(!modifier.HasNextEvent(run), "Proceed detects an exhausted pool before trying to load another room");
        AccessTools.Field(typeof(RunState), "_visitedEventIds").SetValue(run, new HashSet<ModelId>());
        Check(modifier.HasNextEvent(run), "Proceed can load an unvisited unconditional event");
        modifier.MysteryStage = modifier.EventLimit;
        Check(!modifier.HasNextEvent(run), "Proceed stops at the configured limit even if unvisited events remain");

        modifier.MysteryStage = 6;
        modifier.MysteryHistoryFloors = 2;
        ModifierValues.Set(modifier, 5);
        var restored = (MysteryEvents)ModifierModel.FromSerializable(modifier.ToSerializable());
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { restored });
        Check(restored.MysteryHistoryFloors == 2 && MysteryEvents.HistoryIndex(run, 0, 1) == 3,
            "Early exhaustion preserves the actual extra-floor count through saving");

        modifier.MysteryStage = 8;
        var legacy = modifier.ToSerializable();
        legacy.Props!.ints!.RemoveAll(prop => prop.name is "MysteryHistoryFloors" or "CustomValue");
        legacy.Props.ints.Add(new("CustomValue", 10));
        restored = (MysteryEvents)ModifierModel.FromSerializable(legacy);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { restored });
        Check(restored.EventLimit == 5 && restored.MysteryHistoryFloors == 8 && MysteryEvents.HistoryIndex(run, 0, 1) == 9,
            "Legacy ten-event saves clamp to five while preserving already-entered floors");
    }

    private static void TestHeadstart()
    {
        var canonical = ModelDb.Modifier<Headstart>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(1, 5, 1, 1, "relics") &&
            ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.ImprovedStart &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<Headstart>().Count() == 1,
            "Headstart appears once in Improved Start with a 1..5 relic slider, step one, default one");
        var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow,
            (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player)));
        Check(canonical.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == typeof(Headstart).Assembly,
            "Headstart provides a Neow relic-selection callback");
        for (var count = 1; count <= 5; count++)
        {
            var model = canonical.ToMutable();
            ModifierValues.Set(model, count);
            var saved = model.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)model.MutableClone() })
                Check(restored is Headstart && ModifierValues.Get(restored) == count,
                    "Headstart count survives save, co-op settings and clone: " + count);
            var selection = new HeadstartSelection(count, 20);
            Check(!selection.CanConfirm && !selection.Toggle(-1) && !selection.Toggle(20),
                "Headstart ignores invalid indexes and requires a complete selection");
            for (var index = 0; index < count; index++) Check(selection.Toggle(index), "Headstart accepts a distinct relic");
            Check(selection.CanConfirm && !selection.Toggle(count), "Headstart enforces exactly the configured count");
            Check(selection.Toggle(0) && !selection.CanConfirm && selection.Toggle(count) && selection.CanConfirm,
                "Headstart supports deselecting and replacing a relic");
            Headstart.ValidateSelection(selection.Indexes, count, 20);
            var choice = MegaCrit.Sts2.Core.GameActions.PlayerChoiceResult.FromIndexes(selection.Indexes.ToList());
            var choiceWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            choice.ToNetData().Serialize(choiceWriter);
            var choiceReader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            choiceReader.Reset(choiceWriter.Buffer);
            var net = choiceReader.Read<MegaCrit.Sts2.Core.Entities.Multiplayer.NetPlayerChoiceResult>();
            Check(MegaCrit.Sts2.Core.GameActions.PlayerChoiceResult.FromNetData(
                (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player)),
                (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState)), net).AsIndexes().SequenceEqual(selection.Indexes),
                "Headstart relic indexes survive native co-op choice serialization");
        }
        foreach (var invalid in new List<int>[] { [], [0, 0], [-1, 0], [0, 20], [0] })
        {
            var rejected = false;
            try { Headstart.ValidateSelection(invalid, 2, 20); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Headstart rejects incomplete, duplicate or out-of-range remote choices");
        }
        RelicModel[] relics =
        [
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DingyRug>(),
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Mango>(),
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.JewelryBox>(),
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BurningBlood>()
        ];
        var all = Headstart.FilterCandidates(relics.Concat(relics), new HashSet<ModelId>(), [], _ => true);
        Check(all.Count == relics.Length && relics.All(all.Contains),
            "Headstart includes unlocked Starter and Ancient relics without rarity exclusions or duplicates");
        Check(Headstart.FilterCandidates(relics.Concat(new RelicModel[] {
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Circlet>(),
            ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DeprecatedRelic>() }),
            new HashSet<ModelId>(), [], _ => true).Select(relic => relic.Id).SequenceEqual(all.Select(relic => relic.Id)),
            "Headstart excludes Circlet and Deprecated Relic even when unlocked and allowed at Neow");
        Check(all.Select(relic => relic.Id).SequenceEqual(Headstart.FilterCandidates(
            relics.Reverse(), new HashSet<ModelId>(), [], _ => true).Select(relic => relic.Id)),
            "Relic candidates have stable co-op indexes independent of source enumeration order");
        var filtered = Headstart.FilterCandidates(relics, new HashSet<ModelId> { relics[1].Id },
            [relics[0]], relic => relic.Id != relics[2].Id);
        Check(filtered.Select(relic => relic.Id).ToHashSet().SetEquals(new[] { relics[3].Id }),
            "Headstart excludes other-character, already-owned non-stackable and disallowed relics");
        Check(Headstart.DescriptionText(1) == Headstart.DisplayDescription &&
            Headstart.DescriptionText(5) == "Choose [blue]5[/blue] relics to start with.",
            "Headstart description handles singular and plural relic counts");
        var preset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(new ModifierPresetEntry { Id = canonical.Id.ToString(), Value = 5 }))!;
        Check(preset.Value == 5, "Headstart slider persists in modifier presets");
        var nativeScreen = (MegaCrit.Sts2.Core.Nodes.Screens.NChooseARelicSelection)RuntimeHelpers.GetUninitializedObject(
            typeof(MegaCrit.Sts2.Core.Nodes.Screens.NChooseARelicSelection));
        Check(!HeadstartRelicUi.Initialize(nativeScreen) && HeadstartRelicUi.DefaultFocus(nativeScreen) == null,
            "Headstart UI leaves unrelated native relic-selection screens untouched");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.NChooseARelicSelection), "_Ready"))
            ?.Owners.Contains(ModEntry.HarmonyId) == true,
            "Headstart picker initialization is patched against the installed game");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}

internal class TestNeow : Neow
{
    public bool FailGeneration;
    public IReadOnlyList<EventOption>? OptionsShown;
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        if (FailGeneration) throw new InvalidOperationException("Test generation failure");
        if (NeowRelicChoice.ModifierCount(2, this) != 0) throw new Exception("Vanilla path was not enabled");
        return Enumerable.Range(0, 3).Select(_ =>
            (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption))).ToArray();
    }
    protected override void SetEventState(LocString description, IEnumerable<EventOption> options) =>
        OptionsShown = options.ToArray();
}
