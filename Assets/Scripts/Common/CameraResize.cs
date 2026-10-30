using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraResize : MonoBehaviour
{
    public float camSize = 5f;
    Camera mainCamera;

    float referenceOrthographicSize;

    float targetAspectRatio;
    float currentAspectRatio;
    float adjustedSize;

    public bool isBounds = false;
    public Transform[] allBounds;
    private void Awake()
    {
        mainCamera = Camera.main;
        mainCamera.orthographicSize = camSize;

        targetAspectRatio = 9.0f / 16.0f;
        referenceOrthographicSize = mainCamera.orthographicSize;

        currentAspectRatio = (float)Screen.width / (float)Screen.height;
        adjustedSize = referenceOrthographicSize / (currentAspectRatio / targetAspectRatio);

        mainCamera.orthographicSize = adjustedSize;

        if (isBounds)
        {
            Vector3 pos = allBounds[0].position;
            pos.x = -1f * (adjustedSize * currentAspectRatio);
            allBounds[0].position = pos;


            pos = allBounds[1].position;
            pos.x = 1f * (adjustedSize * currentAspectRatio);
            allBounds[1].position = pos;

            pos = allBounds[2].position;
            pos.y = -1f * (adjustedSize);
            allBounds[2].position = pos;

            pos = allBounds[3].position;
            pos.y = 1f * (adjustedSize);
            allBounds[3].position = pos;
        }
    }
}
