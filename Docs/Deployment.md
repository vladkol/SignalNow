Deployment
===========================================================

You can deploy SignalNow as an Azure Function app and Azure SignalR service using this [**ARM Template**](../Azure/Deployment/asuredeploy.json) (azuredeploy.json in Azure/Deployment folder)

## Deploy to Azure
[![Deploy to Azure](https://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fvladkol%2FSignalNow%2Fmaster%2FAzure%2FDeployment%2Fazuredeploy.json) 

## Manual deployment

If you prefer manual deployment here are the steps:

1.  Create an Azure SignalR service.

2. Copy connection string from **Keys** tab

![](media/05be40d4ad2630ba9c393f315b76f75b.png)

3.  Create an Azure Function app.

![](media/37ece5ecf85951d658453ad6e3928bf2.png)

4.  In Application Settings section, create a new setting with name "**AzureSignalRConnectionString**" and value of **Connection String** you copied from SignalR

![](media/8df33fe2c809219abe336652fcbe88ef.png)
