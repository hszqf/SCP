# 修复运行时显示旧地图问题

## 问题诊断

运行时仍然显示旧界面/旧地图，新做的 SimpleWorldMap 没有出现在游戏里。

### 根本原因

1. **SimpleWorldMapPanel 未在场景中实例化** - 新地图组件不存在于 Main.unity 场景中
2. **地图 Prefab 未生成** - Assets/Prefabs/UI/Map/ 目录不存在，所需的 prefab 文件未创建
3. **旧地图系统仍处于活动状态** - MapNodeSpawner (旧地图系统) 仍在场景中并且是激活状态
4. **MapSystemManager 未配置** - 没有组件来管理新旧地图的切换

## 修复方案

### 选项 A: 使用 Unity Editor 自动修复 (推荐)

1. 在 Unity Editor 中打开项目
2. 打开菜单：**Tools > SCP > Setup Simple Map (Full)**
3. 这会自动完成以下操作：
   - 生成所有必需的 Map prefabs
   - 在 Main.unity 中添加 SimpleWorldMapPanel
   - 添加 MapSystemManager 来控制新旧地图
   - 添加 DispatchLineFX 用于动画效果
   - 配置所有引用关系
4. 保存场景并运行游戏

### 选项 B: 手动修复步骤

#### 步骤 1: 生成 Map Prefabs

1. 打开 Unity Editor
2. 菜单：**Tools > SCP > Generate Map Prefabs Only**
3. 确认 Assets/Prefabs/UI/Map/ 目录下生成了以下文件：
   - AgentAvatar.prefab
   - TaskBar.prefab
   - AnomalyPin.prefab
   - HQMarker.prefab
   - NodeMarker.prefab
   - SimpleWorldMapPanel.prefab

#### 步骤 2: 设置 Main 场景

1. 打开 **Assets/Scenes/Main.unity**
2. 找到 Canvas 对象
3. 将 **SimpleWorldMapPanel.prefab** 拖入 Canvas 作为子对象
4. 设置 SimpleWorldMapPanel 的 RectTransform：
   - Anchor: Stretch (全屏)
   - Offset: 0, 0, 0, 0
5. 在 SimpleWorldMapPanel 组件中，分配引用：
   - **Node Marker Prefab**: 拖入 NodeMarker.prefab
   - **HQ Marker Prefab**: 拖入 HQMarker.prefab

#### 步骤 3: 添加其他组件

1. 在 Canvas 下创建空对象，命名为 **DispatchLineFX**
2. 添加 **DispatchLineFX** 组件
3. 在 Canvas 下创建空对象，命名为 **MapSystemManager**
4. 添加 **MapSystemManager** 组件
5. 在 MapSystemManager 组件中配置：
   - **Old Map System**: 拖入 MapRoot 或 NodeLayer (包含 MapNodeSpawner 的对象)
   - **Simple World Map Panel**: 拖入 SimpleWorldMapPanel 对象
   - **Use Simple Map**: 勾选 ✓

#### 步骤 4: 隐藏旧地图

方式 1 (推荐): 使用 MapSystemManager (上面步骤 3 已配置)
方式 2 (手动): 在 Hierarchy 中找到 MapRoot 或 NodeLayer，取消勾选激活状态

#### 步骤 5: 保存并测试

1. 保存场景 (Ctrl+S)
2. 运行游戏 (Play)
3. 检查 Console 日志：
   - 应该看到 `[MapUI] Initializing simple world map`
   - 应该看到 `[MapUI] Simple world map enabled`
   - 应该看到 `[MapUI] Old map system disabled`

## 验证

### 运行时诊断

游戏启动时会自动运行诊断脚本 (MapSystemDiagnostic.cs)，在 Console 中输出：

```
===========================================
[MapUI] Map System Diagnostic Starting...
===========================================
[MapUI] ✓ SimpleWorldMapPanel found: SimpleWorldMapPanel
[MapUI] ✓ SimpleWorldMapPanel active: True
[MapUI] ⚠ Old map system still active: NodeLayer
[MapUI] ✓ MapSystemManager found: MapSystemManager
...
===========================================
[MapUI] Map System Diagnostic Complete
===========================================
```

### 正确的诊断结果应该是：

```
✓ SimpleWorldMapPanel found
✓ SimpleWorldMapPanel active: True
✓ MapSystemManager found
✓ DispatchLineFX found
✓ GameController found
✓ UIPanelRoot found
✓ Canvas found
```

### 如果看到错误：

**错误 1**: `❌ SimpleWorldMapPanel NOT in scene!`
- **原因**: 新地图未添加到场景
- **修复**: 按照上面步骤 2 手动添加，或运行 Tools > SCP > Setup Simple Map (Full)

**错误 2**: `⚠ Old map system still active`
- **原因**: 旧地图仍在运行
- **修复**: 按照步骤 3-4 配置 MapSystemManager 或手动禁用旧地图

**错误 3**: `SimpleWorldMapPanel exists but is inactive!`
- **原因**: SimpleWorldMapPanel 被禁用
- **修复**: 在 Hierarchy 中找到它并勾选激活

## 架构说明

### 新地图系统组件

1. **SimpleWorldMapPanel** - 主地图控制器
   - 管理 HQ + 3 个城市节点
   - 纯色背景（深蓝灰色）
   - 固定节点位置布局

2. **NodeMarkerView** - 节点标记
   - 显示城市名称
   - 任务进度条
   - 异常标记
   - 注意徽章

3. **DispatchLineFX** - 派遣动画
   - HQ 到节点的飞行线条
   - 任务完成特效

4. **MapSystemManager** - 地图切换管理
   - 控制新旧地图可见性
   - useSimpleMap = true 时显示新地图

### 旧地图系统 (需要禁用)

- **MapNodeSpawner** - 旧地图生成器
- **MapRoot** / **NodeLayer** - 旧地图容器

## 已生成的脚本

1. **MapSetupAutomation.cs** (Editor) - 自动化设置工具
2. **MapSystemDiagnostic.cs** (Runtime) - 运行时诊断工具
3. **MapSystemManager.cs** (已存在) - 地图系统管理器
4. **SimpleWorldMapPanel.cs** (已存在) - 简化地图面板

## 技术细节

### Prefab 依赖关系

```
SimpleWorldMapPanel
├─ nodeMarkerPrefab → NodeMarker
│  ├─ taskBarPrefab → TaskBar
│  │  └─ agentAvatarPrefab → AgentAvatar
│  └─ anomalyPinPrefab → AnomalyPin
└─ hqMarkerPrefab → HQMarker
```

### 场景层级结构

```
Canvas
├─ UIPanelRoot
├─ SimpleWorldMapPanel  ← 新地图 (需要添加)
│  └─ MapContainer
│     ├─ HQMarker (运行时生成)
│     └─ NodeMarkers (运行时生成)
├─ DispatchLineFX  ← 动画系统 (需要添加)
├─ MapSystemManager  ← 切换管理器 (需要添加)
└─ MapRoot  ← 旧地图 (需要禁用)
   ├─ Image
   └─ NodeLayer
      └─ MapNodeSpawner
```

## 常见问题

### Q: 为什么不直接删除旧地图？

A: 保留旧地图是为了兼容性。使用 MapSystemManager 可以在需要时切换回旧地图。如果确定不再需要旧地图，可以直接删除 MapRoot 对象。

### Q: Prefabs 生成失败怎么办？

A: 手动创建 Assets/Prefabs/UI/Map/ 目录，然后重新运行 Tools > SCP > Generate Map Prefabs Only。

### Q: 运行时看不到地图？

A: 检查 Console 日志中的诊断信息。最常见的原因是 SimpleWorldMapPanel 未激活或引用未正确配置。

### Q: 能否在 WebGL 构建中使用？

A: 是的，新地图系统完全兼容 WebGL。确保在构建前完成上述设置步骤。

## 相关文档

- `README_SimpleWorldMap.md` - 简化地图功能文档
- `Docs/SimpleWorldMapSetup.md` - 详细设置指南
- `IMPLEMENTATION_SUMMARY_SimpleWorldMap.md` - 实现总结

## 支持

如有问题，请：
1. 查看 Console 中的 [MapUI] 日志
2. 运行诊断脚本查看详细信息
3. 参考上述文档

---

**最后更新**: 2026-02-09
**版本**: 1.0
