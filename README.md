# MediaShow

MediaShow 是一款运行于 Windows 的 WPF 摄像头显示程序。它使用 DirectShow 扫描本机摄像头，将摄像头画面按可配置的布局显示在无边框窗口中，并可通过系统托盘、UDP 指令或局域网网页进行控制。

## 功能一览

- 启动时扫描本机摄像头，并按配置显示默认布局。
- 支持自定义布局、窗口尺寸和多个摄像头画面；每个布局当前最多显示前两个视图。
- 常驻系统托盘，可切换布局、打开设置、隐藏画面或退出程序。
- 提供桌面设置窗口和可选的局域网网页设置页面，保存后立即应用。
- 支持按计算机名和 UDP 端口接收布局切换指令。
- 根据当前显示器 DPI 将配置的物理像素尺寸换算为 WPF 尺寸。

## 运行要求

- Windows
- .NET Framework 4.7.2 或更高版本
- x64 环境
- 支持 DirectShow 的摄像头（使用摄像头功能时）

## 构建与启动

在仓库根目录运行：

```powershell
dotnet restore .\MediaShow.sln
dotnet build .\MediaShow.sln -c Release
```

构建完成后运行：

```powershell
.\bin\Release\net472\MediaShow.exe
```

也可以在 Visual Studio 中打开 `MediaShow.sln` 并生成 Release 配置。目标电脑需要安装 .NET Framework 4.7.2 或更高版本；该框架不会随程序打包。

程序使用 EXE 所在目录的 `config.json`。如果文件不存在，首次启动时会生成默认配置。发布时如需保留已有设置，请将对应的 `config.json` 放在 EXE 同目录。

## 使用方法

程序启动后会在通知区域显示托盘图标。右键托盘图标可使用以下操作：

| 菜单项 | 说明 |
| --- | --- |
| 配置的布局名称 | 切换到对应布局 |
| `Blank` | 清空摄像头画面，显示黑屏 |
| `Desktop` | 隐藏程序窗口以显示桌面；再次选择显示布局或双击托盘图标可恢复 |
| `Shutdown` | 请求 Windows 立即关机，系统可能会要求提升权限 |
| `Settings...` | 打开设置窗口 |
| `Exit` | 退出程序并释放摄像头和网络服务 |

按 `Esc` 可关闭主窗口并退出程序。

### 桌面设置

在托盘菜单中选择 **Settings...**。设置分为以下内容：

| 设置 | 说明 |
| --- | --- |
| 基本设置 | 计算机名、UDP 端口、网页设置端口、默认模式和窗口宽高 |
| 布局模式 | 新增或删除布局，设置布局名称及每个视图使用的摄像头和宽高 |
| 摄像头 | 查看本机扫描到的摄像头清单 |

点击「保存并应用」后配置会写入 `config.json` 并立即生效。修改计算机名或 UDP 端口会重启 UDP 服务；修改网页端口会重启网页设置服务。网页端口设为 `0` 时会关闭该服务。

## 配置文件

`config.json` 使用 JSON 格式。主要字段如下：

| 字段 | 含义 |
| --- | --- |
| `PcName` | 本机 UDP 控制名称，指令中用于匹配目标；不能包含连字符 `-` |
| `UdpPort` | UDP 控制监听端口，范围为 `1`–`65535` |
| `WebPort` | 网页设置端口，范围为 `0`–`65535`；`0` 表示关闭 |
| `DefaultMode` | 启动时使用的布局名称，也可以是内置模式 `Blank` 或 `Desktop` |
| `WindowWidth`、`WindowHeight` | 窗口宽高，单位为物理像素；任一值为 `0` 时窗口最大化 |
| `CameraSources` | 本机摄像头清单，包含 ID、名称、索引和分辨率 |
| `Modes` | 布局列表；每个布局包含名称 `Mode` 和视图数组 `Views` |
| `Views[].CameraId` | 视图关联的摄像头 ID |
| `Views[].W`、`Views[].H` | 视图宽高，单位为物理像素，必须大于 `0` |

程序每次启动都会重新扫描摄像头并覆盖 `CameraSources`，同时修正布局中不存在的摄像头 ID。因此，不要依赖手动维护的摄像头清单；如更换设备，请在设置窗口中确认扫描结果和布局关联。

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
  "Modes": [
    {
      "Mode": "Full",
      "Views": [
        {
          "CameraId": "cam_0",
          "W": 1280,
          "H": 720
        }
      ]
    }
  ]
}
```

`Views` 可以配置一个或多个视图，但目前最多显示前两个：一个视图居中显示，两个视图左右排列。视图尺寸和窗口尺寸分别控制摄像头画面与程序窗口的大小。

## 局域网网页设置

网页服务默认使用 `8000` 端口。确保设备之间网络可达，并允许 Windows 防火墙接收入站 TCP 连接后，在同一可信局域网的浏览器中访问：

```text
http://<运行 MediaShow 的电脑 IP>:8000/
```

本机也可以访问 `http://localhost:8000/`。页面可编辑基本设置和布局，提交后会校验、保存并立即应用。

服务接口（供设置页面使用）：

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/` 或 `/index.html` | 设置页面 |
| `GET` | `/api/config` | 获取当前配置 |
| `GET` | `/api/status` | 获取窗口尺寸、DPI、显示状态和当前模式 |
| `POST` | `/api/config` | 提交完整配置并应用；成功返回 `{ "ok": true }`，校验失败返回 HTTP 400 和 `errors` 列表 |

**安全提示：**网页设置服务监听所有网络接口，且没有身份验证。能够连接到该端口的设备可以读取和修改配置。仅在可信网络中启用；不要将端口暴露到互联网。不需要远程设置时，将 `WebPort` 设为 `0`。

## UDP 控制

程序监听 `UdpPort`，使用 UTF-8 文本指令选择布局。消息格式为：

```text
<PcName>-<模式名称>
```

例如，若配置为 `"PcName": "PC_1"`、`"UdpPort": 30000`，发送 `PC_1-Full` 即可切换到 `Full` 布局；也可发送 `PC_1-Blank` 或 `PC_1-Desktop`。发送方应使用 UDP 向运行程序的电脑及对应端口发送消息。计算机名比较不区分大小写，布局名称需与配置一致。

## 日志与项目结构

运行日志写入 EXE 所在目录下的 `Logs` 文件夹。

```text
MediaShow/
├── App.xaml(.cs)                 # WPF 应用入口及退出清理
├── MainWindow.xaml(.cs)          # 主窗口、摄像头布局和应用生命周期
├── SettingsWindow.xaml(.cs)      # 桌面设置窗口
├── TrayIcon.cs                   # 系统托盘图标与菜单
├── Controls/CameraView.xaml(.cs) # 摄像头画面控件
├── Models/                       # 配置和视图模型
├── Services/                     # 摄像头、配置、UDP、网页和日志服务
├── Resources/server.ico          # 应用及托盘图标
├── Web/WebPanel.html             # 内嵌网页设置界面
├── config.json                   # 本机配置
└── MediaShow.csproj              # 项目与依赖配置
```

Release 构建通过 Costura.Fody 将第三方托管依赖嵌入 EXE；构建目标会从输出目录移除 DLL 和 EXE 配置文件。发布前请在目标电脑上验证 EXE、.NET Framework 和摄像头驱动兼容性。

## 技术栈

- WPF / .NET Framework 4.7.2
- AForge.Video.DirectShow：摄像头发现与捕获
- Newtonsoft.Json：配置序列化
- Costura.Fody：托管依赖打包
- Windows Forms `NotifyIcon`：系统托盘图标

## 版本与说明

当前版本：1.0.0

本程序由 AI 辅助开发，使用开源的 AForge.Video.DirectShow 库进行摄像头捕获，并通过 WPF 实现用户界面。
