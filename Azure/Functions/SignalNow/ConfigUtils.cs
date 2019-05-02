// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.SignalNow
{
    internal static class ConfigUtils
    {
        public static string GetSignalRConnection()
        {
            //TODO: Add Azure KeyVault option 
            return GetConfigValue("AzureSignalRConnectionString");
        }

        public static string GetHubName()
        {
            return GetConfigValue("HubName", "SignalNow");
        }

        public static string GetAuthTokenLifetimeMinutes()
        {
            return GetConfigValue("AuthTokenLifetimeMinutes", "60");
        }

        public static string GetSignalNowKey()
        {
            return GetConfigValue("SignalNowKey", "");
        }

        public static string GetTURNServersKey()
        {
            //TODO: Add Azure KeyVault option 
            return GetConfigValue("TURNAuthorizationKey", "");
        }

        public static string GetTURNServersList()
        {
            string listString = GetConfigValue("TURNServersList", "");
            if(!string.IsNullOrEmpty(listString))
            {
                var list = listString.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries);
                listString = string.Empty;
                foreach(var server in list)
                {
                    string addQuotes = string.Empty;
                    var serverClean = server.Trim();
                    if(serverClean[0] != '"' && serverClean[0] != '\'')
                    {
                        addQuotes = "\"";
                    }
                    listString += $"{addQuotes}{serverClean}{addQuotes},";
                }
            }

            return listString;
        }

        public static string MakeTURNAuthToken(string companyNameOrTenant)
        {
            string key = GetTURNServersKey();
            string servers = GetTURNServersList();
            if(string.IsNullOrEmpty(key) || string.IsNullOrEmpty(servers))
            {
                return string.Empty;
            }

            DateTime expirationTime = DateTime.UtcNow + TimeSpan.FromMinutes(long.Parse(GetAuthTokenLifetimeMinutes()));
            string userCombo = $"{(long)((expirationTime - DateTime.UnixEpoch).TotalSeconds)}:{companyNameOrTenant}";
            
            using(System.Security.Cryptography.HMACMD5 hashAlg = new System.Security.Cryptography.HMACMD5())
            {
                hashAlg.Key = Encoding.UTF8.GetBytes(key);
                byte[] bytes = hashAlg.ComputeHash(Encoding.UTF8.GetBytes(userCombo));
                string password = Convert.ToBase64String(bytes);

                string token = 
                    $"{{\"username\":\"{userCombo}\", \"password\":\"{password}\", " + 
                    $"\"uris\":[{servers}]}}";

                return token;
            }
        }

        private static string GetConfigValue(string name, string defaultValue = "")
        {
            var res = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if(string.IsNullOrEmpty(res))
                res = defaultValue;

            return res;
        }
    }
}