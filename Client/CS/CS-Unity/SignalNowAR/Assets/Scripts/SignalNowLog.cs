using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

public class SignalNowLog : MonoBehaviour
{
    public SignalNowManager signalManager;
    public UnityEngine.UI.Text logText;
    public bool trackARStatus = false;

    private ConcurrentQueue<string> toAdd = new ConcurrentQueue<string>();

    void OnEnable()
    {
        if(signalManager != null)
        {
            signalManager.ConnectionStatusChanged += SignalManager_ConnectionStatusChanged;
            signalManager.RequestFailed += SignalManager_RequestFailed;
        }

        if (trackARStatus)
        {
            UnityEngine.XR.ARFoundation.ARSubsystemManager.systemStateChanged += ARSubsystemManager_systemStateChanged;
        }
    }

    private void ARSubsystemManager_systemStateChanged(UnityEngine.XR.ARFoundation.ARSystemStateChangedEventArgs stateArgs)
    {
        AddText($"AR subsystem status changed: {stateArgs.state}");
    }

    private void SignalManager_RequestFailed(SignalNowManager sender, string errorString)
    {
        AddText($"SignalNow request failed: {errorString}");
    }

    private void SignalManager_ConnectionStatusChanged(SignalNowManager.ConnectionStatus status)
    {
        AddText($"SignalNow connection status changed: {status}");
    }

    void Update()
    {
        if(logText != null)
        {
            while(toAdd.Count > 0)
            {
                string val = string.Empty;
                if(toAdd.TryDequeue(out val) && !string.IsNullOrEmpty(val))
                {
                    logText.text += val;
                }
            }
        }
    }

    private void AddText(string text)
    {
        toAdd.Enqueue(text + "\n");
    }
}
