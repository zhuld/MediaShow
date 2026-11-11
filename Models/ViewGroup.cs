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
    /// 单个子视图：来源为摄像头（CameraId）或视频源（VideoId），以及宽高
    /// </summary>
    public class ViewItem
    {
        /// <summary>来源类型：Camera（默认）或 Video</summary>
        public string SourceType { get; set; } = "Camera";
        public string CameraId { get; set; } = string.Empty;
        public string VideoId { get; set; } = string.Empty;
        public double W { get; set; } = 640;
        public double H { get; set; } = 480;

        /// <summary>音量百分比（0～100），0 表示静音；仅对视频视图生效</summary>
        public double Volume { get; set; } = 100;
    }
}