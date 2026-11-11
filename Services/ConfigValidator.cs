using System;
using System.Collections.Generic;
using System.Linq;
using MediaShow.Models;

namespace MediaShow.Services
{
    /// <summary>
    /// 配置校验与规范化，供设置对话框和网页设置共用，保证两条入口的规则一致。
    /// </summary>
    public static class ConfigValidator
    {
        public const string BlankModeName = "Blank";
        public const string DesktopModeName = "Desktop_Show";

        /// <summary>视图来源类型：摄像头</summary>
        public const string CameraSourceType = "Camera";

        /// <summary>视图来源类型：视频源</summary>
        public const string VideoSourceType = "Video";

        /// <summary>视图来源类型：图片源</summary>
        public const string ImageSourceType = "Image";

        public const string DefaultVideoExtensions = ".mp4;.avi;.wmv";

        public const string DefaultImageExtensions = ".jpg;.jpeg;.png;.bmp;.gif";

        /// <summary>图片翻页间隔（秒）的取值范围，由视图的 Volume 值表示</summary>
        public const int MinImageIntervalSeconds = 1;
        public const int MaxImageIntervalSeconds = 100;

        /// <summary>图片视图的翻页间隔（秒）：复用视图的 Volume 值，并限制在有效范围内</summary>
        public static int ResolveImageIntervalSeconds(ViewItem? view)
        {
            if (view == null) return 10;

            var seconds = (int)Math.Round(view.Volume);
            if (seconds < MinImageIntervalSeconds) return MinImageIntervalSeconds;
            if (seconds > MaxImageIntervalSeconds) return MaxImageIntervalSeconds;
            return seconds;
        }

        /// <summary>视图可选的画面拉伸方式（对应 WPF Stretch 枚举）；留空表示按来源自动选择</summary>
        private static readonly string[] StretchModes = { "Fill", "Uniform", "UniformToFill", "None" };

        /// <summary>规范化拉伸方式名称：空或无法识别时返回空（自动）</summary>
        public static string NormalizeStretch(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;

            foreach (var mode in StretchModes)
            {
                if (string.Equals(text, mode, StringComparison.OrdinalIgnoreCase))
                {
                    return mode;
                }
            }

            return string.Empty;
        }

        public static bool IsValidStretch(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Length == 0 || string.Equals(text, NormalizeStretch(text), StringComparison.Ordinal);
        }

        /// <summary>判断视图是否播放视频源</summary>
        public static bool IsVideoView(ViewItem? view)
        {
            return view != null && string.Equals(view.SourceType, VideoSourceType, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>判断视图是否轮播图片源</summary>
        public static bool IsImageView(ViewItem? view)
        {
            return view != null && string.Equals(view.SourceType, ImageSourceType, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>规范化视图来源类型：无法识别时按摄像头处理</summary>
        public static string NormalizeSourceType(string? value)
        {
            if (string.Equals(value, VideoSourceType, StringComparison.OrdinalIgnoreCase))
            {
                return VideoSourceType;
            }

            if (string.Equals(value, ImageSourceType, StringComparison.OrdinalIgnoreCase))
            {
                return ImageSourceType;
            }

            return CameraSourceType;
        }

        /// <summary>判断是否为内置模式（不依赖 config.json 中的 Modes 定义）</summary>
        public static bool IsBuiltInMode(string? modeName)
        {
            return string.Equals(modeName, BlankModeName, StringComparison.OrdinalIgnoreCase)
                   || IsDesktopMode(modeName);
        }

        public static bool IsDesktopMode(string? modeName)
        {
            return string.Equals(modeName, DesktopModeName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>将配置中的文本字段与集合规范化为可直接保存的状态</summary>
        public static void Normalize(AppConfig config)
        {
            if (config == null) return;

            config.PcName = (config.PcName ?? string.Empty).Trim();
            config.DefaultMode = (config.DefaultMode ?? string.Empty).Trim();
            config.CameraSources ??= new List<CameraSource>();
            config.VideoSources ??= new List<VideoSource>();
            config.ImageSources ??= new List<ImageSource>();
            config.Modes ??= new List<ViewGroup>();

            foreach (var video in config.VideoSources)
            {
                if (video == null) continue;

                video.Id = (video.Id ?? string.Empty).Trim();
                video.Name = (video.Name ?? string.Empty).Trim();
                video.FolderPath = (video.FolderPath ?? string.Empty).Trim();
                video.Extensions = (video.Extensions ?? string.Empty).Trim();
                if (video.Extensions.Length == 0)
                {
                    video.Extensions = DefaultVideoExtensions;
                }
            }

            foreach (var image in config.ImageSources)
            {
                if (image == null) continue;

                image.Id = (image.Id ?? string.Empty).Trim();
                image.Name = (image.Name ?? string.Empty).Trim();
                image.FolderPath = (image.FolderPath ?? string.Empty).Trim();
                image.Extensions = (image.Extensions ?? string.Empty).Trim();
                if (image.Extensions.Length == 0)
                {
                    image.Extensions = DefaultImageExtensions;
                }
            }

            foreach (var mode in config.Modes)
            {
                if (mode == null) continue;

                mode.Mode = (mode.Mode ?? string.Empty).Trim();
                mode.Views ??= new List<ViewItem>();

                foreach (var view in mode.Views)
                {
                    if (view == null) continue;

                    view.CameraId = (view.CameraId ?? string.Empty).Trim();
                    view.VideoId = (view.VideoId ?? string.Empty).Trim();
                    view.ImageId = (view.ImageId ?? string.Empty).Trim();
                    view.SourceType = NormalizeSourceType(view.SourceType);
                    view.Stretch = NormalizeStretch(view.Stretch);

                    // 图片视图用 Volume 保存翻页间隔（秒），因此下限是 1
                    if (view.Volume < 0) view.Volume = 0;
                    else if (view.Volume > 100) view.Volume = 100;

                    if (IsImageView(view) && view.Volume < MinImageIntervalSeconds)
                    {
                        view.Volume = MinImageIntervalSeconds;
                    }
                }
            }
        }

        /// <summary>生成配置的深拷贝（用于跨线程读取快照）</summary>
        public static AppConfig Clone(AppConfig config)
        {
            return new AppConfig
            {
                PcName = config.PcName,
                UdpPort = config.UdpPort,
                WebPort = config.WebPort,
                DefaultMode = config.DefaultMode,
                WindowWidth = config.WindowWidth,
                WindowHeight = config.WindowHeight,
                ShowModeStatusBar = config.ShowModeStatusBar,
                CameraSources = (config.CameraSources ?? new List<CameraSource>())
                    .Select(camera => new CameraSource
                    {
                        Id = camera.Id,
                        Name = camera.Name,
                        Index = camera.Index,
                        Width = camera.Width,
                        Height = camera.Height
                    })
                    .ToList(),
                VideoSources = (config.VideoSources ?? new List<VideoSource>())
                    .Select(video => new VideoSource
                    {
                        Id = video.Id,
                        Name = video.Name,
                        FolderPath = video.FolderPath,
                        Extensions = video.Extensions,
                        Loop = video.Loop,
                        Shuffle = video.Shuffle
                    })
                    .ToList(),
                ImageSources = (config.ImageSources ?? new List<ImageSource>())
                    .Select(image => new ImageSource
                    {
                        Id = image.Id,
                        Name = image.Name,
                        FolderPath = image.FolderPath,
                        Extensions = image.Extensions,
                        Loop = image.Loop,
                        Shuffle = image.Shuffle
                    })
                    .ToList(),
                Modes = (config.Modes ?? new List<ViewGroup>())
                    .Select(mode => new ViewGroup
                    {
                        Mode = mode.Mode,
                        Views = (mode.Views ?? new List<ViewItem>())
                            .Select(view => new ViewItem
                            {
                                SourceType = view.SourceType,
                                CameraId = view.CameraId,
                                VideoId = view.VideoId,
                                ImageId = view.ImageId,
                                W = view.W,
                                H = view.H,
                                Stretch = view.Stretch,
                                Volume = view.Volume
                            })
                            .ToList()
                    })
                    .ToList()
            };
        }

        /// <summary>校验配置，返回错误列表（为空表示通过）</summary>
        public static List<string> Validate(AppConfig? config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("配置不能为空。");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(config.PcName))
            {
                errors.Add("计算机名不能为空。");
            }
            else if (config.PcName.Contains('-'))
            {
                errors.Add("计算机名不能包含“-”，UDP 指令以“-”分隔计算机名与模式名。");
            }

            if (config.UdpPort < 1 || config.UdpPort > 65535)
            {
                errors.Add("UDP 端口必须是 1～65535 之间的整数。");
            }

            if (config.WebPort < 0 || config.WebPort > 65535)
            {
                errors.Add("网页设置端口必须是 0～65535 之间的整数（0 表示关闭网页设置）。");
            }

            if (config.WindowWidth < 0)
            {
                errors.Add("窗口宽度必须是大于等于 0 的整数（0 表示铺满全屏）。");
            }

            if (config.WindowHeight < 0)
            {
                errors.Add("窗口高度必须是大于等于 0 的整数（0 表示铺满全屏）。");
            }

            var videoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.VideoSources != null)
            {
                foreach (var video in config.VideoSources)
                {
                    if (video == null) continue;

                    var videoId = (video.Id ?? string.Empty).Trim();
                    if (videoId.Length == 0)
                    {
                        errors.Add("视频源 ID 不能为空。");
                    }
                    else if (!videoIds.Add(videoId))
                    {
                        errors.Add($"视频源 ID 重复：{videoId}");
                    }

                    if (string.IsNullOrWhiteSpace(video.FolderPath))
                    {
                        errors.Add($"视频源“{(string.IsNullOrWhiteSpace(video.Name) ? videoId : video.Name)}”的目录不能为空。");
                    }

                    if (string.IsNullOrWhiteSpace(video.Extensions))
                    {
                        errors.Add($"视频源“{(string.IsNullOrWhiteSpace(video.Name) ? videoId : video.Name)}”的扩展名不能为空，例如 .mp4;.avi。");
                    }
                }
            }

            var imageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.ImageSources != null)
            {
                foreach (var image in config.ImageSources)
                {
                    if (image == null) continue;

                    var imageId = (image.Id ?? string.Empty).Trim();
                    var imageName = string.IsNullOrWhiteSpace(image.Name) ? imageId : image.Name;
                    if (imageId.Length == 0)
                    {
                        errors.Add("图片源 ID 不能为空。");
                    }
                    else if (!imageIds.Add(imageId))
                    {
                        errors.Add($"图片源 ID 重复：{imageId}");
                    }

                    if (string.IsNullOrWhiteSpace(image.FolderPath))
                    {
                        errors.Add($"图片源“{imageName}”的目录不能为空。");
                    }

                    if (string.IsNullOrWhiteSpace(image.Extensions))
                    {
                        errors.Add($"图片源“{imageName}”的扩展名不能为空，例如 .jpg;.png。");
                    }
                }
            }

            var modeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.Modes != null)
            {
                foreach (var mode in config.Modes)
                {
                    if (mode == null) continue;

                    var modeName = (mode.Mode ?? string.Empty).Trim();
                    if (modeName.Length == 0)
                    {
                        errors.Add("布局模式名称不能为空。");
                        continue;
                    }

                    if (!modeNames.Add(modeName))
                    {
                        errors.Add($"布局模式名称重复：{modeName}");
                        continue;
                    }

                    if (mode.Views == null) continue;

                    foreach (var view in mode.Views)
                    {
                        if (view == null) continue;

                        if (view.W <= 0)
                        {
                            errors.Add($"模式“{modeName}”的视图宽度必须是大于 0 的数字。");
                            break;
                        }

                        if (view.H <= 0)
                        {
                            errors.Add($"模式“{modeName}”的视图高度必须是大于 0 的数字。");
                            break;
                        }

                        if (IsImageView(view))
                        {
                            if (view.Volume < MinImageIntervalSeconds || view.Volume > MaxImageIntervalSeconds)
                            {
                                errors.Add($"模式“{modeName}”的图片视图翻页间隔必须是 {MinImageIntervalSeconds}～{MaxImageIntervalSeconds} 秒（在音量列填写）。");
                                break;
                            }
                        }
                        else if (view.Volume < 0 || view.Volume > 100)
                        {
                            errors.Add($"模式“{modeName}”的视图音量必须是 0～100 之间的数字。");
                            break;
                        }

                        if (!IsValidStretch(view.Stretch))
                        {
                            errors.Add($"模式“{modeName}”的视图拉伸方式无效：{view.Stretch}（可选 Fill / Uniform / UniformToFill / None 或留空）");
                            break;
                        }

                        if (IsVideoView(view)
                            && !string.IsNullOrWhiteSpace(view.VideoId)
                            && !videoIds.Contains(view.VideoId.Trim()))
                        {
                            errors.Add($"模式“{modeName}”的视图引用了不存在的视频源：{view.VideoId}");
                            break;
                        }

                        if (IsImageView(view)
                            && !string.IsNullOrWhiteSpace(view.ImageId)
                            && !imageIds.Contains(view.ImageId.Trim()))
                        {
                            errors.Add($"模式“{modeName}”的视图引用了不存在的图片源：{view.ImageId}");
                            break;
                        }
                    }
                }
            }

            var defaultMode = (config.DefaultMode ?? string.Empty).Trim();
            if (defaultMode.Length == 0)
            {
                errors.Add("请选择默认模式。");
            }
            else if (!IsBuiltInMode(defaultMode) && !modeNames.Contains(defaultMode))
            {
                errors.Add($"默认模式“{defaultMode}”不在布局模式列表中。");
            }

            return errors;
        }
    }
}
