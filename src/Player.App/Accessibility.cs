using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace Player.App;

/// <summary>Accessible names. Avalonia names a button after Content.ToString(), so a button whose content is a
/// panel was announced as "Avalonia.Controls.StackPanel". Names are pointed at visible text (UIA LabeledBy) rather
/// than copied, so they follow bindings and a language switch.</summary>
static class Accessibility
{
    /// <summary>Every Button / RadioButton / ToggleButton in the app, including ones created from data templates.</summary>
    public static void Register() =>
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<Button>((button, _) => NameFromContent(button));

    /// <summary>Settings-style rows put the visible label in a TextBlock.label next to the control; every
    /// unnamed input in such a row is labelled by it.</summary>
    public static void LabelRows(ILogical root)
    {
        foreach (var row in root.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("row")))
        {
            if (FirstText(row, t => t.Classes.Contains("label")) is not { } label) continue;
            foreach (var input in row.GetLogicalDescendants().OfType<Control>().Where(IsInput).Where(IsUnnamed))
                AutomationProperties.SetLabeledBy(input, label);
        }
    }

    static void NameFromContent(Button button)
    {
        if (!IsUnnamed(button) || button.Content is null or string || button.Content is not ILogical content) return;
        if (FirstText(content, t => !string.IsNullOrWhiteSpace(t.Text) || t.Inlines?.Count > 0) is { } text)
            AutomationProperties.SetLabeledBy(button, text);
    }

    static TextBlock? FirstText(ILogical root, Func<TextBlock, bool> match) =>
        (root as TextBlock is { } self && match(self) ? self : null)
        ?? root.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(match);

    static bool IsUnnamed(Control c) =>
        string.IsNullOrEmpty(AutomationProperties.GetName(c)) && AutomationProperties.GetLabeledBy(c) is null;

    static bool IsInput(Control c) => c is Button or ToggleButton or RangeBase or TextBox or ComboBox;
}
