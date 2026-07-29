using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace NuoMiDesktopPet
{
    /// <summary>
    /// Applies the warm, flat visual language used by NuoMi to a WPF
    /// ContextMenu.  Keeping the templates here avoids mixing presentation
    /// markup into the pet behaviour code.
    /// </summary>
    internal static class FlatContextMenuStyle
    {
        private const string ContextMenuStyleKey =
            "NuoMi.FlatContextMenu";
        private const string MenuItemStyleKey =
            "NuoMi.FlatMenuItem";
        private const string SeparatorStyleKey =
            "NuoMi.FlatMenuSeparator";

        public static void Apply(ContextMenu menu)
        {
            if (menu == null)
            {
                throw new ArgumentNullException("menu");
            }

            ResourceDictionary resources = CreateResources();
            menu.Resources.MergedDictionaries.Add(resources);

            Style contextMenuStyle =
                (Style)resources[ContextMenuStyleKey];
            Style menuItemStyle =
                (Style)resources[MenuItemStyleKey];
            Style separatorStyle =
                (Style)resources[SeparatorStyleKey];

            menu.Style = contextMenuStyle;
            ApplyItemStyles(menu, menuItemStyle, separatorStyle);

            // The current menu is static, but styling again when it opens
            // also covers menu entries that may be added by a later feature.
            menu.Opened += delegate
            {
                PrepareForOpening(menu, menu);
                ApplyItemStyles(
                    menu,
                    menuItemStyle,
                    separatorStyle);
            };
        }

        public static void PrepareForOpening(
            ContextMenu menu,
            Visual dpiSource)
        {
            if (menu == null)
            {
                throw new ArgumentNullException("menu");
            }

            double dipPerPixel = 1.0;
            if (dpiSource != null)
            {
                PresentationSource source =
                    PresentationSource.FromVisual(dpiSource);
                if (source != null &&
                    source.CompositionTarget != null)
                {
                    Matrix transform =
                        source.CompositionTarget
                            .TransformFromDevice;
                    if (!Double.IsNaN(transform.M22) &&
                        !Double.IsInfinity(transform.M22) &&
                        transform.M22 > 0.0)
                    {
                        dipPerPixel = transform.M22;
                    }
                }
            }

            Forms.Screen screen =
                Forms.Screen.FromPoint(
                    Forms.Cursor.Position);
            double workingHeight =
                screen.WorkingArea.Height *
                dipPerPixel;
            double safeMargin =
                Math.Min(
                    24.0,
                    Math.Max(
                        4.0,
                        workingHeight * 0.06));
            double rootMaximumHeight =
                Math.Max(
                    1.0,
                    workingHeight - safeMargin);
            menu.MaxHeight = rootMaximumHeight;
            ApplySubmenuHeightLimit(
                menu,
                Math.Max(
                    1.0,
                    rootMaximumHeight - 18.0));
        }

        private static void ApplySubmenuHeightLimit(
            ItemsControl parent,
            double maximumHeight)
        {
            foreach (object child in parent.Items)
            {
                MenuItem menuItem = child as MenuItem;
                if (menuItem == null)
                {
                    continue;
                }

                menuItem.MaxHeight = maximumHeight;
                ApplySubmenuHeightLimit(
                    menuItem,
                    maximumHeight);
            }
        }

        private static void ApplyItemStyles(
            ItemsControl parent,
            Style menuItemStyle,
            Style separatorStyle)
        {
            foreach (object child in parent.Items)
            {
                MenuItem menuItem = child as MenuItem;
                if (menuItem != null)
                {
                    menuItem.Style = menuItemStyle;
                    ApplyItemStyles(
                        menuItem,
                        menuItemStyle,
                        separatorStyle);
                    continue;
                }

                Separator separator = child as Separator;
                if (separator != null)
                {
                    separator.Style = separatorStyle;
                }
            }
        }

        private static ResourceDictionary CreateResources()
        {
            string markup =
                StyleMarkup.Replace(
                    "__POPUP_ANIMATION__",
                    SystemParameters.MenuAnimation
                        ? "Fade"
                        : "None");
            object parsed = XamlReader.Parse(markup);
            ResourceDictionary resources =
                parsed as ResourceDictionary;
            if (resources == null)
            {
                throw new InvalidOperationException(
                    "The NuoMi menu style could not be loaded.");
            }

            return resources;
        }

        private const string StyleMarkup = @"
<ResourceDictionary
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

    <SolidColorBrush x:Key=""NuoMi.Menu.Surface"" Color=""#FFFDFBF8"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Border"" Color=""#FFE8E0D8"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Text"" Color=""#FF352E2B"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Muted"" Color=""#FF9A8980"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Accent"" Color=""#FFB9552B"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.AccentDark"" Color=""#FF9F4726"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Hover"" Color=""#FFFFF3E8"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Pressed"" Color=""#FFFFE5D6"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Separator"" Color=""#FFEDE6E0"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.Danger"" Color=""#FFB63D3D"" />
    <SolidColorBrush x:Key=""NuoMi.Menu.DangerHover"" Color=""#FFFFECEB"" />

    <Style x:Key=""NuoMi.FlatContextMenu""
           TargetType=""{x:Type ContextMenu}"">
        <Setter Property=""OverridesDefaultStyle"" Value=""True"" />
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""Foreground"" Value=""{StaticResource NuoMi.Menu.Text}"" />
        <Setter Property=""FontFamily"" Value=""Microsoft YaHei UI"" />
        <Setter Property=""FontSize"" Value=""13"" />
        <Setter Property=""MinWidth"" Value=""270"" />
        <Setter Property=""Padding"" Value=""0"" />
        <Setter Property=""SnapsToDevicePixels"" Value=""True"" />
        <Setter Property=""TextOptions.TextFormattingMode"" Value=""Display"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ContextMenu}"">
                    <Border Margin=""9""
                            Padding=""6""
                            Background=""{StaticResource NuoMi.Menu.Surface}""
                            BorderBrush=""{StaticResource NuoMi.Menu.Border}""
                            BorderThickness=""1""
                            CornerRadius=""12""
                            SnapsToDevicePixels=""True"">
                        <Border.Effect>
                            <DropShadowEffect
                                BlurRadius=""18""
                                Direction=""270""
                                Opacity=""0.20""
                                ShadowDepth=""5""
                                Color=""#FF3E2820"" />
                        </Border.Effect>
                        <ScrollViewer
                            HorizontalScrollBarVisibility=""Disabled""
                            VerticalScrollBarVisibility=""Auto""
                            CanContentScroll=""True"">
                            <ItemsPresenter
                                KeyboardNavigation.DirectionalNavigation=""Cycle""
                                SnapsToDevicePixels=""{TemplateBinding SnapsToDevicePixels}"" />
                        </ScrollViewer>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""NuoMi.FlatMenuItem""
           TargetType=""{x:Type MenuItem}"">
        <Setter Property=""OverridesDefaultStyle"" Value=""True"" />
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""Foreground"" Value=""{StaticResource NuoMi.Menu.Text}"" />
        <Setter Property=""FontFamily"" Value=""Microsoft YaHei UI"" />
        <Setter Property=""FontSize"" Value=""13"" />
        <Setter Property=""MinHeight"" Value=""37"" />
        <Setter Property=""Margin"" Value=""2,1"" />
        <Setter Property=""Padding"" Value=""0"" />
        <Setter Property=""SnapsToDevicePixels"" Value=""True"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type MenuItem}"">
                    <Grid x:Name=""Row""
                          MinHeight=""{TemplateBinding MinHeight}""
                          Background=""Transparent""
                          SnapsToDevicePixels=""True"">
                        <Border x:Name=""RowBackground""
                                Background=""Transparent""
                                CornerRadius=""7"" />

                        <Grid Margin=""7,0,8,0"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""24"" />
                                <ColumnDefinition Width=""*"" />
                                <ColumnDefinition Width=""Auto"" />
                                <ColumnDefinition Width=""17"" />
                            </Grid.ColumnDefinitions>

                            <Border x:Name=""CheckBadge""
                                    Grid.Column=""0""
                                    Width=""18""
                                    Height=""18""
                                    HorizontalAlignment=""Center""
                                    VerticalAlignment=""Center""
                                    Background=""{StaticResource NuoMi.Menu.Accent}""
                                    CornerRadius=""9""
                                    Visibility=""Collapsed"">
                                <Path Width=""10""
                                      Height=""8""
                                      Margin=""1,0,0,0""
                                      HorizontalAlignment=""Center""
                                      VerticalAlignment=""Center""
                                      Data=""M 1,4 L 4,7 L 9,1""
                                      Stroke=""White""
                                      StrokeThickness=""1.8""
                                      StrokeStartLineCap=""Round""
                                      StrokeEndLineCap=""Round""
                                      StrokeLineJoin=""Round""
                                      Stretch=""None"" />
                            </Border>

                            <ContentPresenter
                                x:Name=""HeaderPresenter""
                                Grid.Column=""1""
                                Margin=""7,0,12,0""
                                HorizontalAlignment=""Stretch""
                                VerticalAlignment=""Center""
                                ContentSource=""Header""
                                RecognizesAccessKey=""True""
                                SnapsToDevicePixels=""{TemplateBinding SnapsToDevicePixels}"" />

                            <TextBlock x:Name=""GestureText""
                                       Grid.Column=""2""
                                       Margin=""4,0,10,0""
                                       VerticalAlignment=""Center""
                                       Foreground=""{StaticResource NuoMi.Menu.Muted}""
                                       FontSize=""12""
                                       Text=""{TemplateBinding InputGestureText}"" />

                            <Path x:Name=""SubmenuArrow""
                                  Grid.Column=""3""
                                  Width=""6""
                                  Height=""10""
                                  HorizontalAlignment=""Center""
                                  VerticalAlignment=""Center""
                                  Data=""M 1,1 L 5,5 L 1,9""
                                  Stroke=""{StaticResource NuoMi.Menu.Muted}""
                                  StrokeThickness=""1.5""
                                  StrokeStartLineCap=""Round""
                                  StrokeEndLineCap=""Round""
                                  StrokeLineJoin=""Round""
                                  Stretch=""Fill""
                                  Visibility=""Collapsed"" />
                        </Grid>

                        <Popup x:Name=""PART_Popup""
                               AllowsTransparency=""True""
                               Focusable=""False""
                               HorizontalOffset=""-5""
                               IsOpen=""{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}""
                               Placement=""Right""
                               PopupAnimation=""__POPUP_ANIMATION__""
                               VerticalOffset=""-10"">
                            <Border Margin=""9""
                                    MinWidth=""220""
                                    MaxHeight=""{TemplateBinding MaxHeight}""
                                    Padding=""6""
                                    Background=""{StaticResource NuoMi.Menu.Surface}""
                                    BorderBrush=""{StaticResource NuoMi.Menu.Border}""
                                    BorderThickness=""1""
                                    CornerRadius=""12""
                                    SnapsToDevicePixels=""True"">
                                <Border.Effect>
                                    <DropShadowEffect
                                        BlurRadius=""18""
                                        Direction=""270""
                                        Opacity=""0.20""
                                        ShadowDepth=""5""
                                        Color=""#FF3E2820"" />
                                </Border.Effect>
                                <ScrollViewer
                                    HorizontalScrollBarVisibility=""Disabled""
                                    VerticalScrollBarVisibility=""Auto""
                                    CanContentScroll=""True"">
                                    <ItemsPresenter
                                        KeyboardNavigation.DirectionalNavigation=""Cycle""
                                        SnapsToDevicePixels=""True"" />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>

                    <ControlTemplate.Triggers>
                        <Trigger Property=""HasItems"" Value=""True"">
                            <Setter TargetName=""SubmenuArrow""
                                    Property=""Visibility""
                                    Value=""Visible"" />
                        </Trigger>
                        <Trigger Property=""IsChecked"" Value=""True"">
                            <Setter TargetName=""CheckBadge""
                                    Property=""Visibility""
                                    Value=""Visible"" />
                        </Trigger>
                        <Trigger Property=""IsHighlighted"" Value=""True"">
                            <Setter TargetName=""RowBackground""
                                    Property=""Background""
                                    Value=""{StaticResource NuoMi.Menu.Hover}"" />
                            <Setter Property=""Foreground""
                                    Value=""{StaticResource NuoMi.Menu.AccentDark}"" />
                            <Setter TargetName=""SubmenuArrow""
                                    Property=""Stroke""
                                    Value=""{StaticResource NuoMi.Menu.AccentDark}"" />
                        </Trigger>
                        <Trigger Property=""IsSubmenuOpen"" Value=""True"">
                            <Setter TargetName=""RowBackground""
                                    Property=""Background""
                                    Value=""{StaticResource NuoMi.Menu.Pressed}"" />
                            <Setter Property=""Foreground""
                                    Value=""{StaticResource NuoMi.Menu.AccentDark}"" />
                            <Setter TargetName=""SubmenuArrow""
                                    Property=""Stroke""
                                    Value=""{StaticResource NuoMi.Menu.AccentDark}"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Opacity"" Value=""0.43"" />
                        </Trigger>
                        <Trigger Property=""Tag"" Value=""NuoMi.Accent"">
                            <Setter Property=""Foreground""
                                    Value=""{StaticResource NuoMi.Menu.AccentDark}"" />
                            <Setter Property=""FontWeight"" Value=""SemiBold"" />
                        </Trigger>
                        <Trigger Property=""Tag"" Value=""NuoMi.Danger"">
                            <Setter Property=""Foreground""
                                    Value=""{StaticResource NuoMi.Menu.Danger}"" />
                        </Trigger>
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property=""Tag"" Value=""NuoMi.Danger"" />
                                <Condition Property=""IsHighlighted"" Value=""True"" />
                            </MultiTrigger.Conditions>
                            <Setter TargetName=""RowBackground""
                                    Property=""Background""
                                    Value=""{StaticResource NuoMi.Menu.DangerHover}"" />
                            <Setter Property=""Foreground""
                                    Value=""{StaticResource NuoMi.Menu.Danger}"" />
                        </MultiTrigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""NuoMi.FlatMenuSeparator""
           TargetType=""{x:Type Separator}"">
        <Setter Property=""Height"" Value=""11"" />
        <Setter Property=""Margin"" Value=""14,0"" />
        <Setter Property=""SnapsToDevicePixels"" Value=""True"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type Separator}"">
                    <Grid Height=""11"">
                        <Border Height=""1""
                                VerticalAlignment=""Center""
                                Background=""{StaticResource NuoMi.Menu.Separator}""
                                SnapsToDevicePixels=""True"" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>";
    }
}
