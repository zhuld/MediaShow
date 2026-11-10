using System;
using System.Windows;
using CameraShow.Services;

namespace CameraShow
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 记录启动信息
            LoggerService.Info("=== CameraShow 启动 ===");

            // WPF UI 线程未处理异常
            DispatcherUnhandledException += (s, args) =>
            {
                LoggerService.Error($"WPF UI 线程未处理异常：{args.Exception.Message}", args.Exception);
                try
                {
                    MessageBox.Show($"程序发生错误：{args.Exception.Message}\n\n详情请查看 Logs 文件夹中的日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { /* ignore */ }
                args.Handled = true;
            };

            // 后台线程未处理异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LoggerService.Error($"后台线程未处理异常：{ex?.Message ?? args.ExceptionObject?.ToString()}", ex);
                try
                {
                    MessageBox.Show($"后台线程发生错误：{ex?.Message ?? args.ExceptionObject?.ToString()}\n\n详情请查看 Logs 文件夹中的日志文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { /* ignore */ }
            };
        }
    }
}