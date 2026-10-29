using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Data;
using ReLevel.Revit.UI.Prototype;
using System.Windows.Media;

namespace ReLevel.Revit.UI;

// Layout-only code, shared by the working page and standalone WPF visual checks.
internal static class TransferLayout
{
    public static Grid Selectors(SearchableComboBox source, SearchableComboBox target, string sourceLabel, string targetLabel)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(8) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var choices = new[] { source, target };
        var captions = new[] { sourceLabel, targetLabel };
        for (int i = 0; i < choices.Length; i++)
        {
            var caption = new TextBlock { Text = captions[i], FontSize = 13, Foreground = Brushes.SlateGray,
                TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(caption, i * 2); grid.Children.Add(caption);
            var choice = choices[i];
            choice.MinWidth = 0; choice.Height = 36; choice.Margin = new Thickness(0);
            choice.VerticalContentAlignment = VerticalAlignment.Center;
            choice.Style = LevelSelectorStyle();
            System.Windows.Automation.AutomationProperties.SetName(choice, captions[i]);
            Grid.SetColumn(choice, i * 2); Grid.SetRow(choice, 2); grid.Children.Add(choice);
        }
        return grid;
    }

    public static Grid Workspace(FrameworkElement selectors, TabControl tabs, FrameworkElement actions,
        FrameworkElement progress, FrameworkElement summary)
    {
        var grid = new Grid();
        for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new()
            { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        tabs.BorderThickness = new Thickness(0); tabs.Background = Brushes.Transparent; tabs.Padding = new Thickness(0);
        foreach (TabItem tab in tabs.Items) { tab.Padding = new Thickness(12, 4, 12, 4); tab.FontSize = 13; }
        var children = new[] { selectors, tabs, actions, progress, summary };
        for (int i = 0; i < children.Length; i++) { Grid.SetRow(children[i], i); grid.Children.Add(children[i]); }
        return grid;
    }

    public static void StyleTableAppearance(DataGrid table)
    {
        table.ColumnHeaderStyle = PrototypeTableHeader.CreateStyle();
        table.HeadersVisibility = DataGridHeadersVisibility.Column;
        table.GridLinesVisibility = DataGridGridLinesVisibility.All;
        table.VerticalGridLinesBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(213, 227, 231));
        table.HorizontalGridLinesBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(233, 238, 242));
        table.Background = table.RowBackground = Brushes.White;
        table.AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 247, 248));
        table.BorderBrush = table.VerticalGridLinesBrush;
        table.MinRowHeight = 36;
        table.CanUserReorderColumns = false;
    }

    public static void StyleTable(DataGrid table)
    {
        StyleTableAppearance(table);
        table.Columns[0].HeaderStyle = table.ColumnHeaderStyle;
        table.Columns[0].Width = 48;
        foreach (var column in table.Columns.OfType<DataGridTextColumn>())
        {
            var caption = new FrameworkElementFactory(typeof(TextBlock));
            caption.SetBinding(TextBlock.TextProperty, new Binding());
            caption.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            column.HeaderTemplate = new DataTemplate { VisualTree = caption };
            var property = ((Binding)column.Binding).Path.Path;
            column.SortMemberPath = property;
            column.MinWidth = property == nameof(TableRow.Id) ? 65 : 85;
            column.Width = property == nameof(TableRow.Id) ? new DataGridLength(75)
                : new DataGridLength(property == nameof(TableRow.Status) ? 1.7 : 1, DataGridLengthUnitType.Star);
            var style = new Style(typeof(TextBlock), column.ElementStyle);
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 7, 8, 7)));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            column.ElementStyle = style;
        }
    }

    public static WrapPanel Actions() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, 12, 0, 0)
    };

    // An explicit editable template avoids theme-specific gaps in the outline and text alignment.
    private static Style LevelSelectorStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Background" Value="White"/>
          <Setter Property="BorderBrush" Value="#BBCBD2"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ComboBox">
                <Grid>
                  <Border x:Name="Outline" Background="{TemplateBinding Background}"
                          BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="28"/></Grid.ColumnDefinitions>
                      <TextBox x:Name="PART_EditableTextBox" Margin="8,0,0,0" Padding="0" BorderThickness="0"
                               Background="Transparent" VerticalContentAlignment="Center"
                               IsReadOnly="{TemplateBinding IsReadOnly}"/>
                      <ToggleButton Grid.Column="1" Focusable="False" ClickMode="Press"
                                    IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                        <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                          <Border Background="Transparent"><Path Data="M0,0 L4,4 L8,0" Stroke="#657788"
                            StrokeThickness="1.2" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
                        </ControlTemplate></ToggleButton.Template>
                      </ToggleButton>
                    </Grid>
                  </Border>
                  <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True"
                         IsOpen="{TemplateBinding IsDropDownOpen}" Focusable="False">
                    <Border Background="White" BorderBrush="#BBCBD2" BorderThickness="1"
                            MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                            MaxHeight="{TemplateBinding MaxDropDownHeight}">
                      <ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                    </Border>
                  </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsKeyboardFocusWithin" Value="True">
                    <Setter TargetName="Outline" Property="BorderBrush" Value="#157D88"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}
