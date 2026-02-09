# 调试按键说明文档

## 🎮 调试按键功能

### `/` 斜杠键
**功能**：将配置文件设置为只有一个方块
- 立即将当前场景中的Rocket设置为只有Layer1的Cube13
- 保存此状态到配置文件 `Assets/RocketStatus.txt`
- 在任何场景中都可使用

### `\` 反斜杠键
**功能**：清空配置文件
- 清空 `Assets/RocketStatus.txt` 文件内容
- 下次加载Rocket时会自动初始化为单个方块（因为配置文件为空）
- 在任何场景中都可使用

### `Enter` 回车键
**功能**：在Workshop和Main场景之间切换
- 带淡入淡出过渡效果
- 自动保存和加载Rocket状态
- 只在Workshop或Main场景中有效

---

## 🔧 Unity场景设置

### 必需的GameObject设置

#### 1. **RocketStateManager**（DontDestroyOnLoad）
- 创建空GameObject，命名为 "RocketStateManager"
- 添加脚本：`RocketStateManager.cs`
- 设置：
  - Rocket Container Name: "Rocket"
- 此对象会自动设置为DontDestroyOnLoad

#### 2. **SceneTransitionManager**（DontDestroyOnLoad）
- 创建空GameObject，命名为 "SceneTransitionManager"
- 添加脚本：`SceneTransitionManager.cs`
- 设置：
  - Fade Duration: 0.3
- 此对象会自动设置为DontDestroyOnLoad

#### 3. **DebugKeyManager**（DontDestroyOnLoad）
- 创建空GameObject，命名为 "DebugKeyManager"
- 添加脚本：`DebugKeyManager.cs`
- 设置：
  - Rocket Container Name: "Rocket"
- 建议在第一个加载的场景（如Menu场景）中创建
- 可以手动设置DontDestroyOnLoad，或让它在每个场景都存在

#### 4. **SceneSwitcher**（每个场景都需要）
- 在Workshop和Main场景中分别创建空GameObject，命名为 "SceneSwitcher"
- 添加脚本：`SceneSwitcher.cs`
- 设置：
  - Workshop Scene Name: "Workshop"
  - Main Scene Name: "Main"

---

## 📋 工作流程说明

### 启动流程
1. **加载场景**（Workshop或Main）
2. **RocketStateManager检查配置文件**：
   - 如果文件存在且有内容 → 加载保存的构筑
   - 如果文件为空或不存在 → 初始化为单个方块并保存

### 场景切换流程
1. **按Enter键**
2. **SceneSwitcher触发切换**
3. **RocketStateManager保存当前状态**
4. **淡出效果**
5. **加载目标场景**
6. **RocketStateManager应用保存的状态**
7. **淡入效果**

### 调试流程
- **按 `/` 键** → 重置为单个方块并保存
- **按 `\` 键** → 清空配置文件（下次加载时会重新初始化）

---

## 🗂️ 配置文件格式

**文件位置**：`Assets/RocketStatus.txt`

**格式**：每行4个字符
```
层级(1位) + 方块编号(2位) + 启用状态(1位)
```

**示例**：
```
1131  → Layer1, Cube13, 启用（1=启用）
2050  → Layer2, Cube05, 禁用（0=禁用）
3101  → Layer3, Cube10, 启用
```

**清空状态**：
- 文件为空或不存在时，系统会自动初始化为单个方块

---

## ⚠️ 注意事项

1. **RocketStateManager必须是DontDestroyOnLoad**
   - 确保跨场景时管理器不被销毁

2. **Rocket对象必须命名为"Rocket"**
   - 或在所有管理器脚本中修改 `rocketContainerName` 为相同的名称

3. **Layer和Cube命名规范**
   - Layer必须命名为：Layer1, Layer2, Layer3, Layer4, Layer5
   - Cube必须命名为：Cube01, Cube02, ..., Cube99

4. **Build Settings场景顺序**
   - 确保Workshop和Main场景都添加到Build Settings中

---

## 🐛 调试信息

所有操作都会在Console输出日志：
- `[DebugKeyManager]` - 调试按键操作
- `[RocketStateManager]` - 状态管理操作
- `[SceneSwitcher]` - 场景切换操作
- `[SceneTransitionManager]` - 过渡效果操作
