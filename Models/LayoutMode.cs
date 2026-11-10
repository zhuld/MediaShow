namespace CameraShow.Models
{
    /// <summary>
    /// 显示布局模式（已取消画中画，仅保留单窗口和左右窗口）
    /// </summary>
    public enum LayoutMode
    {
        Single,        // 单窗口：只显示一个摄像头，铺满容器
        LeftRight      // 左右窗口：两个摄像头左右平分容器
    }
}