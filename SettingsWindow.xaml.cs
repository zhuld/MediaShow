using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MediaShow.Models;
using MediaShow.Services;

namespace MediaShow
{
    /// <summary>
    /// 设置对话框：编辑 config.json 中的基础参数、布局模式与视图，确认后返回<b>新</b>配置对象。
    /// 对话框内部操作配置副本，点击“取消”不会影响运行中的配置。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ObservableCollection<ModeEditor> _modes = new();
        private readonly ObservableCollection<VideoSource> _videoSources = new();
        private readonly List<CameraSource> _cameras;
        private readonly List<SourceOption> _cameraSourceOptions = new();
        private List<SourceOption> _videoSourceOptions = new();
        private string _defaultMode = string.Empty;
        private bool _updatingDefaultMode;

        /// <summary>用户确认后生成的新配置；取消时为 null</summary>
        public AppConfig? ResultConfig { get; private set; }

        /// <summary>配置文件完整路径（仅用于展示与定位）</summary>
        public string ConfigPath { get; }

        public SettingsWindow(AppConfig config, string configPath, WindowStatus? windowStatus = null)
        {
            InitializeComponent();

            ConfigPath = configPath;
            ConfigPathText.Text = configPath;
            ShowWindowStatus(windowStatus);

            _cameras = (config.CameraSources ?? new List<CameraSource>())
                .Select(camera => new CameraSource
                {
                    Id = camera.Id,
                    Name = camera.Name,
                    Index = camera.Index,
                    Width = camera.Width,
                    Height = camera.Height
                })
                .ToList();

            _cameraSourceOptions.Add(new SourceOption { Id = string.Empty, Name = "(不显示)" });
            _cameraSourceOptions.AddRange(_cameras.Select(camera => new SourceOption
            {
                Id = camera.Id,
                Name = string.IsNullOrWhiteSpace(camera.Name) ? camera.Id : camera.Name
            }));

            _modes.CollectionChanged += (_, __) => RefreshDefaultModeOptions();
            foreach (var group in config.Modes ?? new List<ViewGroup>())
            {
                AddModeInternal(ModeEditor.From(group), select: false);
            }

            ModeList.ItemsSource = _modes;
            if (_modes.Count > 0)
            {
                ModeList.SelectedIndex = 0;
            }

            CameraGrid.ItemsSource = _cameras;

            foreach (var video in config.VideoSources ?? new List<VideoSource>())
            {
                _videoSources.Add(new VideoSource
                {
                    Id = video.Id,
                    Name = video.Name,
                    FolderPath = video.FolderPath,
                    Extensions = video.Extensions,
                    Loop = video.Loop,
                    Shuffle = video.Shuffle
                });
            }

            VideoGrid.ItemsSource = _videoSources;
            VideoGrid.CellEditEnding += VideoGrid_CellEditEnding;
            RebuildVideoSourceOptions();

            PcNameBox.Text = config.PcName ?? string.Empty;
            UdpPortBox.Text = config.UdpPort.ToString(CultureInfo.InvariantCulture);
            WebPortBox.Text = config.WebPort.ToString(CultureInfo.InvariantCulture);
            WindowWidthBox.Text = config.WindowWidth.ToString(CultureInfo.InvariantCulture);
            WindowHeightBox.Text = config.WindowHeight.ToString(CultureInfo.InvariantCulture);
            ShowModeStatusBarCheckBox.IsChecked = config.ShowModeStatusBar;
            _defaultMode = ConfigValidator.IsDesktopMode(config.DefaultMode)
                ? ConfigValidator.DesktopModeName
                : config.DefaultMode ?? string.Empty;

            RefreshDefaultModeOptions();
        }

        private void AddModeInternal(ModeEditor mode, bool select)
        {
            mode.PropertyChanged += Mode_PropertyChanged;
            foreach (var view in mode.Views)
            {
                AttachSourceOptions(view);
            }

            _modes.Add(mode);
            if (select)
            {
                ModeList.SelectedItem = mode;
            }
        }

        /// <summary>把当前可选的摄像头 / 视频源列表挂到视图编辑器上（来源列按类型自动切换列表）</summary>
        private void AttachSourceOptions(ViewEditor view)
        {
            view.CameraOptions = _cameraSourceOptions;
            view.VideoOptions = _videoSourceOptions;
        }

        private void Mode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModeEditor.Name))
            {
                RefreshDefaultModeOptions();
            }
        }

        /// <summary>模式增删或改名后同步“默认模式”下拉框的可选项</summary>
        private void RefreshDefaultModeOptions()
        {
            var options = _modes
                .Select(mode => (mode.Name ?? string.Empty).Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            options.Add(ConfigValidator.BlankModeName);
            options.Add(ConfigValidator.DesktopModeName);

            _updatingDefaultMode = true;
            try
            {
                DefaultModeCombo.ItemsSource = options;
                var selected = options.FirstOrDefault(
                    option => string.Equals(option, _defaultMode, StringComparison.OrdinalIgnoreCase));
                if (selected == null)
                {
                    selected = options[0];
                }

                _defaultMode = selected;
                DefaultModeCombo.SelectedItem = selected;
            }
            finally
            {
                _updatingDefaultMode = false;
            }
        }

        private void DefaultModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingDefaultMode) return;

            if (DefaultModeCombo.SelectedItem is string modeName)
            {
                _defaultMode = modeName;
            }
        }

        private void ModeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var mode = ModeList.SelectedItem as ModeEditor;
            ModeDetailPanel.DataContext = mode;
            ModeDetailPanel.IsEnabled = mode != null;
        }

        private void AddMode_Click(object sender, RoutedEventArgs e)
        {
            var mode = new ModeEditor { Name = GenerateUniqueModeName() };
            if (_cameras.Count > 0)
            {
                mode.Views.Add(new ViewEditor { CameraId = _cameras[0].Id });
            }

            AddModeInternal(mode, select: true);
            ModeNameBox.Focus();
            ModeNameBox.SelectAll();
        }

        private void RemoveMode_Click(object sender, RoutedEventArgs e)
        {
            if (ModeList.SelectedItem is not ModeEditor mode) return;

            var result = MessageBox.Show(
                $"确定删除模式“{mode.Name}”吗？",
                "删除模式",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            var index = ModeList.SelectedIndex;
            mode.PropertyChanged -= Mode_PropertyChanged;
            _modes.Remove(mode);

            if (_modes.Count > 0)
            {
                ModeList.SelectedIndex = Math.Min(index, _modes.Count - 1);
            }
        }

        private void AddView_Click(object sender, RoutedEventArgs e)
        {
            if (ModeList.SelectedItem is not ModeEditor mode) return;

            if (mode.Views.Count >= 2)
            {
                MessageBox.Show("界面最多同时显示 2 个视图。", "添加视图", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var cameraId = _cameras.Count > mode.Views.Count ? _cameras[mode.Views.Count].Id : string.Empty;
            var view = new ViewEditor { CameraId = cameraId };
            AttachSourceOptions(view);
            mode.Views.Add(view);
        }

        private void RemoveView_Click(object sender, RoutedEventArgs e)
        {
            if (ModeList.SelectedItem is not ModeEditor mode) return;

            if (ViewsGrid.SelectedItem is ViewEditor view)
            {
                mode.Views.Remove(view);
            }
        }

        private string GenerateUniqueVideoId()
        {
            var index = _videoSources.Count + 1;
            string id;
            do
            {
                id = $"vid_{index++}";
            }
            while (_videoSources.Any(video => string.Equals(video.Id, id, StringComparison.OrdinalIgnoreCase)));

            return id;
        }

        private void AddVideo_Click(object sender, RoutedEventArgs e)
        {
            var video = new VideoSource
            {
                Id = GenerateUniqueVideoId(),
                Name = $"视频源 {_videoSources.Count + 1}",
                Extensions = ConfigValidator.DefaultVideoExtensions,
                Loop = true
            };

            _videoSources.Add(video);
            VideoGrid.SelectedItem = video;
            RebuildVideoSourceOptions();
        }

        private void RemoveVideo_Click(object sender, RoutedEventArgs e)
        {
            if (VideoGrid.SelectedItem is not VideoSource video) return;

            var result = MessageBox.Show(
                $"确定删除视频源“{(string.IsNullOrWhiteSpace(video.Name) ? video.Id : video.Name)}”吗？",
                "删除视频源",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            _videoSources.Remove(video);
            RebuildVideoSourceOptions();
        }

        private void BrowseVideoFolder_Click(object sender, RoutedEventArgs e)
        {
            if (VideoGrid.SelectedItem is not VideoSource video)
            {
                MessageBox.Show("请先在列表中选中要设置目录的视频源。", "选择目录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择视频所在目录（包含子文件夹）",
                SelectedPath = Directory.Exists(video.FolderPath) ? video.FolderPath : AppDomain.CurrentDomain.BaseDirectory,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                video.FolderPath = dialog.SelectedPath;
                VideoGrid.Items.Refresh();
                RebuildVideoSourceOptions();
            }
        }

        private void VideoGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            // 等单元格提交完成后再刷新“来源”下拉框中的视频源名称
            Dispatcher.BeginInvoke(new Action(RebuildVideoSourceOptions), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>重建视频源候选列表，并同步到所有视图的“来源”列</summary>
        private void RebuildVideoSourceOptions()
        {
            var options = new List<SourceOption>
            {
                new() { Id = string.Empty, Name = "(不显示)" }
            };
            options.AddRange(_videoSources.Select(video => new SourceOption
            {
                Id = video.Id,
                Name = string.IsNullOrWhiteSpace(video.Name) ? video.Id : video.Name
            }));

            _videoSourceOptions = options;

            foreach (var mode in _modes)
            {
                foreach (var view in mode.Views)
                {
                    AttachSourceOptions(view);
                }
            }
        }

        private string GenerateUniqueModeName()
        {
            var index = _modes.Count + 1;
            string name;
            do
            {
                name = $"Mode{index++}";
            }
            while (_modes.Any(mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase))
                   || string.Equals(name, ConfigValidator.BlankModeName, StringComparison.OrdinalIgnoreCase)
                   || ConfigValidator.IsDesktopMode(name));

            return name;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 提交仍处于编辑状态的单元格，避免最后输入的内容未写回绑定源
            ViewsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ViewsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            VideoGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            VideoGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!TryBuildConfig(out var config, out var error))
            {
                MessageBox.Show(error, "设置校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultConfig = config;
            DialogResult = true;
        }

        private bool TryBuildConfig(out AppConfig config, out string error)
        {
            var errors = new List<string>();

            if (!int.TryParse(UdpPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var udpPort))
            {
                errors.Add("UDP 端口必须是 1～65535 之间的整数。");
                udpPort = 30000;
            }

            if (!int.TryParse(WebPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var webPort))
            {
                errors.Add("网页设置端口必须是 0～65535 之间的整数（0 表示关闭网页设置）。");
                webPort = 0;
            }

            if (!int.TryParse(WindowWidthBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
            {
                errors.Add("窗口宽度必须是大于等于 0 的整数（0 表示铺满全屏）。");
                width = 0;
            }

            if (!int.TryParse(WindowHeightBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                errors.Add("窗口高度必须是大于等于 0 的整数（0 表示铺满全屏）。");
                height = 0;
            }

            var modes = new List<ViewGroup>();
            foreach (var editor in _modes)
            {
                var modeName = (editor.Name ?? string.Empty).Trim();
                var views = new List<ViewItem>();

                foreach (var view in editor.Views)
                {
                    if (!double.TryParse((view.W ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var viewWidth))
                    {
                        errors.Add($"模式“{modeName}”的视图宽度必须是大于 0 的数字。");
                        continue;
                    }

                    if (!double.TryParse((view.H ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var viewHeight))
                    {
                        errors.Add($"模式“{modeName}”的视图高度必须是大于 0 的数字。");
                        continue;
                    }

                    views.Add(new ViewItem
                    {
                        SourceType = string.Equals(view.SourceType, ConfigValidator.VideoSourceType, StringComparison.OrdinalIgnoreCase)
                            ? ConfigValidator.VideoSourceType
                            : ConfigValidator.CameraSourceType,
                        CameraId = (view.CameraId ?? string.Empty).Trim(),
                        VideoId = (view.VideoId ?? string.Empty).Trim(),
                        W = viewWidth,
                        H = viewHeight,
                        Volume = view.Volume
                    });
                }

                modes.Add(new ViewGroup { Mode = modeName, Views = views });
            }

            config = new AppConfig
            {
                PcName = PcNameBox.Text.Trim(),
                UdpPort = udpPort,
                WebPort = webPort,
                DefaultMode = _defaultMode,
                WindowWidth = width,
                WindowHeight = height,
                ShowModeStatusBar = ShowModeStatusBarCheckBox.IsChecked == true,
                CameraSources = _cameras
                    .Select(camera => new CameraSource
                    {
                        Id = camera.Id,
                        Name = camera.Name,
                        Index = camera.Index,
                        Width = camera.Width,
                        Height = camera.Height
                    })
                    .ToList(),
                VideoSources = _videoSources
                    .Select(video => new VideoSource
                    {
                        Id = (video.Id ?? string.Empty).Trim(),
                        Name = (video.Name ?? string.Empty).Trim(),
                        FolderPath = (video.FolderPath ?? string.Empty).Trim(),
                        Extensions = string.IsNullOrWhiteSpace(video.Extensions)
                            ? ConfigValidator.DefaultVideoExtensions
                            : video.Extensions.Trim(),
                        Loop = video.Loop,
                        Shuffle = video.Shuffle
                    })
                    .ToList(),
                Modes = modes
            };

            errors.AddRange(ConfigValidator.Validate(config));

            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors.Distinct().Select(line => "• " + line));
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 展示主窗口当前的实际尺寸（物理像素 + WPF DIP + DPI 缩放）
        /// </summary>
        private void ShowWindowStatus(WindowStatus? status)
        {
            if (status == null)
            {
                WindowStatusText.Text = "当前窗口实际尺寸：暂不可用（窗口尚未创建）";
                return;
            }

            var scalePercent = Math.Round(status.DpiScaleX * 100);
            var dipText = $"{status.WidthDip:0.#} × {status.HeightDip:0.#} DIP";
            var modeText = string.IsNullOrWhiteSpace(status.CurrentMode) ? string.Empty : $"，模式 {status.CurrentMode}";

            WindowStatusText.Text = status.IsMinimized
                ? $"当前窗口：已隐藏/最小化（Desktop），恢复后尺寸 {status.PixelWidth} × {status.PixelHeight} 像素（{dipText}，缩放 {scalePercent}%{modeText}）"
                : $"当前窗口：{status.PixelWidth} × {status.PixelHeight} 像素（{dipText}，缩放 {scalePercent}%{modeText}）";
        }

        private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{ConfigPath}\"");
                }
                else
                {
                    var directory = Path.GetDirectoryName(ConfigPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Process.Start("explorer.exe", $"\"{directory}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开配置文件位置：{ex.Message}", "MediaShow", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>设置对话框中可编辑的布局模式</summary>
    internal class ModeEditor : ObservableObject
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set => Set(ref _name, value ?? string.Empty);
        }

        public ObservableCollection<ViewEditor> Views { get; } = new();

        public static ModeEditor From(ViewGroup group)
        {
            var editor = new ModeEditor { Name = group.Mode ?? string.Empty };
            if (group.Views != null)
            {
                foreach (var view in group.Views)
                {
                    editor.Views.Add(new ViewEditor
                    {
                        SourceType = ConfigValidator.IsVideoView(view)
                            ? ConfigValidator.VideoSourceType
                            : ConfigValidator.CameraSourceType,
                        CameraId = view.CameraId ?? string.Empty,
                        VideoId = view.VideoId ?? string.Empty,
                        W = view.W.ToString(CultureInfo.InvariantCulture),
                        H = view.H.ToString(CultureInfo.InvariantCulture),
                        Volume = view.Volume
                    });
                }
            }

            return editor;
        }
    }

    /// <summary>设置对话框中可编辑的视图（宽高保留文本形式，便于校验输入）</summary>
    internal class ViewEditor : ObservableObject
    {
        private string _sourceType = ConfigValidator.CameraSourceType;
        private string _cameraId = string.Empty;
        private string _videoId = string.Empty;
        private IReadOnlyList<SourceOption> _cameraOptions = Array.Empty<SourceOption>();
        private IReadOnlyList<SourceOption> _videoOptions = Array.Empty<SourceOption>();
        private string _w = "640";
        private string _h = "480";
        private double _volume = 100;

        /// <summary>“类型”下拉框的可选项（摄像头 / 视频），所有视图共用</summary>
        public IReadOnlyList<SourceTypeOption> SourceTypeOptions { get; } = new List<SourceTypeOption>
        {
            new() { Id = ConfigValidator.CameraSourceType, Name = "摄像头" },
            new() { Id = ConfigValidator.VideoSourceType, Name = "视频" }
        };

        /// <summary>“来源”下拉框按类型切换的摄像头候选列表</summary>
        public IReadOnlyList<SourceOption> CameraOptions
        {
            get => _cameraOptions;
            set
            {
                _cameraOptions = value ?? Array.Empty<SourceOption>();
                OnPropertyChanged();
            }
        }

        /// <summary>“来源”下拉框按类型切换的视频源候选列表</summary>
        public IReadOnlyList<SourceOption> VideoOptions
        {
            get => _videoOptions;
            set
            {
                _videoOptions = value ?? Array.Empty<SourceOption>();
                OnPropertyChanged();
            }
        }

        public bool IsVideo => string.Equals(_sourceType, ConfigValidator.VideoSourceType, StringComparison.OrdinalIgnoreCase);

        public bool IsCamera => !IsVideo;

        public string SourceType
        {
            get => _sourceType;
            set
            {
                var normalized = string.Equals(value, ConfigValidator.VideoSourceType, StringComparison.OrdinalIgnoreCase)
                    ? ConfigValidator.VideoSourceType
                    : ConfigValidator.CameraSourceType;
                if (!Set(ref _sourceType, normalized)) return;

                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsCamera));
            }
        }

        public string CameraId
        {
            get => _cameraId;
            set => Set(ref _cameraId, value ?? string.Empty);
        }

        public string VideoId
        {
            get => _videoId;
            set => Set(ref _videoId, value ?? string.Empty);
        }

        public string W
        {
            get => _w;
            set => Set(ref _w, value ?? string.Empty);
        }

        public string H
        {
            get => _h;
            set => Set(ref _h, value ?? string.Empty);
        }

        /// <summary>音量百分比（0～100），仅视频视图生效</summary>
        public double Volume
        {
            get => _volume;
            set => Set(ref _volume, Math.Max(0, Math.Min(100, value)));
        }
    }

    /// <summary>视图来源类型下拉框选项（摄像头 / 视频）</summary>
    internal class SourceTypeOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>“来源”下拉框选项：摄像头或视频源</summary>
    internal class SourceOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    internal abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
