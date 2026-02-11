using UnityEngine;

/// <summary>
/// 全局调试键管理器 - 处理调试快捷键
/// / 斜杠：将Rocket和配置文件改为只有一个方块
/// \ 反斜杠：清空配置文件并重新加载（会初始化为单个方块）
/// Enter：在Workshop和Main场景之间切换（由SceneSwitcher处理）
/// </summary>
public class DebugKeyManager : MonoBehaviour
{
    void Update()
    {
        // / 键：将当前Rocket设置为单个方块并保存到配置文件
        if (Input.GetKeyDown(KeyCode.Slash))
        {
            Debug.Log("[DebugKeyManager] 按下/键，设置为单个方块");
            RocketStateManager.ResetToSingleBlock();
        }
        
        // \ 键：出厂重置 —— 清空所有用户配置并恢复默认位姿/摄像头/方块
        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            Debug.Log("[DebugKeyManager] 按下\\键，执行出厂重置（Factory Reset）");
            RocketStateManager.FactoryResetToDefaults();
        }
    }
}
