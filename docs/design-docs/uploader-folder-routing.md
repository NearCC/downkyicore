# 按 UP 主自动分配下载子目录

状态：草案（待评审，部分实现完成）
最后审查：2026-08-16
范围：按 UP 主身份进行每任务子目录路由，支持两种策略

## Rebase 友好约束

本特性针对 fork 仓库，需要定期 rebase 合并上游 `main`。实现必须满足：

- **尽量少改既有源文件**，让 fork 能顺利重放上游更新而不冲突。能新建文件就不要改旧文件。
- **把别名表当作可移植数据**对待。用户有多台电脑，希望在不同电脑之间直接复制别名表，而不必逐条重新输入。
- **避免改动 SQLite schema**，避免与上游 schema 变更冲突。resolver 的输出直接合并进 `FilePath`，不新增列。

本设计允许的既有文件改动**只有两处**（比最初设计减少一处）：

- `DownloadTaskDraftFactory.BuildFilePath`：新增一个可选参数 `string? subFolder = null`，默认行为完全不变。
- `ViewDownloadSetter.axaml`：新增一个元素承载新的 `ViewUploaderRoutingPanel` 用户控件。

其余全部放在新文件中，路径为：

```
src/DownKyi.Desktop/Services/Uploader/                     # 别名 repo + 偏好 repo + resolver
src/DownKyi.Desktop/Views/Settings/UploaderAliases/        # 别名管理设置页
src/DownKyi.Desktop/Views/Dialogs/Components/              # 下载对话框内嵌路由面板
```

## 背景

DownKyi Core 1.1.1 只能选一个 `SaveVideoRootPath`，所有下载路径都从这个根目录铺开。文件名模板可以包含 `UpName` / `UpMid`，但仅此而已。两个真实痛点无法通过模板变通解决：

1. UP 主改了昵称 → 旧文件夹名对不上新下载，同一个人会分裂出两个并行的文件夹。
2. 视频由 UP 主 A 上传，但内容是"关于"UP 主 B（例如反应视频、解说、联动），用户希望归到 B 而不是 A。

**关于合作投稿（多 UP 主）：** 最初设计想支持从 B 站的 `staff` 字段解析合作 UP 主，但 `staff` 字段在 DownKyi 项目中**从未被反序列化**（`VideoView.cs` 里该字段被注释掉了）。为避免触碰 DTO 与引发额外的 HTTP 请求，**本特性只取主 UP 主（`Owner`）**。需要归档到副 UP 主的用户可以用 Custom 策略手动输入，或在别名页手动加一条映射。

简单地在模板里加 `UpName` 不能解决以上任一点。

## 决策

引入**两种触发模式 × 两种路由策略**的矩阵。

### 触发模式

| 模式 | 何时进入 | UI 行为 |
|---|---|---|
| **自动模式** | 全局设置 `IsAutoDownloadAll = Yes`，剪贴板监听或定时触发自动下载 | 不弹对话框，无策略选择 |
| **手动模式** | 用户在 UI 里手动添加下载 | 弹对话框，让用户选策略 |

### 两种路由策略（仅手动模式可选）

| 策略 | 用户操作 | 命中映射 | 未命中映射 |
|---|---|---|---|
| **Custom**（自定义子目录） | 在文本框里手写子目录名 | 不查映射，直接用输入 | 同左 |
| **ByUploader**（按 UP 主选） | 自动用视频的主 UP 主（`Owner`） | 显示"匹配到旧映射"，文件夹名**可改** | 显示"准备新建映射"，文件夹名**可改** |

**关键设计取舍**：

- **不做模糊搜索**：UP 主选择框只显示主 UP 主，不混入全局映射表。
- **Custom 保持简洁**：纯文本框，没有 autocomplete。
- **ByUploader 显示状态 + 可编辑**：显示"匹配到旧映射"或"准备新建映射"，并允许用户就地修改最终文件夹名（修改 = 更新映射）。

### 自动创建映射的勾选项

仅 **ByUploader** 策略下显示。Custom 不显示。

- ☑ 创建/更新映射 → 用户点击下载时把当前文件夹名写入别名表
- ☐ 不创建/更新 → 仅本次下载使用该文件夹名，别名表保持不动

勾选状态默认 `true`，并持久化到偏好文件（`uploader-routing-prefs.json`）。

### 两种触发模式下的行为差异

| 模式 | 走的策略 | 未命中时是否自动创建映射 |
|---|---|---|
| **自动模式** | `ByUploader` | ❌ 不创建（避免批量下载时静默出现一堆映射） |
| **手动模式** | 用户从两个策略里选 | 由勾选项决定（默认 ✅ 创建） |

### 策略记忆

- 记录用户上次选择的策略 + 上次的 Custom 文本 + 上次的勾选状态
- 下次打开对话框默认沿用上次策略：
  - Custom → 上次文本自动填入输入框
  - ByUploader → 解析新视频的主 UP 主；状态、ResolvedFolderName 实时刷新

### 手动编辑

- 别名表永远可在设置页手动增删改
- 改了之后**旧文件夹不迁移**，新下载按新映射走
- 自动创建 / 用户改写出来的映射，用户可在别名页看到并改名

## 目标

- 同一 UP 主改名后，新下载仍归入同一个文件夹。
- 允许用户在每任务层面完全自定义子目录名（Custom）。
- 允许用户就地把最终文件夹名改写（ByUploader）。
- 自动模式下保持当前行为，不静默创建用户未确认的数据。
- 手动模式下用户每次的选择能在后续下载中被复用。
- 保持 SQLite resume 兼容性：在途任务保留其原 `FilePath`。
- 保持 JSON 设置向后兼容。

## 非目标

- **合作投稿（多 UP 主）选择**：本特性只取主 UP 主。后续若上游 `staff` 字段重新可用，再扩展。
- 在下拉框中模糊搜索全局映射表。
- 自动从视频元数据识别"内容关于 UP 主 B"，由用户显式选择。
- 用户编辑别名后自动重命名或合并既有文件夹。
- B 站以外的云端 / 远端 UP 主身份提供方。

## 数据模型

### 别名存储（独立可移植文件）

别名表**不**纳入 `ApplicationSettings`，而是单独存为一个 JSON 文件，路径由用户控制且跨平台一致：

| 平台 | 默认位置 |
|---|---|
| Windows | `%APPDATA%\DownKyi\Config\uploader-aliases.json` |
| macOS | `~/Library/Application Support/DownKyi/Config\uploader-aliases.json` |
| Linux | `$XDG_CONFIG_HOME/DownKyi/Config\uploader-aliases.json` |

具体目录取 `ApplicationDataPaths.Config` 再拼接文件名。可通过环境变量 `DOWNKYI_UPLOADER_ALIAS_PATH` 覆盖路径，便于 Dropbox / Syncthing / Git 仓库同步。

文件结构（生命周期内冻结）：

```json
{
  "aliases": [
    { "mid": 123456, "folderName": "博主A" },
    { "mid": 789012, "folderName": "博主B-官方" }
  ]
}
```

> **注意**：早期版本草稿里写了 `schemaVersion: 1`。后来确认这个字段短期用不上，**当前 JSON 里不写 schemaVersion**，读 JSON 失败就当空表。等以后真的需要变更格式时再加版本号和迁移逻辑。

使用独立文件而不是合并进 `ApplicationSettings` 的原因：

1. **跨机器同步**：用户直接复制这个文件即可，与 `ApplicationSettings` 其它字段（机器特定的路径、窗口位置、登录态）解耦。
2. **无需 schema 迁移**：`ApplicationSettings.cs` 与 JSON 验证器均不变，上游合并不会触碰本特性代码。
3. **冲突概率低**：本特性不需要修改任何既有源文件。

### 别名模型

```csharp
public sealed record UploaderAlias(
    long Mid,
    string FolderName);
```

### 别名仓库接口

```csharp
public interface IUploaderAliasRepository
{
    IReadOnlyDictionary<long, string> Load();

    // 新增或修改单条；内部用 Load + 合并 + Save 实现原子写入
    void Upsert(long mid, string folderName);

    void Save(IReadOnlyDictionary<long, string> aliases);
    string FilePath { get; }
}
```

实现：

- `FileUploaderAliasRepository`：
  - `Upsert` 内部读出现有别名表 → 合并/覆盖这一条 → 临时文件 + rename 原子写回。
  - `Save` 整体覆盖（用于导入场景）。
- `InMemoryUploaderAliasRepository`：测试用。

仓库在 `DesktopComposition.AddDownKyiDesktop()` 中注册为单例；UI 通过构造函数注入消费。

### 路由偏好存储（独立小文件）

用户上一次选定的策略 + 上次的 Custom 文本 + 上次的勾选状态存到另一个 JSON 文件：

| 平台 | 默认位置 |
|---|---|
| Windows | `%APPDATA%\DownKyi\Config\uploader-routing-prefs.json` |
| macOS | `~/Library/Application Support/DownKyi/Config\uploader-routing-prefs.json` |
| Linux | `$XDG_CONFIG_HOME/DownKyi/Config\uploader-routing-prefs.json` |

文件结构（生命周期内冻结）：

```json
{
  "lastStrategy": "ByUploader",
  "lastCustomFolder": "",
  "lastPersistAlias": true
}
```

> 同样不写 `schemaVersion`。

`lastStrategy` 是 `"Custom" | "ByUploader"` 之一，缺省视为 `"ByUploader"`（最常用的默认）。
`lastPersistAlias` 缺省视为 `true`。

注意：这个文件**不参与跨机器同步**——不同电脑用户的最后策略与勾选状态可能不同，没必要共享。

### 路由偏好模型

```csharp
public enum UploaderRoutingStrategy
{
    Custom,
    ByUploader,
}

public sealed record UploaderRoutingPreference(
    UploaderRoutingStrategy LastStrategy,
    string LastCustomFolder,
    bool LastPersistAlias);
```

### 路由偏好仓库接口

```csharp
public interface IUploaderRoutingPreferenceRepository
{
    UploaderRoutingPreference Load();
    void Save(UploaderRoutingPreference preference);
    string FilePath { get; }
}
```

实现：

- `FileUploaderRoutingPreferenceRepository`：原子写入。
- `InMemoryUploaderRoutingPreferenceRepository`：测试用。

## 解析规则

所有者：`DownloadSubFolderResolver`，新文件位于
`src/DownKyi.Desktop/Services/Uploader/`。纯函数。

### 角色分离

resolver 只负责"算出一个预览值"——告诉 UI 当前视频对应的 mid、初始文件夹名是什么、状态如何。**不**负责最终路径组装，**不**负责别名表写入。这两件事由调用方分别处理：

- 路径组装：`DownloadTaskDraftFactory.BuildFilePath(... subFolder)`
- 别名表写入：调用方根据勾选项决定是否调 `repo.Upsert(mid, folderName)`

### 输入

```csharp
public sealed record ResolverInputs(
    UploaderRoutingStrategy Strategy,
    string? CustomFolder,           // 仅 Custom 时由 UI 提供
    string? ResolvedFolderName,     // 可选：UI 算好/改好的最终文件夹名（用户编辑过的或初始值）
    IReadOnlyDictionary<long, string> Aliases);
```

加上 `(VideoInfoView video, ...)`。

`ResolvedFolderName` 是核心设计：当 UI 想用某个特定文件夹名时，传进来即可跳过 resolver 的内部查找逻辑。

### 输出

```csharp
public enum ResolutionStatus
{
    NoUploader,         // 没有可用 UP 主（mid <= 0）
    MatchedAlias,       // 在别名表里命中现有映射
    WillCreateAlias,    // 没有命中，将要新建
}

public sealed record ResolverPreview(
    long Mid,                       // 关联的 UP 主 mid（> 0）
    string InitialFolderName,       // 初始文件夹名（别名或当前名字）
    ResolutionStatus Status);

public sealed record ResolverResult(
    ResolverPreview? Preview,       // Custom 策略下为 null
    string SubFolder);              // 最终写入路径的子目录
```

### 规则分支

```text
根据 inputs.Strategy 分支：

=== Custom ===
    folder = inputs.CustomFolder?.Trim() ?? ""
    return (Preview = null, SubFolder = folder)
    // 注：CustomFolder 为空时 SubFolder 也为空，最终落到根目录（无子目录）

=== ByUploader ===
    // 如果 UI 已经算好了最终文件夹名（含用户编辑过的），直接用它
    if inputs.ResolvedFolderName 非空：
        mid = video.Owner?.Mid ?? -1
        return (Preview = ComputePreview(mid, video.Owner?.Name, aliases),
                SubFolder = inputs.ResolvedFolderName.Trim())

    // 否则走 resolver 自动解析
    mid = video.Owner?.Mid ?? -1
    if mid <= 0：
        return (Preview = NoUploader, SubFolder = "")

    if inputs.Aliases.TryGetValue(mid, out var aliasName)：
        return (Preview = MatchedAlias(mid, aliasName), SubFolder = aliasName)

    name = video.Owner?.Name ?? ""
    return (Preview = WillCreateAlias(mid, name), SubFolder = name)
```

`ComputePreview` 内部仍然查映射，得出 matched / will create 状态——这样 UI 能正确显示状态文字，但 SubFolder 用的是 `ResolvedFolderName`。

### 自动模式调用方式

`DownloadSubFolderResolver` 在自动模式下被这样调用：

```text
inputs = ResolverInputs(
    Strategy          = ByUploader,
    CustomFolder      = null,
    ResolvedFolderName = null,   // 不传 override，走自动解析
    Aliases           = aliases)
```

返回 `ResolverResult.SubFolder`，作为路径子目录使用。**不**关心 `Preview` 是否带 `WillCreateAlias`——自动模式不做别名写入。

### 调用方职责

`AddToDownloadService`（或当前实际的下载草稿工厂调用方）拿到 `ResolverResult` 后：

1. 把 `SubFolder` 拼到 `DownloadBase.FilePath`（`SubFolder` 为空时跳过子目录）。
2. 如果 `Preview != null` 且**用户在 UI 上勾选了"创建/更新映射"**：
   - 用 `Preview.Mid` 与 `SubFolder`（或 UI 上的最终值）调 `repo.Upsert(mid, folderName)`。
3. `Status` 仅用于 UI 展示与日志记录。

### 消毒

UI 在显示和提交前对 `InitialFolderName` / 用户编辑值都过 `Format.FormatFileName`。如果消毒后变成空串，UI 提示用户并清空路径预览；最终路径构造器跳过该段，避免出现连续斜杠或孤立点目录。

## UI

### 设置页：别名管理（全新页面）

`src/DownKyi.Desktop/Views/Settings/UploaderAliases/ViewUploaderAliasesPage.axaml`
+ `ViewUploaderAliasesViewModel.cs`。该页通过 `DesktopComposition` 注册（不修改 `ViewVideo.axaml`），从设置菜单新加一条入口进入。

功能：

- **列表**：展示 `(mid, folderName)` 对，按 mid 升序排序。
- **新增**：输入 mid（接受 B 站个人空间 URL 或纯数字 mid）和文件夹名后点击添加。
- **编辑**：仅可改 `folderName`，`mid` 不可改（mid 是主键，改动会造成孤儿条目）。
- **删除**：删除条目；后续下载回退到当前名字。
- **导出**：按钮弹保存对话框，把 JSON 写到用户指定位置。
- **导入**：按钮弹打开对话框，校验 schema 后**替换**内存中的表。导入会覆盖现有条目（合并模式留作未来选项，导入前给出警告）。
- **显示当前文件路径**：标签展示当前 JSON 文件的绝对路径，方便用户手动拷贝（例如复制到 Dropbox 目录）。

仓库通过构造函数注入；页面在初始化时调 `repo.Load()`，每次增删改后调 `repo.Save()`。

### 下载对话框：每任务策略（一个新 UserControl）

`Views/Dialogs/ViewDownloadSetter.axaml` 仅新增一个元素承载新 UserControl，既有布局完全不动：

```xml
<!-- 以上既有布局不变 -->
<views:ViewUploaderRoutingPanel Grid.Row="2" />
```

新 UserControl 位于
`Views/Dialogs/Components/ViewUploaderRoutingPanel.axaml`，绑定到一个小 VM：

```csharp
public UploaderRoutingStrategy Strategy { get; set; }   // 顶部下拉框
public string? CustomFolder { get; set; }              // 仅 Custom 可见
public string? ResolvedFolderName { get; set; }        // 仅 ByUploader 可见
public bool PersistAlias { get; set; }                 // 仅 ByUploader 可见
public ResolutionStatus Status { get; }                // 仅 ByUploader 可见
public string ResolvedSubFolderPreview { get; }        // 只读预览
public string RootDirectory { get; }
```

#### 面板布局

```
┌──────────────────────────────────────────────────────────┐
│ 路由策略:  [▼ 按 UP 主选 | 自定义 ]                       │
│                                                          │
│ ─── 当策略 = 自定义 ───                                  │
│  子目录:    [_____________________________]               │
│             (留空则文件直接落到根目录)                    │
│                                                          │
│ ─── 当策略 = 按 UP 主选 ───                              │
│  状态:    [ℹ 匹配到旧映射] 或 [ℹ 准备新建映射]            │
│  文件夹:  [_____________________________]                │
│           ↑ 可编辑（决定这次下载落哪个目录）              │
│           预填为主 UP 主名（命中映射时预填别名）          │
│                                                          │
│  ☑ 创建/更新映射                                         │
│                                                          │
│ 将保存到:  D:\BilibiliDownload\<resolved>\…               │
└──────────────────────────────────────────────────────────┘
```

#### 行为细节

- 顶部策略下拉框默认选中**上次保存的策略**（首次为 `ByUploader`）。
- 切换策略时：
  - 切换到 `Custom` → CustomFolder 文本框显示上次保存的值（若有）。
  - 切换到 `ByUploader` → 触发 resolver 重算，刷新 Status、ResolvedFolderName；PersistAlias 用上次值。
  - **特殊情况**：如果当前视频没有 UP 主（Owner mid ≤ 0），UI 自动把策略切到 `Custom`，并提示"该视频没有 UP 主信息，请输入自定义目录"。
- 改文件夹名时，`ResolvedFolderName` 实时更新；预览路径同步刷新。
- 切换策略或勾选项变化时立即通过偏好仓库保存（去抖 300ms）。
- 用户点击对话框的**下载**按钮时：
  - 收集 `Strategy`、`CustomFolder`、`ResolvedFolderName`、`PersistAlias`。
  - 调 resolver 拿到 `ResolverResult`：
    - 如果是 Custom 策略且 CustomFolder 为空，SubFolder 为空，文件落到根目录（无子目录）。
    - 如果是 ByUploader 策略且 UI 文本框有值，把该值作为 `ResolvedFolderName` 传入 resolver（让 UI 说了算）。
    - 如果是 ByUploader 策略且 UI 文本框为空（罕见），走自动解析。
  - 把 `SubFolder` 传给 `DownloadTaskDraftFactory` 的新参数。
  - 如果 `PersistAlias == true` 且 `Preview != null`：调 `repo.Upsert(Preview.Mid, SubFolder)` 写入别名表。

`ViewDownloadSetterViewModel` 本身**不直接改动**。面板对外暴露一个 `UploaderRoutingSnapshot` 对象（含 Strategy + CustomFolder + ResolvedFolderName + PersistAlias），现有 VM 在组装下载草稿时读取它。

### 自动模式无 UI

`IsAutoDownloadAll = Yes` 触发自动下载时：

- 不打开对话框
- 直接调 resolver，传入 `Strategy = ByUploader`
- 命中的直接拿映射名；未命中拿当前名字
- **不**写入别名表

## 兼容性与迁移

- **设置**：`ApplicationSettings.cs` **完全不动**。所有新数据在两个独立 JSON 文件中。
- **SQLite**：无 schema 变更。resolver 输出在草稿创建时写入现有的 `DownloadBase.FilePath` 字段。
- **在途任务**：`FilePath` 在入队时已经固化。resolver 只在入队时跑一次；暂停与恢复不会重跑，原子目录保留。
- **已完成历史任务**：不迁移。`FilePath` 已经在磁盘上，之后调整 resolver 行为也不会移动文件。
- **跨机器同步**：用户直接复制 `uploader-aliases.json` 即可（不含路由偏好，偏好不跨机器同步）。
- **上游 rebase**：仅有两处既有文件被触碰（`DownloadTaskDraftFactory.cs`、`ViewDownloadSetter.axaml`），且改动都是最小化、追加式的。`VideoView.cs`、`VideoInfoService.cs`、`VideoInfoView.cs` 完全不动。

## 失败模式

| 失败 | 行为 |
|---|---|
| 别名 `FolderName` 在本 OS 非法 | 消毒；若为空则提示用户重新输入 |
| 两个别名解析到同一个 `FolderName` | 允许；后续任务归入同一文件夹 |
| 自定义子目录名与非 DownKyi 既有目录冲突 | 路径构造器创建该目录；FFmpeg / 文件 IO 仅覆盖空文件 |
| 路由偏好文件损坏 | 视为首次启动，回退到默认 `ByUploader` + 空 Custom + `PersistAlias=true` |
| 别名表 JSON 损坏 | 视为空表，用户在别名页看到丢了数据，可以重新导入或重建 |
| 自动模式下未命中映射时持久化失败 | 自动模式不持久化，无影响 |
| 用户清空了"创建/更新映射"勾选项但编辑了文本框 | 文本框的值仍用于本次下载路径，但**不**写入别名表 |
| 视频没有 UP 主（Owner mid ≤ 0） | UI 自动把策略切到 Custom 并提示用户 |
| Custom 策略下用户没填目录名 | 文件落到根目录（无子目录），不影响其他逻辑 |

## 测试方案

按"两种模式 × 两种策略"组织对抗式矩阵测试（xunit v3）：

### 别名仓库

1. `FileUploaderAliasRepositoryTests`
   - `Load`：文件缺失 → 空表；JSON 损坏 → 空表 + 错误日志（不抛异常）。
   - `Save`：整体覆盖语义；原子写入在中断情况下不损坏目标文件。
   - `Upsert(mid, name)`：新增场景；覆盖已有 mid 的场景；mid 不存在则新增。
   - `Upsert` 的原子性：连续两次 Upsert 不会因中途失败丢失前一次结果。

### 路由偏好仓库

2. `FileUploaderRoutingPreferenceRepositoryTests`
   - 往返：load → save → load 结果一致。
   - 文件缺失 → 默认值（`ByUploader` + 空字符串 + `PersistAlias=true`）。
   - JSON 损坏 → 默认值 + 错误日志。

### Resolver（核心矩阵）

3. `DownloadSubFolderResolverTests`

   Custom 策略：
   - CustomFolder 非空 → 返回 trim 后的字符串，Preview = null。
   - CustomFolder 为空 → 返回空段，Preview = null（落根目录）。
   - CustomFolder 不查别名表。

   ByUploader 策略（自动解析，无 ResolvedFolderName）：
   - 命中别名 → Status=MatchedAlias，SubFolder=别名。
   - 未命中别名 → Status=WillCreateAlias，SubFolder=当前名字（Owner.Name）。
   - Owner 为空 → Status=NoUploader，SubFolder=空段。

   ByUploader 策略（用户编辑了文本框，传入 ResolvedFolderName）：
   - ResolvedFolderName 非空 → Preview 仍按 mid 算出（MatchedAlias/WillCreate），但 SubFolder = ResolvedFolderName.Trim()。
   - 用户改名后即便状态原本是 MatchedAlias，SubFolder 也是改名后的值。

### 调用方集成

4. `DownloadTaskDraftFactoryTests`（更新既有测试）
   - 默认（无 subfolder）→ 行为与基线完全一致。
   - 传入 subfolder → 路径形如 `root / subfolder / templateRelative`。
   - subfolder 含尾部斜杠会被裁掉。
   - subfolder 为空 → 不增加路径段。

### 别名持久化副作用

5. `UploaderAliasPersistenceTests`
   - 勾选 + Status=WillCreateAlias + 用户未改文本 → `Upsert(mid, currentName)`，添加新条目。
   - 勾选 + Status=MatchedAlias + 用户未改文本 → `Upsert(mid, aliasName)`，幂等（值未变）。
   - 勾选 + Status=MatchedAlias + 用户改了文本 → `Upsert(mid, newName)`，覆盖现有条目。
   - 不勾选 → 不调用 Upsert，别名表保持原样。
   - 策略 = Custom → Preview=null，不调用 Upsert。
   - Status = NoUploader → Preview.Mid ≤ 0，不调用 Upsert（避免无效条目）。

### UI

6. `ViewUploaderRoutingPanelTests`
   - 策略下拉框默认选中上次保存的策略。
   - 切换到 Custom 时显示文本框；切换到 ByUploader 时显示状态 + 文件夹编辑框 + 勾选框。
   - 改变 `ResolvedFolderName` 时预览路径同步刷新。
   - 视频没有 UP 主（Owner mid ≤ 0）时，自动切到 Custom 策略并提示用户。
   - Custom 策略下不显示 status / persist 勾选。
   - 切换策略 / 改动选项后偏好文件被更新（去抖后）。

7. `ViewUploaderAliasesViewModelTests`
   - 新增 / 编辑 / 删除别名后通过 repo 往返。
   - 导入校验 schema；JSON 损坏时给用户可见的错误。
   - 导出的 JSON 与 repo 写出的格式一致。

集成覆盖放在 `tests/DownKyi.Tests` 中，覆盖 `AddToDownloadService` 端到端。架构测试断言：

- `src/DownKyi.Desktop/Services/Uploader/` 下的新类型不依赖 `DownKyi.ViewModels` 或 Avalonia。
- `ViewUploaderAliasesPage` 与 `ViewUploaderRoutingPanel` 遵循既有 CommunityToolkit MVVM 模式（不引入第二套 IOC 容器）。

## 既有文件改动清单

| 文件 | 改动 |
|---|---|
| `src/DownKyi.Desktop/Services/Download/DownloadTaskDraftFactory.cs` | 一个函数签名新增可选参数（默认 `null`）；函数体增加约 3 行把 `subFolder` 拼到相对路径之前 |
| `src/DownKyi.Desktop/Views/Dialogs/ViewDownloadSetter.axaml` | 在布局网格中新增一个 `<views:ViewUploaderRoutingPanel />` 元素 |

其他所有改动都在新文件中。上游 rebase 的冲突面仅限这两处 hunk。

## 实施阶段

每个阶段是一个独立 commit，可在 PR 中独立评审。所有新文件落在
`src/DownKyi.Desktop/Services/Uploader/`、
`src/DownKyi.Desktop/Views/Settings/UploaderAliases/`、
`src/DownKyi.Desktop/Views/Dialogs/Components/`，与既有路径无重叠。

| Phase | 内容 | 状态 | 既有文件改动数 |
|---|---|---|---|
| 1 | 别名存储 + 仓库（`IUploaderAliasRepository` + 实现 + 测试） | ✅ 完成 | 0 |
| 2 | 路由偏好存储 + 仓库（`IUploaderRoutingPreferenceRepository` + 实现 + 测试） | ✅ 完成 | 0 |
| ~~3~~ | ~~B 站 `staff` 解析~~ | ❌ 删除 | — |
| 3（原 4） | Resolver（`DownloadSubFolderResolver` + 测试） | ✅ 完成 | 0 |
| 4（原 5） | `DownloadTaskDraftFactory` 集成 | ✅ 完成 | 1（`DownloadTaskDraftFactory.cs`） |
| 5（原 6） | 设置 UI：UP 主标签页 + 主开关 + 别名 CRUD | ✅ 完成 | 0 |
| 6（原 7） | 弹窗分流：原弹窗 vs 新弹窗 `ViewDownloadSetterWithSubFolder` + 入口路由 | ✅ 完成 | 0 |
| 7（原 8） | 新弹窗内嵌策略面板（Custom / ByUploader 下拉 + 状态文本 + 更新映射按钮） + 修复 SetDirectory/GetVideo 时序 | ✅ 完成 | 4（`IAddToDownloadSession.cs`、`AddToDownloadService.cs`、`VideoDetailDownloadCoordinator.cs`、`ContentDownloadCoordinator.cs`，加 1 个新文件 `SubFolderRouteResolver.cs`） |
| 8（原 9） | 自动模式接线 | 待开始 | 0–1 |

### Phase 7 的实现细节

与原文档 §"下载对话框：每任务策略"计划一致，但落地点不同：直接在新建的 `ViewDownloadSetterWithSubFolder.axaml` 里改（不引入独立的 `ViewUploaderRoutingPanel` UserControl）。

**新增文件**：

- `src/DownKyi.Desktop/Services/Uploader/SubFolderRouteResolver.cs` —— 从弹窗 result dict + UP 主 mid/name + 别名表算出最终子目录（封装 `LoadAliasesBestEffort` 异常吞咽 + 调用 `DownloadSubFolderResolver.Resolve`）。
- `tests/DownKyi.Tests/Dialogs/ViewDownloadSetterWithSubFolderViewModelTests.cs`

**修改文件**（动既有文件，但限定在最小范围）：

- `src/DownKyi.Desktop/ViewModels/Dialogs/ViewDownloadSetterWithSubFolderViewModel.cs`
  - 注入 `IUploaderRoutingPreferenceRepository` + `IUploaderAliasRepository`
  - 新增属性：`Strategy`（枚举）、`SubFolder`、`ResolutionStatus`、`StatusText`、`StrategyChoices`、`IsByUploader`、`IsCustom`、`IsUpdateAliasEnabled`
  - 新增命令：`UpdateAliasCommand`（ByUploader 策略下点击 → 把当前 `SubFolder` upsert 到别名表）
  - `OnDialogOpened`：根据偏好文件读取上次策略；ByUploader 模式自动查别名预填文本框 + 状态；Custom 模式预填上次的 CustomFolder（无则用 UP 主昵称）
  - `ExecuteDownloadCommand`：返回 `subFolder` + `strategy` 两个参数
- `src/DownKyi.Desktop/Views/Dialogs/ViewDownloadSetterWithSubFolder.axaml`
  - 把现有"子目录（可选）"一栏替换为：策略下拉 + 文本框（自适应显示） + 状态提示文字 + 更新映射按钮（仅 ByUploader 显示）
  - 所有新增文本用中文字面量（避免改 `Default.axaml` 引发 rebase 冲突）
- `src/DownKyi.Desktop/Services/Download/IAddToDownloadSession.cs`
  - 加一个 `void SetOwner(long ownerMid, string ownerName)` 方法。配合下面对协调器的修改，解决「`SetDirectory` 先于 `GetVideo` 导致弹窗拿不到 UP 主信息」的问题。
- `src/DownKyi.Desktop/Services/Download/AddToDownloadService.cs`
  - 注入新路由解析器 `SubFolderRouteResolver`（构造时由现有 `IUploaderAliasRepository` 组装，避免 DI 改 `DesktopComposition`）
  - `SetDirectory`：构造 dialogParameters 时优先用 `_pendingOwnerMid/_pendingOwnerName`（协调器注入），避免空 mid/name
  - `ResolveSubFolderFromDialogResult`：委托给 `_subFolderRouteResolver`，只剩一行调用
- `src/DownKyi.Desktop/Services/Video/VideoDetailDownloadCoordinator.cs`
  - `AddAsync` 立刻在 `_serviceFactory.Create` 之后调 `addService.SetOwner(videoInfoView.UpperMid, videoInfoView.UpName)` 把 UP 主信息提前灌给 session。
- `src/DownKyi.Desktop/Services/Media/ContentDownloadCoordinator.cs`
  - `AddAsync` 在弹目录窗之前用 `_infoServiceFactory.CreateAsync(selectedItems[0])` 拿到首项的 `VideoInfoView` 并调用 `addService.SetOwner`。
  - `AddItemsAsync` 签名改为 `ContentDownloadItem[]`（CA1859），并把已构造好的 first info service 直接传给循环，避免重复创建。

## 风险与未决问题

1. **`Format.FormatFileName` 偏向文件名**。它可能误删目录允许的字符（例如尾部点号）。可能需要新增一个 `FormatDirectoryName` 辅助函数，对跨平台规则更严格。
2. **跨机器别名迁移**：导出 / 导入格式必须稳定。
3. **一个 UP 主一个文件夹未必够用**。部分用户想要 `UpMid/UpName/` 双层以避免冲突。暂时出范围，后续可加一个设置开关。
4. **未来若上游用回 `staff` 字段**：本特性未读取该字段，所以上游变化不会冲突。如果将来要支持合作投稿，需要重新启用 staff 解析（届时可在不动既有文件的前提下加回）。
5. **Resolver 唯一所有者**：遵循 AGENTS.md，禁止并行所有者。resolver 是唯一所有者；设置存储和草稿工厂都是消费者。
6. **勾选项的副作用范围**：用户清空勾选后，编辑的文本框值仍用于本次下载路径，但不会写入别名表。下次打开对话框，状态会重新回到"准备新建映射"（因为别名表没变），文本框回到 resolver 给出的初始值。

## 验证

每个阶段完成后执行：

- 严格 Release 构建，所有 analyzer 启用。
- `script/test-review-invariants.ps1 -Configuration Release`。
- `script/test-solution.ps1 -Configuration Release`。
- `script/audit-module-boundaries.ps1` —— 无 ratchet 增长。
- `dotnet format --verify-no-changes`。
- `script/scan-secrets.ps1`。
- `git diff --check`。
- 新测试通过；既有测试仍绿。

## 暂不实现（未来）

- 合作投稿（多 UP 主）选择：等上游 `staff` 字段可用后再启用。
- 单文件夹配额 / 大小限制。
- 别名编辑后自动重命名 / 合并文件夹。
- 内置云同步（`DOWNKYI_UPLOADER_ALIAS_PATH` 环境变量已支持 Dropbox /
  Syncthing / Git 跟踪目录的工作流，无需额外代码）。
- 除 UP 主外的文件夹模板（日期、分区、合集）。
- 描述中的 `@昵称` mention 自动识别为相关 UP 主。
- 在 Custom 输入框中模糊搜索全局已有映射。
