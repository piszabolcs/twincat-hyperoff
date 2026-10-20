using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VbsAutoOff.Core;

namespace VbsAutoOff.Ui
{
    // UI: fixed-size (no scrolling) two-column window, dark Windows 11 title bar matching the
    // window, a selectable/copyable log, the Phase 2 explanation, an icon, and the correct version.
    // The status probe (WMI + bcdedit) runs on a background thread so the window never freezes.
    public sealed class MainWindow : Window
    {
        private bool _busy;
        private INameScope _names;
        private DispatcherTimer _logTimer;
        private Grid _root;

        private Image ImgLogo;
        private TextBlock TxtVersion, HeroTitle, HeroSub, Phase2State;
        private Ellipse HeroDot, DotVbs, DotService, DotHv, DotUefi;
        private TextBlock ValVbs, ValService, ValHv, ValUefi;
        private Button BtnPrimary, BtnReboot, BtnRefresh, BtnUninstall, BtnPhase2, BtnCopyLog, BtnOpenLog;
        private Button SigBtn;
        private TextBox LogBox;

        private Grid _aboutOverlay;
        private Button _aboutCloseBtn;

        private const string RepoUrl = "https://github.com/piszabolcs/twincat-hyperoff";

        private static readonly Brush BrushText = BrushFromHex("#E8EBF0");
        private static readonly Brush BrushMuted = BrushFromHex("#7D8696");
        private static readonly Brush BrushAccent = BrushFromHex("#F2A33C");
        private static readonly Brush BrushGood = BrushFromHex("#3FB984");
        private static readonly Brush BrushBad = BrushFromHex("#E5634D");
        private static readonly Brush BrushInfo = BrushFromHex("#6FA8DC");

        private static Brush BrushFromHex(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public MainWindow()
        {
            BuildUi();
            // Single source of truth for the displayed version: the executing assembly version.
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            string verText = "v" + (ver != null ? ver.ToString(3) : "1.0.0");
            TxtVersion.Text = verText;
            SigBtn.Content = "psz // " + verText;
            BuildAboutOverlay(verText);
            LoadIcon();
            SourceInitialized += delegate { ApplyDarkTitleBar(); };
            Loaded += delegate
            {
                RefreshStatus();
                _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _logTimer.Tick += delegate { UpdateLog(false); };
                _logTimer.Start();
            };
        }

        // Dark title bar + matching border (Windows 11 DWM). On Win10 it silently does nothing.
        private void ApplyDarkTitleBar()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
                int bg = 0x0015110F;                                  // #0F1115 COLORREF (0x00BBGGRR)
                DwmSetWindowAttribute(h, 35, ref bg, 4);              // DWMWA_CAPTION_COLOR
                int border = 0x0015110F;
                DwmSetWindowAttribute(h, 34, ref border, 4);          // DWMWA_BORDER_COLOR
            }
            catch { }
        }

        private void LoadIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VbsAutoOff.app.ico"))
                {
                    if (s == null) return;
                    var decoder = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = null;
                    foreach (var f in decoder.Frames)
                        if (best == null || f.PixelWidth > best.PixelWidth) best = f;
                    Icon = decoder.Frames[0];
                    if (best != null) ImgLogo.Source = best;
                }
            }
            catch { }
        }

        private T Get<T>(string name) where T : class
        {
            var o = _names.FindName(name) as T;
            if (o == null) throw new InvalidOperationException("UI element not found in the XAML: " + name);
            return o;
        }

        private void BuildUi()
        {
            const string xaml = @"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='TwinCAT HyperOFF'
        Width='1060' Height='740' ResizeMode='CanMinimize'
        WindowStartupLocation='CenterScreen'
        Background='#0F1115' FontFamily='Segoe UI' UseLayoutRounding='True'
        TextOptions.TextFormattingMode='Display'>
    <Window.Resources>
        <Style x:Key='Card' TargetType='Border'>
            <Setter Property='Background' Value='#161920' />
            <Setter Property='BorderBrush' Value='#232731' />
            <Setter Property='BorderThickness' Value='1' />
            <Setter Property='CornerRadius' Value='14' />
            <Setter Property='Padding' Value='20' />
        </Style>
        <Style x:Key='CardTitle' TargetType='TextBlock'>
            <Setter Property='FontSize' Value='12' />
            <Setter Property='FontWeight' Value='SemiBold' />
            <Setter Property='Foreground' Value='#7D8696' />
        </Style>
        <Style x:Key='RowLabel' TargetType='TextBlock'>
            <Setter Property='FontSize' Value='12' />
            <Setter Property='Foreground' Value='#7D8696' />
        </Style>
        <Style x:Key='RowValue' TargetType='TextBlock'>
            <Setter Property='FontSize' Value='14.5' />
            <Setter Property='FontWeight' Value='SemiBold' />
            <Setter Property='Foreground' Value='#E8EBF0' />
            <Setter Property='TextTrimming' Value='CharacterEllipsis' />
            <Setter Property='Margin' Value='0,2,0,0' />
        </Style>
        <Style x:Key='BaseButton' TargetType='Button'>
            <Setter Property='Foreground' Value='#E8EBF0' />
            <Setter Property='Background' Value='#1D212A' />
            <Setter Property='BorderBrush' Value='#2B303B' />
            <Setter Property='FontSize' Value='13' />
            <Setter Property='Height' Value='38' />
            <Setter Property='Padding' Value='16,0' />
            <Setter Property='Cursor' Value='Hand' />
            <Setter Property='FocusVisualStyle' Value='{x:Null}' />
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                                BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='b' Property='Opacity' Value='0.85' />
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='b' Property='Opacity' Value='0.7' />
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter TargetName='b' Property='Opacity' Value='0.35' />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'>
            <Setter Property='Background' Value='#F2A33C' />
            <Setter Property='BorderBrush' Value='#F2A33C' />
            <Setter Property='Foreground' Value='#17110A' />
            <Setter Property='FontSize' Value='15' />
            <Setter Property='FontWeight' Value='SemiBold' />
            <Setter Property='Height' Value='48' />
        </Style>
        <Style x:Key='GhostButton' TargetType='Button' BasedOn='{StaticResource BaseButton}' />
        <Style x:Key='DangerButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'>
            <Setter Property='Foreground' Value='#E5735F' />
            <Setter Property='Background' Value='Transparent' />
        </Style>
        <Style x:Key='SignatureButton' TargetType='Button'>
            <Setter Property='Background' Value='Transparent' />
            <Setter Property='BorderThickness' Value='0' />
            <Setter Property='Foreground' Value='#7D8696' />
            <Setter Property='FontSize' Value='11.5' />
            <Setter Property='FontFamily' Value='Consolas' />
            <Setter Property='Cursor' Value='Hand' />
            <Setter Property='Padding' Value='6,3' />
            <Setter Property='FocusVisualStyle' Value='{x:Null}' />
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border Background='Transparent' Padding='{TemplateBinding Padding}'>
                            <TextBlock x:Name='sig' Text='{TemplateBinding Content}'
                                       Foreground='{TemplateBinding Foreground}'
                                       FontFamily='{TemplateBinding FontFamily}'
                                       FontSize='{TemplateBinding FontSize}' />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='sig' Property='Foreground' Value='#F2A33C' />
                                <Setter TargetName='sig' Property='Effect'>
                                    <Setter.Value>
                                        <DropShadowEffect Color='#F2A33C' BlurRadius='14' ShadowDepth='0' Opacity='0.9' />
                                    </Setter.Value>
                                </Setter>
                            </Trigger>
                            <Trigger Property='IsKeyboardFocused' Value='True'>
                                <Setter TargetName='sig' Property='Foreground' Value='#F2A33C' />
                                <Setter TargetName='sig' Property='Effect'>
                                    <Setter.Value>
                                        <DropShadowEffect Color='#F2A33C' BlurRadius='14' ShadowDepth='0' Opacity='0.9' />
                                    </Setter.Value>
                                </Setter>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key='CloseButton' TargetType='Button'>
            <Setter Property='Foreground' Value='#7D8696' />
            <Setter Property='Background' Value='Transparent' />
            <Setter Property='FontSize' Value='15' />
            <Setter Property='Width' Value='30' />
            <Setter Property='Height' Value='30' />
            <Setter Property='Cursor' Value='Hand' />
            <Setter Property='FocusVisualStyle' Value='{x:Null}' />
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='cb' Background='{TemplateBinding Background}' CornerRadius='8'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='cb' Property='Background' Value='#1D212A' />
                                <Setter Property='Foreground' Value='#E8EBF0' />
                            </Trigger>
                            <Trigger Property='IsKeyboardFocused' Value='True'>
                                <Setter TargetName='cb' Property='Background' Value='#1D212A' />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType='ScrollBar'>
            <Setter Property='Width' Value='8' />
            <Setter Property='MinWidth' Value='8' />
            <Setter Property='Background' Value='Transparent' />
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ScrollBar'>
                        <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='Thumb'>
                                            <Border CornerRadius='4' Background='#343A46' />
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin='28,22,28,24'>
        <Grid.RowDefinitions>
            <RowDefinition Height='Auto' />
            <RowDefinition Height='*' />
            <RowDefinition Height='Auto' />
        </Grid.RowDefinitions>

        <!-- Header -->
        <Grid Grid.Row='0' Margin='0,0,0,20'>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto' />
                <ColumnDefinition Width='*' />
                <ColumnDefinition Width='Auto' />
            </Grid.ColumnDefinitions>
            <Image x:Name='ImgLogo' Grid.Column='0' Width='44' Height='44' Margin='0,0,14,0' VerticalAlignment='Center' />
            <StackPanel Grid.Column='1' VerticalAlignment='Center'>
                <TextBlock Text='TwinCAT HyperOFF' FontSize='22' FontWeight='Bold' Foreground='#F2F4F7' />
                <TextBlock Text='Keeps the VBS / Hyper-V / Credential Guard features switched off so the TwinCAT 3 real-time runtime can start on Windows 11.'
                           FontSize='12.5' Foreground='#7D8696' Margin='0,2,0,0' TextWrapping='Wrap' />
            </StackPanel>
            <Border Grid.Column='2' VerticalAlignment='Center' CornerRadius='8' Padding='10,4' Background='#161920' BorderBrush='#232731' BorderThickness='1'>
                <TextBlock x:Name='TxtVersion' Text='v1.0.0' FontSize='12' Foreground='#7D8696' FontFamily='Consolas' />
            </Border>
        </Grid>

        <Grid Grid.Row='1'>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width='430' />
                <ColumnDefinition Width='20' />
                <ColumnDefinition Width='*' />
            </Grid.ColumnDefinitions>

            <!-- Left column: status + actions -->
            <Border Grid.Column='0' Style='{StaticResource Card}'>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height='Auto' />
                        <RowDefinition Height='Auto' />
                        <RowDefinition Height='*' />
                        <RowDefinition Height='Auto' />
                    </Grid.RowDefinitions>

                    <StackPanel Grid.Row='0'>
                        <StackPanel Orientation='Horizontal'>
                            <Ellipse x:Name='HeroDot' Width='12' Height='12' Fill='#7D8696' VerticalAlignment='Center' />
                            <TextBlock x:Name='HeroTitle' Text='Checking status...' FontSize='18' FontWeight='SemiBold'
                                       Foreground='#E8EBF0' Margin='10,0,0,0' VerticalAlignment='Center' />
                        </StackPanel>
                        <TextBlock x:Name='HeroSub' Text='' FontSize='12.5' Foreground='#9AA3B2' TextWrapping='Wrap'
                                   Margin='22,6,0,0' LineHeight='18' />
                    </StackPanel>

                    <Border Grid.Row='1' Height='1' Background='#232731' Margin='0,18,0,8' />

                    <StackPanel Grid.Row='2'>
                        <Grid Margin='0,8'>
                            <Grid.ColumnDefinitions><ColumnDefinition Width='22' /><ColumnDefinition Width='*' /></Grid.ColumnDefinitions>
                            <Ellipse x:Name='DotVbs' Width='9' Height='9' Fill='#7D8696' VerticalAlignment='Top' Margin='0,5,0,0' HorizontalAlignment='Left' />
                            <StackPanel Grid.Column='1'>
                                <TextBlock Style='{StaticResource RowLabel}' Text='VBS (Virtualization Based Security)' />
                                <TextBlock x:Name='ValVbs' Style='{StaticResource RowValue}' Text='-' />
                            </StackPanel>
                        </Grid>
                        <Grid Margin='0,8'>
                            <Grid.ColumnDefinitions><ColumnDefinition Width='22' /><ColumnDefinition Width='*' /></Grid.ColumnDefinitions>
                            <Ellipse x:Name='DotService' Width='9' Height='9' Fill='#7D8696' VerticalAlignment='Top' Margin='0,5,0,0' HorizontalAlignment='Left' />
                            <StackPanel Grid.Column='1'>
                                <TextBlock Style='{StaticResource RowLabel}' Text='Background service and watchdog' />
                                <TextBlock x:Name='ValService' Style='{StaticResource RowValue}' Text='-' />
                            </StackPanel>
                        </Grid>
                        <Grid Margin='0,8'>
                            <Grid.ColumnDefinitions><ColumnDefinition Width='22' /><ColumnDefinition Width='*' /></Grid.ColumnDefinitions>
                            <Ellipse x:Name='DotHv' Width='9' Height='9' Fill='#7D8696' VerticalAlignment='Top' Margin='0,5,0,0' HorizontalAlignment='Left' />
                            <StackPanel Grid.Column='1'>
                                <TextBlock Style='{StaticResource RowLabel}' Text='Hypervisor launch (BCD)' />
                                <TextBlock x:Name='ValHv' Style='{StaticResource RowValue}' Text='-' />
                            </StackPanel>
                        </Grid>
                        <Grid Margin='0,8'>
                            <Grid.ColumnDefinitions><ColumnDefinition Width='22' /><ColumnDefinition Width='*' /></Grid.ColumnDefinitions>
                            <Ellipse x:Name='DotUefi' Width='9' Height='9' Fill='#7D8696' VerticalAlignment='Top' Margin='0,5,0,0' HorizontalAlignment='Left' />
                            <StackPanel Grid.Column='1'>
                                <TextBlock Style='{StaticResource RowLabel}' Text='UEFI lock / Phase 2' />
                                <TextBlock x:Name='ValUefi' Style='{StaticResource RowValue}' Text='-' />
                            </StackPanel>
                        </Grid>
                    </StackPanel>

                    <StackPanel Grid.Row='3'>
                        <Button x:Name='BtnPrimary' Style='{StaticResource PrimaryButton}' Content='Activate protection' />
                        <Grid Margin='0,10,0,0'>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width='*' />
                                <ColumnDefinition Width='10' />
                                <ColumnDefinition Width='*' />
                                <ColumnDefinition Width='10' />
                                <ColumnDefinition Width='*' />
                            </Grid.ColumnDefinitions>
                            <Button x:Name='BtnReboot' Grid.Column='0' Style='{StaticResource GhostButton}' Content='Restart' />
                            <Button x:Name='BtnRefresh' Grid.Column='2' Style='{StaticResource GhostButton}' Content='Refresh' />
                            <Button x:Name='BtnUninstall' Grid.Column='4' Style='{StaticResource DangerButton}' Content='Uninstall' />
                        </Grid>
                    </StackPanel>
                </Grid>
            </Border>

            <!-- Right column: Phase 2 info + log -->
            <Grid Grid.Column='2'>
                <Grid.RowDefinitions>
                    <RowDefinition Height='Auto' />
                    <RowDefinition Height='20' />
                    <RowDefinition Height='*' />
                </Grid.RowDefinitions>

                <Border Grid.Row='0' Style='{StaticResource Card}'>
                    <Grid>
                        <StackPanel>
                            <TextBlock Style='{StaticResource CardTitle}' Text='PHASE 2  ·  RELEASE THE UEFI LOCK' />
                            <TextBlock x:Name='Phase2State' Text='' FontSize='14.5' FontWeight='SemiBold' Foreground='#E8EBF0' Margin='0,6,0,8' TextWrapping='Wrap' />
                            <TextBlock FontSize='12.5' Foreground='#9AA3B2' TextWrapping='Wrap' LineHeight='18'>
                                <Run FontWeight='SemiBold' Foreground='#C9CED8'>What is it for?</Run>
                                <Run>On some machines VBS is enabled not only by the registry but is also written into the motherboard UEFI firmware (UEFI lock). In that case the normal cleanup runs in vain: after a restart VBS starts again. Phase 2 runs Microsoft's own SecConfig.efi tool once, at the next boot, and that releases the lock.</Run>
                                <LineBreak /><LineBreak />
                                <Run FontWeight='SemiBold' Foreground='#C9CED8'>When is it needed?</Run>
                                <Run>Only if VBS is still running after Activate protection and a restart.</Run>
                                <LineBreak /><LineBreak />
                                <Run FontWeight='SemiBold' Foreground='#C9CED8'>What happens?</Run>
                                <Run>At restart a Microsoft screen appears before Windows: press F3 (or the key shown on screen). BitLocker is suspended for 2 restarts, so keep the recovery key at hand.</Run>
                            </TextBlock>
                            <Button x:Name='BtnPhase2' Style='{StaticResource GhostButton}' Content='Prepare Phase 2'
                                    HorizontalAlignment='Left' Margin='0,14,0,0' />
                        </StackPanel>

                    </Grid>
                </Border>

                <Border Grid.Row='2' Style='{StaticResource Card}' Padding='20,16,12,16'>
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height='Auto' />
                            <RowDefinition Height='*' />
                        </Grid.RowDefinitions>
                        <Grid Grid.Row='0' Margin='0,0,8,10'>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width='*' />
                                <ColumnDefinition Width='Auto' />
                                <ColumnDefinition Width='8' />
                                <ColumnDefinition Width='Auto' />
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column='0' VerticalAlignment='Center'>
                                <TextBlock Style='{StaticResource CardTitle}' Text='LOG' />
                                <TextBlock Text='C:\ProgramData\VbsAutoOff\VbsAutoOff.log  ·  selectable, copyable' FontSize='11.5' Foreground='#5C6473' Margin='0,2,0,0' />
                            </StackPanel>
                            <Button x:Name='BtnCopyLog' Grid.Column='1' Style='{StaticResource GhostButton}' Content='Copy' Height='32' />
                            <Button x:Name='BtnOpenLog' Grid.Column='3' Style='{StaticResource GhostButton}' Content='Open folder' Height='32' />
                        </Grid>
                        <TextBox x:Name='LogBox' Grid.Row='1' IsReadOnly='True' IsReadOnlyCaretVisible='False'
                                 Background='Transparent' BorderThickness='0' Padding='0'
                                 Foreground='#AEB5C2' FontFamily='Consolas' FontSize='11.5'
                                 SelectionBrush='#F2A33C' TextWrapping='Wrap' AcceptsReturn='True'
                                 VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'
                                 FocusVisualStyle='{x:Null}' />
                    </Grid>
                </Border>
            </Grid>
        </Grid>

        <!-- Footer: author signature -->
        <Button x:Name='SigBtn' Grid.Row='2' Style='{StaticResource SignatureButton}' Content='psz // v1.0.0'
                HorizontalAlignment='Right' Margin='0,14,0,0' ToolTip='About this app' />
    </Grid>
</Window>
";
            var win = (Window)XamlReader.Parse(xaml);
            _names = NameScope.GetNameScope(win);
            if (_names == null) throw new InvalidOperationException("The XAML NameScope was not created.");

            Title = win.Title;
            Width = win.Width;
            Height = win.Height;
            ResizeMode = win.ResizeMode;
            WindowStartupLocation = win.WindowStartupLocation;
            Background = win.Background;
            FontFamily = win.FontFamily;
            UseLayoutRounding = win.UseLayoutRounding;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Resources = win.Resources;
            var content = (FrameworkElement)win.Content;
            win.Content = null;
            _root = new Grid();
            _root.Children.Add(content);
            Content = _root;
            NameScope.SetNameScope(this, _names);

            ImgLogo = Get<Image>("ImgLogo");
            TxtVersion = Get<TextBlock>("TxtVersion");
            HeroDot = Get<Ellipse>("HeroDot");
            HeroTitle = Get<TextBlock>("HeroTitle");
            HeroSub = Get<TextBlock>("HeroSub");
            DotVbs = Get<Ellipse>("DotVbs");
            ValVbs = Get<TextBlock>("ValVbs");
            DotService = Get<Ellipse>("DotService");
            ValService = Get<TextBlock>("ValService");
            DotHv = Get<Ellipse>("DotHv");
            ValHv = Get<TextBlock>("ValHv");
            DotUefi = Get<Ellipse>("DotUefi");
            ValUefi = Get<TextBlock>("ValUefi");
            BtnPrimary = Get<Button>("BtnPrimary");
            BtnReboot = Get<Button>("BtnReboot");
            BtnRefresh = Get<Button>("BtnRefresh");
            BtnUninstall = Get<Button>("BtnUninstall");
            Phase2State = Get<TextBlock>("Phase2State");
            BtnPhase2 = Get<Button>("BtnPhase2");
            LogBox = Get<TextBox>("LogBox");
            BtnCopyLog = Get<Button>("BtnCopyLog");
            BtnOpenLog = Get<Button>("BtnOpenLog");
            SigBtn = Get<Button>("SigBtn");

            BtnPrimary.Click += BtnPrimary_Click;
            BtnReboot.Click += delegate { OfferReboot(); };
            BtnRefresh.Click += delegate { RefreshStatus(); };
            BtnUninstall.Click += BtnUninstall_Click;
            BtnPhase2.Click += BtnPhase2_Click;
            SigBtn.Click += delegate { ShowAbout(); };
            BtnCopyLog.Click += delegate
            {
                try
                {
                    string t = LogBox.SelectionLength > 0 ? LogBox.SelectedText : LogBox.Text;
                    Clipboard.SetText(t ?? "");
                    BtnCopyLog.Content = "Copied";
                    var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    t2.Tick += delegate { BtnCopyLog.Content = "Copy"; t2.Stop(); };
                    t2.Start();
                }
                catch { }
            };
            BtnOpenLog.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(Logger.LogDir);
                    Process.Start("explorer.exe", "\"" + Logger.LogDir + "\"");
                }
                catch { }
            };

            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && _aboutOverlay != null && _aboutOverlay.Visibility == Visibility.Visible)
                {
                    HideAbout();
                    e.Handled = true;
                }
            };
        }

        // Compact About card shown as an in-window overlay. Dismissable by the close button, Esc,
        // and by clicking outside the card. The rounded card sits on a full-window dimmed layer,
        // so there is never a square shadow/background behind the rounded corners.
        private void BuildAboutOverlay(string verText)
        {
            _aboutOverlay = new Grid
            {
                Background = BrushFromHex("#CC0B0D11"),
                Visibility = Visibility.Collapsed
            };
            _aboutOverlay.MouseLeftButtonDown += delegate { HideAbout(); };

            var card = new Border
            {
                Background = BrushFromHex("#161920"),
                BorderBrush = BrushFromHex("#232731"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(26),
                Width = 470,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.6 }
            };
            // Clicks inside the card must not reach the overlay (which would dismiss it).
            card.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };

            var panel = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new TextBlock
            {
                Text = "TwinCAT HyperOFF",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#F2F4F7"),
                VerticalAlignment = VerticalAlignment.Center
            });
            var verBadge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 2, 8, 2),
                Background = BrushFromHex("#1D212A"),
                BorderBrush = BrushFromHex("#2B303B"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            verBadge.Child = new TextBlock
            {
                Text = verText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = BrushAccent
            };
            titleRow.Children.Add(verBadge);
            Grid.SetColumn(titleRow, 0);
            header.Children.Add(titleRow);

            _aboutCloseBtn = new Button
            {
                Content = "\u2715",
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)Resources["CloseButton"]
            };
            _aboutCloseBtn.Click += delegate { HideAbout(); };
            Grid.SetColumn(_aboutCloseBtn, 1);
            header.Children.Add(_aboutCloseBtn);
            panel.Children.Add(header);

            panel.Children.Add(new Border { Height = 1, Background = BrushFromHex("#232731"), Margin = new Thickness(0, 16, 0, 2) });

            panel.Children.Add(AboutLabel("WHAT IT DOES"));
            panel.Children.Add(AboutBody(
                "Keeps Windows VBS / Device Guard / Credential Guard / HVCI switched off so a Beckhoff "
                + "TwinCAT 3 real-time runtime can start. A SYSTEM service re-applies the settings on boot, "
                + "on shutdown and on a timer, against domain GPO."));

            panel.Children.Add(AboutLabel("ARCHITECTURE"));
            panel.Children.Add(AboutArchLine("UI:", "WPF status panel"));
            panel.Children.Add(AboutArchLine("Core:", "VbsCleaner / StatusProbe / Phase2Setup / ServiceControl"));
            panel.Children.Add(AboutArchLine("Service:", "CleanupWorker (boot + shutdown + timer)"));

            panel.Children.Add(AboutLabel("BUILT WITH"));
            panel.Children.Add(AboutBody("C#, .NET Framework 4.8, WPF, Windows Service, WMI, bcdedit / powercfg / manage-bde"));

            panel.Children.Add(AboutLabel("REPOSITORY"));
            var repoTb = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            var link = new Hyperlink(new Run(RepoUrl)) { Foreground = BrushAccent };
            link.NavigateUri = new Uri(RepoUrl);
            link.RequestNavigate += delegate(object s, RequestNavigateEventArgs e)
            {
                try { Process.Start(e.Uri.AbsoluteUri); } catch { }
            };
            repoTb.Inlines.Add(link);
            panel.Children.Add(repoTb);

            card.Child = panel;
            _aboutOverlay.Children.Add(card);
            _root.Children.Add(_aboutOverlay);
        }

        private static TextBlock AboutLabel(string t)
        {
            return new TextBlock
            {
                Text = t,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushMuted,
                Margin = new Thickness(0, 16, 0, 5)
            };
        }

        private static TextBlock AboutBody(string t)
        {
            return new TextBlock
            {
                Text = t,
                FontSize = 12.5,
                Foreground = BrushFromHex("#9AA3B2"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
        }

        private static TextBlock AboutArchLine(string head, string rest)
        {
            var tb = new TextBlock
            {
                FontSize = 12.5,
                Foreground = BrushFromHex("#9AA3B2"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 1, 0, 1)
            };
            tb.Inlines.Add(new Run(head) { Foreground = BrushText, FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new Run(" " + rest));
            return tb;
        }

        private void ShowAbout()
        {
            if (_aboutOverlay == null) return;
            _aboutOverlay.Visibility = Visibility.Visible;
            if (_aboutCloseBtn != null) _aboutCloseBtn.Focus();
        }

        private void HideAbout()
        {
            if (_aboutOverlay != null) _aboutOverlay.Visibility = Visibility.Collapsed;
        }

        private async void RefreshStatus()
        {
            if (_busy) return;
            BtnRefresh.IsEnabled = false;
            VbsSnapshot snap;
            bool installed = false;
            string svc = null;
            try
            {
                snap = await Task.Run(() => StatusProbe.Read());
                installed = ServiceControl.IsInstalled();
                svc = ServiceControl.CurrentStatus();
            }
            catch { snap = new VbsSnapshot(); }
            BtnRefresh.IsEnabled = true;
            Apply(snap, installed, svc);
            UpdateLog(true);
        }

        private static void SetRow(Ellipse dot, TextBlock val, string text, Brush color)
        {
            val.Text = text;
            val.Foreground = color == BrushMuted ? BrushText : color;
            dot.Fill = color;
        }

        private void Apply(VbsSnapshot snap, bool installed, string svc)
        {
            bool running = installed && svc == "Running";
            bool hvOff = string.Equals(snap.HypervisorLaunch, "Off", StringComparison.OrdinalIgnoreCase);
            bool vbsOff = snap.VbsStatus == 0;

            // VBS
            if (snap.VbsStatus == 0) SetRow(DotVbs, ValVbs, "Disabled", BrushGood);
            else if (snap.VbsStatus == 1) SetRow(DotVbs, ValVbs, "Enabled, but not running", BrushAccent);
            else if (snap.VbsStatus == 2) SetRow(DotVbs, ValVbs, "Active and running, TwinCAT cannot start", BrushBad);
            else SetRow(DotVbs, ValVbs, "Cannot be read (WMI)", BrushMuted);

            // Service
            if (!installed) SetRow(DotService, ValService, "Not installed", BrushMuted);
            else if (running) SetRow(DotService, ValService, "Running", BrushGood);
            else SetRow(DotService, ValService, svc ?? "Installed, but not running", BrushAccent);

            // BCD
            if (hvOff) SetRow(DotHv, ValHv, "Off (disabled)", BrushGood);
            else if (snap.HypervisorLaunch == "?") SetRow(DotHv, ValHv, "Not set (default: Auto)", BrushAccent);
            else SetRow(DotHv, ValHv, snap.HypervisorLaunch, BrushBad);

            // UEFI / Phase 2
            if (snap.Phase2Queued)
            {
                SetRow(DotUefi, ValUefi, "Phase 2 scheduled, waiting for restart", BrushInfo);
                Phase2State.Text = "Scheduled. Restart the machine and press F3 on the Microsoft screen.";
                Phase2State.Foreground = BrushInfo;
            }
            else if (vbsOff)
            {
                SetRow(DotUefi, ValUefi, "No lock", BrushGood);
                Phase2State.Text = "Not needed, VBS is disabled.";
                Phase2State.Foreground = BrushGood;
            }
            else if (snap.UefiLocked)
            {
                SetRow(DotUefi, ValUefi, "Lock detected, Phase 2 required", BrushAccent);
                Phase2State.Text = "UEFI lock detected on this machine: run Phase 2.";
                Phase2State.Foreground = BrushAccent;
            }
            else
            {
                SetRow(DotUefi, ValUefi, "Not detected", BrushMuted);
                Phase2State.Text = "Run it if VBS is still running after a restart.";
                Phase2State.Foreground = BrushText;
            }
            BtnPhase2.IsEnabled = !vbsOff;

            BtnPrimary.Content = installed ? "Reactivate and clean up now" : "Activate protection";

            if (!installed)
                SetHero(BrushAccent, "Protection is not active",
                    "Press the Activate protection button. It installs the background service, disables the VBS / Credential Guard settings and sets the TwinCAT clock.");
            else if (running && hvOff && vbsOff)
                SetHero(BrushGood, "Protected, TwinCAT can run",
                    "VBS is disabled. The service re-applies it on start, on shutdown and every minute if the GPO turns it back on.");
            else if (running && snap.Phase2Queued)
                SetHero(BrushInfo, "Waiting for restart (Phase 2)",
                    "Restart the machine and confirm with F3 on the Microsoft screen shown before Windows.");
            else if (running)
                SetHero(BrushInfo, "Configured, restart required",
                    "The settings are in place, but VBS only stops after a restart (Restart, not Shut down). If it still runs afterwards, use Phase 2.");
            else
                SetHero(BrushAccent, "The service is not running",
                    "Press the Reactivate button to start the service.");
        }

        private void SetHero(Brush color, string title, string sub)
        {
            HeroDot.Fill = color;
            HeroTitle.Text = title;
            HeroTitle.Foreground = color;
            HeroSub.Text = sub;
        }

        // force=false: timed refresh. If the user is selecting text or is inside the log,
        // we do not overwrite it, so the selection is not lost.
        private void UpdateLog(bool force)
        {
            if (!force && (LogBox.IsKeyboardFocusWithin || LogBox.SelectionLength > 0)) return;
            var lines = Logger.Tail(200);
            var sb = new StringBuilder();
            foreach (var l in lines) sb.AppendLine(l);
            string text = lines.Length == 0 ? "(no log entries yet)" : sb.ToString().TrimEnd();
            if (text == LogBox.Text) return;
            LogBox.Text = text;
            LogBox.ScrollToEnd();
        }

        private async void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            SetBusy(true, "Installing and cleaning up...");
            var result = await Task.Run(() =>
            {
                var inst = ServiceControl.InstallAndStart();
                if (inst.Ok)
                {
                    try { VbsCleaner.RunPhase1(true); } catch { }
                }
                return inst;
            });
            SetBusy(false, null);
            RefreshStatus();
            if (!result.Ok)
                MessageBox.Show(this, result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async void BtnPhase2_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var confirm = MessageBox.Show(this,
                "I will prepare the Microsoft SecConfig.efi UEFI lock release (Phase 2).\n\n" +
                "At the next restart a Microsoft screen appears before Windows; confirm it there with the F3 key.\n\n" +
                "BitLocker is suspended for 2 restarts. Keep the recovery key at hand.\n\nContinue?",
                "Prepare Phase 2", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            SetBusy(true, "Preparing Phase 2...");
            var res = await Task.Run(() => Phase2Setup.Prepare());
            SetBusy(false, null);
            RefreshStatus();
            MessageBox.Show(this, res.Message, res.Ok ? "Phase 2 done" : "Phase 2 error",
                MessageBoxButton.OK, res.Ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (res.Ok) OfferReboot();
        }

        private void OfferReboot()
        {
            var r = MessageBox.Show(this,
                "Restart the machine now? Save your open work first.",
                "Restart", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            try { VbsCleaner.RunPhase1(true); } catch { }
            Proc.Run("shutdown.exe", "/r /t 5 /c \"TwinCAT HyperOFF: restart\"");
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var r = MessageBox.Show(this,
                "Are you sure you want to remove the background service and the watchdog task?\n\nThe registry / BCD values already set are not reverted, but the GPO may turn VBS back on over time.",
                "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            var res = ServiceControl.Uninstall();
            RefreshStatus();
            MessageBox.Show(this, res.Message, "Uninstall",
                MessageBoxButton.OK, res.Ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        private void SetBusy(bool busy, string label)
        {
            _busy = busy;
            BtnPrimary.IsEnabled = !busy;
            BtnPhase2.IsEnabled = !busy;
            BtnUninstall.IsEnabled = !busy;
            BtnReboot.IsEnabled = !busy;
            BtnRefresh.IsEnabled = !busy;
            if (busy && label != null) SetHero(BrushInfo, label, "Please wait...");
        }
    }
}
