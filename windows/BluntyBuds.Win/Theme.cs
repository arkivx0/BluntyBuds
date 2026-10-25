using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace BluntyBuds.Win;

/// <summary>Dark theme: implicit styles for every control used, plus an immersive dark title bar.</summary>
public static class Theme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public static void DarkTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle; int on = 1;
            if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); // 19 = pre-20H1
            int caption = 0x00120F0E; // Windows 11: title bar in the app background colour (#0E0F12, COLORREF is BGR); ignored on Windows 10
            DwmSetWindowAttribute(h, 35, ref caption, 4);
        }
        catch { }
    }

    public static ResourceDictionary Load() => (ResourceDictionary)XamlReader.Parse(Xaml);

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="BgC">#0E0F12</Color>
  <SolidColorBrush x:Key="Bg" Color="#0E0F12"/>
  <SolidColorBrush x:Key="Card" Color="#17191E"/>
  <SolidColorBrush x:Key="Ctl" Color="#23262D"/>
  <SolidColorBrush x:Key="CtlHover" Color="#30343E"/>
  <SolidColorBrush x:Key="Line" Color="#353944"/>
  <SolidColorBrush x:Key="Fg" Color="#E6E8EE"/>
  <SolidColorBrush x:Key="Dim" Color="#8C93A3"/>
  <SolidColorBrush x:Key="Accent" Color="#3DD6B5"/>
  <SolidColorBrush x:Key="AccentFg" Color="#0B1A17"/>
  <SolidColorBrush x:Key="AccentDim" Color="#1F6B5B"/>

  <Style TargetType="Window">
    <Setter Property="Background" Value="{StaticResource Bg}"/>
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
  </Style>
  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{StaticResource Fg}"/></Style>
  <Style TargetType="ToolTip">
    <Setter Property="Background" Value="{StaticResource Ctl}"/><Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
  </Style>

  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Background" Value="{StaticResource Ctl}"/>
    <Setter Property="Padding" Value="10,4"/>
    <Setter Property="Margin" Value="0,0,6,0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}"
                  BorderBrush="{StaticResource Line}" BorderThickness="1">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="AccentButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{StaticResource Accent}"/>
    <Setter Property="Foreground" Value="{StaticResource AccentFg}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
  </Style>
  <Style x:Key="SmallButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Padding" Value="7,2"/><Setter Property="FontSize" Value="11"/>
  </Style>

  <!-- borderless icon / text button -->
  <Style x:Key="FlatButton" TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Padding" Value="8,0"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <!-- bottom-row tool: icon over a small label, on a surface tile -->
  <Style x:Key="ToolButton" TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Height" Value="56"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{StaticResource Card}" CornerRadius="12" Padding="0,8">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource Ctl}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <!-- Sound Profile chips -->
  <Style x:Key="Chip" TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Background" Value="{StaticResource Ctl}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Height" Value="32"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Margin" Value="0,0,8,0"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="16" Padding="12,0">
            <ContentPresenter VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ChipOn" TargetType="Button" BasedOn="{StaticResource Chip}">
    <Setter Property="Foreground" Value="{StaticResource AccentFg}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border Background="{StaticResource Accent}" CornerRadius="16" Padding="12,0">
            <ContentPresenter VerticalAlignment="Center"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- segmented toggle (RadioButton) -->
  <Style x:Key="Segment" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="b" Background="Transparent" CornerRadius="6" Padding="12,0" Margin="1">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Accent}"/>
              <Setter Property="Foreground" Value="{StaticResource AccentFg}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- switch-style toggle (CheckBox with key) -->
  <Style x:Key="Switch" TargetType="CheckBox">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <ContentPresenter VerticalAlignment="Center" Margin="0,0,8,0"/>
            <Border x:Name="track" Width="36" Height="20" CornerRadius="10" Background="{StaticResource Ctl}" BorderBrush="{StaticResource Line}" BorderThickness="1">
              <Ellipse x:Name="knob" Width="14" Height="14" Fill="{StaticResource Dim}" HorizontalAlignment="Left" Margin="2,0"/>
            </Border>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="track" Property="Background" Value="{StaticResource Accent}"/>
              <Setter TargetName="knob" Property="Fill" Value="{StaticResource AccentFg}"/>
              <Setter TargetName="knob" Property="HorizontalAlignment" Value="Right"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Border x:Name="box" Width="16" Height="16" CornerRadius="3" Background="{StaticResource Ctl}" BorderBrush="{StaticResource Line}" BorderThickness="1" VerticalAlignment="Center">
              <Path x:Name="tick" Data="M3,8 L7,12 L13,4" Stroke="{StaticResource AccentFg}" StrokeThickness="2" Visibility="Collapsed"/>
            </Border>
            <ContentPresenter VerticalAlignment="Center" Margin="6,0,0,0"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="box" Property="Background" Value="{StaticResource Accent}"/>
              <Setter TargetName="tick" Property="Visibility" Value="Visible"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="RadioButton">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Grid Width="16" Height="16" VerticalAlignment="Center">
              <Ellipse Fill="{StaticResource Ctl}" Stroke="{StaticResource Line}"/>
              <Ellipse x:Name="dot" Width="8" Height="8" Fill="{StaticResource Accent}" Visibility="Collapsed"/>
            </Grid>
            <ContentPresenter VerticalAlignment="Center" Margin="6,0,0,0"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True"><Setter TargetName="dot" Property="Visibility" Value="Visible"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Slider -->
  <Style x:Key="SliderRepeat" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="Template">
      <Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="SliderThumb" TargetType="Thumb">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <Ellipse x:Name="e" Width="14" Height="14" Fill="{StaticResource Accent}" Stroke="{StaticResource Bg}" StrokeThickness="2"/>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="e" Property="Fill" Value="#6BE6CB"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <ControlTemplate x:Key="HSlider" TargetType="Slider">
    <Grid Background="Transparent" MinHeight="18">
      <Border Height="4" CornerRadius="2" Background="{StaticResource Ctl}" VerticalAlignment="Center"/>
      <Track x:Name="PART_Track">
        <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style="{StaticResource SliderThumb}"/></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <ControlTemplate x:Key="VSlider" TargetType="Slider">
    <Grid Background="Transparent" MinWidth="18">
      <Border Width="4" CornerRadius="2" Background="{StaticResource Ctl}" HorizontalAlignment="Center"/>
      <Border Width="10" Height="1" Background="{StaticResource Dim}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      <Track x:Name="PART_Track">
        <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style="{StaticResource SliderThumb}"/></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <Style TargetType="Slider">
    <Setter Property="Template" Value="{StaticResource HSlider}"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Vertical"><Setter Property="Template" Value="{StaticResource VSlider}"/></Trigger>
    </Style.Triggers>
  </Style>

  <!-- ComboBox -->
  <ControlTemplate x:Key="ComboToggle" TargetType="ToggleButton">
    <Border x:Name="b" Background="{StaticResource Ctl}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="6">
      <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0" Data="M0,0 L4,4 L8,0" Stroke="{StaticResource Dim}" StrokeThickness="1.5"/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
      <Trigger Property="IsChecked" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="MinHeight" Value="26"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Template="{StaticResource ComboToggle}" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
            <ContentPresenter IsHitTestVisible="False" Margin="9,3,24,3" VerticalAlignment="Center" HorizontalAlignment="Left"
                              Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
            <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
              <Border Background="{StaticResource Card}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="6"
                      MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Margin="0,2,0,0">
                <ScrollViewer Margin="2"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="b" Background="Transparent" Padding="8,4" CornerRadius="4"><ContentPresenter/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource CtlHover}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="{StaticResource Accent}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="TextBox">
    <Setter Property="Background" Value="{StaticResource Ctl}"/><Setter Property="Foreground" Value="{StaticResource Fg}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/><Setter Property="CaretBrush" Value="{StaticResource Fg}"/>
    <Setter Property="Padding" Value="4,3"/>
  </Style>

  <!-- ScrollBar -->
  <Style x:Key="ScrollThumb" TargetType="Thumb">
    <Setter Property="Template">
      <Setter.Value><ControlTemplate TargetType="Thumb"><Border CornerRadius="4" Background="{StaticResource Line}"/></ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="ScrollBar">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Width" Value="8"/><Setter Property="MinWidth" Value="8"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Track x:Name="PART_Track" IsDirectionReversed="True">
            <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="ScrollBar.PageUpCommand"/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="ScrollBar.PageDownCommand"/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/><Setter Property="MinWidth" Value="0"/>
        <Setter Property="Height" Value="8"/><Setter Property="MinHeight" Value="8"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Track x:Name="PART_Track">
                <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="ScrollBar.PageLeftCommand"/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource SliderRepeat}" Command="ScrollBar.PageRightCommand"/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>
""";
}
