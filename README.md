# CameraShow

## 摄像头显示应用

### 项目简介
CameraShow 是一个基于 .NET Framework 4.7.2 的 WPF 桌面应用程序，用于显示和管理本地摄像头视频流。

### 主要功能
- 自动扫描本地可用摄像头
- 多种布局模式切换
- 系统托盘运行（最小化到任务栏右下角）
- 可自定义的视图布局
- 实时摄像头帧显示

### 架构
- **前端**: WPF (Windows Presentation Foundation)
- **摄像头捕获**: AForge.Video.DirectShow
- **配置管理**: JSON 配置文件
- **网络通信**: UDP 协议

### 快速开始

#### 先决条件
- .NET Framework 4.7.2 或更高版本
- 支持 DirectShow 的本地摄像头设备

#### 运行应用
1. 克隆或下载项目
2. 还原 NuGet 依赖: `dotnet restore`
3. 构建项目: `dotnet build`
4. 运行: `dotnet run` 或直接执行 `CameraShow.exe`

### 项目结构
```
CameraShow/
├── MainWindow.xaml        # 主窗口 UI
├── MainWindow.xaml.cs     # 主窗口逻辑
├── CameraShow.csproj      # 项目配置
├── config.json            # 配置文件
├── bin/                   # 构建输出目录
└── Resources/             # 资源文件夹 (图标等)
```

### 自定义
- 修改 `config.json` 调整摄像头配置
- `config.json` 会在构建时复制到输出目录（仅当项目中的文件更新时才覆盖，避免丢失运行期生成的配置）
- 自定义布局模式在 `Models/Modes` 中定义
- 图标可在 `Resources` 文件夹中替换

### 构建说明
项目使用 `Microsoft.NET.Sdk.WindowsDesktop` SDK 目标 .NET Framework 4.7.2。
如需添加自定义图标，请将 .ico 文件添加到 `Resources` 文件夹并在 XAML 中引用。

### 版本
当前版本: 1.0.0

### 联系方式
- GitHub: [your-repository-url]
- 问题: [issue-tracker-url]