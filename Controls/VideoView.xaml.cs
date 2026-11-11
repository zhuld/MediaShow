using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaShow.Models;
using MediaShow.Services;

namespace MediaShow.Controls
{
    /// <summary>
    /// 单个视频源显示控件：按播放列表循环播放视频源目录中的视频文件。
    /// 帧由 <see cref="MfVideoDecoder"/>（Media Foundation）解码为位图后在此显示，
    /// 不依赖 Windows Media Player，静音播放。
    /// </summary>
    public partial class VideoView : Grid, IDisposable
    {
        private VideoPlaylistService? _playlist;
        private VideoSource? _videoSource;
        private MfVideoDecoder? _decoder;
        private string? _currentFile;
        private int _framePending;
        private int _framesShown;
        private bool _disposed;

        public string VideoId { get; set; } = string.Empty;

        public VideoView()
        {
            InitializeComponent();
        }

        /// <summary>设置该视图播放的视频源，需在 Play 之前调用</summary>
        public void Configure(VideoSource? videoSource, string videoId)
        {
            _videoSource = videoSource;
            VideoId = videoId ?? string.Empty;
        }

        /// <summary>开始播放；控件尚未加载完成时会等到加载后再开始</summary>
        public void Play()
        {
            if (_disposed) return;

            if (!IsLoaded)
            {
                Loaded -= VideoView_Loaded;
                Loaded += VideoView_Loaded;
                return;
            }

            StartPlayback();
        }

        /// <summary>停止播放并释放解码线程</summary>
        public void Stop()
        {
            StopDecoder();
            _currentFile = null;
            PART_Image.Source = null;
        }

        private void VideoView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= VideoView_Loaded;
            if (!_disposed)
            {
                StartPlayback();
            }
        }

        private void VideoView_Unloaded(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void StartPlayback()
        {
            if (_disposed) return;

            if (_playlist == null)
            {
                _playlist = VideoPlaylistService.Create(_videoSource, out var error);
                if (_playlist == null)
                {
                    var message = error ?? "无法加载视频源";
                    LoggerService.Warn($"视频视图 {VideoId} 无法播放：{message}");
                    ShowMessage(message);
                    return;
                }
            }

            if (!_playlist.MoveNext())
            {
                ShowMessage("没有可播放的视频文件");
                return;
            }

            PlayCurrent();
        }

        private void PlayCurrent()
        {
            var file = _playlist?.CurrentFile;
            if (file == null)
            {
                ShowMessage("没有可播放的视频文件");
                return;
            }

            StopDecoder();

            var decoder = new MfVideoDecoder(file);
            decoder.FrameReady += OnFrameReady;
            decoder.ErrorOccurred += OnDecoderError;
            decoder.PlaybackEnded += OnPlaybackEnded;

            _decoder = decoder;
            _currentFile = file;
            _framesShown = 0;
            HideMessage();
            LoggerService.Info($"视频视图 {VideoId} 开始播放：{Path.GetFileName(file)}");

            try
            {
                decoder.Start();
            }
            catch (Exception ex)
            {
                LoggerService.Error($"视频视图 {VideoId} 启动解码失败：{file}", ex);
                HandlePlaybackFailure(file, ex.Message);
            }
        }

        /// <summary>解码线程每解码一帧触发；界面处理不过来时丢弃多余帧</summary>
        private void OnFrameReady(BitmapSource frame)
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _framePending, 1) == 1) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                Interlocked.Exchange(ref _framePending, 0);
                if (!_disposed)
                {
                    PART_Image.Source = frame;
                    if (_framesShown++ == 0)
                    {
                        LoggerService.Info($"视频视图 {VideoId} 已显示首帧：{Path.GetFileName(_currentFile ?? string.Empty)}");
                    }
                }
            }));
        }

        private void OnDecoderError(string message)
        {
            if (_disposed) return;

            var file = _currentFile;
            Dispatcher.BeginInvoke(new Action(() => HandlePlaybackFailure(file, message)));
        }

        private void OnPlaybackEnded()
        {
            if (_disposed) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;

                if (_playlist != null && _playlist.MoveNext())
                {
                    PlayCurrent();
                }
                else
                {
                    LoggerService.Info($"视频视图 {VideoId} 播放结束（循环已关闭）");
                }
            }));
        }

        /// <summary>标记当前文件无法播放并尝试下一个；全部失败时在视图中显示原因</summary>
        private void HandlePlaybackFailure(string? file, string reason)
        {
            if (_disposed) return;

            _playlist?.MarkCurrentFailed();
            StopDecoder();

            if (_playlist != null && _playlist.MoveNext())
            {
                PlayCurrent();
                return;
            }

            var message = file == null
                ? $"无法播放视频：{reason}"
                : $"无法播放视频文件（可能缺少解码器）：{Path.GetFileName(file)}\n{reason}";
            ShowMessage(message);
            LoggerService.Warn($"视频视图 {VideoId} 没有可播放的文件，已停止");
        }

        private void StopDecoder()
        {
            var decoder = _decoder;
            _decoder = null;
            if (decoder == null) return;

            decoder.FrameReady -= OnFrameReady;
            decoder.ErrorOccurred -= OnDecoderError;
            decoder.PlaybackEnded -= OnPlaybackEnded;

            try
            {
                decoder.Dispose();
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"释放视频解码器失败：{ex.Message}");
            }

            _currentFile = null;
        }

        private void ShowMessage(string message)
        {
            PART_Message.Text = message;
            PART_Message.Visibility = Visibility.Visible;
        }

        private void HideMessage()
        {
            PART_Message.Visibility = Visibility.Collapsed;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Loaded -= VideoView_Loaded;
            Stop();
            _playlist = null;
            _videoSource = null;
            GC.SuppressFinalize(this);
        }
    }
}
