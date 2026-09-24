# 第三方依赖与发行审查

本清单根据 `src/ControllerMapper.Desktop/packages.lock.json` 中 `net10.0-windows7.0` 目标和本地还原的 NuGet 包编写，版本锁定于 2026-09-23。表中“许可证”是对应版本 NuGet 元数据及随包文件所示的直接许可；它不改变本项目的 Apache-2.0，也不能代替各组件完整许可文本、版权声明或包内第三方声明。正式发行时应将适用的原文与本清单一起提供，并按实际发布目录重新核查。

## NuGet 依赖

| 包 | 锁定版本 | 引入方式 | 包许可证 | 发行时注意 |
| --- | --- | --- | --- | --- |
| [WPF-UI](https://www.nuget.org/packages/WPF-UI/4.3.0) | 4.3.0 | 直接 | MIT | 随包 `LICENSE.md`、`ThirdPartyNotices.txt`；后者列出 Fluent System Icons、VirtualizingWrapPanel 等嵌入来源。核对最终发布文件，不另行捆绑 Segoe Fluent Icons 字体。 |
| [WPF-UI.Abstractions](https://www.nuget.org/packages/WPF-UI.Abstractions/4.3.0) | 4.3.0 | 传递 | MIT | 随包 `LICENSE.md`、`ThirdPartyNotices.txt`。 |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | 8.4.2 | 直接 | MIT | 随包 `License.md`、`ThirdPartyNotices.txt`；源代码生成器包含构建期资产，应检查实际发布目录。 |
| [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.12) | 10.0.12 | 直接 | MIT | 随包 `THIRD-PARTY-NOTICES.TXT`。 |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12) | 10.0.12 | 传递 | MIT | 随包 `THIRD-PARTY-NOTICES.TXT`。 |
| [HidSharp](https://www.nuget.org/packages/HidSharp/2.6.4) | 2.6.4 | 直接 | Apache-2.0 | 随包 `LICENSE.txt` 有作者版权与 Apache 许可说明。 |
| [Nefarius.ViGEm.Client](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256) | 1.21.256 | 直接 | MIT | 这是客户端库，不包含使虚拟手柄可用的 ViGEmBus 驱动；项目已停止维护，发行前核对包内原生资产、签名和运行时兼容性。 |
| [Serilog](https://www.nuget.org/packages/Serilog/4.4.0) | 4.4.0 | 直接 | Apache-2.0 | 日志不得记录完整按键活动或不必要的个人信息。 |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | 7.0.0 | 直接 | Apache-2.0 | 依赖 Serilog；核查发行包实际解析版本。 |

自包含 `win-x64` 还原还取得 .NET 10.0.12 的 `Microsoft.NETCore.App.Runtime.win-x64`、`Microsoft.WindowsDesktop.App.Runtime.win-x64`、`Microsoft.AspNetCore.App.Runtime.win-x64` 运行时包。前两者的包内 `LICENSE` 显示 MIT；运行时包还带有第三方声明（其中 WindowsDesktop 包的具体声明须按最终发布文件和微软的 [许可信息](https://github.com/dotnet/core/blob/main/license-information.md) 复核）。取得某个运行时包不代表它的全部文件一定进入最终发行物；请以 `publish` 输出为准。

`System.Text.Json` 随 .NET 10 使用；XInput 1.4 来自 Windows 系统。项目未引入 MoonSharp、GameInput SDK 或独立图标包。若后来加入这些依赖，须重新生成锁文件并补充本清单；尤其不能把 MoonSharp 的 BSD-3-Clause 写成 MIT，也不能把 GameInput 原生组件许可与运行时再分发条款混为一谈。

## 外部虚拟手柄驱动

[ViGEmBus v1.22.0](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0) 是用户可自行取得并安装的候选外部驱动，**不是本项目发行包的组成部分**。源码仓库标 BSD-3-Clause，不能据此代替对指定安装包的许可、签名、安全和 Windows 10/11 兼容性审查。ViGEmBus [已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)；若审查或实机验证未通过，应暂停虚拟手柄输出发行或改选后端。本项目不捆绑、不下载、不静默安装该驱动或 HidHide 过滤驱动。

## 每次发布前检查

1. 对发布所用 SDK、锁文件与 `dotnet package list --include-transitive` 结果逐项核对版本、直接和传递依赖、许可证及随包声明。
2. 查看 `publish` 输出，随应用提供适用的原文许可和版权/第三方声明，审查自包含 .NET 运行时的再分发条件。
3. 检查是否误将驱动安装包、过滤驱动、微软字体或未审核的原生库打入发行物。
4. 若切换安装器、虚拟手柄后端或图标资源，先核查其准确版本和许可，再更新本文件。
