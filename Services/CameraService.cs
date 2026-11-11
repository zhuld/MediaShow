using System;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaShow.Models;
using AForge.Video;
using AForge.Video.DirectShow;

namespace MediaShow.Services
{
    /// <summary>
    /// 摄像头采集服务：基于 AForge.Video.DirectShow，每个摄像头独立线程读取帧，
    /// 通过事件推送 BitmapSource 到 UI 线程。
    /// </summary>
    public class CameraService : IDisposable
    {
        private readonly string _cameraId;
        private readonly VideoCaptureDevice _capture;
        private readonly Thread _thread;
        private volatile bool _running;
        private readonly ManualResetEvent _stopEvent = new(false);

        public event Action<BitmapSource>? FrameReady;
        public event Action<string>? ErrorOccurred;

        public bool IsRunning => _running;

        public CameraService(CameraSource source)
        {
            _cameraId = source.Id;

            try
            {
                LoggerService.Info($"正在打开摄像头：{source.Name} (Index={source.Index})");

                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                if (source.Index < 0 || source.Index >= videoDevices.Count)
                {
                    throw new Exception($"摄像头索引 {source.Index} 超出范围，可用设备数：{videoDevices.Count}");
                }

                _capture = new VideoCaptureDevice(videoDevices[source.Index].MonikerString);
                _capture.NewFrame += Capture_NewFrame;

                // 读取摄像头支持的分辨率，取首个（通常为默认/最大）写入配置，供 config.json 持久化
                try
                {
                    var caps = _capture.VideoCapabilities;
                    if (caps != null && caps.Length > 0 && source.Width == 0 && source.Height == 0)
                    {
                        var size = caps[0].FrameSize;
                        source.Width = size.Width;
                        source.Height = size.Height;
                        LoggerService.Info($"摄像头 {source.Name} 默认分辨率：{source.Width}x{source.Height}");
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"读取摄像头 {source.Name} 分辨率失败：{ex.Message}");
                }

                LoggerService.Info($"摄像头打开成功：{source.Name} (Index={source.Index}, {source.Width}x{source.Height})");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"摄像头初始化失败：{source.Name}", ex);
                ErrorOccurred?.Invoke(ex.Message);
                throw;
            }

            _thread = new Thread(RunLoop) { IsBackground = true, Name = $"Camera-{source.Id}" };
        }

        /// <summary>
        /// AForge 的 NewFrame 事件回调：每帧到达时转换为 WPF BitmapSource 并推送。
        /// </summary>
        private void Capture_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!_running) return;

            try
            {
                using var bitmap = (System.Drawing.Bitmap)eventArgs.Frame.Clone();
                var bmpSrc = BitmapToBitmapSource(bitmap);
                FrameReady?.Invoke(bmpSrc);
            }
            catch (Exception ex)
            {
                LoggerService.Error($"摄像头 {_cameraId} 处理帧失败", ex);
                ErrorOccurred?.Invoke($"处理帧失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 采集线程：启动 AForge 设备，然后等待停止信号。
        /// </summary>
        private void RunLoop()
        {
            try
            {
                _capture.Start();
                LoggerService.Info($"摄像头采集线程已启动：{_cameraId}");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"摄像头 {_cameraId} 启动采集失败", ex);
                ErrorOccurred?.Invoke($"启动采集失败：{ex.Message}");
                return;
            }

            _stopEvent.WaitOne();

            try
            {
                _capture.SignalToStop();
            }
            catch { /* ignore */ }

            try
            {
                _capture.Stop();
            }
            catch { /* ignore */ }

            LoggerService.Info($"摄像头采集线程已停止：{_cameraId}");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _stopEvent.Reset();
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running && !_thread.IsAlive) return;
            _running = false;
            _stopEvent.Set();
            if (_thread.IsAlive)
            {
                _thread.Join();
            }
        }

        /// <summary>
        /// 将 System.Drawing.Bitmap 转换为 WPF BitmapSource（可跨线程共享）。
        /// </summary>
        private static BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {
                // AForge DirectShow 通常输出 RGB24，WPF 使用 Bgr24。若颜色异常，可尝试 PixelFormats.Rgb24。
                var bitmapSource = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null,
                    bmpData.Scan0,
                    bmpData.Stride * bitmap.Height,
                    bmpData.Stride);

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        public void Dispose()
        {
            Stop();
            _capture.NewFrame -= Capture_NewFrame;
            _stopEvent?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}