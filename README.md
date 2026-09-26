# Controller Mapper（工作名）

Windows 10/11 手柄输入与虚拟手柄输出项目。目标是为单机游戏、模拟器和生产力应用提供手柄按钮映射、组合输出、Shift 分层和**按住触发的单一手柄控制项连发**。项目不提供键盘或鼠标映射、自动压枪、连招、多键序列或无人按住的循环。

**当前状态：开发原型。** DualSense Edge 的 USB、左右独立背键、按键映射和单键连发均已通过实机验证；蓝牙连接不作保证。Xbox、标准 DualSense 和任何具体游戏的输入兼容性仍未验收。请勿把编译成功理解为这些未验收设备或游戏已受支持。

本项目官方免费开源，采用 [Apache-2.0](LICENSE) 许可证；该许可证允许下游商业使用。项目仅用于单机游戏、模拟器和生产力场景，**不用于竞技网游**。项目与 Sony、Microsoft 无关联，也未获其背书。

## 工作方式与前提

- 物理手柄输入路径使用 Windows XInput 1.4（Xbox）和 HID（DualSense / DualSense Edge）。DualSense Edge 的 USB 连接，以及左右独立背键用于按键映射和单键连发，均已通过实机验证；蓝牙连接不作保证。Xbox 和标准 DualSense 的具体设备、固件与连接组合仍需实机验证。
- 手柄按钮输出目标是**虚拟 Xbox 360 兼容手柄**。当前候选后端使用 `Nefarius.ViGEm.Client` 连接 ViGEmBus。用户须自行、明确地安装兼容的虚拟手柄驱动；本软件**不捆绑、不下载、不静默安装驱动**。ViGEmBus [已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)，其实际安装包、签名、兼容性和安全性仍需发行审查。驱动缺失时不应启用映射或连发。
- 虚拟手柄不会自动屏蔽物理手柄。目标应用若同时读取两只手柄，原始按键仍可能影响结果；必须在目标应用中确认其输入路径。项目不安装过滤驱动，也不保证完整替换原始手柄输入。
- 当前选择的配置是**全局配置**，由用户手动切换；不再按游戏或应用的 `.exe` 路径自动匹配。启用后，虚拟手柄输出可作用于任意前台应用。切换前台窗口时先提交中性状态，连发须松开触发键再按下才会继续。
- DualSense Edge 的左右实体背键已在 USB 连接下通过实机验证，可作为独立输入用于按键映射和单键连发；蓝牙连接不作保证。背键设置不会写入手柄固件。
- 连发只对一个虚拟手柄按钮或全幅扳机重复按下与松开。松开触发键、前台窗口切换或不可识别、设备断开、安全监测失败或安全门关闭时，应立即停止并提交中性输出。检测到已知反作弊进程后不得自动恢复，须由用户手动恢复。进程名检测可能误报或漏报，不能保证识别所有版本。

项目不注入游戏进程、不挂钩 API、不读写游戏内存，不自动启动应用、注册系统服务或设置开机自启。

## 桌面界面

- 黑蓝色分页面界面：首页提供“自定义按键配置”“单键连发”“配置文件”“设备与运行”四个入口。
- 按键页使用手柄线稿与两侧引线按钮，点选后弹出编辑器；支持同时输出第二按钮、Shift 分层、替换同按钮同层的规则，以及从规则列表逐条移除。
- 按键页新增可见的“添加 / 修改键位”和“启用映射”入口，也可直接点击图上的 18 个输入键。确认映射后保存配置，再启用虚拟手柄输出；修改配置时运行中的输出会停止，需要重新启用。映射只改变虚拟手柄，原手柄输入不会被屏蔽。
- LT/RT 可作为映射输入或虚拟输出。输入扳机达到约半程才视为按键按下；未映射扳机仍按原模拟行程透传。映射到扳机时输出全按下，不提供力度曲线或连续模拟量重映射；具体设备表现待实机验证。
- 编辑器中的输入、输出和 Shift 设置先暂存，确认后生效；取消保留原配置。修改已有配置会停止当前输出。
- 可新建、复制、重命名与手动切换全局配置；支持 JSON 导入导出。未保存修改会提示，退出时可选择保存或放弃。
- 首页直接显示已保存配置数量与当前名称，并提供“查看已保存配置”“新建配置”。“保存配置”更新当前配置；需要另一份独立配置时先点“新建配置”。保存时记录当前选择，重新启动后恢复。
- 每 2 秒检查已连接设备。识别到 DualSense HID 时，输入按钮显示 ✕/○/□/△ 等 PlayStation 名称；XInput 显示 Xbox 名称，输出按钮始终按虚拟 Xbox 360 命名。XInput 插槽本身不能证明物理手柄型号。用户手动选定设备后，自动检查会保留该选择，断开后才切换到其他可用设备。
- 配置格式为 `SchemaVersion: 2`。旧 v1 配置迁移规则不变，本地迁移前保留 `.v1.bak` 备份。
- 连发页面支持数值与滑块调节，显示每秒次数；“关闭单键连发”会同时清空触发键与输出键。
- 底部始终可查看启用状态、停止输出及保存配置。快捷键：`Ctrl+S` 保存、`F5` 刷新设备、`Esc` 立即停止（仅在本窗口内）。
- 无设备时仍可编辑配置。按键蓝色背景表示引擎启用后报告的实际按下状态，深蓝底色表示选中的输入。摇杆曲线、扳机死区和震动强度尚未实现。

使用与检查细节见 [前端说明](docs/FRONTEND.md)。

## 下载运行（Windows x64）

打开 [GitHub Releases 下载页](https://github.com/gyw11451419-a11y/controller-mapper/releases)，下载预发布版本 Assets 中的 `ControllerMapper-…-win-x64.zip`，完整解压后运行 `ControllerMapper.exe`。压缩包包含 .NET 运行环境，不需要安装 SDK。不要下载 `Source code` 作为安装包。

首次使用、外部驱动要求及校验方法见 [下载与使用](docs/INSTALL.md)。当前为未签名的开发预发布版本；虚拟手柄输出仍需用户自行安装兼容的 ViGEmBus 驱动。

## 从源码构建

需要 Windows 10/11 与 .NET 10 SDK。仓库中的 `.dotnet/` 是可选的本地 SDK 安装目录，不是项目源码。

```powershell
dotnet restore .\src\ControllerMapper.Desktop\ControllerMapper.Desktop.csproj -r win-x64 -p:SelfContained=true
dotnet publish .\src\ControllerMapper.Desktop\ControllerMapper.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o .\dist\win-x64
```

预计输出为 `dist\win-x64\ControllerMapper.exe`。发布包还应包含本项目 [LICENSE](LICENSE)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 及所含第三方组件要求的许可和声明；不要把外部 ViGEmBus 安装包放进本项目发行包。

若本机 `dotnet` 不在 `PATH`，可显式调用本地 SDK 的 `G:\codex data\.dotnet\dotnet.exe`。开发环境中的项目和 SDK 路径不是软件运行时要求。

### 制作预发布 ZIP

在完成上面的还原后，使用对应版本号发布，再运行打包脚本：

```powershell
dotnet publish .\src\ControllerMapper.Desktop\ControllerMapper.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=0.1.0-preview.1 --no-restore -o .\dist\releases\v0.1.0-preview.1\ControllerMapper-win-x64
powershell -ExecutionPolicy Bypass -File .\scripts\Package-Release.ps1 -Version 0.1.0-preview.1
```

脚本读取还原资产中的包目录和版本，收集许可证、使用说明并生成 ZIP 和 `SHA256SUMS.txt`；还需项目本地 `.dotnet` SDK 中的 WindowsDesktop 第三方声明。输出目录为 `dist/releases/v0.1.0-preview.1/`。脚本不会覆盖已存在的 ZIP，发布新版本时请使用新版本号并更新使用说明中的文件名。

## 设计与许可

- [整体架构与能力边界](docs/ARCHITECTURE.md)
- [依赖与第三方许可清单](THIRD_PARTY_NOTICES.md)

设备协议、运行时再分发与驱动发行须依据锁定的版本和实际发布文件重新核查。任何未经验证的硬件能力都不应标为已支持。



