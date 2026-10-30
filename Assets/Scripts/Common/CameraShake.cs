using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraShake : MonoBehaviour
{

    public static CameraShake instance;
    private void Awake()
    {
        if (instance)
        {
            DestroyImmediate(gameObject);
        }
        else
        {
            instance = this;
        }
    }

    // Start is called before the first frame update


    public Transform cameraTransform; // Assign the camera's transform in the Inspector
    public float shakeDuration = 0.5f;
    public float shakeMagnitude = 0.5f;
    public float dampingSpeed = 1.0f;

    private Vector3 initialPosition;
    private float shakeElapsedTime = 0f;

    void Start()
    {
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main.transform;
        }
        initialPosition = cameraTransform.localPosition;
    }

    void Update()
    {
        if (shakeElapsedTime > 0)
        {
            cameraTransform.localPosition = initialPosition + Random.insideUnitSphere * shakeMagnitude;
            shakeElapsedTime -= Time.deltaTime * dampingSpeed;
        }
        else
        {
            shakeElapsedTime = 0f;
            cameraTransform.localPosition = initialPosition;
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.S))
        {
            TriggerShake();
        }
#endif
    }

    public void TriggerShake()
    {
        shakeElapsedTime = shakeDuration;
    }
}
