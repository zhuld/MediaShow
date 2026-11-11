using System.Collections.Generic;

namespace MediaShow.Models
{
    /// <summary>
    /// 应用程序完整配置，持久化到 config.json
    /// </summary>
    public class AppConfig
    {
        public string PcName { get; set; } = "PC_1";
        public int UdpPort { get; set; } = 30000;
        public int WebPort { get; set; } = 8000;
        public string DefaultMode { get; set; } = "Single";
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;
        public bool ShowModeStatusBar { get; set; } = true;
        public List<CameraSource> CameraSources { get; set; } = new();
        public List<ViewGroup> Modes { get; set; } = new();
    }
}