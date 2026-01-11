using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    [SerializeField] float mainThrust = 100f;
    [SerializeField] float rotationThrust = 10f;          // 旋转力矩
    [SerializeField] AudioClip mainEngine;
    
    [SerializeField] ParticleSystem mainEngineParticles;
    [SerializeField] ParticleSystem leftThrusterParticle;
    [SerializeField] ParticleSystem rightThrusterParticle;
    
    Rigidbody myRigidBody;
    AudioSource myAudioSource;
    
    // 输入状态缓存
    private bool isThrustingThisFrame;
    private bool isRotatingLeftThisFrame;
    private bool isRotatingRightThisFrame;
    
    // 追踪玩家是否操控过火箭
    private bool hasPlayerControlled = false;

    // Start is called before the first frame update
    void Start()
    {
        myRigidBody = GetComponent<Rigidbody>();
        myAudioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {
        ProcessInput();
    }

    // FixedUpdate用于物理计算，保证稳定性
    void FixedUpdate()
    {
        ProcessThrust();
        ProcessRotation();
    }

    void ProcessInput()
    {
        // 缓存输入状态
        isThrustingThisFrame = Input.GetKey(KeyCode.Space);
        isRotatingLeftThisFrame = Input.GetKey(KeyCode.A);
        isRotatingRightThisFrame = Input.GetKey(KeyCode.D);
        
        // 标记玩家是否操控过火箭
        if (isThrustingThisFrame || isRotatingLeftThisFrame || isRotatingRightThisFrame)
        {
            hasPlayerControlled = true;
        }
        
        // 在Update中处理音效和粒子效果（非物理部分）
        if (isThrustingThisFrame)
        {
            // 播放推进音效
            if (myAudioSource != null && mainEngine != null && !myAudioSource.isPlaying)
            {
                myAudioSource.PlayOneShot(mainEngine);
            }
            
            // 播放主引擎粒子效果
            if (mainEngineParticles != null && !mainEngineParticles.isPlaying)
            {
                mainEngineParticles.Play();
            }
        }
        else
        {
            // 释放空格键，停止音效和粒子
            if (myAudioSource != null)
            {
                myAudioSource.Stop();
            }
            if (mainEngineParticles != null)
            {
                mainEngineParticles.Stop();
            }
        }

        // 处理左推进器粒子
        if (isRotatingLeftThisFrame)
        {
            if (leftThrusterParticle != null && !leftThrusterParticle.isPlaying)
            {
                leftThrusterParticle.Play();
            }
        }
        else
        {
            if (leftThrusterParticle != null)
            {
                leftThrusterParticle.Stop();
            }
        }

        // 处理右推进器粒子
        if (isRotatingRightThisFrame)
        {
            if (rightThrusterParticle != null && !rightThrusterParticle.isPlaying)
            {
                rightThrusterParticle.Play();
            }
        }
        else
        {
            if (rightThrusterParticle != null)
            {
                rightThrusterParticle.Stop();
            }
        }
    }

    void ProcessThrust()
    {
        if (isThrustingThisFrame)
        {
            // 按下空格键，向上施加推力
            myRigidBody.AddRelativeForce(Vector3.up * mainThrust);
        }
    }

    void ProcessRotation()
    {
        // A键控制左转
        if (isRotatingLeftThisFrame)
        {
            myRigidBody.AddRelativeTorque(Vector3.forward * rotationThrust);
        }

        // D键控制右转
        if (isRotatingRightThisFrame)
        {
            myRigidBody.AddRelativeTorque(-Vector3.forward * rotationThrust);
        }
    }
    
    // 公共方法：供其他类调用，检查玩家是否操控过火箭
    public bool HasPlayerControlled()
    {
        return hasPlayerControlled;
    }
}
