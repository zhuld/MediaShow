namespace MediaShow.Models
{
    /// <summary>
    /// 视频源：指向一个本地目录，程序会递归扫描其中的视频文件并按顺序播放。
    /// </summary>
    public class VideoSource
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Extensions { get; set; } = ".mp4;.avi;.wmv";
        public bool Loop { get; set; } = true;
        public bool Shuffle { get; set; }
    }
}
