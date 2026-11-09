using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Workspace;

// Same search/navigation rules as the original table, with independent prototype localization.
internal sealed class WorkspaceTableSearch
{
    public DockPanel Bar { get; } = new() { Height = 32, Margin = new Thickness(0, 0, 0, 8) };

    public WorkspaceTableSearch(DataGrid table, WorkspaceText text, Func<object, string>? searchableText = null)
    {
        var input = new TextBox { Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var filter = new ToggleButton { Width = 32, Margin = new Thickness(0, 0, 8, 0) };
        input.SetBinding(FrameworkElement.ToolTipProperty, new Binding("[Search]") { Source = text });
        input.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("[Search]") { Source = text });
        var counter = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var previous = new Button { Width = 32 };
        var next = new Button { Width = 32, Margin = new Thickness(4, 0, 0, 0) };
        void SetIcon(ButtonBase button, string key)
        {
            WorkspaceIcons.SetSearchButtonIcon(button, key);
            button.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"[{key}]") { Source = text });
            button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding($"[{key}]") { Source = text });
        }
        SetIcon(filter, "Filter"); SetIcon(previous, "Previous"); SetIcon(next, "Next");
        void FilterTip() => filter.SetBinding(FrameworkElement.ToolTipProperty,
            new Binding(filter.IsChecked == true ? "[FilterActive]" : "[Filter]") { Source = text });
        filter.Checked += (_, _) => FilterTip(); filter.Unchecked += (_, _) => FilterTip();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(counter); actions.Children.Add(previous); actions.Children.Add(next);
        DockPanel.SetDock(filter, Dock.Left); Bar.Children.Add(filter);
        DockPanel.SetDock(actions, Dock.Right); Bar.Children.Add(actions);
        var field = new Grid(); field.Children.Add(input);
        var placeholder = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        placeholder.SetBinding(TextBlock.TextProperty, new Binding("[Search]") { Source = text });
        field.Children.Add(placeholder); Bar.Children.Add(field);
        string Query() => input.Text.Trim();
        bool MatchesLevel(WorkspaceLevel row) => Query().Length > 0 &&
            ((table.Columns.Any(c => c.SortMemberPath == nameof(WorkspaceLevel.ModelElementCount)) ? row.Name : row.NameWithCount)
                .Contains(Query(), StringComparison.CurrentCultureIgnoreCase) ||
             (table.Columns.Any(c => c.SortMemberPath == nameof(WorkspaceLevel.ModelElementCount)) &&
              row.ModelElementCountText.Contains(Query(), StringComparison.CurrentCultureIgnoreCase)) ||
             row.ElevationText.Contains(Query(), StringComparison.CurrentCultureIgnoreCase) ||
             (table.Columns.Any(c => c.SortMemberPath == nameof(WorkspaceLevel.ViewCount)) &&
              row.ViewCount.ToString().Contains(Query(), StringComparison.CurrentCultureIgnoreCase)) ||
             (table.Columns.Any(c => c.SortMemberPath == nameof(WorkspaceLevel.ElementCount)) &&
              row.ElementCount.ToString().Contains(Query(), StringComparison.CurrentCultureIgnoreCase)) ||
             (table.Columns.Any(c => c.SortMemberPath == nameof(WorkspaceLevel.Hidden)) && row.Selected && row.Hidden &&
              text["Hidden"].Contains(Query(), StringComparison.CurrentCultureIgnoreCase)));
        bool Matches(object item) => searchableText is null ? MatchesLevel((WorkspaceLevel)item)
            : Query().Length > 0 && searchableText(item).Contains(Query(), StringComparison.CurrentCultureIgnoreCase);
        List<object> MatchesInOrder() => table.Items.Cast<object>().Where(Matches).ToList();
        void Count()
        {
            var matches = MatchesInOrder();
            int index = table.SelectedItem is object row ? matches.IndexOf(row) : -1;
            counter.Text = Query().Length == 0 ? "—" : index < 0 ? $"{text["Matches"]}: {matches.Count}" : $"{index + 1} / {matches.Count}";
            previous.IsEnabled = next.IsEnabled = matches.Count > 0;
        }
        var view = (ListCollectionView)table.ItemsSource;
        view.Filter = item => filter.IsChecked != true || Query().Length == 0 || Matches(item);
        void Refresh() { view.Refresh(); Count(); }
        void Navigate(int direction)
        {
            var matches = MatchesInOrder(); if (matches.Count == 0) return;
            int current = table.SelectedItem is WorkspaceLevel row ? matches.IndexOf(row) : -1;
            int index = current < 0 ? (direction > 0 ? 0 : matches.Count - 1) : (current + direction + matches.Count) % matches.Count;
            table.SelectedItems.Clear(); table.SelectedItem = matches[index]; table.ScrollIntoView(matches[index]); Count();
        }
        input.TextChanged += (_, _) => { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        filter.Checked += (_, _) => Refresh(); filter.Unchecked += (_, _) => Refresh();
        previous.Click += (_, _) => Navigate(-1); next.Click += (_, _) => Navigate(1);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Navigate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true;
        };
        table.SelectionChanged += (_, _) => Count();
        void RefreshPreservingSelection()
        {
            var selected = table.SelectedItems.Cast<object>().ToList();
            Refresh();
            foreach (var row in selected.Where(table.Items.Contains)) table.SelectedItems.Add(row);
        }
        text.PropertyChanged += (_, _) =>
        {
            if (filter.IsChecked == true && Query().Length > 0) RefreshPreservingSelection();
            else Count();
        };
        bool refreshPending = false;
        foreach (var row in view.SourceCollection.OfType<WorkspaceLevel>()) row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(WorkspaceLevel.ElevationText) or nameof(WorkspaceLevel.Selected) or nameof(WorkspaceLevel.Hidden) or nameof(WorkspaceLevel.ElementCount) or nameof(WorkspaceLevel.NameWithCount) or nameof(WorkspaceLevel.ViewCount)) || refreshPending) return;
            refreshPending = true;
            table.Dispatcher.BeginInvoke(new Action(() =>
            {
                refreshPending = false;
                if (filter.IsChecked == true && Query().Length > 0) RefreshPreservingSelection();
                else Count();
            }));
        };
        ((System.Collections.Specialized.INotifyCollectionChanged)view).CollectionChanged += (_, _) => Count();
        Count();
    }
}
