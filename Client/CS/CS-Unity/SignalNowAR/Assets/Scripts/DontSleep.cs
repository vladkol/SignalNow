using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DontSleep : MonoBehaviour
{
    // Start is called before the first frame update
    void OnEnable()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.runInBackground = true;

    }

}
