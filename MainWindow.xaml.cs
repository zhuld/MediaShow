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
using CameraShow.Models;
using CameraShow.Services;
using AForge.Video.DirectShow;

namespace CameraShow
{
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly AppConfig _config;
        private readonly UdpControlService _udpService;

        private readonly Dictionary<string, CameraService> _cameraServices = new();
        private readonly Dictionary<string, Controls.CameraView> _cameraViews = new();
        private volatile bool _isShuttingDown;
        private bool _allowClose;
        private HwndSource? _windowSource;

        public MainWindow()
        {
            InitializeComponent();

            LoggerService.Info("主窗口构造开始...");

            _configService = new ConfigService("config.json");
            _config = _configService.Load();

            // 每次启动自动扫描并列出本地可用摄像头
            AutoDiscoverLocalCameras();

            foreach (var cam in _config.CameraSources)
            {
                try
                {
                    var svc = new CameraService(cam);
                    svc.ErrorOccurred += err =>
                    {
                        LoggerService.Error($"摄像头{cam.Name} 运行错误：{err}");
                        DispatchToUi(() => UpdateStatus($"摄像头{cam.Name} 错误：{err}"));
                    };
                    _cameraServices[cam.Id] = svc;
                    LoggerService.Info($"摄像头服务已创建：{cam.Id} ({cam.Name}, {cam.Width}x{cam.Height})");
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"摄像头{cam.Name} 创建失败", ex);
                    UpdateStatus($"摄像头{cam.Name} 创建失败：{ex.Message}");
                }
            }

            _udpService = new UdpControlService(_config.PcName, _config.UdpPort);
            _udpService.ModeChanged += mode =>
            {
                DispatchToUi(() =>
                {
                    UpdateStatus($"网络指令：已切换到模式 {mode}");
                    LoggerService.Info($"网络指令切换布局：{mode}");
                });
            };

            try
            {
                _udpService.Start();
                UpdateStatus($"UDP 监听端口 {_config.UdpPort}，计算机名：{_config.PcName}");
            }
            catch (Exception ex)
            {
                LoggerService.Error("UDP 服务启动失败", ex);
                UpdateStatus($"UDP 启动失败：{ex.Message}");
            }

            LoggerService.Info("主窗口构造完成");
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
                        ApplyViewsLayout();
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
                    UpdateStatus($"摄像头{kvp.Key} 启动失败：{ex.Message}");
                }
            }

            UpdateStatus($"布局完成，共 {_cameraViews.Count} 个显示窗口");
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

            e.Cancel = true;
            if (_isShuttingDown) return;

            _isShuttingDown = true;
            IsEnabled = false;
            UpdateStatus("正在停止摄像头和网络服务...");

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

                LoggerService.Info("后台服务清理完成");
            });

            _allowClose = true;
            Close();
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
        private void ApplyViewsLayout()
        {
            if (_config.Modes == null || _config.Modes.Count == 0)
            {
                LoggerService.Warn("config.Modes 为空，未创建任何摄像头显示窗口");
                return;
            }

            var group = _config.Modes.FirstOrDefault(
                mode => string.Equals(mode.Mode, _config.DefaultMode, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                LoggerService.Warn($"未找到启动默认模式 {_config.DefaultMode}，回退到第一个布局 { _config.Modes[0].Mode }");
                group = _config.Modes[0];
            }

            var items = group.Views;

            if (items == null || items.Count == 0)
            {
                LoggerService.Warn($"ViewGroup {group.Id} 中没有子视图");
                return;
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
            if (_cameraViews.TryGetValue(item.Id, out var existingView))
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
                ViewId = item.Id,
                CameraId = item.CameraId,
                Width = w,
                Height = h,
                Tag = item.Id
            };
            Canvas.SetLeft(camView, x);
            Canvas.SetTop(camView, y);
            Canvas.SetZIndex(camView, zIndex);

            if (_cameraServices.TryGetValue(item.CameraId, out var svc))
            {
                svc.FrameReady += bmp =>
                {
                    DispatchToUi(() => camView.SetImage(bmp));
                };
            }

            _cameraViews[item.Id] = camView;
            LayoutCanvas.Children.Add(camView);
            LoggerService.Info($"创建摄像头显示窗口：ViewId={item.Id}, CameraId={item.CameraId}, X={x}, Y={y}, W={w}, H={h}");
            return camView;
        }

        private void UpdateStatus(string msg)
        {
            StatusText.Text = msg;
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
                    if (string.IsNullOrWhiteSpace(view.Id))
                    {
                        view.Id = $"view_{i}";
                    }

                    if (string.IsNullOrWhiteSpace(view.CameraId) || !cameraIds.Contains(view.CameraId))
                    {
                        view.CameraId = i < cameras.Count ? cameras[i].Id : string.Empty;
                    }
                }
            }
        }
    }
}
