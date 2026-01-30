using UnityEngine;

public class Crosshair : MonoBehaviour
{
    [Header("准星设置")]
    [SerializeField] private Color crosshairColor = Color.white;
    [SerializeField] private float crosshairSize = 10f;      // 准星长度
    [SerializeField] private float crosshairThickness = 2f;  // 准星粗细
    [SerializeField] private float crosshairGap = 5f;        // 中心间隙

    void OnGUI()
    {
        // 获取屏幕中心点
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        // 创建准星材质
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, crosshairColor);
        texture.Apply();

        GUI.skin.box.normal.background = texture;

        // 绘制水平线（左）
        GUI.Box(new Rect(centerX - crosshairGap - crosshairSize, centerY - crosshairThickness / 2f, crosshairSize, crosshairThickness), GUIContent.none);
        
        // 绘制水平线（右）
        GUI.Box(new Rect(centerX + crosshairGap, centerY - crosshairThickness / 2f, crosshairSize, crosshairThickness), GUIContent.none);
        
        // 绘制垂直线（上）
        GUI.Box(new Rect(centerX - crosshairThickness / 2f, centerY - crosshairGap - crosshairSize, crosshairThickness, crosshairSize), GUIContent.none);
        
        // 绘制垂直线（下）
        GUI.Box(new Rect(centerX - crosshairThickness / 2f, centerY + crosshairGap, crosshairThickness, crosshairSize), GUIContent.none);
    }
}
