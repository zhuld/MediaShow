using System;
using System.Collections.Generic;
using System.IO;
using MediaShow.Models;

namespace MediaShow.Services
{
    /// <summary>
    /// 图片播放列表：递归扫描图片源目录，负责按顺序（或随机顺序）推进轮播。
    /// 仅在 UI 线程使用，内部不加锁。
    /// </summary>
    public class ImagePlaylistService : MediaPlaylistService
    {
        private ImagePlaylistService(List<string> files, bool loop, bool shuffle)
            : base(files, loop, shuffle)
        {
        }

        /// <summary>
        /// 根据图片源配置构建播放列表。配置无效或目录中没有匹配文件时返回 null，并通过 error 返回原因。
        /// </summary>
        public static ImagePlaylistService? Create(ImageSource? source, out string? error)
        {
            if (source == null)
            {
                error = "未配置图片源";
                return null;
            }

            var folder = (source.FolderPath ?? string.Empty).Trim();
            if (folder.Length == 0)
            {
                error = "图片源未配置目录";
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
                error = "图片源未配置扩展名";
                return null;
            }

            var files = new List<string>();
            CollectFiles(folder, extensions, files, "图片");

            if (files.Count == 0)
            {
                error = $"目录中没有匹配的图片文件：{folder}";
                return null;
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (source.Shuffle)
            {
                ShuffleFiles(files);
            }

            LoggerService.Info($"图片源 {source.Id}（{folder}）已加载 {files.Count} 个图片文件，循环={source.Loop}，随机={source.Shuffle}");
            error = null;
            return new ImagePlaylistService(files, source.Loop, source.Shuffle);
        }
    }
}
