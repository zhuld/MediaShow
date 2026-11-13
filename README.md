# MediaShow

MediaShow 是一款运行于 Windows 的 WPF 多媒体显示程序。它使用 DirectShow 扫描本机摄像头，可在同一布局中显示摄像头画面、播放本地视频、轮播本地图片，并可通过系统托盘、UDP 指令或局域网网页进行控制。

## 功能一览

- 启动时扫描本机摄像头，并按配置显示默认布局。
- 支持自定义布局、窗口尺寸和多个视图；每个布局当前最多显示前两个视图。
- 支持从指定目录播放视频：视图可选择摄像头或视频源，目录及子文件夹中的视频会按顺序循环播放，可配置随机顺序，每个视图可单独调节音量。
- 支持从指定目录轮播图片：视图可选择摄像头、视频源或图片源，目录及子文件夹中的图片会按视图设置的间隔循环或随机轮播，并提供淡入淡出、滑动、缩放、旋转等 5 种切换特效，无需音频解码器。
- 常驻系统托盘，可切换布局、打开设置、隐藏画面或退出程序。
- 提供桌面设置窗口和可选的局域网网页设置页面，保存后立即应用。
- 支持按计算机名和 UDP 端口接收布局切换指令。
- 根据当前显示器 DPI 将配置的物理像素尺寸换算为 WPF 尺寸。

## 运行要求

- Windows
- .NET Framework 4.8 或更高版本
- x64 环境
- 支持 DirectShow 的摄像头（使用摄像头功能时）
- Media Foundation（Windows 7 及以上默认包含；Windows N 版需安装 Media Feature Pack，用于视频播放和相应的解码器）

## 构建与启动

在仓库根目录运行：

```powershell
dotnet restore .\MediaShow.sln
dotnet build .\MediaShow.sln -c Release
```

构建完成后运行：

```powershell
.\bin\Release\net48\MediaShow.exe
```

也可以在 Visual Studio 中打开 `MediaShow.sln` 并生成 Release 配置。目标电脑需要安装 .NET Framework 4.8 或更高版本；该框架不会随程序打包。

程序使用 EXE 所在目录的 `config.json`。如果文件不存在，首次启动时会生成默认配置。发布时如需保留已有设置，请将对应的 `config.json` 放在 EXE 同目录。

## 使用方法

程序启动后会在通知区域显示托盘图标。右键托盘图标可使用以下操作：

| 菜单项 | 说明 |
| --- | --- |
| 配置的布局名称 | 切换到对应布局 |
| `Blank` | 清空所有画面，显示黑屏 |
| `Desktop_Show` | 隐藏程序窗口以显示桌面；再次选择显示布局或双击托盘图标可恢复 |
| `Shutdown` | 请求 Windows 立即关机，系统可能会要求提升权限 |
| `Settings...` | 打开设置窗口 |
| `Exit` | 退出程序并释放摄像头和网络服务 |

按 `Esc` 可关闭主窗口并退出程序。

### 桌面设置

在托盘菜单中选择 **Settings...**。设置分为以下内容：

| 设置 | 说明 |
| --- | --- |
| 基本设置 | 计算机名、UDP 端口、网页设置端口、默认模式和窗口宽高 |
| 布局模式 | 新增或删除布局，设置布局名称及每个视图的来源类型（摄像头 / 视频 / 图片）、来源、宽高、拉伸方式和音量/间隔 |
| 摄像头 | 查看本机扫描到的摄像头清单 |
| 视频源 | 新增、删除视频源，设置视频目录、扩展名、循环和随机顺序 |
| 图片源 | 新增、删除图片源，设置图片目录、扩展名、切换特效、循环和随机顺序；翻页间隔在「布局模式」的「音量/间隔」列按视图设置 |

点击「保存并应用」后配置会写入 `config.json` 并立即生效。修改计算机名或 UDP 端口会重启 UDP 服务；修改网页端口会重启网页设置服务。网页端口设为 `0` 时会关闭该服务。

## 配置文件

`config.json` 使用 JSON 格式。主要字段如下：

| 字段 | 含义 |
| --- | --- |
| `PcName` | 本机 UDP 控制名称，指令中用于匹配目标；不能包含连字符 `-` |
| `UdpPort` | UDP 控制监听端口，范围为 `1`–`65535` |
| `WebPort` | 网页设置端口，范围为 `0`–`65535`；`0` 表示关闭 |
| `DefaultMode` | 启动时使用的布局名称，也可以是内置模式 `Blank` 或 `Desktop_Show`（旧配置 `Desktop` 仍兼容） |
| `WindowWidth`、`WindowHeight` | 窗口宽高，单位为物理像素；任一值为 `0` 时窗口最大化 |
| `ShowModeStatusBar` | 是否在模式切换后短暂显示窗口底部的模式状态栏；默认 `true` |
| `CameraSources` | 本机摄像头清单，包含 ID、名称、索引和分辨率 |
| `VideoSources` | 视频源清单，每项包含 `Id`、`Name`、`FolderPath`、`Extensions`、`Loop`、`Shuffle` |
| `ImageSources` | 图片源清单，每项包含 `Id`、`Name`、`FolderPath`、`Extensions`、`Transition`、`Loop`、`Shuffle` |
| `Modes` | 布局列表；每个布局包含名称 `Mode` 和视图数组 `Views` |
| `Views[].SourceType` | 视图来源类型：`Camera`（默认）、`Video` 或 `Image` |
| `Views[].SourceId` | 视图关联的来源 ID，按 `SourceType` 解释：摄像头 ID、视频源 ID 或图片源 ID |
| `Views[].W`、`Views[].H` | 视图宽高，单位为物理像素，必须大于 `0` |
| `Views[].Stretch` | 画面拉伸方式：留空为自动（摄像头填充、视频与图片等比适应），可选 `Fill`、`Uniform`、`UniformToFill`、`None` |
| `Views[].Volume` | 视图音量，`0`～`100` 的百分比，`0` 表示静音；仅对视频视图生效，默认 `100`。图片视图复用该值作为翻页间隔（秒，`1`～`100`），不表示音量 |

程序每次启动都会重新扫描摄像头并覆盖 `CameraSources`，同时修正摄像头视图中不存在的来源 ID。因此，不要依赖手动维护的摄像头清单；如更换设备，请在设置窗口中确认扫描结果和布局关联。

旧版配置中按类型拆分的 `Views[].CameraId`、`Views[].VideoId`、`Views[].ImageId` 仍可读取，程序会自动合并到 `SourceId`，保存后只写出 `SourceId`。

示例：

```json
{
  "PcName": "PC_1",
  "UdpPort": 30000,
  "WebPort": 8000,
  "DefaultMode": "Full",
  "WindowWidth": 1280,
  "WindowHeight": 720,
  "CameraSources": [
    {
      "Id": "cam_0",
      "Name": "Integrated Camera",
      "Index": 0,
      "Width": 1920,
      "Height": 1080
    }
  ],
  "VideoSources": [
    {
      "Id": "vid_1",
      "Name": "宣传片",
      "FolderPath": "D:\\Videos\\Show",
      "Extensions": ".mp4;.avi;.wmv",
      "Loop": true,
      "Shuffle": false
    }
  ],
  "ImageSources": [
    {
      "Id": "img_1",
      "Name": "宣传图",
      "FolderPath": "D:\\Pictures\\Show",
      "Extensions": ".jpg;.png",
      "Transition": "Fade",
      "Loop": true,
      "Shuffle": false
    }
  ],
  "Modes": [
    {
      "Mode": "Full",
      "Views": [
        {
          "SourceType": "Camera",
          "SourceId": "cam_0",
          "W": 1280,
          "H": 720
        }
      ]
    },
    {
      "Mode": "Mixed",
      "Views": [
        {
          "SourceType": "Camera",
          "SourceId": "cam_0",
          "W": 640,
          "H": 480
        },
        {
          "SourceType": "Video",
          "SourceId": "vid_1",
          "W": 640,
          "H": 480,
          "Stretch": "Uniform",
          "Volume": 50
        }
      ]
    },
    {
      "Mode": "Gallery",
      "Views": [
        {
          "SourceType": "Image",
          "SourceId": "img_1",
          "W": 1280,
          "H": 720,
          "Stretch": "Uniform",
          "Volume": 10
        }
      ]
    }
  ]
}
```

`Views` 可以配置一个或多个视图，但目前最多显示前两个：一个视图居中显示，两个视图左右排列。视图尺寸和窗口尺寸分别控制画面与程序窗口的大小。

上例中 `Gallery` 视图的 `Volume` 为 `10`，表示图片每 10 秒翻页一次；`Mixed` 视图的 `Volume` 为 `50`，表示视频音量为 50%。

## 视频播放

在「布局模式」中把视图类型设为「视频」并选择视频源，即可在摄像头画面旁或整屏播放本地视频：

- 视频源指向一个目录，程序会递归扫描其子文件夹，按 `Extensions` 中配置的扩展名（分号、逗号或空格分隔，如 `.mp4;.avi`）收集文件。
- 默认按文件名顺序播放，`Loop` 为 `true` 时播完最后一个后从头继续；`Shuffle` 为 `true` 时随机顺序播放。
- 使用 Media Foundation 逐帧解码后绘制到 WPF 画面，不依赖 Windows Media Player。
- 音轨通过 Media Foundation 解码后由系统音频设备输出；音量按视图的 `Volume`（0～100）调节，多个视图各自独立，`0` 为静音。文件没有音轨时会自动静音播放。
- 画面拉伸方式按视图的 `Stretch` 调节：`Fill` 拉伸填满（可能变形）、`Uniform` 等比适应（可能留黑边）、`UniformToFill` 等比裁剪填满、`None` 原始大小；留空则摄像头用 `Fill`、视频用 `Uniform`。
- 切换布局、切换到 `Blank` 或退出程序时会停止播放并释放解码线程。

能否解码取决于系统已安装的 Media Foundation 解码器：mp4/H.264、wmv 通常可直接播放，mkv/mov 等格式一般不支持。无法播放的文件会记录到日志并自动跳过；目录为空或全部文件都无法播放时，视图内会显示原因。视频画面默认按等比缩放显示（可能留黑边），可通过 `Stretch` 调整。

## 图片播放

在「布局模式」中把视图类型设为「图片」并选择图片源，即可与摄像头或视频画面并排、或整屏轮播本地图片：

- 图片源指向一个目录，程序会递归扫描其子文件夹，按 `Extensions` 中配置的扩展名（分号、逗号或空格分隔，如 `.jpg;.png`）收集文件。
- 翻页间隔由视图的 `Volume` 值决定（秒，`1`～`100`），在设置窗口/网页的「音量/间隔」列设置；图片视图不使用音量。
- 切换特效在图片源中设置（`Transition`，对应设置窗口/网页「图片源」页的「特效」列），可选 `Fade`（淡入淡出，默认）、`Slide`（横向滑动）、`SlideUp`（纵向滑动）、`Zoom`（缩放）、`Rotate`（旋转），时长约 0.45 秒。
- `Loop` 为 `true` 时最后一张之后从第一张继续，`Shuffle` 为 `true` 时随机顺序轮播。
- 图片在后台线程解码，读取完成后立即释放文件句柄，因此轮播期间可以替换或删除图片文件；下次轮播到该文件时才会重新读取。
- 使用 WPF 内置的图片解码（WIC），jpg / png / bmp / gif 等常见格式无需额外解码器；GIF 只显示第一帧，不播放动画。
- 画面拉伸方式按视图的 `Stretch` 调节，留空时按等比适应（`Uniform`）显示。
- 无法读取的文件会记录到日志并自动跳过；目录为空或全部文件都无法读取时，视图内会显示原因。`Loop` 关闭时，最后一张图片会保持显示。

## 局域网网页设置

网页服务默认使用 `8000` 端口。确保设备之间网络可达，并允许 Windows 防火墙接收入站 TCP 连接后，在同一可信局域网的浏览器中访问：

```text
http://<运行 MediaShow 的电脑 IP>:8000/
```

本机也可以访问 `http://localhost:8000/`。页面可编辑基本设置、布局、视频源和图片源，提交后会校验、保存并立即应用。
页面顶部的「实时模式控制」可直接切换已配置布局以及 `Blank`、`Desktop_Show`，不会修改启动默认模式。

服务接口（供设置页面使用）：

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/` 或 `/index.html` | 设置页面 |
| `GET` | `/api/config` | 获取当前配置 |
| `GET` | `/api/status` | 获取窗口尺寸、DPI、显示状态和当前模式 |
| `POST` | `/api/config` | 提交完整配置并应用；成功返回 `{ "ok": true }`，校验失败返回 HTTP 400 和 `errors` 列表 |
| `POST` | `/api/mode` | 即时切换模式；请求正文为 `{ "Mode": "模式名称" }` |

**安全提示：**网页设置服务监听所有网络接口，且没有身份验证。能够连接到该端口的设备可以读取和修改配置。仅在可信网络中启用；不要将端口暴露到互联网。不需要远程设置时，将 `WebPort` 设为 `0`。

## UDP 控制

程序监听 `UdpPort`，使用 UTF-8 文本指令选择布局。消息格式为：

```text
<PcName>-<模式名称>
```

例如，若配置为 `"PcName": "PC_1"`、`"UdpPort": 30000`，发送 `PC_1-Full` 即可切换到 `Full` 布局；也可发送 `PC_1-Blank`、`PC_1-Desktop_Show` 或 `PC_1-Shutdown` 关机。发送方应使用 UDP 向运行程序的电脑及对应端口发送消息。计算机名和 `Shutdown` 指令比较不区分大小写，布局名称需与配置一致。

## 日志与项目结构

运行日志写入 EXE 所在目录下的 `Logs` 文件夹。

```text
MediaShow/
├── App.xaml(.cs)                 # WPF 应用入口及退出清理
├── MainWindow.xaml(.cs)          # 主窗口、视图布局和应用生命周期
├── SettingsWindow.xaml(.cs)      # 桌面设置窗口
├── TrayIcon.cs                   # 系统托盘图标与菜单
├── Controls/CameraView.xaml(.cs) # 摄像头画面控件
├── Controls/VideoView.xaml(.cs)  # 视频播放控件
├── Controls/ImageView.xaml(.cs)  # 图片轮播控件
├── Models/                       # 配置和视图模型
├── Services/                     # 摄像头、视频/图片播放列表、配置、UDP、网页和日志服务
├── Resources/server.ico          # 应用及托盘图标
├── Web/WebPanel.html             # 内嵌网页设置界面
├── config.json                   # 本机配置
└── MediaShow.csproj              # 项目与依赖配置
```

Release 构建通过 Costura.Fody 将第三方托管依赖嵌入 EXE；构建目标会从输出目录移除 DLL 和 EXE 配置文件。发布前请在目标电脑上验证 EXE、.NET Framework 和摄像头驱动兼容性。

## 技术栈

- WPF / .NET Framework 4.8
- AForge.Video.DirectShow：摄像头发现与捕获
- Media Foundation（`mfreadwrite.dll`）：本地视频/音频解码，不依赖 Windows Media Player
- WPF 属性动画：图片切换特效（淡入淡出 / 滑动 / 缩放 / 旋转）
- NAudio：音轨输出与音量控制
- Newtonsoft.Json：配置序列化
- Costura.Fody：托管依赖打包
- Windows Forms `NotifyIcon`：系统托盘图标

## 版本与说明

当前版本：1.2.0（新增图片轮播与 5 种切换特效；视图来源统一为 `SourceId`，兼容旧配置）

本程序由 AI 辅助开发，使用开源的 AForge.Video.DirectShow 库进行摄像头捕获，并通过 WPF 实现用户界面。
