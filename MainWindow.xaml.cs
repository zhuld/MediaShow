using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using MediaShow.Models;
using MediaShow.Services;
using AForge.Video.DirectShow;

namespace MediaShow
{
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly AppConfig _config;
        private UdpControlService _udpService;
        private WebSettingsService? _webService;

        private readonly Dictionary<string, CameraService> _cameraServices = new();
        private readonly Dictionary<ViewItem, Controls.CameraView> _cameraViews = new();
        private readonly DispatcherTimer _statusTimer = new()
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        private volatile bool _isShuttingDown;
        private bool _allowClose;
        private bool _cleanupStarted;
        private HwndSource? _windowSource;
        private const string BlankModeName = "Blank";
        private const string DesktopModeName = "Desktop";
        private string _currentModeName = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            _statusTimer.Tick += StatusTimer_Tick;

            LoggerService.Info("主窗口构造开始...");

            _configService = new ConfigService(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
            _config = _configService.Load();

            LoggerService.Info($"配置加载完成 - WindowWidth: {_config.WindowWidth}, WindowHeight: {_config.WindowHeight}");

            // **关键**: 托盘图标由 TrayIconManager 统一创建（全进程唯一），
            // 这里只订阅菜单事件，避免出现第二个托盘图标
            SubscribeTrayEvents();
            TrayIconManager.Initialize(_config);

            // 提前创建窗口句柄（此时窗口还未显示），这样才能拿到窗口所在屏幕的 DPI，
            // 把配置中的物理像素尺寸换算为 WPF DIP 后再显示，避免先以错误尺寸显示再调整
            new WindowInteropHelper(this).EnsureHandle();
            ApplyWindowSizeFromConfig();

            // 每次启动自动扫描并列出本地可用摄像头
            AutoDiscoverLocalCameras();

            foreach (var cam in _config.CameraSources)
            {
                try
                {
                    var svc = new CameraService(cam);
                    svc.FrameReady += bitmap =>
                    {
                        DispatchToUi(() => UpdateCameraFrames(cam.Id, bitmap));
                    };
                    svc.ErrorOccurred += err =>
                    {
                        LoggerService.Error($"摄像头{cam.Name} 运行错误：{err}");
                    };
                    _cameraServices[cam.Id] = svc;
                    LoggerService.Info($"摄像头服务已创建：{cam.Id} ({cam.Name}, {cam.Width}x{cam.Height})");
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"摄像头{cam.Name} 创建失败", ex);
                }
            }

            _udpService = CreateUdpService();

            try
            {
                _udpService.Start();
            }
            catch (Exception ex)
            {
                LoggerService.Error("UDP 服务启动失败", ex);
            }

            StartWebSettingsService();

            LoggerService.Info("主窗口构造完成");
        }

        /// <summary>
        /// 创建 UDP 控制服务并订阅模式切换事件（设置变更后需要重建服务）
        /// </summary>
        private UdpControlService CreateUdpService()
        {
            var service = new UdpControlService(_config.PcName, _config.UdpPort);
            service.ModeChanged += modeName =>
            {
                DispatchToUi(() =>
                {
                    ApplyViewsLayout(modeName);
                    ShowModeStatus(_currentModeName);
                    LoggerService.Info($"网络指令切换布局：{modeName}");
                });
            };
            return service;
        }

        private void SubscribeTrayEvents()
        {
            TrayIconManager.ModeSelected += OnTrayModeSelected;
            TrayIconManager.BlankRequested += OnTrayBlankRequested;
            TrayIconManager.DesktopRequested += OnTrayDesktopRequested;
            TrayIconManager.ShutdownRequested += OnTrayShutdownRequested;
            TrayIconManager.SettingsRequested += OnTraySettingsRequested;
            TrayIconManager.ExitRequested += OnTrayExitRequested;
            TrayIconManager.RestoreRequested += OnTrayRestoreRequested;
        }

        private void UnsubscribeTrayEvents()
        {
            TrayIconManager.ModeSelected -= OnTrayModeSelected;
            TrayIconManager.BlankRequested -= OnTrayBlankRequested;
            TrayIconManager.DesktopRequested -= OnTrayDesktopRequested;
            TrayIconManager.ShutdownRequested -= OnTrayShutdownRequested;
            TrayIconManager.SettingsRequested -= OnTraySettingsRequested;
            TrayIconManager.ExitRequested -= OnTrayExitRequested;
            TrayIconManager.RestoreRequested -= OnTrayRestoreRequested;
        }

        private void OnTrayModeSelected(string modeName)
        {
            if (_isShuttingDown) return;

            _currentModeName = modeName;
            ApplyViewsLayout(modeName);
            ShowModeStatus(modeName);
            LoggerService.Info($"手动切换模式：{modeName}");
        }

        private void OnTrayBlankRequested()
        {
            if (_isShuttingDown) return;

            ApplyViewsLayout(BlankModeName);
        }

        private void OnTrayDesktopRequested()
        {
            if (_isShuttingDown) return;

            ApplyDesktopMode();
            LoggerService.Info("手动切换模式：Desktop");
        }

        private void OnTrayShutdownRequested()
        {
            if (_isShuttingDown) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 0",
                    Verb = "runas"
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LoggerService.Error("关机命令执行失败", ex);
            }

            _currentModeName = "Shutdown";
            ShowModeStatus("Shutdown");
            LoggerService.Info("手动切换模式：Shutdown");
        }

        private void OnTraySettingsRequested()
        {
            if (_isShuttingDown) return;

            // 等托盘菜单关闭后再弹出模态对话框，避免在菜单点击回调里直接打开窗口
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowSettingsWindow));
        }

        private void ShowSettingsWindow()
        {
            if (_isShuttingDown) return;

            try
            {
                var dialog = new SettingsWindow(_config, _configService.ConfigPath, GetWindowStatus());

                // 主窗口处于最小化（Desktop 模式）时不能作为拥有者，否则对话框可能被隐藏
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    dialog.Owner = this;
                }

                if (dialog.ShowDialog() == true && dialog.ResultConfig != null)
                {
                    ApplySettings(dialog.ResultConfig);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("设置窗口打开或应用设置失败", ex);
            }
        }

        /// <summary>
        /// 将设置对话框 / 网页返回的配置合并到运行中的配置对象，并立即生效
        /// </summary>
        /// <param name="updated">新的完整配置</param>
        /// <param name="interactive">保存失败时是否弹窗提示（网页入口使用 false，仅记录日志）</param>
        /// <returns>是否成功写入 config.json</returns>
        private bool ApplySettings(AppConfig updated, bool interactive = true)
        {
            var udpChanged = !string.Equals(_config.PcName, updated.PcName, StringComparison.Ordinal)
                             || _config.UdpPort != updated.UdpPort;
            var webPortChanged = _config.WebPort != updated.WebPort;
            var previousMode = _currentModeName;

            _config.PcName = updated.PcName;
            _config.UdpPort = updated.UdpPort;
            _config.WebPort = updated.WebPort;
            _config.DefaultMode = updated.DefaultMode;
            _config.WindowWidth = updated.WindowWidth;
            _config.WindowHeight = updated.WindowHeight;
            _config.Modes = updated.Modes;

            var saved = _configService.Save(_config);
            if (!saved)
            {
                LoggerService.Error("保存设置到 config.json 失败");
                if (interactive)
                {
                    System.Windows.MessageBox.Show(
                        "设置已应用，但写入 config.json 失败，请查看 Logs 文件夹中的日志了解详情。",
                        "MediaShow",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            TrayIconManager.ReloadConfig(_config);

            if (udpChanged)
            {
                RestartUdpService();
            }

            if (webPortChanged)
            {
                RestartWebSettingsService();
            }

            if (WindowState == WindowState.Minimized)
            {
                // Desktop 模式（窗口最小化）下不改动窗口状态，尺寸与布局在恢复显示时再应用
                LoggerService.Info("窗口当前处于最小化（Desktop）状态，尺寸与布局将在恢复显示时应用");
            }
            else
            {
                ApplyWindowSizeFromConfig();

                // 尽量保持当前显示的画面：Blank 保持黑屏，其他模式被删除时回退到默认模式
                string targetMode;
                if (string.Equals(previousMode, BlankModeName, StringComparison.OrdinalIgnoreCase))
                {
                    targetMode = BlankModeName;
                }
                else if (_config.Modes.Any(mode => string.Equals(mode.Mode, previousMode, StringComparison.OrdinalIgnoreCase)))
                {
                    targetMode = previousMode;
                }
                else
                {
                    targetMode = _config.DefaultMode;
                }

                ApplyViewsLayout(targetMode);
            }

            LoggerService.Info($"设置已保存并应用：PcName={_config.PcName}, UdpPort={_config.UdpPort}, WebPort={_config.WebPort}, DefaultMode={_config.DefaultMode}, 模式数={_config.Modes.Count}");
            return saved;
        }

        private void RestartUdpService()
        {
            try
            {
                _udpService.Dispose();
            }
            catch (Exception ex)
            {
                LoggerService.Error("停止旧的 UDP 控制服务失败", ex);
            }

            _udpService = CreateUdpService();

            try
            {
                _udpService.Start();
                LoggerService.Info($"UDP 控制服务已按新配置重启：{_config.PcName}:{_config.UdpPort}");
            }
            catch (Exception ex)
            {
                LoggerService.Error("UDP 控制服务重启失败", ex);
            }
        }

        /// <summary>
        /// 启动内嵌的网页设置服务（WebPort 为 0 时表示关闭）
        /// </summary>
        private void StartWebSettingsService()
        {
            if (_config.WebPort <= 0)
            {
                LoggerService.Info("网页设置服务未启用（WebPort = 0）");
                return;
            }

            try
            {
                _webService = new WebSettingsService(_config.WebPort, GetConfigSnapshot, ApplySettingsFromWeb, GetWindowStatusSnapshot);
                _webService.Start();
            }
            catch (Exception ex)
            {
                LoggerService.Error($"网页设置服务启动失败（端口 {_config.WebPort}）", ex);
                _webService?.Dispose();
                _webService = null;
            }
        }

        private void RestartWebSettingsService()
        {
            try
            {
                _webService?.Dispose();
            }
            catch (Exception ex)
            {
                LoggerService.Error("停止旧的网页设置服务失败", ex);
            }

            _webService = null;
            StartWebSettingsService();
        }

        /// <summary>
        /// 供网页服务在后台线程读取配置副本，避免与 UI 线程修改冲突
        /// </summary>
        private AppConfig GetConfigSnapshot()
        {
            try
            {
                if (!_isShuttingDown && !Dispatcher.HasShutdownStarted)
                {
                    return Dispatcher.Invoke(() => ConfigValidator.Clone(_config));
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("读取配置快照失败", ex);
            }

            return ConfigValidator.Clone(_config);
        }

        /// <summary>
        /// 网页提交配置后，回到 UI 线程执行与设置对话框相同的应用流程
        /// </summary>
        private bool ApplySettingsFromWeb(AppConfig config)
        {
            if (_isShuttingDown) return false;

            try
            {
                return Dispatcher.Invoke(() => ApplySettings(config, interactive: false));
            }
            catch (Exception ex)
            {
                LoggerService.Error("应用网页设置失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 读取主窗口的实际显示尺寸（WPF DIP）并按当前 DPI 换算为物理像素
        /// </summary>
        private WindowStatus GetWindowStatus()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = ActualWidth;
            var height = ActualHeight;

            // 最小化或尚未完成布局时 ActualWidth/Height 可能为 0，改用恢复后的尺寸
            if (width <= 0) width = RestoreBounds.Width;
            if (height <= 0) height = RestoreBounds.Height;

            // 配置中的宽高是物理像素，回退计算时需要换算为 DIP
            if (width <= 0) width = _config.WindowWidth > 0 ? _config.WindowWidth / dpi.DpiScaleX : SystemParameters.FullPrimaryScreenWidth;
            if (height <= 0) height = _config.WindowHeight > 0 ? _config.WindowHeight / dpi.DpiScaleY : SystemParameters.FullPrimaryScreenHeight;

            return new WindowStatus
            {
                WidthDip = Math.Round(width, 1),
                HeightDip = Math.Round(height, 1),
                PixelWidth = (int)Math.Round(width * dpi.DpiScaleX),
                PixelHeight = (int)Math.Round(height * dpi.DpiScaleY),
                DpiScaleX = Math.Round(dpi.DpiScaleX, 2),
                DpiScaleY = Math.Round(dpi.DpiScaleY, 2),
                IsMinimized = WindowState == WindowState.Minimized,
                CurrentMode = _currentModeName
            };
        }

        /// <summary>
        /// 供网页服务在后台线程读取窗口状态
        /// </summary>
        private WindowStatus? GetWindowStatusSnapshot()
        {
            try
            {
                if (!_isShuttingDown && !Dispatcher.HasShutdownStarted)
                {
                    return Dispatcher.Invoke(GetWindowStatus);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("读取窗口状态失败", ex);
            }

            return null;
        }

        private void OnTrayExitRequested()
        {
            // 等托盘菜单处理完本次点击后再退出，避免在菜单回调中直接关闭应用
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ExitApplication));
        }

        private async void ExitApplication()
        {
            LoggerService.Info("收到托盘 Exit 指令，开始退出程序");

            // 此时托盘菜单已关闭，可以安全释放托盘图标
            TrayIconManager.Dispose();

            await CleanupServicesAsync();

            _allowClose = true;
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                Close();
            }
        }

        private void OnTrayRestoreRequested()
        {
            EnsureWindowVisible();
        }

        /// <summary>
        /// 从最小化/隐藏状态恢复到正常显示（Desktop 模式切回任何显示模式时使用）
        /// </summary>
        private void EnsureWindowVisible()
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                Show();
                WindowState = WindowState.Normal;
                // 设置可能已改变窗口尺寸，恢复显示时统一按当前配置重新应用
                ApplyWindowSizeFromConfig();
                Activate();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowHook);
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowSource?.RemoveHook(WindowHook);
            _windowSource = null;
            UnsubscribeTrayEvents();
            TrayIconManager.Dispose();
            base.OnClosed(e);
        }

        private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmDpiChanged = 0x02E0;
            if (message == WmDpiChanged && !_isShuttingDown)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!_isShuttingDown)
                    {
                        // 缩放比例变化后重新按配置的物理像素设置窗口尺寸
                        if (WindowState != WindowState.Minimized)
                        {
                            ApplyWindowSizeFromConfig();
                        }

                        ApplyViewsLayout(_currentModeName);
                    }
                }));
            }

            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 等窗口完成布局后再读取画布尺寸，并按当前 DPI 将配置像素换算为 WPF DIP。
            ApplyViewsLayout();

            foreach (var kvp in _cameraServices)
            {
                try { kvp.Value.Start(); }
                catch (Exception ex)
                {
                    LoggerService.Error($"摄像头{kvp.Key} 启动失败", ex);
                }
            }

            ShowModeStatus(_currentModeName);
            LoggerService.Info("主窗口加载完成");
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_allowClose) return;

            // 先取消本次关闭，等后台服务清理完成后再真正关闭窗口
            e.Cancel = true;
            await CleanupServicesAsync();

            _allowClose = true;
            Close();
        }

        /// <summary>
        /// 释放摄像头与 UDP 服务（幂等，可由关闭窗口、托盘 Exit 等多条路径触发）
        /// </summary>
        private async Task CleanupServicesAsync()
        {
            if (_cleanupStarted) return;
            _cleanupStarted = true;

            _isShuttingDown = true;
            IsEnabled = false;
            _statusTimer.Stop();

            await Task.Run(() =>
            {
                foreach (var service in _cameraServices.Values)
                {
                    try
                    {
                        service.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Error("关闭摄像头服务失败", ex);
                    }
                }

                try
                {
                    _udpService.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerService.Error("关闭 UDP 控制服务失败", ex);
                }

                try
                {
                    _webService?.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerService.Error("关闭网页设置服务失败", ex);
                }
            });

            LoggerService.Info("后台服务清理完成");
        }

        private void DispatchToUi(Action action)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            try
            {
                Dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException ex)
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    LoggerService.Error("派发 UI 更新失败", ex);
                }
            }
        }

        /// <summary>
        /// 根据 config.Modes 创建摄像头显示窗口
        /// 1 个视图：居中显示；2 个视图：左右排列；超过 2 个：暂时仅显示前 2 个
        /// 所有尺寸和位置完全来自 config.json ViewItem.W / ViewItem.H
        /// </summary>
        /// <summary>
        /// 内置 Blank 模式：移除所有摄像头画面，窗口只显示黑色背景
        /// </summary>
        private void ApplyBlankMode()
        {
            ClearCameraViews();
            _currentModeName = BlankModeName;
            ShowModeStatus(BlankModeName);
            LoggerService.Info("切换到 Blank 模式：黑屏，不显示任何内容");
        }

        /// <summary>
        /// 内置 Desktop 模式：最小化窗口，只显示桌面（保留当前视图，恢复窗口时可直接显示）
        /// </summary>
        private void ApplyDesktopMode()
        {
            _currentModeName = DesktopModeName;
            WindowState = WindowState.Minimized;
            ShowModeStatus(DesktopModeName);
            LoggerService.Info("切换到 Desktop 模式：最小化到桌面");
        }

        private void ClearCameraViews()
        {
            foreach (var view in _cameraViews.Values)
            {
                LayoutCanvas.Children.Remove(view);
            }

            _cameraViews.Clear();
        }

        private void ApplyViewsLayout(string? requestedMode = null)
        {
            var modeName = requestedMode ?? _config.DefaultMode;

            // 内置模式 Desktop：直接最小化，无需先恢复窗口
            if (string.Equals(modeName, DesktopModeName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyDesktopMode();
                return;
            }

            // 从 Desktop 模式（最小化）切回任何显示模式时，先把窗口恢复到正常状态
            EnsureWindowVisible();

            // 内置模式 Blank：清空所有摄像头画面，只显示黑色背景
            if (string.Equals(modeName, BlankModeName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyBlankMode();
                return;
            }

            if (_config.Modes == null || _config.Modes.Count == 0)
            {
                LoggerService.Warn("config.Modes 为空，未创建任何摄像头显示窗口");
                return;
            }

            var group = _config.Modes.FirstOrDefault(
                mode => string.Equals(mode.Mode, modeName, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                if (requestedMode != null)
                {
                    LoggerService.Warn($"未配置布局模式 {requestedMode}，保持当前布局不变");
                    return;
                }

                LoggerService.Warn($"未找到启动默认模式 {_config.DefaultMode}，回退到第一个布局 {_config.Modes[0].Mode}");
                group = _config.Modes[0];
            }

            var items = group.Views;
            _currentModeName = group.Mode;

            if (items == null || items.Count == 0)
            {
                ClearCameraViews();
                LoggerService.Info($"模式 {group.Mode} 没有配置视图，清空显示内容");
                return;
            }

            var visibleItems = items.Take(2).ToList();
            foreach (var oldView in _cameraViews.Keys.Where(item => !visibleItems.Contains(item)).ToList())
            {
                LayoutCanvas.Children.Remove(_cameraViews[oldView]);
                _cameraViews.Remove(oldView);
            }

            double canvasW = LayoutCanvas.ActualWidth;
            double canvasH = LayoutCanvas.ActualHeight;
            var dpi = VisualTreeHelper.GetDpi(this);
            LoggerService.Info($"按窗口当前 DPI 重新布局：{dpi.PixelsPerInchX}x{dpi.PixelsPerInchY}");

            if (items.Count == 1)
            {
                // 1 个view：居中显示
                var item = items[0];
                var width = item.W / dpi.DpiScaleX;
                var height = item.H / dpi.DpiScaleY;
                var x = (canvasW - width) / 2.0;
                var y = (canvasH - height) / 2.0;
                CreateCameraView(item, x, y, width, height, 0);
                LoggerService.Info($"单视图布局：居中显示{item.CameraId} ({item.W}x{item.H})");
            }
            else
            {
                // 2个view：左右排列（暂时忽略更多 view，仅取前 2 个）
                var item0 = items[0];
                var item1 = items.Count > 1 ? items[1] : items[0];
                var width0 = item0.W / dpi.DpiScaleX;
                var height0 = item0.H / dpi.DpiScaleY;
                var width1 = item1.W / dpi.DpiScaleX;
                var height1 = item1.H / dpi.DpiScaleY;
                var totalWidth = width0 + width1;
                var startX = (canvasW - totalWidth) / 2.0;
                var y0 = (canvasH - height0) / 2.0;
                var y1 = (canvasH - height1) / 2.0;

                CreateCameraView(item0, startX, y0, width0, height0, 0);
                CreateCameraView(item1, startX + width0, y1, width1, height1, 1);

                LoggerService.Info($"双视图布局：左右排列[{item0.CameraId}({item0.W}x{item0.H})] + [{item1.CameraId}({item1.W}x{item1.H})]");
                if (items.Count > 2)
                {
                    LoggerService.Warn($"视图数量 {items.Count} 超过 2，暂时仅显示前 2 个");
                }
            }
        }

        private Controls.CameraView CreateCameraView(ViewItem item, double x, double y, double w, double h, int zIndex)
        {
            if (_cameraViews.TryGetValue(item, out var existingView))
            {
                existingView.Width = w;
                existingView.Height = h;
                Canvas.SetLeft(existingView, x);
                Canvas.SetTop(existingView, y);
                Canvas.SetZIndex(existingView, zIndex);
                return existingView;
            }

            var camView = new Controls.CameraView
            {
                CameraId = item.CameraId,
                Width = w,
                Height = h
            };
            Canvas.SetLeft(camView, x);
            Canvas.SetTop(camView, y);
            Canvas.SetZIndex(camView, zIndex);

            _cameraViews[item] = camView;
            LayoutCanvas.Children.Add(camView);
            LoggerService.Info($"创建摄像头显示窗口：CameraId={item.CameraId}, X={x}, Y={y}, W={w}, H={h}");
            return camView;
        }

        private void UpdateCameraFrames(string cameraId, System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            foreach (var view in _cameraViews.Values)
            {
                if (string.Equals(view.CameraId, cameraId, StringComparison.Ordinal))
                {
                    view.SetImage(bitmap);
                }
            }
        }

        private void ShowModeStatus(string modeName)
        {
            if (string.IsNullOrWhiteSpace(modeName) || _isShuttingDown) return;

            StatusText.Text = modeName;
            ModeStatusBar.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void StatusTimer_Tick(object? sender, EventArgs e)
        {
            _statusTimer.Stop();
            ModeStatusBar.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 从配置文件应用窗口尺寸
        /// 配置值是物理像素，需按窗口当前 DPI 换算为 WPF DIP；宽度或高度为 0 时撑满全屏
        /// </summary>
        private void ApplyWindowSizeFromConfig()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyWindowSizeFromConfig);
                return;
            }

            int width = _config.WindowWidth;
            int height = _config.WindowHeight;

            if (width <= 0 || height <= 0)
            {
                // 撑满全屏 - 使用 Maximized 状态
                this.WindowState = WindowState.Maximized;
                // 确保窗体填充整个屏幕（SystemParameters 返回值已是 DIP）
                this.Width = SystemParameters.FullPrimaryScreenWidth;
                this.Height = SystemParameters.FullPrimaryScreenHeight;
            }
            else
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                this.Width = width / dpi.DpiScaleX;
                this.Height = height / dpi.DpiScaleY;
                this.WindowState = WindowState.Normal;
                LoggerService.Info(
                    $"按 DPI 应用窗口尺寸：配置 {width}x{height} 像素，缩放 {dpi.DpiScaleX}x{dpi.DpiScaleY} -> {this.Width:F1}x{this.Height:F1} DIP");
            }
        }

        /// <summary>
        /// 每次启动自动扫描本地摄像头（DirectShow），并覆盖配置中的摄像头清单
        /// </summary>
        private void AutoDiscoverLocalCameras()
        {
            LoggerService.Info("开始自动扫描本地摄像头（DirectShow）...");

            var discovered = new List<CameraSource>();

            try
            {
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                for (int i = 0; i < videoDevices.Count; i++)
                {
                    try
                    {
                        var device = new VideoCaptureDevice(videoDevices[i].MonikerString);
                        LoggerService.Info($"发现本地摄像头：索引 {i}, 名称 {videoDevices[i].Name}");

                        // 读取摄像头支持的首个分辨率，写入配置
                        int width = 0, height = 0;
                        try
                        {
                            var caps = device.VideoCapabilities;
                            if (caps != null && caps.Length > 0)
                            {
                                var size = caps[0].FrameSize;
                                width = size.Width;
                                height = size.Height;
                            }
                        }
                        catch { /* 读取分辨率失败不影响摄像头使用 */ }

                        discovered.Add(new CameraSource
                        {
                            Id = $"cam_{i}",
                            Name = videoDevices[i].Name,
                            Index = i,
                            Width = width,
                            Height = height
                        });
                        try { device.SignalToStop(); } catch { }
                        try { device.Stop(); } catch { }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Warn($"无法打开摄像头索引 {i} 进行测试：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("DirectShow 摄像头枚举失败", ex);
                return;
            }

            _config.CameraSources = discovered;
            UpdateViewCameraAssignments(discovered);

            if (_configService.Save(_config))
            {
                LoggerService.Info($"自动扫描完成，共发现 {discovered.Count} 个本地摄像头，已更新并保存摄像头清单和视图关联");
            }
            else
            {
                LoggerService.Error("自动保存摄像头清单到 config.json 失败");
            }
        }

        private void UpdateViewCameraAssignments(IReadOnlyList<CameraSource> cameras)
        {
            var cameraIds = new HashSet<string>(cameras.Select(camera => camera.Id));

            foreach (var group in _config.Modes)
            {
                if (group.Views == null)
                {
                    continue;
                }

                for (var i = 0; i < group.Views.Count; i++)
                {
                    var view = group.Views[i];
                    if (string.IsNullOrWhiteSpace(view.CameraId) || !cameraIds.Contains(view.CameraId))
                    {
                        view.CameraId = i < cameras.Count ? cameras[i].Id : string.Empty;
                    }
                }
            }
        }
    }
}
