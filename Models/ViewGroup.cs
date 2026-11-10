using System.Collections.Generic;

namespace MediaShow.Models
{
    /// <summary>
    /// 视图分组：每个分组包含一个 Mode 以及一个或多个子视图
    /// </summary>
    public class ViewGroup
    {
        public string Mode { get; set; } = "Single";
        public List<ViewItem> Views { get; set; } = new();
    }

    /// <summary>
    /// 单个子视图：包含 CameraId 以及宽高
    /// </summary>
    public class ViewItem
    {
        public string CameraId { get; set; } = string.Empty;
        public double W { get; set; } = 640;
        public double H { get; set; } = 480;
    }
}