using System;
using System.Collections.Generic;
using System.Linq;
using CameraShow.Models;

namespace CameraShow.Services
{
    /// <summary>
    /// 配置校验与规范化，供设置对话框和网页设置共用，保证两条入口的规则一致。
    /// </summary>
    public static class ConfigValidator
    {
        public const string BlankModeName = "Blank";
        public const string DesktopModeName = "Desktop";

        /// <summary>判断是否为内置模式（不依赖 config.json 中的 Modes 定义）</summary>
        public static bool IsBuiltInMode(string? modeName)
        {
            return string.Equals(modeName, BlankModeName, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(modeName, DesktopModeName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>将配置中的文本字段与集合规范化为可直接保存的状态</summary>
        public static void Normalize(AppConfig config)
        {
            if (config == null) return;

            config.PcName = (config.PcName ?? string.Empty).Trim();
            config.DefaultMode = (config.DefaultMode ?? string.Empty).Trim();
            config.CameraSources ??= new List<CameraSource>();
            config.Modes ??= new List<ViewGroup>();

            foreach (var mode in config.Modes)
            {
                if (mode == null) continue;

                mode.Mode = (mode.Mode ?? string.Empty).Trim();
                mode.Views ??= new List<ViewItem>();

                foreach (var view in mode.Views)
                {
                    if (view == null) continue;

                    view.CameraId = (view.CameraId ?? string.Empty).Trim();
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
                Modes = (config.Modes ?? new List<ViewGroup>())
                    .Select(mode => new ViewGroup
                    {
                        Mode = mode.Mode,
                        Views = (mode.Views ?? new List<ViewItem>())
                            .Select(view => new ViewItem
                            {
                                CameraId = view.CameraId,
                                W = view.W,
                                H = view.H
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
