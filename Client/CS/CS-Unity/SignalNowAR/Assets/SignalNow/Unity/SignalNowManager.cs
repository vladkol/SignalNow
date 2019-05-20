using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]
public abstract class SignalNowData
{
    [JsonIgnore]
    public long Id => _id;

    [JsonIgnore]
    public long Time => _time;

    [JsonProperty]
    private static long _id = GenerateId();

    [JsonProperty]
    private static long _time = DateTime.UtcNow.ToFileTime();

    private static long _newId;
    private static long GenerateId()
    {
        return System.Threading.Interlocked.Increment(ref _newId);
    }
}

[Preserve]
public abstract class SignalNowObjectStateData : SignalNowData
{
    public string objectName;
}

[Preserve]
public class SignalNowTransformableData : SignalNowObjectStateData
{
    public System.Numerics.Vector3 position;
    public System.Numerics.Vector3 rotation;
    public System.Numerics.Vector3 scale;
}

public class SignalNowManager : MonoBehaviour
{
    public enum ConnectionStatus
    {
        Disconnected = 0, 
        Connecting, 
        Connected 
    }
    public delegate void TransformableUpdatedHandler(string fullName, System.Numerics.Vector3 localPosition, System.Numerics.Vector3 localRotation, System.Numerics.Vector3 localScale, long remoteTime);
    public delegate void StatusChangedHandler(ConnectionStatus status);
    public delegate void RequestFailedHandler(SignalNowManager sender, string errorString);

    public bool autoStart = false;

    public string signalNowServer;
    public string graphName;
    public string userName;
    public string company;
    public string team;
    public string authKey;

    [Range(10, 1000)]
    public uint updateTimeThresholdInMS = 25;

    [Range(1, 10)]
    public uint maxSimultaneousRequests = 4;

    public bool connected
    {
        get; private set;
    }

    public event TransformableUpdatedHandler TransformableUpdated;
    public event StatusChangedHandler ConnectionStatusChanged;
    public event RequestFailedHandler RequestFailed;

    private Task lifecyleTask = null;
    private Microsoft.SignalNow.Client.SignalNowClient client;
    private ConcurrentQueue<SignalNowData> outgoingQueue = new ConcurrentQueue<SignalNowData>();
    private ConcurrentQueue<SignalNowData> incomingQueue = new ConcurrentQueue<SignalNowData>();

    private DateTime lastSendTime = DateTime.MinValue;
    private DateTime lastReceiveTime = DateTime.MinValue;
    private Task sendTask = null;
    private Task receiveTask = null;

    private static long timeOut = (long)TimeSpan.FromMinutes(1).TotalSeconds;


    public SignalNowManager()
    {
        connected = false;
    }

    public void StartSession()
    {
        if(lifecyleTask != null && !lifecyleTask.IsCompleted)
        {
            return;
        }
        lifecyleTask = StartSessionInternal();
    }

    public void StopSession()
    {
        Disconnect();
    }

    public Task Disconnect()
    {
        if (client != null && client.Me != null)
            return client.Disconnect();
        else
            return Task.Run(() => { });
    }


    public void SendData(SignalNowData data)
    {
        outgoingQueue.Enqueue(data);
    }

    void OnEnable()
    {
        if(client != null)
        {
            Disconnect();
            client = null;
        }
        client = new Microsoft.SignalNow.Client.SignalNowClient(signalNowServer, 0, maxSimultaneousRequests);
        client.NewMessage += Client_NewMessage;
        client.RequestFailed += Client_RequestFailed;
        client.ConnectionChanged += Client_ConnectionChanged;
        client.PeerStatusChanged += Client_PeerStatusChanged;
        client.NewPeer += Client_NewPeer;

        if (autoStart)
            StartSession();
    }


    private void OnDisable()
    {
        if (client != null)
        {
            Disconnect().Wait();
            client = null;
        }
    }


    void Update()
    {
        if (!connected)
            return;

        DateTime now = DateTime.UtcNow;

        if (now - lastReceiveTime > TimeSpan.FromMilliseconds(updateTimeThresholdInMS) 
            && (receiveTask == null || receiveTask.IsCompleted))
        {
            lastReceiveTime = now;
            receiveTask = Receive();
        }

        now = DateTime.UtcNow;

        if (now - lastSendTime > TimeSpan.FromMilliseconds(updateTimeThresholdInMS) 
           && (sendTask == null || sendTask.IsCompleted))
        {
            lastSendTime = now;
            sendTask = Task.Run(Send);
        }
    }

    private Microsoft.SignalNow.Client.SignalNowMessageAction lastMessageAction = null;

    void Send()
    {
        if(lastMessageAction != null && lastMessageAction.Waiting)
        {
            return;
        }

        List <SignalNowTransformableData> toSendTransformable = new List<SignalNowTransformableData>();

        while (!outgoingQueue.IsEmpty)
        {
            SignalNowData data = null;
            if (outgoingQueue.TryDequeue(out data) && data != null)
            {
                string dataType = data.GetType().Name;
                if (dataType == typeof(SignalNowTransformableData).Name)
                {
                    var trans = data as SignalNowTransformableData;

                    var existing = toSendTransformable.Where(t => t.objectName == trans.objectName).FirstOrDefault();
                    if(existing != null && !string.IsNullOrEmpty(existing.objectName))
                    {
                        toSendTransformable.Remove(existing);
                    }

                    toSendTransformable.Add(trans);
                }
            }
        }

        if (toSendTransformable.Count > 0)
        {
            try
            {
                var settings = new JsonSerializerSettings();
                settings.ReferenceLoopHandling = ReferenceLoopHandling.Error;
                string json = JsonConvert.SerializeObject(toSendTransformable.ToArray(), Formatting.None, settings);
                lastMessageAction = client.SendElasticMessage(client.GetEveryoneRecipient(), true, typeof(SignalNowTransformableData).Name, json, true);
            }
            catch(Exception ex)
            {
                Debug.LogError($"Exception when sending an update: {ex.Message}");
            }
        }
    }

    private Task Receive()
    {
        return Task.Run(() =>
        {
            List<SignalNowData> datas = new List<SignalNowData>();

            while (!incomingQueue.IsEmpty)
            {
                SignalNowData data = null;
                if (incomingQueue.TryDequeue(out data))
                {
                    bool doNotAdd = false;
                    if (data is SignalNowObjectStateData)
                    {
                        SignalNowObjectStateData ss = data as SignalNowObjectStateData;

                        var sameObj = datas.FirstOrDefault(d => d is SignalNowObjectStateData
                            && ((SignalNowObjectStateData)d).objectName == ss.objectName) as SignalNowObjectStateData;

                        if (sameObj != null && !string.IsNullOrEmpty(sameObj.objectName))
                        {
                            if (sameObj.Time < data.Time)
                            {
                                datas.Remove(sameObj);
                            }
                            else
                            {
                                doNotAdd = true;
                            }
                        }
                    }

                    if (!doNotAdd)
                    {
                        datas.Add(data);
                    }
                }
            }


            foreach (var data in datas)
            {
                string dataType = data.GetType().Name;
                /*if (dataType == typeof(SignalNowData).Name)
                {

                }
                else if (dataType == typeof(SignalNowObjectStateData).Name)
                {
                    var dataState = data as SignalNowObjectStateData;
                }
                else*/
                if (dataType == typeof(SignalNowTransformableData).Name)
                {
                    var dataTrans = data as SignalNowTransformableData;
                    TransformableUpdated?.Invoke(dataTrans.objectName, dataTrans.position, dataTrans.rotation, dataTrans.scale, dataTrans.Time);
                }
            }
        });
    }


    Task StartSessionInternal()
    {
        return Task.Run(async()=>
        {
            if (client != null)
            {
                await Disconnect();
            }
            connected = false;
            sendTask = null;

            Debug.Log("Connecting to SignalNow");

            try
            {
                ConnectionStatusChanged?.Invoke(ConnectionStatus.Connecting);
                if (await client.Connect(userName,
                                        Guid.NewGuid().ToString(),
                                        company,
                                        team,
                                        authKey,
                                        graphName))
                {
                    Debug.Log($"Connected to {client.ServerAddress} as {client.Me.UserId}.");
                    lastSendTime = DateTime.UtcNow;
                    connected = true;
                }
                else
                {
                    Debug.LogError($"Could't connect to SignalNow");
                }
            }
            catch(Exception ex)
            {
                Debug.LogError($"!!!!!!!!!!!!!!!!!!!!!!! Exception when connecting to SignalNow. {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                if(ex.InnerException != null)
                {
                    Debug.LogError($"=== INNER EXCEPTION!!! ===\n{ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                }
            }
        });
    }

    private void Client_ConnectionChanged(Microsoft.SignalNow.Client.SignalNowClient signalNow, bool nowConnected, Exception ifErrorWhy)
    {
        this.connected = nowConnected;
        outgoingQueue = new ConcurrentQueue<SignalNowData>();
        incomingQueue = new ConcurrentQueue<SignalNowData>();
        ConnectionStatusChanged?.Invoke(connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected);
    }

    private void Client_RequestFailed(Microsoft.SignalNow.Client.SignalNowClient signalNow, string errorMessage)
    {        
        Debug.LogError($"Signaling request failed: {errorMessage}");
        RequestFailed?.Invoke(this, errorMessage);
    }

    void Client_PeerStatusChanged(Microsoft.SignalNow.Client.SignalNowClient signalNow, Microsoft.SignalNow.Client.SignalNowPeer peer)
    {
        Debug.Log($"Peer {peer.UserName} is {peer.Status}");
    }

    private void Client_NewPeer(Microsoft.SignalNow.Client.SignalNowClient signalNow, Microsoft.SignalNow.Client.SignalNowPeer newPeer)
    {
        Debug.Log($"Peer {newPeer.UserName} is {newPeer.Status}"); ;
    }

    void Client_NewMessage(Microsoft.SignalNow.Client.SignalNowClient signalNow, string senderId, string messageType, string messagePayload)
    {
        SignalNowData[] dataArray = null;
        if(messageType.Equals(typeof(SignalNowTransformableData).Name, StringComparison.InvariantCultureIgnoreCase))
        {
            try
            {
                dataArray = JsonConvert.DeserializeObject<SignalNowTransformableData[]>(messagePayload);
            }
            catch(Exception ex)
            {
                Debug.LogError($"Cannot process SignalNowTransformableData message. Exception: {ex.Message} ({ex.GetType().Name})");
            }
        }

        if (dataArray != null)
        {
            foreach (var d in dataArray)
            {
                incomingQueue.Enqueue(d);
            }
        }
    }

}
