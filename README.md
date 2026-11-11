# MediaShow

## 摄像头显示应用

### 项目简介
MediaShow 是一个基于 .NET Framework 4.7.2 的 WPF 桌面应用程序，用于显示和管理本地摄像头视频流。

### 主要功能
- 自动扫描本地可用摄像头
- 多种布局模式切换
- 系统托盘运行（最小化到任务栏右下角）
- 托盘菜单 **Settings...** 打开设置对话框，直接编辑 config.json 参数并立即生效
- 远程网页设置：默认端口 8000，局域网内用浏览器访问 `http://<本机IP>:8000/` 即可修改配置
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
4. 运行: `dotnet run` 或直接执行 `MediaShow.exe`

### 项目结构
```
MediaShow/
├── MainWindow.xaml        # 主窗口 UI
├── MainWindow.xaml.cs     # 主窗口逻辑
├── MediaShow.csproj      # 项目配置
├── config.json            # 配置文件
├── Web/WebPanel.html      # 内嵌的远程网页设置页面
├── bin/                   # 构建输出目录
└── Resources/             # 资源文件夹 (图标等)
```

### 自定义
- 修改 `config.json` 调整摄像头配置
- `config.json` 会在构建时复制到输出目录（仅当项目中的文件更新时才覆盖，避免丢失运行期生成的配置）
- 自定义布局模式在 `Models/Modes` 中定义
- 图标可在 `Resources` 文件夹中替换

#### 托盘设置对话框
右键点击托盘图标，选择 **Settings...** 可以在图形界面中修改 `config.json`：

| 选项卡 | 可修改内容 |
| --- | --- |
| 基本设置 | 计算机名（PcName）、UDP 端口（UdpPort）、网页设置端口（WebPort）、默认模式（DefaultMode）、窗口宽高（WindowWidth / WindowHeight，单位为物理像素，运行时按当前 DPI 换算；任一项为 0 表示铺满全屏） |
| 布局模式 | 新增/删除模式，修改模式名称，编辑每个视图的摄像头、宽（W）与高（H），每个模式最多 2 个视图 |
| 摄像头 | 查看程序启动时自动扫描到的摄像头（该清单每次启动都会被重新生成并覆盖配置文件） |

> `DefaultMode` 除 `Modes` 中定义的模式外，还支持两个内置模式：`Blank`（纯黑屏）与 `Desktop`（启动时最小化到桌面）；
> UDP 指令与托盘菜单同样可以直接切换到这两个内置模式。

「窗口尺寸」下方会显示程序窗口**当前的实际尺寸**（物理像素 / WPF DIP / DPI 缩放 / 当前状态），方便对照配置值与实际效果。

点击 **保存并应用** 后配置会写入 `config.json` 并立即生效：窗口尺寸、当前布局与托盘菜单都会刷新；
若修改了计算机名或 UDP 端口，UDP 控制服务会自动按新参数重启；若修改了网页设置端口，网页设置服务也会自动重启（端口填 0 表示关闭网页设置）。配置项含义与写入的 JSON 字段一一对应。

#### 远程网页设置

程序内置一个轻量 HTTP 服务，默认监听 **8000** 端口（`config.json` 中的 `WebPort`，`0` 表示关闭），
局域网内的手机、平板或其他电脑用浏览器访问即可：

```
http://<运行 MediaShow 的电脑 IP>:8000/
```

页面提供与桌面设置对话框相同的编辑能力（基本参数、布局模式、摄像头清单），点击「保存并应用」后同样立即生效；
直接在浏览器中打开 `http://<运行 MediaShow 的电脑 IP>:<WebPort>/` 即可（本机可用 `http://localhost:<WebPort>/`）。

相关接口（供页面自身使用）：

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/` | 网页设置页面 |
| GET | `/api/config` | 返回当前配置 JSON |
| GET | `/api/status` | 返回主窗口当前实际尺寸（像素 / DIP / 缩放）、是否最小化以及当前模式 |
| POST | `/api/config` | 提交完整配置并应用，成功返回 `{ "ok": true }`，校验失败返回 400 与 `errors` 列表 |

> 该服务不带身份验证，仅应部署在可信的局域网内；如不需要请将 `WebPort` 设为 `0` 关闭。

### 构建说明
项目使用 `Microsoft.NET.Sdk.WindowsDesktop` SDK 目标 .NET Framework 4.7.2。执行 `dotnet build MediaShow.sln -c Release` 后，第三方托管 DLL 会由 Costura.Fody 嵌入 `MediaShow.exe`，应用程序图标和托盘图标也会嵌入 EXE，构建后会从输出目录移除这些 DLL，发布时只需分发 `MediaShow.exe`。

目标电脑仍需安装 .NET Framework 4.7.2 或更高版本；该运行时和 Windows 系统 DLL 不会打包进 EXE。程序首次启动时会在 EXE 所在目录生成可编辑的 `config.json`。

如需添加自定义图标，请将 .ico 文件添加到 `Resources` 文件夹并在 XAML 中引用。

### 版本
当前版本: 1.0.0

### 联系方式
- GitHub: [your-repository-url]
- 问题: [issue-tracker-url]