using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RouteScene : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        if (Application.platform == RuntimePlatform.IPhonePlayer
           || Application.platform == RuntimePlatform.Android)
        {
            SceneManager.LoadSceneAsync(2);
        }
        else
        {
            SceneManager.LoadSceneAsync(1);
        }
    }

}
