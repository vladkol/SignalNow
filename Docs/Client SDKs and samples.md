Client SDKs and samples
===========================================================

[**.NET Core 2.2 Client and demo app**](../Client/CS/CS-Sample)
(Client/CS/CS-Sample folder)

```cs
using System;
using Microsoft.SignalNow.Client;

string graphName = "signalnowkey"; // Use "graph.microsoft.com" for Azure Active Directory (AAD) and Microsoft Graph, or "github.com" for GitHub  
string userName = "vlad"; // or "vladkol@microsoft.com" for AAD, or "vladkol" for GitHub 
string company = "microsoft"; // or MSFT AAD Tenant Id, or GitHub organization name
string team = "cse"; // or any AAD group id, or GitHub team name, or * for not limiting it to a specific group
string deviceId = Guid.NewGuid().ToString(); // May be device's MAC address 

// If Azure Active Directory used, the authenticating app should have permissions sufficient for calling checkMemberGroups API 
// See https://docs.microsoft.com/en-us/graph/api/user-checkmembergroups?view=graph-rest-beta (as of today, it is Directory.Read.All) 
string authBearer = "INSERT YOUR SIGNALNOW KEY OR BEARER TOKEN HERE";  

SignalNowClient client = new SignalNowClient("mysignalnow"); // server will be resolved to mysignalnow.azurewibsites.net 

client.NewPeer += (SignalNowClient signalNow, SignalNowPeer newPeer) =>
{
    Console.WriteLine($"{newPeer.UserName} connected");
};

client.PeerStatusChanged += (SignalNowClient signalNow, SignalNowPeer peer) =>
{
    if(peer.Status == PeerStatus.Offline)
    {
        DateTime peerTime = DateTime.UtcNow;
        Console.WriteLine($"Peer {peer.UserName} went offline");
    }
};

client.RequestFailed += (SignalNowClient signalNow, string error) =>
{
    Console.WriteLine($"Signaling request failed. Message: {error}");
};

client.NewMessage += async (SignalNowClient signalNow, string senderId, string messageType, string messagePayload)=>
{
    switch(messageType.ToUpperInvariant())
    {
        case "PING":
            Console.WriteLine($"Received a PING message from {senderId}. Payload: {messagePayload}");
            await client.SendMessage(senderId, false, "PONG", messagePayload, true); 
            break;
        case "PONG":
            Console.WriteLine($"Received a PONG message from {senderId}");
            break;
        default:
            break;
    }
};

if (await client.Connect(userName,
        deviceId,
        company,
        team,
        authBearer,
        graphName))
{
    Console.WriteLine($"Connected to {client.ServerAddress} as {client.UserId}");
    await client.SendMessageToAll("PING", $"Hello!", false);
}
else
{
    Console.WriteLine($"Cannot connect to {client.ServerAddress}");
}
```

[**Unity 2018.3 app**](../Client/CS/CS-Unity/SignalNowAR)
*(Scripting Runtime .NET 4.x, both with .NET Standard 2.0 and .NET 4.x compatibility levels, works on iOS, Android, WinDesktop, UWP, MacOS*, 
Client/CS/CS-Unity/SignalNowAR folder)

Unity app sends devices poses from the mobile scene to the desktop scene. SignalNowManager + SignalNowTransformable are two key components you want to learn about first. 

Unity and C# clients share the same code. It is relatively complex because higly optimized for sending small messages very frequently (< 30ms) with low latency. 

[**TypeScript (in development)**]()

 

 

 
