using System;
using System.Collections.Generic;
using System.IO;
using MediaShow.Models;

namespace MediaShow.Services
{
    /// <summary>
    /// 视频播放列表：递归扫描视频源目录，负责按顺序（或随机顺序）推进播放。
    /// 仅在 UI 线程使用，内部不加锁。
    /// </summary>
    public class VideoPlaylistService : MediaPlaylistService
    {
        private VideoPlaylistService(List<string> files, bool loop, bool shuffle)
            : base(files, loop, shuffle)
        {
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
            CollectFiles(folder, extensions, files, "视频");

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
    }
}
