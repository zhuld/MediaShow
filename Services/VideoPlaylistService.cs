using System;
using System.Collections.Generic;
using System.IO;
using MediaShow.Models;

namespace MediaShow.Services
{
    /// <summary>
    /// 视频播放列表：递归扫描视频源目录，负责按顺序（或随机顺序）推进播放，
    /// 并记录无法播放的文件以跳过。仅在 UI 线程使用，内部不加锁。
    /// </summary>
    public class VideoPlaylistService
    {
        private const int MaxScanDepth = 16;

        private readonly List<string> _files;
        private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
        private int _index = -1;

        public bool Loop { get; }
        public bool Shuffle { get; }
        public int Count => _files.Count;

        /// <summary>当前应播放的文件；列表为空或尚未开始时为 null</summary>
        public string? CurrentFile => _index >= 0 && _index < _files.Count ? _files[_index] : null;

        private VideoPlaylistService(List<string> files, bool loop, bool shuffle)
        {
            _files = files;
            Loop = loop;
            Shuffle = shuffle;
        }

        /// <summary>
        /// 根据视频源配置构建播放列表。配置无效或目录中没有匹配文件时返回 null，并通过 error 返回原因。
        /// </summary>
        public static VideoPlaylistService? Create(VideoSource? source, out string? error)
        {
            if (source == null)
            {
                error = "未配置视频源";
                return null;
            }

            var folder = (source.FolderPath ?? string.Empty).Trim();
            if (folder.Length == 0)
            {
                error = "视频源未配置目录";
                return null;
            }

            if (!Directory.Exists(folder))
            {
                error = $"目录不存在：{folder}";
                return null;
            }

            var extensions = ParseExtensions(source.Extensions);
            if (extensions.Count == 0)
            {
                error = "视频源未配置扩展名";
                return null;
            }

            var files = new List<string>();
            CollectFiles(folder, extensions, files, 0);

            if (files.Count == 0)
            {
                error = $"目录中没有匹配的视频文件：{folder}";
                return null;
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (source.Shuffle)
            {
                ShuffleFiles(files);
            }

            LoggerService.Info($"视频源 {source.Id}（{folder}）已加载 {files.Count} 个视频文件，循环={source.Loop}，随机={source.Shuffle}");
            error = null;
            return new VideoPlaylistService(files, source.Loop, source.Shuffle);
        }

        /// <summary>
        /// 前进到下一个可播放文件；到达末尾且不循环、或所有文件都失败时返回 false。
        /// </summary>
        public bool MoveNext()
        {
            for (var step = 0; step < _files.Count; step++)
            {
                if (!Advance()) return false;
                if (!_failed.Contains(_files[_index])) return true;
            }

            return false;
        }

        /// <summary>把当前文件标记为无法播放，后续 MoveNext 会跳过它</summary>
        public void MarkCurrentFailed()
        {
            var file = CurrentFile;
            if (file != null)
            {
                _failed.Add(file);
            }
        }

        private bool Advance()
        {
            if (_files.Count == 0) return false;

            if (_index < 0)
            {
                _index = 0;
                return true;
            }

            if (_index + 1 < _files.Count)
            {
                _index++;
                return true;
            }

            if (!Loop) return false;

            _index = 0;
            return true;
        }

        private static HashSet<string> ParseExtensions(string? raw)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0) return result;

            foreach (var part in text.Split(new[] { ';', ',', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var extension = part.Trim();
                if (extension.Length == 0) continue;

                if (!extension.StartsWith(".", StringComparison.Ordinal))
                {
                    extension = "." + extension;
                }

                result.Add(extension);
            }

            return result;
        }

        private static void CollectFiles(string folder, HashSet<string> extensions, List<string> files, int depth)
        {
            if (depth > MaxScanDepth)
            {
                LoggerService.Warn($"视频目录扫描深度超过 {MaxScanDepth} 层，已跳过：{folder}");
                return;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (extensions.Contains(Path.GetExtension(file)))
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"读取视频目录失败：{folder}｜{ex.Message}");
            }

            try
            {
                foreach (var subFolder in Directory.EnumerateDirectories(folder))
                {
                    CollectFiles(subFolder, extensions, files, depth + 1);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"读取视频子目录失败：{folder}｜{ex.Message}");
            }
        }

        private static void ShuffleFiles(List<string> files)
        {
            var random = new Random();
            for (var i = files.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (files[i], files[j]) = (files[j], files[i]);
            }
        }
    }
}
