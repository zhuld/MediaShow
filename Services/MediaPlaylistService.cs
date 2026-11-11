using System;
using System.Collections.Generic;
using System.IO;

namespace MediaShow.Services
{
    /// <summary>
    /// 媒体播放列表基类：保存目录扫描得到的文件清单，负责按顺序（或随机顺序）推进播放，
    /// 并记录无法播放的文件以跳过。仅在 UI 线程使用，内部不加锁。
    /// </summary>
    public abstract class MediaPlaylistService
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

        protected MediaPlaylistService(List<string> files, bool loop, bool shuffle)
        {
            _files = files;
            Loop = loop;
            Shuffle = shuffle;
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

        protected static HashSet<string> ParseExtensions(string? raw)
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

        /// <param name="kind">用于日志的来源类型描述，例如“视频”“图片”</param>
        protected static void CollectFiles(string folder, HashSet<string> extensions, List<string> files, string kind)
        {
            CollectFiles(folder, extensions, files, kind, 0);
        }

        private static void CollectFiles(string folder, HashSet<string> extensions, List<string> files, string kind, int depth)
        {
            if (depth > MaxScanDepth)
            {
                LoggerService.Warn($"{kind}目录扫描深度超过 {MaxScanDepth} 层，已跳过：{folder}");
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
                LoggerService.Warn($"读取{kind}目录失败：{folder}｜{ex.Message}");
            }

            try
            {
                foreach (var subFolder in Directory.EnumerateDirectories(folder))
                {
                    CollectFiles(subFolder, extensions, files, kind, depth + 1);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"读取{kind}子目录失败：{folder}｜{ex.Message}");
            }
        }

        protected static void ShuffleFiles(List<string> files)
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
