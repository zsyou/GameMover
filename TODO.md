# TODO

## 已完成

- 对照官方文档、Toolbox GenericPlugin 模板（master，PlayniteSDK 6.17.0）和 `Playnite.SDK.dll` 元数据实现插件，没有自造 API。
- 插件骨架：`GameMoverPlugin : GenericPlugin`、`extension.yaml`（`Type: GenericPlugin`）、设置页、`HasSettings = true`。
- 游戏菜单 `GameMover`：Move Game...、Move to {库}、Manage Libraries...（`MenuSection` 用官方的 `|` 分段规则；这里只用一层）。
- 移动对话框：游戏名、当前安装目录、库选择 / Browse、目标预览、进度、取消。
- 多库设置：添加 / 编辑 / 删除，`LoadPluginSettings` / `SavePluginSettings`，`Serialization.GetClone` 支持取消编辑。
- 预检查：空目录、源不存在、库无效、源等于目标、嵌套路径、目标已存在、剩余空间、Playnite 运行标志 + 安装目录内进程。
- 复制 → 校验（文件数、总大小、相对路径）→ `Database.Games.Update` → 删除源。取消/失败保留源。
- `PathRewriteService.RewritePath`：大小写、尾部斜杠、兄弟目录前缀、引号、环境变量、相对路径、`{InstallDir}`、`\\?\`。
- 外部库游戏：`Game.IsCustomGame`（源码定义为 `PluginId == Guid.Empty`）为 false 时拒绝，并用 `Addons.Plugins` 里的 `LibraryPlugin.Name` 组成提示。
- Release 编译通过，输出不含 `Playnite.SDK.dll`。`artifacts/GameMover_1.0.0.pext` 已打包。
- 单元测试 23 项通过（路径、进程边界、复制、取消、目标已存在、空间不足、校验失败不删源、成功后删源）。

## 未完成 / 风险

- 没有在真实 Playnite 进程里加载。当前环境是 Linux，不能启动 Playnite，也不能做“移动后从 Playnite 点击启动”的验收。
- 设置 JSON 的实际往返依赖 Playnite 的 `Serialization`，单元测试没有覆盖 `SavePluginSettings`。
- `ObservableObject` 在 SDK 6.17.0 里位于命名空间 `System.Collections.Generic`，不是文档旧示例里的 `Playnite.SDK`。模板能编过是因为它引用了 `System.Collections.Generic`。
- 剩余空间无法读取时只记警告并继续。
- 重解析点被跳过，不跟随、不重建。
- 未加引号且含空格的参数路径可能改不了，只会打警告。
- 安装目录外的启动器进程可能扫不到。
- 长路径依赖系统是否开启 long path；已经带 `\\?\` 的路径会保留前缀。

## API 笔记（PlayniteSDK 6.17.0）

- 包名是 nuget.org 上的 `PlayniteSDK`，不是 Playnite 11 feed 上的 `Playnite.SDK` 11.0.0-alpha。
- 插件类继承 `Playnite.SDK.Plugins.GenericPlugin`，构造函数 `(IPlayniteAPI)`，必须实现 `Guid Id`。
- `Properties = new GenericPluginProperties { HasSettings = true }`。
- 菜单：`GetGameMenuItems(GetGameMenuItemsArgs)`，项类型 `GameMenuItem`，`Action` 收到 `GameMenuItemActionArgs.Games`。子菜单用 `MenuSection`，嵌套用 `|`。
- 设置：`GetSettings` 返回 `ISettings`，`GetSettingsView` 返回 WPF `UserControl`。保存用 `LoadPluginSettings<T>` / `SavePluginSettings`。克隆用 `Playnite.SDK.Data.Serialization.GetClone`。
- 忽略序列化的属性用 `[DontSerialize]`（`Playnite.SDK.Data`）。
- 日志：模板使用 `LogManager.GetLogger()`。`IPlayniteAPI` 上没有 `CreateLogger()`。
- 对话框：`Dialogs.CreateWindow(WindowCreationOptions)`、`GetCurrentAppWindow()`、`SelectFolder()`、`SelectFolder(initialDir)`、`ShowMessage`、`ShowErrorMessage`。自定义按钮用 `MessageBoxOption(title, isDefault, isCancel)`。
- 进度也可以用 `Dialogs.ActivateGlobalProgress`。本插件要同时显示阶段、当前文件和字节数，所以用 `CreateWindow` 自绘，文件工作放到 `Task.Run`，UI 更新走 `MainView.UIDispatcher`。
- 游戏：`InstallDirectory`、`GameActions`（`Path`、`WorkingDir`、`Arguments`、`AdditionalArguments`、`TrackingPath`、`Script`）、`Roms`、`PreScript`、`PostScript`、`GameStartedScript`、`IsRunning`、`IsLaunching`、`IsInstalling`、`IsUninstalling`、`IsCustomGame`、`PluginId`。
- 数据库：`IGameDatabaseAPI` 继承 `IGameDatabase`。持久化是 `PlayniteApi.Database.Games.Update(game)`，`Get(Guid)` 取当前记录。只改内存不会写盘。
- 库插件：`PlayniteApi.Addons.Plugins` 类型是 `List<Plugin>`。`LibraryPlugin.Name` 是库显示名。
- 官方 csproj：`net462`、`UseWPF`、`PackageReference PlayniteSDK 6.17.0`，并把 `extension.yaml` 复制到输出。本仓库额外设置了 `EnableWindowsTargeting` 和 `ExcludeAssets=runtime`。

## 下一步

- 在 Windows 上用 External extensions 加载 `bin\Release\net462`，按验收场景移动 `D:\Games\TestGame`。
- 看 Playnite 日志里的 move / verify / update / delete 记录。
- 若设置页绑定异常，先核对 `ObservableObject` 的命名空间是否仍是 `System.Collections.Generic`。
- 真实验收通过后再考虑哈希校验和合并模式。
