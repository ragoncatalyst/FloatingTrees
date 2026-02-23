using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 附加到商店按钮上的组件。负责把点击事件转发给交易管理器。
/// 同时在初始化时给所有子孙对象添加一个代理组件，以便点击它们也能触发购买。
/// </summary>
[RequireComponent(typeof(Button))]
public class ShopItem : MonoBehaviour, IPointerClickHandler
{
    [Tooltip("此商品的价格")]
    public int price = 1;

    [Header("Purchase Limit")]
    [Tooltip("此商品可购买的最大次数，0表示无限制")]
    public int maxPurchases = 1;

    [HideInInspector]
    public int purchasedCount = 0;

    [Tooltip("交易管理器（若为空会自动查找）")]
    public TradingSystem manager;

    void Awake()
    {
        if (manager == null)
            manager = FindObjectOfType<TradingSystem>();

        // 为所有子物体添加代理，用于接收点击事件
        AddProxiesRecursively(transform);
    }

    void AddProxiesRecursively(Transform t)
    {
        foreach (Transform child in t)
        {
            // 如果子物体已经有代理或自身是按钮跳过
            if (child.GetComponent<ShopItemProxy>() != null) continue;
            var proxy = child.gameObject.AddComponent<ShopItemProxy>();
            proxy.owner = this;
            AddProxiesRecursively(child);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (manager != null)
            manager.TryPurchase(this);
    }
}

/// <summary>
/// 点击代理，转发给父ShopItem。
/// </summary>
public class ShopItemProxy : MonoBehaviour, IPointerClickHandler
{
    public ShopItem owner;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner != null && owner.manager != null)
            owner.manager.TryPurchase(owner);
    }
}