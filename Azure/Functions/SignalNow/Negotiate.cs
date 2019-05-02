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

namespace Microsoft.SignalNow
{
    public static class Negotiate
    {
        [FunctionName("Negotiate")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, Route = null)] HttpRequest req,
            ILogger log)
        {
            string errorMessage = string.Empty;

            var deviceId = req.Headers["deviceid"].ToString().ToLowerInvariant(); // Device id, either a MAC address or a random string per session 
            var userName = req.Headers["username"].ToString().ToLowerInvariant(); // Username as registered in choosen authernication service (e.g. AAD userPrincipalName, GitHub username)
            var team = req.Headers["teamname"].ToString().ToLowerInvariant(); // Teamname - id or name of the target group or team. Must be group id for AAD, team id for GitHub 
            var company = req.Headers["companyname"].ToString().ToLowerInvariant(); // Company name or tenant id. Must be tenant id for AAD, company id for GitHub 
            var authServiceToken = req.Headers["authservicetoken"].ToString(); // Authentication token received from authentication service. Bearer token for AAD or GitHub. 
            var authService = req.Headers["authservicename"].ToString().ToLowerInvariant(); // Authentication sercvice name. "graph.microsoft.com" for AAD, "github.com" for GitHub 

            var provider = GraphServiceFactory.GetGraphService(authService);
            GraphAuthStatus authStatus = GraphAuthStatus.Unauthorized;
            if(provider == null)
            {
                errorMessage = $"{authService} is not a valid graph service name.";
            }
            else
            {
                authStatus = await provider.IsGroupMember(userName, company, team, authServiceToken, log);
            }

            ObjectResult funcResult = null;
            string userId = string.Empty;

            if(authStatus == GraphAuthStatus.OK)
            {
                userId = SignalRGroupUtils.GetFullUserId(authService, company, team, userName, deviceId);
                userId = userId.ToLowerInvariant();
                
                ServiceUtils utils = new ServiceUtils(ConfigUtils.GetSignalRConnection());
                var hubName = ConfigUtils.GetHubName();
                var clientUrl = $"{utils.Endpoint}/client/?hub={hubName}";
                var clientToken = utils.GenerateAccessToken(clientUrl, 
                                userId, 
                                TimeSpan.FromMinutes(UInt64.Parse(ConfigUtils.GetAuthTokenLifetimeMinutes())));

                string serverTime = ((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds).ToString();
                string turnServersAuthorization = ConfigUtils.MakeTURNAuthToken(company);

                funcResult = new OkObjectResult(new string[] {userId, clientToken, clientUrl, serverTime, turnServersAuthorization});
            }
            else
            {
                if(string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = $"Graph verification failed. Reason: {authStatus}";
                }
            }

            if(!string.IsNullOrEmpty(errorMessage))
            {
                log.LogError(errorMessage);
            }
            else
            {
                log.LogInformation($"Successfully negotiated for {userName} as {userId}");
            }

            return string.IsNullOrEmpty(errorMessage) 
                ? (ActionResult)funcResult
                : new BadRequestObjectResult(new { message = errorMessage });
        }
    }
}
