using System.Windows;
using System.Windows.Markup;

namespace ReLevel.Revit.UI.Prototype;

internal static class PrototypeTableHeader
{
    // Explicit glyphs keep sorting visible regardless of the Windows/Revit theme.
    // Preserve the named grippers used by DataGridColumnHeader for column resizing.
    public static Style CreateStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="DataGridColumnHeader">
          <Setter Property="Background" Value="#D1E9EC"/>
          <Setter Property="Foreground" Value="#183247"/>
          <Setter Property="BorderBrush" Value="#ACCBD0"/>
          <Setter Property="BorderThickness" Value="0,0,1,1"/>
          <Setter Property="Padding" Value="6,12"/>
          <Setter Property="FontSize" Value="12"/>
          <Setter Property="FontWeight" Value="SemiBold"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="DataGridColumnHeader">
                <Grid>
                  <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                    <DockPanel>
                      <TextBlock x:Name="SortArrow" DockPanel.Dock="Right" Width="9" Margin="3,0,0,0"
                                 VerticalAlignment="Center" Foreground="#157D88" FontSize="10"
                                 Visibility="Collapsed" IsHitTestVisible="False"/>
                      <ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True"/>
                    </DockPanel>
                  </Border>
                  <Thumb x:Name="PART_LeftHeaderGripper" Width="5" HorizontalAlignment="Left" Cursor="SizeWE">
                    <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate></Thumb.Template>
                  </Thumb>
                  <Thumb x:Name="PART_RightHeaderGripper" Width="5" HorizontalAlignment="Right" Cursor="SizeWE">
                    <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate></Thumb.Template>
                  </Thumb>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="SortDirection" Value="Ascending">
                    <Setter TargetName="SortArrow" Property="Text" Value="▲"/>
                    <Setter TargetName="SortArrow" Property="Visibility" Value="Visible"/>
                  </Trigger>
                  <Trigger Property="SortDirection" Value="Descending">
                    <Setter TargetName="SortArrow" Property="Text" Value="▼"/>
                    <Setter TargetName="SortArrow" Property="Visibility" Value="Visible"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}
