// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using System.Net;

namespace Microsoft.SignalNow
{
    public static class Message
    {
        private const int maxTries = 3;

        [FunctionName("Message")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var authToken = req.Headers["authtoken"].ToString(); // SignalNow authentication token received from GetMessageToken 
            var sendTo = req.Headers["sendto"].ToString().ToLowerInvariant(); // SignalNow username or group name 
            var userOrGroup = req.Headers["userorgroup"].ToString(); // Message's recipient - "user" or "group" 
            var messageType = req.Headers["messagetype"].ToString(); // Message type (any string, defined by the signaling protocol)

            string payload = string.Empty;
            
            // Message payload may be passed in "payload" header or, if there is no such header, via request body 
            if(req.Headers.ContainsKey("payload"))
            {
                payload = req.Headers["payload"].ToString(); 
            }
            else
            {        
                // If message pack was used to create playload, there should be "withmessagepack" header. 
                // If its value is "lz", then payload is compressed with MessagePack.LZ4MessagePackSerializer. 
                // We don't use req.ContentType because of ambiguity between application/x-msgpack and application/msgpack
                // https://github.com/msgpack/msgpack/issues/194 (plus we also need to differentiate between uncompressed and compressed)
                if(req.Headers.ContainsKey("withmessagepack"))
                {
                    var mpValue = req.Headers["withmessagepack"].ToString();
                    bool useLZ = string.Equals("lz", mpValue, StringComparison.InvariantCultureIgnoreCase);
                    
                    int capacity = 0;
                    if(req.Body.Length >= 0 && req.Body.Length <= int.MaxValue)
                    {
                        capacity = (int)req.Body.Length;
                    }

                    using(var ms = new MemoryStream(capacity))
                    {
                        await req.Body.CopyToAsync(ms);
                        byte[] messageData = ms.ToArray();

                        payload = useLZ ? 
                                        MessagePack.LZ4MessagePackSerializer.ToJson(messageData)
                                        : MessagePack.MessagePackSerializer.ToJson(messageData);
                    }
                }
                else
                {
                    using(var sr = new StreamReader(req.Body))
                    {
                        payload = await sr.ReadToEndAsync();
                    }
                }
            }

            string userIdHash, userId;
            ServiceUtils utils = new ServiceUtils(ConfigUtils.GetSignalRConnection());

            if(!utils.ParseAndValidateToken(authToken, out userIdHash, out userId, null))
            {
                log.LogError($"Invalid authentication token ({authToken})");
                return new UnauthorizedResult();
            }
            
            if(!SignalRGroupUtils.CanSendMessage(userId, sendTo))
            {
                log.LogError($"{userId} is not allowed to send messages to {sendTo}");
                return new BadRequestObjectResult(new { message = $"{userId} is not allowed to send messages to {sendTo}" });
            }

            var hubName = ConfigUtils.GetHubName();

            var sendToHash = SignalRGroupUtils.GetNameHash(sendTo, utils.AccessKey);
            
            string url = string.Equals(userOrGroup, "group", StringComparison.InvariantCultureIgnoreCase) ? 
                             $"{utils.Endpoint}/api/v1/hubs/{hubName}/groups/{sendToHash}" : 
                             $"{utils.Endpoint}/api/v1/hubs/{hubName}/users/{sendToHash}"; 
            string sendToken = utils.GenerateAccessToken(url, userId, TimeSpan.FromMinutes(1));

            PayloadMessage message = new PayloadMessage()
            {
                Target = "SIGNAL",
                Arguments = new[]
                {
                    userId,
                    messageType,
                    payload
                }
            };

            bool msgSent = false;
            string errMessage = string.Empty;
            HttpStatusCode httpStatus = HttpStatusCode.OK;
            string errPhrase = string.Empty;
            string errContent = string.Empty;
            
            for(int i=0; i < maxTries; i++)
            {
                try
                {
                    using(var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        request.Version = System.Net.HttpVersion.Version20;
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", sendToken);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Content = new StringContent(JsonConvert.SerializeObject(message, Formatting.None), Encoding.UTF8, "application/json");

                        using(var requestResult = await SignalRHttpClient.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if(requestResult.IsSuccessStatusCode)
                            {
                                msgSent = true;
                                log.LogInformation($"{userId} sent a message to {sendTo}");
                            }
                            else
                            {
                                errPhrase = requestResult.ReasonPhrase;
                                if(requestResult.Content != null)
                                {
                                    errContent = await requestResult.Content.ReadAsStringAsync();
                                }
                                log.LogError($"Cannot send signal. Error {requestResult.StatusCode}: {requestResult.ReasonPhrase}");
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    errMessage = ex.Message;
                    errPhrase = ex.GetType().Name;
                    errContent = ex.Message;
                    log.LogError($"Exception when sending a SignalR request: {errMessage}");
                }

                if(msgSent)
                    break;
            }

            if(msgSent)
            {
                return (ActionResult)new OkResult();
            }
            else
            {
                return new BadRequestObjectResult(
                                new { message = $"Cannot send signal. Error {httpStatus}: {errPhrase} ({errContent})"}
                                );
            }
        }


        internal class PayloadMessage
        {
            public string Target { get; set; }
            public object[] Arguments { get; set; }
        }
    }
}
