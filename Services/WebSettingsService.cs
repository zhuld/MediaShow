using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using MediaShow.Models;
using Newtonsoft.Json;

namespace MediaShow.Services
{
    /// <summary>
    /// 内嵌的远程网页设置服务：在指定端口（默认 8000）提供设置页面与配置接口。
    /// 使用 TcpListener 自行解析最小的 HTTP 协议，避免 HttpListener 需要管理员权限或 URL ACL 的问题，
    /// 可直接从局域网内其他设备访问。
    /// </summary>
    public class WebSettingsService : IDisposable
    {
        private const int MaxRequestBytes = 2 * 1024 * 1024;
        private const string PageResourceName = "MediaShow.Web.WebPanel.html";

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private static string? _pageCache;

        private readonly int _port;
        private readonly Func<AppConfig> _getConfig;
        private readonly Func<AppConfig, bool> _applyConfig;
        private readonly Func<string, bool> _applyMode;
        private readonly Func<WindowStatus?>? _getWindowStatus;
        private TcpListener? _listener;
        private Thread? _acceptThread;
        private volatile bool _running;

        public int Port => _port;

        public bool IsRunning => _running;

        /// <summary>
        /// </summary>
        /// <param name="port">监听端口，小于等于 0 表示不启用</param>
        /// <param name="getConfig">返回当前配置快照（应保证线程安全）</param>
        /// <param name="applyConfig">应用并保存新配置，返回是否成功</param>
        /// <param name="applyMode">切换到指定模式，返回是否成功</param>
        /// <param name="getWindowStatus">返回主窗口当前实际尺寸，供网页展示</param>
        public WebSettingsService(
            int port,
            Func<AppConfig> getConfig,
            Func<AppConfig, bool> applyConfig,
            Func<string, bool> applyMode,
            Func<WindowStatus?>? getWindowStatus = null)
        {
            _port = port;
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
            _applyConfig = applyConfig ?? throw new ArgumentNullException(nameof(applyConfig));
            _applyMode = applyMode ?? throw new ArgumentNullException(nameof(applyMode));
            _getWindowStatus = getWindowStatus;
        }

        public void Start()
        {
            if (_running || _port <= 0) return;

            var listener = new TcpListener(IPAddress.Any, _port);
            // 端口被上一次的停止流程短暂占用（TIME_WAIT）时允许重新绑定
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();

            _listener = listener;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WebSettings" };
            _acceptThread.Start();

            LoggerService.Info($"网页设置服务已启动：端口 {_port}（http://<本机IP>:{_port}/）");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { _listener?.Stop(); } catch { /* ignore */ }

            _acceptThread?.Join(TimeSpan.FromSeconds(2));
            LoggerService.Info("网页设置服务已停止");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener!.AcceptTcpClient();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }
                catch (Exception ex)
                {
                    LoggerService.Error("网页设置服务接收连接失败", ex);
                    break;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;

                    var stream = client.GetStream();
                    if (!TryReadRequest(stream, out var method, out var rawUrl, out var body))
                    {
                        WriteJson(stream, 400, new { ok = false, message = "无法解析请求。" });
                        return;
                    }

                    var path = rawUrl.Split('?')[0].TrimEnd('/');
                    if (path.Length == 0) path = "/";

                    if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        if (path == "/" || path == "/index.html")
                        {
                            WriteResponse(stream, 200, "OK", "text/html; charset=utf-8", LoadPage());
                            return;
                        }

                        if (path == "/api/config")
                        {
                            var config = _getConfig();
                            WriteResponse(stream, 200, "OK", "application/json; charset=utf-8",
                                JsonConvert.SerializeObject(config, JsonSettings));
                            return;
                        }

                        if (path == "/api/status")
                        {
                            var status = _getWindowStatus?.Invoke();
                            if (status == null)
                            {
                                WriteJson(stream, 503, new { ok = false, message = "窗口状态不可用。" });
                            }
                            else
                            {
                                WriteResponse(stream, 200, "OK", "application/json; charset=utf-8",
                                    JsonConvert.SerializeObject(status, JsonSettings));
                            }

                            return;
                        }

                        if (path == "/favicon.ico")
                        {
                            WriteResponse(stream, 204, "No Content", "text/plain; charset=utf-8", string.Empty);
                            return;
                        }
                    }
                    else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                             && path == "/api/config")
                    {
                        HandleUpdateConfig(stream, body);
                        return;
                    }
                    else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                             && path == "/api/mode")
                    {
                        HandleModeChange(stream, body);
                        return;
                    }

                    WriteJson(stream, 404, new { ok = false, message = "未找到该地址。" });
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("处理网页设置请求失败", ex);
            }
        }

        private void HandleUpdateConfig(NetworkStream stream, string body)
        {
            AppConfig? config;
            try
            {
                config = JsonConvert.DeserializeObject<AppConfig>(body);
            }
            catch (JsonException ex)
            {
                WriteJson(stream, 400, new { ok = false, errors = new[] { $"配置格式不正确：{ex.Message}" } });
                return;
            }

            if (config == null)
            {
                WriteJson(stream, 400, new { ok = false, errors = new[] { "配置内容为空。" } });
                return;
            }

            ConfigValidator.Normalize(config);

            var errors = ConfigValidator.Validate(config);
            if (errors.Count > 0)
            {
                WriteJson(stream, 400, new { ok = false, errors });
                return;
            }

            if (!_applyConfig(config))
            {
                WriteJson(stream, 500, new { ok = false, errors = new[] { "配置已提交，但保存失败，请查看程序 Logs 目录中的日志。" } });
                return;
            }

            LoggerService.Info($"网页设置已应用：PcName={config.PcName}, UdpPort={config.UdpPort}, WebPort={config.WebPort}, 模式数={config.Modes.Count}");
            WriteJson(stream, 200, new { ok = true, message = "配置已保存并生效。" });
        }

        private void HandleModeChange(NetworkStream stream, string body)
        {
            ModeControlRequest? request;
            try
            {
                request = JsonConvert.DeserializeObject<ModeControlRequest>(body);
            }
            catch (JsonException ex)
            {
                WriteJson(stream, 400, new { ok = false, message = $"模式指令格式不正确：{ex.Message}" });
                return;
            }

            var modeName = request?.Mode?.Trim();
            if (modeName == null || modeName.Length == 0)
            {
                WriteJson(stream, 400, new { ok = false, message = "模式名称不能为空。" });
                return;
            }

            var config = _getConfig();
            var isConfigured = config.Modes != null
                && config.Modes.Any(mode =>
                    mode != null && string.Equals(mode.Mode, modeName, StringComparison.OrdinalIgnoreCase));
            if (!ConfigValidator.IsBuiltInMode(modeName) && !isConfigured)
            {
                WriteJson(stream, 400, new { ok = false, message = $"未找到模式“{modeName}”。" });
                return;
            }

            if (!_applyMode(modeName))
            {
                WriteJson(stream, 503, new { ok = false, message = "模式切换失败，程序可能正在关闭。" });
                return;
            }

            WriteJson(stream, 200, new { ok = true, mode = modeName, message = $"已切换到“{modeName}”。" });
        }

        private sealed class ModeControlRequest
        {
            public string? Mode { get; set; }
        }

        /// <summary>
        /// 读取并解析一个完整的 HTTP 请求（请求行 + 头部 + 可选正文）
        /// </summary>
        private static bool TryReadRequest(NetworkStream stream, out string method, out string rawUrl, out string body)
        {
            method = string.Empty;
            rawUrl = string.Empty;
            body = string.Empty;

            var buffer = new byte[8192];
            using var received = new MemoryStream();
            var headerEnd = -1;

            while (received.Length < MaxRequestBytes)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                received.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
                if (headerEnd >= 0) break;
            }

            if (headerEnd < 0) return false;

            var data = received.GetBuffer();
            var headerText = Encoding.UTF8.GetString(data, 0, headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return false;

            method = requestLine[0];
            rawUrl = requestLine[1];

            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                if (line.Substring(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line.Substring(colon + 1).Trim(), out contentLength);
                }
            }

            if (contentLength < 0 || contentLength > MaxRequestBytes) return false;

            var bodyStart = headerEnd + 4;
            var available = (int)received.Length - bodyStart;
            var bodyBytes = new byte[contentLength];
            var copied = Math.Min(available, contentLength);

            if (copied > 0)
            {
                Array.Copy(data, bodyStart, bodyBytes, 0, copied);
            }

            var offset = copied;
            while (offset < contentLength)
            {
                var read = stream.Read(bodyBytes, offset, contentLength - offset);
                if (read <= 0) break;
                offset += read;
            }

            body = offset > 0 ? Encoding.UTF8.GetString(bodyBytes, 0, offset) : string.Empty;
            return true;
        }

        private static int FindHeaderEnd(byte[] data, int length)
        {
            for (var i = 0; i + 3 < length; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static void WriteJson(NetworkStream stream, int statusCode, object payload)
        {
            WriteResponse(stream, statusCode, GetReason(statusCode), "application/json; charset=utf-8",
                JsonConvert.SerializeObject(payload, JsonSettings));
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string reason, string contentType, string content)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(content);
            var headers = new StringBuilder()
                .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n")
                .Append("Content-Type: ").Append(contentType).Append("\r\n")
                .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
                .Append("Cache-Control: no-store\r\n")
                .Append("Connection: close\r\n\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private static string GetReason(int statusCode) => statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "OK"
        };

        private static string LoadPage()
        {
            if (_pageCache != null) return _pageCache;

            try
            {
                using var stream = typeof(WebSettingsService).Assembly.GetManifestResourceStream(PageResourceName);
                if (stream == null)
                {
                    LoggerService.Error($"未找到网页资源：{PageResourceName}");
                    _pageCache = "<!DOCTYPE html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><h1>网页设置资源缺失</h1></html>";
                    return _pageCache;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                _pageCache = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载网页设置页面失败", ex);
                _pageCache = "<!DOCTYPE html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><h1>网页设置页面加载失败</h1></html>";
            }

            return _pageCache;
        }

        public void Dispose()
        {
            Stop();
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
            GC.SuppressFinalize(this);
        }
    }
}
