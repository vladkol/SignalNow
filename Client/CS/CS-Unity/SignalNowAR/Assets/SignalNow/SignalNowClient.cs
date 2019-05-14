using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.SignalNow.Client
{
    public delegate void ConnectionStatusHandler(SignalNowClient signalNow, bool connected, Exception ifErrorWhy);
    public delegate void MessageHandler(SignalNowClient signalNow, string senderId, string messageType, string messagePayload);
    public delegate void NewPeerHandler(SignalNowClient signalNow, SignalNowPeer newPeer);
    public delegate void PeerStatusChangedHandler(SignalNowClient signalNow, SignalNowPeer peer);
    public delegate void RequestFailedHandler(SignalNowClient signalNow, string errorMessage);

    public class SignalNowClient
    {
#region private const and readonly declarations
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const int MessageTokenRefreshInAdvanceMinutes = 1;
        public const int MinimalStatusTimeoutInSeconds = 15;
        private const int SelfStatusTimeMarginInSeconds = 5;
        private const int PeerTimerFrequencyInSeconds = 5;
        private const int unbornPeerWaitingTries = 20;
        private const int importantMessageTries = 3;
        private const uint defaultMaxNumberOfSendRequests = 4;
#endregion

        private SignalNowHttp httpClient;
        private string serverAddress;

        private Timer peerTimer = null;
        private TimeSpan ClientServerTimeDiff;

        private HubConnection connection;
        private ConcurrentDictionary<string, SignalNowPeer> peers = new ConcurrentDictionary<string, SignalNowPeer>();
        private bool disconnecting = false;
        private CancellationTokenSource disconnectingCancel = new CancellationTokenSource();

#region Elastic messaging variables
        // stores tasks of sending "elastic" messages 
        private ConcurrentDictionary<long, Task> elasticActiveTasks = new ConcurrentDictionary<long, Task>();
        // queue of all messages to be sent
        private ConcurrentQueue<SignalNowMessageAction> elasticQueue = new ConcurrentQueue<SignalNowMessageAction>();

        private Mutex newSendMutex = new Mutex();
        private AutoResetEvent newMessageTaskEvent = new AutoResetEvent(false);
        private Task elasticQueueTask = null;
        private uint maxNumberOfSendRequests = defaultMaxNumberOfSendRequests;
#endregion

        public event ConnectionStatusHandler ConnectionChanged;
        public event MessageHandler NewMessage;
        public event NewPeerHandler NewPeer;
        public event PeerStatusChangedHandler PeerStatusChanged;
        public event RequestFailedHandler RequestFailed;

        public string ServerAddress => serverAddress;
        public string UserId { get; private set;}
        public string AuthService {get; private set;}
        public string ClientToken { get; private set;}
        public string ClientUrl { get; private set;}
        public string TurnServersAuthorization { get; private set;}
        public string MessageToken { get; private set;}
        public DateTime MessageTokenValidTo {get; private set;}
        public TimeSpan StatusTimeout {get; private set;}

        public System.Collections.ObjectModel.ReadOnlyDictionary<string, SignalNowPeer> Peers => new System.Collections.ObjectModel.ReadOnlyDictionary<string, SignalNowPeer>(peers);

        public SignalNowPeer Me { get; private set; }

        public SignalNowClient(string signalNowServerName, int statusTimeoutSeconds = 0, uint maxSimultaneousRequests = defaultMaxNumberOfSendRequests)
        {
            StatusTimeout = TimeSpan.FromSeconds(Math.Max(MinimalStatusTimeoutInSeconds, statusTimeoutSeconds));
            maxNumberOfSendRequests = Math.Max(1, maxSimultaneousRequests);

            if(signalNowServerName.Contains("://"))
            {
                serverAddress = signalNowServerName;
                if(serverAddress[serverAddress.Length - 1] == '/')
                {
                    serverAddress = serverAddress.Substring(0, serverAddress.Length - 1);
                }
            }
            else
            {
                serverAddress = $"https://{signalNowServerName}.azurewebsites.net";
            }

            peerTimer = new Timer(PeerTimerCallback, this, PeerTimerFrequencyInSeconds * 1000, PeerTimerFrequencyInSeconds * 1000);
        }



        public async Task<bool> Connect(string userName, string deviceId, string companyName, string teamName, string authServiceToken, string authServiceName)
        {
            if (httpClient == null)
            {
                httpClient = new SignalNowHttp(serverAddress,
                                (string msg, Exception ex) =>
                                {
                                    RequestFailed?.Invoke(this, msg);
                                });
            }

            string responseString = string.Empty;

            UserId = string.Empty;
            ClientUrl = string.Empty;
            ClientToken = string.Empty;
            TurnServersAuthorization = string.Empty;
            MessageToken = string.Empty;
            MessageTokenValidTo = DateTime.MinValue;
            
            AuthService = authServiceName;
            peers.Clear();

            if(connection != null)
            {
                if(connection.State == HubConnectionState.Connected)
                {
                    await Disconnect();
                }
                connection = null;
            }

            using (HttpRequestMessage request = httpClient.CreateHttpRequest("/api/Negotiate", HttpMethod.Post))
            {
                request.Headers.Add("username", userName);
                request.Headers.Add("deviceid", deviceId);
                request.Headers.Add("companyname", companyName);
                request.Headers.Add("teamname", teamName);
                request.Headers.Add("authservicetoken", authServiceToken);
                request.Headers.Add("authservicename", authServiceName);

#if UNITY_2018_2_OR_NEWER
                UnityEngine.Debug.Log($"Connecting to {serverAddress}.");
#else
                System.Diagnostics.Debug.WriteLine($"Connecting to {serverAddress}.");
#endif
                responseString = await httpClient.SendRequestLiteWithResultAsync(request);
                if (responseString == null)
                {
                    return false; // no need to handle the error because already done
                }

#if UNITY_2018_2_OR_NEWER
                UnityEngine.Debug.Log($"!!! Connected to {serverAddress}.");
#else
                System.Diagnostics.Debug.WriteLine($"!!! Connected to {serverAddress}.");
#endif
            }

            //userId, clientToken, clientUrl, serverTimeString, turnServersAuthorization
            JArray paramArray = JArray.Parse(responseString);
            UserId = paramArray[0].ToString();
            ClientToken = paramArray[1].ToString();
            ClientUrl = paramArray[2].ToString();

            var serverTimeString = paramArray[3].ToString();
            ClientServerTimeDiff = DateTime.UtcNow - UnixEpoch.AddSeconds(long.Parse(serverTimeString));

            if (paramArray.Count > 4)
            {
                TurnServersAuthorization = paramArray[4].ToString();
            }

#if UNITY_2018_2_OR_NEWER
            UnityEngine.Debug.Log($"Initializing SignalR connection {ClientUrl}.");
#else
            System.Diagnostics.Debug.WriteLine($"Initializing SignalR connection {ClientUrl}.");
#endif

            connection = new HubConnectionBuilder()
                .WithUrl(ClientUrl,
                option =>
                {
                    option.AccessTokenProvider = () =>
                    {
                        return Task.FromResult(ClientToken);
                    };
                })
#if !UNITY_2018_2_OR_NEWER
                .AddMessagePackProtocol() // https://fogbugz.unity3d.com/default.asp?1091189_1sqkebcrot7vvv9b
#endif
                .Build();


#if UNITY_2018_2_OR_NEWER
            UnityEngine.Debug.Log($"Connecting to SignalR...");
#else
            System.Diagnostics.Debug.WriteLine($"Connecting to SignalR...");
#endif

            await connection.StartAsync();
            bool bRes = (connection.State == HubConnectionState.Connected);

            if (bRes)
            {
                Me = new SignalNowPeer(UserId, PeerStatus.Online, StatusTimeout);

                connection.Closed += (Exception why) =>
                {
#if UNITY_2018_2_OR_NEWER
                    UnityEngine.Debug.LogWarning($"SignalR disconnected");
#else
                        System.Diagnostics.Debug.WriteLine($"SignalR disconnected");
#endif
                    connection = null;
                    if (!disconnecting)
                    {
                        return DisconnectInternal(why);
                    }
                    else return Task.Delay(0);
                };

#if UNITY_2018_2_OR_NEWER
                UnityEngine.Debug.Log($"Getting a message token.");
#else
                System.Diagnostics.Debug.WriteLine($"Getting a message token.");
#endif

                bRes = await EnsureMessageToken();

#if UNITY_2018_2_OR_NEWER
                UnityEngine.Debug.Log($"Message token is {(bRes ? "good" : "empty")}");
#else
                System.Diagnostics.Debug.WriteLine($"Message token is {(bRes ? "good" : "empty")}");
#endif

                if (bRes)
                {
                    connection.On<string, string, string>("SIGNAL",
                        (string senderId, string messageType, string messagePayload) =>
                        {
                            if (senderId != UserId)
                            {
                                OnMessageIn(senderId, messageType, messagePayload);
                            }
                        });

                    ConnectionChanged?.Invoke(this, true, null);

                    bRes = await SendImportantMessage(GetEveryoneRecipient(), true, "I_AM_HERE",
                                            ((int)StatusTimeout.TotalSeconds).ToString(), true);
                }
            }
            else
            {
#if UNITY_2018_2_OR_NEWER
                UnityEngine.Debug.Log($"Couldn't connect to SignalR.");
#else
                System.Diagnostics.Debug.WriteLine($"Couldn't connect to SignalR.");
#endif
            }

            return bRes;
        }


        private async Task<bool> EnsureMessageToken()
        {
            if (disconnecting || connection == null)
                return false;

            if (!string.IsNullOrEmpty(MessageToken) && DateTime.UtcNow < MessageTokenValidTo)
                return true;

            using (HttpRequestMessage request = httpClient.CreateHttpRequest("/api/GetMessageToken", HttpMethod.Post))
            {
                request.Headers.Add("authToken", string.IsNullOrEmpty(MessageToken) ?
                                                    ClientToken : 
                                                    MessageToken);

                try
                {
                    string tokenString = await httpClient.SendRequestLiteWithResultAsync(request);
                    if (tokenString == null)
                    {
                        return false; // no need to handle the error because already done
                    }

                    JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
                    if (JwtTokenHandler.CanReadToken(tokenString))
                    {
                        var token = JwtTokenHandler.ReadJwtToken(tokenString);

                        MessageTokenValidTo = token.ValidTo - TimeSpan.FromMinutes(MessageTokenRefreshInAdvanceMinutes) - ClientServerTimeDiff;
                        MessageToken = tokenString;
                    }
                }
                catch (Exception ex)
                {
                    RequestFailed?.Invoke(this, $"Exception when renewing a Message Token: {ex.Message}");
                }

                bool bOK = !string.IsNullOrEmpty(MessageToken);

                return bOK;
            }
        }


        // Elastic messages are combined into a queue so that is only certain numbers of messages is being going 
        // through active HTTP requests at certain point of time. 
        // It assumes you handle requests failures via RequestFailed handlers. 
        // SendElasticMessage returns a SignalNowMessageAction which you can use to check status of the message: 
        // * Waiting us true if message is waiting in the queue 
        // * Started is true if message is being sent 
        // * Cancelled is true if message has been cancelled 
        public SignalNowMessageAction SendElasticMessage(string recipient, bool groupRecipient, string messageType, 
                                        string messagePayload, bool payloadIsJson = false)
        {
            var action = new SignalNowMessageAction(this, 
                                        recipient, groupRecipient, messageType, 
                                        messagePayload, payloadIsJson, disconnectingCancel.Token);
            elasticQueue.Enqueue(action);

            if(elasticQueueTask == null || elasticQueueTask.IsCompleted)
            {
                try
                {
                    newSendMutex.WaitOne();

                    if(elasticQueueTask == null || elasticQueueTask.IsCompleted)
                    {
                        elasticQueueTask = Task.Run((Action)ElasticQueueThreadProc);
                    }  
                }
                finally
                {
                    newSendMutex.ReleaseMutex();
                }
            }

            newMessageTaskEvent.Set();
            return action;
        }


        // processes elastic message queue 
        private void ElasticQueueThreadProc()
        {
            while (!disconnecting && connection != null)
            {
                if (elasticQueue.Count > 0)
                {
                    if (!disconnecting && elasticActiveTasks.Count > maxNumberOfSendRequests)
                    {
                        Task.WaitAny(elasticActiveTasks.Values.ToArray(), disconnectingCancel.Token);
                    }

                    if(!disconnecting && elasticActiveTasks.Count <= maxNumberOfSendRequests)
                    {
                        SignalNowMessageAction messageAction;
                        if (!elasticQueue.TryDequeue(out messageAction))
                            continue;

                        long now = DateTime.UtcNow.Ticks;

                        Action fullAction = () =>
                        {
                            try
                            {
                                if (!disconnecting && connection != null && !messageAction.Completed && !messageAction.Cancelled)
                                {
                                    messageAction.Run();
                                }
                            }
                            catch(Exception ex)
                            {
                                if(!disconnecting)
                                {
                                    RequestFailed?.Invoke(this, $"Exception when sending an elastic message: {ex.Message}");
                                }
                            }

                            Task removed = null;
                            while (elasticActiveTasks.ContainsKey(now))
                            {
                                if (!elasticActiveTasks.TryRemove(now, out removed))
                                {
                                    Task.Delay(0);
                                }
                            }
                        };

                        elasticActiveTasks[now] = Task.Run(fullAction);
                    }
                }
                else
                {
                    WaitHandle.WaitAny(new WaitHandle[] { newMessageTaskEvent, disconnectingCancel.Token.WaitHandle });
                }
            }
        }


        public async Task<bool> SendMessage(string recipient, bool groupRecipient, string messageType, string messagePayload, bool payloadIsJson = false)
        {
            if (disconnecting || string.IsNullOrEmpty(MessageToken) || connection == null)
                return false;

            using (HttpRequestMessage request = httpClient.CreateHttpRequest("/api/Message", HttpMethod.Post))
            {
                request.Headers.Add("authToken", MessageToken);
                request.Headers.Add("sendto", recipient);
                request.Headers.Add("userorgroup", groupRecipient ? "group" : "user");
                request.Headers.Add("messagetype", messageType);

                request.Content = new StringContent(
                    payloadIsJson ? messagePayload : JsonConvert.SerializeObject(messagePayload),
                    System.Text.Encoding.UTF8, "application/json");

                try
                {
                    DateTime now = DateTime.UtcNow;
                    return await httpClient.SendRequestLiteAsync(request, false);
                }
                catch (Exception ex)
                {
                    if (!disconnecting)
                    {
                        bool isTaskCancelled =
                           ex.GetType() == typeof(TaskCanceledException)
                         || ex.InnerException != null && ex.InnerException.GetType() == typeof(TaskCanceledException);

                        if (!isTaskCancelled)
                        {
                            RequestFailed?.Invoke(this, $"Exception when sending a message: {ex.Message}");
                        }
                    }
                    return false;
                }
            }
        }


        public Task<bool> SendMessageToAll(string messageType, string messagePayload, bool payloadIsJson = false)
        {
            return SendMessage(GetEveryoneRecipient(), true, messageType, messagePayload, payloadIsJson);
        }

        public Task<bool> SetMyStatus(PeerStatus status)
        {
            Me.Status = status;
            Me.LastStatusTime = DateTime.UtcNow;
            return SendMessageToAll("STILL_HERE", Me.Status.ToString(), true);
        }

        public Task Disconnect()
        {
            return DisconnectInternal(null);
        }

#pragma warning disable 1998
        public async Task DisconnectInternal(Exception ifErrorWhy)
        {
            if (disconnecting)
                return;
            if(connection != null && connection.State == HubConnectionState.Connected)
            {
                SendMessageToAll("I_AM_OUTTA_HERE", string.Empty, true).Wait(1000);

                disconnecting = true;
                disconnectingCancel.Cancel();
#if !UNITY_2018_3_OR_NEWER
                await connection.DisposeAsync();
#else
                var tt = connection.DisposeAsync();
#endif
            }
            if (!disconnecting)
            {
                disconnecting = true;
                disconnectingCancel.Cancel();
            }
            try
            {
                connection = null;

                if(httpClient != null) 
                {
                    httpClient.Dispose();
                    httpClient = null;
                }

                ConnectionChanged?.Invoke(this, false, ifErrorWhy);
            }
            finally
            {
                peers.Clear();
                UserId = string.Empty;
                ClientUrl = string.Empty;
                ClientToken = string.Empty;
                MessageToken = string.Empty;
                Me = null;

                disconnectingCancel = new CancellationTokenSource();
                disconnecting = false;
            }
        }
#pragma warning restore 1998

        public async Task<bool> SendImportantMessage(string receiverId, bool isToGroup, string messageType, string payload, bool isPayloadJson)
        {
            bool ok = false;
            for(int i=0; i < importantMessageTries; i++)
            {
                ok = await SendMessage(receiverId, isToGroup, messageType, payload, isPayloadJson);
                if(ok)
                    break;
            }

            return ok;
        }


        public string GetEveryoneRecipient()
        {
            if(disconnecting || Me == null || connection == null)
            {
                return string.Empty;
            }
            return $"{AuthService}/{Me.Company}/{Me.Team}";
        }


        private void OnMessageIn(string senderId, string messageType, string messagePayload)
        {
            if (disconnecting)
                return;
                
            DateTime messageTime = DateTime.UtcNow;

            if(messageType.Equals("I_AM_HERE", StringComparison.InvariantCultureIgnoreCase))
            {
                TimeSpan statusExpirationTimeout = TimeSpan.FromSeconds(int.Parse(messagePayload));
                    
                bool newPeer = true;
                if(peers.ContainsKey(senderId))
                {
                    newPeer = false;
                    peers[senderId].Resurrect();
                    PeerStatusChanged?.Invoke(this, peers[senderId]);
                }

                string response = $"[\"{(int)Me.StatusExpirationTimeout.TotalSeconds}\", \"{Me.Status.ToString()}\"]";

                SendImportantMessage(senderId, false, "HELLO", response, true)
                    .ContinueWith((res)=>
                    {
                        if(newPeer)
                        {
                            peers[senderId] = new SignalNowPeer(senderId, PeerStatus.Online, statusExpirationTimeout);
                            NewPeer?.Invoke(this, peers[senderId]);
                        }
                    });
            }
            else if(messageType.Equals("HELLO", StringComparison.InvariantCultureIgnoreCase))
            {
                JArray paramArray = JArray.Parse(messagePayload);
                var timeoutStr = paramArray[0].ToString();
                var statusStr = paramArray[1].ToString();

                TimeSpan statusExpirationTimeout = TimeSpan.FromSeconds(int.Parse(timeoutStr));
                PeerStatus status = (PeerStatus)Enum.Parse(typeof(PeerStatus), statusStr, true);

                if(!peers.ContainsKey(senderId))
                {
                    peers[senderId] = new SignalNowPeer(senderId, status, statusExpirationTimeout);
                    NewPeer?.Invoke(this, peers[senderId]);
                }
                else
                {
                    peers[senderId].LastStatusTime = messageTime;
                    peers[senderId].Status = status;
                    peers[senderId].StatusExpirationTimeout = statusExpirationTimeout;   
                    PeerStatusChanged?.Invoke(this, peers[senderId]);                     
                }
            }
            else if(messageType.Equals("I_AM_OUTTA_HERE", StringComparison.InvariantCultureIgnoreCase))
            {
                if(peers.ContainsKey(senderId))
                {
                    peers[senderId].Status = PeerStatus.Offline;
                    PeerStatusChanged?.Invoke(this, peers[senderId]);
#if UNITY_2018_2_OR_NEWER
                    UnityEngine.Debug.Log($"{senderId} quit.");
#else
                    System.Diagnostics.Debug.WriteLine($"{senderId} quit.");
#endif
                }
            }
            else
            {
                // only respect messages from peers we had a handshake with 
                if(peers.ContainsKey(senderId))
                {
                    peers[senderId].LastDataMessageTime = messageTime;
                    peers[senderId].LastStatusTime = messageTime;

                    if(messageType.Equals("STILL_HERE", StringComparison.InvariantCultureIgnoreCase))
                    {
                        PeerStatus status = (PeerStatus)Enum.Parse(typeof(PeerStatus), messagePayload, true);
                        peers[senderId].Status = status;
                    }
                    else
                    {
                        Task.Run(()=>
                        {
                            NewMessage?.Invoke(this, senderId, messageType, messagePayload);
                        });
                    }
                }
            }

        }

        private void PeerTimerCallback(object state)
        {
            if (!disconnecting && connection != null && connection.State == HubConnectionState.Connected && Me != null)
            {
                if(!string.IsNullOrEmpty(MessageToken) && DateTime.UtcNow > MessageTokenValidTo)
                {
                    EnsureMessageToken().Wait();
                }

                if(Me.LastStatusTime + Me.StatusExpirationTimeout > DateTime.UtcNow - TimeSpan.FromSeconds(SelfStatusTimeMarginInSeconds))
                {
                    Me.LastStatusTime = DateTime.UtcNow;
                    SendMessageToAll("STILL_HERE", Me.Status.ToString(), true);
                }

                foreach(SignalNowPeer peer in Peers.Values)
                {
                    if(peer.Status != PeerStatus.Offline)
                    {
                        if(peer.LastStatusTime + peer.StatusExpirationTimeout < DateTime.UtcNow)
                        {
#if UNITY_2018_2_OR_NEWER
                            UnityEngine.Debug.Log($"{peer.UserId} status expired.");
#else
                            System.Diagnostics.Debug.WriteLine($"{peer.UserId} status expired.");
#endif

                            peer.Status = PeerStatus.Offline;
                            PeerStatusChanged?.Invoke(this, peer);
                        }
                    }
                }
            }
        }


    }
}