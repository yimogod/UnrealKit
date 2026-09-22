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

## 备用方案

若 pak 内的 `DefaultEngine.ini` 没有设置该 CVar（即 `PostMount()` 后标志仍为 `false`），可在 `PakScanConfig` 中加入显式覆盖：

```csharp
// PakScanConfig.cs
/// <summary>显式覆盖 CUE4Parse 版本标志</summary>
public IReadOnlyDictionary<string, bool>? VersionOverrides { get; init; }
```

```csharp
// PakScanService.cs，PostMount() 之后
if (config.VersionOverrides != null)
    foreach (var kv in config.VersionOverrides)
        provider.Versions[kv.Key] = kv.Value;
```

调用方传入：

```csharp
new PakScanConfig {
    VersionOverrides = new Dictionary<string, bool> {
        ["StaticMesh.KeepMobileMinLODSettingOnDesktop"] = true
    }
}
```

---

## 关键文件索引

| 文件 | 作用 |
|------|------|
| `CUE4Parse/UE4/Assets/Exports/StaticMesh/FStaticMeshRenderData.cs` line 29 | 版本标志控制是否读取 MinMobileLODIdx |
| `CUE4Parse/UE4/Versions/VersionContainer.cs` line 99 | 标志默认值 false |
| `CUE4Parse/FileProvider/AbstractFileProvider.cs` line 441, 473 | LoadIniConfigs / 解析 CVar |
| `CUE4Parse/FileProvider/Vfs/AbstractVfsFileProvider.cs` line 504 | PostMount() 入口 |
| `UnrealKit.Core/PakScan/PakScanService.cs` line 140 | 修复点：加 PostMount() |
| `Engine/Source/Runtime/Engine/Private/StaticMesh.cpp` line 2446 | UE5 引擎侧序列化逻辑 |
