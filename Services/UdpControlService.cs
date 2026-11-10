using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CameraShow.Models;
using CameraShow.Services;

namespace CameraShow.Services
{
    /// <summary>
    /// UDP 网络控制服务：监听指定端口，解析 "PC_1-Cmd" 指令，触发布局模式切换。
    /// </summary>
    public class UdpControlService : IDisposable
    {
        private readonly string _pcName;
        private readonly int _port;
        private UdpClient? _udpClient;
        private Thread? _listenThread;
        private volatile bool _running;

        public event Action<string>? ModeChanged;

        public UdpControlService(string pcName, int port)
        {
            _pcName = pcName;
            _port = port;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "UdpControl" };
            _listenThread.Start();
            LoggerService.Info($"UDP 控制服务启动，监听端口：{_port}，计算机名：{_pcName}");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            // 先关闭 UdpClient，使阻塞的 Receive 抛出 ObjectDisposedException，后台线程可据此退出
            try { _udpClient?.Close(); } catch { /* ignore */ }

            _listenThread?.Join(TimeSpan.FromSeconds(3));
            LoggerService.Info("UDP 控制服务已停止");
        }

        private void ListenLoop()
        {
            try
            {
                // 先创建不绑定端口的 UdpClient，以便设置 SO_REUSEADDR，
                // 避免“每个套接字地址只允许使用一次”的绑定失败
                _udpClient = new UdpClient
                {
                    EnableBroadcast = true
                };

                _udpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _port));

                while (_running)
                {
                    bool hasData = false;
                    try
                    {
                        hasData = _udpClient.Client.Available > 0 || _udpClient.Client.Poll(100, SelectMode.SelectRead);
                    }
                    catch (ObjectDisposedException)
                    {
                        // UdpClient 已被关闭，正常退出
                        break;
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    if (!hasData)
                    {
                        continue;
                    }

                    IPEndPoint remoteEp = new(IPAddress.Any, _port);
                    byte[] bytes;
                    try
                    {
                        bytes = _udpClient.Receive(ref remoteEp);
                    }
                    catch (ObjectDisposedException)
                    {
                        // UdpClient 已被关闭，正常退出
                        break;
                    }
                    catch (NullReferenceException ex)
                    {
                        if (!_running) break;

                        LoggerService.Error("UDP 接收异常", ex);
                        _running = false;
                        break;
                    }
                    catch (SocketException)
                    {
                        if (!_running) break;
                        continue;
                    }

                    if (bytes.Length == 0) continue;

                    var msg = Encoding.UTF8.GetString(bytes).Trim();
                    LoggerService.Info($"收到 UDP 指令：{msg} 来自 {remoteEp.Address}");
                    ProcessCommand(msg);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("UDP 监听异常", ex);
                _running = false;
            }
        }

        private void ProcessCommand(string msg)
        {
            // 期望格式：PC_1-ModeName（ModeName 与配置中的 Modes[].Mode 对应）
            var idx = msg.IndexOf('-');
            if (idx <= 0) return;

            var name = msg.Substring(0, idx);
            var cmd = msg.Substring(idx + 1);

            if (!string.Equals(name, _pcName, StringComparison.OrdinalIgnoreCase))
            {
                LoggerService.Warn($"UDP 指令忽略：计算机名不匹配，期望 {_pcName}，实际 {name}");
                return;
            }

            LoggerService.Info($"收到 UDP 模式指令：{cmd}");
            ModeChanged?.Invoke(cmd);
        }

        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}