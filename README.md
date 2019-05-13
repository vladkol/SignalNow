SignalNow: Serverless Signaling and Real-Time Messaging
===========================================================

SignalNow is a real-time signaling service built with [Azure SignalR](https://azure.microsoft.com/en-us/services/signalr-service/) and [Azure Functions](https://azure.microsoft.com/en-us/services/functions/). 
SignalNow key features: 
1. Serverless. 
2. Real-time and easily scalable with Azure Functions and SignalR. 
3. Extensible authentication with integrated authentication using [Microsoft Azure Active Directory](https://azure.microsoft.com/en-us/services/active-directory/), [Microsoft Accounts](https://account.microsoft.com), [GitHub accounts](https://developer.github.com/v3/guides/basics-of-authentication/), as well as key-based mechanism.

## How to get started
1. [Deploy](Docs/Deployment.md) 
2. [Use](Docs/Client%20SDKs%20and%20samples.md) (.NET Core 2.2, Unity 2018.3+).

* [Details](#details)

## Deploy to Azure
[![Deploy to Azure](https://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fvladkol%2FSignalNow%2Fmaster%2FAzure%2FDeployment%2Fazuredeploy.json)

### A minimal C# sample 
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

## Details
[Serverless Signaling Documentation](Docs/Serverless%20Signaling.md)

[SignalNowKey Authentication](Docs/SignalNowKey%20Authentication.md)

[Signaling Messages](Docs/Signaling%20Messages.md)

[TURN REST Authentication ](Docs/TURN%20REST%20Authentication.md)

[Deployment](Docs/Deployment.md)

[Client SDKs and Samples](Docs/Client%20SDKs%20and%20samples.md)

## Source code

[**Azure Function app**](Azure/Functions/SignalNow)
(Azure/Functions/SignalNow folder)

[**ARM Template**](Azure/Deployment/azuredeploy.json)

[**.NET Core 2.2 Client and demo app**](Client/CS/CS-Sample)
(Client/CS/CS-Sample folder)

[**Unity 2018.3 app**](Client/CS/CS-Unity/SignalNowAR)
*(Scripting Runtime .NET 4.x, both with .NET Standard 2.0 and .NET 4.x compatibility levels, works on iOS, Android, WinDesktop, UWP, MacOS*, 
Client/CS/CS-Unity/SignalNowAR folder)

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
