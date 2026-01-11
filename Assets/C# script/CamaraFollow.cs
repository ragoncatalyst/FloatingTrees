using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamaraFollow : MonoBehaviour
{
    [SerializeField] Transform target;// Ҫ�����Ŀ�꣨�����
    [SerializeField] float followHeight = 3f;    // ������ڻ���Ϸ��ĸ߶�
    [SerializeField] float smoothSpeed = 5f;     // ����������ƽ���ȣ�Խ��Խ�죩
    [SerializeField] float lookAheadDistance = 2f; // �����������ǰ���ľ���
    
    [SerializeField] float baseDistance = 5f;    // �������루��ֹʱ��
    [SerializeField] float speedToDistanceMultiplier = 1.5f; // �ٶ�ת��Ϊ�����ϵ���������ޣ�
    [SerializeField] float distanceSmoothSpeed = 2f; // ����ƽ���ȣ�Խ��Խ��ͣ�
 
    private Rigidbody targetRb;
    private float currentDistance;

    void Start()
    {
        if (target == null)
        {
            target = FindObjectOfType<Movement>().transform;
        }
    
        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody>();
        }
        
        currentDistance = baseDistance;
    }

    void LateUpdate()
    {
        if (target == null) return;
    
        // ���㵱ǰ������ٶ�
        float currentSpeed = targetRb != null ? targetRb.velocity.magnitude : 0f;
        
        // �����ٶȼ���Ŀ����루�����ޣ��ٶ�Խ�����ԽԶ��
        float targetDistance = baseDistance + (currentSpeed * speedToDistanceMultiplier);
  
        // 平滑靠近距离（超平滑模式，让距离变化非常平缓）
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * distanceSmoothSpeed);
        
        // 计算动态跟随的目标位置
        Vector3 dynamicOffset = new Vector3(0, followHeight, -currentDistance);
        Vector3 desiredPosition = target.position + target.TransformDirection(dynamicOffset);
        
        // 平滑移动到目标位置
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * smoothSpeed);
 
        // �����������ǰ�������򣩣��������õ��ӽ�Ч��
        Vector3 lookAtTarget = target.position - target.forward * lookAheadDistance + Vector3.up * (followHeight * 0.5f);
        transform.LookAt(lookAtTarget);
    }
}
