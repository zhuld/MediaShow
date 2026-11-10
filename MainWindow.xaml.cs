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
using CameraShow.Models;
using CameraShow.Services;
using AForge.Video.DirectShow;
using System.Windows.Forms;

namespace CameraShow
{
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly AppConfig _config;
        private readonly UdpControlService _udpService;

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
        private string _currentModeName = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            _statusTimer.Tick += StatusTimer_Tick;

            LoggerService.Info("涓荤獥鍙ｆ瀯閫犲紑濮?..");

            _configService = new ConfigService("config.json");
            _config = _configService.Load();

            LoggerService.Info($"閰嶇疆鍔犺浇瀹屾垚 - WindowWidth: {_config.WindowWidth}, WindowHeight: {_config.WindowHeight}");

            // **鍏抽敭**: 鎵樼洏鍥炬爣鐢?TrayIconManager 缁熶竴鍒涘缓锛堝叏杩涚▼鍞竴锛夛紝
            // 杩欓噷鍙闃呰彍鍗曚簨浠讹紝閬垮厤鍑虹幇绗簩涓墭鐩樺浘鏍?
            SubscribeTrayEvents();
            TrayIconManager.Initialize(_config);

            // 搴旂敤绐楀彛灏哄閰嶇疆锛堜粠 config.json 璇诲彇 WindowWidth/WindowHeight锛?
            // 濡傛灉瀹藉害鎴栭珮搴︿负 0锛屽垯鎾戞弧鍏ㄥ睆
            ApplyWindowSizeFromConfig();

            // 姣忔鍚姩鑷姩鎵弿骞跺垪鍑烘湰鍦板彲鐢ㄦ憚鍍忓ご
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
                        LoggerService.Error($"鎽勫儚澶磠cam.Name} 杩愯閿欒锛歿err}");
                    };
                    _cameraServices[cam.Id] = svc;
                    LoggerService.Info($"鎽勫儚澶存湇鍔″凡鍒涘缓锛歿cam.Id} ({cam.Name}, {cam.Width}x{cam.Height})");
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"鎽勫儚澶磠cam.Name} 鍒涘缓澶辫触", ex);
                }
            }

            _udpService = new UdpControlService(_config.PcName, _config.UdpPort);
            _udpService.ModeChanged += modeName =>
            {
                DispatchToUi(() =>
                {
                    ApplyViewsLayout(modeName);
                    ShowModeStatus(_currentModeName);
                    LoggerService.Info($"缃戠粶鎸囦护鍒囨崲甯冨眬锛歿modeName}");
                });
            };

            try
            {
                _udpService.Start();
            }
            catch (Exception ex)
            {
                LoggerService.Error("UDP 鏈嶅姟鍚姩澶辫触", ex);
            }

            LoggerService.Info("涓荤獥鍙ｆ瀯閫犲畬鎴?);
        }

        private void SubscribeTrayEvents()
        {
            TrayIconManager.ModeSelected += OnTrayModeSelected;
            TrayIconManager.BlankRequested += OnTrayBlankRequested;
            TrayIconManager.DesktopRequested += OnTrayDesktopRequested;
            TrayIconManager.ShutdownRequested += OnTrayShutdownRequested;
            TrayIconManager.ExitRequested += OnTrayExitRequested;
            TrayIconManager.RestoreRequested += OnTrayRestoreRequested;
        }

        private void UnsubscribeTrayEvents()
        {
            TrayIconManager.ModeSelected -= OnTrayModeSelected;
            TrayIconManager.BlankRequested -= OnTrayBlankRequested;
            TrayIconManager.DesktopRequested -= OnTrayDesktopRequested;
            TrayIconManager.ShutdownRequested -= OnTrayShutdownRequested;
            TrayIconManager.ExitRequested -= OnTrayExitRequested;
            TrayIconManager.RestoreRequested -= OnTrayRestoreRequested;
        }

        private void OnTrayModeSelected(string modeName)
        {
            if (_isShuttingDown) return;

            _currentModeName = modeName;
            ApplyViewsLayout(modeName);
            ShowModeStatus(modeName);
            LoggerService.Info($"鎵嬪姩鍒囨崲妯″紡锛歿modeName}");
        }

        private void OnTrayBlankRequested()
        {
            if (_isShuttingDown) return;

            ApplyViewsLayout(BlankModeName);
        }

        private void OnTrayDesktopRequested()
        {
            if (_isShuttingDown) return;

            WindowState = WindowState.Minimized;
            _currentModeName = "Desktop";
            ShowModeStatus("Desktop");
            TrayIconManager.ShowBalloonTip("妯″紡鍒囨崲", "宸叉渶灏忓寲鍒版墭鐩橈紝鏄剧ず妗岄潰", ToolTipIcon.Info);
            LoggerService.Info("鎵嬪姩鍒囨崲妯″紡锛欴esktop");
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
                LoggerService.Error("鍏虫満鍛戒护鎵ц澶辫触", ex);
            }

            _currentModeName = "Shutdown";
            ShowModeStatus("Shutdown");
            TrayIconManager.ShowBalloonTip("妯″紡鍒囨崲", "姝ｅ湪鍏虫満", ToolTipIcon.Warning);
            LoggerService.Info("鎵嬪姩鍒囨崲妯″紡锛歋hutdown");
        }

        private void OnTrayExitRequested()
        {
            // 绛夋墭鐩樿彍鍗曞鐞嗗畬鏈鐐瑰嚮鍚庡啀閫€鍑猴紝閬垮厤鍦ㄨ彍鍗曞洖璋冧腑鐩存帴鍏抽棴搴旂敤
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ExitApplication));
        }

        private async void ExitApplication()
        {
            LoggerService.Info("鏀跺埌鎵樼洏 Exit 鎸囦护锛屽紑濮嬮€€鍑虹▼搴?);

            // 姝ゆ椂鎵樼洏鑿滃崟宸插叧闂紝鍙互瀹夊叏閲婃斁鎵樼洏鍥炬爣
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
            if (WindowState == WindowState.Minimized || !IsVisible)
            {
                Show();
                WindowState = WindowState.Normal;
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
                        ApplyViewsLayout(_currentModeName);
                    }
                }));
            }

            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 绛夌獥鍙ｅ畬鎴愬竷灞€鍚庡啀璇诲彇鐢诲竷灏哄锛屽苟鎸夊綋鍓?DPI 灏嗛厤缃儚绱犳崲绠椾负 WPF DIP銆?
            ApplyViewsLayout();

            foreach (var kvp in _cameraServices)
            {
                try { kvp.Value.Start(); }
                catch (Exception ex)
                {
                    LoggerService.Error($"鎽勫儚澶磠kvp.Key} 鍚姩澶辫触", ex);
                }
            }

            ShowModeStatus(_currentModeName);
            LoggerService.Info("涓荤獥鍙ｅ姞杞藉畬鎴?);
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

            // 鍏堝彇娑堟湰娆″叧闂紝绛夊悗鍙版湇鍔℃竻鐞嗗畬鎴愬悗鍐嶇湡姝ｅ叧闂獥鍙?
            e.Cancel = true;
            await CleanupServicesAsync();

            _allowClose = true;
            Close();
        }

        /// <summary>
        /// 閲婃斁鎽勫儚澶翠笌 UDP 鏈嶅姟锛堝箓绛夛紝鍙敱鍏抽棴绐楀彛銆佹墭鐩?Exit 绛夊鏉¤矾寰勮Е鍙戯級
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
                        LoggerService.Error("鍏抽棴鎽勫儚澶存湇鍔″け璐?, ex);
                    }
                }

                try
                {
                    _udpService.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerService.Error("鍏抽棴 UDP 鎺у埗鏈嶅姟澶辫触", ex);
                }
            });

            LoggerService.Info("鍚庡彴鏈嶅姟娓呯悊瀹屾垚");
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
                    LoggerService.Error("娲惧彂 UI 鏇存柊澶辫触", ex);
                }
            }
        }

        /// <summary>
        /// 鏍规嵁 config.Modes 鍒涘缓鎽勫儚澶存樉绀虹獥鍙?
        /// 1 涓鍥撅細灞呬腑鏄剧ず锛? 涓鍥撅細宸﹀彸鎺掑垪锛涜秴杩?2 涓細鏆傛椂浠呮樉绀哄墠 2 涓?
        /// 鎵€鏈夊昂瀵稿拰浣嶇疆瀹屽叏鏉ヨ嚜 config.json ViewItem.W / ViewItem.H
        /// </summary>
        /// <summary>
        /// 鍐呯疆 Blank 妯″紡锛氱Щ闄ゆ墍鏈夋憚鍍忓ご鐢婚潰锛岀獥鍙ｅ彧鏄剧ず榛戣壊鑳屾櫙
        /// </summary>
        private void ApplyBlankMode()
        {
            ClearCameraViews();
            _currentModeName = BlankModeName;
            ShowModeStatus(BlankModeName);
            LoggerService.Info("鍒囨崲鍒?Blank 妯″紡锛氶粦灞忥紝涓嶆樉绀轰换浣曞唴瀹?);
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

            // 鍐呯疆妯″紡 Blank锛氭竻绌烘墍鏈夋憚鍍忓ご鐢婚潰锛屽彧鏄剧ず榛戣壊鑳屾櫙
            if (string.Equals(modeName, BlankModeName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyBlankMode();
                return;
            }

            if (_config.Modes == null || _config.Modes.Count == 0)
            {
                LoggerService.Warn("config.Modes 涓虹┖锛屾湭鍒涘缓浠讳綍鎽勫儚澶存樉绀虹獥鍙?);
                return;
            }

            var group = _config.Modes.FirstOrDefault(
                mode => string.Equals(mode.Mode, modeName, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                if (requestedMode != null)
                {
                    LoggerService.Warn($"鏈厤缃竷灞€妯″紡 {requestedMode}锛屼繚鎸佸綋鍓嶅竷灞€涓嶅彉");
                    return;
                }

                LoggerService.Warn($"鏈壘鍒板惎鍔ㄩ粯璁ゆā寮?{_config.DefaultMode}锛屽洖閫€鍒扮涓€涓竷灞€ {_config.Modes[0].Mode}");
                group = _config.Modes[0];
            }

            var items = group.Views;
            _currentModeName = group.Mode;

            if (items == null || items.Count == 0)
            {
                ClearCameraViews();
                LoggerService.Info($"妯″紡 {group.Mode} 娌℃湁閰嶇疆瑙嗗浘锛屾竻绌烘樉绀哄唴瀹?);
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
            LoggerService.Info($"鎸夌獥鍙ｅ綋鍓?DPI 閲嶆柊甯冨眬锛歿dpi.PixelsPerInchX}x{dpi.PixelsPerInchY}");

            if (items.Count == 1)
            {
                // 1 涓獀iew锛氬眳涓樉绀?
                var item = items[0];
                var width = item.W / dpi.DpiScaleX;
                var height = item.H / dpi.DpiScaleY;
                var x = (canvasW - width) / 2.0;
                var y = (canvasH - height) / 2.0;
                CreateCameraView(item, x, y, width, height, 0);
                LoggerService.Info($"鍗曡鍥惧竷灞€锛氬眳涓樉绀簕item.CameraId} ({item.W}x{item.H})");
            }
            else
            {
                // 2涓獀iew锛氬乏鍙虫帓鍒楋紙鏆傛椂蹇界暐鏇村 view锛屼粎鍙栧墠 2 涓級
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

                LoggerService.Info($"鍙岃鍥惧竷灞€锛氬乏鍙虫帓鍒梉{item0.CameraId}({item0.W}x{item0.H})] + [{item1.CameraId}({item1.W}x{item1.H})]");
                if (items.Count > 2)
                {
                    LoggerService.Warn($"瑙嗗浘鏁伴噺 {items.Count} 瓒呰繃 2锛屾殏鏃朵粎鏄剧ず鍓?2 涓?);
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
            LoggerService.Info($"鍒涘缓鎽勫儚澶存樉绀虹獥鍙ｏ細CameraId={item.CameraId}, X={x}, Y={y}, W={w}, H={h}");
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
        /// 浠庨厤缃枃浠跺簲鐢ㄧ獥鍙ｅ昂瀵?
        /// 濡傛灉瀹藉害鎴栭珮搴︿负 0锛屽垯鎾戞弧鍏ㄥ睆
        /// </summary>
        private void ApplyWindowSizeFromConfig()
        {
            int width = _config.WindowWidth;
            int height = _config.WindowHeight;

            // 浣跨敤 Dispatcher 纭繚绐楀彛宸插畬鍏ㄥ垵濮嬪寲鍚庡啀璁剧疆灏哄
            Dispatcher.Invoke(() =>
            {
                if (width == 0 || height == 0)
                {
                    // 鎾戞弧鍏ㄥ睆 - 浣跨敤 Maximized 鐘舵€?
                    this.WindowState = WindowState.Maximized;
                    // 纭繚绐椾綋濉厖鏁翠釜灞忓箷
                    this.Width = SystemParameters.FullPrimaryScreenWidth;
                    this.Height = SystemParameters.FullPrimaryScreenHeight;
                }
                else
                {
                    this.Width = width;
                    this.Height = height;
                    this.WindowState = WindowState.Normal;
                }
            });
        }

        /// <summary>
        /// 姣忔鍚姩鑷姩鎵弿鏈湴鎽勫儚澶达紙DirectShow锛夛紝骞惰鐩栭厤缃腑鐨勬憚鍍忓ご娓呭崟
        /// </summary>
        private void AutoDiscoverLocalCameras()
        {
            LoggerService.Info("寮€濮嬭嚜鍔ㄦ壂鎻忔湰鍦版憚鍍忓ご锛圖irectShow锛?..");

            var discovered = new List<CameraSource>();

            try
            {
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                for (int i = 0; i < videoDevices.Count; i++)
                {
                    try
                    {
                        var device = new VideoCaptureDevice(videoDevices[i].MonikerString);
                        LoggerService.Info($"鍙戠幇鏈湴鎽勫儚澶达細绱㈠紩 {i}, 鍚嶇О {videoDevices[i].Name}");

                        // 璇诲彇鎽勫儚澶存敮鎸佺殑棣栦釜鍒嗚鲸鐜囷紝鍐欏叆閰嶇疆
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
                        catch { /* 璇诲彇鍒嗚鲸鐜囧け璐ヤ笉褰卞搷鎽勫儚澶翠娇鐢?*/ }

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
                        LoggerService.Warn($"鏃犳硶鎵撳紑鎽勫儚澶寸储寮?{i} 杩涜娴嬭瘯锛歿ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("DirectShow 鎽勫儚澶存灇涓惧け璐?, ex);
                return;
            }

            _config.CameraSources = discovered;
            UpdateViewCameraAssignments(discovered);

            if (_configService.Save(_config))
            {
                LoggerService.Info($"鑷姩鎵弿瀹屾垚锛屽叡鍙戠幇 {discovered.Count} 涓湰鍦版憚鍍忓ご锛屽凡鏇存柊骞朵繚瀛樻憚鍍忓ご娓呭崟鍜岃鍥惧叧鑱?);
            }
            else
            {
                LoggerService.Error("鑷姩淇濆瓨鎽勫儚澶存竻鍗曞埌 config.json 澶辫触");
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
