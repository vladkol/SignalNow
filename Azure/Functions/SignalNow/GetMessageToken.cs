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
using System.Web;

namespace Microsoft.SignalNow
{
    public static class GetMessageToken
    {
        [FunctionName("GetMessageToken")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var authToken = req.Headers["authtoken"].ToString();
            ServiceUtils utils = new ServiceUtils(ConfigUtils.GetSignalRConnection());
            string userIdHash = string.Empty;
            string userId = string.Empty;

            if(!utils.ParseAndValidateToken(authToken, out userIdHash, out userId, null))
            {
                log.LogError($"GetMessageToken: Invalid authentication token ({authToken})");
                return new BadRequestObjectResult("Invalid authentication token");
            }

            TimeSpan messageTokenLifetime = TimeSpan.FromMinutes(UInt64.Parse(ConfigUtils.GetAuthTokenLifetimeMinutes()));
            var messageToken = utils.GenerateAccessToken(utils.Endpoint, 
                              userId, 
                              messageTokenLifetime);

            string errorMessage = string.Empty;
            string userName;
            string company;
            string team;
            string authService;
            string deviceId;

            SignalRGroupUtils.ParseUserId(userId, out userName, out deviceId, out company, out team, out authService);

            var hubName = ConfigUtils.GetHubName();
            var groups = SignalRGroupUtils.GetUserGroups(authService, company, team, userName, deviceId);

            foreach(var group in groups)
            {
                var groupHash = SignalRGroupUtils.GetNameHash(group, utils.AccessKey);

                var url = $"{utils.Endpoint}/api/v1/hubs/{hubName}/groups/{groupHash}/users/{userIdHash}";
                var requestToken = utils.GenerateAccessToken(url, userId, TimeSpan.FromMinutes(1));
                
                using(var request = new HttpRequestMessage(HttpMethod.Put, url))
                {
                    request.Version = System.Net.HttpVersion.Version20;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", requestToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using(var httpResult = await SignalRHttpClient.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if(!httpResult.IsSuccessStatusCode)
                        {
                            var body = await httpResult.Content.ReadAsStringAsync(); 
                            errorMessage = $"Cannot add user {userId} ({userIdHash}) to group {group} ({groupHash}). Code: {httpResult.StatusCode}, Message: {httpResult.ReasonPhrase}, Body: {body}";
                            log.LogError(errorMessage);
                            break;
                        }
                    }
                }
            }

            return string.IsNullOrEmpty(errorMessage) 
                ? (ActionResult)new OkObjectResult(messageToken) 
                : new BadRequestObjectResult(new { message = errorMessage });
        }
    }
}
