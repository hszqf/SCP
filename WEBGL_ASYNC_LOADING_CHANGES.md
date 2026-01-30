# WebGL 异步 JSON 加载改造总结

## 问题背景

在 WebGL 平台上，原始代码使用 busy-wait 循环等待 UnityWebRequest 完成：
```csharp
var op = request.SendWebRequest();
while (!op.isDone) { }  // ❌ 阻塞浏览器事件循环
```

这导致浏览器无法处理其他事件，请求永远无法完成，游戏卡在加载进度条最后。

## 解决方案

### 1. DataRegistry.cs 改动

#### 修改 LoadJsonText (私有)
- **移除** WebGL 分支的 busy-wait 循环
- **改为** 在 WebGL 下抛出异常，提示必须使用异步版本
- **保留** 非 WebGL 平台的同步 File.ReadAllText（Editor/Standalone）

```csharp
private static string LoadJsonText(string path)
{
    if (Application.platform == RuntimePlatform.WebGLPlayer)
    {
        throw new InvalidOperationException(
            "[DataRegistry] WebGL platform must use async JSON loading. Use LoadJsonTextAsync() coroutine instead.");
    }
    
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"[DataRegistry] Missing game data at: {path}", path);
    }
    
    return File.ReadAllText(path);
}
```

#### 新增 LoadJsonTextAsync (公开协程)
- 接收 URL 和两个回调：`onOk(jsonText)` 和 `onErr(exception)`
- 使用 `yield return request.SendWebRequest()` 非阻塞式等待
- 包含详细日志：
  - 开始加载：`[DataRegistry] Starting async JSON load from: {url}`
  - 成功：`[DataRegistry] Successfully loaded JSON (length={len})`
  - 失败：`[DataRegistry] Failed to load JSON from {url}: {error}`

```csharp
public static IEnumerator LoadJsonTextAsync(string url, Action<string> onOk, Action<Exception> onErr)
{
    Debug.Log($"[DataRegistry] Starting async JSON load from: {url}");
    using var request = UnityWebRequest.Get(url);
    yield return request.SendWebRequest();

    if (request.result != UnityWebRequest.Result.Success)
    {
        var error = new InvalidOperationException(
            $"[DataRegistry] Failed to load JSON from {url}: {request.error}");
        Debug.LogError($"[DataRegistry] {error.Message}");
        onErr?.Invoke(error);
        yield break;
    }

    var text = request.downloadHandler.text;
    Debug.Log($"[DataRegistry] Successfully loaded JSON (length={text?.Length ?? 0})");
    onOk?.Invoke(text);
}
```

#### 新增 InitializeFromText (实例方法)
- 接收已加载的 JSON 文本
- 执行反序列化、索引构建、数据验证（同 Reload 的后半部分）
- 供异步路径调用

```csharp
public void InitializeFromText(string jsonText)
{
    // 反序列化 JSON
    var settings = new JsonSerializerSettings { ... };
    Root = JsonConvert.DeserializeObject<GameDataRoot>(jsonText, settings) ?? new GameDataRoot();
    
    // 构建索引 + 验证
    BuildIndexes();
    GameDataValidator.ValidateOrThrow(this);
    LogSummary();
}
```

#### 新增 using
- 添加 `using System.Collections;` 支持 IEnumerator

### 2. GameController.cs 改动

#### 新增 using
- 添加 `using System.Collections;` 支持协程
- 添加 `using System.IO;` 支持 Path.Combine

#### 重构 Awake()
原始 Awake 直接访问 `DataRegistry.Instance`，会同步加载（所有平台）。

**新逻辑：**
- 检测平台：`if (Application.platform == RuntimePlatform.WebGLPlayer)`
- **WebGL**：调用 `StartCoroutine(InitializeAsync())`
- **非WebGL**：调用 `InitializeSync()`

```csharp
private void Awake()
{
    if (I != null) { Destroy(gameObject); return; }
    I = this;
    DontDestroyOnLoad(gameObject);

    _rng = new System.Random(seed);
    Sim.OnIgnorePenaltyApplied += HandleIgnorePenaltyApplied;

    // Check platform and choose sync or async initialization
    if (Application.platform == RuntimePlatform.WebGLPlayer)
    {
        StartCoroutine(InitializeAsync());
    }
    else
    {
        InitializeSync();
    }
}
```

#### 新增 InitializeSync()
- 非 WebGL 平台的同步初始化
- 调用 `DataRegistry.Instance`（触发磁盘加载）
- 调用 `InitializeGameState(registry)`

```csharp
private void InitializeSync()
{
    Debug.Log("[GameController] Initializing with sync DataRegistry load (non-WebGL)");
    var registry = DataRegistry.Instance;
    InitializeGameState(registry);
}
```

#### 新增 InitializeAsync()
- WebGL 平台的异步初始化（协程）
- 创建 DataRegistry 实例（不调用 Reload）
- 启动 `DataRegistry.LoadJsonTextAsync()` 协程
- 在回调中调用 `registry.InitializeFromText(jsonText)`
- 错误处理和日志

```csharp
private IEnumerator InitializeAsync()
{
    Debug.Log("[GameController] Starting async DataRegistry load (WebGL)");
    
    var registry = new DataRegistry();
    string path = Path.Combine(Application.streamingAssetsPath, "game_data.json");
    bool loadSuccess = false;
    Exception loadError = null;

    // Start async JSON load
    yield return DataRegistry.LoadJsonTextAsync(
        path,
        onOk: (jsonText) =>
        {
            try
            {
                registry.InitializeFromText(jsonText);
                loadSuccess = true;
                Debug.Log("[GameController] Async JSON load and initialization complete");
            }
            catch (Exception ex)
            {
                loadError = ex;
                Debug.LogError($"[GameController] Failed to initialize registry from async-loaded JSON: {ex}");
            }
        },
        onErr: (error) =>
        {
            loadError = error;
            Debug.LogError($"[GameController] Async JSON load failed: {error}");
        }
    );

    if (!loadSuccess)
    {
        Debug.LogError($"[GameController] Game initialization failed. Error: {loadError?.Message ?? "Unknown"}");
        yield break;
    }

    InitializeGameState(registry);
}
```

#### 新增 InitializeGameState()
- 提取原 Awake 中的 Balance 读取 + 节点初始化逻辑
- 接收已初始化的 registry 作为参数
- 非平台相关，两个路径（sync/async）都调用

```csharp
private void InitializeGameState(DataRegistry registry)
{
    if (registry == null)
    {
        Debug.LogError("[GameController] Registry is null; cannot initialize game state");
        return;
    }

    // 读取 Balance 表
    int startMoney = registry.GetBalanceIntWithWarn("StartMoney", 0);
    // ...
    
    // 初始化节点
    foreach (var nodeDef in registry.NodesById.Values.OrderBy(n => n.nodeId))
    {
        // ...
        State.Nodes.Add(nodeState);
    }

    Notify();
}
```

## 日志示例

### 同步加载（非WebGL）
```
[GameController] Initializing with sync DataRegistry load (non-WebGL)
[DataRegistry] schema=1.0 dataVersion=v1.0 events=10 options=20 news=5 effects=15 ops=30
[Data] Loaded successfully
```

### 异步加载（WebGL）
```
[GameController] Starting async DataRegistry load (WebGL)
[DataRegistry] Starting async JSON load from: /StreamingAssets/game_data.json
[DataRegistry] Successfully loaded JSON (length=45832)
[GameController] Async JSON load and initialization complete
[DataRegistry] schema=1.0 dataVersion=v1.0 events=10 options=20 news=5 effects=15 ops=30
[Data] Loaded successfully
```

### 失败案例
```
[GameController] Starting async DataRegistry load (WebGL)
[DataRegistry] Starting async JSON load from: /StreamingAssets/game_data.json
[DataRegistry] Failed to load JSON from /StreamingAssets/game_data.json: Error 404
[GameController] Async JSON load failed: System.InvalidOperationException: [DataRegistry] Failed to load JSON...
[GameController] Game initialization failed. Error: [DataRegistry] Failed to load JSON...
```

## 关键改变点

| 方面 | 前 | 后 |
|-----|----|----|
| **WebGL 加载** | busy-wait（死锁） | 非阻塞协程（正常） |
| **非WebGL 加载** | 同步 (变化) | 同步（无变化） |
| **启动流程** | 单一路径（Awake → Reload）| 双路径（平台检测 → Sync/Async） |
| **错误处理** | 异常卡死 | 详细日志 + 回调处理 |
| **数据初始化** | 在 Reload 中 | 分离到 InitializeFromText / InitializeGameState |

## 测试清单

- [ ] 非WebGL 平台（Editor/Standalone）启动正常
  - 同步加载 game_data.json
  - 游戏进入正常流程
  
- [ ] WebGL 平台启动正常
  - 异步加载 game_data.json（无卡顿）
  - 加载完成后游戏进入正常流程
  - 浏览器控制台输出加载日志
  
- [ ] 错误处理
  - game_data.json 不存在 → 错误日志 + 游戏不启动
  - 网络超时 → 错误回调 + 日志
  - JSON 格式错误 → 反序列化失败 + 日志

## StreamingAssets 路径

确保 `game_data.json` 放在：
```
Assets/StreamingAssets/game_data.json
```

Unity 会自动打包到构建输出，WebGL 可通过 `Application.streamingAssetsPath` 访问。
