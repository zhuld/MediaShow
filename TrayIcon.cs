using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MediaShow.Models;
using MediaShow.Services;

namespace MediaShow
{
    /// <summary>
    /// 托盘图标管理类（进程内唯一实例）
    /// 负责初始化、显示托盘图标并构建右键菜单，菜单点击通过事件通知主窗口处理
    /// </summary>
    public static class TrayIconManager
    {
        private static NotifyIcon? _trayIcon;
        private static ContextMenuStrip? _trayMenu;
        private static AppConfig? _config;

        /// <summary>配置中的模式被点击，参数为模式名称</summary>
        public static event Action<string>? ModeSelected;

        /// <summary>内置 Blank 模式（黑屏）被点击</summary>
        public static event Action? BlankRequested;

        /// <summary>Desktop 菜单被点击</summary>
        public static event Action? DesktopRequested;

        /// <summary>Shutdown 菜单被点击</summary>
        public static event Action? ShutdownRequested;

        /// <summary>Settings 菜单被点击</summary>
        public static event Action? SettingsRequested;

        /// <summary>Exit 菜单被点击（托盘图标已先行释放）</summary>
        public static event Action? ExitRequested;

        /// <summary>托盘图标被双击</summary>
        public static event Action? RestoreRequested;

        /// <summary>
        /// 初始化托盘图标（可重复调用，只有第一次会真正创建图标）
        /// </summary>
        /// <param name="config">应用配置对象，包含模式列表</param>
        public static void Initialize(AppConfig config)
        {
            _config = config;

            if (_trayIcon != null)
            {
                RebuildMenu();
                return;
            }

            _trayIcon = new NotifyIcon
            {
                Text = "MediaShow",
                Visible = false
            };

            try
            {
                // 使用 server.ico 作为托盘图标
                var trayIconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Resources\\server.ico");
                _trayIcon.Icon = new System.Drawing.Icon(trayIconPath);
            }
            catch
            {
                // 提取失败时使用默认图标
            }

            _trayIcon.DoubleClick += (s, e) => RestoreRequested?.Invoke();

            // 先构建菜单再显示，确保菜单就绪后再出现图标
            RebuildMenu();
            _trayIcon.Visible = true;
        }

        /// <summary>
        /// 重新加载配置并更新托盘菜单
        /// </summary>
        public static void ReloadConfig(AppConfig config)
        {
            _config = config;
            RebuildMenu();
        }

        /// <summary>
        /// 释放托盘图标
        /// </summary>
        public static void Dispose()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;
        }

        private static void RebuildMenu()
        {
            var menu = new ContextMenuStrip();
            int modeCount = 0;

            // 来自配置的模式
            if (_config?.Modes != null)
            {
                foreach (var mode in _config.Modes)
                {
                    if (string.IsNullOrWhiteSpace(mode.Mode)) continue;

                    var modeName = mode.Mode;
                    var modeItem = new ToolStripMenuItem(modeName);
                    modeItem.Click += (s, e) => ModeSelected?.Invoke(modeName);
                    menu.Items.Add(modeItem);
                    modeCount++;
                }
            }

            // 内置模式：Blank（黑屏），配置中已定义同名模式时不重复添加
            if (!HasConfiguredMode("Blank"))
            {
                var blankItem = new ToolStripMenuItem("Blank");
                blankItem.Click += (s, e) => BlankRequested?.Invoke();
                menu.Items.Add(blankItem);
            }

            // 始终添加的特殊模式
            var desktopItem = new ToolStripMenuItem("Desktop");
            desktopItem.Click += (s, e) => DesktopRequested?.Invoke();
            menu.Items.Add(desktopItem);

            var shutdownItem = new ToolStripMenuItem("Shutdown");
            shutdownItem.Click += (s, e) => ShutdownRequested?.Invoke();
            menu.Items.Add(shutdownItem);

            menu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => SettingsRequested?.Invoke();
            menu.Items.Add(settingsItem);

            var exitItem = new ToolStripMenuItem("Exit");
            // 不要在菜单点击回调里释放菜单/托盘图标，否则会打断菜单的点击处理流程，
            // 由主窗口在清理完成后统一退出（OnClosed / OnExit 中释放）
            exitItem.Click += (s, e) => ExitRequested?.Invoke();
            menu.Items.Add(exitItem);

            var oldMenu = _trayMenu;
            _trayMenu = menu;
            if (_trayIcon != null)
            {
                _trayIcon.ContextMenuStrip = menu;
            }

            oldMenu?.Dispose();

            LoggerService.Info($"托盘菜单已构建：配置模式 {modeCount} 个，共 {menu.Items.Count} 项");
        }

        private static bool HasConfiguredMode(string modeName)
        {
            return _config?.Modes != null
                && _config.Modes.Any(mode => string.Equals(mode.Mode, modeName, StringComparison.OrdinalIgnoreCase));
        }
    }
}