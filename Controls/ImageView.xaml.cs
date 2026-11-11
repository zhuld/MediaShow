using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaShow.Models;
using MediaShow.Services;
using ImageSource = MediaShow.Models.ImageSource;

namespace MediaShow.Controls
{
    /// <summary>
    /// 单个图片源显示控件：按播放列表轮播图片源目录中的图片文件，并按配置执行切换特效。
    /// 图片在后台线程解码为冻结的位图后交给 UI 显示，文件句柄在解码完成后立即释放。
    /// </summary>
    public partial class ImageView : Grid, IDisposable
    {
        private const int TransitionDurationMilliseconds = 450;

        private ImagePlaylistService? _playlist;
        private ImageSource? _imageSource;
        private DispatcherTimer? _timer;
        private string? _currentFile;
        private int _intervalSeconds = 10;
        private string _transition = ConfigValidator.DefaultImageTransition;
        private int _loadVersion;
        private int _transitionToken;
        private bool _playing;
        private bool _disposed;

        public string ImageId { get; set; } = string.Empty;

        public ImageView()
        {
            InitializeComponent();
        }

        /// <summary>设置该视图轮播的图片源、翻页间隔与画面拉伸方式，需在 Play 之前调用</summary>
        public void Configure(ImageSource? imageSource, string imageId, int intervalSeconds, Stretch stretch)
        {
            _imageSource = imageSource;
            ImageId = imageId ?? string.Empty;
            _intervalSeconds = Math.Max(ConfigValidator.MinImageIntervalSeconds, intervalSeconds);
            _transition = ConfigValidator.NormalizeImageTransition(imageSource?.Transition);
            SetStretch(stretch);
        }

        /// <summary>更新翻页间隔（秒）</summary>
        public void SetInterval(int intervalSeconds)
        {
            _intervalSeconds = Math.Max(ConfigValidator.MinImageIntervalSeconds, intervalSeconds);

            if (_timer != null && _playing)
            {
                _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
                _timer.Stop();
                _timer.Start();
            }
        }

        /// <summary>设置画面拉伸方式（Fill / Uniform / UniformToFill / None）</summary>
        public void SetStretch(Stretch stretch)
        {
            PART_Image.Stretch = stretch;
            PART_Previous.Stretch = stretch;
        }

        /// <summary>开始轮播；控件尚未加载完成时会等到加载后再开始</summary>
        public void Play()
        {
            if (_disposed) return;

            if (!IsLoaded)
            {
                Loaded -= ImageView_Loaded;
                Loaded += ImageView_Loaded;
                return;
            }

            StartSlideshow();
        }

        /// <summary>停止轮播并清除当前图片</summary>
        public void Stop()
        {
            _playing = false;
            _loadVersion++;
            _transitionToken++;

            if (_timer != null)
            {
                _timer.Stop();
            }

            _currentFile = null;
            ResetImage(PART_Image);
            ResetImage(PART_Previous);
            PART_Image.Source = null;
            PART_Previous.Source = null;
            PART_Previous.Opacity = 0;
        }

        private void ImageView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ImageView_Loaded;
            if (!_disposed)
            {
                StartSlideshow();
            }
        }

        private void ImageView_Unloaded(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void StartSlideshow()
        {
            if (_disposed || _playing) return;

            if (_playlist == null)
            {
                _playlist = ImagePlaylistService.Create(_imageSource, out var error);
                if (_playlist == null)
                {
                    var message = error ?? "无法加载图片源";
                    LoggerService.Warn($"图片视图 {ImageId} 无法轮播：{message}");
                    ShowMessage(message);
                    return;
                }
            }

            _playing = true;
            ShowNextImage();
        }

        private void ShowNextImage()
        {
            if (_disposed || !_playing) return;

            // 先停掉计时器，等图片真正显示后再开始计时，避免解码耗时挤占显示时长
            _timer?.Stop();

            if (_playlist == null || !_playlist.MoveNext())
            {
                LoggerService.Info($"图片视图 {ImageId} 轮播结束（循环已关闭）");
                _playing = false;
                return;
            }

            LoadCurrentImage();
        }

        private void LoadCurrentImage()
        {
            var file = _playlist?.CurrentFile;
            if (file == null)
            {
                ShowMessage("没有可轮播的图片文件");
                return;
            }

            _currentFile = file;
            var version = ++_loadVersion;

            HideMessage();

            Task.Run(() => LoadBitmap(file))
                .ContinueWith(
                    task => DispatchToUi(() => OnImageLoaded(version, task)),
                    TaskScheduler.Default);
        }

        /// <summary>关闭窗口时调度线程可能已停止，忽略此时的回调</summary>
        private void DispatchToUi(Action action)
        {
            if (_disposed || Dispatcher.HasShutdownStarted) return;

            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, action);
            }
            catch (InvalidOperationException)
            {
                // 调度已关闭，忽略
            }
        }

        private void OnImageLoaded(int version, Task<BitmapSource> task)
        {
            if (_disposed || version != _loadVersion) return;

            if (task.IsFaulted || task.IsCanceled)
            {
                var reason = task.Exception?.GetBaseException().Message ?? "无法读取图片";
                HandleImageFailure(_currentFile, reason);
                return;
            }

            DisplayImage(task.Result);
            LoggerService.Info($"图片视图 {ImageId} 显示图片：{Path.GetFileName(_currentFile ?? string.Empty)}");
            ScheduleNextImage();
        }

        /// <summary>显示新图片并执行切换特效：旧图片保留在底层，动画结束后再释放</summary>
        private void DisplayImage(BitmapSource bitmap)
        {
            var previous = PART_Image.Source;

            ResetImage(PART_Image);
            PART_Image.Source = bitmap;
            PART_Image.Opacity = 1;

            if (previous != null && !ReferenceEquals(previous, bitmap))
            {
                ResetImage(PART_Previous);
                PART_Previous.Source = previous;
                PART_Previous.Opacity = 1;
            }
            else
            {
                PART_Previous.Source = null;
                PART_Previous.Opacity = 0;
            }

            StartTransition();
        }

        /// <summary>清除图层上的动画与变换，避免上一张图片的特效残留</summary>
        private static void ResetImage(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.RenderTransform = Transform.Identity;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.Opacity = 1;
        }

        private void StartTransition()
        {
            var token = ++_transitionToken;
            var duration = TimeSpan.FromMilliseconds(TransitionDurationMilliseconds);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            switch (_transition)
            {
                case "Slide":
                    AnimateSlide(token, duration, easing, horizontal: true);
                    break;
                case "SlideUp":
                    AnimateSlide(token, duration, easing, horizontal: false);
                    break;
                case "Zoom":
                    AnimateZoom(token, duration, easing);
                    break;
                case "Rotate":
                    AnimateRotate(token, duration, easing);
                    break;
                default:
                    AnimateFade(token, duration, easing);
                    break;
            }
        }

        /// <summary>淡入淡出：新图片从透明淡入，覆盖在旧图片上</summary>
        private void AnimateFade(int token, TimeSpan duration, IEasingFunction easing)
        {
            PART_Image.Opacity = 1;
            PART_Image.BeginAnimation(
                UIElement.OpacityProperty,
                CreateAnimation(0, 1, duration, easing, () => OnTransitionCompleted(token)));
        }

        /// <summary>滑动：新图片从右侧（或下方）滑入并覆盖旧图片</summary>
        private void AnimateSlide(int token, TimeSpan duration, IEasingFunction easing, bool horizontal)
        {
            var distance = horizontal ? PART_Image.ActualWidth : PART_Image.ActualHeight;
            if (distance <= 0) distance = horizontal ? ActualWidth : ActualHeight;
            if (distance <= 0) distance = 1;

            var transform = horizontal ? new TranslateTransform(distance, 0) : new TranslateTransform(0, distance);
            PART_Image.RenderTransform = transform;
            PART_Image.Opacity = 1;

            var property = horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            transform.BeginAnimation(
                property,
                CreateAnimation(distance, 0, duration, easing, () => OnTransitionCompleted(token)));
        }

        /// <summary>缩放：新图片由小放大并淡入</summary>
        private void AnimateZoom(int token, TimeSpan duration, IEasingFunction easing)
        {
            var scale = new ScaleTransform(0.82, 0.82);
            PART_Image.RenderTransform = scale;
            PART_Image.Opacity = 0.4;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(0.82, 1, duration, easing));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(0.82, 1, duration, easing));
            PART_Image.BeginAnimation(
                UIElement.OpacityProperty,
                CreateAnimation(0.4, 1, duration, easing, () => OnTransitionCompleted(token)));
        }

        /// <summary>旋转：新图片轻微旋转并淡入</summary>
        private void AnimateRotate(int token, TimeSpan duration, IEasingFunction easing)
        {
            var rotate = new RotateTransform(-12);
            PART_Image.RenderTransform = rotate;
            PART_Image.Opacity = 0.3;

            rotate.BeginAnimation(RotateTransform.AngleProperty, CreateAnimation(-12, 0, duration, easing));
            PART_Image.BeginAnimation(
                UIElement.OpacityProperty,
                CreateAnimation(0.3, 1, duration, easing, () => OnTransitionCompleted(token)));
        }

        private static DoubleAnimation CreateAnimation(
            double from,
            double to,
            TimeSpan duration,
            IEasingFunction easing,
            Action? onCompleted = null)
        {
            // FillBehavior.Stop 表示动画结束后回到基础值，由 ResetImage 统一清理
            var animation = new DoubleAnimation(from, to, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            if (onCompleted != null)
            {
                animation.Completed += (_, _) => onCompleted();
            }

            return animation;
        }

        /// <summary>特效结束后释放底层旧图片，避免长时间占用内存</summary>
        private void OnTransitionCompleted(int token)
        {
            if (_disposed || token != _transitionToken) return;

            PART_Previous.Source = null;
            PART_Previous.Opacity = 0;
            ResetImage(PART_Image);
        }

        private void ScheduleNextImage()
        {
            if (_disposed || !_playing) return;

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background);
                _timer.Tick += (_, _) => ShowNextImage();
            }

            _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>标记当前图片无法读取并立即尝试下一张；全部失败时在视图中显示原因</summary>
        private void HandleImageFailure(string? file, string reason)
        {
            if (_disposed) return;

            _playlist?.MarkCurrentFailed();
            PART_Image.Source = null;
            LoggerService.Warn($"图片视图 {ImageId} 跳过无法显示的图片：{Path.GetFileName(file ?? string.Empty)}｜{reason}");

            if (_playlist != null && _playlist.MoveNext())
            {
                LoadCurrentImage();
                return;
            }

            var message = file == null
                ? $"无法显示图片：{reason}"
                : $"无法显示图片文件：{Path.GetFileName(file)}\n{reason}";
            ShowMessage(message);
            _playing = false;
            LoggerService.Warn($"图片视图 {ImageId} 没有可显示的图片，已停止：{reason}");
        }

        /// <summary>在后台线程解码图片；OnLoad 确保解码完成后立即释放文件句柄，Freeze 便于跨线程使用</summary>
        private static BitmapSource LoadBitmap(string file)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void ShowMessage(string message)
        {
            PART_Message.Text = message;
            PART_Message.Visibility = Visibility.Visible;
        }

        private void HideMessage()
        {
            PART_Message.Visibility = Visibility.Collapsed;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Loaded -= ImageView_Loaded;
            Stop();

            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            _playlist = null;
            _imageSource = null;
            GC.SuppressFinalize(this);
        }
    }
}
