namespace CameraShow.Models
{
    /// <summary>
    /// 保留以兼容旧配置，当前不再使用。摄像头显示窗口的尺寸与位置由 ViewGroup / ViewItem 定义。
    /// </summary>
    public class WindowLayout
    {
        public string Id { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public double X { get; set; } = 100;
        public double Y { get; set; } = 100;
        public double W { get; set; } = 1280;
        public double H { get; set; } = 720;
        public int ZIndex { get; set; } = 0;
    }
}