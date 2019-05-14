// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Dynamic;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.SignalNow
{
    public class AuthenticateAAD : IGraphService
    {
        private HttpClient _client = new HttpClient();
        public async Task<GraphAuthStatus> IsGroupMember(string userName, string companyOrTenant, string groupOrTeam, string authToken, ILogger log = null)
        {
            if(string.IsNullOrEmpty(userName) 
            || string.IsNullOrEmpty(groupOrTeam)
            || string.IsNullOrEmpty(authToken))
            {
                log.LogError($"Invalid userName, company or group name");
                return GraphAuthStatus.InvalidName;
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            var tokenStatus = await IsUserAuthToken(_client, userName, companyOrTenant, log);

            if(tokenStatus != System.Net.HttpStatusCode.OK)
            {
                return GraphServiceFactory.GraphAuthStatusFromHttp(tokenStatus);
            }

            // If group doesn't matter we only check authentication status in AAD tenant 
            if(groupOrTeam == "*")
            {
                return GraphAuthStatus.OK;
            }

            GraphAuthStatus resStatus = GraphAuthStatus.UnknownError;

            string json = JsonConvert.SerializeObject(new GroupMembershipRequest()
                            {
                                groupIds = new string[] {groupOrTeam}
                            });
            using(var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                using(var response = await _client.PostAsync("https://graph.microsoft.com/beta/me/checkMemberGroups", content))
                {
                    if(response.IsSuccessStatusCode)
                    {
                        dynamic res = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
                        if(res.value.Count > 0)
                        {
                            resStatus = GraphAuthStatus.OK;
                        }
                        else
                        {
                            resStatus = GraphAuthStatus.NotMemberOfTargetGroup;
                        }
                    }
                    else
                    {
                        if(log != null)
                        {
                            log.LogError($"Team membership check failed: {response.StatusCode} ({await response.Content.ReadAsStringAsync()})");
                        }

                        resStatus = GraphServiceFactory.GraphAuthStatusFromHttp(response.StatusCode);
                    }
                }
            }

            return resStatus;
        }


        private static async Task<System.Net.HttpStatusCode> IsUserAuthToken(HttpClient client, string userPrincipalName, string tenantId, ILogger log)
        {
            System.Net.HttpStatusCode resStatus = System.Net.HttpStatusCode.Unauthorized;

            var response = await client.GetAsync("https://graph.microsoft.com/beta/me");
 
            if(response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                if(!string.IsNullOrEmpty(responseJson))
                {
                    dynamic res = JsonConvert.DeserializeObject(responseJson);
                    string upn = res.userPrincipalName;
                    string mail = res.mail;
                
                    if(string.Equals(upn, userPrincipalName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        resStatus =  System.Net.HttpStatusCode.OK;
                    }
                    else if(mail != null && string.Equals(mail, userPrincipalName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        resStatus =  System.Net.HttpStatusCode.OK;
                    }
                    else if(log != null)
                    {
                        log.LogError($"Bearer token is for {upn}, expected to be for {userPrincipalName}");
                    }
                }
                else if(log != null)
                {
                    log.LogError($"Graph bearer token check failed with empty response");
                }
            }
            else 
            {
                resStatus = response.StatusCode;
                if(log != null)
                    log.LogError($"Graph bearer token check failed: {response.StatusCode} ({await response.Content.ReadAsStringAsync()})");
            }

            return resStatus;
        }

        private class GroupMembershipRequest
        {
            public string[] groupIds;
        }


    }
}