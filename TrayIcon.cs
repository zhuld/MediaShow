using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CameraShow.Models;

namespace CameraShow
{
    /// <summary>
    /// 托盘图标管理类
    /// 负责初始化、显示和处理系统托盘图标的右键菜单
    /// </summary>
    public static class TrayIconManager
    {
        private static NotifyIcon? _trayIcon;
        private static ContextMenuStrip? _trayMenu;
        private static AppConfig? _config;

        /// <summary>
        /// 初始化托盘图标
        /// 必须在 MainWindow 构造函数中调用，并传入配置对象
        /// </summary>
        /// <param name="config">应用配置对象，包含模式列表</param>
        public static void Initialize(AppConfig config)
        {
            _config = config;

            // 创建托盘图标
            _trayIcon = new NotifyIcon
            {
                Visible = true,
                Text = "CameraShow"
            };

            // 尝试从可执行文件提取图标
            try
            {
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                // 如果提取失败，使用默认图标（Visible=true 仍会显示标准 .NET 图标）
            }

            // 创建右键菜单
            _trayMenu = new ContextMenuStrip();

            // 添加模式选择项 - 来自配置
            if (_config?.Modes != null)
            {
                foreach (var mode in _config.Modes)
                {
                    if (!string.IsNullOrWhiteSpace(mode.Mode))
                    {
                        var modeItem = new ToolStripMenuItem(mode.Mode);
                        modeItem.Click += (s, e) =>
                        {
                            // 通过事件传递给主窗口处理
                            _trayIcon?.ShowBalloonTip(2000, "模式切换", $"切换到 {mode.Mode} 模式", System.Windows.Forms.ToolTipIcon.Info);
                        };
                        _trayMenu!.Items.Add(modeItem);
                    }
                }
            }

            // 始终添加特殊模式（Desktop 和 Shutdown），确保它们总是出现
            var desktopItem = new ToolStripMenuItem("Desktop");
            desktopItem.Click += (s, e) =>
            {
                // 通过事件或Action传递给主窗口
                _trayIcon?.ShowBalloonTip(2000, "模式切换", "已最小化到托盘，显示桌面", System.Windows.Forms.ToolTipIcon.Info);
            };
            _trayMenu!.Items.Add(desktopItem);

            var shutdownItem = new ToolStripMenuItem("Shutdown");
            shutdownItem.Click += (s, e) =>
            {
                // 关机操作
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/s /t 0",
                        Verb = "runas"
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    // 错误已在日志中记录
                }
                _trayIcon?.ShowBalloonTip(2000, "模式切换", "正在关机", System.Windows.Forms.ToolTipIcon.Warning);
            };
            _trayMenu!.Items.Add(shutdownItem);

            // 添加分隔符
            _trayMenu!.Items.Add(new ToolStripSeparator());

            // 添加退出按钮 - 包含正确的清理逻辑
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                // 先隐藏并处置托盘图标
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                _trayMenu = null;
                // 退出应用
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
            _trayMenu!.Items.Add(exitItem);

            // 将菜单绑定到托盘图标
            _trayIcon!.ContextMenuStrip = _trayMenu;
        }

        /// <summary>
        /// 获取当前的托盘图标实例（用于主窗口等需要访问的情况）
        /// </summary>
        public static NotifyIcon? GetTrayIcon()
        {
            return _trayIcon;
        }

        /// <summary>
        /// 重新加载配置并更新托盘菜单
        /// </summary>
        public static void ReloadConfig(AppConfig config)
        {
            _config = config;

            // 清除现有的模式菜单项（除了特殊模式）
            if (_trayMenu?.Items != null)
            {
                // 保留特殊模式（Desktop 和 Shutdown），移除其他项
                var specialTexts = new List<string> { "Desktop", "Shutdown" };
                var itemsToRemove = new List<ToolStripItem>();

                foreach (ToolStripItem item in _trayMenu.Items)
                {
                    if (!specialTexts.Contains(item.Text))
                    {
                        itemsToRemove.Add(item);
                    }
                }

                foreach (var item in itemsToRemove)
                {
                    _trayMenu.Items.Remove(item);
                }

                // 重新添加来自配置的模式
                if (_config?.Modes != null)
                {
                    foreach (var mode in _config.Modes)
                    {
                        if (!string.IsNullOrWhiteSpace(mode.Mode))
                        {
                            var modeItem = new ToolStripMenuItem(mode.Mode);
                            modeItem.Click += (s, e) =>
                            {
                                _trayIcon?.ShowBalloonTip(2000, "模式切换", $"切换到 {mode.Mode} 模式", System.Windows.Forms.ToolTipIcon.Info);
                            };
                            _trayMenu.Items.Add(modeItem);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 强制显示气球提示
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息</param>
        /// <param name="icon">图标类型</param>
        public static void ShowBalloonTip(string title, string message, ToolTipIcon icon)
        {
            _trayIcon?.ShowBalloonTip(2000, title, message, icon);
        }
    }
}