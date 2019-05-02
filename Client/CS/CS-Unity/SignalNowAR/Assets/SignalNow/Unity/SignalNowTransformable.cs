using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

public class SignalNowTransformable : MonoBehaviour
{
    public enum TransformableMode
    {
        None = 0, 
        Receive = 1,
        Send = 2, 
        SendAndReceive = 3
    }

    public enum TrackingTypes
    {
        None = 0, 
        Rotation = 1, 
        Position = 2, 
        Scale = 4, 
        RotationAndPosition = 3, 
        RotationAndScale = 5, 
        PositionAndScale = 6, 
        All = 7
    }

    public SignalNowManager signalManager;
    public string signalObjectName;
    public bool overrideFullName = false;
    public TransformableMode updateDirection = TransformableMode.Receive;
    public TrackingTypes trackingTypes = TrackingTypes.All;

    [Range(0, 100)]
    public float smoothFactor = 10;

    public string signalObjectFullName
    {
        get; private set;
    }

    public bool sendRelativeUpdates = false;

    #region Private Constants
    private readonly TimeSpan remoteTimeDeltaQueueTime = TimeSpan.FromSeconds(5);
    private readonly TimeSpan minimumLowDeltaTime = TimeSpan.FromMilliseconds(85);
    private readonly double minimalDeltaQueueLenghtRatio = 0.1;
    private readonly double startToBigDeltaRatio = 2.0;
    #endregion

    private Vector3 position = new Vector3(0, 0, 0);
    private Quaternion rotation = Quaternion.identity;
    private Vector3 scale = new Vector3(1, 1, 1);

    private Vector3 startPosition = new Vector3(0, 0, 0);
    private Quaternion startRotation = Quaternion.identity;
    private Vector3 startScale = new Vector3(1, 1, 1);

    private Vector3 sentPosition = new Vector3(0, 0, 0);
    private Quaternion sentRotation = Quaternion.identity;
    private Vector3 sentScale = new Vector3(1, 1, 1);
    private bool sentAtLeastOnce = false;

    private Vector3 velocityPosition = new Vector3(0, 0, 0);
    private Vector3 velocityScale = new Vector3(0, 0, 0);
    private Vector3 velocityRotation = new Vector3(0, 0, 0);

    private bool recSetScale = false;
    private bool recSetPosition = false;
    private bool recSetRotation = false;

    private bool connectionStatusChanged = false;
    private bool initializedStartTransform = false;
    private bool updatedTransform = false;
    private bool sentLastUpdate = false;

    private DateTime lastSentTime = DateTime.MinValue;
    private DateTime lastReceivedTime = DateTime.MinValue;
    private DateTime lastRemoteTime = DateTime.MinValue;
    private TimeSpan lastRemoteTimeDelta = TimeSpan.MaxValue;
    private SignalNowTransformableData lastDataSent = null;

    private ConcurrentQueue<TimeSpan> remoteTimeDeltas = new ConcurrentQueue<TimeSpan>();
    private int remoteTimeDeltaQueueSampleLimit = 1;
    private TimeSpan averageRemoteTimeDelta = TimeSpan.Zero;


    public void UpdateStartTransform()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        startScale = transform.localScale;
        initializedStartTransform = true;
    }

    void OnEnable()
    {
        if (string.IsNullOrEmpty(signalObjectName))
        {
            signalObjectName = gameObject.name;
        }
        //var md5 = MD5.Create();
        //var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{BuildName(transform.parent)}/{signalObjectName}"));
        signalObjectFullName = overrideFullName ? signalObjectName : $"{BuildName(transform.parent)}/{signalObjectName}";// Convert.ToBase64String(hash);

        position = transform.position;
        rotation = transform.rotation;
        scale = transform.localScale;

        if(sendRelativeUpdates && signalManager.connected && !initializedStartTransform)
        {
            UpdateStartTransform();
        }

        signalManager.ConnectionStatusChanged += SignalManager_ConnectionStatusChanged;
        signalManager.TransformableUpdated += SignalManager_TransformableUpdated;

        remoteTimeDeltaQueueSampleLimit = (int)(remoteTimeDeltaQueueTime.Ticks / TimeSpan.FromMilliseconds(signalManager.updateTimeThresholdInMS).Ticks);
    }


    void SignalManager_ConnectionStatusChanged(SignalNowManager.ConnectionStatus status)
    {
        connectionStatusChanged = true;
    }


    void SignalManager_TransformableUpdated(string fullName, System.Numerics.Vector3 localPositionIn, System.Numerics.Vector3 localRotationIn, System.Numerics.Vector3 localScaleIn, long remoteTime)
    {
        if (fullName != signalObjectFullName)
            return;

        Vector3 localPosition = new Vector3(localPositionIn.X, localPositionIn.Y, localPositionIn.Z);
        Quaternion localRotation = Quaternion.Euler(localRotationIn.X, localRotationIn.Y, localRotationIn.Z);
        Vector3 localScale = new Vector3(localScaleIn.X, localScaleIn.Y, localScaleIn.Z);

        lastReceivedTime = DateTime.UtcNow;
        var newRemoteTime = DateTime.FromFileTime(remoteTime);
        lastRemoteTimeDelta = newRemoteTime - lastRemoteTime;

        remoteTimeDeltas.Enqueue(lastRemoteTimeDelta);
        while(remoteTimeDeltas.Count > remoteTimeDeltaQueueSampleLimit)
        {
            TimeSpan dummy = TimeSpan.Zero;
            remoteTimeDeltas.TryDequeue(out dummy);
        }

        averageRemoteTimeDelta = TimeSpan.FromTicks((long)remoteTimeDeltas.Average(ts => ts.Ticks));

        lastRemoteTime = newRemoteTime;

        float delta = (float)lastRemoteTimeDelta.TotalMilliseconds;

        var posDelta = localPosition - position;
        velocityPosition = new Vector3(posDelta.x / delta, posDelta.y / delta, posDelta.z / delta);

        var scDelta = localScale - scale;
        velocityScale = new Vector3(scDelta.x / delta, scDelta.y / delta, scDelta.z / delta);

        var rotDelta = localRotation.eulerAngles - rotation.eulerAngles;
        velocityRotation = new Vector3(rotDelta.x / delta, rotDelta.y / delta, rotDelta.z / delta);

        UpdateTransform(localPosition, localRotation, localScale);
    }


    public void UpdateTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    { 
        if(position != null)
        {
            this.position = position;
        }
        if(rotation != null)
        {
            this.rotation = rotation;
        }
        if(scale != null)
        {
            this.scale = scale;
        }

        updatedTransform = true;
    }

    private void ReceiveUpdate()
    {
        if (!updatedTransform)
        {
            DateTime now = DateTime.UtcNow;

            if(now - lastReceivedTime > TimeSpan.FromMilliseconds(signalManager.updateTimeThresholdInMS))
            {
                float delta = (float)(now - lastReceivedTime).TotalSeconds;
                if (velocityPosition.magnitude > 0)
                {
                    updatedTransform = true;
                    position += new Vector3(velocityPosition.x * delta, velocityPosition.y * delta, velocityPosition.z * delta);
                }
                if(velocityScale.magnitude > 0)
                {
                    updatedTransform = true;
                    scale += new Vector3(velocityScale.x * delta, velocityScale.y * delta, velocityScale.z * delta);
                }
                if(velocityRotation.magnitude > 0)
                {
                    updatedTransform = true;
                    rotation.eulerAngles += new Vector3(velocityRotation.x * delta, velocityRotation.y * delta, velocityRotation.z * delta);
                }
            }
        }

        if (updatedTransform)
        {
            updatedTransform = false;

            recSetScale = true;
            recSetRotation = true;
            recSetPosition = true;
        }
    }


    private void SendUpdate()
    {
        bool sendNow = false;
        bool forcedSend = false;

        bool setScale = false;
        bool setPosition = false;
        bool setRotation = false;

        Transform tr = this.transform;
        scale = tr.localScale;
        rotation = tr.localRotation;
        position = tr.localPosition;

        if (!sentAtLeastOnce)
        {
            sendNow = true;
            sentAtLeastOnce = true;
            setScale = true;
            setPosition = true;
            setRotation = true;
            forcedSend = true;
        }

        if (!sendNow)
        {
            setRotation = trackingTypes.HasFlag(TrackingTypes.Rotation);
            setPosition = trackingTypes.HasFlag(TrackingTypes.Position);
            setScale = trackingTypes.HasFlag(TrackingTypes.Scale);

            sendNow = setRotation || setPosition || setScale;
        }

        var sendPositionVal = position - startPosition;
        var sendRotationValue = rotation * Quaternion.Inverse(startRotation);
        var sendScaleValue = new Vector3(scale.x / startScale.x, scale.y / startScale.y, scale.z / startScale.z);
        SignalNowTransformableData trData = new SignalNowTransformableData()
        {
            objectName = signalObjectFullName,
            position = new System.Numerics.Vector3(sendPositionVal.x, sendPositionVal.y, sendPositionVal.z),
            rotation = new System.Numerics.Vector3(sendRotationValue.eulerAngles.x, sendRotationValue.eulerAngles.y, sendRotationValue.eulerAngles.z),
            scale = new System.Numerics.Vector3(sendScaleValue.x, sendScaleValue.y, sendScaleValue.z)
        };


        if (sendNow && lastDataSent != null && !forcedSend)
        {
            if((trData.position - lastDataSent.position).Length() <= float.Epsilon
               && (trData.scale - lastDataSent.scale).Length() <= float.Epsilon
                && (trData.rotation - lastDataSent.rotation).Length() <= float.Epsilon)
            {
                sendNow = false;
            }
        }

        if (sendNow)
        {
            lastDataSent = trData;
            sentLastUpdate = true;
            lastSentTime = DateTime.UtcNow;
            signalManager.SendData(trData);
        }
        else if(sentLastUpdate) // If no update, send the same values once again
        {                       // This way remote peers can correct velocity 
            sentLastUpdate = false;
            lastSentTime = DateTime.UtcNow;
            signalManager.SendData(trData);
        }
    }


    private void Update()
    {
        if (connectionStatusChanged && !initializedStartTransform && sendRelativeUpdates && signalManager.connected)
        {
            UpdateStartTransform();
        }

        connectionStatusChanged = false;

        if (signalManager.connected)
        {
            if (updateDirection.HasFlag(TransformableMode.Receive))
            {
                ReceiveUpdate();
                if (recSetPosition || recSetRotation || recSetScale)
                {
                    float realSmooth = smoothFactor;
                    if (realSmooth > 0.1)
                    {
                        if (averageRemoteTimeDelta > TimeSpan.FromMilliseconds(startToBigDeltaRatio * signalManager.updateTimeThresholdInMS)
                            && averageRemoteTimeDelta > minimumLowDeltaTime  
                            && remoteTimeDeltas.Count > (int)(minimalDeltaQueueLenghtRatio * remoteTimeDeltaQueueSampleLimit))
                        {
                            realSmooth /= 2;
                        }
                    }

                    Transform tr = transform;
                    float deltaTime = Time.deltaTime * (float)(realSmooth > 0.1 ? realSmooth : 1f);

                    if (trackingTypes.HasFlag(TrackingTypes.Position))
                    {
                        if (recSetPosition && tr.localPosition != position)
                        {
                            transform.position = Vector3.Lerp(transform.localPosition, position, deltaTime);
                        }
                        else
                        {
                            recSetPosition = false;
                        }
                    }

                    if (trackingTypes.HasFlag(TrackingTypes.Scale))
                    {
                        if (recSetScale && tr.localScale != scale)
                        {
                            transform.localScale = Vector3.Lerp(transform.localScale, scale, deltaTime);
                        }
                        else
                        {
                            recSetScale = false;
                        }
                    }

                    if (trackingTypes.HasFlag(TrackingTypes.Rotation))
                    {
                        if (recSetRotation && tr.localRotation != rotation)
                        {
                            transform.localRotation = Quaternion.Lerp(transform.localRotation, rotation, deltaTime);
                        }
                        else
                        {
                            recSetRotation = false;
                        }
                    }
                }
            }


            if (updateDirection.HasFlag(TransformableMode.Send) 
                && DateTime.UtcNow - lastSentTime >= TimeSpan.FromMilliseconds(signalManager.updateTimeThresholdInMS))
            {
                SendUpdate();
            }
        }
    }


    private static string BuildName(Transform thisObject)
    {
        if (thisObject == null)
            return string.Empty;
        SignalNowViewRoot rootObj = thisObject.gameObject.GetComponent<SignalNowViewRoot>();
        if(rootObj != null)
        {
            return rootObj.name;
        }
        return BuildName(thisObject.parent) + "/" + thisObject.gameObject.name;
    }
}
