# 下载与使用

适用于 Windows 10/11 x64。本项目目前为预发布原型。

1. 打开 https://github.com/gyw11451419-a11y/controller-mapper/releases ，选择最新预发布版本。
2. 在 Assets 中下载 `ControllerMapper-v0.1.0-preview.1-win-x64.zip`；`Source code` 是源码，不是可运行程序。
3. 完整解压到一个可写文件夹。保留所有 DLL 和许可证文件，不要只复制 EXE，也不要直接从压缩包运行。
4. 双击 `ControllerMapper.exe`。已包含 .NET 运行环境，无须安装 .NET SDK。
5. 连接手柄，新建或选择配置，设置按键并保存，再启用映射。当前已实机验证 DualSense Edge USB 连接及左右独立背键；蓝牙、Xbox 和标准 DualSense 尚未完成验收。

## 虚拟手柄驱动

编辑配置不需要驱动；启用虚拟 Xbox 360 手柄输出需要另行安装兼容的 ViGEmBus。软件不捆绑、下载或静默安装驱动。

官方候选版本：https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0

ViGEmBus 已停止维护，其在使用者系统上的兼容性不作保证。安装前阅读上游说明，安装器要求重启时先重启，再打开本软件。驱动不可用时不要启用输出。

虚拟手柄不会屏蔽物理手柄；如果游戏出现双重输入，请在游戏中确认实际使用的输入设备。仅用于单机游戏、模拟器和生产力应用，不用于竞技网游。

## 更新、退出与问题反馈

- 更新前退出旧版本，将新版完整解压到新文件夹。建议先在软件中导出配置备份。
- 停止输出可点击底部停止按钮；窗口内可按 Esc。
- 卸载免安装程序：退出后删除解压文件夹。外部驱动需在 Windows 中单独管理。
- 当前发布包没有代码签名，Windows 可能显示未知发布者提示；请核对来源和发布页的 SHA-256，不要关闭系统安全保护。
- 问题反馈：https://github.com/gyw11451419-a11y/controller-mapper/issues 。请提供系统版本、手柄型号、连接方式和复现步骤。

可用 PowerShell 校验下载文件：

```powershell
Get-FileHash .\ControllerMapper-v0.1.0-preview.1-win-x64.zip -Algorithm SHA256
```

将结果与同一发布页的 `SHA256SUMS.txt` 对照。
