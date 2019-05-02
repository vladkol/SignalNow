// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.SignalNow
{
    public interface IGraphService
    {
        Task<GraphAuthStatus> IsGroupMember(string userName, string companyOrTenant, string groupOrTeam, string authToken, ILogger logger = null);
    }

    public enum GraphAuthStatus
    {
        NotMemberOfTargetGroup = 0, 
        UnknownError,
        InvalidName, 
        OK = System.Net.HttpStatusCode.OK, 
        _firstHttpCode = OK, 
        Unauthorized = System.Net.HttpStatusCode.Unauthorized, 
        Forbidden = System.Net.HttpStatusCode.Forbidden, 
        ServerError = System.Net.HttpStatusCode.InternalServerError, 
        ServiceUnavailable = System.Net.HttpStatusCode.ServiceUnavailable, 

    }

    public static class GraphServiceFactory
    {
        public static IGraphService GetGraphService(string name)
        {
            switch(name.ToLowerInvariant())
            {
                case "graph.microsoft.com":
                case "teams.microsoft.com":
                    return new AuthenticateAAD();
                case "signalnowkey":
                    return new AuthenticateSignalNowKey();
                default:
                    return null;
            }

        }

        public static GraphAuthStatus GraphAuthStatusFromHttp(System.Net.HttpStatusCode code)
        {
            if((int)code >= (int)GraphAuthStatus._firstHttpCode)
                return (GraphAuthStatus)(int)code;
            else
            {
                return GraphAuthStatus.UnknownError;
            }
        }
    }
}