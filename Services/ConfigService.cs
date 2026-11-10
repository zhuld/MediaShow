using System;
using System.IO;
using CameraShow.Models;
using Newtonsoft.Json;
using CameraShow.Services;

namespace CameraShow.Services
{
    public class ConfigService
    {
        private readonly string _configPath;
        private readonly object _lock = new();

        public AppConfig Config { get; private set; } = new();

        public ConfigService(string configPath = "config.json")
        {
            _configPath = Path.GetFullPath(configPath);
        }

        public AppConfig Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_configPath))
                {
                    LoggerService.Info($"配置文件不存在，将生成默认配置：{_configPath}");
                    Config = new AppConfig();
                    Save(Config);
                    return Config;
                }

                try
                {
                    var json = File.ReadAllText(_configPath);
                    Config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    LoggerService.Info($"配置文件加载成功：{_configPath}");
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"配置文件加载失败：{_configPath}", ex);
                    Config = new AppConfig();
                }

                // 确保基本字段不为空
                if (string.IsNullOrWhiteSpace(Config.PcName)) Config.PcName = "PC_1";
                if (Config.UdpPort <= 0) Config.UdpPort = 30000;
                if (Config.CameraSources == null) Config.CameraSources = new();
                if (Config.Modes == null) Config.Modes = new();

                return Config;
            }
        }

        public bool Save(AppConfig config)
        {
            lock (_lock)
            {
                Config = config ?? Config;
                try
                {
                    var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                    File.WriteAllText(_configPath, json);
                    return true;
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"配置文件保存失败：{_configPath}", ex);
                    return false;
                }
            }
        }

        public bool Save() => Save(Config);
    }
}