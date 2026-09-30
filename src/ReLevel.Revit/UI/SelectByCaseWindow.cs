using System.Windows;
using System.Windows.Controls;
using ReLevel.Revit.Transfer;

namespace ReLevel.Revit.UI;

internal sealed class SelectByCaseWindow : Window
{
    private readonly ComboBox cases = new() { DisplayMemberPath = nameof(TransferCase.Name), MinWidth = 440 };
    public TransferCase SelectedCase => (TransferCase)cases.SelectedItem;

    public SelectByCaseWindow(FrameworkElement owner)
    {
        DialogOwner.Attach(this, owner); Title = L.Get("Выделить все элементы по"); Width = 610;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = L.Get("Кейс"), Margin = new Thickness(0, 0, 0, 8) });
        cases.ItemsSource = TransferCases.All;
        panel.Children.Add(cases);
        panel.Children.Add(new TextBlock
        {
            Text = L.Get("Выделение во всей текущей модели, на всех уровнях. Уровень, поиск и отметки таблицы не ограничивают выбор. Подходящие кейсу экземпляры с параметрами только для чтения тоже включаются."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var select = new Button { Content = L.Get("Выделить"), IsDefault = true, IsEnabled = false,
            Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
        cases.SelectionChanged += (_, _) => select.IsEnabled = cases.SelectedItem is TransferCase;
        select.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(select);
        buttons.Children.Add(new Button { Content = L.Get("Отмена"), IsCancel = true,
            Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) });
        panel.Children.Add(buttons); Content = panel;
        if (TransferCases.All.Count > 0) cases.SelectedIndex = 0;
    }
}
