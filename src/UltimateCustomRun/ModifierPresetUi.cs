using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

internal static class ModifierPresetUi
{
    private const string DropdownScene = "res://scenes/screens/char_select/char_select_act_dropdown.tscn";
    private const string DropdownItemScene = "res://scenes/ui/dropdown_item.tscn";
    private const string PopupScene = "res://scenes/ui/generic_popup.tscn";
    private const string DeleteIconPath = "res://images/packed/main_menu/delete_button.png";
    private const float HorizontalPadding = 16;
    private const float RemoveButtonWidth = 44;
    private const float RemoveButtonGap = 8;

    private sealed class DropdownState
    {
        internal required NCustomRunModifiersList ModifierList;
        internal List<ModifierPreset> Presets = [];
        internal string? SelectedName;
    }

    private sealed class ScreenState
    {
        internal required NActDropdown Dropdown;
        internal required NButton SaveButton;
        internal required NCustomRunModifiersList ModifierList;
    }

    private static readonly ConditionalWeakTable<NActDropdown, DropdownState> Dropdowns = new();
    private static readonly ConditionalWeakTable<NCustomRunScreen, ScreenState> Screens = new();
    private static readonly Color ButtonColor = Color.FromHtml("36566b");
    private static readonly Color ButtonHoverColor = Color.FromHtml("496f83");
    private static readonly Color ButtonBorderColor = Color.FromHtml("a6bdc8");

    internal static void AddControls(NCustomRunScreen screen)
    {
        if (Screens.TryGetValue(screen, out _)) return;

        var randomizeButton = screen.GetNode<NCustomRunRandomizeButton>("%CustomRunRandomizeButton");
        var row = randomizeButton.GetParent<HBoxContainer>();
        var rowIndex = randomizeButton.GetIndex();
        // Hide rather than free: the screen still disables and enables its _randomizeButton on embark and
        // unready, and a freed node throws there, which made the first press of the start button fail.
        randomizeButton.Visible = false;
        row.AddThemeConstantOverride("separation", 20);

        var modifierList = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList")!.GetValue(screen)!;
        var dropdownSlot = new Control
        {
            Name = "ModifierPresetSlot",
            CustomMinimumSize = new Vector2(272, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddChild(dropdownSlot);
        row.MoveChild(dropdownSlot, rowIndex);

        var dropdown = ResourceLoader.Load<PackedScene>(DropdownScene).Instantiate<NActDropdown>();
        dropdown.Name = "ModifierPresetDropdown";
        dropdown.CustomMinimumSize = Vector2.Zero;
        dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dropdown.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        dropdown.GetNode<Control>("CurrentOption").CustomMinimumSize = new Vector2(272, 40);
        Dropdowns.Add(dropdown, new DropdownState { ModifierList = modifierList });
        dropdownSlot.AddChild(dropdown);
        dropdown.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ApplySettingsDropdownAppearance(dropdown);
        RebuildOptions(dropdown);

        var saveButton = CreateSaveButton();
        saveButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(button =>
        {
            _ = OpenSaveDialog(screen, dropdown);
        }));
        row.AddChild(saveButton);
        saveButton.FocusNeighborLeft = saveButton.GetPathTo(dropdown);
        saveButton.FocusPrevious = saveButton.GetPathTo(dropdown);
        dropdown.FocusNeighborRight = dropdown.GetPathTo(saveButton);
        dropdown.FocusNext = dropdown.GetPathTo(saveButton);

        Screens.Add(screen, new ScreenState { Dropdown = dropdown, SaveButton = saveButton, ModifierList = modifierList });
        UpdateAvailability(screen);
    }

    internal static void UpdateAvailability(NCustomRunScreen screen)
    {
        if (!Screens.TryGetValue(screen, out var state)) return;
        var mode = (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunScreen), "_uiMode")!.GetValue(screen)!;
        var editable = mode is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;
        state.Dropdown.Visible = editable;
        state.SaveButton.Visible = editable;
        state.Dropdown.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        state.SaveButton.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
    }

    private static NButton CreateSaveButton()
    {
        var button = new NButton
        {
            Name = "SaveModifierPresetButton",
            CustomMinimumSize = new Vector2(136, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var background = new Panel
        {
            Name = "Background",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        var style = new StyleBoxFlat
        {
            BgColor = ButtonColor,
            BorderColor = ButtonBorderColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        background.AddThemeStyleboxOverride("panel", style);
        button.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            Name = "Label",
            Text = IsRussian() ? "Сохранить" : "Save",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 25);
        label.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        button.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.MouseEntered += () => style.BgColor = ButtonHoverColor;
        button.MouseExited += () => style.BgColor = ButtonColor;
        return button;
    }

    private static void ApplySettingsDropdownAppearance(NActDropdown dropdown)
    {
        var label = dropdown.GetNode<MegaLabel>("CurrentOption/Label");
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = HorizontalPadding;
        label.OffsetRight = -(HorizontalPadding + RemoveButtonWidth);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        var currentOption = dropdown.GetNode<Panel>("CurrentOption/Highlight");
        currentOption.Modulate = Colors.White;
        currentOption.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("2c434f")
        });

        var optionsPanel = dropdown.GetNode<Panel>("DropdownContainer/Highlight");
        optionsPanel.Modulate = Colors.White;
        optionsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("12212a")
        });
    }

    private static void RebuildOptions(NActDropdown dropdown, string? selectedName = null)
    {
        if (!Dropdowns.TryGetValue(dropdown, out var state) || !GodotObject.IsInstanceValid(dropdown)) return;
        state.Presets = ModifierPresetStore.Load();

        var items = dropdown.GetNode<VBoxContainer>("DropdownContainer/VBoxContainer");
        foreach (var oldItem in items.GetChildren())
        {
            items.RemoveChild(oldItem);
            oldItem.QueueFree();
        }

        AddDropdownItem(items, ModifierPresetStore.EmptyName, ModifierPresetStore.Empty, dropdown, state, false);
        foreach (var preset in state.Presets)
            AddDropdownItem(items, ModifierPresetStore.IsReservedName(preset.Name)
                ? preset.Name + (IsRussian() ? " (сохранённый)" : " (saved)") : preset.Name, preset, dropdown, state);

        items.GetParent<NDropdownContainer>().RefreshLayout();
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(items)) items.GetParent<NDropdownContainer>().RefreshLayout();
        }).CallDeferred();
        var label = dropdown.GetNode<MegaLabel>("CurrentOption/Label");
        state.SelectedName = selectedName;
        label.SetTextAutoSize(selectedName ?? (IsRussian() ? "Выбрать набор" : "Select loadout"));
    }

    private static void AddDropdownItem(
        VBoxContainer items, string text, ModifierPreset preset, NActDropdown dropdown, DropdownState state, bool removable = true)
    {
        var item = ResourceLoader.Load<PackedScene>(DropdownItemScene).Instantiate<NDropdownItem>();
        item.Selected += _ =>
        {
            AccessTools.Method(typeof(NDropdown), "CloseDropdown")!.Invoke(dropdown, null);
            state.SelectedName = preset.Name;
            dropdown.GetNode<MegaLabel>("CurrentOption/Label").SetTextAutoSize(preset.Name);
            ApplyPreset(state.ModifierList, preset);
        };
        items.AddChild(item);
        item.Text = text;
        var label = item.GetNode<MegaLabel>("Label");
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = HorizontalPadding;
        label.OffsetRight = -HorizontalPadding;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (removable) AddRemoveButton(item, preset, dropdown, state);
    }

    private static void AddRemoveButton(NDropdownItem item, ModifierPreset preset, NActDropdown dropdown, DropdownState state)
    {
        var label = item.GetNode<MegaLabel>("Label");
        label.OffsetRight = -(HorizontalPadding + RemoveButtonWidth + RemoveButtonGap);
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        var button = new ModifierPresetRemoveButton
        {
            Name = "RemovePresetButton", CustomMinimumSize = new Vector2(RemoveButtonWidth, 44),
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = IsRussian() ? $"Удалить набор «{preset.Name}»" : $"Delete loadout: {preset.Name}"
        };
        item.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        button.OffsetLeft = -(HorizontalPadding + RemoveButtonWidth);
        button.OffsetRight = -HorizontalPadding;
        var highlight = new ColorRect
        {
            Color = new Color(0.7f, 0.15f, 0.12f, 0.5f), Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        button.AddChild(highlight);
        highlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var icon = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(DeleteIconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 8;
        icon.OffsetRight = icon.OffsetBottom = -8;
        button.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ => highlight.Visible = true));
        button.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ => highlight.Visible = false));
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
            Callable.From(() => DeletePreset(dropdown, state, preset.Name)).CallDeferred()));
    }

    private static void DeletePreset(NActDropdown dropdown, DropdownState state, string name)
    {
        if (!GodotObject.IsInstanceValid(dropdown)) return;
        try
        {
            ModifierPresetStore.Delete(name);
            // Rebuild after the release signal returns; removing a row never applies a preset.
            var selected = string.Equals(state.SelectedName, name, StringComparison.OrdinalIgnoreCase) ? null : state.SelectedName;
            AccessTools.Method(typeof(NDropdown), "CloseDropdown")!.Invoke(dropdown, null);
            RebuildOptions(dropdown, selected);
        }
        catch (Exception ex) { GD.PushError($"[Ultimate Custom Run] Could not delete modifier preset: {ex}"); }
    }

    internal static void RefreshDropdownFocus(NActDropdown dropdown)
    {
        if (!Dropdowns.TryGetValue(dropdown, out _)) return;
        var items = dropdown.GetNode<VBoxContainer>("DropdownContainer/VBoxContainer").GetChildren().OfType<NDropdownItem>().ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var previous = items[Math.Max(0, index - 1)];
            var next = items[Math.Min(items.Length - 1, index + 1)];
            var remove = item.GetNodeOrNull<NButton>("RemovePresetButton");
            item.FocusPrevious = item.GetPathTo(previous.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)previous);
            item.FocusNext = item.GetPathTo(remove ?? (Control)next);
            if (remove == null) continue;
            // Native OpenDropdown rewrites row focus neighbors on each opening.
            item.FocusNeighborRight = item.GetPathTo(remove);
            item.FocusNext = item.GetPathTo(remove);
            remove.FocusNeighborLeft = remove.GetPathTo(item);
            remove.FocusPrevious = remove.GetPathTo(item);
            remove.FocusNeighborRight = remove.GetPath();
            remove.FocusNeighborTop = remove.GetPathTo(previous.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)previous);
            remove.FocusNeighborBottom = remove.GetPathTo(next.GetNodeOrNull<NButton>("RemovePresetButton") ?? (Control)next);
            remove.FocusNext = remove.FocusNeighborBottom;
        }
    }

    private static void ApplyPreset(NCustomRunModifiersList list, ModifierPreset preset)
    {
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes")!.GetValue(list)!;
        var selected = new List<ModifierModel>();
        foreach (var entry in preset.Modifiers)
        {
            var source = tickboxes.Select(tickbox => tickbox.Modifier)
                .FirstOrDefault(modifier => modifier != null && string.Equals(modifier.Id.ToString(), entry.Id, StringComparison.Ordinal));
            if (source == null) continue;

            var modifier = (ModifierModel)source.MutableClone();
            if (modifier is CustomRunParameters parameters)
            {
                var values = entry.Parameters ?? CustomRunParameterValues.Default;
                foreach (var parameter in Enum.GetValues<CustomRunParameter>())
                    CustomRunParameterValuesStore.Set(parameters, parameter, values.Get(parameter));
            }
            if (modifier is MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)
                ModifierValues.SetSealedPool(modifier, entry.SealedPoolSize ?? ModifierValues.SealedPoolSpec.Default);
            if (modifier is Dill dill) dill.MaxHpPerFight = entry.MaxHpPerFight ?? Dill.GrowthSpec.Default;
            if (entry.Value is { } value && ModifierValues.For(modifier) != null)
                ModifierValues.Set(modifier, value);
            selected.Add(modifier);
        }

        list.SetTickedModifiers(selected);
    }

    private static async Task OpenSaveDialog(NCustomRunScreen screen, NActDropdown dropdown)
    {
        if (!Screens.TryGetValue(screen, out var state) || !GodotObject.IsInstanceValid(state.ModifierList)) return;
        var modals = NModalContainer.Instance;
        if (modals == null || modals.OpenModal != null) return;

        string? validationError = null;
        while (true)
        {
            var popup = ResourceLoader.Load<PackedScene>(PopupScene).Instantiate<NGenericPopup>();
            popup.Name = "ModifierPresetNamePopup";
            modals.Add(popup);

            var verticalPopup = popup.GetNode<NVerticalPopup>("VerticalPopup");
            var russian = IsRussian();
            var confirmation = popup.WaitForConfirmation(
                new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.body"),
                new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.header"),
                new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
                new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
            verticalPopup.SetText(russian ? "Сохранить набор" : "Save loadout", string.Empty);
            verticalPopup.YesButton.SetText(russian ? "Сохранить" : "Save");
            verticalPopup.NoButton.SetText(russian ? "Отмена" : "Cancel");

            var body = verticalPopup.GetNode<RichTextLabel>("Description");
            body.Visible = false;
            var input = new LineEdit
            {
                Name = "LoadoutNameInput",
                PlaceholderText = validationError ?? (russian ? "Название набора" : "Loadout name"),
                CustomMinimumSize = new Vector2(400, 64),
                Size = new Vector2(400, 64),
                FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MaxLength = 64,
                Alignment = HorizontalAlignment.Center,
                ExpandToTextLength = false,
                ZIndex = 1
            };
            input.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
            input.AddThemeFontSizeOverride("font_size", 26);
            input.AddThemeColorOverride("font_color", Color.FromHtml("fff6e2"));
            input.AddThemeColorOverride("font_placeholder_color", new Color(1, 0.9647f, 0.8863f, 0.6f));
            verticalPopup.AddChild(input);
            input.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            input.OffsetLeft = -200;
            input.OffsetTop = -32;
            input.OffsetRight = 200;
            input.OffsetBottom = 32;
            Callable.From(input.GrabFocus).CallDeferred();

            var confirmed = await confirmation;
            var name = input.Text.Trim();
            modals.Clear();

            if (!confirmed) return;
            if (name.Length == 0 || ModifierPresetStore.IsReservedName(name))
            {
                validationError = name.Length == 0
                    ? (russian ? "Введите название набора" : "Enter a loadout name")
                    : (russian ? "Имя Empty зарезервировано" : "Empty is reserved. Choose another name");
                continue;
            }

            try
            {
                ModifierPresetStore.Save(name, state.ModifierList.GetModifiersTickedOn());
                RebuildOptions(dropdown, name);
            }
            catch (Exception ex)
            {
                GD.PushError($"[Ultimate Custom Run] Could not save modifier preset: {ex}");
            }
            return;
        }
    }

    private static bool IsRussian() => LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
}

internal sealed class ModifierPresetRemoveButton : NButton
{
    // NButton._Ready rejects derived types before connecting their input signals.
    public override void _Ready() => ConnectSignals();

    public override void _GuiInput(InputEvent input)
    {
        base._GuiInput(input);
        // Keyboard/controller selection must not bubble up and also apply this row.
        if (input is InputEventMouseButton || input.IsAction(MegaCrit.Sts2.Core.ControllerInput.MegaInput.select)) AcceptEvent();
    }
}

[HarmonyPatch(typeof(NDropdown), "OpenDropdown")]
internal static class ModifierPresetDropdownFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDropdown __instance)
    {
        if (__instance is NActDropdown dropdown) ModifierPresetUi.RefreshDropdownFocus(dropdown);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Ready))]
internal static class ModifierPresetScreenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => ModifierPresetUi.AddControls(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), "AfterInitialized")]
internal static class ModifierPresetAvailabilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) => ModifierPresetUi.UpdateAvailability(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnRandomizePressed")]
internal static class ModifierPresetDisableRandomizePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => false;
}
