namespace MediaShow.Models
{
    /// <summary>
    /// 图片源：指向一个本地目录，程序会递归扫描其中的图片文件并按间隔轮播。
    /// </summary>
    public class ImageSource
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Extensions { get; set; } = ".jpg;.jpeg;.png;.bmp;.gif";
        public bool Loop { get; set; } = true;
        public bool Shuffle { get; set; }

        /// <summary>
        /// 切换特效：Fade（淡入淡出，默认）、Slide（横向滑动）、SlideUp（纵向滑动）、Zoom（缩放）、Rotate（旋转）
        /// </summary>
        public string Transition { get; set; } = "Fade";
    }
}
