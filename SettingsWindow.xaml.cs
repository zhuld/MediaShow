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
using CameraShow.Models;

namespace CameraShow
{
    /// <summary>
    /// 设置对话框：编辑 config.json 中的基础参数、布局模式与视图，确认后返回<b>新</b>配置对象。
    /// 对话框内部操作配置副本，点击“取消”不会影响运行中的配置。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private const string BlankModeName = "Blank";

        private readonly ObservableCollection<ModeEditor> _modes = new();
        private readonly List<CameraSource> _cameras;
        private readonly List<CameraSource> _cameraOptions;
        private string _defaultMode = string.Empty;
        private bool _updatingDefaultMode;

        /// <summary>用户确认后生成的新配置；取消时为 null</summary>
        public AppConfig? ResultConfig { get; private set; }

        /// <summary>配置文件完整路径（仅用于展示与定位）</summary>
        public string ConfigPath { get; }

        public SettingsWindow(AppConfig config, string configPath)
        {
            InitializeComponent();

            ConfigPath = configPath;
            ConfigPathText.Text = configPath;

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

            _cameraOptions = new List<CameraSource>
            {
                new CameraSource { Id = string.Empty, Name = "(不显示)" }
            };
            _cameraOptions.AddRange(_cameras);

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
            CameraIdColumn.ItemsSource = _cameraOptions;
            CameraIdColumn.DisplayMemberPath = nameof(CameraSource.Name);
            CameraIdColumn.SelectedValuePath = nameof(CameraSource.Id);

            PcNameBox.Text = config.PcName ?? string.Empty;
            UdpPortBox.Text = config.UdpPort.ToString(CultureInfo.InvariantCulture);
            WindowWidthBox.Text = config.WindowWidth.ToString(CultureInfo.InvariantCulture);
            WindowHeightBox.Text = config.WindowHeight.ToString(CultureInfo.InvariantCulture);
            _defaultMode = config.DefaultMode ?? string.Empty;

            RefreshDefaultModeOptions();
        }

        private void AddModeInternal(ModeEditor mode, bool select)
        {
            mode.PropertyChanged += Mode_PropertyChanged;
            _modes.Add(mode);
            if (select)
            {
                ModeList.SelectedItem = mode;
            }
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
            options.Add(BlankModeName);

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
            mode.Views.Add(new ViewEditor { CameraId = cameraId });
        }

        private void RemoveView_Click(object sender, RoutedEventArgs e)
        {
            if (ModeList.SelectedItem is not ModeEditor mode) return;

            if (ViewsGrid.SelectedItem is ViewEditor view)
            {
                mode.Views.Remove(view);
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
                   || string.Equals(name, BlankModeName, StringComparison.OrdinalIgnoreCase));

            return name;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 提交仍处于编辑状态的单元格，避免最后输入的内容未写回绑定源
            ViewsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ViewsGrid.CommitEdit(DataGridEditingUnit.Row, true);

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
            config = new AppConfig();
            var errors = new List<string>();

            var pcName = PcNameBox.Text.Trim();
            if (pcName.Length == 0)
            {
                errors.Add("计算机名不能为空。");
            }
            else if (pcName.Contains('-'))
            {
                errors.Add("计算机名不能包含“-”，UDP 指令以“-”分隔计算机名与模式名。");
            }

            if (!int.TryParse(UdpPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var udpPort)
                || udpPort < 1 || udpPort > 65535)
            {
                errors.Add("UDP 端口必须是 1～65535 之间的整数。");
            }

            if (!int.TryParse(WindowWidthBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || width < 0)
            {
                errors.Add("窗口宽度必须是大于等于 0 的整数（0 表示铺满全屏）。");
            }

            if (!int.TryParse(WindowHeightBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                || height < 0)
            {
                errors.Add("窗口高度必须是大于等于 0 的整数（0 表示铺满全屏）。");
            }

            var modeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modes = new List<ViewGroup>();
            foreach (var editor in _modes)
            {
                var modeName = (editor.Name ?? string.Empty).Trim();
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

                var views = new List<ViewItem>();
                var viewsValid = true;
                foreach (var view in editor.Views)
                {
                    if (!double.TryParse((view.W ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var viewWidth)
                        || viewWidth <= 0)
                    {
                        errors.Add($"模式“{modeName}”的视图宽度必须是大于 0 的数字。");
                        viewsValid = false;
                        break;
                    }

                    if (!double.TryParse((view.H ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var viewHeight)
                        || viewHeight <= 0)
                    {
                        errors.Add($"模式“{modeName}”的视图高度必须是大于 0 的数字。");
                        viewsValid = false;
                        break;
                    }

                    views.Add(new ViewItem
                    {
                        CameraId = (view.CameraId ?? string.Empty).Trim(),
                        W = viewWidth,
                        H = viewHeight
                    });
                }

                if (viewsValid)
                {
                    modes.Add(new ViewGroup { Mode = modeName, Views = views });
                }
            }

            if (string.IsNullOrWhiteSpace(_defaultMode))
            {
                errors.Add("请选择默认模式。");
            }
            else if (!string.Equals(_defaultMode, BlankModeName, StringComparison.OrdinalIgnoreCase)
                     && !modeNames.Contains(_defaultMode))
            {
                errors.Add($"默认模式“{_defaultMode}”不在布局模式列表中。");
            }

            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors.Select(line => "• " + line));
                return false;
            }

            config = new AppConfig
            {
                PcName = pcName,
                UdpPort = udpPort,
                DefaultMode = _defaultMode,
                WindowWidth = width,
                WindowHeight = height,
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
                Modes = modes
            };
            error = string.Empty;
            return true;
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
                MessageBox.Show($"无法打开配置文件位置：{ex.Message}", "CameraShow", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        CameraId = view.CameraId ?? string.Empty,
                        W = view.W.ToString(CultureInfo.InvariantCulture),
                        H = view.H.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }

            return editor;
        }
    }

    /// <summary>设置对话框中可编辑的视图（宽高保留文本形式，便于校验输入）</summary>
    internal class ViewEditor : ObservableObject
    {
        private string _cameraId = string.Empty;
        private string _w = "640";
        private string _h = "480";

        public string CameraId
        {
            get => _cameraId;
            set => Set(ref _cameraId, value ?? string.Empty);
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
