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
    public static class GenerateSignalNowKey
    {
        [FunctionName("GenerateSignalNowKey")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Admin, "post", Route = null)] HttpRequest req,
            ILogger log)
            {
                return await Task<IAsyncResult>.Run(()=>
                {
                    var signalRConnection = ConfigUtils.GetSignalRConnection();
                    log.LogInformation($"Generating a key with conection as {signalRConnection}");
                    ServiceUtils utils = new ServiceUtils(signalRConnection);
                    string key = utils.AccessKey;

                    var userName = req.Headers["username"].ToString().ToLowerInvariant(); 
                    var team = req.Headers["teamname"].ToString().ToLowerInvariant();  
                    var company = req.Headers["companyname"].ToString().ToLowerInvariant(); 

                    string hash = AuthenticateSignalNowKey.GenerateSignalNowKey(userName, company, team, key);

                    return (ActionResult)new OkObjectResult(hash);
                });
            }
    }
}
