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
    Vector2 openPosition;   // 正常显示的位置
    Vector2 closedPosition; // 完全在父容器右侧以外的位置
    bool isOpen = false;
    Coroutine slideCoroutine;

    void Start()
    {
        UpdateUI();
        if (shopPanel == null) return;

        shopRect = shopPanel.GetComponent<RectTransform>();
        // record the position where the panel should sit when visible
        openPosition = shopRect.anchoredPosition;
        // compute closed position based on parent width so it is guaranteed offscreen
        RectTransform parentRect = shopRect.parent as RectTransform;
        float parentWidth = parentRect != null ? parentRect.rect.width : 0f;
        float panelWidth = shopRect.rect.width;
        // move the panel completely to the right of its parent
        closedPosition = openPosition + new Vector2(parentWidth + panelWidth, 0f);

        // start hidden
        shopRect.anchoredPosition = closedPosition;
        shopPanel.SetActive(false);
        isOpen = false;
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

        // always compute closed position from current width and parent size (layout/resolution may change)
        float width = shopRect.rect.width;
        RectTransform parentRect = shopRect.parent as RectTransform;
        float parentWidth = parentRect != null ? parentRect.rect.width : 0f;
        closedPosition = openPosition + new Vector2(parentWidth + width, 0);

        isOpen = !isOpen;
        shopOpen = isOpen; // update global flag

        if (isOpen)
        {
            shopPanel.SetActive(true);
            // when opening always slide from the closed position (which we've just recomputed)
            shopRect.anchoredPosition = closedPosition;
        }

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
            float tt = t * t * (3f - 2f * t);
            shopRect.anchoredPosition = Vector2.Lerp(start, target, tt);
            yield return null;
        }

        shopRect.anchoredPosition = target;

        // remember open position in case layout/resolution shifted while open
        if (isOpen)
            openPosition = target;

        Time.timeScale = isOpen ? 0f : 1f;

        if (!isOpen)
            shopPanel.SetActive(false);
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