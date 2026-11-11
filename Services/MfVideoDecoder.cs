using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaShow.Services
{
    /// <summary>
    /// 基于 Media Foundation SourceReader 的视频解码器：后台线程逐帧解码为 RGB32，
    /// 按时间戳控制播放节奏，并以冻结的 <see cref="BitmapSource"/> 推送到 UI。
    /// 不依赖 Windows Media Player，静音播放（不处理音轨）。
    /// </summary>
    public sealed class MfVideoDecoder : IDisposable
    {
        private const int JoinTimeoutMs = 3000;

        private readonly string _path;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stopEvent = new(false);
        private volatile bool _started;
        private volatile bool _disposed;

        /// <summary>每帧解码完成（在解码线程触发）</summary>
        public event Action<BitmapSource>? FrameReady;

        /// <summary>解码失败（在解码线程触发），参数为可直接展示的原因</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>正常播放到文件结尾（在解码线程触发）</summary>
        public event Action? PlaybackEnded;

        public MfVideoDecoder(string path)
        {
            _path = path;
            _thread = new Thread(Run) { IsBackground = true, Name = "Video-" + Path.GetFileName(path) };
        }

        public void Start()
        {
            if (_started || _disposed) return;

            _started = true;
            _thread.Start();
        }

        private void Run()
        {
            var started = false;
            IMFSourceReader? reader = null;
            IMFMediaType? outputType = null;
            IMFMediaType? currentType = null;

            try
            {
                LoggerService.Info($"视频解码线程启动：{Path.GetFileName(_path)}");
                var hr = MfInterop.MFStartup(MfInterop.MF_VERSION, MfInterop.MFSTARTUP_LITE);
                if (hr < 0)
                {
                    Fail($"Media Foundation 初始化失败：{MfInterop.DescribeResult(hr)}");
                    return;
                }

                started = true;
                reader = CreateReader();
                if (reader == null) return;

                outputType = CreateRgb32Type();
                if (outputType == null) return;

                hr = reader.SetCurrentMediaType(MfInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, outputType);
                if (hr < 0)
                {
                    Fail($"无法解码该视频（系统可能缺少对应解码器）：{MfInterop.DescribeResult(hr)}");
                    return;
                }

                hr = reader.GetCurrentMediaType(MfInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out currentType);
                if (hr < 0)
                {
                    Fail($"读取视频格式失败：{MfInterop.DescribeResult(hr)}");
                    return;
                }

                long packedSize;
                var sizeKey = MfInterop.MF_MT_FRAME_SIZE;
                hr = currentType.GetUINT64(ref sizeKey, out packedSize);
                if (hr < 0)
                {
                    Fail($"读取视频尺寸失败：{MfInterop.DescribeResult(hr)}");
                    return;
                }

                var width = (int)(packedSize >> 32);
                var height = (int)(packedSize & 0xFFFFFFFF);
                if (width <= 0 || height <= 0)
                {
                    Fail("视频尺寸无效");
                    return;
                }

                LoggerService.Info($"视频解码就绪：{Path.GetFileName(_path)} {width}x{height}");
                DecodeLoop(reader, width, height);
            }
            catch (Exception ex)
            {
                Fail($"视频解码异常：{ex.Message}");
                LoggerService.Error($"视频解码失败：{_path}", ex);
            }
            finally
            {
                ReleaseComObject(currentType);
                ReleaseComObject(outputType);
                ReleaseComObject(reader);

                if (started)
                {
                    try { MfInterop.MFShutdown(); } catch { /* ignore */ }
                }
            }
        }

        private IMFSourceReader? CreateReader()
        {
            IntPtr attributesPtr = IntPtr.Zero;
            IMFAttributes? attributes = null;

            try
            {
                var hr = MfInterop.MFCreateAttributes(out attributes, 2);
                if (hr < 0)
                {
                    Fail($"创建解码参数失败：{MfInterop.DescribeResult(hr)}");
                    return null;
                }

                var key = MfInterop.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING;
                attributes.SetUINT32(ref key, 1);
                attributesPtr = Marshal.GetComInterfaceForObject(attributes, typeof(IMFAttributes));

                hr = MfInterop.MFCreateSourceReaderFromURL(_path, attributesPtr, out var reader);
                if (hr < 0)
                {
                    Fail($"无法打开视频文件：{MfInterop.DescribeResult(hr)}");
                    return null;
                }

                // 只解码视频，忽略音轨（本程序静音播放）
                reader.SetStreamSelection(MfInterop.MF_SOURCE_READER_ALL_STREAMS, false);
                reader.SetStreamSelection(MfInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
                return reader;
            }
            finally
            {
                if (attributesPtr != IntPtr.Zero)
                {
                    Marshal.Release(attributesPtr);
                }

                ReleaseComObject(attributes);
            }
        }

        private IMFMediaType? CreateRgb32Type()
        {
            var hr = MfInterop.MFCreateMediaType(out var mediaType);
            if (hr < 0)
            {
                Fail($"创建视频输出格式失败：{MfInterop.DescribeResult(hr)}");
                return null;
            }

            var majorKey = MfInterop.MF_MT_MAJOR_TYPE;
            var major = MfInterop.MFMediaType_Video;
            hr = mediaType.SetGUID(ref majorKey, ref major);
            if (hr >= 0)
            {
                var subtypeKey = MfInterop.MF_MT_SUBTYPE;
                var subtype = MfInterop.MFVideoFormat_RGB32;
                hr = mediaType.SetGUID(ref subtypeKey, ref subtype);
            }

            if (hr < 0)
            {
                Fail($"设置视频输出格式失败：{MfInterop.DescribeResult(hr)}");
                ReleaseComObject(mediaType);
                return null;
            }

            return mediaType;
        }

        private void DecodeLoop(IMFSourceReader reader, int width, int height)
        {
            var clock = Stopwatch.StartNew();
            long firstTimestamp = -1;

            while (!_stopEvent.IsSet)
            {
                var hr = reader.ReadSample(
                    MfInterop.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                    0,
                    out _,
                    out var flags,
                    out var timestamp,
                    out var sample);

                if (hr < 0)
                {
                    // 部分媒体源在文件结尾直接返回 MF_E_END_OF_STREAM，而非结束标志
                    if (hr == MfInterop.MF_E_END_OF_STREAM)
                    {
                        PlaybackEnded?.Invoke();
                        return;
                    }

                    if (!_stopEvent.IsSet)
                    {
                        Fail($"读取视频帧失败：{MfInterop.DescribeResult(hr)}");
                    }

                    return;
                }

                if ((flags & MfInterop.MF_SOURCE_READERF_ERROR) != 0)
                {
                    Fail("读取视频帧时发生错误（文件可能损坏或编码不受支持）");
                    return;
                }

                if ((flags & MfInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    PlaybackEnded?.Invoke();
                    return;
                }

                if (sample == null) continue;

                try
                {
                    if (firstTimestamp < 0)
                    {
                        firstTimestamp = timestamp;
                        // 以第一帧为基准重新计时，避免解码启动耗时导致随后大量丢帧
                        clock.Restart();
                    }

                    var dueMs = (timestamp - firstTimestamp) / 10000.0;
                    var waitMs = dueMs - clock.Elapsed.TotalMilliseconds;

                    if (waitMs > 1)
                    {
                        if (_stopEvent.Wait(TimeSpan.FromMilliseconds(waitMs))) return;
                    }

                    // 解码或界面处理落后过多时丢帧，避免画面越播越晚
                    if (clock.Elapsed.TotalMilliseconds - dueMs > 500)
                    {
                        continue;
                    }

                    var bitmap = CreateBitmap(sample, width, height);
                    if (bitmap == null)
                    {
                        if (!_loggedFrameFailure)
                        {
                            _loggedFrameFailure = true;
                            LoggerService.Warn("创建视频帧位图失败（缓冲区格式不受支持）");
                        }
                    }
                    else if (!_stopEvent.IsSet)
                    {
                        FrameReady?.Invoke(bitmap);
                    }
                }
                finally
                {
                    ReleaseComObject(sample);
                }
            }
        }

        private static BitmapSource? CreateBitmap(IMFSample sample, int width, int height)
        {
            var hr = sample.ConvertToContiguousBuffer(out var buffer);
            if (hr < 0 || buffer == null)
            {
                hr = sample.GetBufferByIndex(0, out buffer);
                if (hr < 0 || buffer == null) return null;
            }

            try
            {
                if (buffer is IMF2DBuffer)
                {
                    var buffer2D = (IMF2DBuffer)buffer;
                    hr = buffer2D.Lock2D(out var scanline0, out var pitch);
                    if (hr >= 0)
                    {
                        try
                        {
                            var result = CreateBitmapFromScanlines(scanline0, pitch == 0 ? width * 4 : pitch, width, height);
                            if (result != null) return result;
                        }
                        finally
                        {
                            buffer2D.Unlock2D();
                        }
                    }
                }

                hr = buffer.Lock(out var data, out _, out var length);
                if (hr < 0) return null;

                try
                {
                    return CreateBitmapFromScanlines(data, width * 4, width, height, length);
                }
                finally
                {
                    buffer.Unlock();
                }
            }
            finally
            {
                ReleaseComObject(buffer);
            }
        }

        private bool _loggedFrameFailure;

        private static BitmapSource? CreateBitmapFromScanlines(IntPtr scanline0, int pitch, int width, int height, int availableBytes = 0)
        {
            var rowBytes = width * 4;
            if (pitch == 0 || rowBytes <= 0 || height <= 0) return null;

            var stride = Math.Abs(pitch);
            if (stride < rowBytes) return null;

            var totalBytes = stride * height;
            if (availableBytes > 0 && availableBytes < totalBytes) return null;

            try
            {
                if (pitch > 0)
                {
                    // 正向 pitch：数据已经是自上而下排列，可直接交给 WPF（内部会复制）
                    var direct = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, scanline0, totalBytes, stride);
                    direct.Freeze();
                    return direct;
                }

                // 负 pitch：行序自下而上，需要逐行翻转
                var data = new byte[rowBytes * height];
                for (var y = 0; y < height; y++)
                {
                    var sourceRow = height - 1 - y;
                    Marshal.Copy(IntPtr.Add(scanline0, sourceRow * stride), data, y * rowBytes, rowBytes);
                }

                var flipped = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, rowBytes);
                flipped.Freeze();
                return flipped;
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"创建视频帧位图失败：{ex.Message}");
                return null;
            }
        }

        private void Fail(string message)
        {
            LoggerService.Warn($"视频解码：{message}（{Path.GetFileName(_path)}）");
            ErrorOccurred?.Invoke(message);
        }

        public void Stop()
        {
            _stopEvent.Set();

            if (_started && _thread.IsAlive && !_thread.Join(JoinTimeoutMs))
            {
                LoggerService.Warn($"视频解码线程未能及时退出：{Path.GetFileName(_path)}");
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value == null) return;

            try
            {
                if (Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"释放视频解码对象失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Stop();

            // 仅在线程确实退出后释放事件，避免线程仍在访问时抛出 ObjectDisposedException
            if (!_thread.IsAlive)
            {
                _stopEvent.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
