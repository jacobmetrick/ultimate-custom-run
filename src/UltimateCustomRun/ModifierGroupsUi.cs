using System.Runtime.CompilerServices;
using OpCodes = System.Reflection.Emit.OpCodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;

namespace UltimateCustomRun;

[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList._Ready))]
internal static class ModifierGroupsUi
{
    private sealed class Section
    {
        internal required ModifierGroup Group;
        internal required VBoxContainer Root;
        internal required NButton Header;
        internal required MarginContainer Body;
        internal required VBoxContainer ChildList;
        internal required TextureRect Arrow;
        internal required List<NRunModifierTickbox> Rows;
        internal bool Expanded = true;
    }
    private sealed class LockEntry
    {
        internal required NRunModifierTickbox Row;
        internal required List<Control> Nodes;
        internal Section Home = null!;
    }
    private sealed class Layout
    {
        internal required VBoxContainer Content;
        internal List<Section> Sections = [];
        internal List<LockEntry> Lockable = [];
        internal Dictionary<Section, List<Node>> OriginalChildren = [];
        internal Dictionary<Section, List<NRunModifierTickbox>> OriginalRows = [];
        // Forced rows and their tick state from before the game mode forced them.
        internal Dictionary<NRunModifierTickbox, bool> Forced = [];
    }
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Layout> Layouts = new();
    private static readonly Color NegativeColor = Color.FromHtml("ff6b64");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var source = AccessTools.Method(typeof(NCustomRunModifiersList), "GetAllModifiers");
        for (var i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(source)) continue;
            // Inject at the actual UI enumeration call site; a tiny iterator factory may be inlined.
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(ModifierListPatch), nameof(ModifierListPatch.ForCustomRun))));
            return code;
        }
        throw new InvalidOperationException("Custom Run modifier enumeration changed; could not add our custom modifiers safely.");
    }

    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance)
    {
        // Wait for vanilla's queued child additions/removals and each checkbox's _Ready.
        Callable.From(() => Build(__instance)).CallDeferred();
    }

    internal static Control? DefaultFocus(NCustomRunModifiersList list) =>
        Layouts.TryGetValue(list, out var layout) ? layout.Sections.FirstOrDefault()?.Header : null;

    private static void Build(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || Layouts.TryGetValue(list, out _)) return;
        var content = list.GetNode<VBoxContainer>("ScrollContainer/Mask/Content");
        var rows = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        var negatives = ModelDb.BadModifiers.Select(m => m.GetType()).ToHashSet();
        var font = ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres");
        var arrowTexture = ResourceLoader.Load<Texture2D>("res://images/packed/common_ui/settings_tiny_right_arrow.png");
        var layout = new Layout { Content = content };
        Layouts.Add(list, layout);
        content.AddThemeConstantOverride("separation", 12);

        foreach (var (group, title) in ModifierGroups.Sections)
        {
            var children = rows.Where(row => row.Modifier != null && ModifierGroups.Classify(row.Modifier, negatives) == group).ToList();
            var visibleRows = children.Where(row => !ModifierVariantUi.IsHiddenVariant(row.Modifier) && row.Modifier is not SubmodifierModel).ToList();
            var sectionRoot = new VBoxContainer { Name = group.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            if (group == ModifierGroup.Disabled) sectionRoot.Modulate = new Color(1, 1, 1, 0.6f);
            sectionRoot.AddThemeConstantOverride("separation", 4);
            var header = new NButton
            {
                Name = "SectionHeader", CustomMinimumSize = new Vector2(0, 56),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All,
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            var highlight = new ColorRect
            {
                Name = "Highlight", Color = new Color(1, 0.6392157f, 0.44705883f, 0.15f),
                MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false
            };
            header.AddChild(highlight);
            highlight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            header.AddChild(margin);
            margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var heading = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            heading.AddThemeConstantOverride("separation", 8);
            margin.AddChild(heading);
            var arrowSlot = new Control { CustomMinimumSize = new Vector2(40, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
            var arrow = new TextureRect
            {
                Texture = arrowTexture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = Mathf.Pi / 2,
                PivotOffset = new Vector2(16, 16)
            };
            arrowSlot.AddChild(arrow);
            arrow.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            arrow.OffsetLeft = -16; arrow.OffsetTop = -16; arrow.OffsetRight = 16; arrow.OffsetBottom = 16;
            heading.AddChild(arrowSlot);
            var label = new Label
            {
                Text = title, VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", 28);
            label.AddThemeColorOverride("font_color", group is ModifierGroup.Ascension or ModifierGroup.Negatives ? NegativeColor : new Color(1, 0.9647059f, 0.8862745f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
            label.AddThemeConstantOverride("shadow_offset_x", 3);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
            heading.AddChild(label);
            var line = new ColorRect
            {
                Color = new Color(1, 0.9647059f, 0.8862745f, 0.15f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            header.AddChild(line);
            line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
            line.OffsetTop = -1;
            var body = new MarginContainer { Name = "Children", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            // Native checkbox center is 28 px; header arrow center is 8 + 40/2 = 28 px.
            body.AddThemeConstantOverride("margin_left", 0);
            var childList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            childList.AddThemeConstantOverride("separation", 8);
            body.AddChild(childList);
            if (group == ModifierGroup.Disabled)
                body.AddChild(new Control { Name = "ClickBlocker", MouseFilter = Control.MouseFilterEnum.Stop });
            sectionRoot.AddChild(header);
            sectionRoot.AddChild(body);
            content.AddChild(sectionRoot);
            var variantRows = new List<ModifierVariantUi.VariantRow>();
            try
            {
                foreach (var family in ModifierVariantUi.Families)
                {
                    var parentRow = children.FirstOrDefault(row => ModifierVariantUi.IsParent(row.Modifier, family));
                    var draftRow = children.FirstOrDefault(row => ModifierVariantUi.IsDraft(row.Modifier, family));
                    var pickAnyRow = children.FirstOrDefault(row => ModifierVariantUi.IsPickAny(row.Modifier, family));
                    if (parentRow == null || draftRow == null) continue;
                    variantRows.Add(ModifierVariantUi.Create(list, family, parentRow, draftRow, pickAnyRow, () => Refresh(list)));
                }
            }
            catch (Exception ex)
            {
                GD.PushError($"[Ultimate Custom Run] Could not build modifier sub-options: {ex}");
            }
            foreach (var row in children)
            {
                row.Reparent(childList);
                row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var lockEntry = ModifierGroups.IsSingleplayerDisabled(row.Modifier) || BlitzCompatibility.IsDisabled(row.Modifier)
                    ? new LockEntry { Row = row, Nodes = [row] } : null;
                if (lockEntry != null) layout.Lockable.Add(lockEntry);
                if (ModifierVariantUi.IsHiddenVariant(row.Modifier))
                {
                    row.Visible = false;
                    row.FocusMode = Control.FocusModeEnum.None;
                    continue;
                }
                // Custom-only modifiers are absent from the global daily pool.
                if (row.Modifier is { } modifier &&
                    (ModifierListPatch.IsCustomOnly(modifier) || ModifierVariantUi.IsParent(modifier)))
                {
                    var text = new MegaCrit.Sts2.Core.Localization.LocString("main_menu_ui", "CUSTOM_RUN_SCREEN.MODIFIER_LABEL");
                    var modifierTitle = modifier.Title.GetFormattedText();
                    if (ModifierVariantUi.IsParent(modifier)) modifierTitle += " [color=#ff9c3d]*[/color]";
                    text.Add("color", group is ModifierGroup.Ascension or ModifierGroup.Negatives ? "red" : "green");
                    text.Add("modifier_title", modifierTitle);
                    text.Add("modifier_description", modifier.Description.GetFormattedText());
                    row.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("HBoxContainer/Description").Text = text.GetFormattedText();
                }
                foreach (var variantRow in variantRows.Where(variantRow => ReferenceEquals(row, variantRow.ParentRow)))
                {
                    childList.AddChild(variantRow.Container);
                    lockEntry?.Nodes.Add(variantRow.Container);
                }
                if (ModifierValueUi.Create(list, row) is { } valueRow)
                {
                    childList.AddChild(valueRow);
                    lockEntry?.Nodes.Add(valueRow);
                }
                if (CustomRunParametersUi.Create(list, row) is { } parameterRows)
                    childList.AddChild(parameterRows);
                if (CoopHandicapUi.Create(list, row) is { } handicapRows)
                {
                    childList.AddChild(handicapRows);
                    lockEntry?.Nodes.Add(handicapRows);
                }
            }
            if (group == ModifierGroup.ImprovedStart) childList.AddChild(StartOrderUi.Create(list));
            if (variantRows.Count > 0) ModifierVariantUi.InitializeChoices(list);
            LinkedModifierChains.Attach(body, visibleRows);
            var section = new Section
            {
                Group = group, Root = sectionRoot, Header = header, Body = body,
                ChildList = childList, Arrow = arrow, Rows = visibleRows
            };
            layout.Sections.Add(section);
            header.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Toggle(content, layout, section)));
            header.MouseEntered += () => highlight.Visible = true;
            header.MouseExited += () => highlight.Visible = false;
        }

        SubmodifierUi.Attach(list, rows);
        foreach (var section in layout.Sections)
        {
            layout.OriginalChildren[section] = [.. section.ChildList.GetChildren()];
            layout.OriginalRows[section] = [.. section.Rows];
        }
        foreach (var entry in layout.Lockable)
            entry.Home = layout.Sections.First(section => section.ChildList == entry.Row.GetParent());
        content.MinimumSizeChanged += () => Callable.From(() => ResizeContent(content)).CallDeferred();
        ApplyMode(list, layout);
        ModifierValueUi.Refresh(list);
        UpdateFocus(layout);
        ResizeContent(content);
        Callable.From(() => ResizeContent(content)).CallDeferred();
    }

    internal static void ApplyMode(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout)) return;
        ApplyMode(list, layout);
        Refresh(list);
    }

    private static void ApplyMode(NCustomRunModifiersList list, Layout layout)
    {
        var disabled = layout.Sections.FirstOrDefault(section => section.Group == ModifierGroup.Disabled);
        if (disabled == null) return;
        var moved = false;
        var unticked = false;
        foreach (var entry in layout.Lockable)
        {
            var row = entry.Row;
            var locked = IsLocked(list, row.Modifier);
            var target = locked ? disabled : entry.Home;
            if (row.GetParent() != target.ChildList)
            {
                var source = target == disabled ? entry.Home : disabled;
                var focus = layout.Content.GetViewport()?.GuiGetFocusOwner();
                if (focus != null && entry.Nodes.Any(node => node == focus || node.IsAncestorOf(focus)))
                    source.Header.GrabFocus();
                foreach (var node in entry.Nodes) node.Reparent(target.ChildList, false);
                moved = true;
            }
            if (locked && row.IsTicked) { row.IsTicked = false; unticked = true; }
            row.FocusMode = locked || ModifierVariantUi.IsHiddenVariant(row.Modifier)
                ? Control.FocusModeEnum.None
                : Control.FocusModeEnum.All;
        }
        if (moved)
            foreach (var section in layout.Sections) Reorder(layout, section);
        var forcedChanged = ApplyForced(list, layout);
        if (unticked || forcedChanged) ModifierVariantUi.RefreshFromTickboxes(list);
        disabled.Root.Visible = disabled.Rows.Count > 0;
    }

    // Lockable nodes return to their original positions; nodes moved in from other sections follow in build order.
    private static void Reorder(Layout layout, Section section)
    {
        var original = layout.OriginalChildren.GetValueOrDefault(section) ?? [];
        var current = section.ChildList.GetChildren().ToHashSet();
        var incoming = layout.Lockable.Where(entry => entry.Home != section)
            .SelectMany(entry => entry.Nodes).Where(current.Contains);
        var order = original.Where(current.Contains).Concat(incoming).Distinct().ToList();
        for (var i = 0; i < order.Count; i++) section.ChildList.MoveChild(order[i], i);
        var rows = order.OfType<NRunModifierTickbox>().ToHashSet();
        var originalRows = layout.OriginalRows.GetValueOrDefault(section) ?? [];
        section.Rows = originalRows.Where(rows.Contains)
            .Concat(layout.Lockable.Where(entry => entry.Home != section && rows.Contains(entry.Row) &&
                !ModifierVariantUi.IsHiddenVariant(entry.Row.Modifier)).Select(entry => entry.Row))
            .Distinct().ToList();
    }

    // In Blitz, Blitz Merchant is always on: the mode already uses it for every Merchant, and its slider sets the picks.
    private static bool ApplyForced(NCustomRunModifiersList list, Layout layout)
    {
        var editable = IsEditable(list);
        var blitz = BlitzCompatibility.IsBlitz(list);
        var changed = false;
        var forcedRows = layout.Sections.SelectMany(section => section.Rows)
            .Where(row => BlitzCompatibility.IsForced(row.Modifier)).ToList();
        foreach (var row in forcedRows)
        {
            if (blitz)
            {
                if (!layout.Forced.ContainsKey(row)) layout.Forced[row] = row.IsTicked;
                // Clients mirror the host selection, which already contains the forced modifier.
                if (editable && !row.IsTicked) { row.IsTicked = true; changed = true; }
                row.MouseFilter = Control.MouseFilterEnum.Ignore;
                row.FocusMode = Control.FocusModeEnum.None;
            }
            else if (layout.Forced.Remove(row, out var previous))
            {
                if (editable && row.IsTicked != previous) { row.IsTicked = previous; changed = true; }
                row.MouseFilter = Control.MouseFilterEnum.Stop;
                row.FocusMode = row.IsEnabled ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            }
        }
        return changed;
    }

    internal static bool IsSingleplayer(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)! == MultiplayerUiMode.Singleplayer;

    private static bool IsEditable(NCustomRunModifiersList list) =>
        (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;

    internal static bool IsLocked(NCustomRunModifiersList list, ModifierModel? modifier) =>
        (ModifierGroups.IsSingleplayerDisabled(modifier) && IsSingleplayer(list)) ||
        (BlitzCompatibility.IsDisabled(modifier) && BlitzCompatibility.IsBlitz(list));

    internal static void RemoveLockedModifiers(NCustomRunModifiersList list, List<ModifierModel> modifiers)
    {
        modifiers.RemoveAll(modifier => IsLocked(list, modifier));
        if (!BlitzCompatibility.IsBlitz(list)) return;
        var tickboxes = (List<NRunModifierTickbox>)AccessTools.Field(typeof(NCustomRunModifiersList), "_modifierTickboxes").GetValue(list)!;
        foreach (var forced in tickboxes.Select(row => row.Modifier).OfType<ModifierModel>().Where(BlitzCompatibility.IsForced))
            if (!modifiers.Any(modifier => modifier.GetType() == forced.GetType())) modifiers.Add(forced);
    }

    private static void Toggle(VBoxContainer content, Layout layout, Section section)
    {
        var focus = content.GetViewport().GuiGetFocusOwner();
        if (section.Expanded && focus != null && section.Body.IsAncestorOf(focus)) section.Header.GrabFocus();
        section.Expanded = !section.Expanded;
        section.Body.Visible = section.Expanded;
        section.Arrow.Rotation = section.Expanded ? Mathf.Pi / 2 : 0;
        UpdateFocus(layout);
        Callable.From(() => ResizeContent(content)).CallDeferred();
    }

    private static void ResizeContent(VBoxContainer content)
    {
        if (GodotObject.IsInstanceValid(content))
            content.Size = new Vector2(content.Size.X, content.GetCombinedMinimumSize().Y);
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Layouts.TryGetValue(list, out var layout)) return;
        UpdateFocus(layout);
        ResizeContent(layout.Content);
        Callable.From(() => ResizeContent(layout.Content)).CallDeferred();
    }

    private static void UpdateFocus(Layout layout)
    {
        var controls = layout.Sections.SelectMany(FocusableControls).ToList();
        for (var i = 0; i < controls.Count; i++)
        {
            var previous = controls[(i + controls.Count - 1) % controls.Count];
            var next = controls[(i + 1) % controls.Count];
            controls[i].FocusNeighborTop = controls[i].GetPathTo(previous);
            controls[i].FocusPrevious = controls[i].GetPathTo(previous);
            controls[i].FocusNeighborBottom = controls[i].GetPathTo(next);
            controls[i].FocusNext = controls[i].GetPathTo(next);
        }

    }

    private static IEnumerable<Control> FocusableControls(Section section)
    {
        if (!section.Root.Visible) yield break;
        yield return section.Header;
        if (!section.Expanded || section.Group == ModifierGroup.Disabled) yield break;
        foreach (var child in section.ChildList.GetChildren().OfType<Control>())
        {
            if (!child.Visible) continue;
            if (child is NRunModifierTickbox row && row.FocusMode == Control.FocusModeEnum.All) yield return row;
            foreach (var nested in child.FindChildren("*", "Control", true, false).OfType<NRunModifierTickbox>())
                if (nested.IsVisibleInTree() && nested.FocusMode == Control.FocusModeEnum.All) yield return nested;
            foreach (var slider in child.FindChildren("*", "Range", true, false).OfType<Control>())
                if (slider.FocusMode == Control.FocusModeEnum.All) yield return slider;
            foreach (var button in StartOrderUi.FocusableControls(child)) yield return button;
        }
    }
}

[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList.DefaultFocusedControl), MethodType.Getter)]
internal static class ModifierGroupsFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunModifiersList __instance, ref Control? __result) =>
        __result = ModifierGroupsUi.DefaultFocus(__instance) ?? __result;
}
