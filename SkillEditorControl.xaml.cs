using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CrossingVoidZDTool;

public sealed partial class SkillEditorControl : UserControl
{
    public event EventHandler<object>? DeleteRequested;
    public event EventHandler<object>? IconSelectionRequested;
    public event EventHandler? FocusReleaseRequested;
    public event EventHandler<SkillEditorEditEventArgs>? Edited;
    private bool _isApplyingEditorValues;
    private bool _isEditorReady;

    public SkillEditorControl()
    {
        InitializeComponent();
    }

    public void ApplyEditorValuesToModel()
    {
        _isApplyingEditorValues = true;
        try
        {
            if (DataContext is CharacterSkillEntry entry)
            {
                entry.PositionName = PositionNameTextBox.Text;
                entry.TrueName = TrueNameTextBox.Text;
                entry.PtCost = PtCostTextBox.Text;
                entry.AttackCapacity = AttackCapacityTextBox.Text;
                entry.Description = DescriptionTextBox.Text;
                entry.SkillState = SkillStateComboBox.SelectedValue as string ?? string.Empty;
                entry.GuardState = GuardStateComboBox.SelectedValue as string ?? string.Empty;
                entry.GuardValue = GuardValueTextBox.Text;
                entry.AutoPriority = AutoPriorityTextBox.Text;
            }

            foreach (var descendant in EnumerateDescendants(this))
            {
                switch (descendant)
                {
                    case TextBox { DataContext: SkillMultiplierLevel } textBox:
                        ApplyTextBoxValue(textBox, out _);
                        break;
                }
            }
        }
        finally
        {
            _isApplyingEditorValues = false;
        }
    }

    private void SkillEditorControl_Loaded(object sender, RoutedEventArgs e)
    {
        _isEditorReady = true;
    }

    private void SkillEditorControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        _isEditorReady = false;
        DispatcherQueue.TryEnqueue(() => _isEditorReady = true);
    }

    private void DeleteStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterSkillEntry entry)
        {
            DeleteRequested?.Invoke(this, entry);
        }
    }

    private void IconToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterSkillEntry entry)
        {
            IconSelectionRequested?.Invoke(this, entry);
        }
    }

    private void IconButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (DataContext is CharacterSkillEntry entry)
        {
            entry.IsExpanded = !entry.IsExpanded;
            e.Handled = true;
        }
    }

    private void SkillEditorCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        FocusReleaseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SkillEditorCard_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        FocusReleaseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SkillEditorInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        FocusReleaseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SkillEditorField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ApplyTextBoxValue(textBox, out var edit);
            RaiseEdited(edit);
            return;
        }
    }

    private void SkillEditorField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyComboBoxValue(comboBox, out var edit);
            RaiseEdited(edit);
            return;
        }
    }

    private void RaiseEdited(SkillEditorEditEventArgs? edit)
    {
        if (_isEditorReady && !_isApplyingEditorValues && edit is not null)
        {
            Edited?.Invoke(this, edit);
        }
    }

    private static void ApplyTextBoxValue(TextBox textBox, out SkillEditorEditEventArgs? edit)
    {
        edit = null;
        if (textBox.Tag is not string fieldName)
        {
            return;
        }

        if (textBox.DataContext is SkillMultiplierLevel level)
        {
            ApplyMultiplierText(level, fieldName, textBox.Text);
            edit = new SkillEditorEditEventArgs(fieldName, textBox.Text, level.Level);
            return;
        }

        if (textBox.DataContext is CharacterSkillEntry entry)
        {
            ApplySkillText(entry, fieldName, textBox.Text);
            edit = new SkillEditorEditEventArgs(fieldName, textBox.Text);
            return;
        }

        if (FindOwningSkillEntry(textBox) is CharacterSkillEntry owningEntry)
        {
            ApplySkillText(owningEntry, fieldName, textBox.Text);
            edit = new SkillEditorEditEventArgs(fieldName, textBox.Text);
        }
    }

    private static void ApplyComboBoxValue(ComboBox comboBox, out SkillEditorEditEventArgs? edit)
    {
        edit = null;
        if (comboBox.Tag is not string fieldName)
        {
            return;
        }

        var entry = comboBox.DataContext as CharacterSkillEntry ?? FindOwningSkillEntry(comboBox);
        if (entry is null)
        {
            return;
        }

        var value = comboBox.SelectedValue as string ?? string.Empty;
        switch (fieldName)
        {
            case nameof(CharacterSkillEntry.SkillState):
                entry.SkillState = value;
                edit = new SkillEditorEditEventArgs(fieldName, value);
                break;
            case nameof(CharacterSkillEntry.GuardState):
                entry.GuardState = value;
                edit = new SkillEditorEditEventArgs(fieldName, value);
                break;
        }
    }

    private static void ApplySkillText(CharacterSkillEntry entry, string fieldName, string value)
    {
        switch (fieldName)
        {
            case nameof(CharacterSkillEntry.PositionName):
                entry.PositionName = value;
                break;
            case nameof(CharacterSkillEntry.TrueName):
                entry.TrueName = value;
                break;
            case nameof(CharacterSkillEntry.PtCost):
                entry.PtCost = value;
                break;
            case nameof(CharacterSkillEntry.AttackCapacity):
                entry.AttackCapacity = value;
                break;
            case nameof(CharacterSkillEntry.Description):
                entry.Description = value;
                break;
            case nameof(CharacterSkillEntry.GuardValue):
                entry.GuardValue = value;
                break;
            case nameof(CharacterSkillEntry.AutoPriority):
                entry.AutoPriority = value;
                break;
        }
    }

    private static void ApplyMultiplierText(SkillMultiplierLevel level, string fieldName, string value)
    {
        switch (fieldName)
        {
            case nameof(SkillMultiplierLevel.PhysicalMultiplier):
                level.PhysicalMultiplier = value;
                break;
            case nameof(SkillMultiplierLevel.EnergyMultiplier):
                level.EnergyMultiplier = value;
                break;
        }
    }

    private static bool IsInteractiveSource(object originalSource)
    {
        if (originalSource is not DependencyObject source)
        {
            return false;
        }

        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or ComboBox or ButtonBase or ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static CharacterSkillEntry? FindOwningSkillEntry(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: CharacterSkillEntry entry })
            {
                return entry;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
