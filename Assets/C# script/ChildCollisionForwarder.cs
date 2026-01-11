using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 该脚本已弃用 - 使用简单架构，父物体直接处理碰撞
public class ChildCollisionForwarder : MonoBehaviour
{
    void Start()
    {
        // 移除此组件（因为现在不需要）
        Destroy(this);
    }
}
