using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SignalNow.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SignalNowTest
{
    class Program
    {
        private static long timeOut = (long)TimeSpan.FromMinutes(1).TotalSeconds;
        private static string graphName = "signalnowkey";
        private static string userName = "vlad";
        private static string company = "microsoft";
        private static string team = "cse";
        private static string auth = "u_7c964ca6a3a45adf1e7bddc67fedcbccu_5cb4a8e010044872d0e156ce628eccb2";
        private static string hostName = System.Net.Dns.GetHostName();

        static void Main(string[] args)
        {
            Run().Wait();
        }

        static SignalNowClient client = new SignalNowClient("signalnowwestus2");
        static Timer statusTimer;
        static Timer sendTimer;
        private static bool dataReadyState = false;

        static async Task Run()
        {
            DateTime now = DateTime.UtcNow;
            Console.WriteLine($"Start time: {now.ToLongTimeString()}, {now.ToShortDateString()}");

            client.NewPeer += (SignalNowClient signalNow, SignalNowPeer newPeer) =>
            {
                DateTime newPeerTime = DateTime.UtcNow;
                Console.WriteLine($"\nGot a new peer at {newPeerTime.ToLongTimeString()}, {newPeerTime.ToShortDateString()}. Name: {newPeer.UserName}");
            };
            client.PeerStatusChanged += (SignalNowClient signalNow, SignalNowPeer peer) =>
            {
                if(peer.Status == PeerStatus.Offline)
                {
                    DateTime peerTime = DateTime.UtcNow;
                    Console.WriteLine($"\nPeer {peer.UserName} went offline at {peerTime.ToLongTimeString()}, {peerTime.ToShortDateString()}");
                }
            };
            client.RequestFailed += (SignalNowClient signalNow, string error) =>
            {
                Console.WriteLine($"\nSignaling request failed. Message: {error}");
            };

            client.NewMessage += OnNewMessage;

            Console.WriteLine($"Connecting to {client.ServerAddress}");
            if (await client.Connect(userName,
                                    Guid.NewGuid().ToString(),
                                    company,
                                    team,
                                    auth,
                                    graphName))
            {
                now = DateTime.UtcNow;
                Console.WriteLine($"Connected to {client.ServerAddress} at {now.ToLongTimeString()}, {now.ToShortDateString()}");
                Console.WriteLine($"User name: {userName}\nHost name: {hostName}\nUser Id: {client.UserId}\n");

                statusTimer = new Timer((state) =>
                {
                    if(client.Peers.Count == 0)
                    {
                        Console.Write($"\rWaiting for others...");
                        return;
                    }

                    if (!dataReadyState)
                    {
                        SendUpdateMessage(DateTime.UtcNow);

                        hopsTimeTotal = TimeSpan.Zero;
                        messagesReceived = 0;
                        dataReadyState = true;
                        sendTimer = new Timer(SendTimerHandler, null, 20, 20);
                    }
                    else if(messagesReceived != 0 || messagesReceivedOneWay != 0)
                    {
                        var t = Task.Run(()=>
                        {
                            Console.Write($"\rAverage time (ms): 2-way {hopTime.TotalMilliseconds:0.0} ({minDelta.TotalMilliseconds:0.0}, {maxDelta.TotalMilliseconds:0.0}), " + 
                                           $"1-way {minDeltaOneWay.TotalMilliseconds:0.0} ({minDeltaOneWay.TotalMilliseconds:0.0}, {maxDeltaOneWay.TotalMilliseconds:0.0}). " + 
                                           $"Messages: 2-way {messagesReceived}, 1-way {messagesReceivedOneWay}");
                        });
                    }
                }, null, 1000, 1000);

                string line;
                do
                {
                    line = Console.ReadLine();
                }
                while (!line.ToLower().StartsWith('q'));

            }
            else
            {
                Console.WriteLine($"Cannot connect to {client.ServerAddress}\n");
            }

        }

        private static long messagesReceived = 0;
        private static long messagesReceivedOneWay = 0;
        private static TimeSpan hopTime = TimeSpan.Zero;
        private static TimeSpan hopTimeOneWay = TimeSpan.Zero;
        private static TimeSpan hopsTimeTotal = TimeSpan.Zero;
        private static TimeSpan hopsTimeTotalOneWay = TimeSpan.Zero;

        private static TimeSpan minDelta = TimeSpan.Zero;
        private static TimeSpan minDeltaOneWay = TimeSpan.Zero;
        private static TimeSpan maxDelta = TimeSpan.Zero;
        private static TimeSpan maxDeltaOneWay = TimeSpan.Zero;

        private static Mutex mutex = new Mutex();

        private static void OnNewMessage(SignalNowClient signalNow, string senderId, string messageType, string messagePayload)
        {
            if (messageType.Equals("MSGBOX", StringComparison.InvariantCultureIgnoreCase))
            {
                var now = DateTime.UtcNow;

                bool sentTimeIsMine = false;
                int starIndex = 0;
                string senderHostName = string.Empty;

                starIndex = messagePayload.IndexOf('*');
                if(messagePayload[0] == '#') // this is a message every other client sent back to me, it has MY 'SENT' TIME
                {
                    sentTimeIsMine = true;
                    senderHostName = messagePayload.Substring(1, starIndex - 1);
                }
                else
                {
                    senderHostName = messagePayload.Substring(0, starIndex);
                }

                string timeString = messagePayload.Substring(starIndex + 1);

                int dataIndex = timeString.IndexOf(' ');
                if(dataIndex != -1)
                {
                    timeString = timeString.Substring(0, dataIndex);
                }
                long fileTimeUtc = long.Parse(timeString);

                DateTime messageTime = DateTime.FromFileTimeUtc(fileTimeUtc);
                TimeSpan delta = now - messageTime;

                // If it's not for me personally, just send the original time back 
                // SendUpdateMessage will add '#' prefix 
                if(!sentTimeIsMine && taskList.Count < maxNumberOfPendingTasks) 
                {
                    SendUpdateMessage(messageTime, senderId);
                }

                mutex.WaitOne();

                try
                {
                    if(sentTimeIsMine)
                    {
                        messagesReceived++;
                        hopsTimeTotal += delta;
                        hopTime = hopsTimeTotal / messagesReceived;
                        
                        if(minDelta > delta || minDelta == TimeSpan.Zero)
                            minDelta = delta;
                        if(maxDelta < delta || maxDelta == TimeSpan.Zero)
                            maxDelta = delta;
                    }
                    else if(!sentTimeIsMine && senderHostName == hostName) // only can measure one-way time if from the same host 
                    {
                        messagesReceivedOneWay++;
                        hopsTimeTotalOneWay += delta;
                        hopTimeOneWay = hopsTimeTotalOneWay / messagesReceivedOneWay;
                        
                        if(minDeltaOneWay > delta || minDeltaOneWay == TimeSpan.Zero)
                            minDeltaOneWay = delta;
                        if(maxDeltaOneWay < delta || maxDeltaOneWay == TimeSpan.Zero)
                            maxDeltaOneWay = delta;
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static int maxNumberOfPendingTasks = 4;
        private static ConcurrentDictionary<long, Task> taskList = new ConcurrentDictionary<long, Task>();

        private static void SendTimerHandler(object state)
        {
            if(taskList.Count < maxNumberOfPendingTasks)
            {
                SendUpdateMessage(DateTime.UtcNow);
            }
        }

        private static Task<bool> SendUpdateMessage(DateTime timeToSend, string signleRecipient = null)
        {
            DateTime timeNow = timeToSend;
            long ft = timeNow.ToFileTime();
            Task<bool> taskSend = null;

            double[] data = new double [3 * 3 * new Random().Next(1, 64)];
            var dataString = JsonConvert.SerializeObject(data);

            if(signleRecipient == null)
            {
                taskSend = client.SendMessageToAll("MSGBOX", $"{hostName}*{ft.ToString()} {dataString}", true);
            }
            else
            {
                taskSend = client.SendMessage(signleRecipient, false, "MSGBOX", $"#{hostName}*{ft.ToString()} {dataString}", true);
            }

            Task.Run(()=>
            {
                taskList[ft] = taskSend;
                
                var postTask = taskSend.ContinueWith(async (t)=>
                {
                    while(taskList.ContainsKey(ft))
                    {
                        Task removed;
                        if(taskList.TryRemove(ft, out removed))
                            break;

                        await Task.Delay(1);
                    }
                });
            });

            return taskSend;
        }
    }
}
