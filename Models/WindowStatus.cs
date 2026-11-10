namespace MediaShow.Models
{
    /// <summary>
    /// 运行中主窗口的实际尺寸与状态，供设置对话框与网页设置页面展示。
    /// DIP 为 WPF 逻辑单位，像素为按当前 DPI 缩放换算后的物理尺寸。
    /// </summary>
    public class WindowStatus
    {
        public double WidthDip { get; set; }
        public double HeightDip { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public double DpiScaleX { get; set; } = 1;
        public double DpiScaleY { get; set; } = 1;
        public bool IsMinimized { get; set; }
        public string CurrentMode { get; set; } = string.Empty;
    }
}
