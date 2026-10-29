using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace ReLevel.Revit.UI.Prototype;

internal static class PrototypeTableChecks
{
    public static void Add(DataGrid table, IReadOnlyList<PrototypeLevel> levels, PrototypeText text,
        string key, string property, double width, string? enabledProperty)
    {
        var value = typeof(PrototypeLevel).GetProperty(property)!;
        var enabled = enabledProperty is null ? null : typeof(PrototypeLevel).GetProperty(enabledProperty);
        bool Available(PrototypeLevel row) => enabled is null || (bool)enabled.GetValue(row)!;
        bool Checked(PrototypeLevel row) => (bool)value.GetValue(row)!;
        var header = new CheckBox { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        header.SetBinding(FrameworkElement.ToolTipProperty, new Binding("[SelectAll]") { Source = text });
        header.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("[SelectAll]") { Source = text });
        if (key is "Selected" or "Keep") header.HorizontalAlignment = HorizontalAlignment.Center;
        else
        {
            header.SetBinding(ContentControl.ContentProperty, new Binding($"[{key}]") { Source = text });
            header.ContentTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };
            header.ContentTemplate.VisualTree.SetBinding(TextBlock.TextProperty, new Binding());
            header.ContentTemplate.VisualTree.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        }
        bool updating = false;
        void Update()
        {
            if (updating) return;
            var rows = table.Items.OfType<PrototypeLevel>().Where(Available).ToList();
            header.IsEnabled = rows.Count > 0;
            header.IsChecked = rows.Count == 0 || rows.All(r => !Checked(r)) ? false : rows.All(Checked) ? true : null;
        }
        void Set(IEnumerable<PrototypeLevel> rows, bool state)
        {
            updating = true;
            try { foreach (var row in rows.Where(Available).ToList()) value.SetValue(row, state); }
            finally { updating = false; }
            Update();
        }
        void Toggle(PrototypeLevel row)
        {
            Set(table.SelectedItems.Contains(row) ? table.SelectedItems.Cast<PrototypeLevel>() : new[] { row }, !Checked(row));
        }
        header.Click += (_, e) =>
        {
            var rows = table.Items.OfType<PrototypeLevel>().Where(Available).ToList();
            Set(rows, !rows.All(Checked)); e.Handled = true;
        };
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(property) { Mode = BindingMode.OneWay });
        check.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding($"[{key}]") { Source = text });
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (enabledProperty is not null) check.SetBinding(UIElement.IsEnabledProperty, new Binding(enabledProperty));
        check.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((sender, e) =>
        {
            if (((CheckBox)sender).DataContext is not PrototypeLevel row) return;
            e.Handled = true; Toggle(row);
        }));
        check.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((sender, e) =>
        {
            if (e.Key != Key.Space || ((CheckBox)sender).DataContext is not PrototypeLevel row) return;
            e.Handled = true; if (!e.IsRepeat) Toggle(row);
        }));
        check.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (((CheckBox)sender).DataContext is PrototypeLevel row) Toggle(row);
            e.Handled = true;
        }));
        table.Columns.Add(new DataGridTemplateColumn { Header = header, Width = width, SortMemberPath = property, CanUserSort = true,
            CellTemplate = new DataTemplate { VisualTree = check } });
        foreach (var row in levels) row.PropertyChanged += (_, _) => Update();
        ((System.Collections.Specialized.INotifyCollectionChanged)table.Items).CollectionChanged += (_, _) => Update();
        Update();
    }
}
