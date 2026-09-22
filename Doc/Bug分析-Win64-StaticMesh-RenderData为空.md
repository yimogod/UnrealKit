# Bug 分析：Win64 IoStore StaticMesh RenderData 为空

## 现象

- **平台**：Win64 IoStore（`.utoc` / `.ucas`）
- **症状**：StaticMesh 资产的名称和路径能正确读取，但 LodCount / MaterialCount / 顶点数 / 三角形数全为 N/A。
- **对比**：同一工程的 Android IoStore 数据完全正常。

---

## 根因分析

### 1. UE5 引擎侧：Desktop 平台多写了 4 字节

UE5 C++ 中 `FStaticMeshRenderData::Serialize`（`StaticMesh.cpp` ~line 2446）有如下平台编译开关：

```cpp
#if PLATFORM_DESKTOP
if (bCooked) {
    int32 MinMobileLODIdx = 0;
    bool bShouldSerialize = CVarStaticMeshKeepMobileMinLODSettingOnDesktop.GetValueOnAnyThread() != 0;
    // ...
    if (bShouldSerialize) {
        Ar << MinMobileLODIdx;  // 写入 4 字节
    }
}
#endif // PLATFORM_DESKTOP
```

- **Win64（Desktop）cook**：`#if PLATFORM_DESKTOP` 成立，若 CVar `r.StaticMesh.KeepMobileMinLODSettingOnDesktop=1` 则在 `FStaticMeshRenderData` 数据头部写入 4 字节 `MinMobileLODIdx`。
- **Android cook**：`PLATFORM_DESKTOP` 不成立，该代码段根本不参与编译，pak 内**不存在**这 4 个字节。

### 2. CUE4Parse 侧：版本标志未被设置

CUE4Parse 的 `FStaticMeshRenderData` 构造函数（`FStaticMeshRenderData.cs` line 29）：

```csharp
if (Ar.Versions["StaticMesh.KeepMobileMinLODSettingOnDesktop"])
    _ = Ar.Read<int>(); // minMobileLODIdx
```

该版本标志在 `VersionContainer.cs` 中**默认为 `false`**：

```csharp
Options["StaticMesh.KeepMobileMinLODSettingOnDesktop"] = false;
```

正确的设置路径是：`AbstractFileProvider.LoadIniConfigs()` 解析 pak 内的 `DefaultEngine.ini`，若 `[ConsoleVariables]` 段中存在 `r.StaticMesh.KeepMobileMinLODSettingOnDesktop=1`，则将该标志置为 `true`。`LoadIniConfigs()` 由 `PostMount()` 调用。

### 3. 直接触发点：`PostMount()` 从未被调用

`PakScanService.cs`（原 line 140）：

```csharp
await provider.MountAsync();
// PostMount() 缺失 → ini 未解析 → 版本标志永远为 false
```

结果链：

```
PostMount() 未调用
  → DefaultEngine.ini 未解析
    → Versions["StaticMesh.KeepMobileMinLODSettingOnDesktop"] 保持 false
      → FStaticMeshRenderData 跳过读取 MinMobileLODIdx 的 4 字节
        → 后续所有字段读取偏移错位（+4 字节）
          → LOD 数组解析失败
            → RenderData 为 null
              → LodCount / MaterialCount / 顶点 / 三角形 = N/A
```

### 4. 为什么 Android 不受影响

Android pak 中 `FStaticMeshRenderData` 数据头部根本没有 `MinMobileLODIdx` 这 4 个字节。CUE4Parse 跳过读取和不跳过读取，在 Android 上读到的偏移一样正确，因此 Android 解析一直正常。

---

## 修复

**文件**：`UnrealKit.Core/PakScan/PakScanService.cs`

```csharp
await provider.MountAsync();
provider.PostMount();  // 解析 DefaultEngine.ini，设置平台相关版本标志
```

改动极小，`PostMount()` 是 CUE4Parse 设计用于此目的的标准接口。

---

## 实际验证结果

对 `E:\XGameProfile\Intermediate\Download\Win64\ProjectX_Dev_Lite` 运行 CLI 扫描后，诊断日志输出：

```
[Information] PKS_DBG: StaticMesh.KeepMobileMinLODSettingOnDesktop=True
              SkeletalMesh.KeepMobileMinLODSettingOnDesktop=False
              VersionOverrides=1
[Information] PKS_DBG: 首个 StaticMesh: XGame/Content/FluidFlux/Surface/Meshes/SM_DebugBounds
              bCooked=True  RenderData=LODs=1
[Information] PKS006: 扫描完成：34 Texture2D / 350 StaticMesh / 0 SkeletalMesh / 29 Material，共 716 个资产
```

**关键发现**：`VersionOverrides=1` 说明标志是由项目 `DefaultGame.ini` 的 `[UnrealKit.PakScan]` 节注入的，`pak` 内的 `DefaultEngine.ini` 并未包含该 CVar（否则 `PostMount()` 就够了，无需 `VersionOverrides`）。这意味着该工程 cook 时使用的引擎配置启用了此 CVar，但它没有被打进 pak 包，需要在项目侧手动声明。

350 个 StaticMesh 全部正确解析出 LodCount / VertexCount / TriangleCount，修复验证通过。

---

## 最终修复方案

### 第一步：`PakScanService.cs` — 调用 PostMount()

```csharp
await provider.MountAsync();
provider.PostMount();  // 尝试从 pak 内 DefaultEngine.ini 自动设置版本标志

// 无条件应用项目 DefaultGame.ini 的显式覆盖，优先级高于 pak 内 DefaultEngine.ini
foreach (var kv in config.VersionOverrides)
    provider.Versions[kv.Key] = kv.Value;
```

### 第二步：`PakScanConfig.cs` — 版本标志覆盖字段

```csharp
/// <summary>
/// 显式覆盖 CUE4Parse 版本标志，作为 pak 内 DefaultEngine.ini 的兜底。
/// PostMount() 之后无条件应用，优先级高于 pak 内 DefaultEngine.ini。
/// </summary>
public IReadOnlyDictionary<string, bool> VersionOverrides { get; init; } =
    new Dictionary<string, bool>();
```

### 第三步：项目 `Config/DefaultGame.ini` — 声明覆盖

```ini
[UnrealKit.PakScan]
StaticMesh.KeepMobileMinLODSettingOnDesktop=True
```

### 第四步：CLI — `--project` 参数

```
unrealkit parse pak-scan \
  --input <pak-dir> \
  --project <project.ukit 或项目根目录> \
  --aes-key <key> \
  --game-version GAME_UE5_6
```

---

## 优先级链

```
CUE4Parse 内置默认 (false)
  ← pak 内 DefaultEngine.ini [ConsoleVariables]（PostMount 自动读取）
    ← 项目 Config/DefaultGame.ini [UnrealKit.PakScan]（无条件覆盖，最高优先级）
```

---

## 关键文件索引

| 文件 | 作用 |
|------|------|
| `CUE4Parse/UE4/Assets/Exports/StaticMesh/FStaticMeshRenderData.cs` line 29 | 版本标志控制是否读取 MinMobileLODIdx |
| `CUE4Parse/UE4/Versions/VersionContainer.cs` line 99 | 标志默认值 false |
| `CUE4Parse/FileProvider/AbstractFileProvider.cs` line 441, 473 | LoadIniConfigs / 解析 CVar |
| `CUE4Parse/FileProvider/Vfs/AbstractVfsFileProvider.cs` line 504 | PostMount() 入口 |
| `UnrealKit.Core/PakScan/PakScanService.cs` | PostMount() + VersionOverrides 应用 |
| `UnrealKit.Core/PakScan/PakScanConfig.cs` | VersionOverrides 字段定义 |
| `UnrealKit.Cli/Commands/ParseCommands.cs` | CLI --project 参数，ReadPakScanVersionOverrides |
| `Engine/Source/Runtime/Engine/Private/StaticMesh.cpp` line 2446 | UE5 引擎侧序列化逻辑（Desktop 平台 +4 字节）|
