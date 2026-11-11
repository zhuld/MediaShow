using System.Collections.Generic;
using Newtonsoft.Json;

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
    /// 单个子视图：来源 ID（含义由 SourceType 决定）以及宽高
    /// </summary>
    public class ViewItem
    {
        /// <summary>来源类型：Camera（默认）、Video 或 Image</summary>
        public string SourceType { get; set; } = "Camera";

        /// <summary>来源 ID：SourceType 为 Camera 时是摄像头 ID，Video 时是视频源 ID，Image 时是图片源 ID</summary>
        public string SourceId { get; set; } = string.Empty;

        public double W { get; set; } = 640;
        public double H { get; set; } = 480;

        /// <summary>画面拉伸方式：留空表示自动（摄像头填充、视频等比适应），可选 Fill / Uniform / UniformToFill / None</summary>
        public string Stretch { get; set; } = string.Empty;

        /// <summary>
        /// 音量百分比（0～100），0 表示静音；仅对视频视图生效。
        /// 图片视图复用该值作为翻页间隔（秒，1～100）。
        /// </summary>
        public double Volume { get; set; } = 100;

        // 兼容旧配置：来源 ID 曾经按类型拆分为三个字段，读取时迁移到 SourceId，写回时不再输出
        [JsonProperty("CameraId")]
        private string? LegacyCameraId { set => MigrateLegacyId(value); }

        [JsonProperty("VideoId")]
        private string? LegacyVideoId { set => MigrateLegacyId(value); }

        [JsonProperty("ImageId")]
        private string? LegacyImageId { set => MigrateLegacyId(value); }

        private void MigrateLegacyId(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || !string.IsNullOrWhiteSpace(SourceId)) return;

            SourceId = text;
        }
    }
}