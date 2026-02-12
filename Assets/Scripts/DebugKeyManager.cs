using UnityEngine;

/// <summary>
/// 全局调试键管理器 - 处理调试快捷键
/// / 斜杠：将Rocket和配置文件改为只有一个方块
/// \ 反斜杠：清空配置文件并重新加载（会初始化为单个方块）
/// Enter：在Workshop和Main场景之间切换（由SceneSwitcher处理）
/// </summary>
#if UNITY_EDITOR
using UnityEngine;

[System.Obsolete("DebugKeyManager removed — debug-only behavior disabled.")]
public class DebugKeyManager : MonoBehaviour { }
#endif
