using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 将子方块的碰撞事件转发给父物体
public class ChildCollisionForwarder : MonoBehaviour
{
    private GameObject parentObject;
    
    // 设置父物体
    public void SetParent(GameObject parent)
    {
        parentObject = parent;
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // 转发碰撞事件给父物体
        if (parentObject != null)
        {
            parentObject.SendMessage("OnCollisionEnter", collision, SendMessageOptions.DontRequireReceiver);
        }
    }
    
    void OnCollisionStay(Collision collision)
    {
        // 转发持续碰撞事件给父物体
        if (parentObject != null)
        {
            parentObject.SendMessage("OnCollisionStay", collision, SendMessageOptions.DontRequireReceiver);
        }
    }
    
    void OnCollisionExit(Collision collision)
    {
        // 转发碰撞退出事件给父物体
        if (parentObject != null)
        {
            parentObject.SendMessage("OnCollisionExit", collision, SendMessageOptions.DontRequireReceiver);
        }
    }
}
