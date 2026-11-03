using System.Windows;
using System.Windows.Controls;

namespace ReLevel.Revit.UI;

internal static class StablePageLayout
{

    public static Grid Create(FrameworkElement options, FrameworkElement table, FrameworkElement actions, FrameworkElement status)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(table);
        Grid.SetRow(options, 1); grid.Children.Add(options);
        var footer = Footer(actions, status);
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        return grid;
    }

    public static Grid Footer(FrameworkElement actions, FrameworkElement status)
    {
        actions.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto, MinHeight = 24 });
        var actionScroller = new ScrollViewer { Content = actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        grid.Children.Add(actionScroller);
        var messages = new ScrollViewer { Content = status, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(messages, 1); grid.Children.Add(messages);
        return grid;
    }

}
