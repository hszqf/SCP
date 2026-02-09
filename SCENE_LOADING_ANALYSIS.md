# Scene Loading Analysis for SCP Project

## A) 入口场景加载机制 (Entry Scene Loading Mechanism)

### 关键发现：**本项目不使用 SceneManager.LoadScene**

经过完整代码搜索，本项目**没有任何地方调用 `SceneManager.LoadScene`**。

### 场景加载方式：单场景启动

本项目采用**单场景启动模式**：

1. **Build Settings 中只有一个场景**: `Assets/Scenes/Main.unity`
2. **启动时直接加载 Main.unity**（Unity自动加载Build Settings中的第一个场景）
3. **没有 Boot 场景切换逻辑**

### 证据代码片段

#### 1. Build Settings 配置
**文件**: `ProjectSettings/EditorBuildSettings.asset`
```yaml
EditorBuildSettings:
  m_Scenes:
  - enabled: 1
    path: Assets/Scenes/Main.unity
    guid: 8c9cfa26abfee488c85f1582747f6a02
```

#### 2. GameController 初始化（无场景加载）
**文件**: `Assets/Scripts/Runtime/GameController.cs`

```csharp
public class GameController : MonoBehaviour
{
    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);  // 跨场景持久化（但本项目实际上只有一个场景）
        _rng = new System.Random(seed);
    }

    private IEnumerator Start()
    {
        if (_initialized) yield break;
        Debug.Log($"[Boot] Platform={Application.platform}");
        
        // 加载游戏数据，但不加载其他场景
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // 从远程URL加载game_data.json
            yield return DataRegistry.LoadJsonTextCoroutine(...);
        }
        else
        {
            // 从本地加载game_data.json
            DataRegistry.LoadFromStreamingAssets();
        }
        
        // 初始化游戏状态
        State = Sim.InitWorld(_rng);
        // ... 但没有 SceneManager.LoadScene 调用
    }
}
```

#### 3. 自动启动机制（RuntimeInitializeOnLoadMethod）
**文件**: `Assets/Scripts/Runtime/Debug/LogOverlayBootstrap.cs`
```csharp
public static class LogOverlayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // 场景加载后自动创建 LogOverlay
        var go = new GameObject("LogOverlay");
        var overlay = go.AddComponent<LogOverlay>();
        Object.DontDestroyOnLoad(go);
    }
}
```

**文件**: `Assets/Scripts/Runtime/MapSystemDiagnostic.cs`
```csharp
public class MapSystemDiagnostic : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void DiagnoseMapSystem()
    {
        // 场景加载后自动诊断地图系统
        var simpleMapPanel = FindAnyObjectByType<SimpleWorldMapPanel>();
        // ...
    }
}
```

### 唯一的场景操作代码（仅用于编辑器工具）

**文件**: `Assets/Editor/AIBridge.cs` 和 `Assets/Scripts/Editor/MapSetupAutomation.cs`

这些文件使用 `EditorSceneManager`，但**仅在Unity编辑器中运行的工具**，不是游戏运行时逻辑：

```csharp
// Assets/Scripts/Editor/MapSetupAutomation.cs
var scene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
// 这是编辑器工具代码，用于自动化配置场景，不是运行时场景加载
```

---

## B) Build Settings 场景列表

### 当前配置（截至本次分析）

**Build Settings 中只有 1 个场景**：

```
0: Assets/Scenes/Main.unity
```

### 项目中存在的场景文件（未加入Build Settings）

目录: `Assets/Scenes/`
```
- Boot.unity         (存在但未加入Build Settings)
- Main.unity         (✓ 已加入Build Settings, 索引 0)
- AI_Test_A.unity    (测试场景，未加入Build Settings)
- AI_Test_B.unity    (测试场景，未加入Build Settings)
```

---

## 总结与建议

### 当前架构特点

1. **单场景架构**: 整个游戏在 `Main.unity` 一个场景中运行
2. **无场景切换**: 不存在场景加载/切换逻辑
3. **自动启动**: 使用 `RuntimeInitializeOnLoadMethod` 在场景加载后自动初始化组件
4. **跨场景持久化**: GameController 和 LogOverlay 使用 `DontDestroyOnLoad`，但实际上只运行在一个场景中

### 如何"一刀切断旧地图"

由于本项目不使用场景切换，要替换旧地图系统只需：

1. **在 Main.unity 场景中**:
   - 禁用/删除旧地图 GameObject (包含 `MapNodeSpawner` 组件的对象)
   - 确保新地图 `SimpleWorldMapPanel` 处于激活状态

2. **无需修改任何场景加载代码**（因为根本不存在）

3. **使用现有工具**: 
   ```
   Unity Editor > Tools > SCP > Setup Simple Map (Full)
   ```
   这个工具会自动配置 Main.unity 场景。

### Boot.unity 的状态

- `Boot.unity` 存在于项目中，但**未加入Build Settings**
- 因此启动时不会加载 Boot.unity
- 如果未来想使用 Boot → Main 的双场景架构，需要：
  1. 将 Boot.unity 加入Build Settings并设为索引0
  2. 在 Boot.unity 中添加场景加载脚本
  3. 修改 Main.unity 为索引1
