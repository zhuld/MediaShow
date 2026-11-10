using System.Collections.Generic;

namespace CameraShow.Models
{
    /// <summary>
    /// 应用程序完整配置，持久化到 config.json
    /// </summary>
    public class AppConfig
    {
        public string PcName { get; set; } = "PC_1";
        public int UdpPort { get; set; } = 30000;
        public string DefaultMode { get; set; } = "Single";
        public List<CameraSource> CameraSources { get; set; } = new();
        public List<ViewGroup> Modes { get; set; } = new();
    }
}