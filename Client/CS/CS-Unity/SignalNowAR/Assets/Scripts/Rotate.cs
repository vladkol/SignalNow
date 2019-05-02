using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotate : MonoBehaviour
{
    public float speed = 3.0f;
    public bool onlyInEditor = false;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    void Update()
    {
        if(onlyInEditor && !UnityEngine.Application.isEditor)
        {
            return;
        }
        transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
    }
}
