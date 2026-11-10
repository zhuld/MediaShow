using System;
using System.IO;
using MediaShow.Models;
using Newtonsoft.Json;
using MediaShow.Services;

namespace MediaShow.Services
{
    public class ConfigService
    {
        private readonly string _configPath;
        private readonly object _lock = new();

        public AppConfig Config { get; private set; } = new();

        /// <summary>配置文件完整路径</summary>
        public string ConfigPath => _configPath;

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
                    BackupInvalidConfig();
                    Config = new AppConfig();
                }

                // 确保基本字段不为空
                if (string.IsNullOrWhiteSpace(Config.PcName)) Config.PcName = "PC_1";
                if (Config.UdpPort <= 0 || Config.UdpPort > 65535) Config.UdpPort = 30000;
                if (Config.WebPort < 0 || Config.WebPort > 65535) Config.WebPort = 8000;
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

        /// <summary>
        /// 配置解析失败时先备份原文件，避免随后的自动保存把用户配置覆盖丢失
        /// </summary>
        private void BackupInvalidConfig()
        {
            try
            {
                var backupPath = _configPath + ".invalid";
                File.Copy(_configPath, backupPath, true);
                LoggerService.Warn($"已将无法解析的配置文件备份到：{backupPath}");
            }
            catch (Exception ex)
            {
                LoggerService.Error("备份无法解析的配置文件失败", ex);
            }
        }
    }
}