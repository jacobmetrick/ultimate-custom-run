using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

internal static class ModifierValueUi
{
    private enum ValueKind { Primary, SealedPool, DillGrowth }
    private sealed class Row
    {
        internal required NRunModifierTickbox Parent;
        internal required ModifierValues.Spec Spec;
        internal required HBoxContainer Root;
        internal required NSlider Slider;
        internal required Label Label;
        internal bool Applying;
        internal ValueKind Kind;
    }
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, List<Row>> Rows = new();
    private static List<NRunModifierTickbox> Tickboxes(NCustomRunModifiersList list) =>
        (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;

    internal static Control? Create(NCustomRunModifiersList list, NRunModifierTickbox parent)
    {
        if (parent.Modifier is not MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck and not Dill) return CreateRow(list, parent);
        var kind = parent.Modifier is Dill ? ValueKind.DillGrowth : ValueKind.SealedPool;
        var root = new VBoxContainer
        {
            Name = parent.Modifier is Dill ? "DillValues" : "SealedDeckValues",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = parent.IsTicked
        };
        root.AddChild(CreateRow(list, parent)!);
        root.AddChild(CreateRow(list, parent, kind)!);
        return root;
    }

    private static ModifierValues.Spec? SpecFor(ModifierModel? modifier, ValueKind kind) => kind switch
    {
        ValueKind.SealedPool => ModifierValues.SealedPoolSpec,
        ValueKind.DillGrowth => Dill.GrowthSpec,
        _ => ModifierValues.For(modifier)
    };

    private static int ValueFor(ModifierModel modifier, ValueKind kind) => kind switch
    {
        ValueKind.SealedPool => ModifierValues.GetSealedPool(modifier),
        ValueKind.DillGrowth => ((Dill)modifier).MaxHpPerFight,
        _ => ModifierValues.Get(modifier)
    };

    private static Control? CreateRow(NCustomRunModifiersList list, NRunModifierTickbox parent, ValueKind kind = ValueKind.Primary)
    {
        if (SpecFor(parent.Modifier, kind) is not { } spec) return null;
        var root = new HBoxContainer
        {
            Name = parent.Modifier!.Id.Entry + kind + "Value", CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = parent.IsTicked
        };
        root.AddThemeConstantOverride("separation", 16);
        root.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var label = new Label
        {
            CustomMinimumSize = new Vector2(190, 0), VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 24);
        root.AddChild(label);
        // NSlider assumes a zero minimum in its mouse/handle arithmetic. Use step
        // indices, mapping 0..N to the actual requested minimum, maximum and step.
        var slider = ResourceLoader.Load<PackedScene>("res://scenes/ui/volume_slider.tscn").Instantiate<NSlider>();
        slider.Name = "ValueSlider";
        slider.MinValue = 0;
        slider.MaxValue = Math.Max(1, (spec.Max - spec.Min) / spec.Step);
        slider.Step = 1;
        slider.Rounded = true;
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.CustomMinimumSize = new Vector2(160, 64);
        slider.FocusMode = Control.FocusModeEnum.All;
        foreach (var child in slider.FindChildren("*", "Control", true, false).OfType<Control>())
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(slider);
        var row = new Row { Parent = parent, Spec = spec, Root = root, Slider = slider, Label = label, Kind = kind };
        Rows.GetOrCreateValue(list).Add(row);
        slider.Ready += () => Refresh(list);
        slider.ValueChanged += value =>
        {
            if (row.Applying || !Editable(list)) return;
            var amount = spec.Min + (int)Math.Round(value) * spec.Step;
            if (kind == ValueKind.SealedPool) ModifierValues.SetSealedPool(parent.Modifier!, amount);
            else if (kind == ValueKind.DillGrowth) ((Dill)parent.Modifier!).MaxHpPerFight = amount;
            else SetFamilyValue(list, parent.Modifier!, amount);
            Refresh(list);
            list.EmitSignal(NCustomRunModifiersList.SignalName.ModifiersChanged);
        };
        slider.GuiInput += input =>
        {
            if (!Editable(list)) return;
            if (input.IsActionPressed(MegaInput.left)) { slider.Value--; slider.AcceptEvent(); }
            if (input.IsActionPressed(MegaInput.right)) { slider.Value++; slider.AcceptEvent(); }
        };
        return root;
    }

    private static bool SameFamily(ModifierModel first, ModifierModel second) =>
        first.GetType() == second.GetType() || ModifierVariantUi.Families.Any(family =>
            ModifierVariantUi.BelongsTo(first, family) && ModifierVariantUi.BelongsTo(second, family));

    private static void SetFamilyValue(NCustomRunModifiersList list, ModifierModel source, int value)
    {
        foreach (var tickbox in Tickboxes(list))
            if (tickbox.Modifier is { } modifier && SameFamily(source, modifier)) ModifierValues.Set(modifier, value);
    }

    internal static void ApplyIncoming(NCustomRunModifiersList list, IReadOnlyCollection<ModifierModel> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                foreach (var tickbox in Tickboxes(list))
                    if (tickbox.Modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                        ModifierValues.SetSealedPool(tickbox.Modifier, ModifierValues.GetSealedPool(modifier));
            if (modifier is Dill incoming)
                foreach (var tickbox in Tickboxes(list))
                    if (tickbox.Modifier is Dill target) target.MaxHpPerFight = incoming.MaxHpPerFight;
            if (ModifierValues.For(modifier) != null) SetFamilyValue(list, modifier, ModifierValues.Get(modifier));
        }
        CustomRunParametersUi.ApplyIncoming(list, modifiers);
        Refresh(list);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        SubmodifierUi.Refresh(list);
        if (!Rows.TryGetValue(list, out var rows)) return;
        foreach (var row in rows)
        {
            row.Applying = true;
            try
            {
                row.Root.Visible = row.Parent.IsTicked;
                if (row.Root.GetParent() is VBoxContainer group && (group.Name == "SealedDeckValues" || group.Name == "DillValues"))
                    group.Visible = row.Parent.IsTicked;
                var editable = Editable(list) && !
                    (ModifierGroupsUi.IsSingleplayer(list) && ModifierGroups.IsSingleplayerDisabled(row.Parent.Modifier));
                row.Spec = SpecFor(row.Parent.Modifier, row.Kind)!;
                editable &= row.Spec.Max > row.Spec.Min;
                row.Slider.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
                row.Slider.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
                // NSlider divides by MaxValue. A single allowed pick count uses a disabled 0..1 track.
                row.Slider.MaxValue = Math.Max(1, (row.Spec.Max - row.Spec.Min) / row.Spec.Step);
                var value = ValueFor(row.Parent.Modifier!, row.Kind);
                var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
                var unit = russian ? row.Spec.Unit switch
                {
                    "bosses" => "боссов", "cards" => "карт", "offers" => "карт в пуле", "extra copies" => "доп. копий", "minutes" => "мин.",
                    "initial max HP" => "начальных макс. HP", "max HP per fight" => "макс. HP за бой", "events" => "событий", "relics" => "реликвий",
                    "extra relics" => "доп. реликвий", "extra cards" => "доп. карт", _ => "% золота"
                } : row.Spec.Unit;
                if (row.Kind == ValueKind.Primary && row.Parent.Modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                    unit = russian ? "карт в колоду" : "cards to choose";
                row.Label.Text = $"{value} {unit}";
                if (row.Slider.IsNodeReady()) row.Slider.SetValueWithoutAnimation((value - row.Spec.Min) / row.Spec.Step);
                else row.Slider.SetValueNoSignal((value - row.Spec.Min) / row.Spec.Step);
                UpdateDescription(row.Parent);
            }
            finally { row.Applying = false; }
        }
        CustomRunParametersUi.Refresh(list);
        StartOrderUi.Refresh(list);
        ModifierGroupsUi.Refresh(list);
    }

    private static bool Editable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    private static void UpdateDescription(NRunModifierTickbox row)
    {
        var modifier = row.Modifier!;
        var text = new LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.MODIFIER_LABEL");
        var title = modifier.Title.GetFormattedText();
        if (ModifierVariantUi.IsParent(modifier)) title += " [color=#ff9c3d]*[/color]";
        text.Add("color", ModifierGroups.Classify(modifier, ModelDb.BadModifiers.Select(m => m.GetType()).ToHashSet())
            == ModifierGroup.Negatives ? "red" : "green");
        text.Add("modifier_title", title);
        text.Add("modifier_description", modifier.Description.GetFormattedText());
        row.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text.GetFormattedText();
    }
}
