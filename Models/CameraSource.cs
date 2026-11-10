namespace MediaShow.Models
{
    /// <summary>
    /// 摄像头源信息
    /// </summary>
    public class CameraSource
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Index { get; set; } = 0;          // 本地摄像头索引
        public int Width { get; set; } = 0;          // 分辨率宽度
        public int Height { get; set; } = 0;         // 分辨率高度
    }
}