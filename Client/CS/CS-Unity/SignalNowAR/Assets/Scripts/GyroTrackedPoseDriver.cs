using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SpatialTracking;

public class GyroTrackedPoseDriver : MonoBehaviour
{
    // Start is called before the first frame update
    void OnEnable()
    {
        var tps = GetComponent<TrackedPoseDriver>();
        if(tps != null && tps.enabled)
        {
            this.enabled = false;
        }
        else
        {
            Input.gyro.enabled = true;
        }
    }

    private void FixedUpdate()
    {
        UpdatePose();
    }

    private void LateUpdate()
    {
        UpdatePose();
    }

    void UpdatePose()
    {
        transform.localRotation = GyroToUnity(Input.gyro.attitude);
    }

    private static Quaternion GyroToUnity(Quaternion q)
    {
        return new Quaternion(q.x, q.y, -q.z, -q.w);
    }
}
