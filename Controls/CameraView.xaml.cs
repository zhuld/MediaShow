using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CameraShow.Controls
{
    /// <summary>
    /// 单个摄像头显示控件（不再支持鼠标拖动和缩放，尺寸位置由配置决定）
    /// </summary>
    public partial class CameraView : Grid
    {
        public string ViewId { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;

        public CameraView()
        {
            InitializeComponent();
        }

        public void SetImage(BitmapSource? source)
        {
            PART_Image.Source = source;
        }
    }
}