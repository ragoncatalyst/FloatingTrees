using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// 简易交易系统管理器。
/// - Z 键切换商店面板
/// - Backspace 获得 1 个代币并更新显示
/// - 购买逻辑由 <see cref="ShopItem"/> 组件处理
/// </summary>
public class TradingSystem : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("商店的根面板（包含若干按钮）")]
    public GameObject shopPanel;

    [Tooltip("显示当前代币数量的 TMP 文本")]
    public TextMeshProUGUI tokenText;

    [Header("Economy")]
    [Tooltip("当前持有的代币数量（运行时）")]
    public int tokens = 0;

    [Header("Animation")]
    [Tooltip("滑动动画的持续时间（秒）")]
    public float slideDuration = 0.3f;

    /// <summary>
    /// 全局指示器：商店面板当前是否打开（供其他系统阻止输入）。
    /// </summary>
    public static bool shopOpen { get; private set; } = false;

    // internal state
    RectTransform shopRect;
    Vector2 closedPosition;
    Vector2 openPosition;
    bool isOpen = false;
    Coroutine slideCoroutine;

    void Start()
    {
        UpdateUI();
        if (shopPanel != null)
        {
            shopRect = shopPanel.GetComponent<RectTransform>();
            // assume panel anchored right side; closed position offscreen to right
            openPosition = shopRect.anchoredPosition;
            closedPosition = openPosition + new Vector2(shopRect.rect.width, 0);
            shopRect.anchoredPosition = closedPosition;
            shopPanel.SetActive(true); // keep active so animation works
            isOpen = false;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Z))
        {
            ToggleShop();
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            tokens += 1;
            UpdateUI();
        }
    }

    void ToggleShop()
    {
        if (shopPanel == null) return;
        if (slideCoroutine != null) StopCoroutine(slideCoroutine);
        isOpen = !isOpen;
        shopOpen = isOpen; // update global flag
        slideCoroutine = StartCoroutine(SlidePanel(isOpen));
    }

    IEnumerator SlidePanel(bool open)
    {
        isOpen = open;
        float elapsed = 0f;
        Vector2 start = shopRect.anchoredPosition;
        Vector2 target = open ? openPosition : closedPosition;
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime; // use unscaled so works when timescale is 0
            float t = Mathf.Clamp01(elapsed / slideDuration);
            // ease-in-out using smoothstep
            float tt = t * t * (3f - 2f * t);
            shopRect.anchoredPosition = Vector2.Lerp(start, target, tt);
            yield return null;
        }
        shopRect.anchoredPosition = target;

        // adjust timeScale after animation
        Time.timeScale = isOpen ? 0f : 1f;
    }

    public void TryPurchase(ShopItem item)
    {
        if (item == null) return;

        // check purchase limit
        if (item.maxPurchases > 0 && item.purchasedCount >= item.maxPurchases)
            return;

        if (tokens >= item.price)
        {
            tokens -= item.price;
            UpdateUI();
            item.purchasedCount++;

            // disable if reach limit
            if (item.maxPurchases > 0 && item.purchasedCount >= item.maxPurchases)
            {
                Button btn = item.GetComponent<Button>();
                if (btn != null)
                {
                    btn.interactable = false;
                    Image img = btn.GetComponent<Image>();
                    if (img != null)
                        img.color = Color.gray;
                }
                // update TMP child named "TMP This" if exists
                var tmp = item.transform.Find("TMP This")?.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = "Sold Out";
                }
            }
        }
    }

    void UpdateUI()
    {
        if (tokenText != null)
            tokenText.text = tokens.ToString();
    }
}