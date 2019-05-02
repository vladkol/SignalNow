
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
using System.Security.Cryptography;

namespace Microsoft.SignalNow
{
    public class AuthenticateSignalNowKey : IGraphService
    {
        private const string _hashSecuritySeed = "SignalNowSecSeed";
        public Task<GraphAuthStatus> IsGroupMember(string userName, string companyOrTenant, string groupOrTeam, string authToken, ILogger logger = null)
        {
            return Task.Run(()=>
            {
                ServiceUtils utils = new ServiceUtils(ConfigUtils.GetSignalRConnection());
                string key = utils.AccessKey;

                if(string.IsNullOrEmpty(userName) 
                    || string.IsNullOrEmpty(groupOrTeam)
                    || string.IsNullOrEmpty(authToken))
                {
                    logger.LogError($"Invalid userName, company or group name");
                    return GraphAuthStatus.InvalidName;
                }

                string hash = GenerateSignalNowKey(userName, companyOrTenant, groupOrTeam, key);

                return authToken == hash ? GraphAuthStatus.OK : GraphAuthStatus.NotMemberOfTargetGroup;
            });
        }

        internal static string GenerateSignalNowKey(string userName, string companyOrTenant, string groupOrTeam, string signalNowKey)
        {
            string nameHash1 = SignalRGroupUtils.GetNameHash(
                    SignalRGroupUtils.GetUserGroupId(_hashSecuritySeed, companyOrTenant, groupOrTeam, userName),
                    signalNowKey);
            string nameHash2 = SignalRGroupUtils.GetNameHash(nameHash1, signalNowKey);

            return nameHash1 + nameHash2;
        }
    }
}