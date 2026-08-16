# 开发环境准备

本文件记录**首次 clone 项目后到第一次能跑 / 编译 / 测试**之间需要的步骤。仅限开发环境，发布流程见 `verification-and-rollback.md`。

## 为什么需要这份文档

`README.md` 列出的是构建命令，但**没列出前置依赖**（例如 `.NET 10 SDK`、外部二进制）。首次 clone 项目后直接 `dotnet build` 会失败，本文件补齐这些步骤。

> **rebase 注意**：本文件位于 `docs/operations/`，与现有文档无重叠，是新增文件，不影响 rebase。如果上游新增类似文档，可以选择合并或保留两者。

## 前置依赖清单

| 依赖 | 用途 | 必须？ |
|---|---|---|
| **.NET 10 SDK** | 编译 / 测试整个解决方案 | ✅ 必需 |
| **Visual Studio 18.0+** 或 VS Code + C# Dev Kit | IDE 编辑 / 调试 | ⚠️ 推荐（命令行也能编译） |
| **PowerShell 5.x 或 7** | 下载外部二进制（aria2、ffmpeg） | ⚠️ 启动 UI 才需要 |
| **Internet 连接** | 下载 .NET SDK 与外部二进制 | ✅ 首次必需 |

## 步骤 1：装 .NET 10 SDK

### 下载

打开 https://dotnet.microsoft.com/download/dotnet/10.0

下载 **SDK 10.0.x**（x64，除非你是 ARM 机器）。

### 验证

打开**新的 PowerShell 窗口**（旧的会缓存 PATH）：

```powershell
dotnet --list-sdks
```

应看到：

```
10.0.100 [C:\Program Files\dotnet\sdk]
9.0.xxx  [C:\Program Files\dotnet\sdk]   ← 旧版兼容，可保留
```

**没看到 10.0.x**：SDK 没装对，重新装。

## 步骤 2：装 IDE（二选一）

### 选项 A：Visual Studio 18.0+（重量级）

1. 打开 Visual Studio Installer
2. 修改 → 单个组件 → 勾选 ".NET 10 SDK"
3. 把 VS 升级到最新（18.0+）

下载：https://visualstudio.microsoft.com/downloads/

### 选项 B：VS Code + C# Dev Kit（轻量，推荐）

1. 装 VS Code：https://code.visualstudio.com/
2. 装扩展：**C# Dev Kit**（`ms-dotnettools.csdevkit`）
3. 装扩展：**Avalonia for VS Code**（可选，但推荐）

VS Code 用已安装的 .NET SDK，不需要 IDE 自带 SDK。

## 步骤 3：下载外部二进制（启动 UI 才需要）

### 3.1 准备 PowerShell

脚本需要 PowerShell。检查版本：

```powershell
$PSVersionTable.PSVersion
```

- 7.0+ → 用 `pwsh` 调用
- 5.1 → 用 `powershell` 调用（系统自带）

### 3.2 下载 aria2

```powershell
cd E:\Projects\downkyi\downkyicore
& ".\script\aria2.ps1" -arch x64
```

### 3.3 下载 ffmpeg

```powershell
& ".\script\ffmpeg.ps1" -arch x64
```

### 3.4 验证下载

```
DownKyi.Core/
└── Binary/
    └── win-x64/
        ├── aria2/
        │   ├── aria2c.exe
        │   └── aria2c.exe.sha256
        └── ffmpeg/
            ├── ffmpeg.exe
            ├── ffprobe.exe
            └── ...
```

## 步骤 4：首次编译

```powershell
cd E:\Projects\downkyi\downkyicore
dotnet restore
dotnet build -c Debug
```

成功应看到 `Build succeeded. 0 Warning(s) 0 Error(s)`。

## 步骤 5：跑测试

### 命令行（推荐 Phase 1 用）

```powershell
dotnet test tests\DownKyi.Core.Tests\DownKyi.Core.Tests.csproj -c Debug --no-build
```

### VS Test Explorer

1. **Test → Test Explorer**（Ctrl+E, T）
2. 等编译完成（首次需要几秒）
3. 右键要跑的测试 → Run

## 常见错误

### `pwsh` 不是内部或外部命令

**原因**：没装 PowerShell 7。  
**解决**：用 `powershell`（一个 l，5.1 系统自带）代替，或装 [PowerShell 7](https://github.com/PowerShell/PowerShell/releases)。

### `.\script\xxx.ps1` 找不到

**原因**：PowerShell 调用脚本需要 `&`。  
**解决**：`& ".\script\aria2.ps1" -arch x64`（带 `&` 和引号）。

### `execution of scripts is disabled on this system`

**解决**：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

### `NETSDK1209` —— VS 版本太低

**错误信息**：

```
The current Visual Studio version does not support targeting .NET 10.0.
Either target .NET 9.0 or lower, or use Visual Studio version 18.0 or higher
```

**原因**：VS < 18.0，且 .NET 10 SDK 没装或没被 VS 识别。  
**解决**：
1. 装 .NET 10 SDK（见步骤 1）
2. 升级 VS 到 18.0+（见步骤 2 选项 A）
3. 完全关闭 VS 重开
4. 验证 VS 能识别 SDK：VS 里按 Ctrl+` 打开终端，跑 `dotnet --version`，应输出 `10.0.x`

### `The packaged aria2 executable is missing`

**原因**：没下二进制（步骤 3 跳过）。  
**解决**：跑步骤 3 的下载脚本。

### 找不到 `DownKyi` 启动项目

**解决**：解决方案资源管理器 → 右键 `DownKyi` → "设为启动项目"。

## 不需要外部二进制的场景

| 场景 | 需要二进制？ |
|---|---|
| 跑单元测试 | ❌ 不需要 |
| 编译核心库（`DownKyi.Core` 等） | ❌ 不需要 |
| 启动 UI 主程序 | ✅ 需要 aria2 + ffmpeg |
| 实际下载视频 | ✅ 需要 |

Phase 1（别名仓库 + 测试）只跑测试，不需要二进制。

## 检查清单

首次跑通后核对一遍：

- [ ] `dotnet --version` 输出 `10.0.x`
- [ ] `dotnet build -c Debug` 成功，无 error
- [ ] `dotnet test ...` 至少跑通既有测试
- [ ] `DownKyi.Core/Binary/win-x64/aria2/aria2c.exe` 存在
- [ ] `DownKyi.Core/Binary/win-x64/ffmpeg/ffmpeg.exe` 存在
- [ ] VS / VS Code 能打开解决方案
- [ ] VS 里 `dotnet --version` 也输出 `10.0.x`

## 跨平台提示

| 平台 | .NET SDK | 脚本调用 | 二进制路径 |
|---|---|---|---|
| Windows x64 | dotnet.microsoft.com | `& ".\script\xxx.ps1"` 或 `pwsh` | `DownKyi.Core/Binary/win-x64/` |
| macOS Apple Silicon | 同上 | `bash script/xxx.sh` | `DownKyi.Core/Binary/osx-arm64/` |
| Linux x64 | 同上 | `bash script/xxx.sh` | `DownKyi.Core/Binary/linux-x64/` |

macOS / Linux 用户跑 `bash script/aria2.sh` 和 `bash script/ffmpeg.sh`，逻辑一致。

## 维护

- 新增外部依赖时，在 `script/` 加对应下载脚本，并在此文档补一节
- .NET SDK 版本变更时同步更新本文档
- 上游如果新增同类文档，可保留两版或在 PR 中协调合并
