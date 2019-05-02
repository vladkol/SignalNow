using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SignalNowStateTracker : MonoBehaviour
{
    public SignalNowManager signalManager;
    public Transform[] showWhenConected;
    public Transform[] showWhenDisconnected;

    public UnityEngine.UI.Text uiTextsToShowStatus;
    public UnityEngine.TextMesh text3dsToShowStatus;
    public bool hideStatusWhenConnected = true;

    public string disconnectedText = "Connect";
    public string connectingText = "Connecting...";
    public string connectedText = "Disconnect";

    private bool statusChanged = false;
    private SignalNowManager.ConnectionStatus status = SignalNowManager.ConnectionStatus.Disconnected;



    void OnEnable()
    {
        bool connected = signalManager != null ? signalManager.connected : false;
        status = connected ? SignalNowManager.ConnectionStatus.Connected : SignalNowManager.ConnectionStatus.Disconnected;


        if(signalManager != null)
        {
            signalManager.ConnectionStatusChanged += SignalManager_ConnectionStatusChanged;
        }

        statusChanged = true;
    }


    void SignalManager_ConnectionStatusChanged(SignalNowManager.ConnectionStatus newStatus)
    {
        status = newStatus;
        statusChanged = true;
    }

    void UpdateStatusObjects()
    {
        if (showWhenConected != null )
        {
            foreach (var t in showWhenConected)
            {
                t.gameObject.SetActive(status == SignalNowManager.ConnectionStatus.Connected);
            }
        }
        if (showWhenDisconnected != null)
        {
            foreach (var t in showWhenDisconnected)
            {
                t.gameObject.SetActive(status == SignalNowManager.ConnectionStatus.Disconnected);
            }
        }

        if(hideStatusWhenConnected)
        {
            if (text3dsToShowStatus != null)
            {
                text3dsToShowStatus.gameObject.SetActive(status != SignalNowManager.ConnectionStatus.Connected);
            }

            if (uiTextsToShowStatus != null)
            {
                text3dsToShowStatus.gameObject.SetActive(status != SignalNowManager.ConnectionStatus.Connected);
            }
        }

        string statusText; 

        if(status == SignalNowManager.ConnectionStatus.Connected)
        {
            statusText = connectedText;
        }
        else if (status == SignalNowManager.ConnectionStatus.Disconnected)
        {
            statusText = disconnectedText;
        }
        else
        {
            statusText = connectingText;
        }

        if (text3dsToShowStatus != null)
        {
            text3dsToShowStatus.text = statusText;
        }

        if(uiTextsToShowStatus != null)
        {
            uiTextsToShowStatus.text = statusText;
        }
    }


    void Update()
    {
        if(statusChanged)
        {
            statusChanged = false;
            UpdateStatusObjects();
        }
    }
}
