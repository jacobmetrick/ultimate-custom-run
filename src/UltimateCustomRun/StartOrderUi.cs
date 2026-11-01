using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace UltimateCustomRun;

internal static class StartOrderUi
{
    private sealed class Row
    {
        internal required HBoxContainer Root;
        internal required Label Position;
        internal required Label Title;
        internal required NButton Up;
        internal required NButton Down;
        internal Type? Key;
    }
    private sealed class Panel
    {
        internal required VBoxContainer Root;
        internal required Label Heading;
        internal required VBoxContainer RowList;
        internal List<Row> Rows = [];
    }
    private static readonly ConditionalWeakTable<NCustomRunModifiersList, Panel> Panels = new();
    private static readonly Color TextColor = new(1, 0.9647059f, 0.8862745f);
    private static readonly Color ButtonColor = Color.FromHtml("36566b");
    private static readonly Color ButtonFocusColor = Color.FromHtml("496f83");

    internal static Control Create(NCustomRunModifiersList list)
    {
        var root = new VBoxContainer { Name = "StartOrder", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
        root.AddThemeConstantOverride("separation", 4);
        var heading = new Label { VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_bold_shared.tres"));
        heading.AddThemeFontSizeOverride("font_size", 24);
        heading.AddThemeColorOverride("font_color", TextColor);
        var headingRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 48) };
        headingRow.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        headingRow.AddChild(heading);
        root.AddChild(headingRow);
        var rowList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rowList.AddThemeConstantOverride("separation", 4);
        root.AddChild(rowList);
        Panels.Add(list, new Panel { Root = root, Heading = heading, RowList = rowList });
        return root;
    }

    internal static void Refresh(NCustomRunModifiersList list)
    {
        if (!GodotObject.IsInstanceValid(list) || !Panels.TryGetValue(list, out var panel)) return;
        var selected = list.GetModifiersTickedOn().Where(StartOrder.IsOrderable).ToList();
        var editable = (MultiplayerUiMode)AccessTools.Field(typeof(NCustomRunModifiersList), "_mode").GetValue(list)!
            is MultiplayerUiMode.Singleplayer or MultiplayerUiMode.Host;
        var russian = LocManager.Instance.CultureInfo.TwoLetterISOLanguageName == "ru";
        panel.Heading.Text = russian ? "Порядок стартовых бонусов" : "Start order";
        // A single start bonus has nothing to reorder.
        panel.Root.Visible = selected.Count > 1;
        while (panel.Rows.Count < selected.Count) panel.Rows.Add(CreateRow(list, panel));
        for (var i = 0; i < panel.Rows.Count; i++)
        {
            var row = panel.Rows[i];
            row.Root.Visible = i < selected.Count;
            if (!row.Root.Visible) { row.Key = null; continue; }
            row.Key = StartOrder.KeyOf(selected[i]);
            row.Position.Text = $"{i + 1}.";
            row.Title.Text = Regex.Replace(selected[i].Title.GetFormattedText(), @"\[[^\]]*\]", string.Empty);
            SetEnabled(row.Up, editable, i > 0);
            SetEnabled(row.Down, editable, i < selected.Count - 1);
            row.Up.FocusNeighborRight = row.Up.GetPathTo(row.Down);
            row.Down.FocusNeighborLeft = row.Down.GetPathTo(row.Up);
        }
    }

    // Lets the section's top-to-bottom focus chain include the arrow buttons.
    internal static IEnumerable<Control> FocusableControls(Control child)
    {
        if (child.Name != "StartOrder") yield break;
        foreach (var button in child.FindChildren("*", "Control", true, false).OfType<NButton>())
            if (button.IsVisibleInTree() && button.FocusMode == Control.FocusModeEnum.All) yield return button;
    }

    private static void SetEnabled(NButton button, bool editable, bool canMove)
    {
        // Clients only see the host's order.
        button.Visible = editable;
        button.FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        button.MouseFilter = editable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        button.Modulate = new Color(1, 1, 1, canMove ? 1 : 0.35f);
    }

    private static Row CreateRow(NCustomRunModifiersList list, Panel panel)
    {
        var root = new HBoxContainer { CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 12);
        root.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var position = CreateLabel(48);
        var title = CreateLabel(0);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.ClipText = true;
        var row = new Row
        {
            Root = root, Position = position, Title = title,
            Up = CreateArrowButton("MoveStartBonusUp", -Mathf.Pi / 2),
            Down = CreateArrowButton("MoveStartBonusDown", Mathf.Pi / 2)
        };
        root.AddChild(position);
        root.AddChild(title);
        root.AddChild(row.Up);
        root.AddChild(row.Down);
        panel.RowList.AddChild(root);
        row.Up.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Move(list, panel, row, -1)));
        row.Down.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Move(list, panel, row, 1)));
        return row;
    }

    private static void Move(NCustomRunModifiersList list, Panel panel, Row row, int delta)
    {
        if (row.Key is not { } key || !StartOrder.Move(StartOrder.For(list), key, delta)) return;
        ModifierValueUi.Refresh(list);
        // The lobby (and any clients) take the new order from GetModifiersTickedOn.
        list.EmitSignal(NCustomRunModifiersList.SignalName.ModifiersChanged);
        // Follow the moved bonus so repeated presses keep moving it.
        var moved = panel.Rows.FirstOrDefault(candidate => candidate.Key == key);
        var button = moved == null ? null : delta < 0 ? moved.Up : moved.Down;
        if (button != null) Callable.From(button.GrabFocus).CallDeferred();
    }

    private static Label CreateLabel(float width)
    {
        var label = new Label
        {
            CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>("res://themes/kreon_regular_shared.tres"));
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", TextColor);
        return label;
    }

    private static NButton CreateArrowButton(string name, float rotation)
    {
        var button = new NButton
        {
            Name = name, CustomMinimumSize = new Vector2(48, 48), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop
        };
        var background = new Godot.Panel { MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None };
        var style = new StyleBoxFlat
        {
            BgColor = ButtonColor, BorderColor = Color.FromHtml("a6bdc8"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        background.AddThemeStyleboxOverride("panel", style);
        button.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var arrow = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>("res://images/packed/common_ui/settings_tiny_right_arrow.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = rotation, PivotOffset = new Vector2(14, 14)
        };
        button.AddChild(arrow);
        arrow.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        arrow.OffsetLeft = -14; arrow.OffsetTop = -14; arrow.OffsetRight = 14; arrow.OffsetBottom = 14;
        button.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ => style.BgColor = ButtonFocusColor));
        button.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ => style.BgColor = ButtonColor));
        button.MouseEntered += () => style.BgColor = ButtonFocusColor;
        button.MouseExited += () => style.BgColor = button.HasFocus() ? ButtonFocusColor : ButtonColor;
        return button;
    }
}
