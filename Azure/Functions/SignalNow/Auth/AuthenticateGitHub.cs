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
using GraphQL.Client;
using GraphQL.Common.Request;

namespace Microsoft.SignalNow
{
    public class AuthenticateGitHub : IGraphService
    {
        public async Task<GraphAuthStatus> IsGroupMember(string userName, string companyOrTenant, string groupOrTeam, string authToken, ILogger log = null)
        {
            // authToken should be aquired with read:org scope included 

            if(string.IsNullOrEmpty(userName) 
            || string.IsNullOrEmpty(companyOrTenant) 
            || string.IsNullOrEmpty(authToken))
            {
                log.LogError($"Invalid userName, company or authentication token");
                return GraphAuthStatus.InvalidName;
            }

            bool isMember = false;

            using (var client = new GraphQLClient("https://api.github.com/graphql"))
            {
                bool isPersonalToken = authToken.StartsWith(":");
                string authTokenType = isPersonalToken ? "token" : "bearer";
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(authTokenType, 
                                                isPersonalToken ? authToken.Substring(1) : authToken);
                client.DefaultRequestHeaders.Add("User-Agent", "SignalNow");

                var userLoginRequest = new GraphQLRequest
                {
                    Query = @"
                                query {
                                  viewer {
                                    login
                                  }
                                }"
                };

                string githubLogin = string.Empty;
                try
                {
                    var userResult = await client.PostAsync(userLoginRequest);
                    if (userResult.Errors != null && userResult.Errors.Length > 0)
                    {
                        log.LogError($"Token verification failed: {userResult.Errors[0].Message}");
                        return GraphAuthStatus.UnknownError;
                    }
                    else
                    {
                        githubLogin = userResult.Data.viewer.login;
                        if (githubLogin != userName)
                        {
                            log.LogError($"Bearer token is for {githubLogin}, expected to be for {userName}");
                            return GraphAuthStatus.Unauthorized;
                        }
                    }
                }
                catch(Exception ex)
                {
                    log.LogError($"Token verification failed with exception: {ex.Message}");
                    return GraphAuthStatus.UnknownError;
                }

                if (groupOrTeam == "" || groupOrTeam == "*") // only check organization membership
                {
                    var orgMembersRequest = new GraphQLRequest
                    {
                        Query = @"
                                query {
                                  organization(login: ""%orgname%"") {
                                        viewerIsAMember
                                    }
                                }".Replace("%orgname%", companyOrTenant)
                    };

                    try
                    {
                        var orgResult = await client.PostAsync(orgMembersRequest);
                        if (orgResult.Errors != null && orgResult.Errors.Length > 0)
                        {
                            log.LogError($"Organization membership verification failed: {orgResult.Errors[0].Message}");
                            return GraphAuthStatus.UnknownError;
                        }
                        else if (orgResult.Data.organization.viewerIsAMember != null)
                        {
                            isMember = (bool)orgResult.Data.organization.viewerIsAMember;
                        }
                    }
                    catch(Exception ex)
                    {
                        log.LogError($"Organization membership verification failed with exception: {ex.Message}");
                        return GraphAuthStatus.UnknownError;
                    }
                }
                else
                {
                    var teamMembersRequest = new GraphQLRequest
                    {
                        Query = @"
                                query {
                                  organization(login: ""%orgname%"") {
                                        team(slug: ""%teamname%"")
                                        {
                                            members(query: ""%username%"", first: 1)
                                            {
                                                nodes
                                                {
                                                    login
                                                }
                                            }
                                        }
                                    }
                                }".Replace("%username%", githubLogin).Replace("%orgname%", companyOrTenant).Replace("%teamname%", groupOrTeam)
                    };

                    try
                    {
                        var teamResult = await client.PostAsync(teamMembersRequest);

                        if (teamResult.Errors != null && teamResult.Errors.Length > 0)
                        {
                            log.LogError($"Team membership verification failed: {teamResult.Errors[0].Message}");
                            return GraphAuthStatus.UnknownError;
                        }
                        else if (teamResult.Data.organization != null && teamResult.Data.organization.team != null)
                        {
                            var nodes = teamResult.Data.organization.team.members.nodes;
                            foreach (dynamic node in nodes)
                            {
                                var user = node.login;
                                if (user == githubLogin)
                                {
                                    isMember = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogError($"Team membership verification failed with exception: {ex.Message}");
                        return GraphAuthStatus.UnknownError;
                    }

                }
            }

            return isMember ? GraphAuthStatus.OK : GraphAuthStatus.NotMemberOfTargetGroup;
        }
    }
}