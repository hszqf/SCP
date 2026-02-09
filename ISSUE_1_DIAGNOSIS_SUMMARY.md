# Issue #1 诊断与修复总结

## 问题描述

运行时仍显示旧界面/旧地图，新做的 SimpleWorldMap 没有出现在游戏里。

## 根本原因分析

经过代码库分析，发现以下问题：

### 1. SimpleWorldMap 系统存在但未激活

代码库中已有完整的 SimpleWorldMap 实现：
- ✅ `SimpleWorldMapPanel.cs` - 简化地图主控制器
- ✅ `NodeMarkerView.cs` - 节点标记视图
- ✅ `MapSystemManager.cs` - 新旧地图切换管理器
- ✅ `DispatchLineFX.cs` - 派遣动画系统
- ✅ `SimpleMapPrefabGenerator.cs` - Prefab 生成工具

### 2. 缺少的关键组件

⚠️ **Assets/Prefabs/UI/Map/ 目录不存在**
- 需要的 6 个 prefab 文件未生成：
  - AgentAvatar.prefab
  - TaskBar.prefab
  - AnomalyPin.prefab
  - HQMarker.prefab
  - NodeMarker.prefab
  - SimpleWorldMapPanel.prefab

⚠️ **Main.unity 场景未配置**
- SimpleWorldMapPanel 未实例化到场景中
- MapSystemManager 未添加到场景
- DispatchLineFX 未添加到场景
- 旧地图系统 (MapNodeSpawner) 仍处于激活状态

### 3. 旧地图系统仍在运行

Main.unity 场景中的当前状态：
```
Canvas
├─ UIPanelRoot ✓
├─ MapRoot ⚠️ (旧地图，仍激活)
│  └─ NodeLayer
│     └─ MapNodeSpawner (旧地图生成器)
└─ [SimpleWorldMapPanel 不存在] ❌
```

应该的状态：
```
Canvas
├─ UIPanelRoot ✓
├─ SimpleWorldMapPanel ← 新地图
├─ DispatchLineFX ← 动画
├─ MapSystemManager ← 切换管理
└─ MapRoot (旧地图，应禁用)
```

## 已实施的修复

由于无法在 GitHub Actions 环境中运行 Unity Editor，我已创建以下工具和文档：

### 1. 运行时诊断脚本

**文件**: `Assets/Scripts/Runtime/MapSystemDiagnostic.cs`

**功能**: 游戏启动时自动运行，诊断并报告地图系统状态

**输出示例**:
```
===========================================
[MapUI] Map System Diagnostic Starting...
===========================================
❌ ISSUE FOUND: SimpleWorldMapPanel NOT in scene!
The new simplified map is not instantiated.
SOLUTION: Open Unity Editor and run 'Tools > SCP > Setup Simple Map (Full)'

⚠ Old map system still active: NodeLayer
...
╔═══════════════════════════════════════════════════════════════╗
║  DIAGNOSIS: Old map is showing because new map is missing!    ║
║                                                               ║
║  FIX REQUIRED:                                                ║
║  1. Open project in Unity Editor                              ║
║  2. Go to: Tools > SCP > Setup Simple Map (Full)              ║
║  3. This will generate prefabs and add SimpleWorldMapPanel    ║
║  4. Play the game again                                       ║
╚═══════════════════════════════════════════════════════════════╝
```

### 2. 自动化设置工具

**文件**: `Assets/Scripts/Editor/MapSetupAutomation.cs`

**功能**: Unity Editor 菜单工具，自动完成所有设置

**使用方法**:
1. 打开 Unity Editor
2. 菜单：**Tools > SCP > Setup Simple Map (Full)**
3. 自动执行：
   - 生成 6 个 map prefabs
   - 在 Main.unity 中添加 SimpleWorldMapPanel
   - 添加 MapSystemManager 并配置
   - 添加 DispatchLineFX
   - 保存场景

**替代选项**: **Tools > SCP > Generate Map Prefabs Only** (仅生成 prefabs)

### 3. Bash 脚本（可选）

**文件**: `setup_simple_map.sh`

**功能**: 通过 Unity 批处理模式自动运行设置

**使用方法**:
```bash
./setup_simple_map.sh
```

### 4. 详细文档

**文件**: `FIX_OLD_MAP_DISPLAY_ISSUE.md`

**内容**:
- 问题诊断详解
- 自动修复方案（选项 A）
- 手动修复步骤（选项 B）
- 验证方法
- 架构说明
- 常见问题解答

## 完整修复步骤

### 方法 A: 自动化修复（推荐）

```
1. 打开 Unity Editor
2. 确保打开的是 hszqf/SCP 项目
3. 菜单栏点击: Tools > SCP > Setup Simple Map (Full)
4. 等待完成（Console 会显示进度）
5. 保存场景 (Ctrl+S / Cmd+S)
6. 运行游戏测试
```

### 方法 B: 使用 Bash 脚本

```bash
cd /path/to/SCP
./setup_simple_map.sh
```

### 方法 C: 手动修复

详见 `FIX_OLD_MAP_DISPLAY_ISSUE.md` 的"选项 B: 手动修复步骤"部分。

## 验证修复

### 检查清单

运行游戏后，在 Console 中查找：

✅ **成功标志**:
```
[MapUI] ✓ SimpleWorldMapPanel found: SimpleWorldMapPanel
[MapUI] ✓ SimpleWorldMapPanel active: True
[MapUI] ✓ MapSystemManager found: MapSystemManager
[MapUI] ✓ DispatchLineFX found: DispatchLineFX
[MapUI] Old map system disabled
```

✅ **地图显示**:
- 纯色深蓝灰背景
- HQ 标记在底部中央
- N1, N2, N3 城市标记分别在左、右、上方位置
- 点击城市可打开面板

❌ **失败标志**:
```
[MapUI] ❌ SimpleWorldMapPanel NOT in scene!
[MapUI] ⚠ Old map system still active
```

## 技术细节

### 为什么不能自动修复？

Unity 场景 (.unity) 和 Prefab (.prefab) 是二进制/YAML 格式，需要 Unity Editor 的序列化系统来正确创建和修改。GitHub Actions 环境中没有 Unity Editor GUI，因此无法直接生成这些资源。

### 设置工具的工作原理

`MapSetupAutomation.cs` 使用 Unity Editor API：
1. `PrefabUtility.SaveAsPrefabAsset()` - 创建 prefab 资源
2. `EditorSceneManager` - 修改场景
3. `SerializedObject` / `SerializedProperty` - 配置组件引用
4. 程序化创建 GameObject 层级结构

### 新旧地图切换

`MapSystemManager` 组件控制：
```csharp
if (useSimpleMap)
{
    oldMapSystem.SetActive(false);  // 禁用旧地图
    simpleWorldMapPanel.SetActive(true);  // 启用新地图
}
```

## 已知限制

1. **必须使用 Unity Editor** - 无法通过 CI/CD 自动完成设置
2. **需要手动保存场景** - 修改后必须保存 Main.unity
3. **Prefab 依赖** - SimpleWorldMapPanel 需要所有子 prefabs 存在

## 后续建议

### 短期
1. 立即运行 `Tools > SCP > Setup Simple Map (Full)` 完成设置
2. 测试新地图在各种场景下的表现
3. 如有问题，查看 Console 中的 [MapUI] 日志

### 长期
1. 将生成的 prefabs 提交到版本控制
2. 将配置好的 Main.unity 提交到版本控制
3. 考虑在 CI 流程中添加验证步骤，检查 prefabs 是否存在

## 相关文件

### 新增文件
- `Assets/Scripts/Runtime/MapSystemDiagnostic.cs` - 运行时诊断
- `Assets/Scripts/Editor/MapSetupAutomation.cs` - 自动化设置工具
- `FIX_OLD_MAP_DISPLAY_ISSUE.md` - 详细修复指南
- `setup_simple_map.sh` - Bash 自动化脚本
- `ISSUE_1_DIAGNOSIS_SUMMARY.md` - 本文件

### 现有文件（相关）
- `Assets/Scripts/UI/Map/SimpleWorldMapPanel.cs` - 简化地图面板
- `Assets/Scripts/UI/Map/MapSystemManager.cs` - 地图系统管理器
- `Assets/Scripts/UI/Map/NodeMarkerView.cs` - 节点标记视图
- `Assets/Scripts/UI/Map/DispatchLineFX.cs` - 派遣动画
- `Assets/Scripts/Editor/SimpleMapPrefabGenerator.cs` - Prefab 生成器
- `README_SimpleWorldMap.md` - SimpleWorldMap 功能说明
- `Docs/SimpleWorldMapSetup.md` - 详细设置指南

## 联系与支持

如有问题：
1. 查看 `FIX_OLD_MAP_DISPLAY_ISSUE.md` 的常见问题部分
2. 检查 Console 日志中的诊断信息
3. 参考 `README_SimpleWorldMap.md` 了解功能详情

---

**创建日期**: 2026-02-09
**问题编号**: Issue #1
**状态**: 诊断完成，等待在 Unity Editor 中执行修复
**优先级**: 高
