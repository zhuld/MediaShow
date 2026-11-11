using System;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MediaShow.Services
{
    /// <summary>
    /// 视频音轨播放：使用 Media Foundation 解码音频并通过 WaveOut 输出（不依赖 Windows Media Player）。
    /// 音量按视图配置的 0～100 百分比换算；0 表示不创建音频输出（静音）。
    /// </summary>
    public sealed class VideoAudioPlayer : IDisposable
    {
        private static readonly object StartupLock = new();
        private static bool _mediaFoundationStarted;

        private readonly MediaFoundationReader _reader;
        private readonly IWavePlayer _output;
        private bool _disposed;

        private VideoAudioPlayer(MediaFoundationReader reader, IWavePlayer output)
        {
            _reader = reader;
            _output = output;
        }

        /// <summary>
        /// 为指定视频文件创建音频播放器。文件没有音轨或初始化失败时返回 null（视频继续静音播放）。
        /// </summary>
        public static VideoAudioPlayer? TryCreate(string path, double volumePercent)
        {
            var volume = Math.Max(0, Math.Min(100, volumePercent));
            if (volume <= 0) return null;

            MediaFoundationReader? reader = null;
            IWavePlayer? output = null;

            try
            {
                EnsureMediaFoundation();
                reader = new MediaFoundationReader(path);

                output = new WaveOutEvent
                {
                    DesiredLatency = 200,
                    NumberOfBuffers = 3
                };
                output.Init(reader);

                try
                {
                    output.Volume = (float)(volume / 100.0);
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"设置视频音量失败（将使用系统音量）：{ex.Message}");
                }

                LoggerService.Info(
                    $"视频音频就绪：{Path.GetFileName(path)} {reader.WaveFormat.SampleRate}Hz " +
                    $"{reader.WaveFormat.Channels}ch 音量={volume:0}%");
                return new VideoAudioPlayer(reader, output);
            }
            catch (Exception ex)
            {
                LoggerService.Info($"视频没有可播放的音轨或音频初始化失败，将静音播放：{Path.GetFileName(path)}｜{ex.Message}");
                try { output?.Dispose(); } catch { /* ignore */ }
                try { reader?.Dispose(); } catch { /* ignore */ }
                return null;
            }
        }

        public void Play()
        {
            if (_disposed) return;

            try
            {
                _output.Play();
                LoggerService.Info($"视频音频开始输出：状态={_output.PlaybackState}");
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"视频音频播放失败：{ex.Message}");
            }
        }

        private static void EnsureMediaFoundation()
        {
            lock (StartupLock)
            {
                if (_mediaFoundationStarted) return;

                MediaFoundationApi.Startup();
                _mediaFoundationStarted = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _output.Stop();
            }
            catch { /* ignore */ }

            try { _output.Dispose(); } catch { /* ignore */ }
            try { _reader.Dispose(); } catch { /* ignore */ }
            GC.SuppressFinalize(this);
        }
    }
}
