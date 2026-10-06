# GameMover

Playnite 桌面版通用插件。把**手动添加**的游戏安装目录迁到另一个库文件夹，并在校验通过后更新 Playnite 里的路径。

面向 Playnite 10，引用 NuGet 包 **PlayniteSDK 6.17.0**（.NET Framework 4.6.2，`GenericPlugin`）。这不是 Playnite 11 alpha 的 `Playnite.SDK` 11.x。

## 功能

- 游戏右键菜单 **GameMover**：
  - 当前安装路径（没有安装目录时显示 `(no install directory)`）
  - **Move Game...**
  - **Move to {库名称}**（设置里保存过的库会逐个出现，打开对话框并预选目标）
  - **Manage Libraries...**（打开插件设置）
- 移动对话框显示游戏名、当前 `InstallDirectory`、目标库、最终目录预览。  
  例：`D:\Games\Diablo II Resurrected` → `E:\Games` → `E:\Games\Diablo II Resurrected`
- 设置里可添加、修改、删除多个库根目录，并随 Playnite 插件设置保存。
- 移动前检查：安装目录非空、源目录存在、目标库存在、源和目标不同、目标不在源内部、目标文件夹尚不存在、目标卷剩余空间足够、游戏没有在运行。
- 复制使用 C# 文件流（可取消、报告进度）。顺序固定为：**复制 → 校验 → 写回 Playnite → 删除源目录**。失败或取消时源目录保留。
- 校验比较文件数量、总字节数和相对路径。接口预留了以后的 CRC32 / xxHash / SHA256，当前版本不算哈希。
- 校验通过后更新 `InstallDirectory`，并按旧目录 → 新目录重写动作里的 `Path`、`WorkingDir`、`Arguments`、`AdditionalArguments`、`TrackingPath`、`Script`，以及 ROM、前后脚本和指向旧目录的封面/手册绝对路径。然后调用 `Database.Games.Update`。
- 目标文件夹已存在时不覆盖，提示 **Target folder already exists.**，可选换一个文件夹或取消。
- 复制过程中可取消。取消后询问是否删除这次新建的目标副本，不会擅自删掉原本就存在的数据。
- Steam、Epic、GOG、Ubisoft、EA、Battle.net、Xbox 等由库插件管理的游戏会被拒绝。判断依据是 Playnite 的 `Game.IsCustomGame`（即 `PluginId == Guid.Empty`）以及 `Addons.Plugins` 里的 `LibraryPlugin` 名称，不写死平台 ID。

## 安装

需要 Windows 上的 **Playnite 10 桌面模式**。

1. 下载 `artifacts/GameMover_1.0.0.pext`。
2. 在 Playnite 里安装这个扩展包：双击 `.pext`，或使用 Playnite 的扩展安装器。
3. 重启 Playnite。
4. 在扩展列表里确认 GameMover 已启用。

开发时如果要直接加载编译输出，可以在 Playnite **设置 → For developers → External extensions** 里加入 `src\GameMover\bin\Release\net462`。这不是普通安装方式。不要把 `Playnite.SDK.dll` 放进扩展目录。

## 使用

1. 打开 **设置 → 扩展 → GameMover**，添加库文件夹，例如 `D:\Games`、`E:\Games`。
2. 右键一个手动添加的游戏，选择 **GameMover → Move Game...**。
3. 选择目标库，确认预览目录，点 **Start**。
4. 等进度走完。成功后游戏从新目录启动，旧目录被删除。

一次只能移动一个游戏。库插件导入的游戏会提示改用原来的启动器迁移。

## 本地构建

需要 .NET SDK（能编译 `net462` + WPF）。Windows 上安装 .NET SDK 的 Windows 桌面工作负载即可。

```powershell
dotnet build src\GameMover\GameMover.csproj -c Release
dotnet test tests\GameMover.Tests\GameMover.Tests.csproj -c Release
```

输出目录：`src\GameMover\bin\Release\net462\`。

打包时只放入 `extension.yaml`、`GameMover.dll`、`icon.png` 和 `Localization`。官方 Toolbox 也会剥掉可能和 Playnite 冲突的依赖；这个项目用 `ExcludeAssets=runtime` 避免把 PlayniteSDK 复制进输出。

Linux / macOS 上交叉编译依赖项目里的 `EnableWindowsTargeting`。插件本身仍然只能在 Windows 的 Playnite 里运行。

## 安全说明

- 先复制，再校验，再更新数据库，最后才删除源目录。
- 源目录和目标目录相同、互相包含，或目标已存在时，不会开始写入。
- 删除使用不跟随重解析点的递归删除，并拒绝删除盘符根目录。复制和校验也会跳过 junction / symlink，并写日志。
- 取消或失败时源目录保留。若目标目录是这次移动创建的，会询问是否删除该副本。
- 数据库已经改指向新目录、但旧目录删不掉时，两边都会留下，游戏应仍从新目录启动。请查看 Playnite 日志后再手动清理。
- Playnite SDK 不是完全线程安全的。文件复制在后台进行；读取运行状态和 `Games.Update` 会切回 UI 线程。

## 已知限制

- 只处理 Playnite 自己保存安装路径的手动游戏（`IsCustomGame == true`）。
- 不合并进已有目标文件夹。
- 不做全文件哈希、junction/symlink 迁移、Steam 库文件夹自动化、Epic manifest、注册表或配置扫描、云同步。
- 游戏若从安装目录之外的启动器进程拉起，且 Playnite 的 `IsRunning` / `IsLaunching` 仍为 false，进程扫描可能发现不了。从 Playnite 启动的游戏会看这几个标志。
- 带空格且未加引号的参数路径、环境变量路径、`{InstallDir}` 这类变量路径不会硬改；无法安全判断时保持原样并写警告日志。
- 路径匹配按目录边界进行，`D:\Games\ABC` 不会误伤 `D:\Games\ABC2`。比较在 Windows 上不区分大小写。
- 剩余空间读不到（例如某些网络盘）时会记警告并继续；复制中途磁盘满了会失败并保留源目录。
- 本环境无法启动 Playnite，因此没有做真实的“从 Playnite 点击启动游戏”验收。单元测试覆盖了路径重写、复制、校验、取消、目标已存在、空间不足和“先更新再删除源”的顺序。

## 以后可以做

- CRC32 / xxHash / SHA256 校验模式
- 合并到已有目录
- 批量移动
- 可选 robocopy
- 在确认安全的前提下处理 junction
