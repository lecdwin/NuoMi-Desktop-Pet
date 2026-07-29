using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace NuoMiDesktopPet
{
    internal static class Program
    {
        private const string ShowExistingEventName =
            "NuoMiDesktopPet.ShowExisting.89AE3DA0";
        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _showExistingEvent;
        private static RegisteredWaitHandle _showExistingWait;

        [STAThread]
        private static void Main(string[] args)
        {
            // A tiny per-pixel transparent desktop window is inexpensive to
            // render in software, and this avoids intermittent lost surfaces
            // on remote, virtual and mixed-refresh display drivers.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            string diagnosticPreviewDirectory =
                GetArgumentValue(args, "--diagnostic-preview=");
            bool diagnosticPreview =
                !String.IsNullOrEmpty(
                    diagnosticPreviewDirectory);
            string mutexName =
                diagnosticPreview
                ? "NuoMiDesktopPet.Diagnostic." +
                    Process.GetCurrentProcess().Id.ToString(
                        CultureInfo.InvariantCulture)
                : "NuoMiDesktopPet.SingleInstance.89AE3DA0";
            bool isFirstInstance;
            _singleInstanceMutex = new Mutex(
                true,
                mutexName,
                out isFirstInstance);
            if (!isFirstInstance)
            {
                bool signaled = false;
                if (!diagnosticPreview)
                {
                    try
                    {
                        using (EventWaitHandle showEvent =
                            EventWaitHandle.OpenExisting(
                                ShowExistingEventName))
                        {
                            signaled = showEvent.Set();
                        }
                    }
                    catch
                    {
                        signaled = false;
                    }
                }

                if (!signaled)
                {
                    System.Windows.MessageBox.Show(
                        "糯米已经在运行啦。请双击右下角托盘图标把它叫回来。",
                        "糯米桌面宠物",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            if (!diagnosticPreview)
            {
                _showExistingEvent =
                    new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ShowExistingEventName);
            }

            WpfApplication app = new WpfApplication();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool startHidden =
                HasArgument(args, "--hidden");
            PetWindow pet;
            try
            {
                pet = new PetWindow(
                    diagnosticPreview,
                    startHidden && !diagnosticPreview);
            }
            catch (Exception ex)
            {
                ReportFatalError(ex);
                DisposeProcessCoordination();
                return;
            }

            if (_showExistingEvent != null)
            {
                _showExistingWait =
                    ThreadPool.RegisterWaitForSingleObject(
                        _showExistingEvent,
                        delegate
                        {
                            if (pet.Dispatcher.HasShutdownStarted)
                            {
                                return;
                            }

                            pet.Dispatcher.BeginInvoke(
                                (Action)pet.ShowFromExternalRequest);
                        },
                        null,
                        Timeout.Infinite,
                        false);
            }
            app.DispatcherUnhandledException +=
                delegate(
                    object sender,
                    DispatcherUnhandledExceptionEventArgs e)
                {
                    ReportFatalError(e.Exception);
                    e.Handled = true;
                    try
                    {
                        pet.ExitApplication();
                    }
                    catch
                    {
                        app.Shutdown();
                    }
                };
            app.SessionEnding += delegate
            {
                pet.ExitApplication();
            };

            if (!startHidden || diagnosticPreview)
            {
                pet.Show();
            }
            if (diagnosticPreview)
            {
                pet.BeginDiagnosticPreviewSequence(
                    diagnosticPreviewDirectory);
            }

            app.Run();

            DisposeProcessCoordination();
        }

        private static void DisposeProcessCoordination()
        {
            if (_showExistingWait != null)
            {
                _showExistingWait.Unregister(null);
                _showExistingWait = null;
            }
            if (_showExistingEvent != null)
            {
                _showExistingEvent.Dispose();
                _showExistingEvent = null;
            }
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }

        private static void ReportFatalError(Exception exception)
        {
            string logPath = null;
            try
            {
                string directory =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "NuoMiDesktopPet",
                        "logs");
                Directory.CreateDirectory(directory);
                logPath =
                    Path.Combine(
                        directory,
                        "latest.log");
                File.WriteAllText(
                    logPath,
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    (exception == null
                        ? "Unknown error."
                        : exception.ToString()));
            }
            catch
            {
                logPath = null;
            }

            string message =
                "糯米遇到问题，需要先休息一下。重新打开程序通常就能恢复。";
            if (!String.IsNullOrEmpty(logPath))
            {
                message +=
                    "\n\n错误记录已保存在：\n" +
                    logPath;
            }
            System.Windows.MessageBox.Show(
                message,
                "糯米桌面宠物",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static bool HasArgument(string[] args, string expected)
        {
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetArgumentValue(
            string[] args,
            string prefix)
        {
            if (args == null)
            {
                return null;
            }

            for (int index = 0; index < args.Length; index++)
            {
                string value = args[index];
                if (value != null &&
                    value.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(prefix.Length);
                }
            }
            return null;
        }

    }

    internal sealed class PetWindow : Window
    {
        private const double BaseWidth = 220.0;
        private const double BaseHeight = 260.0;
        private const int MinimumScalePercentage = 60;
        private const int MaximumScalePercentage = 150;
        private const string AppName = "糯米桌面宠物";
        private const string StartupValueName = "NuoMiDesktopPet";
        private const string SettingsKeyPath = @"Software\NuoMiDesktopPet";
        private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const int WmMouseActivate = 0x0021;
        private const int WmNcHitTest = 0x0084;
        private const int MaNoActivate = 3;
        private const int HtTransparent = -1;
        private const double TailRootX = 154.0;
        private const double TailRootY = 234.0;

        private static readonly WpfBrush FurBrush = MakeOrangeFurBrush();
        private static readonly WpfBrush FurLightBrush = MakeBrush(255, 239, 198);
        private static readonly WpfBrush FurShadowBrush = MakeBrush(211, 91, 24);
        private static readonly WpfBrush OutlineBrush = MakeBrush(88, 48, 33);
        private static readonly WpfBrush EarBrush = MakeBrush(255, 154, 151);
        private static readonly WpfBrush EyeBrush = MakeBrush(71, 53, 54);
        private static readonly WpfBrush IrisBrush = MakeGreenIrisBrush();
        private static readonly WpfBrush EyeGlintBrush = WpfBrushes.White;
        private static readonly WpfBrush CollarBrush = MakeBrush(61, 177, 169);
        private static readonly WpfBrush CollarLightBrush = MakeBrush(132, 225, 211);
        private static readonly WpfBrush PinkBrush = MakeBrush(255, 116, 151);
        private static readonly WpfBrush CheekBrush = MakeBrush(255, 137, 158, 145);
        private static readonly WpfBrush MuzzleBrush = MakeBrush(255, 246, 229);
        private static readonly WpfBrush BellBrush = MakeBrush(255, 217, 91);
        private static readonly WpfBrush BubbleBrush = MakeBrush(255, 255, 255);
        private static readonly WpfBrush BubbleTextBrush = MakeBrush(96, 67, 61);
        private static readonly WpfBrush ShadowBrush = MakeBrush(80, 72, 68, 45);
        private static readonly WpfBrush AmbientGlowBrush = MakeAmbientGlowBrush();
        private static readonly WpfBrush BongoDeskBrush = MakeBongoDeskBrush();
        private static readonly WpfBrush BongoKeyboardBrush =
            MakeSoftSurfaceBrush(255, 253, 248, 232, 224, 215);
        private static readonly WpfBrush BongoKeyBrush =
            MakeSoftSurfaceBrush(241, 238, 233, 203, 198, 192);
        private static readonly WpfBrush BongoMouseBrush =
            MakeSoftSurfaceBrush(255, 254, 249, 218, 211, 203);
        private static readonly WpfBrush BongoMouseButtonBrush =
            MakeSoftSurfaceBrush(246, 243, 237, 208, 204, 198);
        private static readonly WpfBrush BongoMouseWheelSlotBrush = MakeBrush(107, 91, 85);
        private static readonly WpfBrush BongoMouseWheelBrush = MakeBrush(185, 172, 163);
        private static readonly WpfBrush BongoOutlineBrush = MakeBrush(91, 66, 56);
        private static readonly WpfBrush BongoLeftActiveBrush =
            MakeSoftSurfaceBrush(255, 173, 185, 247, 94, 128);
        private static readonly WpfBrush BongoRightActiveBrush =
            MakeSoftSurfaceBrush(139, 226, 215, 48, 165, 160);
        private static readonly WpfBrush BongoKeyTextBrush = MakeBrush(103, 91, 85);
        private static readonly WpfBrush BongoActiveKeyTextBrush = WpfBrushes.White;
        private static readonly WpfBrush BongoWheelActiveBrush = MakeBrush(255, 205, 87);
        private static readonly WpfBrush BongoKeyRecessBrush = MakeBrush(204, 193, 185, 115);
        private static readonly WpfBrush BongoContactGlowBrush = MakeBrush(255, 246, 213, 150);
        private static readonly Typeface BongoKeyTypeface = new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.Bold,
            FontStretches.Normal);

        private static readonly Pen OutlinePen = MakePen(OutlineBrush, 2.8);
        private static readonly Pen ThinOutlinePen = MakePen(OutlineBrush, 1.8);
        private static readonly Pen WhiskerPen = MakePen(MakeBrush(128, 91, 82), 1.6);
        private static readonly Pen CollarPen = MakePen(MakeBrush(190, 78, 111), 2.0);
        private static readonly Pen ToePen = MakePen(MakeBrush(186, 113, 89), 1.7);
        private static readonly Pen BubblePen = MakePen(MakeBrush(229, 205, 184), 1.6);
        private static readonly Pen BubbleCoverPen = MakePen(BubbleBrush, 3.2);
        private static readonly Pen BongoOutlinePen = MakePen(BongoOutlineBrush, 1.7);
        private static readonly Pen BongoKeyPen = MakePen(MakeBrush(151, 139, 132), 0.75);
        private static readonly Pen BongoMouseDetailPen = MakePen(MakeBrush(126, 110, 102), 0.9);
        private static readonly StreamGeometry BubblePointerGeometry = CreateTriangle(
            new Point(98, 34),
            new Point(112, 34),
            new Point(105, 43));
        private static readonly StreamGeometry BubbleLeftHeartGeometry = CreateHeart(new Point(34, 24), 7);
        private static readonly StreamGeometry BubbleRightHeartGeometry = CreateHeart(new Point(185, 31), 5);
        private static readonly StreamGeometry FloatingHeartGeometry = CreateHeart(new Point(0, 0), 6);
        private static readonly BongoKeyCap[] BongoKeyCaps = CreateBongoKeyCaps();
        private static readonly StreamGeometry BongoMouseBodyGeometry = CreateBongoMouseBodyGeometry();
        private static readonly StreamGeometry BongoMouseLeftButtonGeometry = CreateBongoMouseButtonGeometry(false);
        private static readonly StreamGeometry BongoMouseRightButtonGeometry = CreateBongoMouseButtonGeometry(true);
        private static readonly RotateTransform BongoMouseUserOrientationTransform =
            CreateFrozenRotateTransform(180.0, 36.0, 226.0);
        private static readonly DrawingGroup BongoSurfaceDrawing = CreateBongoSurfaceDrawing();
        private static readonly DrawingGroup BongoDeskDrawing = CreateBongoDeskDrawing();

        private readonly Stopwatch _clock;
        private readonly Random _random;
        private readonly BehaviorEngine _behavior;
        private readonly InteractionDynamics _interactionMotion;
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly Drawing.Icon _trayIcon;
        private BitmapSource _rigHead;
        private BitmapSource _rigBlinkHead;
        private BitmapSource _rigBody;
        private BitmapSource _rigTail;
        private BitmapSource _rigLeftLeg;
        private BitmapSource _rigRightLeg;
        private BitmapSource _rigBow;
        private readonly TranslateTransform _characterTranslate = new TranslateTransform();
        private readonly RotateTransform _characterRotate = new RotateTransform(0, 110, 238);
        private readonly ScaleTransform _characterScale = new ScaleTransform(1, 1, 110, 238);
        private readonly TranslateTransform _tailTranslate = new TranslateTransform();
        private readonly RotateTransform _tailRotate = new RotateTransform(0, TailRootX, TailRootY);
        private readonly SkewTransform _tailSkew = new SkewTransform(0, 0, TailRootX, TailRootY);
        private readonly TranslateTransform _headTranslate = new TranslateTransform();
        private readonly RotateTransform _headRotate = new RotateTransform(0, 110, 149);
        private readonly ScaleTransform _headScale = new ScaleTransform(1, 1, 110, 149);
        private readonly ScaleTransform _bodyScale = new ScaleTransform(1, 1, 110, 245);
        private readonly TranslateTransform _bodyTranslate = new TranslateTransform();
        private readonly TranslateTransform _bowTranslate = new TranslateTransform();
        private readonly RotateTransform _bowRotate = new RotateTransform(0, 110, 145);
        private readonly TranslateTransform _leftLegTranslate = new TranslateTransform();
        private readonly RotateTransform _leftLegRotate = new RotateTransform(0, 94, 162);
        private readonly ScaleTransform _leftLegScale = new ScaleTransform(1, 1, 94, 226);
        private readonly TranslateTransform _rightLegTranslate = new TranslateTransform();
        private readonly RotateTransform _rightLegRotate = new RotateTransform(0, 126, 162);
        private readonly ScaleTransform _rightLegScale = new ScaleTransform(1, 1, 126, 226);
        private readonly ScaleTransform _windowScale = new ScaleTransform();
        private readonly TranslateTransform[] _bongoKeyDepthTransforms =
            CreateBongoKeyDepthTransforms();
        private readonly TranslateTransform _bongoLeftMouseDepth =
            new TranslateTransform();
        private readonly TranslateTransform _bongoRightMouseDepth =
            new TranslateTransform();
        private readonly TranslateTransform _bongoWheelTranslate =
            new TranslateTransform();
        private readonly TranslateTransform[] _floatingHeartTransforms =
        {
            new TranslateTransform(),
            new TranslateTransform(),
            new TranslateTransform()
        };

        private WpfContextMenu _petMenu;
        private WpfMenuItem _showHideMenuItem;
        private WpfMenuItem _followMenuItem;
        private WpfMenuItem _startupMenuItem;
        private WpfMenuItem _topmostMenuItem;
        private WpfMenuItem _autoHideFullscreenMenuItem;
        private WpfMenuItem _autoInteractionMenuItem;
        private WpfMenuItem _bongoModeMenuItem;
        private WpfMenuItem _motionPersonalityRootMenuItem;
        private WpfMenuItem _sizeRootMenuItem;
        private readonly List<WpfMenuItem> _sizeMenuItems = new List<WpfMenuItem>();
        private readonly List<WpfMenuItem> _motionPersonalityMenuItems =
            new List<WpfMenuItem>();

        private Forms.ContextMenuStrip _trayMenu;
        private Forms.ToolStripMenuItem _trayShowHideItem;
        private Forms.ToolStripMenuItem _trayFollowItem;
        private Forms.ToolStripMenuItem _trayStartupItem;
        private Forms.ToolStripMenuItem _trayTopmostItem;
        private Forms.ToolStripMenuItem _trayAutoHideFullscreenItem;
        private Forms.ToolStripMenuItem _trayAutoInteractionItem;
        private Forms.ToolStripMenuItem _trayBongoModeItem;
        private Forms.ToolStripMenuItem _traySizeRoot;
        private readonly List<Forms.ToolStripMenuItem> _traySizeItems =
            new List<Forms.ToolStripMenuItem>();
        private Forms.ToolStripMenuItem _trayMotionPersonalityRoot;
        private readonly List<Forms.ToolStripMenuItem> _trayMotionPersonalityItems =
            new List<Forms.ToolStripMenuItem>();

        private bool _isExiting;
        private bool _isDragging;
        private bool _dragPending;
        private bool _followMouse = true;
        private bool _autoInteraction = true;
        private bool _bongoMode = true;
        private bool _autoHideFullscreen = true;
        private bool _userHidden;
        private bool _fullscreenSuppressed;
        private bool _fullscreenBypassUntilExit;
        private bool _hasShownBackgroundTip;
        private bool _hasShownInputMonitorError;
        private bool _hasShownSettingsSaveError;
        private bool _needsVisibilityCorrection;
        private Drawing.Point _dragStartCursor;
        private NativeRect _dragStartWindowRect;
        private Drawing.Point _lastDragMotionCursor;
        private long _lastDragMotionAt;
        private double _dragLeanTarget;
        private double _dragLean;
        private double _dragVerticalTarget;
        private double _dragVertical;
        private double _userScale = 0.80;
        private double _breathPhase;
        private double _tailSwayPhase = 0.3;
        private double _tailEngagementEnvelope;
        private double _headAngle;
        private double _headShiftX;
        private double _headShiftY;
        private double _pupilOffsetX;
        private double _pupilOffsetY;
        private double _lastTrackedTargetAngle;
        private long _lastLargeGazeShiftAt;
        private double _blinkAmount;
        private long _nextBlinkAt;
        private long _blinkStartedAt = -1;
        private int _blinkDuration = 210;
        private int _remainingDoubleBlinks;
        private bool _nextBlinkIsFollowup;
        private bool _slowBlinkRequested;
        private bool _settleBlinkArmed;
        private long _settleBlinkDue;
        private long _messageUntil;
        private long _waveStartedAt = -1;
        private long _waveUntil;
        private bool _renderingSubscribed;
        private bool _hasLastRenderingTime;
        private TimeSpan _lastRenderingTime;
        private string _message = "喵~";
        private FormattedText _formattedMessage;
        private Drawing.Point _lastCursorPosition;
        private long _lastCursorSampleAt;
        private double _cursorSpeed;
        private long _lastStateSaveAt;
        private bool _behaviorCueShown;
        private bool _behaviorOnRight = true;
        private bool _behaviorWasUserRequested;
        private bool _poseRecoveryActive;
        private long _poseRecoveryStartedAt;
        private long _poseRecoveryUntil;
        private long _tailRecoveryUntil;
        private PetPose _poseRecoveryDelta;
        private long _behaviorInputConflictStartedAt = -1L;
        private bool _hasAutoWindowPosition;
        private double _autoWindowX;
        private double _autoWindowY;
        private bool _hasBehaviorHomePosition;
        private double _behaviorHomeX;
        private double _behaviorHomeY;
        private PropWindow _activeProp;
        private int _propOriginX;
        private int _propOriginY;
        private int _propDirection = 1;
        private int _propMinimumX;
        private int _propMaximumX;
        private int _propMinimumY;
        private int _propMaximumY;
        private double _propPixelScaleX = 1.0;
        private double _propPixelScaleY = 1.0;
        private long _lastPropTouchAt = -10000L;
        private double _propTouchStrength;
        private GlobalInputMonitor _globalInputMonitor;
        private bool _inputMonitoringStarted;
        private readonly HashSet<int> _leftKeysDown = new HashSet<int>();
        private readonly HashSet<int> _rightKeysDown = new HashSet<int>();
        private readonly Dictionary<int, long> _keyPulseUntilByVirtualKey =
            new Dictionary<int, long>();
        private readonly Dictionary<int, long> _keyPressedAtByVirtualKey =
            new Dictionary<int, long>();
        private readonly double[] _bongoKeyAmounts =
            new double[BongoKeyCaps.Length];
        private bool _mouseLeftDown;
        private bool _mouseRightDown;
        private long _leftInputPulseUntil;
        private long _rightInputPulseUntil;
        private long _mouseLeftPulseUntil;
        private long _mouseRightPulseUntil;
        private long _wheelPulseUntil;
        private long _mouseLeftAutoReleaseAt;
        private long _mouseRightAutoReleaseAt;
        private long _nextKeyStateReconcileAt;
        private long _lastBongoInputAt;
        private double _leftKeyAmount;
        private double _rightKeyAmount;
        private double _leftMouseAmount;
        private double _rightMouseAmount;
        private double _wheelAmount;
        private double _bongoPointerX = 0.5;
        private double _bongoPointerY = 0.5;
        private double _bongoPointerTargetX = 0.5;
        private double _bongoPointerTargetY = 0.5;
        private bool _localBongoMousePress;
        private bool _localBongoRightMousePress;
        private PetHitZone _pointerDownZone;
        private bool _hasShownWelcome;
        private System.Windows.Threading.DispatcherTimer _fullscreenTimer;
        private Drawing.Rectangle _lastPetPixelBounds;
        private bool _hasLastPetPixelBounds;
        private int _fullscreenEnterSamples;
        private int _fullscreenExitSamples;
        private long _fullscreenBypassEarliestResetAt;
        private FullscreenSample _lastFullscreenSample =
            FullscreenSample.Unknown;
        private IntPtr _lastFullscreenWindow;
        private IntPtr _bypassedFullscreenWindow;
        private System.Windows.Threading.DispatcherTimer _diagnosticTimer;
        private string _diagnosticPreviewDirectory;
        private int _diagnosticPreviewStep;
        private bool _isDiagnosticPreview;
        private HwndSource _windowSource;

        public PetWindow(bool diagnosticPreview, bool startHidden)
        {
            _isDiagnosticPreview = diagnosticPreview;
            _userHidden = startHidden;
            Title = AppName;
            Width = BaseWidth;
            Height = BaseHeight;
            MinWidth =
                BaseWidth * MinimumScalePercentage / 100.0;
            MinHeight =
                BaseHeight * MinimumScalePercentage / 100.0;
            MaxWidth =
                BaseWidth * MaximumScalePercentage / 100.0;
            MaxHeight =
                BaseHeight * MaximumScalePercentage / 100.0;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = WpfBrushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = true;
            SnapsToDevicePixels = false;
            UseLayoutRounding = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            _random = new Random();
            _clock = Stopwatch.StartNew();
            _behavior = new BehaviorEngine(_random);
            _interactionMotion = new InteractionDynamics(_random);
            _globalInputMonitor = new GlobalInputMonitor();
            _globalInputMonitor.KeyChanged += GlobalKeyChanged;
            _globalInputMonitor.MouseButtonChanged += GlobalMouseButtonChanged;
            _globalInputMonitor.MouseWheel += GlobalMouseWheel;
            _nextBlinkAt = 1700 + _random.Next(2200);

            LoadEmbeddedRig();
            LoadSettings();
            LoadPetState();
            _behavior.AdvanceNeeds(DateTime.UtcNow, false);
            ScheduleNextAutonomous(
                _clock.ElapsedMilliseconds,
                5000,
                10000);
            ApplyScale(false);
            RestoreOrChooseInitialPosition();
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            _trayIcon = CreateTrayIcon();
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Icon = _trayIcon;
            _notifyIcon.Text = AppName;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseDoubleClick += NotifyIconMouseDoubleClick;

            BuildPetMenu();
            BuildTrayMenu();

            MouseLeftButtonDown += PetMouseLeftButtonDown;
            MouseMove += PetMouseMove;
            MouseLeftButtonUp += PetMouseLeftButtonUp;
            MouseRightButtonDown += PetMouseRightButtonDown;
            MouseRightButtonUp += PetMouseRightButtonUp;
            MouseLeave += PetMouseLeave;
            LostMouseCapture += PetLostMouseCapture;
            Closing += PetWindowClosing;
            Closed += PetWindowClosed;
            IsVisibleChanged += PetWindowIsVisibleChanged;
            SourceInitialized += PetSourceInitialized;

            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;

            if (!_isDiagnosticPreview)
            {
                new WindowInteropHelper(this).EnsureHandle();
            }
        }

        public void BeginDiagnosticPreviewSequence(string outputDirectory)
        {
            if (String.IsNullOrEmpty(outputDirectory))
            {
                return;
            }

            _diagnosticPreviewDirectory =
                Path.GetFullPath(outputDirectory);
            _isDiagnosticPreview = true;
            StopInputMonitoring();
            Directory.CreateDirectory(_diagnosticPreviewDirectory);
            _diagnosticPreviewStep = 0;
            _autoInteraction = false;
            _followMouse = false;
            _bongoMode = true;
            _behavior.Cancel(_clock.ElapsedMilliseconds);
            ClearBongoInputState(true);
            _nextKeyStateReconcileAt = Int64.MaxValue;
            _nextBlinkAt = Int64.MaxValue;
            _blinkStartedAt = -1;
            _blinkAmount = 0.0;

            _userScale = 1.25;
            ApplyScale(false);

            _diagnosticTimer =
                new System.Windows.Threading.DispatcherTimer();
            _diagnosticTimer.Interval =
                TimeSpan.FromMilliseconds(480);
            _diagnosticTimer.Tick += DiagnosticPreviewTick;
            _diagnosticTimer.Start();
        }

        private void DiagnosticPreviewTick(object sender, EventArgs e)
        {
            switch (_diagnosticPreviewStep++)
            {
                case 0:
                    CaptureCurrentFrame("refined-idle.png");
                    ApplyGlobalKeyChange(0x41, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(72);
                    break;

                case 1:
                    CaptureCurrentFrame("refined-key-a-contact.png");
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(390);
                    break;

                case 2:
                    CaptureCurrentFrame("refined-key-a-held.png");
                    ApplyGlobalKeyChange(0x41, false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(235);
                    break;

                case 3:
                    ApplyGlobalKeyChange(0x50, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(78);
                    break;

                case 4:
                    CaptureCurrentFrame("refined-key-p-contact.png");
                    ApplyGlobalKeyChange(0x50, false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(235);
                    break;

                case 5:
                    SetDiagnosticMouseState(true, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(64);
                    break;

                case 6:
                    CaptureCurrentFrame("refined-physical-left-contact.png");
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(250);
                    break;

                case 7:
                    CaptureCurrentFrame("refined-physical-left-held.png");
                    SetDiagnosticMouseState(true, false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(220);
                    break;

                case 8:
                    SetDiagnosticMouseState(false, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(64);
                    break;

                case 9:
                    CaptureCurrentFrame("refined-physical-right-contact.png");
                    SetDiagnosticMouseState(false, false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(210);
                    break;

                case 10:
                    CaptureCurrentFrame("refined-recovered.png");
                    _blinkAmount = 0.5;
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(80);
                    break;

                case 11:
                    CaptureCurrentFrame("refined-blink-mid.png");
                    _blinkAmount = 1.0;
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(80);
                    break;

                case 12:
                    CaptureCurrentFrame("refined-blink-closed.png");
                    _blinkAmount = 0.0;
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(80);
                    break;

                case 13:
                    CaptureCurrentFrame("refined-blink-recovered.png");
                    _userScale = 0.60;
                    ApplyScale(false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(120);
                    break;

                case 14:
                    CaptureCurrentFrame("refined-scale-60.png");
                    StartBehavior(CatBehavior.CupPush, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(720);
                    break;

                case 15:
                    CaptureCurrentFrame(
                        "refined-scale-60-cup.png");
                    CaptureActivePropFrame(
                        "refined-scale-60-cup-prop.png");
                    ValidateActivePropForDiagnostic(
                        "60%-cup");
                    CancelBehavior(
                        _clock.ElapsedMilliseconds);
                    _userScale = 1.50;
                    ApplyScale(false);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(120);
                    break;

                case 16:
                    CaptureCurrentFrame("refined-scale-150.png");
                    StartBehavior(CatBehavior.Playing, true);
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(720);
                    break;

                case 17:
                    CaptureCurrentFrame(
                        "refined-scale-150-ball.png");
                    CaptureActivePropFrame(
                        "refined-scale-150-ball-prop.png");
                    ValidateActivePropForDiagnostic(
                        "150%-ball");
                    _diagnosticTimer.Interval =
                        TimeSpan.FromMilliseconds(40);
                    break;

                default:
                    _diagnosticTimer.Stop();
                    _diagnosticTimer.Tick -= DiagnosticPreviewTick;
                    _diagnosticTimer = null;
                    ClearBongoInputState(true);
                    ExitApplication();
                    break;
            }
        }

        private void SetDiagnosticMouseState(
            bool physicalLeft,
            bool isDown)
        {
            long now = _clock.ElapsedMilliseconds;
            if (physicalLeft)
            {
                _mouseLeftDown = isDown;
                _mouseLeftPulseUntil = isDown ? now + 145 : 0;
            }
            else
            {
                _mouseRightDown = isDown;
                _mouseRightPulseUntil = isDown ? now + 145 : 0;
            }

            if (isDown)
            {
                _interactionMotion.RegisterMouseDown(physicalLeft, now);
                _lastBongoInputAt = now;
            }
            else
            {
                _interactionMotion.RegisterMouseUp(physicalLeft, now);
            }
        }

        private void CaptureCurrentFrame(string fileName)
        {
            UpdateLayout();
            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Pbgra32);
            bitmap.Render(this);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(
                _diagnosticPreviewDirectory,
                fileName);
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        private void CaptureActivePropFrame(string fileName)
        {
            if (_activeProp == null ||
                !_activeProp.IsVisible)
            {
                throw new InvalidOperationException(
                    "The diagnostic prop is not visible.");
            }

            _activeProp.UpdateLayout();
            int width = Math.Max(
                1,
                (int)Math.Ceiling(
                    _activeProp.ActualWidth));
            int height = Math.Max(
                1,
                (int)Math.Ceiling(
                    _activeProp.ActualHeight));
            RenderTargetBitmap bitmap =
                new RenderTargetBitmap(
                    width,
                    height,
                    96.0,
                    96.0,
                    PixelFormats.Pbgra32);
            bitmap.Render(_activeProp);

            PngBitmapEncoder encoder =
                new PngBitmapEncoder();
            encoder.Frames.Add(
                BitmapFrame.Create(bitmap));
            string path = Path.Combine(
                _diagnosticPreviewDirectory,
                fileName);
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        private void ValidateActivePropForDiagnostic(
            string label)
        {
            if (_activeProp == null)
            {
                throw new InvalidOperationException(
                    "The diagnostic prop was not created.");
            }

            NativeRect petBounds;
            if (!TryGetPetWindowRect(out petBounds))
            {
                throw new InvalidOperationException(
                    "The diagnostic pet bounds are unavailable.");
            }

            Drawing.Rectangle propBounds =
                _activeProp.GetPixelBounds();
            Drawing.Rectangle petRectangle =
                Drawing.Rectangle.FromLTRB(
                    petBounds.Left,
                    petBounds.Top,
                    petBounds.Right,
                    petBounds.Bottom);
            Drawing.Rectangle workArea =
                Forms.Screen.FromRectangle(
                    petRectangle).WorkingArea;
            double physicalScale =
                petRectangle.Width / BaseWidth;
            double expectedSize =
                128.0 * physicalScale;
            double tolerance =
                Math.Max(3.0, expectedSize * 0.045);
            bool sizeIsCorrect =
                Math.Abs(
                    propBounds.Width -
                    expectedSize) <= tolerance &&
                Math.Abs(
                    propBounds.Height -
                    expectedSize) <= tolerance;
            bool isInsideWorkArea =
                propBounds.Left >= workArea.Left - 1 &&
                propBounds.Top >= workArea.Top - 1 &&
                propBounds.Right <= workArea.Right + 1 &&
                propBounds.Bottom <= workArea.Bottom + 1;

            string report = String.Format(
                CultureInfo.InvariantCulture,
                "{0}: actual={1}x{2}, expected={3:0.0}, workArea={4},{5},{6},{7}, pass={8}\r\n",
                label,
                propBounds.Width,
                propBounds.Height,
                expectedSize,
                workArea.Left,
                workArea.Top,
                workArea.Width,
                workArea.Height,
                sizeIsCorrect && isInsideWorkArea);
            File.AppendAllText(
                Path.Combine(
                    _diagnosticPreviewDirectory,
                    "prop-diagnostics.txt"),
                report);

            if (!sizeIsCorrect ||
                !isInsideWorkArea)
            {
                throw new InvalidOperationException(
                    "The diagnostic prop failed size or placement validation.");
            }
        }

        private static WpfBrush MakeBrush(byte red, byte green, byte blue)
        {
            return MakeBrush(red, green, blue, 255);
        }

        private static WpfBrush MakeBrush(byte red, byte green, byte blue, byte alpha)
        {
            SolidColorBrush brush = new SolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static WpfBrush MakeOrangeFurBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.15, 0.05);
            brush.EndPoint = new Point(0.85, 0.95);
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 190, 75), 0.0));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(244, 139, 41), 0.52));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(224, 104, 27), 1.0));
            brush.Freeze();
            return brush;
        }

        private static WpfBrush MakeGreenIrisBrush()
        {
            RadialGradientBrush brush = new RadialGradientBrush();
            brush.GradientOrigin = new Point(0.34, 0.30);
            brush.Center = new Point(0.5, 0.5);
            brush.RadiusX = 0.66;
            brush.RadiusY = 0.66;
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(169, 223, 121), 0.0));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(75, 145, 91), 0.58));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(34, 74, 58), 1.0));
            brush.Freeze();
            return brush;
        }

        private static WpfBrush MakeAmbientGlowBrush()
        {
            RadialGradientBrush brush = new RadialGradientBrush();
            brush.Center = new Point(0.5, 0.52);
            brush.GradientOrigin = brush.Center;
            brush.RadiusX = 0.52;
            brush.RadiusY = 0.52;
            brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(42, 255, 225, 169), 0.0));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(18, 255, 225, 169), 0.66));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0, 255, 225, 169), 1.0));
            brush.Freeze();
            return brush;
        }

        private static WpfBrush MakeBongoDeskBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.10, 0.0);
            brush.EndPoint = new Point(0.90, 1.0);
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(255, 232, 202), 0.0));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(224, 184, 148), 0.58));
            brush.GradientStops.Add(new GradientStop(WpfColor.FromRgb(191, 139, 104), 1.0));
            brush.Freeze();
            return brush;
        }

        private static WpfBrush MakeSoftSurfaceBrush(
            byte topRed,
            byte topGreen,
            byte topBlue,
            byte bottomRed,
            byte bottomGreen,
            byte bottomBlue)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.18, 0.0);
            brush.EndPoint = new Point(0.82, 1.0);
            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromRgb(topRed, topGreen, topBlue),
                    0.0));
            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromRgb(
                        (byte)((topRed + bottomRed) / 2),
                        (byte)((topGreen + bottomGreen) / 2),
                        (byte)((topBlue + bottomBlue) / 2)),
                    0.52));
            brush.GradientStops.Add(
                new GradientStop(
                    WpfColor.FromRgb(bottomRed, bottomGreen, bottomBlue),
                    1.0));
            brush.Freeze();
            return brush;
        }

        private sealed class BongoKeyCap
        {
            public Rect Bounds;
            public string Label;
            public int[] VirtualKeys;
            public DrawingGroup NormalLabel;
            public DrawingGroup ActiveLabel;

            public bool Matches(int virtualKey)
            {
                for (int index = 0; index < VirtualKeys.Length; index++)
                {
                    if (VirtualKeys[index] == virtualKey)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private enum PetHitZone
        {
            None,
            Head,
            Body,
            Tail,
            Bubble,
            BongoMouse,
            BongoKeyboard,
            BongoDesk
        }

        private static BongoKeyCap[] CreateBongoKeyCaps()
        {
            List<BongoKeyCap> keys = new List<BongoKeyCap>();
            const double keyHeight = 8.4;
            const double row0 = 201.0;
            const double row1 = 210.7;
            const double row2 = 220.4;
            const double row3 = 230.1;
            const double row4 = 239.8;

            string digits = "1234567890";
            for (int index = 0; index < digits.Length; index++)
            {
                int mainVirtualKey = index == 9
                    ? 0x30
                    : 0x31 + index;
                int numberPadVirtualKey = index == 9
                    ? 0x60
                    : 0x61 + index;
                AddBongoKeyCap(
                    keys,
                    new Rect(68.0 + index * 12.25, row0, 11.1, keyHeight),
                    digits[index].ToString(),
                    4.5,
                    mainVirtualKey,
                    numberPadVirtualKey);
            }
            AddBongoKeyCap(
                keys,
                new Rect(190.5, row0, 21.5, keyHeight),
                "BK",
                3.3,
                0x08);

            AddBongoKeyCap(
                keys,
                new Rect(68.0, row1, 15.0, keyHeight),
                "Tab",
                3.1,
                0x09);
            AddBongoLetterRow(
                keys,
                "QWERTYUIOP",
                84.2,
                row1,
                11.6,
                1.1,
                keyHeight);

            AddBongoKeyCap(
                keys,
                new Rect(68.0, row2, 17.0, keyHeight),
                "CAP",
                3.0,
                0x14);
            AddBongoLetterRow(
                keys,
                "ASDFGHJKL",
                86.2,
                row2,
                11.5,
                1.1,
                keyHeight);
            AddBongoKeyCap(
                keys,
                new Rect(199.7, row2, 12.3, keyHeight),
                "ENT",
                3.0,
                0x0D);

            AddBongoKeyCap(
                keys,
                new Rect(70.0, row3, 18.0, keyHeight),
                "⇧",
                4.8,
                0x10,
                0xA0,
                0xA1);
            AddBongoLetterRow(
                keys,
                "ZXCVBNM",
                89.2,
                row3,
                11.5,
                1.1,
                keyHeight);
            AddBongoKeyCap(
                keys,
                new Rect(177.5, row3, 10.0, keyHeight),
                ",",
                4.8,
                0xBC);
            AddBongoKeyCap(
                keys,
                new Rect(188.6, row3, 10.0, keyHeight),
                ".",
                4.8,
                0xBE,
                0x6E);
            AddBongoKeyCap(
                keys,
                new Rect(199.7, row3, 10.0, keyHeight),
                "/",
                4.5,
                0xBF,
                0x6F);

            AddBongoKeyCap(
                keys,
                new Rect(68.0, row4, 16.0, keyHeight),
                "Ctrl",
                2.9,
                0x11,
                0xA2,
                0xA3);
            AddBongoKeyCap(
                keys,
                new Rect(85.2, row4, 14.0, keyHeight),
                "Alt",
                3.1,
                0x12,
                0xA4,
                0xA5);
            AddBongoKeyCap(
                keys,
                new Rect(100.4, row4, 51.0, keyHeight),
                "SPACE",
                3.1,
                0x20);
            AddBongoKeyCap(
                keys,
                new Rect(152.6, row4, 10.0, keyHeight),
                "←",
                4.6,
                0x25);
            AddBongoKeyCap(
                keys,
                new Rect(163.7, row4, 10.0, keyHeight),
                "↓",
                4.6,
                0x28);
            AddBongoKeyCap(
                keys,
                new Rect(174.8, row4, 10.0, keyHeight),
                "↑",
                4.6,
                0x26);
            AddBongoKeyCap(
                keys,
                new Rect(185.9, row4, 10.0, keyHeight),
                "→",
                4.6,
                0x27);
            AddBongoKeyCap(
                keys,
                new Rect(197.1, row4, 14.0, keyHeight),
                "Esc",
                3.0,
                0x1B);

            return keys.ToArray();
        }

        private static void AddBongoLetterRow(
            List<BongoKeyCap> keys,
            string letters,
            double startX,
            double y,
            double keyWidth,
            double gap,
            double keyHeight)
        {
            for (int index = 0; index < letters.Length; index++)
            {
                char letter = letters[index];
                AddBongoKeyCap(
                    keys,
                    new Rect(
                        startX + index * (keyWidth + gap),
                        y,
                        keyWidth,
                        keyHeight),
                    letter.ToString(),
                    4.7,
                    (int)letter);
            }
        }

        private static void AddBongoKeyCap(
            List<BongoKeyCap> keys,
            Rect bounds,
            string label,
            double fontSize,
            params int[] virtualKeys)
        {
            bounds = FaceKeyboardBoundsTowardCat(bounds);
            BongoKeyCap key = new BongoKeyCap();
            key.Bounds = bounds;
            key.Label = label;
            key.VirtualKeys = virtualKeys;
            key.NormalLabel = CreateBongoKeyLabel(
                label,
                bounds,
                fontSize,
                BongoKeyTextBrush);
            key.ActiveLabel = CreateBongoKeyLabel(
                label,
                bounds,
                fontSize,
                BongoActiveKeyTextBrush);
            keys.Add(key);
        }

        private static Rect FaceKeyboardBoundsTowardCat(Rect bounds)
        {
            const double centerX = 140.0;
            const double centerY = 226.0;
            return new Rect(
                centerX * 2.0 - bounds.Right,
                centerY * 2.0 - bounds.Bottom,
                bounds.Width,
                bounds.Height);
        }

        private static DrawingGroup CreateBongoKeyLabel(
            string label,
            Rect bounds,
            double fontSize,
            WpfBrush brush)
        {
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                RotateTransform faceCat = CreateFrozenRotateTransform(
                    180.0,
                    bounds.X + bounds.Width * 0.5,
                    bounds.Y + bounds.Height * 0.5);
                dc.PushTransform(faceCat);
                FormattedText text = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    BongoKeyTypeface,
                    fontSize,
                    brush,
                    1.0);
                dc.DrawText(
                    text,
                    new Point(
                        bounds.X +
                        (bounds.Width - text.WidthIncludingTrailingWhitespace) * 0.5,
                        bounds.Y +
                        (bounds.Height - text.Height) * 0.5 -
                        0.15));
                dc.Pop();
            }
            group.Freeze();
            return group;
        }

        private static StreamGeometry CreateBongoMouseBodyGeometry()
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(36, 198), true, true);
                context.BezierTo(
                    new Point(23, 198),
                    new Point(14, 207),
                    new Point(13, 222),
                    true,
                    false);
                context.BezierTo(
                    new Point(12, 238),
                    new Point(16, 249),
                    new Point(26, 253),
                    true,
                    false);
                context.BezierTo(
                    new Point(32, 255),
                    new Point(41, 255),
                    new Point(47, 252),
                    true,
                    false);
                context.BezierTo(
                    new Point(56, 248),
                    new Point(60, 237),
                    new Point(59, 222),
                    true,
                    false);
                context.BezierTo(
                    new Point(58, 207),
                    new Point(49, 198),
                    new Point(36, 198),
                    true,
                    false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static StreamGeometry CreateBongoMouseButtonGeometry(bool right)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(36, 199), true, true);
                if (right)
                {
                    context.BezierTo(
                        new Point(48, 198),
                        new Point(56, 205),
                        new Point(59, 216),
                        true,
                        false);
                    context.BezierTo(
                        new Point(53, 220),
                        new Point(45, 222),
                        new Point(36, 222),
                        true,
                        false);
                }
                else
                {
                    context.BezierTo(
                        new Point(25, 198),
                        new Point(16, 205),
                        new Point(13, 216),
                        true,
                        false);
                    context.BezierTo(
                        new Point(19, 220),
                        new Point(27, 222),
                        new Point(36, 222),
                        true,
                        false);
                }
            }
            geometry.Freeze();
            return geometry;
        }

        private static DrawingGroup CreateBongoSurfaceDrawing()
        {
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                StreamGeometry desk = new StreamGeometry();
                using (StreamGeometryContext context = desk.Open())
                {
                    context.BeginFigure(new Point(7, 192), true, true);
                    context.LineTo(new Point(213, 192), true, false);
                    context.LineTo(new Point(220, 259), true, false);
                    context.LineTo(new Point(0, 259), true, false);
                }
                desk.Freeze();
                dc.DrawGeometry(BongoDeskBrush, BongoOutlinePen, desk);
                dc.DrawLine(
                    BongoKeyPen,
                    new Point(8, 195),
                    new Point(212, 195));
            }
            group.Freeze();
            return group;
        }

        private static DrawingGroup CreateBongoDeskDrawing()
        {
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                // The cat is the user: its palm rests on the rear of the mouse
                // (screen top), while the buttons, wheel and front cable point
                // away from the cat toward the viewer (screen bottom).
                StreamGeometry cable = new StreamGeometry();
                using (StreamGeometryContext context = cable.Open())
                {
                    context.BeginFigure(new Point(36, 253), false, false);
                    context.BezierTo(
                        new Point(36, 256),
                        new Point(31, 257),
                        new Point(33, 259),
                        true,
                        false);
                }
                cable.Freeze();
                dc.DrawGeometry(null, BongoMouseDetailPen, cable);
                dc.DrawEllipse(ShadowBrush, null, new Point(36, 254), 25, 3);
                dc.PushTransform(BongoMouseUserOrientationTransform);
                dc.DrawGeometry(BongoMouseBrush, null, BongoMouseBodyGeometry);
                dc.DrawGeometry(
                    BongoMouseButtonBrush,
                    null,
                    BongoMouseLeftButtonGeometry);
                dc.DrawGeometry(
                    BongoMouseButtonBrush,
                    null,
                    BongoMouseRightButtonGeometry);
                dc.DrawGeometry(null, BongoOutlinePen, BongoMouseBodyGeometry);
                dc.DrawLine(
                    BongoMouseDetailPen,
                    new Point(36, 199),
                    new Point(36, 222));

                StreamGeometry buttonSeam = new StreamGeometry();
                using (StreamGeometryContext context = buttonSeam.Open())
                {
                    context.BeginFigure(new Point(13, 222), false, false);
                    context.QuadraticBezierTo(
                        new Point(36, 228),
                        new Point(59, 222),
                        true,
                        false);
                }
                buttonSeam.Freeze();
                dc.DrawGeometry(null, BongoMouseDetailPen, buttonSeam);
                dc.DrawRoundedRectangle(
                    BongoMouseWheelSlotBrush,
                    null,
                    new Rect(31, 203, 10, 18),
                    5,
                    5);
                dc.DrawRoundedRectangle(
                    BongoMouseWheelBrush,
                    BongoMouseDetailPen,
                    new Rect(33, 205, 6, 13),
                    3,
                    3);
                dc.DrawLine(BongoMouseDetailPen, new Point(34, 208), new Point(38, 208));
                dc.DrawLine(BongoMouseDetailPen, new Point(34, 211), new Point(38, 211));
                dc.DrawLine(BongoMouseDetailPen, new Point(34, 214), new Point(38, 214));
                dc.DrawEllipse(
                    BongoRightActiveBrush,
                    null,
                    new Point(36, 247),
                    1.15,
                    1.15);
                dc.Pop();

                dc.DrawRoundedRectangle(
                    BongoKeyboardBrush,
                    BongoOutlinePen,
                    new Rect(64, 197, 152, 58),
                    7,
                    7);

                for (int index = 0; index < BongoKeyCaps.Length; index++)
                {
                    BongoKeyCap key = BongoKeyCaps[index];
                    dc.DrawRoundedRectangle(
                        BongoKeyBrush,
                        BongoKeyPen,
                        key.Bounds,
                        1.7,
                        1.7);
                    dc.DrawDrawing(key.NormalLabel);
                }
            }
            group.Freeze();
            return group;
        }

        private static RotateTransform CreateFrozenRotateTransform(
            double angle,
            double centerX,
            double centerY)
        {
            RotateTransform transform =
                new RotateTransform(angle, centerX, centerY);
            transform.Freeze();
            return transform;
        }

        private static TranslateTransform[] CreateBongoKeyDepthTransforms()
        {
            TranslateTransform[] transforms =
                new TranslateTransform[BongoKeyCaps.Length];
            for (int index = 0; index < transforms.Length; index++)
            {
                transforms[index] = new TranslateTransform();
            }
            return transforms;
        }

        private static Pen MakePen(WpfBrush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
            return pen;
        }

        private void LoadEmbeddedRig()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("NuoMiDesktopPet.OrangeKittenRig.png"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("内置橘猫动画素材缺失。");
                }

                PngBitmapDecoder decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapFrame sheet = decoder.Frames[0];

                _rigHead = CropAndFreeze(sheet, 86, 100, 535, 493);
                _rigBody = CropAndFreeze(sheet, 715, 150, 461, 471);
                _rigTail = MirrorHorizontallyAndFreeze(
                    CropAndFreeze(sheet, 142, 657, 323, 457));
                _rigLeftLeg = CropAndFreeze(sheet, 556, 703, 185, 413);
                _rigRightLeg = CropAndFreeze(sheet, 870, 703, 185, 413);
            }

            using (Stream stream = assembly.GetManifestResourceStream("NuoMiDesktopPet.OrangeKittenBlink.png"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("内置橘猫眨眼素材缺失。");
                }

                PngBitmapDecoder decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                _rigBlinkHead = CropAndFreeze(decoder.Frames[0], 86, 106, 533, 491);
            }

            using (Stream stream = assembly.GetManifestResourceStream("NuoMiDesktopPet.OrangeKittenBow.png"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("内置橘猫蝴蝶结素材缺失。");
                }

                PngBitmapDecoder decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                _rigBow = CropAndFreeze(decoder.Frames[0], 120, 286, 1015, 665);
            }
        }

        private static BitmapSource CropAndFreeze(BitmapSource source, int x, int y, int width, int height)
        {
            CroppedBitmap crop = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            crop.Freeze();
            return crop;
        }

        private static BitmapSource MirrorHorizontallyAndFreeze(BitmapSource source)
        {
            TransformedBitmap mirrored = new TransformedBitmap(
                source,
                new ScaleTransform(-1.0, 1.0));
            mirrored.Freeze();
            return mirrored;
        }

        private void PetSourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle =
                new WindowInteropHelper(this).Handle;
            _windowSource =
                HwndSource.FromHwnd(handle);
            if (_windowSource != null)
            {
                _windowSource.AddHook(PetWindowProc);
            }

            NativeRect rect;
            TryGetPetWindowRect(out rect);
            if (IsVisible)
            {
                EnsureVisibleOnAnyScreen();
            }
            else
            {
                _needsVisibilityCorrection = true;
            }
            StartFullscreenMonitoring();
        }

        private void PetWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && !_isExiting)
            {
                if (_needsVisibilityCorrection)
                {
                    _needsVisibilityCorrection = false;
                    EnsureVisibleOnAnyScreen();
                    if (!_isDiagnosticPreview)
                    {
                        SavePosition();
                    }
                }
                _behavior.AdvanceNeeds(DateTime.UtcNow, false);
                if (!_behavior.IsBusy)
                {
                    ScheduleNextAutonomous(
                        _clock.ElapsedMilliseconds,
                        5000,
                        10000);
                }
                StartInputMonitoring();
                StartRendering();
                ShowWelcomeIfNeeded();
            }
            else
            {
                StopInputMonitoring();
                CancelBehavior(_clock.ElapsedMilliseconds);
                SavePetState();
                StopRendering();
            }
            UpdateTrayDescription();
        }

        private void StartFullscreenMonitoring()
        {
            if (_isDiagnosticPreview ||
                _isExiting ||
                _fullscreenTimer != null)
            {
                return;
            }

            _fullscreenTimer =
                new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background,
                    Dispatcher);
            _fullscreenTimer.Interval =
                TimeSpan.FromMilliseconds(500);
            _fullscreenTimer.Tick += FullscreenMonitorTick;
            _fullscreenTimer.Start();
        }

        private void StopFullscreenMonitoring()
        {
            if (_fullscreenTimer == null)
            {
                return;
            }

            _fullscreenTimer.Stop();
            _fullscreenTimer.Tick -= FullscreenMonitorTick;
            _fullscreenTimer = null;
        }

        private void FullscreenMonitorTick(object sender, EventArgs e)
        {
            if (_isExiting ||
                _isDiagnosticPreview ||
                !_autoHideFullscreen)
            {
                return;
            }

            Drawing.Rectangle petBounds;
            if (!TryGetFullscreenPetBounds(out petBounds))
            {
                _lastFullscreenSample = FullscreenSample.Unknown;
                return;
            }

            IntPtr foregroundWindow;
            FullscreenSample sample = FullscreenDetector.Sample(
                petBounds,
                out foregroundWindow);
            _lastFullscreenSample = sample;
            long now = _clock.ElapsedMilliseconds;

            if (sample == FullscreenSample.FullscreenOnPetMonitor)
            {
                _fullscreenExitSamples = 0;
                if (_fullscreenBypassUntilExit &&
                    _bypassedFullscreenWindow != IntPtr.Zero &&
                    foregroundWindow != IntPtr.Zero &&
                    foregroundWindow !=
                        _bypassedFullscreenWindow)
                {
                    // A different foreground full-screen HWND marks a new
                    // movie/game session. A manual "show anyway" exception
                    // from the previous session must not leak into this one.
                    _fullscreenBypassUntilExit = false;
                    _bypassedFullscreenWindow = IntPtr.Zero;
                }

                _lastFullscreenWindow = foregroundWindow;
                if (_fullscreenBypassUntilExit &&
                    _bypassedFullscreenWindow == IntPtr.Zero)
                {
                    _bypassedFullscreenWindow = foregroundWindow;
                }

                if (_fullscreenBypassUntilExit ||
                    _userHidden ||
                    _fullscreenSuppressed ||
                    ShouldPostponeFullscreenHide())
                {
                    _fullscreenEnterSamples = 0;
                    return;
                }

                _fullscreenEnterSamples++;
                if (_fullscreenEnterSamples >= 2)
                {
                    _fullscreenEnterSamples = 0;
                    HideForFullscreen();
                }
                return;
            }

            if (sample != FullscreenSample.NotFullscreen)
            {
                return;
            }

            _fullscreenEnterSamples = 0;
            _fullscreenExitSamples++;
            bool hasExitedFullscreen =
                _fullscreenExitSamples >= 3;
            if (_fullscreenBypassUntilExit &&
                hasExitedFullscreen &&
                now >= _fullscreenBypassEarliestResetAt)
            {
                _fullscreenBypassUntilExit = false;
                _bypassedFullscreenWindow = IntPtr.Zero;
            }

            if (_fullscreenSuppressed &&
                !_userHidden &&
                hasExitedFullscreen)
            {
                _fullscreenExitSamples = 0;
                RestoreAfterFullscreen();
            }

            if (hasExitedFullscreen)
            {
                _lastFullscreenWindow = IntPtr.Zero;
            }
        }

        private bool TryGetFullscreenPetBounds(
            out Drawing.Rectangle bounds)
        {
            NativeRect nativeBounds;
            if (TryGetPetWindowRect(out nativeBounds))
            {
                bounds = Drawing.Rectangle.FromLTRB(
                    nativeBounds.Left,
                    nativeBounds.Top,
                    nativeBounds.Right,
                    nativeBounds.Bottom);
                return bounds.Width > 0 && bounds.Height > 0;
            }

            bounds = _lastPetPixelBounds;
            return _hasLastPetPixelBounds &&
                   bounds.Width > 0 &&
                   bounds.Height > 0;
        }

        private bool ShouldPostponeFullscreenHide()
        {
            return
                _isDragging ||
                _dragPending ||
                IsMouseCaptured ||
                (_petMenu != null && _petMenu.IsOpen) ||
                (_trayMenu != null && _trayMenu.Visible);
        }

        private void HideForFullscreen()
        {
            if (_userHidden ||
                _fullscreenBypassUntilExit ||
                _fullscreenSuppressed)
            {
                return;
            }

            NativeRect ignored;
            TryGetPetWindowRect(out ignored);
            _fullscreenSuppressed = true;
            CloseActiveProp();
            if (IsVisible)
            {
                Hide();
            }
            UpdateTrayDescription();
        }

        private void RestoreAfterFullscreen()
        {
            if (!_fullscreenSuppressed)
            {
                return;
            }

            _fullscreenSuppressed = false;
            if (!_userHidden)
            {
                ShowPetWindow();
            }
            UpdateTrayDescription();
        }

        private void ShowWelcomeIfNeeded()
        {
            if (_hasShownWelcome ||
                _isDiagnosticPreview ||
                _isExiting)
            {
                return;
            }

            if (_autoHideFullscreen)
            {
                Drawing.Rectangle petBounds;
                IntPtr foregroundWindow;
                if (TryGetFullscreenPetBounds(out petBounds))
                {
                    FullscreenSample sample =
                        FullscreenDetector.Sample(
                            petBounds,
                            out foregroundWindow);
                    _lastFullscreenSample = sample;
                    if (sample ==
                        FullscreenSample.FullscreenOnPetMonitor)
                    {
                        _lastFullscreenWindow =
                            foregroundWindow;
                        return;
                    }
                }
            }

            _hasShownWelcome = true;
            SaveSimpleSetting("HasShownWelcome", 1);
            ShowMessage(
                "右键我，可以互动和设置哦",
                5200,
                _clock.ElapsedMilliseconds);
            _notifyIcon.BalloonTipTitle = "你好，我是糯米";
            _notifyIcon.BalloonTipText =
                "按住我可以拖动搬家；如果隐藏了，双击右下角托盘图标，或者再次打开 EXE 就能把我叫回来。";
            _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(5200);
        }

        private void StartRendering()
        {
            if (_renderingSubscribed || _isExiting || !IsVisible)
            {
                return;
            }

            _hasLastRenderingTime = false;
            _renderingSubscribed = true;
            CompositionTarget.Rendering += AnimationTick;
            InvalidateVisual();
        }

        private void StopRendering()
        {
            if (_renderingSubscribed)
            {
                CompositionTarget.Rendering -= AnimationTick;
                _renderingSubscribed = false;
            }

            _hasLastRenderingTime = false;
        }

        private void PetWindowClosed(object sender, EventArgs e)
        {
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(PetWindowProc);
                _windowSource = null;
            }
            StopFullscreenMonitoring();
            DisposeInputMonitor();
            StopRendering();
            CloseActiveProp();
            IsVisibleChanged -= PetWindowIsVisibleChanged;
            SourceInitialized -= PetSourceInitialized;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            Closed -= PetWindowClosed;
        }

        private void BuildPetMenu()
        {
            _petMenu = new WpfContextMenu();
            _petMenu.FontFamily = new FontFamily("Microsoft YaHei UI");
            _petMenu.FontSize = 13;

            _showHideMenuItem = AddPetMenuItem(
                "隐藏到托盘（继续运行）",
                TogglePetVisibility);
            _showHideMenuItem.Tag = "NuoMi.Accent";

            WpfMenuItem interactionRoot = new WpfMenuItem();
            interactionRoot.Header = "和糯米互动";
            AddChildMenuItem(
                interactionRoot,
                "打个招呼",
                ShowGreeting);
            AddChildMenuItem(
                interactionRoot,
                "摸摸头",
                PetTheCat);
            AddChildMenuItem(
                interactionRoot,
                "喂小鱼干",
                FeedTheCat);
            AddChildMenuItem(
                interactionRoot,
                "逗它玩",
                PlayWithTheCat);
            AddChildMenuItem(
                interactionRoot,
                "放个杯子",
                PlaceCupForCat);
            AddChildMenuItem(
                interactionRoot,
                "查看状态",
                ShowPetStatus);
            _petMenu.Items.Add(interactionRoot);

            _petMenu.Items.Add(new WpfSeparator());

            _bongoModeMenuItem = AddPetMenuItem(
                "键盘鼠标互动（Bongo）",
                delegate
            {
                SetBongoMode(_bongoModeMenuItem.IsChecked);
            });
            _bongoModeMenuItem.IsCheckable = true;

            _autoInteractionMenuItem = AddPetMenuItem("自己活动", delegate
            {
                _autoInteraction = _autoInteractionMenuItem.IsChecked;
                SaveSimpleSetting(
                    "AutoInteraction",
                    _autoInteraction ? 1 : 0);
                if (!_autoInteraction)
                {
                    CancelBehavior(_clock.ElapsedMilliseconds);
                }
                else
                {
                    ScheduleNextAutonomous(
                        _clock.ElapsedMilliseconds,
                        5000,
                        10000);
                }
            });
            _autoInteractionMenuItem.IsCheckable = true;

            _motionPersonalityRootMenuItem =
                new WpfMenuItem();
            _motionPersonalityRootMenuItem.Header =
                "陪伴风格";
            AddMotionPersonalityMenuItem(
                _motionPersonalityRootMenuItem,
                "安静陪伴",
                MotionPersonality.Quiet);
            AddMotionPersonalityMenuItem(
                _motionPersonalityRootMenuItem,
                "自然（推荐）",
                MotionPersonality.Natural);
            AddMotionPersonalityMenuItem(
                _motionPersonalityRootMenuItem,
                "活泼",
                MotionPersonality.Playful);
            _petMenu.Items.Add(
                _motionPersonalityRootMenuItem);

            _petMenu.Items.Add(new WpfSeparator());

            _sizeRootMenuItem = new WpfMenuItem();
            AddSizeMenuItem(_sizeRootMenuItem, "迷你 60%", 60);
            AddSizeMenuItem(_sizeRootMenuItem, "小巧 80%", 80);
            AddSizeMenuItem(_sizeRootMenuItem, "标准 100%", 100);
            AddSizeMenuItem(_sizeRootMenuItem, "大号 125%", 125);
            AddSizeMenuItem(_sizeRootMenuItem, "特大 150%", 150);
            _sizeRootMenuItem.Items.Add(new WpfSeparator());
            AddChildMenuItem(
                _sizeRootMenuItem,
                "自定义百分比…",
                ShowScaleDialog);
            _petMenu.Items.Add(_sizeRootMenuItem);

            WpfMenuItem moreSettingsRoot =
                new WpfMenuItem();
            moreSettingsRoot.Header = "更多设置";

            _followMenuItem = AddChildMenuItem(
                moreSettingsRoot,
                "跟随鼠标转头",
                delegate
                {
                    _followMouse = _followMenuItem.IsChecked;
                    SaveSimpleSetting(
                        "FollowMouse",
                        _followMouse ? 1 : 0);
                });
            _followMenuItem.IsCheckable = true;

            _topmostMenuItem = AddChildMenuItem(
                moreSettingsRoot,
                "保持在其他窗口前面",
                delegate
                {
                    Topmost = _topmostMenuItem.IsChecked;
                    if (_activeProp != null)
                    {
                        _activeProp.Topmost = Topmost;
                    }
                    SaveSimpleSetting(
                        "Topmost",
                        Topmost ? 1 : 0);
                });
            _topmostMenuItem.IsCheckable = true;

            _autoHideFullscreenMenuItem =
                AddChildMenuItem(
                    moreSettingsRoot,
                    "全屏时自动隐藏",
                    delegate
                    {
                        SetAutoHideFullscreen(
                            _autoHideFullscreenMenuItem
                                .IsChecked);
                    });
            _autoHideFullscreenMenuItem.IsCheckable = true;

            _startupMenuItem = AddChildMenuItem(
                moreSettingsRoot,
                "开机自动显示糯米",
                delegate
                {
                    SetStartupEnabled(
                        _startupMenuItem.IsChecked);
                    _startupMenuItem.IsChecked =
                        IsStartupEnabled();
                });
            _startupMenuItem.IsCheckable = true;
            moreSettingsRoot.Items.Add(new WpfSeparator());
            AddChildMenuItem(
                moreSettingsRoot,
                "恢复推荐设置…",
                ResetRecommendedSettings);
            _petMenu.Items.Add(moreSettingsRoot);

            _petMenu.Items.Add(new WpfSeparator());

            AddPetMenuItem(
                "找回糯米（移到主屏幕）",
                MoveToPrimaryScreen);
            AddPetMenuItem("使用帮助", ShowHelp);
            AddPetMenuItem("关于糯米", ShowAbout);
            _petMenu.Items.Add(new WpfSeparator());

            WpfMenuItem exitItem = AddPetMenuItem(
                "退出程序（停止运行）",
                ExitApplication);
            exitItem.Tag = "NuoMi.Danger";

            ContextMenuOpening += delegate
            {
                PauseAutonomousForMenu();
                RefreshMenuState();
                FlatContextMenuStyle.PrepareForOpening(
                    _petMenu,
                    this);
            };

            FlatContextMenuStyle.Apply(_petMenu);
            ContextMenu = _petMenu;
            RefreshMenuState();
        }

        private WpfMenuItem AddPetMenuItem(string header, Action action)
        {
            WpfMenuItem item = new WpfMenuItem();
            item.Header = header;
            item.Click += delegate
            {
                action();
            };
            _petMenu.Items.Add(item);
            return item;
        }

        private static WpfMenuItem AddChildMenuItem(
            WpfMenuItem parent,
            string header,
            Action action)
        {
            WpfMenuItem item = new WpfMenuItem();
            item.Header = header;
            item.Click += delegate
            {
                action();
            };
            parent.Items.Add(item);
            return item;
        }

        private void AddSizeMenuItem(
            WpfMenuItem parent,
            string header,
            int percentage)
        {
            WpfMenuItem item = new WpfMenuItem();
            item.Header = header;
            item.IsCheckable = true;
            item.Tag = percentage;
            item.Click += delegate
            {
                SetUserScale(percentage / 100.0);
            };
            parent.Items.Add(item);
            _sizeMenuItems.Add(item);
        }

        private void AddMotionPersonalityMenuItem(
            WpfMenuItem parent,
            string header,
            MotionPersonality personality)
        {
            WpfMenuItem item = new WpfMenuItem();
            item.Header = header;
            item.IsCheckable = true;
            item.Tag = personality;
            item.Click += delegate
            {
                SetMotionPersonality(personality);
            };
            parent.Items.Add(item);
            _motionPersonalityMenuItems.Add(item);
        }

        private void BuildTrayMenu()
        {
            _trayMenu =
                TrayMenuTheme.CreateContextMenu();
            _trayMenu.ShowImageMargin = false;
            _trayMenu.ShowCheckMargin = true;

            _trayShowHideItem = new Forms.ToolStripMenuItem();
            _trayShowHideItem.Tag = "NuoMi.Accent";
            _trayShowHideItem.Click += delegate
            {
                Dispatcher.BeginInvoke(
                    (Action)TogglePetVisibility);
            };
            _trayMenu.Items.Add(_trayShowHideItem);

            Forms.ToolStripMenuItem interactionRoot =
                new Forms.ToolStripMenuItem(
                    "和糯米互动");
            AddTrayAction(
                interactionRoot.DropDownItems,
                "打个招呼",
                ShowGreeting);
            AddTrayAction(
                interactionRoot.DropDownItems,
                "摸摸头",
                PetTheCat);
            AddTrayAction(
                interactionRoot.DropDownItems,
                "喂小鱼干",
                FeedTheCat);
            AddTrayAction(
                interactionRoot.DropDownItems,
                "逗它玩",
                PlayWithTheCat);
            AddTrayAction(
                interactionRoot.DropDownItems,
                "放个杯子",
                PlaceCupForCat);
            AddTrayAction(
                interactionRoot.DropDownItems,
                "查看状态",
                ShowPetStatus);
            _trayMenu.Items.Add(interactionRoot);

            _trayMenu.Items.Add(
                new Forms.ToolStripSeparator());

            _trayBongoModeItem =
                new Forms.ToolStripMenuItem(
                    "键盘鼠标互动（Bongo）");
            _trayBongoModeItem.CheckOnClick = true;
            _trayBongoModeItem.Click += delegate
            {
                bool desired = _trayBongoModeItem.Checked;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetBongoMode(desired);
                });
            };
            _trayMenu.Items.Add(_trayBongoModeItem);

            _trayAutoInteractionItem =
                new Forms.ToolStripMenuItem(
                    "自己活动");
            _trayAutoInteractionItem.CheckOnClick = true;
            _trayAutoInteractionItem.Click += delegate
            {
                bool desired =
                    _trayAutoInteractionItem.Checked;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    _autoInteraction = desired;
                    SaveSimpleSetting(
                        "AutoInteraction",
                        _autoInteraction ? 1 : 0);
                    if (!_autoInteraction)
                    {
                        CancelBehavior(
                            _clock.ElapsedMilliseconds);
                    }
                    else
                    {
                        ScheduleNextAutonomous(
                            _clock.ElapsedMilliseconds,
                            5000,
                            10000);
                    }
                });
            };
            _trayMenu.Items.Add(
                _trayAutoInteractionItem);

            _trayMotionPersonalityRoot =
                new Forms.ToolStripMenuItem(
                    "陪伴风格");
            AddTrayMotionPersonalityItem(
                "安静陪伴",
                MotionPersonality.Quiet);
            AddTrayMotionPersonalityItem(
                "自然（推荐）",
                MotionPersonality.Natural);
            AddTrayMotionPersonalityItem(
                "活泼",
                MotionPersonality.Playful);
            _trayMenu.Items.Add(_trayMotionPersonalityRoot);

            _trayMenu.Items.Add(
                new Forms.ToolStripSeparator());

            _traySizeRoot = new Forms.ToolStripMenuItem();
            AddTraySizeItem("迷你 60%", 60);
            AddTraySizeItem("小巧 80%", 80);
            AddTraySizeItem("标准 100%", 100);
            AddTraySizeItem("大号 125%", 125);
            AddTraySizeItem("特大 150%", 150);
            _traySizeRoot.DropDownItems.Add(
                new Forms.ToolStripSeparator());
            _traySizeRoot.DropDownItems.Add(
                "自定义百分比…",
                null,
                delegate
                {
                    Dispatcher.BeginInvoke(
                        (Action)ShowScaleDialog);
                });
            _trayMenu.Items.Add(_traySizeRoot);

            Forms.ToolStripMenuItem moreSettingsRoot =
                new Forms.ToolStripMenuItem(
                    "更多设置");

            _trayFollowItem =
                new Forms.ToolStripMenuItem(
                    "跟随鼠标转头");
            _trayFollowItem.CheckOnClick = true;
            _trayFollowItem.Click += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    _followMouse =
                        _trayFollowItem.Checked;
                    SaveSimpleSetting(
                        "FollowMouse",
                        _followMouse ? 1 : 0);
                });
            };
            moreSettingsRoot.DropDownItems.Add(
                _trayFollowItem);

            _trayTopmostItem =
                new Forms.ToolStripMenuItem(
                    "保持在其他窗口前面");
            _trayTopmostItem.CheckOnClick = true;
            _trayTopmostItem.Click += delegate
            {
                bool desired =
                    _trayTopmostItem.Checked;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    Topmost = desired;
                    if (_activeProp != null)
                    {
                        _activeProp.Topmost = Topmost;
                    }
                    SaveSimpleSetting(
                        "Topmost",
                        Topmost ? 1 : 0);
                });
            };
            moreSettingsRoot.DropDownItems.Add(
                _trayTopmostItem);

            _trayAutoHideFullscreenItem =
                new Forms.ToolStripMenuItem(
                    "全屏时自动隐藏");
            _trayAutoHideFullscreenItem.CheckOnClick = true;
            _trayAutoHideFullscreenItem.Click += delegate
            {
                bool desired =
                    _trayAutoHideFullscreenItem.Checked;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetAutoHideFullscreen(desired);
                });
            };
            moreSettingsRoot.DropDownItems.Add(
                _trayAutoHideFullscreenItem);

            _trayStartupItem =
                new Forms.ToolStripMenuItem(
                    "开机自动显示糯米");
            _trayStartupItem.CheckOnClick = true;
            _trayStartupItem.Click += delegate
            {
                bool desired =
                    _trayStartupItem.Checked;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetStartupEnabled(desired);
                });
            };
            moreSettingsRoot.DropDownItems.Add(
                _trayStartupItem);
            moreSettingsRoot.DropDownItems.Add(
                new Forms.ToolStripSeparator());
            AddTrayAction(
                moreSettingsRoot.DropDownItems,
                "恢复推荐设置…",
                ResetRecommendedSettings);
            _trayMenu.Items.Add(moreSettingsRoot);

            _trayMenu.Items.Add(
                new Forms.ToolStripSeparator());

            AddTrayAction(
                _trayMenu.Items,
                "找回糯米（移到主屏幕）",
                MoveToPrimaryScreen);
            AddTrayAction(
                _trayMenu.Items,
                "使用帮助",
                ShowHelp);
            AddTrayAction(
                _trayMenu.Items,
                "关于糯米",
                ShowAbout);

            _trayMenu.Items.Add(
                new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem exitItem =
                AddTrayAction(
                    _trayMenu.Items,
                    "退出程序（停止运行）",
                    ExitApplication);
            exitItem.Tag = "NuoMi.Danger";

            _trayMenu.Opening += delegate
            {
                Dispatcher.BeginInvoke(
                    (Action)PauseAutonomousForMenu);
                RefreshTrayMenuState();
            };
            _notifyIcon.ContextMenuStrip = _trayMenu;
        }

        private Forms.ToolStripMenuItem AddTrayAction(
            Forms.ToolStripItemCollection items,
            string text,
            Action action)
        {
            Forms.ToolStripMenuItem item =
                new Forms.ToolStripMenuItem(text);
            item.Click += delegate
            {
                Dispatcher.BeginInvoke(action);
            };
            items.Add(item);
            return item;
        }

        private void PauseAutonomousForMenu()
        {
            if (_behavior.IsBusy &&
                !_behaviorWasUserRequested)
            {
                CancelBehavior(
                    _clock.ElapsedMilliseconds);
            }
        }

        private void AddTrayMotionPersonalityItem(
            string text,
            MotionPersonality personality)
        {
            Forms.ToolStripMenuItem item =
                new Forms.ToolStripMenuItem(text);
            item.Tag = personality;
            item.Click += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetMotionPersonality(personality);
                });
            };
            _trayMotionPersonalityRoot.DropDownItems.Add(item);
            _trayMotionPersonalityItems.Add(item);
        }

        private void AddTraySizeItem(
            string text,
            int percentage)
        {
            Forms.ToolStripMenuItem item =
                new Forms.ToolStripMenuItem(text);
            item.Tag = percentage;
            item.Click += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetUserScale(percentage / 100.0);
                });
            };
            _traySizeRoot.DropDownItems.Add(item);
            _traySizeItems.Add(item);
        }

        private void NotifyIconMouseDoubleClick(object sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.BeginInvoke(
                    (Action)ShowFromExternalRequest);
            }
        }

        private void RefreshMenuState()
        {
            _showHideMenuItem.Header = GetShowHideMenuText();
            _followMenuItem.IsChecked = _followMouse;
            _startupMenuItem.IsChecked = IsStartupEnabled();
            _topmostMenuItem.IsChecked = Topmost;
            _autoHideFullscreenMenuItem.IsChecked =
                _autoHideFullscreen;
            _bongoModeMenuItem.IsChecked = _bongoMode;
            _autoInteractionMenuItem.IsChecked = _autoInteraction;
            _motionPersonalityRootMenuItem.Header =
                "陪伴风格 · " +
                GetMotionPersonalityLabel();
            int currentPercentage = GetCurrentScalePercentage();
            _sizeRootMenuItem.Header =
                "大小 · " +
                currentPercentage.ToString(CultureInfo.InvariantCulture) +
                "%";

            for (int i = 0; i < _sizeMenuItems.Count; i++)
            {
                int itemPercentage = (int)_sizeMenuItems[i].Tag;
                _sizeMenuItems[i].IsChecked =
                    itemPercentage == currentPercentage;
            }

            for (int i = 0; i < _motionPersonalityMenuItems.Count; i++)
            {
                MotionPersonality personality =
                    (MotionPersonality)_motionPersonalityMenuItems[i].Tag;
                _motionPersonalityMenuItems[i].IsChecked =
                    personality == _interactionMotion.Personality;
            }
        }

        private void RefreshTrayMenuState()
        {
            _trayShowHideItem.Text = GetShowHideMenuText();
            _trayFollowItem.Checked = _followMouse;
            _trayStartupItem.Checked = IsStartupEnabled();
            _trayTopmostItem.Checked = Topmost;
            _trayAutoHideFullscreenItem.Checked =
                _autoHideFullscreen;
            _trayBongoModeItem.Checked = _bongoMode;
            _trayAutoInteractionItem.Checked = _autoInteraction;
            _trayMotionPersonalityRoot.Text =
                "陪伴风格 · " +
                GetMotionPersonalityLabel();
            int currentPercentage = GetCurrentScalePercentage();
            _traySizeRoot.Text =
                "大小 · " +
                currentPercentage.ToString(CultureInfo.InvariantCulture) +
                "%";

            for (int i = 0; i < _traySizeItems.Count; i++)
            {
                Forms.ToolStripMenuItem item =
                    _traySizeItems[i];
                item.Checked =
                    (int)item.Tag == currentPercentage;
            }

            for (int i = 0; i < _trayMotionPersonalityItems.Count; i++)
            {
                Forms.ToolStripMenuItem item =
                    _trayMotionPersonalityItems[i];
                item.Checked =
                    (MotionPersonality)item.Tag ==
                    _interactionMotion.Personality;
            }
        }

        private string GetShowHideMenuText()
        {
            if (IsVisible)
            {
                return "隐藏到托盘（继续运行）";
            }
            if (_fullscreenSuppressed)
            {
                return "全屏中暂时隐藏（点击仍显示）";
            }
            return "显示糯米";
        }

        private string GetMotionPersonalityLabel()
        {
            switch (_interactionMotion.Personality)
            {
                case MotionPersonality.Quiet:
                    return "安静";
                case MotionPersonality.Playful:
                    return "活泼";
                default:
                    return "自然";
            }
        }

        private void UpdateTrayDescription()
        {
            if (_notifyIcon == null)
            {
                return;
            }

            if (_fullscreenSuppressed)
            {
                _notifyIcon.Text =
                    "糯米全屏时暂时隐藏｜双击显示";
            }
            else if (!IsVisible)
            {
                _notifyIcon.Text =
                    "糯米正在后台｜双击显示，右键打开菜单";
            }
            else
            {
                _notifyIcon.Text =
                    "糯米桌面宠物｜右键互动和设置";
            }
        }

        private void SetAutoHideFullscreen(bool enabled)
        {
            _autoHideFullscreen = enabled;
            SaveSimpleSetting(
                "AutoHideFullscreen",
                enabled ? 1 : 0);
            _fullscreenEnterSamples = 0;
            _fullscreenExitSamples = 0;
            _lastFullscreenWindow = IntPtr.Zero;
            _bypassedFullscreenWindow = IntPtr.Zero;

            if (!enabled)
            {
                _fullscreenBypassUntilExit = false;
                if (_fullscreenSuppressed && !_userHidden)
                {
                    RestoreAfterFullscreen();
                }
                else
                {
                    _fullscreenSuppressed = false;
                }
            }

            RefreshMenuState();
            RefreshTrayMenuState();
            UpdateTrayDescription();
        }

        private int GetCurrentScalePercentage()
        {
            return (int)Math.Round(
                _userScale * 100.0,
                MidpointRounding.AwayFromZero);
        }

        private void ShowScaleDialog()
        {
            using (ScaleDialog dialog = new ScaleDialog(
                GetCurrentScalePercentage(),
                MinimumScalePercentage,
                MaximumScalePercentage))
            {
                dialog.TopMost = Topmost;
                Forms.DialogResult result;
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (IsVisible && handle != IntPtr.Zero)
                {
                    dialog.StartPosition =
                        Forms.FormStartPosition.CenterParent;
                    result = dialog.ShowDialog(
                        new WindowHandleOwner(handle));
                }
                else
                {
                    dialog.CenterOnCursorScreen();
                    result = dialog.ShowDialog();
                }

                if (result == Forms.DialogResult.OK)
                {
                    SetUserScale(
                        dialog.SelectedPercentage / 100.0);
                }
            }
        }

        private void ResetRecommendedSettings()
        {
            MessageBoxResult result =
                System.Windows.MessageBox.Show(
                    this,
                    "将恢复这些推荐设置：\n\n" +
                    "• 糯米大小 80%\n" +
                    "• 自然陪伴、自己活动和键盘鼠标互动\n" +
                    "• 跟随鼠标、保持在其他窗口前面\n" +
                    "• 全屏时自动隐藏\n\n" +
                    "亲密度、宠物状态和开机自启不会被清除。",
                    "恢复推荐设置",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _followMouse = true;
            Topmost = true;
            if (_activeProp != null)
            {
                _activeProp.Topmost = true;
            }
            _autoInteraction = true;
            SaveSimpleSetting("FollowMouse", 1);
            SaveSimpleSetting("Topmost", 1);
            SaveSimpleSetting("AutoInteraction", 1);

            SetAutoHideFullscreen(true);
            SetMotionPersonality(
                MotionPersonality.Natural);
            SetBongoMode(true);
            SetUserScale(0.80);
            ScheduleNextAutonomous(
                _clock.ElapsedMilliseconds,
                5000,
                10000);
            MoveToPrimaryScreen();
            RefreshMenuState();
            RefreshTrayMenuState();
            ShowMessage(
                "已经恢复推荐设置啦",
                2200,
                _clock.ElapsedMilliseconds);
            InvalidateVisual();
        }

        private void SetMotionPersonality(MotionPersonality personality)
        {
            _interactionMotion.Personality = personality;
            SaveSimpleSetting("MotionPersonality", (int)personality);
            RefreshMenuState();
            RefreshTrayMenuState();
            if (_autoInteraction && !_behavior.IsBusy)
            {
                ScheduleNextAutonomous(
                    _clock.ElapsedMilliseconds,
                    8000,
                    15000);
            }

            string message;
            switch (personality)
            {
                case MotionPersonality.Quiet:
                    message = "我会安静陪着你";
                    break;
                case MotionPersonality.Playful:
                    message = "今天要活泼一点！";
                    break;
                default:
                    message = "动作调到自然啦";
                    break;
            }
            ShowMessage(message, 1400, _clock.ElapsedMilliseconds);
        }

        private void SetBongoMode(bool enabled)
        {
            _bongoMode = enabled;
            SaveSimpleSetting("BongoMode", enabled ? 1 : 0);

            if (enabled)
            {
                if (_behavior.IsBusy && _behavior.Priority < 80)
                {
                    CancelBehavior(_clock.ElapsedMilliseconds);
                }
                StartInputMonitoring();
                if (_bongoMode)
                {
                    if (_autoInteraction)
                    {
                        ScheduleNextAutonomous(
                            _clock.ElapsedMilliseconds,
                            8000,
                            15000);
                    }
                    ShowMessage(
                        "键鼠同步开启啦",
                        1500,
                        _clock.ElapsedMilliseconds);
                }
            }
            else
            {
                StopInputMonitoring();
                if (_autoInteraction)
                {
                    ScheduleNextAutonomous(
                        _clock.ElapsedMilliseconds,
                        8000,
                        15000);
                }
                ShowMessage(
                    "去自由玩耍啦",
                    1500,
                    _clock.ElapsedMilliseconds);
            }

            RefreshMenuState();
            RefreshTrayMenuState();
            InvalidateVisual();
        }

        private void StartInputMonitoring()
        {
            if (_inputMonitoringStarted ||
                !_bongoMode ||
                !IsVisible ||
                _isExiting ||
                _isDiagnosticPreview ||
                _globalInputMonitor == null)
            {
                return;
            }

            try
            {
                _globalInputMonitor.Start();
                _inputMonitoringStarted = _globalInputMonitor.IsRunning;
                if (_inputMonitoringStarted)
                {
                    SynchronizeVisibleKeyState();
                }
            }
            catch (Exception ex)
            {
                _inputMonitoringStarted = false;
                _bongoMode = false;
                ClearBongoInputState(true);
                SaveSimpleSetting("BongoMode", 0);

                if (!_hasShownInputMonitorError)
                {
                    _notifyIcon.BalloonTipTitle = AppName;
                    _notifyIcon.BalloonTipText =
                        "键鼠互动暂时无法启动，糯米已切回自由活动模式。\n" +
                        ex.Message;
                    _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
                    _notifyIcon.ShowBalloonTip(2600);
                    _hasShownInputMonitorError = true;
                }
            }
        }

        private void StopInputMonitoring()
        {
            if (_globalInputMonitor == null)
            {
                _inputMonitoringStarted = false;
                ClearBongoInputState(true);
                return;
            }

            try
            {
                _globalInputMonitor.Stop();
            }
            catch
            {
                // Windows will reclaim any remaining hook when the process exits.
            }

            _inputMonitoringStarted = _globalInputMonitor.IsRunning;
            ClearBongoInputState(true);
        }

        private void DisposeInputMonitor()
        {
            GlobalInputMonitor monitor = _globalInputMonitor;
            if (monitor == null)
            {
                return;
            }

            _globalInputMonitor = null;
            monitor.KeyChanged -= GlobalKeyChanged;
            monitor.MouseButtonChanged -= GlobalMouseButtonChanged;
            monitor.MouseWheel -= GlobalMouseWheel;
            try
            {
                monitor.Dispose();
            }
            catch
            {
                // Process shutdown is still safe even if Windows rejects unhook.
            }

            _inputMonitoringStarted = false;
            ClearBongoInputState(true);
        }

        private void GlobalKeyChanged(int virtualKey, bool isDown)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        ApplyGlobalKeyChange(virtualKey, isDown);
                    });
                }
                return;
            }

            ApplyGlobalKeyChange(virtualKey, isDown);
        }

        private void ApplyGlobalKeyChange(int virtualKey, bool isDown)
        {
            if (!_bongoMode || !IsVisible || _isExiting)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            bool useRightPaw = UsesRightPawForVirtualKey(virtualKey);
            HashSet<int> keys = useRightPaw
                ? _rightKeysDown
                : _leftKeysDown;

            if (isDown)
            {
                bool isRepeat = !keys.Add(virtualKey);
                _keyPressedAtByVirtualKey[virtualKey] = now;
                _keyPulseUntilByVirtualKey[virtualKey] = now + 140;
                if (useRightPaw)
                {
                    _rightInputPulseUntil = now + 125;
                }
                else
                {
                    _leftInputPulseUntil = now + 125;
                }

                _interactionMotion.RegisterKeyDown(
                    virtualKey,
                    GetBongoKeyReach(virtualKey),
                    GetBongoKeyRow(virtualKey),
                    isRepeat,
                    now);
                if (!_settleBlinkArmed &&
                    _interactionMotion.TypingEnergy > 0.62 &&
                    _random.NextDouble() < 0.24)
                {
                    _settleBlinkArmed = true;
                    _settleBlinkDue = now + 950;
                }
                _lastBongoInputAt = now;
                InterruptAutonomousBehaviorForBongo(now);
            }
            else
            {
                bool wasHeld =
                    _leftKeysDown.Remove(virtualKey) |
                    _rightKeysDown.Remove(virtualKey);
                _keyPressedAtByVirtualKey.Remove(virtualKey);
                if (!wasHeld)
                {
                    return;
                }
                if (_leftKeysDown.Count == 0 &&
                    _rightKeysDown.Count == 0)
                {
                    _interactionMotion.RegisterKeyUp(now);
                }
                else
                {
                    RetargetToMostRecentHeldKey();
                }
            }
        }

        private void GlobalMouseButtonChanged(MouseInputKind button, bool isDown)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        ApplyGlobalMouseButtonChange(button, isDown);
                    });
                }
                return;
            }

            ApplyGlobalMouseButtonChange(button, isDown);
        }

        private void ApplyGlobalMouseButtonChange(
            MouseInputKind button,
            bool isDown)
        {
            if (!_bongoMode || !IsVisible || _isExiting)
            {
                return;
            }

            if (isDown && IsCursorInsidePetWindow())
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            switch (button)
            {
                case MouseInputKind.LeftButton:
                case MouseInputKind.XButton1:
                    bool wasLeftDown = _mouseLeftDown;
                    _mouseLeftDown = isDown;
                    if (isDown)
                    {
                        _mouseLeftPulseUntil = now + 145;
                        _mouseLeftAutoReleaseAt = now + 5000;
                    }
                    else
                    {
                        _mouseLeftAutoReleaseAt = 0;
                        if (wasLeftDown)
                        {
                            _interactionMotion.RegisterMouseUp(true, now);
                        }
                    }
                    break;

                case MouseInputKind.RightButton:
                case MouseInputKind.XButton2:
                    bool wasRightDown = _mouseRightDown;
                    _mouseRightDown = isDown;
                    if (isDown)
                    {
                        _mouseRightPulseUntil = now + 145;
                        _mouseRightAutoReleaseAt = now + 5000;
                    }
                    else
                    {
                        _mouseRightAutoReleaseAt = 0;
                        if (wasRightDown)
                        {
                            _interactionMotion.RegisterMouseUp(false, now);
                        }
                    }
                    break;

                case MouseInputKind.MiddleButton:
                    if (isDown)
                    {
                        _wheelPulseUntil = now + 190;
                    }
                    break;
            }

            if (isDown)
            {
                if (button == MouseInputKind.LeftButton ||
                    button == MouseInputKind.XButton1)
                {
                    _interactionMotion.RegisterMouseDown(true, now);
                }
                else if (button == MouseInputKind.RightButton ||
                    button == MouseInputKind.XButton2)
                {
                    _interactionMotion.RegisterMouseDown(false, now);
                }
                _lastBongoInputAt = now;
                InterruptAutonomousBehaviorForBongo(now);
            }
        }

        private bool IsCursorInsidePetWindow()
        {
            Drawing.Point cursor = Forms.Cursor.Position;
            Point relative;
            try
            {
                relative = PointFromScreen(
                    new Point(cursor.X, cursor.Y));
            }
            catch
            {
                return false;
            }

            return GetPetHitZone(relative) != PetHitZone.None;
        }

        private void GlobalMouseWheel(int delta)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        ApplyGlobalMouseWheel(delta);
                    });
                }
                return;
            }

            ApplyGlobalMouseWheel(delta);
        }

        private void ApplyGlobalMouseWheel(int delta)
        {
            if (!_bongoMode || !IsVisible || _isExiting || delta == 0)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            _wheelPulseUntil = now + 210;
            _interactionMotion.RegisterWheel(delta, now);
            _lastBongoInputAt = now;
            InterruptAutonomousBehaviorForBongo(now);
        }

        private void InterruptAutonomousBehaviorForBongo(long now)
        {
            if (_behavior.IsBusy &&
                _behavior.Priority < 80 &&
                IsBehaviorConflictingWithBongo(_behavior.Current) &&
                _behaviorInputConflictStartedAt < 0L)
            {
                _behaviorInputConflictStartedAt = now;
            }
        }

        private void TogglePetVisibility()
        {
            if (IsVisible)
            {
                bool fullscreenWasActive =
                    _lastFullscreenSample ==
                        FullscreenSample.FullscreenOnPetMonitor ||
                    _fullscreenBypassUntilExit;
                _userHidden = true;
                _fullscreenSuppressed = false;
                _fullscreenBypassUntilExit = false;
                _lastFullscreenWindow = IntPtr.Zero;
                _bypassedFullscreenWindow = IntPtr.Zero;
                _fullscreenEnterSamples = 0;
                _fullscreenExitSamples = 0;
                CloseActiveProp();
                Hide();
                if (!_hasShownBackgroundTip &&
                    !fullscreenWasActive)
                {
                    _notifyIcon.BalloonTipTitle = AppName;
                    _notifyIcon.BalloonTipText = "糯米已在后台运行。双击系统托盘图标，可以随时把它叫回来。";
                    _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
                    _notifyIcon.ShowBalloonTip(1800);
                    _hasShownBackgroundTip = true;
                }
            }
            else
            {
                PrepareManualShow();
                ShowPetWindow();
            }
            UpdateTrayDescription();
        }

        public void ShowFromExternalRequest()
        {
            if (_isExiting)
            {
                return;
            }

            EnsurePetIsVisible();
            ShowMessage(
                "我在这里呀~",
                1800,
                _clock.ElapsedMilliseconds);
            InvalidateVisual();
        }

        private void PrepareManualShow()
        {
            bool wasFullscreenHidden = _fullscreenSuppressed;
            _userHidden = false;
            _fullscreenSuppressed = false;
            _fullscreenEnterSamples = 0;
            _fullscreenExitSamples = 0;

            if (_autoHideFullscreen &&
                (wasFullscreenHidden ||
                 _lastFullscreenSample ==
                    FullscreenSample.FullscreenOnPetMonitor))
            {
                _fullscreenBypassUntilExit = true;
                _bypassedFullscreenWindow =
                    _lastFullscreenWindow;
                _fullscreenBypassEarliestResetAt =
                    _clock.ElapsedMilliseconds + 5000L;
            }
        }

        private void ShowPetWindow()
        {
            if (!IsVisible)
            {
                Show();
            }
            EnsureVisibleOnAnyScreen();
        }

        private void ShowGreeting()
        {
            EnsurePetIsVisible();
            string[] messages = new string[]
            {
                "喵~",
                "今天也加油",
                "陪你工作",
                "摸摸我",
                "休息一下吧"
            };
            long now = _clock.ElapsedMilliseconds;
            ShowMessage(messages[_random.Next(messages.Length)], 1800, now);
            _waveStartedAt = now;
            _waveUntil = now + 1500;
            InvalidateVisual();
        }

        private void ShowMessage(string message, int durationMilliseconds, long now)
        {
            _message = message;
            _messageUntil = now + durationMilliseconds;
            _formattedMessage = new FormattedText(
                _message,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Microsoft YaHei UI"),
                    FontStyles.Normal,
                    FontWeights.SemiBold,
                    FontStretches.Normal),
                13.0,
                BubbleTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private void PetTheCat()
        {
            EnsurePetIsVisible();
            long now = _clock.ElapsedMilliseconds;
            _slowBlinkRequested = true;
            _nextBlinkAt = Math.Min(_nextBlinkAt, now + 210);
            StartBehavior(CatBehavior.Purring, true);
        }

        private void TouchTheTail()
        {
            long now = _clock.ElapsedMilliseconds;
            _interactionMotion.RegisterTailTouch(now);
            string[] messages =
            {
                "呀，尾巴！",
                "喵？",
                "尾巴会痒啦"
            };
            ShowMessage(
                messages[_random.Next(messages.Length)],
                1250,
                now);
        }

        private void FeedTheCat()
        {
            EnsurePetIsVisible();
            StartBehavior(CatBehavior.Eating, true);
        }

        private void PlayWithTheCat()
        {
            EnsurePetIsVisible();
            StartBehavior(CatBehavior.Playing, true);
        }

        private void PlaceCupForCat()
        {
            EnsurePetIsVisible();
            StartBehavior(CatBehavior.CupPush, true);
        }

        private void EnsurePetIsVisible()
        {
            PrepareManualShow();
            ShowPetWindow();
            UpdateTrayDescription();
        }

        private bool StartBehavior(CatBehavior behavior, bool userRequested)
        {
            long now = _clock.ElapsedMilliseconds;
            long duration = GetBehaviorDuration(behavior);
            int priority = userRequested ? 90 : GetBehaviorPriority(behavior);
            bool replacingBehavior = _behavior.IsBusy;
            bool continuingPoseRecovery =
                !replacingBehavior &&
                _poseRecoveryActive;
            CatBehavior replacedBehavior = _behavior.Current;
            PetPose interruptedPose = new PetPose();
            if (replacingBehavior ||
                continuingPoseRecovery)
            {
                interruptedPose = BuildPose(
                    now / 1000.0,
                    now);
            }
            if (!_behavior.Start(behavior, now, duration, priority))
            {
                return false;
            }

            _poseRecoveryActive = false;
            _behaviorInputConflictStartedAt = -1L;
            _behaviorCueShown = false;
            _behaviorWasUserRequested = userRequested;
            _hasAutoWindowPosition = false;
            _hasBehaviorHomePosition = false;

            NativeRect windowRect;
            if (TryGetPetWindowRect(out windowRect))
            {
                _autoWindowX = windowRect.Left;
                _autoWindowY = windowRect.Top;
                _hasAutoWindowPosition = true;
                _behaviorHomeX = windowRect.Left;
                _behaviorHomeY = windowRect.Top;
                _hasBehaviorHomePosition = true;
                _behaviorOnRight = Forms.Cursor.Position.X >=
                    windowRect.Left + (windowRect.Right - windowRect.Left) / 2;
            }

            PrepareBehaviorVisuals(behavior);

            if (replacingBehavior ||
                continuingPoseRecovery)
            {
                BeginPoseRecovery(
                    interruptedPose,
                    now,
                    replacedBehavior);
            }

            if (userRequested ||
                behavior == CatBehavior.Begging)
            {
                switch (behavior)
                {
                    case CatBehavior.Pounce:
                        ShowMessage("要抓到你啦！", 1300, now);
                        break;
                    case CatBehavior.Begging:
                        ShowMessage("主人，我饿啦…", 3000, now);
                        break;
                    case CatBehavior.Eating:
                        ShowMessage("小鱼干！", 1700, now);
                        break;
                    case CatBehavior.Purring:
                        ShowMessage("呼噜呼噜~", 1900, now);
                        break;
                    case CatBehavior.CupPush:
                        ShowMessage("这个杯子…", 1500, now);
                        break;
                    case CatBehavior.Grooming:
                        ShowMessage("洗洗脸~", 1400, now);
                        break;
                    case CatBehavior.Stretching:
                        ShowMessage("伸个懒腰", 1400, now);
                        break;
                    case CatBehavior.Sleeping:
                        ShowMessage("Zzz…", 1800, now);
                        break;
                    case CatBehavior.Zoomies:
                        ShowMessage("冲呀！", 1200, now);
                        break;
                    case CatBehavior.Playing:
                        ShowMessage("来玩毛线球！", 1800, now);
                        break;
                }
            }

            InvalidateVisual();
            return true;
        }

        private static bool IsNoticeableAutonomousBehavior(
            CatBehavior behavior)
        {
            switch (behavior)
            {
                case CatBehavior.Pounce:
                case CatBehavior.CupPush:
                case CatBehavior.Begging:
                case CatBehavior.Grooming:
                case CatBehavior.Stretching:
                case CatBehavior.Sleeping:
                case CatBehavior.Zoomies:
                case CatBehavior.Playing:
                    return true;
                default:
                    return false;
            }
        }

        private static long GetBehaviorDuration(CatBehavior behavior)
        {
            switch (behavior)
            {
                case CatBehavior.Observe:
                    return 3800;
                case CatBehavior.Pounce:
                    return 3600;
                case CatBehavior.CupPush:
                    return 4600;
                case CatBehavior.Begging:
                    return 9000;
                case CatBehavior.Eating:
                    return 5600;
                case CatBehavior.Purring:
                    return 5200;
                case CatBehavior.Grooming:
                    return 5600;
                case CatBehavior.Stretching:
                    return 3600;
                case CatBehavior.Sleeping:
                    return 18000;
                case CatBehavior.Zoomies:
                    return 6500;
                case CatBehavior.Playing:
                    return 6000;
                default:
                    return 2800;
            }
        }

        private int GetBehaviorPriority(CatBehavior behavior)
        {
            switch (behavior)
            {
                case CatBehavior.Eating:
                    return 90;
                case CatBehavior.Begging:
                    return _behavior.Hunger >= 82.0 ? 80 : 60;
                case CatBehavior.Pounce:
                    return 65;
                case CatBehavior.CupPush:
                    return 55;
                case CatBehavior.Playing:
                    return 52;
                case CatBehavior.Zoomies:
                    return 40;
                case CatBehavior.Purring:
                    return 35;
                case CatBehavior.Sleeping:
                    return _behavior.Energy <= 16.0
                        ? 80
                        : 30;
                case CatBehavior.Grooming:
                case CatBehavior.Stretching:
                    return 25;
                default:
                    return 15;
            }
        }

        private void ShowPetStatus()
        {
            EnsurePetIsVisible();
            _behavior.AdvanceNeeds(DateTime.UtcNow, IsVisible);
            double fullness =
                100.0 -
                _behavior.Hunger;
            string status =
                "现在：" +
                GetBehaviorName(
                    _behavior.Current) +
                "\n\n" +
                "肚子：" +
                DescribeFullness(fullness) +
                "（" +
                FormatNeed(fullness) +
                "）\n" +
                "精神：" +
                DescribeEnergy(
                    _behavior.Energy) +
                "（" +
                FormatNeed(
                    _behavior.Energy) +
                "）\n" +
                "心情：" +
                DescribeMood(
                    _behavior.Mood) +
                "（" +
                FormatNeed(
                    _behavior.Mood) +
                "）\n" +
                "关系：" +
                DescribeAffection(
                    _behavior.Affection) +
                "（" +
                FormatNeed(
                    _behavior.Affection) +
                "）\n" +
                "玩心：" +
                DescribeBoredom(
                    _behavior.Boredom) +
                "（" +
                FormatNeed(
                    _behavior.Boredom) +
                "）\n\n" +
                "小建议：" +
                GetPetSuggestion();

            System.Windows.MessageBox.Show(
                this,
                status,
                "糯米的状态",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static string FormatNeed(double value)
        {
            int rounded = (int)Math.Round(Clamp(value, 0.0, 100.0));
            return rounded.ToString(CultureInfo.InvariantCulture) + " / 100";
        }

        private static string DescribeFullness(double value)
        {
            if (value < 22.0)
            {
                return "饿得在找你";
            }
            if (value < 45.0)
            {
                return "有点饿";
            }
            if (value < 78.0)
            {
                return "刚刚好";
            }
            return "吃得很满足";
        }

        private static string DescribeEnergy(double value)
        {
            if (value < 20.0)
            {
                return "困得睁不开眼";
            }
            if (value < 45.0)
            {
                return "想休息一下";
            }
            if (value < 78.0)
            {
                return "精神不错";
            }
            return "精力满满";
        }

        private static string DescribeMood(double value)
        {
            if (value < 30.0)
            {
                return "需要一点陪伴";
            }
            if (value < 65.0)
            {
                return "很平静";
            }
            return "心情很好";
        }

        private static string DescribeAffection(double value)
        {
            if (value < 35.0)
            {
                return "正在慢慢熟悉你";
            }
            if (value < 70.0)
            {
                return "已经很信任你";
            }
            return "最喜欢你啦";
        }

        private static string DescribeBoredom(double value)
        {
            if (value < 30.0)
            {
                return "安静陪着你";
            }
            if (value < 65.0)
            {
                return "想活动一下";
            }
            return "很想和你玩";
        }

        private string GetPetSuggestion()
        {
            if (_behavior.Hunger >= 78.0)
            {
                return "给我一条小鱼干吧。";
            }
            if (_behavior.Energy <= 24.0)
            {
                return "让我安静睡一会儿吧。";
            }
            if (_behavior.Boredom >= 68.0)
            {
                return "逗我玩毛线球会很开心。";
            }
            if (_behavior.Mood <= 38.0)
            {
                return "摸摸头，我会开心一点。";
            }
            return "现在状态很好，陪着你就很开心。";
        }

        private static string GetBehaviorName(CatBehavior behavior)
        {
            switch (behavior)
            {
                case CatBehavior.Observe:
                    return "观察你";
                case CatBehavior.Pounce:
                    return "准备突袭鼠标";
                case CatBehavior.CupPush:
                    return "研究杯子";
                case CatBehavior.Begging:
                    return "撒娇讨食";
                case CatBehavior.Eating:
                    return "吃小鱼干";
                case CatBehavior.Purring:
                    return "享受摸摸";
                case CatBehavior.Grooming:
                    return "洗脸梳毛";
                case CatBehavior.Stretching:
                    return "伸懒腰";
                case CatBehavior.Sleeping:
                    return "睡觉";
                case CatBehavior.Zoomies:
                    return "疯跑";
                case CatBehavior.Playing:
                    return "玩毛线球";
                default:
                    return "安静陪着你";
            }
        }

        private void ShowHelp()
        {
            System.Windows.MessageBox.Show(
                this,
                "快速上手\n\n" +
                "• 单击头或身体可以摸摸，碰尾巴会有不同反应\n" +
                "• 双击糯米，它会向你打招呼\n" +
                "• 按住糯米拖动，可以把它搬到喜欢的位置\n" +
                "• 右键糯米，可以互动、调整大小和修改设置\n" +
                "• 隐藏后，双击右下角托盘图标可以把它叫回来\n" +
                "• 找不到时，再次打开 EXE 也会自动叫回糯米\n" +
                "• 想彻底关闭，请选择红色的“退出程序（停止运行）”",
                "糯米使用帮助",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            System.Windows.MessageBox.Show(
                this,
                "糯米桌面宠物 3.3\n\n" +
                "一只会根据键盘鼠标互动，也会自己生活的橘猫桌宠。\n\n" +
                "• 单文件 EXE，免安装、可离线运行\n" +
                "• 支持多显示器、高 DPI、高刷新率与全屏自动隐藏\n" +
                "• 键鼠互动只读取瞬时状态，不记录、保存或上传输入内容\n" +
                "• 位置、设置和宠物状态只保存在当前 Windows 用户本机\n\n" +
                "所有角色素材和程序资源都内置在 EXE 中。",
                "关于糯米",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (_isExiting || !IsVisible)
            {
                StopRendering();
                return;
            }

            RenderingEventArgs rendering = e as RenderingEventArgs;
            if (rendering == null)
            {
                return;
            }

            TimeSpan frameTime = rendering.RenderingTime;
            if (_hasLastRenderingTime && frameTime == _lastRenderingTime)
            {
                return;
            }

            double deltaSeconds = _hasLastRenderingTime
                ? (frameTime - _lastRenderingTime).TotalSeconds
                : 0.0;
            _lastRenderingTime = frameTime;
            _hasLastRenderingTime = true;

            if (deltaSeconds < 0.0 ||
                double.IsNaN(deltaSeconds) ||
                double.IsInfinity(deltaSeconds))
            {
                deltaSeconds = 0.0;
            }

            double motionDelta = Math.Min(deltaSeconds, 0.05);
            long now = _clock.ElapsedMilliseconds;

            UpdateCursorTelemetry(now);
            UpdateBongoInputAnimation(now, motionDelta);
            double engagement = _interactionMotion.Engagement;
            _breathPhase = AdvancePhase(
                _breathPhase,
                motionDelta *
                    (2.03 + engagement * 0.28));
            _tailEngagementEnvelope = Smooth(
                _tailEngagementEnvelope,
                engagement,
                engagement >
                    _tailEngagementEnvelope
                        ? 0.035
                        : 0.018,
                motionDelta);
            _tailSwayPhase = AdvancePhase(
                _tailSwayPhase,
                motionDelta *
                    (0.58 +
                     _tailEngagementEnvelope * 0.34));
            if (!_isDragging || now - _lastDragMotionAt > 65L)
            {
                _dragLeanTarget = 0.0;
                _dragVerticalTarget = 0.0;
            }
            _dragLean = Smooth(
                _dragLean,
                _dragLeanTarget,
                _isDragging ? 0.24 : 0.10,
                motionDelta);
            _dragVertical = Smooth(
                _dragVertical,
                _dragVerticalTarget,
                _isDragging ? 0.22 : 0.09,
                motionDelta);
            _behavior.AdvanceNeeds(DateTime.UtcNow, true);
            UpdateBehavior(now, motionDelta);

            if (_settleBlinkArmed && now >= _settleBlinkDue)
            {
                if (now - _lastBongoInputAt >= 650L &&
                    _behavior.Current != CatBehavior.Sleeping)
                {
                    _settleBlinkArmed = false;
                    _slowBlinkRequested = true;
                    _nextBlinkAt = Math.Min(_nextBlinkAt, now + 80);
                }
                else
                {
                    _settleBlinkDue = now + 420;
                }
            }

            if (_behavior.Current == CatBehavior.Sleeping)
            {
                double sleepClose = _behavior.IsBusy
                    ? SmoothStep(
                        Clamp(_behavior.Progress(now) / 0.11, 0.0, 1.0))
                    : 1.0;
                _blinkAmount = sleepClose;
                _blinkStartedAt = -1;
                _nextBlinkAt = now + 900;
                _remainingDoubleBlinks = 0;
                _nextBlinkIsFollowup = false;
            }
            else if (_blinkStartedAt < 0 && now >= _nextBlinkAt)
            {
                BeginNaturalBlink(now);
            }

            if (_behavior.Current != CatBehavior.Sleeping && _blinkStartedAt >= 0)
            {
                double blinkPhase =
                    (now - _blinkStartedAt) /
                    (double)Math.Max(1, _blinkDuration);
                if (blinkPhase >= 1.0)
                {
                    _blinkAmount = 0.0;
                    _blinkStartedAt = -1;
                    if (_remainingDoubleBlinks > 0)
                    {
                        _remainingDoubleBlinks--;
                        _nextBlinkIsFollowup = true;
                        _nextBlinkAt = now + 95 + _random.Next(75);
                    }
                    else
                    {
                        _nextBlinkIsFollowup = false;
                        ScheduleNextNaturalBlink(now);
                    }
                }
                else
                {
                    // Closing is faster than opening, with a tiny moment of
                    // contact in the middle instead of a robotic sine loop.
                    if (blinkPhase < 0.34)
                    {
                        _blinkAmount = SmoothStep(blinkPhase / 0.34);
                    }
                    else if (blinkPhase < 0.46)
                    {
                        _blinkAmount = 1.0;
                    }
                    else
                    {
                        _blinkAmount =
                            1.0 -
                            SmoothStep((blinkPhase - 0.46) / 0.54);
                    }
                }
            }

            UpdateHeadTracking(now, motionDelta);

            if (now - _lastStateSaveAt >= 300000)
            {
                SavePetState();
                _lastStateSaveAt = now;
            }

            InvalidateVisual();
        }

        private void BeginNaturalBlink(long now)
        {
            _blinkStartedAt = now;

            if (_slowBlinkRequested)
            {
                _slowBlinkRequested = false;
                _blinkDuration = 720 + _random.Next(260);
                _remainingDoubleBlinks = 0;
                _nextBlinkIsFollowup = false;
                return;
            }

            if (_nextBlinkIsFollowup)
            {
                _nextBlinkIsFollowup = false;
                _blinkDuration = 170 + _random.Next(45);
                return;
            }

            _blinkDuration = 185 + _random.Next(55);
            _remainingDoubleBlinks =
                _random.NextDouble() < 0.18
                    ? 1
                    : 0;
        }

        private void ScheduleNextNaturalBlink(long now)
        {
            // A skewed distribution produces clusters and quiet stretches,
            // while active typing suppresses unnecessary blinking a little.
            double randomUnit = Math.Max(0.001, 1.0 - _random.NextDouble());
            int interval =
                1650 +
                (int)Math.Round(-Math.Log(randomUnit) * 1950.0);
            interval = ClampInteger(interval, 1650, 7200);
            interval +=
                (int)Math.Round(_interactionMotion.Engagement * 1050.0);
            _nextBlinkAt = now + interval;
        }

        private void UpdateCursorTelemetry(long now)
        {
            Drawing.Point cursor = Forms.Cursor.Position;
            if (_lastCursorSampleAt > 0 && now > _lastCursorSampleAt)
            {
                double elapsedSeconds = (now - _lastCursorSampleAt) / 1000.0;
                double deltaX = cursor.X - _lastCursorPosition.X;
                double deltaY = cursor.Y - _lastCursorPosition.Y;
                double instantaneous =
                    Math.Sqrt(deltaX * deltaX + deltaY * deltaY) /
                    Math.Max(elapsedSeconds, 0.001);
                double filterAmount =
                    1.0 - Math.Pow(1.0 - 0.28, elapsedSeconds * 60.0);
                _cursorSpeed +=
                    (instantaneous - _cursorSpeed) *
                    Clamp(filterAmount, 0.0, 1.0);
            }

            _lastCursorPosition = cursor;
            _lastCursorSampleAt = now;

            if (IsBongoDeskActive())
            {
                Drawing.Rectangle screenBounds =
                    Forms.SystemInformation.VirtualScreen;
                double ratioX =
                    (cursor.X - screenBounds.Left) /
                    (double)Math.Max(1, screenBounds.Width);
                double ratioY =
                    (cursor.Y - screenBounds.Top) /
                    (double)Math.Max(1, screenBounds.Height);
                _bongoPointerTargetX = Clamp(ratioX, 0.0, 1.0);
                _bongoPointerTargetY = Clamp(ratioY, 0.0, 1.0);
            }
        }

        private void UpdateBongoInputAnimation(long now, double deltaSeconds)
        {
            _bongoPointerX = Smooth(
                _bongoPointerX,
                _bongoPointerTargetX,
                0.38,
                deltaSeconds);
            _bongoPointerY = Smooth(
                _bongoPointerY,
                _bongoPointerTargetY,
                0.38,
                deltaSeconds);

            if (now >= _nextKeyStateReconcileAt)
            {
                int keyCountBefore =
                    _leftKeysDown.Count +
                    _rightKeysDown.Count;
                ReconcileHeldKeys(_leftKeysDown);
                ReconcileHeldKeys(_rightKeysDown);
                int keyCountAfter =
                    _leftKeysDown.Count +
                    _rightKeysDown.Count;
                if (keyCountAfter != keyCountBefore)
                {
                    RemoveUnheldKeyTimestamps();
                    if (keyCountAfter == 0)
                    {
                        _interactionMotion.RegisterKeyUp(now);
                    }
                    else
                    {
                        RetargetToMostRecentHeldKey();
                    }
                }
                if (_mouseLeftDown &&
                    !_localBongoMousePress &&
                    (GetAsyncKeyState(0x01) & 0x8000) == 0)
                {
                    _mouseLeftDown = false;
                    _mouseLeftAutoReleaseAt = 0;
                    _interactionMotion.RegisterMouseUp(true, now);
                }
                if (_mouseRightDown &&
                    !_localBongoRightMousePress &&
                    (GetAsyncKeyState(0x02) & 0x8000) == 0)
                {
                    _mouseRightDown = false;
                    _mouseRightAutoReleaseAt = 0;
                    _interactionMotion.RegisterMouseUp(false, now);
                }
                _nextKeyStateReconcileAt = now + 500;
            }
            if (_mouseLeftAutoReleaseAt > 0 &&
                now >= _mouseLeftAutoReleaseAt)
            {
                if ((GetAsyncKeyState(0x01) & 0x8000) == 0)
                {
                    _mouseLeftDown = false;
                    _mouseLeftAutoReleaseAt = 0;
                    _interactionMotion.RegisterMouseUp(true, now);
                }
                else
                {
                    _mouseLeftAutoReleaseAt = now + 5000;
                }
            }
            if (_mouseRightAutoReleaseAt > 0 &&
                now >= _mouseRightAutoReleaseAt)
            {
                if ((GetAsyncKeyState(0x02) & 0x8000) == 0)
                {
                    _mouseRightDown = false;
                    _mouseRightAutoReleaseAt = 0;
                    _interactionMotion.RegisterMouseUp(false, now);
                }
                else
                {
                    _mouseRightAutoReleaseAt = now + 5000;
                }
            }

            _interactionMotion.Update(
                now,
                deltaSeconds,
                _leftKeysDown.Count > 0 || _rightKeysDown.Count > 0,
                _mouseLeftDown,
                _mouseRightDown);

            double leftKeyTarget =
                _bongoMode &&
                (_leftKeysDown.Count > 0 ||
                 now < _leftInputPulseUntil)
                    ? 1.0
                    : 0.0;
            double rightKeyTarget =
                _bongoMode &&
                (_rightKeysDown.Count > 0 ||
                 now < _rightInputPulseUntil)
                    ? 1.0
                    : 0.0;
            double leftMouseTarget =
                _bongoMode &&
                (_mouseLeftDown ||
                 now < _mouseLeftPulseUntil)
                    ? 1.0
                    : 0.0;
            double rightMouseTarget =
                _bongoMode &&
                (_mouseRightDown ||
                 now < _mouseRightPulseUntil)
                    ? 1.0
                    : 0.0;
            double wheelTarget =
                _bongoMode &&
                now < _wheelPulseUntil
                    ? 1.0
                    : 0.0;

            _leftKeyAmount = Smooth(
                _leftKeyAmount,
                leftKeyTarget,
                leftKeyTarget > _leftKeyAmount ? 0.67 : 0.25,
                deltaSeconds);
            _rightKeyAmount = Smooth(
                _rightKeyAmount,
                rightKeyTarget,
                rightKeyTarget > _rightKeyAmount ? 0.67 : 0.25,
                deltaSeconds);
            _leftMouseAmount = Smooth(
                _leftMouseAmount,
                leftMouseTarget,
                leftMouseTarget > _leftMouseAmount ? 0.72 : 0.28,
                deltaSeconds);
            _rightMouseAmount = Smooth(
                _rightMouseAmount,
                rightMouseTarget,
                rightMouseTarget > _rightMouseAmount ? 0.72 : 0.28,
                deltaSeconds);
            _wheelAmount = Smooth(
                _wheelAmount,
                wheelTarget,
                wheelTarget > _wheelAmount ? 0.66 : 0.25,
                deltaSeconds);

            for (int index = 0; index < BongoKeyCaps.Length; index++)
            {
                BongoKeyCap key = BongoKeyCaps[index];
                bool isActive = false;
                for (int keyIndex = 0;
                    keyIndex < key.VirtualKeys.Length;
                    keyIndex++)
                {
                    int virtualKey = key.VirtualKeys[keyIndex];
                    long pulseUntil;
                    if (_leftKeysDown.Contains(virtualKey) ||
                        _rightKeysDown.Contains(virtualKey) ||
                        (_keyPulseUntilByVirtualKey.TryGetValue(
                            virtualKey,
                            out pulseUntil) &&
                         now < pulseUntil))
                    {
                        isActive = true;
                        break;
                    }
                }

                double target = _bongoMode && isActive ? 1.0 : 0.0;
                _bongoKeyAmounts[index] = Smooth(
                    _bongoKeyAmounts[index],
                    target,
                    target > _bongoKeyAmounts[index] ? 0.70 : 0.25,
                    deltaSeconds);
            }
        }

        private static void ReconcileHeldKeys(HashSet<int> keys)
        {
            if (keys.Count == 0)
            {
                return;
            }

            int[] snapshot = new int[keys.Count];
            keys.CopyTo(snapshot);
            for (int index = 0; index < snapshot.Length; index++)
            {
                int virtualKey = snapshot[index];
                if ((GetAsyncKeyState(virtualKey) & 0x8000) == 0)
                {
                    keys.Remove(virtualKey);
                }
            }
        }

        private void SynchronizeVisibleKeyState()
        {
            long now = _clock.ElapsedMilliseconds;
            for (int capIndex = 0;
                capIndex < BongoKeyCaps.Length;
                capIndex++)
            {
                BongoKeyCap key = BongoKeyCaps[capIndex];
                for (int keyIndex = 0;
                    keyIndex < key.VirtualKeys.Length;
                    keyIndex++)
                {
                    int virtualKey = key.VirtualKeys[keyIndex];
                    if ((GetAsyncKeyState(virtualKey) & 0x8000) == 0)
                    {
                        continue;
                    }

                    HashSet<int> keys = UsesRightPawForVirtualKey(virtualKey)
                        ? _rightKeysDown
                        : _leftKeysDown;
                    if (keys.Add(virtualKey))
                    {
                        _keyPressedAtByVirtualKey[virtualKey] = now;
                    }
                }
            }
            RetargetToMostRecentHeldKey();
        }

        private void RemoveUnheldKeyTimestamps()
        {
            if (_keyPressedAtByVirtualKey.Count == 0)
            {
                return;
            }

            int[] trackedKeys =
                new int[_keyPressedAtByVirtualKey.Count];
            _keyPressedAtByVirtualKey.Keys.CopyTo(trackedKeys, 0);
            for (int index = 0;
                index < trackedKeys.Length;
                index++)
            {
                int virtualKey = trackedKeys[index];
                if (!_leftKeysDown.Contains(virtualKey) &&
                    !_rightKeysDown.Contains(virtualKey))
                {
                    _keyPressedAtByVirtualKey.Remove(virtualKey);
                }
            }
        }

        private static bool UsesRightPawForVirtualKey(int virtualKey)
        {
            return virtualKey >= 0x25 &&
                   virtualKey <= 0x28;
        }

        private static double GetBongoKeyReach(int virtualKey)
        {
            for (int index = 0; index < BongoKeyCaps.Length; index++)
            {
                BongoKeyCap key = BongoKeyCaps[index];
                if (!key.Matches(virtualKey))
                {
                    continue;
                }

                double centerX = key.Bounds.X + key.Bounds.Width * 0.5;
                return Clamp((centerX - 140.0) / 72.0, -1.0, 1.0);
            }

            return 0.0;
        }

        private static double GetBongoKeyRow(int virtualKey)
        {
            for (int index = 0; index < BongoKeyCaps.Length; index++)
            {
                BongoKeyCap key = BongoKeyCaps[index];
                if (!key.Matches(virtualKey))
                {
                    continue;
                }

                double centerY = key.Bounds.Y + key.Bounds.Height * 0.5;
                return Clamp((centerY - 225.0) / 24.0, -1.0, 1.0);
            }

            return 0.0;
        }

        private void RetargetToMostRecentHeldKey()
        {
            int selectedVirtualKey = -1;
            long selectedAt = Int64.MinValue;
            foreach (KeyValuePair<int, long> pair in
                _keyPressedAtByVirtualKey)
            {
                if (!_leftKeysDown.Contains(pair.Key) &&
                    !_rightKeysDown.Contains(pair.Key))
                {
                    continue;
                }
                if (pair.Value >= selectedAt)
                {
                    selectedVirtualKey = pair.Key;
                    selectedAt = pair.Value;
                }
            }

            if (selectedVirtualKey >= 0)
            {
                _interactionMotion.RetargetKeyboard(
                    GetBongoKeyReach(selectedVirtualKey),
                    GetBongoKeyRow(selectedVirtualKey));
            }
        }

        private bool IsBongoInputActive(long now)
        {
            return _bongoMode &&
                (_leftKeysDown.Count > 0 ||
                 _rightKeysDown.Count > 0 ||
                 _mouseLeftDown ||
                 _mouseRightDown ||
                 (_lastBongoInputAt > 0 &&
                  now - _lastBongoInputAt < 720) ||
                 _leftKeyAmount > 0.025 ||
                 _rightKeyAmount > 0.025 ||
                 _leftMouseAmount > 0.025 ||
                 _rightMouseAmount > 0.025 ||
                 _wheelAmount > 0.025);
        }

        private bool IsBongoDeskActive()
        {
            if (!_bongoMode)
            {
                return false;
            }
            if (!_behavior.IsBusy)
            {
                return true;
            }

            switch (_behavior.Current)
            {
                case CatBehavior.Pounce:
                case CatBehavior.CupPush:
                case CatBehavior.Eating:
                case CatBehavior.Begging:
                case CatBehavior.Grooming:
                case CatBehavior.Stretching:
                case CatBehavior.Sleeping:
                case CatBehavior.Zoomies:
                case CatBehavior.Playing:
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsBehaviorConflictingWithBongo(
            CatBehavior behavior)
        {
            switch (behavior)
            {
                case CatBehavior.Pounce:
                case CatBehavior.CupPush:
                case CatBehavior.Begging:
                case CatBehavior.Grooming:
                case CatBehavior.Stretching:
                case CatBehavior.Sleeping:
                case CatBehavior.Zoomies:
                case CatBehavior.Playing:
                    return true;
                default:
                    return false;
            }
        }

        private void ClearBongoInputState(bool resetAnimation)
        {
            _leftKeysDown.Clear();
            _rightKeysDown.Clear();
            _keyPulseUntilByVirtualKey.Clear();
            _keyPressedAtByVirtualKey.Clear();
            _mouseLeftDown = false;
            _mouseRightDown = false;
            _leftInputPulseUntil = 0;
            _rightInputPulseUntil = 0;
            _mouseLeftPulseUntil = 0;
            _mouseRightPulseUntil = 0;
            _wheelPulseUntil = 0;
            _mouseLeftAutoReleaseAt = 0;
            _mouseRightAutoReleaseAt = 0;
            _nextKeyStateReconcileAt = 0;
            _lastBongoInputAt = 0;
            _localBongoMousePress = false;
            _localBongoRightMousePress = false;
            _settleBlinkArmed = false;
            _settleBlinkDue = 0;
            _interactionMotion.Clear();

            if (resetAnimation)
            {
                _leftKeyAmount = 0.0;
                _rightKeyAmount = 0.0;
                _leftMouseAmount = 0.0;
                _rightMouseAmount = 0.0;
                _wheelAmount = 0.0;
                Array.Clear(
                    _bongoKeyAmounts,
                    0,
                    _bongoKeyAmounts.Length);
            }
        }

        private void UpdateBehavior(long now, double deltaSeconds)
        {
            if (_isDragging || _dragPending || !IsVisible)
            {
                return;
            }

            if (_behavior.IsBusy &&
                _bongoMode &&
                _behavior.Priority < 80 &&
                IsBehaviorConflictingWithBongo(_behavior.Current))
            {
                if (IsBongoInputActive(now))
                {
                    if (_behaviorInputConflictStartedAt < 0L)
                    {
                        _behaviorInputConflictStartedAt = now;
                    }
                    else if (
                        now - _behaviorInputConflictStartedAt >= 1300L)
                    {
                        CancelBehavior(now);
                        return;
                    }
                }
                else
                {
                    _behaviorInputConflictStartedAt = -1L;
                }
            }
            else
            {
                _behaviorInputConflictStartedAt = -1L;
            }

            if (_behavior.IsBusy && now >= _behavior.Until)
            {
                CatBehavior completed = _behavior.Current;
                _behavior.Complete(now);
                FinishBehaviorVisuals(completed, now);
                _behaviorInputConflictStartedAt = -1L;
                ScheduleNextAutonomous(
                    now,
                    20000,
                    45000);
            }

            if (!_behavior.IsBusy &&
                _autoInteraction &&
                _behavior.CanAutoStart(now) &&
                (_petMenu == null || !_petMenu.IsOpen) &&
                (_trayMenu == null || !_trayMenu.Visible))
            {
                CatBehavior choice;
                CatBehavior urgentChoice;
                bool hasUrgentNeed =
                    _behavior.TryChooseUrgent(
                        now,
                        out urgentChoice);
                bool inputActive =
                    IsBongoInputActive(now);
                long inputIdleFor =
                    _lastBongoInputAt <= 0L
                        ? Int64.MaxValue
                        : Math.Max(
                            0L,
                            now -
                            _lastBongoInputAt);
                long fullBehaviorIdleRequired =
                    GetFullBehaviorIdleThreshold();
                long needIdleRequired =
                    GetNeedIdleThreshold();
                bool readyForFullBehavior =
                    !_bongoMode ||
                    (!inputActive &&
                     inputIdleFor >=
                        fullBehaviorIdleRequired);
                bool readyForUrgentNeed =
                    !_bongoMode ||
                    (!inputActive &&
                     inputIdleFor >=
                        needIdleRequired);
                if (hasUrgentNeed &&
                    readyForUrgentNeed)
                {
                    choice = urgentChoice;
                }
                else if (
                    !hasUrgentNeed &&
                    readyForFullBehavior)
                {
                    bool cursorNear = IsCursorNearPet(900.0);
                    choice = _behavior.ChooseAutonomous(
                        now,
                        _cursorSpeed,
                        cursorNear);
                }
                else
                {
                    choice = _behavior.ChooseDeskIdle(now);
                }
                if (choice == CatBehavior.Idle)
                {
                    ScheduleNextAutonomous(
                        now,
                        8000,
                        15000);
                }
                else
                {
                    StartBehavior(choice, false);
                }
            }

            if (!_behavior.IsBusy)
            {
                return;
            }

            double progress = _behavior.Progress(now);
            switch (_behavior.Current)
            {
                case CatBehavior.Pounce:
                    if (progress >= 0.76 &&
                        !_behaviorWasUserRequested)
                    {
                        ReturnPetToBehaviorHome(
                            1450.0,
                            deltaSeconds);
                    }
                    else
                    {
                        UpdatePounceBehavior(
                            progress,
                            deltaSeconds,
                            now);
                    }
                    break;
                case CatBehavior.CupPush:
                    UpdateCupBehavior(progress, now);
                    break;
                case CatBehavior.Begging:
                    if (progress < 0.58)
                    {
                        MovePetTowardCursor(470.0, deltaSeconds);
                    }
                    else if (
                        progress >= 0.74 &&
                        !_behaviorWasUserRequested)
                    {
                        ReturnPetToBehaviorHome(
                            980.0,
                            deltaSeconds);
                    }
                    break;
                case CatBehavior.Eating:
                    if (_activeProp != null)
                    {
                        _activeProp.ActionProgress =
                            (progress * 4.0 +
                             GetPropTouchPulse(now) * 0.45) %
                            1.0;
                    }
                    break;
                case CatBehavior.Zoomies:
                    if (progress >= 0.78 &&
                        !_behaviorWasUserRequested)
                    {
                        ReturnPetToBehaviorHome(
                            1800.0,
                            deltaSeconds);
                    }
                    else
                    {
                        UpdateZoomiesBehavior(
                            progress,
                            deltaSeconds);
                    }
                    break;
                case CatBehavior.Playing:
                    UpdateToyBehavior(progress, now);
                    break;
            }
        }

        private void ScheduleNextAutonomous(
            long now,
            int minimumDelay,
            int maximumDelay)
        {
            double intervalScale;
            switch (_interactionMotion.Personality)
            {
                case MotionPersonality.Quiet:
                    intervalScale = 1.25;
                    break;
                case MotionPersonality.Playful:
                    intervalScale = 0.78;
                    break;
                default:
                    intervalScale = 1.0;
                    break;
            }

            _behavior.ScheduleNext(
                now,
                Math.Max(
                    500,
                    (int)Math.Round(
                        minimumDelay *
                        intervalScale)),
                Math.Max(
                    700,
                    (int)Math.Round(
                        maximumDelay *
                        intervalScale)));
        }

        private long GetFullBehaviorIdleThreshold()
        {
            switch (_interactionMotion.Personality)
            {
                case MotionPersonality.Quiet:
                    return 45000L;
                case MotionPersonality.Playful:
                    return 10000L;
                default:
                    return 22000L;
            }
        }

        private long GetNeedIdleThreshold()
        {
            switch (_interactionMotion.Personality)
            {
                case MotionPersonality.Quiet:
                    return 12000L;
                case MotionPersonality.Playful:
                    return 7000L;
                default:
                    return 9000L;
            }
        }

        private void UpdatePounceBehavior(double progress, double deltaSeconds, long now)
        {
            if (progress < 0.57)
            {
                MovePetTowardCursor(1180.0, deltaSeconds);
            }
            else if (!_behaviorCueShown)
            {
                if (_behaviorWasUserRequested)
                {
                    ShowMessage(
                        "啪！抓到你啦",
                        1150,
                        now);
                }
                _behaviorCueShown = true;
            }
        }

        private void UpdateCupBehavior(double progress, long now)
        {
            if (_activeProp == null)
            {
                return;
            }

            double touch = GetPropTouchPulse(now);
            double push = SmoothStep(
                Clamp((progress - 0.50) / 0.30, 0.0, 1.0));
            double visiblePush =
                Clamp(push + touch * 0.15, 0.0, 1.08);
            try
            {
                _activeProp.ActionProgress = visiblePush;
                _activeProp.VisualRotation =
                    _propDirection *
                    (68.0 * push + 10.0 * touch);
                _activeProp.MoveToPixels(
                    ClampInteger(
                            _propOriginX +
                            (int)Math.Round(
                                _propDirection *
                                (44.0 * push + 8.0 * touch) *
                                _propPixelScaleX),
                        _propMinimumX,
                        _propMaximumX),
                    ClampInteger(
                            _propOriginY +
                            (int)Math.Round(
                                (7.0 * push -
                                 3.0 * touch) *
                                _propPixelScaleY),
                        _propMinimumY,
                        _propMaximumY));
            }
            catch
            {
                CancelBehavior(now);
                ShowMessage("杯子滚远啦", 1500, now);
                return;
            }

            if (progress >= 0.54 && !_behaviorCueShown)
            {
                if (_behaviorWasUserRequested)
                {
                    ShowMessage(
                        "啪嗒！",
                        1100,
                        now);
                }
                _behaviorCueShown = true;
            }
        }

        private void UpdateToyBehavior(double progress, long now)
        {
            if (_activeProp == null)
            {
                return;
            }

            double wave = Math.Sin(progress * Math.PI * 5.0);
            double touch = GetPropTouchPulse(now);
            int travel = (int)Math.Round(
                _propDirection *
                (progress * 54.0 +
                 wave * 12.0 +
                 touch * 17.0) *
                _propPixelScaleX);
            int bounce = (int)Math.Round(
                (-Math.Abs(
                    Math.Sin(progress * Math.PI * 7.0)) *
                    9.0 -
                 touch * 7.0) *
                _propPixelScaleY);
            try
            {
                _activeProp.ActionProgress =
                    (progress * 3.0 + touch * 0.38) % 1.0;
                _activeProp.VisualRotation =
                    _propDirection *
                    (progress * 720.0 + touch * 95.0);
                _activeProp.MoveToPixels(
                    ClampInteger(
                        _propOriginX + travel,
                        _propMinimumX,
                        _propMaximumX),
                    ClampInteger(
                        _propOriginY + bounce,
                        _propMinimumY,
                        _propMaximumY));
            }
            catch
            {
                CancelBehavior(now);
                ShowMessage("毛线球跑远啦", 1500, now);
                return;
            }

            if (progress >= 0.58 && !_behaviorCueShown)
            {
                if (_behaviorWasUserRequested)
                {
                    ShowMessage(
                        "抓住毛线球！",
                        1250,
                        now);
                }
                _behaviorCueShown = true;
            }
        }

        private void UpdateZoomiesBehavior(double progress, double deltaSeconds)
        {
            NativeRect rect;
            if (!TryGetPetWindowRect(out rect))
            {
                return;
            }

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            Drawing.Rectangle workArea = Forms.Screen.FromRectangle(
                new Drawing.Rectangle(rect.Left, rect.Top, width, height)).WorkingArea;
            int leg = Math.Min(3, (int)(progress * 4.0));
            int targetLeft = leg % 2 == 0
                ? workArea.Left
                : workArea.Right - width;
            int targetTop = workArea.Bottom - height;
            _behaviorOnRight = targetLeft > rect.Left;
            MovePetWindowToward(
                targetLeft,
                targetTop,
                workArea,
                1450.0 * GetPetPixelScale(rect),
                deltaSeconds);
        }

        private void MovePetTowardCursor(double speedPixelsPerSecond, double deltaSeconds)
        {
            NativeRect rect;
            if (!TryGetPetWindowRect(out rect))
            {
                return;
            }

            Drawing.Point cursor = Forms.Cursor.Position;
            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            int centerX = rect.Left + width / 2;
            _behaviorOnRight = cursor.X >= centerX;

            double targetLeft = _behaviorOnRight
                ? cursor.X - width * 0.82
                : cursor.X - width * 0.18;
            double targetTop = cursor.Y - height * 0.78;
            Drawing.Rectangle workArea = Forms.Screen.FromPoint(cursor).WorkingArea;
            MovePetWindowToward(
                targetLeft,
                targetTop,
                workArea,
                speedPixelsPerSecond *
                    GetPetPixelScale(rect),
                deltaSeconds);
        }

        private void ReturnPetToBehaviorHome(
            double speedPixelsPerSecond,
            double deltaSeconds)
        {
            if (!_hasBehaviorHomePosition)
            {
                return;
            }

            NativeRect rect;
            if (!TryGetPetWindowRect(out rect))
            {
                return;
            }

            int width =
                Math.Max(
                    1,
                    rect.Right - rect.Left);
            int height =
                Math.Max(
                    1,
                    rect.Bottom - rect.Top);
            Drawing.Rectangle workArea =
                FindNearestWorkArea(
                    (int)Math.Round(
                        _behaviorHomeX) +
                        width / 2,
                    (int)Math.Round(
                        _behaviorHomeY) +
                        height / 2);
            MovePetWindowToward(
                _behaviorHomeX,
                _behaviorHomeY,
                workArea,
                speedPixelsPerSecond *
                    GetPetPixelScale(rect),
                deltaSeconds);
        }

        private void RestoreBehaviorHomePosition(
            CatBehavior behavior,
            bool shouldRestore)
        {
            if (!shouldRestore ||
                !_hasBehaviorHomePosition ||
                !IsWindowMovingBehavior(behavior))
            {
                return;
            }

            NativeRect rect;
            IntPtr windowHandle =
                new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero ||
                !TryGetPetWindowRect(out rect))
            {
                return;
            }

            int width =
                Math.Max(
                    1,
                    rect.Right - rect.Left);
            int height =
                Math.Max(
                    1,
                    rect.Bottom - rect.Top);
            Drawing.Rectangle workArea =
                FindNearestWorkArea(
                    (int)Math.Round(
                        _behaviorHomeX) +
                        width / 2,
                    (int)Math.Round(
                        _behaviorHomeY) +
                        height / 2);
            int targetLeft =
                ClampInteger(
                    (int)Math.Round(
                        _behaviorHomeX),
                    workArea.Left,
                    Math.Max(
                        workArea.Left,
                        workArea.Right - width));
            int targetTop =
                ClampInteger(
                    (int)Math.Round(
                        _behaviorHomeY),
                    workArea.Top,
                    Math.Max(
                        workArea.Top,
                        workArea.Bottom - height));
            SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                targetLeft,
                targetTop,
                0,
                0,
                SetWindowPosNoSize |
                SetWindowPosNoZOrder |
                SetWindowPosNoActivate);
        }

        private static bool IsWindowMovingBehavior(
            CatBehavior behavior)
        {
            return
                behavior == CatBehavior.Pounce ||
                behavior == CatBehavior.Begging ||
                behavior == CatBehavior.Zoomies;
        }

        private static double GetPetPixelScale(NativeRect rect)
        {
            int width = Math.Max(1, rect.Right - rect.Left);
            return Math.Max(0.1, width / BaseWidth);
        }

        private void MovePetWindowToward(
            double targetLeft,
            double targetTop,
            Drawing.Rectangle workArea,
            double speedPixelsPerSecond,
            double deltaSeconds)
        {
            NativeRect rect;
            if (!TryGetPetWindowRect(out rect))
            {
                return;
            }

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            targetLeft = Clamp(
                targetLeft,
                workArea.Left,
                Math.Max(workArea.Left, workArea.Right - width));
            targetTop = Clamp(
                targetTop,
                workArea.Top,
                Math.Max(workArea.Top, workArea.Bottom - height));

            if (!_hasAutoWindowPosition)
            {
                _autoWindowX = rect.Left;
                _autoWindowY = rect.Top;
                _hasAutoWindowPosition = true;
            }

            double deltaX = targetLeft - _autoWindowX;
            double deltaY = targetTop - _autoWindowY;
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            double maximumStep = Math.Max(0.0, speedPixelsPerSecond * deltaSeconds);
            if (distance <= maximumStep || distance < 0.5)
            {
                _autoWindowX = targetLeft;
                _autoWindowY = targetTop;
            }
            else if (maximumStep > 0.0)
            {
                _autoWindowX += deltaX / distance * maximumStep;
                _autoWindowY += deltaY / distance * maximumStep;
            }

            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle != IntPtr.Zero)
            {
                int renderLeft = (int)Math.Round(_autoWindowX);
                int renderTop = (int)Math.Round(_autoWindowY);
                Drawing.Rectangle safeWorkArea = FindNearestWorkArea(
                    renderLeft + width / 2,
                    renderTop + height / 2);
                renderLeft = ClampInteger(
                    renderLeft,
                    safeWorkArea.Left,
                    Math.Max(safeWorkArea.Left, safeWorkArea.Right - width));
                renderTop = ClampInteger(
                    renderTop,
                    safeWorkArea.Top,
                    Math.Max(safeWorkArea.Top, safeWorkArea.Bottom - height));

                if (renderLeft != rect.Left || renderTop != rect.Top)
                {
                    SetWindowPos(
                        windowHandle,
                        IntPtr.Zero,
                        renderLeft,
                        renderTop,
                        0,
                        0,
                        SetWindowPosNoSize |
                        SetWindowPosNoZOrder |
                        SetWindowPosNoActivate);
                }
            }
        }

        private static Drawing.Rectangle FindNearestWorkArea(int x, int y)
        {
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            Drawing.Rectangle best = Forms.Screen.PrimaryScreen.WorkingArea;
            long bestDistance = long.MaxValue;
            for (int index = 0; index < screens.Length; index++)
            {
                Drawing.Rectangle area = screens[index].WorkingArea;
                long deltaX = 0L;
                long deltaY = 0L;
                if (x < area.Left)
                {
                    deltaX = area.Left - x;
                }
                else if (x >= area.Right)
                {
                    deltaX = x - area.Right + 1L;
                }

                if (y < area.Top)
                {
                    deltaY = area.Top - y;
                }
                else if (y >= area.Bottom)
                {
                    deltaY = y - area.Bottom + 1L;
                }

                long distance = deltaX * deltaX + deltaY * deltaY;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = area;
                }
            }

            return best;
        }

        private bool IsCursorNearPet(double maximumDistance)
        {
            NativeRect rect;
            if (!TryGetPetWindowRect(out rect))
            {
                return false;
            }

            Drawing.Point cursor = Forms.Cursor.Position;
            double deltaX = cursor.X - (rect.Left + rect.Right) * 0.5;
            double deltaY = cursor.Y - (rect.Top + rect.Bottom) * 0.5;
            return deltaX * deltaX + deltaY * deltaY <=
                   maximumDistance * maximumDistance;
        }

        private bool TryGetPetWindowRect(out NativeRect rect)
        {
            rect = new NativeRect();
            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero ||
                !GetWindowRect(windowHandle, out rect) ||
                rect.Right <= rect.Left ||
                rect.Bottom <= rect.Top)
            {
                return false;
            }

            _lastPetPixelBounds = Drawing.Rectangle.FromLTRB(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom);
            _hasLastPetPixelBounds = true;
            return true;
        }

        private static double SmoothStep(double value)
        {
            value = Clamp(value, 0.0, 1.0);
            return value * value * (3.0 - 2.0 * value);
        }

        private void PrepareBehaviorVisuals(CatBehavior behavior)
        {
            CloseActiveProp();
            switch (behavior)
            {
                case CatBehavior.CupPush:
                    ShowPropNearCat(PropKind.Cup);
                    break;
                case CatBehavior.Eating:
                    ShowPropNearCat(PropKind.FoodBowl);
                    break;
                case CatBehavior.Playing:
                    ShowPropNearCat(PropKind.ToyBall);
                    break;
            }
        }

        private void FinishBehaviorVisuals(CatBehavior completed, long now)
        {
            CloseActiveProp();
            _hasAutoWindowPosition = false;
            _behaviorCueShown = false;
            RestoreBehaviorHomePosition(
                completed,
                !_behaviorWasUserRequested);
            bool shouldSpeak =
                _behaviorWasUserRequested;

            switch (completed)
            {
                case CatBehavior.Eating:
                    if (shouldSpeak)
                    {
                        ShowMessage("吃饱啦，谢谢主人", 2100, now);
                    }
                    break;
                case CatBehavior.Purring:
                    if (shouldSpeak)
                    {
                        ShowMessage("最喜欢你啦~", 1800, now);
                    }
                    break;
                case CatBehavior.Playing:
                    if (shouldSpeak)
                    {
                        ShowMessage("再玩一次吧！", 1600, now);
                    }
                    break;
                case CatBehavior.Sleeping:
                    _blinkAmount = 0.0;
                    _nextBlinkAt = now + 1200;
                    if (shouldSpeak)
                    {
                        ShowMessage("睡醒啦~", 1500, now);
                    }
                    break;
            }
            _behaviorWasUserRequested = false;
            _hasBehaviorHomePosition = false;
        }

        private void CancelBehavior(long now)
        {
            bool wasBusy = _behavior.IsBusy;
            CatBehavior interruptedBehavior = _behavior.Current;
            PetPose interruptedPose = new PetPose();
            if (wasBusy)
            {
                interruptedPose = BuildPose(now / 1000.0, now);
            }

            _behavior.Cancel(now);
            if (wasBusy)
            {
                BeginPoseRecovery(
                    interruptedPose,
                    now,
                    interruptedBehavior);
            }
            FadeAndCloseActiveProp(220);
            _hasAutoWindowPosition = false;
            _hasBehaviorHomePosition = false;
            _behaviorCueShown = false;
            _behaviorWasUserRequested = false;
            _behaviorInputConflictStartedAt = -1L;
            _blinkAmount = 0.0;
            _nextBlinkAt = now + 900;
            if (_autoInteraction)
            {
                ScheduleNextAutonomous(
                    now,
                    10000,
                    18000);
            }
        }

        private void BeginPoseRecovery(
            PetPose interruptedPose,
            long now,
            CatBehavior interruptedBehavior)
        {
            _poseRecoveryActive = false;
            PetPose targetPose = BuildPose(
                now / 1000.0,
                now);
            _poseRecoveryDelta = CreateRecoveryDelta(
                interruptedPose,
                targetPose);
            _poseRecoveryStartedAt = now;
            _poseRecoveryUntil =
                now +
                (interruptedBehavior == CatBehavior.Sleeping
                    ? 320L
                    : 230L);
            _tailRecoveryUntil =
                now +
                (interruptedBehavior == CatBehavior.Sleeping
                    ? 900L
                    : 680L);
            _poseRecoveryActive = true;
        }

        private void ShowPropNearCat(PropKind kind)
        {
            NativeRect catRect;
            if (!TryGetPetWindowRect(out catRect))
            {
                return;
            }

            PropWindow prop = new PropWindow(kind, _userScale);
            prop.Topmost = Topmost;
            prop.WindowStartupLocation = WindowStartupLocation.Manual;
            prop.Left = -10000;
            prop.Top = -10000;
            prop.Activated += ActivePropActivated;
            _activeProp = prop;
            _lastPropTouchAt = -10000L;
            _propTouchStrength = 0.0;
            try
            {
                prop.Show();

                Drawing.Rectangle propBounds = prop.GetPixelBounds();
                Drawing.Rectangle catBounds = Drawing.Rectangle.FromLTRB(
                    catRect.Left,
                    catRect.Top,
                    catRect.Right,
                    catRect.Bottom);
                Drawing.Rectangle workArea = Forms.Screen.FromRectangle(catBounds).WorkingArea;

                // Move the new HWND onto the cat's monitor before measuring
                // it. Per-monitor DPI can resize a WPF window during this
                // move, so all placement math below must use the second,
                // monitor-correct measurement.
                int probeWidth = Math.Max(1, propBounds.Width);
                int probeHeight = Math.Max(1, propBounds.Height);
                int probeX = ClampInteger(
                    catRect.Left,
                    workArea.Left,
                    Math.Max(
                        workArea.Left,
                        workArea.Right - probeWidth));
                int probeY = ClampInteger(
                    catRect.Top,
                    workArea.Top,
                    Math.Max(
                        workArea.Top,
                        workArea.Bottom - probeHeight));
                prop.MoveToPixels(probeX, probeY);
                propBounds = prop.GetPixelBounds();

                int propWidth = Math.Max(1, propBounds.Width);
                int propHeight = Math.Max(1, propBounds.Height);
                _propPixelScaleX =
                    Math.Max(0.1, propWidth / 128.0);
                _propPixelScaleY =
                    Math.Max(0.1, propHeight / 128.0);
                int edgeInsetX = (int)Math.Round(
                    18.0 * _propPixelScaleX);
                int clearanceX = (int)Math.Round(
                    48.0 * _propPixelScaleX);
                int rightX = catRect.Right - edgeInsetX;
                int leftX =
                    catRect.Left -
                    propWidth +
                    edgeInsetX;
                _propMinimumX = workArea.Left;
                _propMaximumX = Math.Max(workArea.Left, workArea.Right - propWidth);
                _propMinimumY = workArea.Top;
                _propMaximumY = Math.Max(workArea.Top, workArea.Bottom - propHeight);
                bool fitsRight =
                    rightX + propWidth + clearanceX <=
                    workArea.Right;
                bool fitsLeft =
                    leftX - clearanceX >= workArea.Left;

                if (fitsRight && fitsLeft)
                {
                    _propDirection = _behaviorOnRight ? 1 : -1;
                }
                else if (fitsRight)
                {
                    _propDirection = 1;
                }
                else
                {
                    _propDirection = -1;
                }

                _behaviorOnRight = _propDirection > 0;
                int targetX = _propDirection > 0 ? rightX : leftX;
                int targetY =
                    catRect.Bottom -
                    propHeight +
                    (int)Math.Round(
                        6.0 * _propPixelScaleY);
                targetX = ClampInteger(
                    targetX,
                    _propMinimumX,
                    _propMaximumX);
                targetY = ClampInteger(
                    targetY,
                    _propMinimumY,
                    _propMaximumY);

                prop.MoveToPixels(targetX, targetY);
                propBounds = prop.GetPixelBounds();
                _propOriginX = propBounds.Left;
                _propOriginY = propBounds.Top;
            }
            catch
            {
                CancelBehavior(_clock.ElapsedMilliseconds);
                ShowMessage("道具没放稳，再试一次吧", 1900, _clock.ElapsedMilliseconds);
            }
        }

        private void ActivePropActivated(PropWindow prop)
        {
            if (prop == null || prop != _activeProp)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            if (now - _lastPropTouchAt < 150L)
            {
                return;
            }

            double remainingPulse = GetPropTouchPulse(now);
            _propTouchStrength = Clamp(
                remainingPulse * 0.55 + 0.72,
                0.0,
                1.0);
            _lastPropTouchAt = now;
            _interactionMotion.RegisterDeskTap(now);

            switch (prop.Kind)
            {
                case PropKind.Cup:
                    ShowMessage("轻一点，要掉啦", 900, now);
                    break;
                case PropKind.FoodBowl:
                    ShowMessage("再吃一口~", 900, now);
                    break;
                case PropKind.ToyBall:
                    ShowMessage("啪！", 700, now);
                    break;
            }
        }

        private double GetPropTouchPulse(long now)
        {
            long age = now - _lastPropTouchAt;
            if (_propTouchStrength <= 0.0 ||
                age < 0L ||
                age >= 520L)
            {
                return 0.0;
            }

            double rise =
                Math.Sin(
                    Clamp(age / 90.0, 0.0, 1.0) *
                    Math.PI *
                    0.5);
            double fade =
                1.0 -
                SmoothStep(age / 520.0);
            return _propTouchStrength * rise * fade;
        }

        private void CloseActiveProp()
        {
            PropWindow prop = _activeProp;
            _activeProp = null;
            _propTouchStrength = 0.0;
            _lastPropTouchAt = -10000L;
            _propPixelScaleX = 1.0;
            _propPixelScaleY = 1.0;
            if (prop == null)
            {
                return;
            }

            prop.Activated -= ActivePropActivated;
            try
            {
                prop.Close();
            }
            catch
            {
                // A prop may already be closing during system shutdown.
            }
        }

        private void FadeAndCloseActiveProp(int durationMilliseconds)
        {
            PropWindow prop = _activeProp;
            _activeProp = null;
            _propTouchStrength = 0.0;
            _lastPropTouchAt = -10000L;
            _propPixelScaleX = 1.0;
            _propPixelScaleY = 1.0;
            if (prop == null)
            {
                return;
            }

            prop.Activated -= ActivePropActivated;
            try
            {
                prop.IsHitTestVisible = false;
                System.Windows.Media.Animation.DoubleAnimation fade =
                    new System.Windows.Media.Animation.DoubleAnimation(
                        prop.Opacity,
                        0.0,
                        TimeSpan.FromMilliseconds(
                            Math.Max(1, durationMilliseconds)));
                fade.Completed += delegate
                {
                    try
                    {
                        prop.Close();
                    }
                    catch
                    {
                    }
                };
                prop.BeginAnimation(Window.OpacityProperty, fade);
            }
            catch
            {
                try
                {
                    prop.Close();
                }
                catch
                {
                }
            }
        }

        private void UpdateHeadTracking(long now, double deltaSeconds)
        {
            double targetAngle;
            double targetShiftX;
            double targetShiftY;
            double targetPupilX;
            double targetPupilY;

            if (_followMouse && IsVisible)
            {
                Drawing.Point cursor = Forms.Cursor.Position;
                Point relative;
                try
                {
                    relative = PointFromScreen(new Point(cursor.X, cursor.Y));
                }
                catch
                {
                    relative = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
                }

                double logicalX = relative.X * BaseWidth / Math.Max(ActualWidth, 1.0);
                double logicalY = relative.Y * BaseHeight / Math.Max(ActualHeight, 1.0);
                double dx = logicalX - 110.0;
                double dy = logicalY - 88.0;
                if (Math.Abs(dx) < 5.0)
                {
                    dx = 0.0;
                }
                if (Math.Abs(dy) < 5.0)
                {
                    dy = 0.0;
                }
                double horizontal = Clamp(dx / 230.0, -1.0, 1.0);
                double vertical = Clamp(dy / 180.0, -1.0, 1.0);

                targetAngle = horizontal * 11.0;
                targetShiftX = horizontal * 5.0;
                targetShiftY = vertical * 3.5;
                targetPupilX = Clamp(dx / 45.0, -4.5, 4.5);
                targetPupilY = Clamp(dy / 50.0, -3.5, 3.5);

                if (Math.Abs(targetAngle - _lastTrackedTargetAngle) > 5.5 &&
                    now - _lastLargeGazeShiftAt > 900L)
                {
                    _lastLargeGazeShiftAt = now;
                    if (_blinkStartedAt < 0 &&
                        _random.NextDouble() < 0.48)
                    {
                        _nextBlinkAt = Math.Min(
                            _nextBlinkAt,
                            now + 35 + _random.Next(55));
                    }
                }
                _lastTrackedTargetAngle = targetAngle;
            }
            else
            {
                double seconds = now / 1000.0;
                targetAngle = Math.Sin(seconds * 0.55) * 2.5;
                targetShiftX = Math.Sin(seconds * 0.55) * 1.1;
                targetShiftY = Math.Sin(seconds * 0.8) * 0.7;
                targetPupilX = 0.0;
                targetPupilY = 0.0;
            }

            double engagement = _interactionMotion.Engagement;
            targetAngle +=
                _interactionMotion.GetIdleHeadMicroTilt(now / 1000.0) *
                (1.0 - engagement * 0.62);
            targetAngle += _interactionMotion.HeadReaction * 12.0;
            targetShiftY += Math.Abs(_interactionMotion.HeadReaction) * 4.0;
            if (_bongoMode && engagement > 0.01)
            {
                targetAngle +=
                    _interactionMotion.KeyboardReach *
                    _interactionMotion.TypingEnergy *
                    1.9;
                targetShiftY += engagement * 2.2;
            }
            targetAngle -= _dragLean * 3.8;
            targetShiftX -= _dragLean * 3.2;
            targetShiftY -= _dragVertical * 2.2;

            switch (_behavior.Current)
            {
                case CatBehavior.Observe:
                    targetAngle *= 1.12;
                    targetShiftY -= 1.6;
                    break;
                case CatBehavior.CupPush:
                    targetAngle = _behaviorOnRight ? 8.5 : -8.5;
                    targetShiftX = _behaviorOnRight ? 3.5 : -3.5;
                    targetShiftY = 5.0;
                    break;
                case CatBehavior.Eating:
                    targetAngle = _behaviorOnRight ? 9.0 : -9.0;
                    targetShiftX = _behaviorOnRight ? 4.5 : -4.5;
                    targetShiftY = 14.0;
                    break;
                case CatBehavior.Purring:
                    targetAngle *= 0.55;
                    targetShiftY += 3.0;
                    break;
                case CatBehavior.Grooming:
                    targetAngle = 10.0 + Math.Sin(now / 130.0) * 3.0;
                    targetShiftX = 2.0;
                    targetShiftY = 7.0;
                    break;
                case CatBehavior.Stretching:
                    targetAngle *= 0.35;
                    targetShiftY = 8.0;
                    break;
                case CatBehavior.Sleeping:
                    targetAngle = -4.0;
                    targetShiftX = -2.0;
                    targetShiftY = 13.0;
                    targetPupilX = 0.0;
                    targetPupilY = 0.0;
                    break;
                case CatBehavior.Begging:
                    targetShiftY -= 2.5;
                    break;
            }

            _headAngle = Smooth(_headAngle, targetAngle, 0.105, deltaSeconds);
            _headShiftX = Smooth(_headShiftX, targetShiftX, 0.115, deltaSeconds);
            _headShiftY = Smooth(_headShiftY, targetShiftY, 0.115, deltaSeconds);
            _pupilOffsetX = Smooth(_pupilOffsetX, targetPupilX, 0.22, deltaSeconds);
            _pupilOffsetY = Smooth(_pupilOffsetY, targetPupilY, 0.22, deltaSeconds);
        }

        private struct PetPose
        {
            public double BodyOffsetY;
            public double BodyScaleX;
            public double BodyScaleY;
            public double TailAngle;
            public double TailFlex;
            public double LeftPawAngle;
            public double RightPawAngle;
            public double LeftPawOffsetX;
            public double LeftPawOffsetY;
            public double RightPawOffsetX;
            public double RightPawOffsetY;
            public double CharacterOffsetY;
            public double CharacterScaleX;
            public double CharacterScaleY;
            public double CharacterAngle;
        }

        private PetPose BuildPose(double seconds, long now)
        {
            PetPose pose = new PetPose();
            double engagement = _interactionMotion.Engagement;
            double breathPhase =
                _breathPhase +
                Math.Sin(seconds * 0.23) * 0.17;
            double breath =
                Math.Sin(breathPhase) * 0.88 +
                Math.Sin(breathPhase * 2.0 + 0.65) * 0.12;
            double weightShift =
                _interactionMotion.GetIdleWeightShift(seconds);
            double tailActivity = Lerp(
                _interactionMotion.GetIdleTailActivity(seconds),
                1.0,
                _tailEngagementEnvelope);
            pose.BodyOffsetY = breath * 0.58;
            pose.BodyScaleX =
                1.0 - breath * 0.0032 +
                Math.Abs(weightShift) * 0.0009;
            pose.BodyScaleY = 1.0 + breath * 0.0062;
            pose.CharacterScaleX = 1.0;
            pose.CharacterScaleY = 1.0;
            pose.TailAngle =
                Math.Sin(_tailSwayPhase) *
                    (2.2 +
                     _tailEngagementEnvelope * 3.0) *
                    tailActivity +
                Math.Sin(seconds * 0.23 + 1.4) *
                    0.65 *
                    tailActivity +
                _interactionMotion.TailKick * 22.0;
            pose.TailFlex =
                pose.TailAngle * 0.23 +
                Math.Sin(seconds * 0.42 + 0.7) *
                    0.45 *
                    tailActivity;
            pose.LeftPawAngle = 1.5 + weightShift * 0.55;
            pose.RightPawAngle = -1.5 - weightShift * 0.55;
            pose.CharacterAngle = weightShift * 0.22;

            if (_behavior.IsBusy)
            {
                PetPose baseBehaviorPose = pose;
                double progress = _behavior.Progress(now);
                double action;
                double cycle;
                switch (_behavior.Current)
                {
                    case CatBehavior.Observe:
                        action = Math.Sin(progress * Math.PI);
                        pose.BodyOffsetY -= action * 1.25;
                        pose.CharacterScaleX = 1.0 - action * 0.012;
                        pose.CharacterScaleY = 1.0 + action * 0.014;
                        pose.CharacterAngle +=
                            (_behaviorOnRight ? 1.0 : -1.0) *
                            action;
                        pose.TailAngle +=
                            (_behaviorOnRight ? -1.0 : 1.0) *
                            action * 4.5;
                        break;

                    case CatBehavior.Pounce:
                        if (progress < 0.57)
                        {
                            cycle = Math.Sin(seconds * 25.0);
                            pose.BodyOffsetY -= Math.Abs(cycle) * 3.8;
                            pose.LeftPawAngle = 24.0 * cycle;
                            pose.RightPawAngle = -24.0 * cycle;
                            pose.CharacterAngle = _behaviorOnRight ? 4.5 : -4.5;
                            pose.TailAngle = _behaviorOnRight ? -13.0 : 13.0;
                            pose.TailFlex = 0.0;
                        }
                        else
                        {
                            action = Math.Sin(
                                Clamp((progress - 0.57) / 0.27, 0.0, 1.0) *
                                Math.PI);
                            pose.CharacterOffsetY = -10.0 * action;
                            pose.CharacterAngle = _behaviorOnRight ? 5.0 : -5.0;
                            if (_behaviorOnRight)
                            {
                                pose.RightPawAngle = -104.0 * action;
                            }
                            else
                            {
                                pose.LeftPawAngle = 104.0 * action;
                            }
                        }
                        break;

                    case CatBehavior.CupPush:
                        action = Math.Sin(
                            Clamp((progress - 0.28) / 0.45, 0.0, 1.0) *
                            Math.PI);
                        if (_behaviorOnRight)
                        {
                            pose.RightPawAngle = -70.0 * action;
                        }
                        else
                        {
                            pose.LeftPawAngle = 70.0 * action;
                        }
                        pose.CharacterAngle = _behaviorOnRight ? 2.0 : -2.0;
                        pose.TailAngle = _behaviorOnRight ? -8.0 : 8.0;
                        pose.TailFlex =
                            pose.TailAngle * 0.18 +
                            Math.Sin(seconds * 1.15) * 0.55;
                        break;

                    case CatBehavior.Begging:
                        cycle = Math.Sin(seconds * 6.4);
                        pose.BodyOffsetY -= Math.Abs(cycle) * 1.8;
                        pose.LeftPawAngle = 9.0 + Math.Max(0.0, cycle) * 18.0;
                        pose.RightPawAngle = -9.0 + Math.Min(0.0, cycle) * 18.0;
                        pose.TailAngle =
                            Math.Sin(seconds * 1.25) * 8.0;
                        pose.TailFlex =
                            pose.TailAngle * 0.24 +
                            Math.Sin(seconds * 0.62) * 0.65;
                        break;

                    case CatBehavior.Eating:
                        pose.CharacterScaleX = 1.025;
                        pose.CharacterScaleY = 0.97;
                        pose.CharacterAngle = _behaviorOnRight ? 2.5 : -2.5;
                        pose.TailAngle =
                            Math.Sin(seconds * 1.15) * 3.5;
                        pose.TailFlex =
                            pose.TailAngle * 0.24 +
                            Math.Sin(seconds * 0.55) * 0.35;
                        break;

                    case CatBehavior.Purring:
                        pose.BodyScaleX = 1.0 - breath * 0.008;
                        pose.BodyScaleY = 1.0 + breath * 0.015;
                        pose.CharacterOffsetY = Math.Sin(seconds * 2.15) * 0.8;
                        pose.TailAngle =
                            Math.Sin(seconds * 0.95) * 7.0;
                        pose.TailFlex =
                            pose.TailAngle * 0.25 +
                            Math.Sin(seconds * 0.46) * 0.45;
                        break;

                    case CatBehavior.Grooming:
                        action = 0.5 + 0.5 * Math.Sin(seconds * 5.8);
                        pose.LeftPawAngle = -145.0 - action * 23.0;
                        pose.CharacterAngle = 2.0;
                        pose.TailAngle =
                            Math.Sin(seconds * 0.86) * 3.0;
                        pose.TailFlex =
                            pose.TailAngle * 0.22 +
                            Math.Sin(seconds * 0.40) * 0.28;
                        break;

                    case CatBehavior.Stretching:
                        action = Math.Sin(progress * Math.PI);
                        pose.CharacterScaleX = 1.0 + action * 0.10;
                        pose.CharacterScaleY = 1.0 - action * 0.11;
                        pose.CharacterOffsetY = action * 2.5;
                        pose.LeftPawAngle = 18.0 * action;
                        pose.RightPawAngle = -18.0 * action;
                        pose.TailAngle = -10.0 * action;
                        break;

                    case CatBehavior.Sleeping:
                        pose.CharacterScaleX = 1.10;
                        pose.CharacterScaleY = 0.86;
                        pose.CharacterOffsetY = 3.0;
                        pose.CharacterAngle = -3.5;
                        pose.LeftPawAngle = 13.0;
                        pose.RightPawAngle = -13.0;
                        pose.TailAngle = -5.0 + Math.Sin(seconds * 0.7) * 1.5;
                        pose.TailFlex = Math.Sin(seconds * 0.8) * 0.7;
                        break;

                    case CatBehavior.Zoomies:
                        cycle = Math.Sin(seconds * 31.0);
                        pose.BodyOffsetY -= Math.Abs(cycle) * 4.2;
                        pose.LeftPawAngle = 30.0 * cycle;
                        pose.RightPawAngle = -30.0 * cycle;
                        pose.CharacterAngle = _behaviorOnRight ? 6.0 : -6.0;
                        pose.TailAngle = _behaviorOnRight ? -16.0 : 16.0;
                        pose.TailFlex = _behaviorOnRight ? -2.0 : 2.0;
                        break;

                    case CatBehavior.Playing:
                        action = 0.5 + 0.5 * Math.Sin(seconds * 8.0);
                        if (_behaviorOnRight)
                        {
                            pose.RightPawAngle = -18.0 - action * 58.0;
                        }
                        else
                        {
                            pose.LeftPawAngle = 18.0 + action * 58.0;
                        }
                        pose.BodyOffsetY -= Math.Abs(Math.Sin(seconds * 8.0)) * 1.5;
                        pose.TailAngle =
                            Math.Sin(seconds * 1.85) * 10.0;
                        pose.TailFlex =
                            pose.TailAngle * 0.24 +
                            Math.Sin(seconds * 1.05) * 0.70;
                        break;
                }

                double enterPart = _behavior.Current == CatBehavior.Sleeping
                    ? 0.10
                    : 0.075;
                double exitPart = _behavior.Current == CatBehavior.Sleeping
                    ? 0.15
                    : 0.11;
                double behaviorWeight = Math.Min(
                    SmoothStep(Clamp(progress / enterPart, 0.0, 1.0)),
                    SmoothStep(
                        Clamp((1.0 - progress) / exitPart, 0.0, 1.0)));
                pose = BlendPetPose(
                    baseBehaviorPose,
                    pose,
                    behaviorWeight);
            }

            double propTouch = GetPropTouchPulse(now);
            if (_activeProp != null && propTouch > 0.0)
            {
                pose.BodyOffsetY += propTouch * 1.4;
                pose.CharacterAngle +=
                    (_behaviorOnRight ? 1.0 : -1.0) *
                    propTouch *
                    1.25;
                if (_behaviorOnRight)
                {
                    pose.RightPawAngle -= propTouch * 8.0;
                }
                else
                {
                    pose.LeftPawAngle += propTouch * 8.0;
                }
            }

            bool bongoInputActive = IsBongoInputActive(now);
            if (IsBongoDeskActive() &&
                (!_behavior.IsBusy ||
                 _behavior.Priority < 80))
            {
                double pointerX = Clamp(_bongoPointerX, 0.0, 1.0);
                double pointerY = Clamp(_bongoPointerY, 0.0, 1.0);
                double keyboardAmount = Clamp(
                    _interactionMotion.KeyboardContact,
                    0.0,
                    1.08);
                double keyboardRecoil =
                    _interactionMotion.KeyboardRecoil;
                double keyboardReach =
                    _interactionMotion.KeyboardReach;
                double keyboardRow =
                    _interactionMotion.KeyboardRow;
                double physicalLeftAmount = Clamp(
                    _interactionMotion.LeftMouseContact,
                    0.0,
                    1.08);
                double physicalRightAmount = Clamp(
                    _interactionMotion.RightMouseContact,
                    0.0,
                    1.08);
                double mouseRecoil =
                    _interactionMotion.LeftMouseRecoil -
                    _interactionMotion.RightMouseRecoil;

                // The familiar Bongo pose: one paw rides the mouse while
                // the other taps the keyboard.  Pointer motion only adds a
                // small grip adjustment so the mouse remains recognizable.
                // The cat faces the owner across the desk, so its mouse-side
                // perspective is mirrored: the owner's physical left click
                // lands on the screen-right half, and vice versa.
                pose.LeftPawAngle =
                    49.0 -
                    4.0 * (pointerX - 0.5) +
                    physicalRightAmount * 3.0 -
                    physicalLeftAmount * 4.0 +
                    _wheelAmount * 2.0 +
                    _interactionMotion.WheelMotion * 2.4 +
                    mouseRecoil * 1.2;
                pose.LeftPawOffsetX =
                    -4.0 +
                    2.0 * (pointerX - 0.5) -
                    physicalRightAmount * 12.0 +
                    physicalLeftAmount * 10.0 +
                    _interactionMotion.WheelMotion * 1.6;
                pose.LeftPawOffsetY =
                    1.5 * (pointerY - 0.5) +
                    physicalLeftAmount * 3.0 +
                    physicalRightAmount * 4.5 -
                    _wheelAmount * 2.5 -
                    Math.Abs(mouseRecoil) * 1.0;

                pose.RightPawAngle =
                    -34.0 +
                    keyboardAmount * 20.0 -
                    keyboardReach * keyboardAmount *
                        (keyboardReach < 0.0 ? 90.0 : 78.0) +
                    keyboardRecoil * 3.2;
                pose.RightPawOffsetX =
                    keyboardReach * keyboardAmount *
                    (keyboardReach > 0.0 ? 16.0 : 2.0);
                pose.RightPawOffsetY =
                    keyboardAmount * 4.2 -
                    keyboardRecoil * 1.6 +
                    keyboardRow * keyboardAmount * 4.0 -
                    _wheelAmount * 1.5;

                if (bongoInputActive)
                {
                    double bodyReaction =
                        _interactionMotion.BodyReaction;
                    pose.BodyOffsetY += bodyReaction * 13.0;
                    pose.BodyScaleX += bodyReaction * 0.022;
                    pose.BodyScaleY -= bodyReaction * 0.030;
                    pose.CharacterAngle +=
                        keyboardReach *
                        _interactionMotion.TypingEnergy *
                        0.55;
                }
            }

            if (Math.Abs(_dragLean) > 0.001 ||
                Math.Abs(_dragVertical) > 0.001)
            {
                double dragAmount = Math.Min(
                    1.0,
                    Math.Abs(_dragLean) +
                    Math.Abs(_dragVertical) * 0.55);
                pose.CharacterAngle -= _dragLean * 4.8;
                pose.CharacterScaleX += dragAmount * 0.012;
                pose.CharacterScaleY -= dragAmount * 0.018;
                pose.CharacterOffsetY += _dragVertical * 1.8;
                pose.TailAngle -= _dragLean * 17.0;
                pose.TailFlex -= _dragLean * 5.0;
                pose.LeftPawOffsetY += Math.Abs(_dragVertical) * 1.2;
                pose.RightPawOffsetY += Math.Abs(_dragVertical) * 1.2;
            }

            if (_waveStartedAt >= 0 && now < _waveUntil)
            {
                double phase = (now - _waveStartedAt) / 1000.0;
                double lift = Math.Sin(Math.Min(1.0, phase / 0.28) * Math.PI * 0.5);
                double lower = phase > 1.18
                    ? Math.Cos(Math.Min(1.0, (phase - 1.18) / 0.32) * Math.PI * 0.5)
                    : 1.0;
                pose.LeftPawAngle =
                    (92.0 + Math.Sin(phase * Math.PI * 6.0) * 12.0) * lift * lower;
            }

            if (_poseRecoveryActive)
            {
                if (now >= _tailRecoveryUntil ||
                    _tailRecoveryUntil <= _poseRecoveryStartedAt)
                {
                    _poseRecoveryActive = false;
                }
                else
                {
                    double bodyRecoveryWeight = 0.0;
                    if (now < _poseRecoveryUntil &&
                        _poseRecoveryUntil >
                            _poseRecoveryStartedAt)
                    {
                        double recoveryProgress =
                            (now - _poseRecoveryStartedAt) /
                            (double)(
                                _poseRecoveryUntil -
                                _poseRecoveryStartedAt);
                        bodyRecoveryWeight =
                            1.0 -
                            SmoothStep(
                                Clamp(
                                    recoveryProgress,
                                    0.0,
                                    1.0));
                    }

                    double tailRecoveryProgress =
                        (now - _poseRecoveryStartedAt) /
                        (double)(
                            _tailRecoveryUntil -
                            _poseRecoveryStartedAt);
                    double tailRecoveryWeight =
                        1.0 -
                        SmoothStep(
                            Clamp(
                                tailRecoveryProgress,
                                0.0,
                                1.0));
                    AddRecoveryDelta(
                        ref pose,
                        _poseRecoveryDelta,
                        bodyRecoveryWeight,
                        tailRecoveryWeight);
                }
            }

            return pose;
        }

        private static PetPose CreateRecoveryDelta(
            PetPose interrupted,
            PetPose target)
        {
            PetPose delta = new PetPose();
            delta.BodyOffsetY = interrupted.BodyOffsetY - target.BodyOffsetY;
            delta.BodyScaleX = interrupted.BodyScaleX - target.BodyScaleX;
            delta.BodyScaleY = interrupted.BodyScaleY - target.BodyScaleY;
            delta.TailAngle = interrupted.TailAngle - target.TailAngle;
            delta.TailFlex = interrupted.TailFlex - target.TailFlex;
            delta.LeftPawAngle =
                interrupted.LeftPawAngle -
                target.LeftPawAngle;
            delta.RightPawAngle =
                interrupted.RightPawAngle -
                target.RightPawAngle;
            delta.LeftPawOffsetX =
                interrupted.LeftPawOffsetX -
                target.LeftPawOffsetX;
            delta.LeftPawOffsetY =
                interrupted.LeftPawOffsetY -
                target.LeftPawOffsetY;
            delta.RightPawOffsetX =
                interrupted.RightPawOffsetX -
                target.RightPawOffsetX;
            delta.RightPawOffsetY =
                interrupted.RightPawOffsetY -
                target.RightPawOffsetY;
            delta.CharacterOffsetY =
                interrupted.CharacterOffsetY -
                target.CharacterOffsetY;
            delta.CharacterScaleX =
                interrupted.CharacterScaleX -
                target.CharacterScaleX;
            delta.CharacterScaleY =
                interrupted.CharacterScaleY -
                target.CharacterScaleY;
            delta.CharacterAngle =
                interrupted.CharacterAngle -
                target.CharacterAngle;
            return delta;
        }

        private static void AddRecoveryDelta(
            ref PetPose pose,
            PetPose delta,
            double bodyAmount,
            double tailAmount)
        {
            pose.BodyOffsetY += delta.BodyOffsetY * bodyAmount;
            pose.BodyScaleX += delta.BodyScaleX * bodyAmount;
            pose.BodyScaleY += delta.BodyScaleY * bodyAmount;
            pose.TailAngle += delta.TailAngle * tailAmount;
            pose.TailFlex += delta.TailFlex * tailAmount;
            pose.LeftPawAngle += delta.LeftPawAngle * bodyAmount;
            pose.RightPawAngle += delta.RightPawAngle * bodyAmount;
            pose.LeftPawOffsetX += delta.LeftPawOffsetX * bodyAmount;
            pose.LeftPawOffsetY += delta.LeftPawOffsetY * bodyAmount;
            pose.RightPawOffsetX += delta.RightPawOffsetX * bodyAmount;
            pose.RightPawOffsetY += delta.RightPawOffsetY * bodyAmount;
            pose.CharacterOffsetY +=
                delta.CharacterOffsetY * bodyAmount;
            pose.CharacterScaleX +=
                delta.CharacterScaleX * bodyAmount;
            pose.CharacterScaleY +=
                delta.CharacterScaleY * bodyAmount;
            pose.CharacterAngle +=
                delta.CharacterAngle * bodyAmount;
        }

        private static PetPose BlendPetPose(
            PetPose from,
            PetPose to,
            double amount)
        {
            amount = Clamp(amount, 0.0, 1.0);
            PetPose result = new PetPose();
            result.BodyOffsetY = Lerp(from.BodyOffsetY, to.BodyOffsetY, amount);
            result.BodyScaleX = Lerp(from.BodyScaleX, to.BodyScaleX, amount);
            result.BodyScaleY = Lerp(from.BodyScaleY, to.BodyScaleY, amount);
            result.TailAngle = Lerp(from.TailAngle, to.TailAngle, amount);
            result.TailFlex = Lerp(from.TailFlex, to.TailFlex, amount);
            result.LeftPawAngle = Lerp(from.LeftPawAngle, to.LeftPawAngle, amount);
            result.RightPawAngle = Lerp(from.RightPawAngle, to.RightPawAngle, amount);
            result.LeftPawOffsetX = Lerp(
                from.LeftPawOffsetX,
                to.LeftPawOffsetX,
                amount);
            result.LeftPawOffsetY = Lerp(
                from.LeftPawOffsetY,
                to.LeftPawOffsetY,
                amount);
            result.RightPawOffsetX = Lerp(
                from.RightPawOffsetX,
                to.RightPawOffsetX,
                amount);
            result.RightPawOffsetY = Lerp(
                from.RightPawOffsetY,
                to.RightPawOffsetY,
                amount);
            result.CharacterOffsetY = Lerp(
                from.CharacterOffsetY,
                to.CharacterOffsetY,
                amount);
            result.CharacterScaleX = Lerp(
                from.CharacterScaleX,
                to.CharacterScaleX,
                amount);
            result.CharacterScaleY = Lerp(
                from.CharacterScaleY,
                to.CharacterScaleY,
                amount);
            result.CharacterAngle = Lerp(
                from.CharacterAngle,
                to.CharacterAngle,
                amount);
            return result;
        }

        private static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double scaleX = ActualWidth / BaseWidth;
            double scaleY = ActualHeight / BaseHeight;
            _windowScale.ScaleX = scaleX;
            _windowScale.ScaleY = scaleY;
            drawingContext.PushTransform(_windowScale);

            double seconds = _clock.Elapsed.TotalSeconds;
            long now = _clock.ElapsedMilliseconds;
            PetPose pose = BuildPose(seconds, now);
            bool showBongoDesk = IsBongoDeskActive();

            if (showBongoDesk)
            {
                DrawBongoSurface(drawingContext);
            }
            DrawShadow(drawingContext, pose.BodyOffsetY);
            _characterTranslate.Y = pose.CharacterOffsetY;
            _characterRotate.Angle = pose.CharacterAngle;
            _characterScale.ScaleX = pose.CharacterScaleX;
            _characterScale.ScaleY = pose.CharacterScaleY;
            drawingContext.PushTransform(_characterTranslate);
            drawingContext.PushTransform(_characterRotate);
            drawingContext.PushTransform(_characterScale);
            DrawRigTail(drawingContext, pose);
            DrawRigBody(drawingContext, pose);
            if (!showBongoDesk)
            {
                DrawRigFrontLeg(
                    drawingContext,
                    _rigLeftLeg,
                    94,
                    pose.LeftPawAngle,
                    pose.LeftPawOffsetX,
                    pose.BodyOffsetY + pose.LeftPawOffsetY);
                DrawRigFrontLeg(
                    drawingContext,
                    _rigRightLeg,
                    126,
                    pose.RightPawAngle,
                    pose.RightPawOffsetX,
                    pose.BodyOffsetY + pose.RightPawOffsetY);
            }
            DrawRigHead(drawingContext, pose);
            if (!showBongoDesk)
            {
                DrawRigBow(drawingContext, pose);
            }
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();

            if (showBongoDesk)
            {
                DrawBongoDesk(drawingContext);
                drawingContext.PushTransform(_characterTranslate);
                drawingContext.PushTransform(_characterRotate);
                drawingContext.PushTransform(_characterScale);
                DrawRigFrontLeg(
                    drawingContext,
                    _rigLeftLeg,
                    94,
                    pose.LeftPawAngle,
                    pose.LeftPawOffsetX,
                    pose.BodyOffsetY + pose.LeftPawOffsetY);
                DrawRigFrontLeg(
                    drawingContext,
                    _rigRightLeg,
                    126,
                    pose.RightPawAngle,
                    pose.RightPawOffsetX,
                    pose.BodyOffsetY + pose.RightPawOffsetY);
                DrawRigBow(drawingContext, pose);
                drawingContext.Pop();
                drawingContext.Pop();
                drawingContext.Pop();
            }

            DrawBehaviorEffects(drawingContext, seconds);

            if (_messageUntil > now)
            {
                DrawMessageBubble(drawingContext);
            }

            drawingContext.Pop();
        }

        private void DrawRigTail(DrawingContext dc, PetPose pose)
        {
            // The bitmap is pre-mirrored during loading. Every live transform now
            // shares one anatomical root, hidden beneath the right hip.
            _tailTranslate.Y = pose.BodyOffsetY;
            _tailRotate.Angle = pose.TailAngle;
            _tailSkew.AngleX = pose.TailFlex;
            dc.PushTransform(_tailTranslate);
            dc.PushTransform(_tailRotate);
            dc.PushTransform(_tailSkew);
            dc.DrawImage(_rigTail, new Rect(133, 137, 72, 102));
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawRigHead(DrawingContext dc, PetPose pose)
        {
            _headTranslate.X = _headShiftX;
            _headTranslate.Y = _headShiftY + pose.BodyOffsetY * 0.18;
            _headRotate.Angle = _headAngle;
            double headCompression =
                Clamp(_interactionMotion.BodyReaction, -0.12, 0.15);
            _headScale.ScaleX = 1.0 + headCompression * 0.045;
            _headScale.ScaleY = 1.0 - headCompression * 0.065;
            dc.PushTransform(_headTranslate);
            dc.PushTransform(_headRotate);
            dc.PushTransform(_headScale);

            Rect headRect = new Rect(22, 4, 176, 162);
            // Keep one fully opaque head underneath the blink overlay. A
            // traditional two-layer cross-fade briefly reduces combined alpha
            // on a transparent window and looks like the whole pet flickers.
            dc.DrawImage(_rigHead, headRect);
            if (_blinkAmount > 0.005)
            {
                dc.PushOpacity(_blinkAmount);
                dc.DrawImage(_rigBlinkHead, headRect);
                dc.Pop();
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawRigBody(DrawingContext dc, PetPose pose)
        {
            _bodyScale.ScaleX = pose.BodyScaleX;
            _bodyScale.ScaleY = pose.BodyScaleY;
            _bodyTranslate.Y = pose.BodyOffsetY;
            dc.PushTransform(_bodyScale);
            dc.PushTransform(_bodyTranslate);
            dc.DrawImage(_rigBody, new Rect(55, 138, 110, 113));
            dc.Pop();
            dc.Pop();
        }

        private void DrawRigBow(DrawingContext dc, PetPose pose)
        {
            _bowTranslate.Y = pose.BodyOffsetY * 0.55;
            _bowTranslate.X =
                -_interactionMotion.HeadReaction * 2.2;
            _bowRotate.Angle =
                _headAngle * 0.08 -
                _interactionMotion.BodyReaction * 18.0 -
                _interactionMotion.HeadReaction * 7.0;
            dc.PushTransform(_bowTranslate);
            dc.PushTransform(_bowRotate);
            dc.DrawImage(_rigBow, new Rect(67, 140, 86, 56));
            dc.Pop();
            dc.Pop();
        }

        private void DrawRigFrontLeg(
            DrawingContext dc,
            BitmapSource leg,
            double pivotX,
            double angle,
            double offsetX,
            double bodyOffsetY)
        {
            TranslateTransform translate = pivotX < 110
                ? _leftLegTranslate
                : _rightLegTranslate;
            RotateTransform rotate = pivotX < 110
                ? _leftLegRotate
                : _rightLegRotate;
            ScaleTransform scale = pivotX < 110
                ? _leftLegScale
                : _rightLegScale;
            double contact = pivotX < 110
                ? Math.Max(
                    _interactionMotion.LeftMouseContact,
                    _interactionMotion.RightMouseContact)
                : _interactionMotion.KeyboardContact;
            translate.X = offsetX;
            translate.Y = bodyOffsetY;
            rotate.Angle = angle;
            scale.ScaleX = 1.0 + Clamp(contact, 0.0, 1.0) * 0.018;
            scale.ScaleY = 1.0 - Clamp(contact, 0.0, 1.0) * 0.024;
            dc.PushTransform(translate);
            dc.PushTransform(rotate);
            dc.PushTransform(scale);
            dc.DrawImage(leg, new Rect(pivotX - 16, 157, 32, 72));
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawShadow(DrawingContext dc, double breathe)
        {
            dc.DrawEllipse(ShadowBrush, null, new Point(110, 244), 60, 7 - breathe * 0.20);
        }

        private void DrawBongoSurface(DrawingContext dc)
        {
            dc.DrawDrawing(BongoSurfaceDrawing);
        }

        private void DrawBongoDesk(DrawingContext dc)
        {
            dc.DrawDrawing(BongoDeskDrawing);

            for (int index = 0; index < BongoKeyCaps.Length; index++)
            {
                double amount = _bongoKeyAmounts[index];
                if (amount <= 0.01)
                {
                    continue;
                }

                BongoKeyCap key = BongoKeyCaps[index];
                double visibleAmount = Clamp(amount, 0.0, 1.0);
                double depth =
                    0.35 +
                    visibleAmount *
                    (0.85 +
                     _interactionMotion.KeyboardContact * 0.35);
                dc.PushOpacity(visibleAmount);
                dc.DrawRoundedRectangle(
                    BongoKeyboardBrush,
                    null,
                    key.Bounds,
                    1.7,
                    1.7);
                dc.DrawRoundedRectangle(
                    BongoKeyRecessBrush,
                    null,
                    new Rect(
                        key.Bounds.X + 0.35,
                        key.Bounds.Y + depth + 0.65,
                        Math.Max(0.0, key.Bounds.Width - 0.7),
                        Math.Max(0.0, key.Bounds.Height - 0.55)),
                    1.6,
                    1.6);
                TranslateTransform keyDepth =
                    _bongoKeyDepthTransforms[index];
                keyDepth.Y = depth;
                dc.PushTransform(keyDepth);
                dc.DrawRoundedRectangle(
                    BongoLeftActiveBrush,
                    BongoKeyPen,
                    key.Bounds,
                    1.7,
                    1.7);
                dc.DrawDrawing(key.ActiveLabel);
                dc.Pop();
                dc.Pop();
            }

            double physicalLeftVisual = Math.Max(
                _leftMouseAmount,
                _interactionMotion.LeftMouseContact);
            double physicalRightVisual = Math.Max(
                _rightMouseAmount,
                _interactionMotion.RightMouseContact);

            dc.PushTransform(BongoMouseUserOrientationTransform);

            if (physicalLeftVisual > 0.01)
            {
                double depth =
                    Clamp(physicalLeftVisual, 0.0, 1.0) * 1.25;
                dc.PushOpacity(
                    Clamp(physicalLeftVisual, 0.0, 1.0) * 0.96);
                _bongoLeftMouseDepth.Y = -depth;
                dc.PushTransform(_bongoLeftMouseDepth);
                dc.DrawGeometry(
                    BongoLeftActiveBrush,
                    null,
                    BongoMouseLeftButtonGeometry);
                dc.DrawGeometry(
                    null,
                    BongoMouseDetailPen,
                    BongoMouseLeftButtonGeometry);
                dc.Pop();
                dc.Pop();
                dc.PushOpacity(
                    Clamp(physicalLeftVisual, 0.0, 1.0) * 0.92);
                dc.DrawEllipse(
                    BongoLeftActiveBrush,
                    null,
                    new Point(23.0, 219.2),
                    3.1,
                    1.25);
                dc.Pop();
            }

            if (physicalRightVisual > 0.01)
            {
                double depth =
                    Clamp(physicalRightVisual, 0.0, 1.0) * 1.25;
                dc.PushOpacity(
                    Clamp(physicalRightVisual, 0.0, 1.0) * 0.96);
                _bongoRightMouseDepth.Y = -depth;
                dc.PushTransform(_bongoRightMouseDepth);
                dc.DrawGeometry(
                    BongoRightActiveBrush,
                    null,
                    BongoMouseRightButtonGeometry);
                dc.DrawGeometry(
                    null,
                    BongoMouseDetailPen,
                    BongoMouseRightButtonGeometry);
                dc.Pop();
                dc.Pop();
                dc.PushOpacity(
                    Clamp(physicalRightVisual, 0.0, 1.0) * 0.92);
                dc.DrawEllipse(
                    BongoRightActiveBrush,
                    null,
                    new Point(49.0, 219.2),
                    3.1,
                    1.25);
                dc.Pop();
            }

            dc.DrawGeometry(null, BongoOutlinePen, BongoMouseBodyGeometry);
            dc.DrawLine(
                BongoMouseDetailPen,
                new Point(36, 199),
                new Point(36, 222));
            dc.DrawRoundedRectangle(
                BongoMouseWheelSlotBrush,
                null,
                new Rect(31, 203, 10, 18),
                5,
                5);
            double wheelOffset =
                _interactionMotion.WheelMotion * 1.4;
            _bongoWheelTranslate.Y = wheelOffset;
            dc.PushTransform(_bongoWheelTranslate);
            dc.DrawRoundedRectangle(
                _wheelAmount > 0.01
                    ? BongoWheelActiveBrush
                    : BongoMouseWheelBrush,
                BongoMouseDetailPen,
                new Rect(33, 205, 6, 13),
                3,
                3);
            dc.DrawLine(BongoMouseDetailPen, new Point(34, 208), new Point(38, 208));
            dc.DrawLine(BongoMouseDetailPen, new Point(34, 211), new Point(38, 211));
            dc.DrawLine(BongoMouseDetailPen, new Point(34, 214), new Point(38, 214));
            dc.Pop();
            dc.Pop();

            if (_interactionMotion.Engagement > 0.04)
            {
                double glowOpacity =
                    Clamp(_interactionMotion.Engagement * 0.34, 0.0, 0.34);
                dc.PushOpacity(glowOpacity);
                dc.DrawEllipse(
                    BongoContactGlowBrush,
                    null,
                    new Point(
                        140.0 + _interactionMotion.KeyboardReach * 55.0,
                        225.0 + _interactionMotion.KeyboardRow * 24.0),
                    8.0,
                    1.6);
                dc.Pop();
            }
        }

        private void DrawBehaviorEffects(DrawingContext dc, double seconds)
        {
            if (_behavior.Current != CatBehavior.Purring)
            {
                return;
            }

            for (int index = 0; index < 3; index++)
            {
                double phase = (seconds * 0.55 + index * 0.34) % 1.0;
                double opacity = Math.Sin(phase * Math.PI) * 0.82;
                double x = 48.0 + index * 61.0 + Math.Sin(phase * Math.PI * 2.0) * 5.0;
                double y = 170.0 - phase * 105.0;
                TranslateTransform heartTransform = _floatingHeartTransforms[index];
                heartTransform.X = x;
                heartTransform.Y = y;

                dc.PushOpacity(opacity);
                dc.PushTransform(heartTransform);
                dc.DrawGeometry(index % 2 == 0 ? PinkBrush : CollarBrush, null, FloatingHeartGeometry);
                dc.Pop();
                dc.Pop();
            }
        }

        private void DrawTail(DrawingContext dc, double sway, double breathe)
        {
            dc.PushTransform(new TranslateTransform(0, breathe * 0.30));
            dc.PushTransform(new RotateTransform(sway * 0.45, 67, 194));

            StreamGeometry tail = new StreamGeometry();
            using (StreamGeometryContext context = tail.Open())
            {
                context.BeginFigure(new Point(75, 205), true, true);
                context.BezierTo(
                    new Point(42, 217),
                    new Point(20, 195),
                    new Point(27, 166),
                    true,
                    false);
                context.BezierTo(
                    new Point(31, 147),
                    new Point(48, 135),
                    new Point(59, 145),
                    true,
                    false);
                context.BezierTo(
                    new Point(68, 153),
                    new Point(65, 165),
                    new Point(57, 169),
                    true,
                    false);
                context.BezierTo(
                    new Point(50, 171),
                    new Point(47, 163),
                    new Point(51, 156),
                    true,
                    false);
                context.BezierTo(
                    new Point(41, 175),
                    new Point(49, 194),
                    new Point(75, 188),
                    true,
                    false);
            }
            tail.Freeze();
            dc.DrawGeometry(FurBrush, OutlinePen, tail);

            StreamGeometry stripeOne = new StreamGeometry();
            using (StreamGeometryContext context = stripeOne.Open())
            {
                context.BeginFigure(new Point(31, 176), false, false);
                context.BezierTo(
                    new Point(35, 183),
                    new Point(40, 187),
                    new Point(47, 190),
                    true,
                    false);
            }
            stripeOne.Freeze();
            dc.DrawGeometry(null, MakePen(FurShadowBrush, 5.2), stripeOne);

            dc.Pop();
            dc.Pop();
        }

        private void DrawBody(DrawingContext dc, PetPose pose)
        {
            dc.PushTransform(new TranslateTransform(0, pose.BodyOffsetY));

            dc.DrawEllipse(FurBrush, OutlinePen, new Point(110, 180), 50, 60);
            dc.DrawEllipse(FurLightBrush, null, new Point(110, 187), 31, 40);

            Pen bodyStripePen = MakePen(FurShadowBrush, 5.2);
            dc.DrawLine(bodyStripePen, new Point(145, 171), new Point(155, 176));
            dc.DrawLine(bodyStripePen, new Point(146, 184), new Point(157, 188));

            dc.DrawEllipse(FurBrush, OutlinePen, new Point(77, 225), 27, 16);
            dc.DrawEllipse(FurBrush, OutlinePen, new Point(143, 225), 27, 16);
            dc.DrawEllipse(FurLightBrush, null, new Point(76, 229), 20, 9);
            dc.DrawEllipse(FurLightBrush, null, new Point(144, 229), 20, 9);

            DrawFrontPaw(dc, 94, 161, pose.LeftPawAngle);
            DrawFrontPaw(dc, 126, 161, pose.RightPawAngle);

            dc.Pop();
        }

        private void DrawFrontPaw(DrawingContext dc, double pivotX, double pivotY, double angle)
        {
            dc.PushTransform(new RotateTransform(angle, pivotX, pivotY));
            dc.DrawEllipse(FurBrush, OutlinePen, new Point(pivotX, 187), 13, 31);
            dc.DrawEllipse(FurLightBrush, ThinOutlinePen, new Point(pivotX, 210), 11, 8);
            dc.DrawLine(ToePen, new Point(pivotX - 4, 211), new Point(pivotX, 212));
            dc.Pop();
        }

        private void DrawHead(DrawingContext dc, double breathe)
        {
            TransformGroup headTransform = new TransformGroup();
            headTransform.Children.Add(new TranslateTransform(_headShiftX, _headShiftY + breathe * 0.25));
            headTransform.Children.Add(new RotateTransform(_headAngle, 110, 102));
            dc.PushTransform(headTransform);

            StreamGeometry leftEar = CreateTriangle(
                new Point(51, 66),
                new Point(63, 17),
                new Point(91, 47));
            StreamGeometry rightEar = CreateTriangle(
                new Point(129, 47),
                new Point(158, 17),
                new Point(169, 66));
            dc.DrawGeometry(FurBrush, OutlinePen, leftEar);
            dc.DrawGeometry(FurBrush, OutlinePen, rightEar);

            StreamGeometry leftEarInner = CreateTriangle(
                new Point(63, 56),
                new Point(66, 32),
                new Point(82, 49));
            StreamGeometry rightEarInner = CreateTriangle(
                new Point(139, 48),
                new Point(154, 31),
                new Point(157, 56));
            dc.DrawGeometry(EarBrush, null, leftEarInner);
            dc.DrawGeometry(EarBrush, null, rightEarInner);

            dc.DrawEllipse(FurBrush, OutlinePen, new Point(110, 91), 71, 60);

            DrawForeheadMarkings(dc);
            DrawEyes(dc);
            DrawMuzzle(dc);
            DrawCollar(dc);

            dc.Pop();
        }

        private void DrawForeheadMarkings(DrawingContext dc)
        {
            Pen markingPen = MakePen(FurShadowBrush, 4.0);
            dc.DrawLine(markingPen, new Point(101, 42), new Point(105, 55));
            dc.DrawLine(markingPen, new Point(112, 40), new Point(112, 54));
            dc.DrawLine(markingPen, new Point(123, 42), new Point(119, 55));
        }

        private void DrawEyes(DrawingContext dc)
        {
            double eyeHeight = 22.0 * (1.0 - _blinkAmount);

            if (eyeHeight < 4.0)
            {
                dc.DrawLine(ThinOutlinePen, new Point(74, 87), new Point(95, 87));
                dc.DrawLine(ThinOutlinePen, new Point(125, 87), new Point(146, 87));
                return;
            }

            dc.DrawEllipse(EyeGlintBrush, ThinOutlinePen, new Point(84, 85), 14, eyeHeight / 2.0);
            dc.DrawEllipse(EyeGlintBrush, ThinOutlinePen, new Point(136, 85), 14, eyeHeight / 2.0);

            double pupilRadiusY = Math.Max(2.2, 7.2 * (1.0 - _blinkAmount));
            dc.DrawEllipse(
                IrisBrush,
                null,
                new Point(84 + _pupilOffsetX, 86 + _pupilOffsetY),
                7.4,
                pupilRadiusY);
            dc.DrawEllipse(
                IrisBrush,
                null,
                new Point(136 + _pupilOffsetX, 86 + _pupilOffsetY),
                7.4,
                pupilRadiusY);
            dc.DrawEllipse(
                EyeBrush,
                null,
                new Point(84 + _pupilOffsetX, 87 + _pupilOffsetY),
                3.1,
                Math.Max(2.0, pupilRadiusY * 0.66));
            dc.DrawEllipse(
                EyeBrush,
                null,
                new Point(136 + _pupilOffsetX, 87 + _pupilOffsetY),
                3.1,
                Math.Max(2.0, pupilRadiusY * 0.66));

            dc.DrawEllipse(EyeGlintBrush, null, new Point(87 + _pupilOffsetX, 82 + _pupilOffsetY), 2.5, 2.5);
            dc.DrawEllipse(EyeGlintBrush, null, new Point(139 + _pupilOffsetX, 82 + _pupilOffsetY), 2.5, 2.5);
            dc.DrawEllipse(EyeGlintBrush, null, new Point(81 + _pupilOffsetX, 89 + _pupilOffsetY), 1.2, 1.2);
            dc.DrawEllipse(EyeGlintBrush, null, new Point(133 + _pupilOffsetX, 89 + _pupilOffsetY), 1.2, 1.2);
        }

        private void DrawMuzzle(DrawingContext dc)
        {
            dc.DrawEllipse(MuzzleBrush, null, new Point(98, 110), 21, 16);
            dc.DrawEllipse(MuzzleBrush, null, new Point(122, 110), 21, 16);

            StreamGeometry nose = CreateTriangle(
                new Point(104, 106),
                new Point(116, 106),
                new Point(110, 113));
            dc.DrawGeometry(PinkBrush, ThinOutlinePen, nose);
            dc.DrawLine(ThinOutlinePen, new Point(110, 113), new Point(110, 118));

            StreamGeometry mouth = new StreamGeometry();
            using (StreamGeometryContext context = mouth.Open())
            {
                context.BeginFigure(new Point(110, 118), false, false);
                context.BezierTo(new Point(107, 123), new Point(101, 123), new Point(99, 119), true, false);
                context.BeginFigure(new Point(110, 118), false, false);
                context.BezierTo(new Point(113, 123), new Point(119, 123), new Point(121, 119), true, false);
            }
            mouth.Freeze();
            dc.DrawGeometry(null, ThinOutlinePen, mouth);

            dc.DrawEllipse(PinkBrush, ThinOutlinePen, new Point(110, 124), 5.2, 3.6);

            dc.DrawEllipse(CheekBrush, null, new Point(72, 108), 11, 5.5);
            dc.DrawEllipse(CheekBrush, null, new Point(148, 108), 11, 5.5);
            dc.DrawLine(ToePen, new Point(64, 106), new Point(71, 103));
            dc.DrawLine(ToePen, new Point(67, 112), new Point(75, 110));
            dc.DrawLine(ToePen, new Point(149, 103), new Point(156, 106));
            dc.DrawLine(ToePen, new Point(145, 110), new Point(153, 112));

            dc.DrawLine(WhiskerPen, new Point(91, 109), new Point(58, 104));
            dc.DrawLine(WhiskerPen, new Point(91, 115), new Point(55, 116));
            dc.DrawLine(WhiskerPen, new Point(129, 109), new Point(162, 104));
            dc.DrawLine(WhiskerPen, new Point(129, 115), new Point(165, 116));
        }

        private void DrawCollar(DrawingContext dc)
        {
            Rect collarRect = new Rect(65, 128, 90, 13);
            dc.DrawRoundedRectangle(CollarBrush, CollarPen, collarRect, 7, 7);
            dc.DrawEllipse(BellBrush, ThinOutlinePen, new Point(110, 145), 8.5, 8.5);
            dc.DrawEllipse(EyeGlintBrush, null, new Point(107, 142), 2.1, 2.1);
            dc.DrawLine(ThinOutlinePen, new Point(106, 148), new Point(114, 148));
            dc.DrawEllipse(OutlineBrush, null, new Point(110, 151), 1.2, 1.2);
        }

        private void DrawMessageBubble(DrawingContext dc)
        {
            Rect bubble = new Rect(48, 1, 124, 35);
            dc.DrawRoundedRectangle(BubbleBrush, BubblePen, bubble, 13, 13);

            dc.DrawGeometry(BubbleBrush, BubblePen, BubblePointerGeometry);
            dc.DrawLine(BubbleCoverPen, new Point(99, 34), new Point(111, 34));
            dc.DrawGeometry(PinkBrush, null, BubbleLeftHeartGeometry);
            dc.DrawGeometry(CollarBrush, null, BubbleRightHeartGeometry);

            if (_formattedMessage == null)
            {
                ShowMessage(_message, 1800, _clock.ElapsedMilliseconds);
            }

            dc.DrawText(
                _formattedMessage,
                new Point(110 - _formattedMessage.Width / 2.0, 8));
        }

        private static StreamGeometry CreateTriangle(Point first, Point second, Point third)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(first, true, true);
                context.LineTo(second, true, false);
                context.LineTo(third, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static StreamGeometry CreateHeart(Point center, double size)
        {
            StreamGeometry heart = new StreamGeometry();
            using (StreamGeometryContext context = heart.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y + size), true, true);
                context.BezierTo(
                    new Point(center.X - size * 1.45, center.Y),
                    new Point(center.X - size * 0.85, center.Y - size),
                    new Point(center.X, center.Y - size * 0.25),
                    true,
                    false);
                context.BezierTo(
                    new Point(center.X + size * 0.85, center.Y - size),
                    new Point(center.X + size * 1.45, center.Y),
                    new Point(center.X, center.Y + size),
                    true,
                    false);
            }
            heart.Freeze();
            return heart;
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            if (GetPetHitZone(hitTestParameters.HitPoint) !=
                PetHitZone.None)
            {
                return new PointHitTestResult(this, hitTestParameters.HitPoint);
            }

            return null;
        }

        private IntPtr PetWindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmMouseActivate)
            {
                handled = true;
                return new IntPtr(MaNoActivate);
            }

            if (message == WmNcHitTest &&
                !_isDragging &&
                !_dragPending &&
                !IsMouseCaptured)
            {
                NativeRect rect;
                if (TryGetPetWindowRect(out rect))
                {
                    int width =
                        Math.Max(
                            1,
                            rect.Right - rect.Left);
                    int height =
                        Math.Max(
                            1,
                            rect.Bottom - rect.Top);
                    long packed = lParam.ToInt64();
                    int screenX =
                        unchecked(
                            (short)(packed & 0xFFFF));
                    int screenY =
                        unchecked(
                            (short)((packed >> 16) & 0xFFFF));
                    double x =
                        (screenX - rect.Left) *
                        BaseWidth /
                        width;
                    double y =
                        (screenY - rect.Top) *
                        BaseHeight /
                        height;
                    if (ResolvePetHitZone(x, y) ==
                        PetHitZone.None)
                    {
                        handled = true;
                        return new IntPtr(HtTransparent);
                    }
                }
            }

            return IntPtr.Zero;
        }

        private PetHitZone GetPetHitZone(Point point)
        {
            double x = point.X * BaseWidth / Math.Max(ActualWidth, 1.0);
            double y = point.Y * BaseHeight / Math.Max(ActualHeight, 1.0);
            return ResolvePetHitZone(x, y);
        }

        private PetHitZone ResolvePetHitZone(double x, double y)
        {
            if (_messageUntil > _clock.ElapsedMilliseconds &&
                x >= 45.0 &&
                x <= 175.0 &&
                y >= 0.0 &&
                y <= 48.0)
            {
                return PetHitZone.Bubble;
            }

            if (IsBongoDeskActive())
            {
                if (EllipseContains(x, y, 36.0, 226.0, 25.5, 30.0))
                {
                    return PetHitZone.BongoMouse;
                }
                if (x >= 64.0 &&
                    x <= 216.0 &&
                    y >= 197.0 &&
                    y <= 256.0)
                {
                    return PetHitZone.BongoKeyboard;
                }
                if (x >= 0.0 &&
                    x <= 220.0 &&
                    y >= 192.0 &&
                    y <= 260.0)
                {
                    return PetHitZone.BongoDesk;
                }
            }

            if (EllipseContains(
                x,
                y,
                110.0 + _headShiftX,
                94.0 + _headShiftY,
                100.0,
                91.0))
            {
                return PetHitZone.Head;
            }
            // The head is rendered in front of the tail.  Below it, the
            // visible tail starts outside the body's actual right silhouette.
            if (x >= 166.0 && IsTailHit(x, y))
            {
                return PetHitZone.Tail;
            }
            if (x >= 36.0 && x <= 184.0 && y >= 145.0 && y <= 250.0)
            {
                return PetHitZone.Body;
            }

            return PetHitZone.None;
        }

        private static bool IsTailHit(double x, double y)
        {
            // The tail is drawn behind the head and body.  Three slightly
            // overlapping capsules follow only its visible outer silhouette,
            // avoiding the old large rectangle that was swallowed by Body.
            return
                EllipseContains(x, y, 192.0, 165.0, 19.0, 38.0) ||
                EllipseContains(x, y, 185.0, 202.0, 23.0, 39.0) ||
                EllipseContains(x, y, 165.0, 228.0, 25.0, 18.0);
        }

        private static bool EllipseContains(double x, double y, double centerX, double centerY, double radiusX, double radiusY)
        {
            double normalizedX = (x - centerX) / radiusX;
            double normalizedY = (y - centerY) / radiusY;
            return normalizedX * normalizedX + normalizedY * normalizedY <= 1.0;
        }

        private void PetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PetHitZone zone = GetPetHitZone(e.GetPosition(this));
            _pointerDownZone = zone;
            if (zone == PetHitZone.None)
            {
                return;
            }

            if (IsBongoDeskActive() && zone == PetHitZone.BongoMouse)
            {
                long now = _clock.ElapsedMilliseconds;
                _localBongoMousePress = true;
                _mouseLeftDown = true;
                _mouseLeftPulseUntil = now + 145;
                _mouseLeftAutoReleaseAt = 0;
                _interactionMotion.RegisterMouseDown(true, now);
                _lastBongoInputAt = now;
                InterruptAutonomousBehaviorForBongo(now);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (e.ClickCount >= 2 &&
                (zone == PetHitZone.Head ||
                 zone == PetHitZone.Body))
            {
                ShowGreeting();
                e.Handled = true;
                return;
            }

            _dragPending = true;
            _isDragging = false;
            _dragStartCursor = Forms.Cursor.Position;
            _lastDragMotionCursor = _dragStartCursor;
            _lastDragMotionAt = _clock.ElapsedMilliseconds;
            _dragLeanTarget = 0.0;
            _dragVerticalTarget = 0.0;
            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            GetWindowRect(windowHandle, out _dragStartWindowRect);
            CaptureMouse();
            e.Handled = true;
        }

        private void PetMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            PetHitZone zone = GetPetHitZone(e.GetPosition(this));
            if (!IsBongoDeskActive() ||
                zone != PetHitZone.BongoMouse)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            _localBongoRightMousePress = true;
            _mouseRightDown = true;
            _mouseRightPulseUntil = now + 145;
            _mouseRightAutoReleaseAt = 0;
            _interactionMotion.RegisterMouseDown(false, now);
            _lastBongoInputAt = now;
            InterruptAutonomousBehaviorForBongo(now);
            CaptureMouse();
            e.Handled = true;
        }

        private void PetMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_localBongoRightMousePress)
            {
                return;
            }

            _localBongoRightMousePress = false;
            _mouseRightDown = false;
            _mouseRightAutoReleaseAt = 0;
            _interactionMotion.RegisterMouseUp(
                false,
                _clock.ElapsedMilliseconds);
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        private void PetMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            UpdatePetCursor(e.GetPosition(this));
            if (_localBongoMousePress)
            {
                return;
            }

            if ((!_isDragging && !_dragPending) ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Drawing.Point cursor = Forms.Cursor.Position;
            if (_dragPending)
            {
                int thresholdX = cursor.X - _dragStartCursor.X;
                int thresholdY = cursor.Y - _dragStartCursor.Y;
                Drawing.Size dragSize =
                    Forms.SystemInformation.DragSize;
                int halfWidth = Math.Max(2, dragSize.Width / 2);
                int halfHeight = Math.Max(2, dragSize.Height / 2);
                if (Math.Abs(thresholdX) < halfWidth &&
                    Math.Abs(thresholdY) < halfHeight)
                {
                    return;
                }

                _dragPending = false;
                _isDragging = true;
                CancelBehavior(_clock.ElapsedMilliseconds);
            }

            long dragNow = _clock.ElapsedMilliseconds;
            if (_lastDragMotionAt > 0 && dragNow > _lastDragMotionAt)
            {
                double elapsed =
                    Math.Max(0.001, (dragNow - _lastDragMotionAt) / 1000.0);
                double velocityX =
                    (cursor.X - _lastDragMotionCursor.X) / elapsed;
                double velocityY =
                    (cursor.Y - _lastDragMotionCursor.Y) / elapsed;
                _dragLeanTarget = Clamp(velocityX / 1450.0, -1.0, 1.0);
                _dragVerticalTarget = Clamp(velocityY / 1450.0, -1.0, 1.0);
            }
            _lastDragMotionCursor = cursor;
            _lastDragMotionAt = dragNow;

            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            NativeRect currentRect;
            if (!GetWindowRect(windowHandle, out currentRect))
            {
                return;
            }

            int proposedLeft = _dragStartWindowRect.Left + cursor.X - _dragStartCursor.X;
            int proposedTop = _dragStartWindowRect.Top + cursor.Y - _dragStartCursor.Y;
            int windowWidth = Math.Max(1, currentRect.Right - currentRect.Left);
            int windowHeight = Math.Max(1, currentRect.Bottom - currentRect.Top);
            Drawing.Rectangle workArea = Forms.Screen.FromPoint(cursor).WorkingArea;

            proposedLeft = ClampInteger(
                proposedLeft,
                workArea.Left,
                Math.Max(workArea.Left, workArea.Right - windowWidth));
            proposedTop = ClampInteger(
                proposedTop,
                workArea.Top,
                Math.Max(workArea.Top, workArea.Bottom - windowHeight));

            if (proposedLeft != currentRect.Left ||
                proposedTop != currentRect.Top)
            {
                SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    proposedLeft,
                    proposedTop,
                    0,
                    0,
                    SetWindowPosNoSize |
                    SetWindowPosNoZOrder |
                    SetWindowPosNoActivate);
            }
            e.Handled = true;
        }

        private void UpdatePetCursor(Point point)
        {
            if (_isDragging || _dragPending)
            {
                Cursor = Cursors.SizeAll;
                return;
            }

            switch (GetPetHitZone(point))
            {
                case PetHitZone.Head:
                case PetHitZone.Body:
                case PetHitZone.Tail:
                case PetHitZone.Bubble:
                case PetHitZone.BongoMouse:
                    Cursor = Cursors.Hand;
                    break;
                case PetHitZone.BongoKeyboard:
                case PetHitZone.BongoDesk:
                    Cursor = Cursors.SizeAll;
                    break;
                default:
                    Cursor = Cursors.Arrow;
                    break;
            }
        }

        private void PetMouseLeave(
            object sender,
            System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging &&
                !_dragPending &&
                !_localBongoMousePress &&
                !_localBongoRightMousePress)
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void PetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_localBongoMousePress)
            {
                _localBongoMousePress = false;
                _mouseLeftDown = false;
                _mouseLeftAutoReleaseAt = 0;
                _interactionMotion.RegisterMouseUp(
                    true,
                    _clock.ElapsedMilliseconds);
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }
                _pointerDownZone = PetHitZone.None;
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                FinishDragging();
            }
            else if (_dragPending)
            {
                PetHitZone pressedZone = _pointerDownZone;
                _dragPending = false;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }

                PetHitZone releaseZone =
                    GetPetHitZone(e.GetPosition(this));
                if ((pressedZone == PetHitZone.Head ||
                     pressedZone == PetHitZone.Body) &&
                    (releaseZone == PetHitZone.Head ||
                     releaseZone == PetHitZone.Body))
                {
                    PetTheCat();
                }
                else if (pressedZone == PetHitZone.Tail &&
                         releaseZone == PetHitZone.Tail)
                {
                    TouchTheTail();
                }
                else if (pressedZone == PetHitZone.Bubble &&
                         releaseZone == PetHitZone.Bubble)
                {
                    if (_behavior.IsBusy &&
                        _behavior.Current ==
                            CatBehavior.Begging)
                    {
                        FeedTheCat();
                    }
                    else
                    {
                        ShowGreeting();
                    }
                }
                else if (pressedZone == PetHitZone.BongoKeyboard &&
                         releaseZone == PetHitZone.BongoKeyboard)
                {
                    TapBongoKeyboard();
                }
                else if (pressedZone == PetHitZone.BongoDesk &&
                         releaseZone == PetHitZone.BongoDesk)
                {
                    TapBongoDesk();
                }
            }
            _pointerDownZone = PetHitZone.None;
            e.Handled = true;
        }

        private void TapBongoKeyboard()
        {
            const int spaceVirtualKey = 0x20;
            long now = _clock.ElapsedMilliseconds;
            _keyPulseUntilByVirtualKey[spaceVirtualKey] = now + 175;
            _leftInputPulseUntil = now + 160;
            _interactionMotion.RegisterKeyDown(
                spaceVirtualKey,
                GetBongoKeyReach(spaceVirtualKey),
                GetBongoKeyRow(spaceVirtualKey),
                false,
                now);
            _lastBongoInputAt = now;
            InterruptAutonomousBehaviorForBongo(now);
        }

        private void TapBongoDesk()
        {
            long now = _clock.ElapsedMilliseconds;
            _interactionMotion.RegisterDeskTap(now);
            _lastBongoInputAt = now;
            InterruptAutonomousBehaviorForBongo(now);
        }

        private void PetLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_localBongoRightMousePress)
            {
                _localBongoRightMousePress = false;
                _mouseRightDown = false;
                _mouseRightAutoReleaseAt = 0;
                _interactionMotion.RegisterMouseUp(
                    false,
                    _clock.ElapsedMilliseconds);
                return;
            }

            if (_localBongoMousePress)
            {
                _localBongoMousePress = false;
                _mouseLeftDown = false;
                _mouseLeftAutoReleaseAt = 0;
                _interactionMotion.RegisterMouseUp(
                    true,
                    _clock.ElapsedMilliseconds);
                _pointerDownZone = PetHitZone.None;
                return;
            }

            if (_isDragging)
            {
                FinishDragging();
            }
            else
            {
                _dragPending = false;
            }
            _dragLeanTarget = 0.0;
            _dragVerticalTarget = 0.0;
            _pointerDownZone = PetHitZone.None;
        }

        private void FinishDragging()
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            _dragPending = false;
            _pointerDownZone = PetHitZone.None;
            _dragLeanTarget = 0.0;
            _dragVerticalTarget = 0.0;
            _hasAutoWindowPosition = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            EnsureVisibleOnAnyScreen();
            SavePosition();
        }

        private void SetUserScale(double scale)
        {
            double normalizedScale = NormalizeUserScale(scale);
            if (Math.Abs(normalizedScale - _userScale) < 0.0001)
            {
                RefreshMenuState();
                RefreshTrayMenuState();
                return;
            }

            if (_activeProp != null)
            {
                CloseActiveProp();
            }
            if (_behavior.IsBusy)
            {
                CancelBehavior(_clock.ElapsedMilliseconds);
            }

            _userScale = normalizedScale;
            ApplyScale(true);
            SaveSimpleSetting("Scale", _userScale.ToString(CultureInfo.InvariantCulture));
            SavePosition();
            RefreshMenuState();
            RefreshTrayMenuState();
            InvalidateVisual();
        }

        private static double NormalizeUserScale(double scale)
        {
            if (Double.IsNaN(scale) ||
                Double.IsInfinity(scale))
            {
                return 1.0;
            }

            int percentage = (int)Math.Round(
                scale * 100.0,
                MidpointRounding.AwayFromZero);
            percentage = ClampInteger(
                percentage,
                MinimumScalePercentage,
                MaximumScalePercentage);
            return percentage / 100.0;
        }

        private void ApplyScale(bool preserveBottomCenter)
        {
            double oldWidth = double.IsNaN(Width) ? BaseWidth : Width;
            double oldHeight = double.IsNaN(Height) ? BaseHeight : Height;
            double oldCenter = Left + oldWidth / 2.0;
            double oldBottom = Top + oldHeight;

            Width = BaseWidth * _userScale;
            Height = BaseHeight * _userScale;

            if (preserveBottomCenter)
            {
                Left = oldCenter - Width / 2.0;
                Top = oldBottom - Height;
                if (IsVisible)
                {
                    EnsureVisibleOnAnyScreen();
                }
                else
                {
                    _needsVisibilityCorrection = true;
                }
            }
        }

        private void RestoreOrChooseInitialPosition()
        {
            double savedLeft;
            double savedTop;
            if (TryReadDoubleSetting("Left", out savedLeft) && TryReadDoubleSetting("Top", out savedTop))
            {
                Left = savedLeft;
                Top = savedTop;
                return;
            }

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 26;
            Top = workArea.Bottom - Height - 18;
        }

        private void MoveToPrimaryScreen()
        {
            PrepareManualShow();
            if (!IsVisible)
            {
                Show();
            }

            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            NativeRect windowRect;
            if (windowHandle != IntPtr.Zero && GetWindowRect(windowHandle, out windowRect))
            {
                Drawing.Rectangle workArea = Forms.Screen.PrimaryScreen.WorkingArea;
                int windowWidth = Math.Max(1, windowRect.Right - windowRect.Left);
                int windowHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);
                int targetLeft = Math.Max(workArea.Left, workArea.Right - windowWidth - 26);
                int targetTop = Math.Max(workArea.Top, workArea.Bottom - windowHeight - 18);
                SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    targetLeft,
                    targetTop,
                    0,
                    0,
                    SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
            }
            else
            {
                Rect workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 26;
                Top = workArea.Bottom - Height - 18;
            }

            SavePosition();
            UpdateTrayDescription();
        }

        private void EnsureVisibleOnAnyScreen()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top))
            {
                Rect workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 26;
                Top = workArea.Bottom - Height - 18;
                return;
            }

            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            NativeRect windowRect;
            if (windowHandle != IntPtr.Zero && GetWindowRect(windowHandle, out windowRect))
            {
                int windowWidth = Math.Max(1, windowRect.Right - windowRect.Left);
                int windowHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);
                Drawing.Rectangle bounds = new Drawing.Rectangle(
                    windowRect.Left,
                    windowRect.Top,
                    windowWidth,
                    windowHeight);
                Drawing.Rectangle workArea = Forms.Screen.FromRectangle(bounds).WorkingArea;

                int clampedLeft = ClampInteger(
                    windowRect.Left,
                    workArea.Left,
                    Math.Max(workArea.Left, workArea.Right - windowWidth));
                int clampedTop = ClampInteger(
                    windowRect.Top,
                    workArea.Top,
                    Math.Max(workArea.Top, workArea.Bottom - windowHeight));

                if (clampedLeft != windowRect.Left || clampedTop != windowRect.Top)
                {
                    SetWindowPos(
                        windowHandle,
                        IntPtr.Zero,
                        clampedLeft,
                        clampedTop,
                        0,
                        0,
                        SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
                }
                return;
            }

            Rect fallbackWorkArea = SystemParameters.WorkArea;
            Left = fallbackWorkArea.Right - Width - 26;
            Top = fallbackWorkArea.Bottom - Height - 18;
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (_isExiting || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke((Action)delegate
            {
                if (_isExiting || Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                _fullscreenEnterSamples = 0;
                _fullscreenExitSamples = 0;
                _lastFullscreenSample =
                    FullscreenSample.Unknown;
                _lastFullscreenWindow = IntPtr.Zero;
                _bypassedFullscreenWindow = IntPtr.Zero;
                _hasLastPetPixelBounds = false;
                if (!IsVisible)
                {
                    _needsVisibilityCorrection = true;
                    return;
                }

                EnsureVisibleOnAnyScreen();
                SavePosition();
            });
        }

        private void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key == null)
                    {
                        return;
                    }

                    object scaleValue = key.GetValue("Scale");
                    double parsedScale;
                    if (scaleValue != null &&
                        double.TryParse(
                            scaleValue.ToString(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out parsedScale) &&
                        parsedScale >=
                            MinimumScalePercentage / 100.0 &&
                        parsedScale <=
                            MaximumScalePercentage / 100.0)
                    {
                        _userScale =
                            NormalizeUserScale(parsedScale);
                    }

                    _followMouse = ReadInteger(key, "FollowMouse", 1) != 0;
                    Topmost = ReadInteger(key, "Topmost", 1) != 0;
                    _autoInteraction = ReadInteger(key, "AutoInteraction", 1) != 0;
                    _bongoMode = ReadInteger(key, "BongoMode", 1) != 0;
                    _autoHideFullscreen =
                        ReadInteger(
                            key,
                            "AutoHideFullscreen",
                            1) != 0;
                    _hasShownWelcome =
                        ReadInteger(key, "HasShownWelcome", 0) != 0;
                    int savedPersonality = ReadInteger(
                        key,
                        "MotionPersonality",
                        (int)MotionPersonality.Natural);
                    _interactionMotion.Personality =
                        savedPersonality >= (int)MotionPersonality.Quiet &&
                        savedPersonality <= (int)MotionPersonality.Playful
                            ? (MotionPersonality)savedPersonality
                            : MotionPersonality.Natural;
                }
            }
            catch
            {
                _userScale = 0.80;
                _followMouse = true;
                Topmost = true;
                _autoInteraction = true;
                _bongoMode = true;
                _autoHideFullscreen = true;
                _interactionMotion.Personality = MotionPersonality.Natural;
            }
        }

        private void LoadPetState()
        {
            double hunger;
            double energy;
            double mood;
            double affection;
            double boredom;
            if (!TryReadDoubleSetting("Hunger", out hunger))
            {
                hunger = 50.0;
            }
            if (!TryReadDoubleSetting("Energy", out energy))
            {
                energy = 82.0;
            }
            if (!TryReadDoubleSetting("Mood", out mood))
            {
                mood = 74.0;
            }
            if (!TryReadDoubleSetting("Affection", out affection))
            {
                affection = 50.0;
            }
            if (!TryReadDoubleSetting("Boredom", out boredom))
            {
                boredom = 52.0;
            }

            DateTime lastNeedsUtc = DateTime.UtcNow;
            long savedTicks;
            if (TryReadLongSetting("LastNeedsUtcTicks", out savedTicks) &&
                savedTicks >= DateTime.MinValue.Ticks &&
                savedTicks <= DateTime.MaxValue.Ticks)
            {
                lastNeedsUtc = new DateTime(savedTicks, DateTimeKind.Utc);
            }

            _behavior.Restore(
                hunger,
                energy,
                mood,
                affection,
                boredom,
                lastNeedsUtc);
        }

        private void SavePetState()
        {
            SaveSimpleSetting(
                "Hunger",
                _behavior.Hunger.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting(
                "Energy",
                _behavior.Energy.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting(
                "Mood",
                _behavior.Mood.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting(
                "Affection",
                _behavior.Affection.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting(
                "Boredom",
                _behavior.Boredom.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting(
                "LastNeedsUtcTicks",
                _behavior.LastNeedsUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static int ReadInteger(RegistryKey key, string name, int fallback)
        {
            object value = key.GetValue(name);
            if (value == null)
            {
                return fallback;
            }

            int parsed;
            if (int.TryParse(value.ToString(), out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private bool TryReadDoubleSetting(string name, out double value)
        {
            value = 0.0;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object raw = key.GetValue(name);
                    if (raw == null ||
                        !double.TryParse(
                            raw.ToString(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out value))
                    {
                        return false;
                    }

                    return !double.IsNaN(value) &&
                           !double.IsInfinity(value) &&
                           Math.Abs(value) <= 10000000.0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadLongSetting(string name, out long value)
        {
            value = 0L;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object raw = key.GetValue(name);
                    return raw != null &&
                           long.TryParse(
                               raw.ToString(),
                               NumberStyles.Integer,
                               CultureInfo.InvariantCulture,
                               out value);
                }
            }
            catch
            {
                return false;
            }
        }

        private void SavePosition()
        {
            SaveSimpleSetting("Left", Left.ToString(CultureInfo.InvariantCulture));
            SaveSimpleSetting("Top", Top.ToString(CultureInfo.InvariantCulture));
        }

        private bool SaveSimpleSetting(string name, object value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value);
                        return true;
                    }
                }
            }
            catch
            {
                // A read-only or restricted profile should not stop the pet
                // from running.
            }

            if (!_hasShownSettingsSaveError &&
                !_isDiagnosticPreview)
            {
                _hasShownSettingsSaveError = true;
                ShowMessage(
                    "设置已生效，但暂时无法保存",
                    3200,
                    _clock.ElapsedMilliseconds);
                if (_notifyIcon != null)
                {
                    _notifyIcon.BalloonTipTitle =
                        "设置暂时无法保存";
                    _notifyIcon.BalloonTipText =
                        "本次设置已经生效，但重启后可能恢复原样。请检查当前 Windows 账户是否允许保存个人设置。";
                    _notifyIcon.BalloonTipIcon =
                        Forms.ToolTipIcon.Warning;
                    _notifyIcon.ShowBalloonTip(3200);
                }
            }
            return false;
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object value =
                        key.GetValue(StartupValueName);
                    if (value == null)
                    {
                        return false;
                    }

                    string executablePath =
                        Process.GetCurrentProcess()
                            .MainModule.FileName;
                    string expected =
                        "\"" +
                        Path.GetFullPath(
                            executablePath) +
                        "\"";
                    return String.Equals(
                        value.ToString().Trim(),
                        expected,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private void SetStartupEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("无法打开系统启动项设置。");
                    }

                    if (enabled)
                    {
                        string executablePath = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(
                            StartupValueName,
                            "\"" +
                            Path.GetFullPath(
                                executablePath) +
                            "\"");
                    }
                    else
                    {
                        key.DeleteValue(StartupValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "开机自启设置失败：\n" + ex.Message,
                    AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static Drawing.Icon CreateTrayIcon()
        {
            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(32, 32))
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
            using (Drawing.SolidBrush fur = new Drawing.SolidBrush(Drawing.Color.FromArgb(244, 139, 41)))
            using (Drawing.SolidBrush inner = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 154, 151)))
            using (Drawing.SolidBrush eye = new Drawing.SolidBrush(Drawing.Color.FromArgb(65, 139, 84)))
            using (Drawing.Pen outline = new Drawing.Pen(Drawing.Color.FromArgb(88, 48, 33), 2F))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Drawing.Color.Transparent);

                Drawing.Point[] leftEar = new Drawing.Point[]
                {
                    new Drawing.Point(5, 13),
                    new Drawing.Point(8, 2),
                    new Drawing.Point(15, 10)
                };
                Drawing.Point[] rightEar = new Drawing.Point[]
                {
                    new Drawing.Point(17, 10),
                    new Drawing.Point(25, 2),
                    new Drawing.Point(28, 14)
                };
                graphics.FillPolygon(fur, leftEar);
                graphics.DrawPolygon(outline, leftEar);
                graphics.FillPolygon(fur, rightEar);
                graphics.DrawPolygon(outline, rightEar);
                graphics.FillEllipse(fur, 4, 7, 24, 22);
                graphics.DrawEllipse(outline, 4, 7, 24, 22);
                graphics.FillEllipse(inner, 8, 5, 3, 6);
                graphics.FillEllipse(inner, 22, 5, 3, 6);
                graphics.FillEllipse(eye, 10, 16, 3, 4);
                graphics.FillEllipse(eye, 20, 16, 3, 4);
                graphics.FillEllipse(inner, 15, 21, 3, 2);

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    using (Drawing.Icon temporary = Drawing.Icon.FromHandle(iconHandle))
                    {
                        return (Drawing.Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        private void PetWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                TogglePetVisibility();
            }
        }

        public void ExitApplication()
        {
            if (_isExiting)
            {
                return;
            }

            _isExiting = true;
            if (!_isDiagnosticPreview)
            {
                SavePosition();
            }
            _behavior.AdvanceNeeds(DateTime.UtcNow, IsVisible);
            if (!_isDiagnosticPreview)
            {
                SavePetState();
            }
            _behavior.Cancel(_clock.ElapsedMilliseconds);
            CloseActiveProp();
            StopFullscreenMonitoring();
            StopInputMonitoring();
            DisposeInputMonitor();
            StopRendering();
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(PetWindowProc);
                _windowSource = null;
            }
            IsVisibleChanged -= PetWindowIsVisibleChanged;
            SourceInitialized -= PetSourceInitialized;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
            }

            WpfApplication.Current.Shutdown();
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double AdvancePhase(
            double phase,
            double amount)
        {
            double next = phase + amount;
            double fullTurn = Math.PI * 2.0;
            if (next >= fullTurn || next <= -fullTurn)
            {
                next %= fullTurn;
            }
            return next;
        }

        private static double Smooth(
            double current,
            double target,
            double amountAt60Hz,
            double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
            {
                return current;
            }

            amountAt60Hz = Clamp(amountAt60Hz, 0.0, 1.0);
            double amount = 1.0 - Math.Pow(1.0 - amountAt60Hz, deltaSeconds * 60.0);
            return current + (target - current) * amount;
        }

        private static int ClampInteger(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SetWindowPosNoSize = 0x0001;
        private const uint SetWindowPosNoZOrder = 0x0004;
        private const uint SetWindowPosNoActivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
