// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;


namespace Microsoft.SignalNow
{
    /// <summary>
    /// Provides utility methods for SignalR group management. 
    /// <para> User name is formed as: </para>
    /// <para> {deviceId}|{userName}+{authServiceName}/{companyName}/{teamName} </para>
    /// </summary>
    /// <remarks>
    /// authServiceName - Authentication and people graph service, e.g. 'github.com' or 'teams.microsoft.com'.
    /// companyName - Company name, company id or tenant id, e.g. 'microsoft' or '72f988bf-86f1-41af-91ab-2d7cd011db47'.
    /// teamName - Team name or team id, e.g. 'MyTeam' or '11244a09-ff97-4178-9635-4ca38dfb1015'.
    /// userName - User name, e.g. 'vlad' or 'vlad@microsoft.com'.
    /// deviceId - Device id (MAC address or GUID expected).
    /// </remarks>
    public static class SignalRGroupUtils
    {
#region private declarations
        private const char deviceUserDelimiter = '|';
        private const char userDelimiter = '+'; 
        private const char teamDelimiter = '/'; 
        private const char deviceGroupDelimiter = ':'; // delimiter between deviceId and the rest of the group name when there is no username specified 
#endregion

#region public methods
        /// <summary>
        /// Returns true if user user userName can send a meessage to user of group defined by sendTo.
        /// The rule is that they must be from the same team. 
        /// <example>
        /// <para>'00-14-22-01-23-44|myusername+github.com/mycompany/myteam' <c>can</c> send to 'github.com/mycompany/myteam'</para>
        /// <para>'00-14-22-01-23-44|myusername+github.com/mycompany/myteam' <c>can</c> send to 'anotheruser+github.com/mycompany/myteam'</para>
        /// <para>'00-14-22-01-23-44|myusername+github.com/mycompany/myteam' <c>can</c> send to '00-14-66-01-55-77|anotheruser+github.com/mycompany/myteam'</para>
        /// <para>'00-14-22-01-23-44|myusername+github.com/mycompany/myteam' <c>can</c> send to '00-14-66-01-55-77:github.com/mycompany/myteam'</para>
        /// <para>'00-14-22-01-23-44|myusername+github.com/mycompany/myteam' can <c>NOT</c> send to 'anotheruser+github.com/mycompany/anotherteam'</para>
        /// </example>
        /// </summary>
        public static bool CanSendMessage(string userName, string sendTo)
        {
            int indexDeviceGroupTo = sendTo.IndexOf(deviceGroupDelimiter); // if sending to device, it will be position of '^' in deviceId^authService/Company/Team 
            int indexUserTo = sendTo.IndexOf(userDelimiter); // if sending to user, it will be position of '!' in [deviceId+]user!authService/Company/Team 
            int toGroupIndex = indexUserTo != -1 ? indexUserTo : indexDeviceGroupTo; // still may be -1 if sendTo is a team group, e.g. github.com/Company/Team 

            int indexUserFrom = userName.IndexOf(userDelimiter); // position of '!' which separates deviceId+user and authService (userName always has deviceId) 

            bool canSend = false;
            if(indexUserFrom != -1)
            {
                string sendToGroup = sendTo.Substring(toGroupIndex + 1); 
                string sendFromGroup = userName.Substring(indexUserFrom + 1);

                canSend = string.Equals(sendFromGroup, sendToGroup, StringComparison.InvariantCultureIgnoreCase);
            }

            return canSend;
        }


        /// <summary>
        /// Returns full user id as a combination of deviceId, username, authentication service name, company name, and team name:
        /// <code>
        ///    $"{deviceId}|{userName}+{authServiceName}/{companyName}/{teamName}"
        /// </code>
        /// <example>
        ///     Example: 00-14-22-01-23-44|myusername+github.com/mycompany/myteam 
        /// </example>
        /// </summary>
        public static string GetFullUserId(string authServiceName, string companyName, string teamName, string userName, string deviceId)
        {
            string userId = $"{deviceId}{deviceUserDelimiter}{userName}{userDelimiter}{authServiceName}{teamDelimiter}{companyName}{teamDelimiter}{teamName}";

            return userId;
        }


        /// <summary>
        /// Parses full user id provided as $"{deviceId}|{userName}+{authServiceName}/{companyName}/{teamName}" to separate components:
        /// </summary>
        public static void ParseUserId(string userId, out string userName, out string deviceId, out string company, out string team, out string authServiceName)
        {
            userName = string.Empty;
            company = string.Empty;
            team = string.Empty;
            authServiceName = string.Empty;
            deviceId = string.Empty;

            int endofdeviceIndex = userId.IndexOf(deviceUserDelimiter);
            int endofusernameIndex = userId.IndexOf(userDelimiter);
            int endofserviceIndex = userId.IndexOf(teamDelimiter);
            int endofcompanyIndex = userId.IndexOf(teamDelimiter, endofserviceIndex + 1);

            deviceId = userId.Substring(0, endofdeviceIndex);
            userName = userId.Substring(endofdeviceIndex + 1, endofusernameIndex - endofdeviceIndex - 1);
            authServiceName = userId.Substring(endofusernameIndex + 1, endofserviceIndex - endofusernameIndex - 1);
            company = userId.Substring(endofserviceIndex + 1, endofcompanyIndex - endofserviceIndex - 1);
            team = userId.Substring(endofcompanyIndex + 1);
        }


        /// <summary>
        /// Returns user group id as a combination of username, authentication service name, company name, and team name. 
        /// Used for sending messages to all user's devices.  
        /// <code>
        ///    $"{userName}+{authServiceName}/{companyName}/{teamName}"
        /// </code>
        /// <example>
        ///     Example: myusername+github.com/mycompany/myteam 
        /// </example>
        /// </summary>
        public static string GetUserGroupId(string authServiceName, string companyName, string teamName, string userName)
        {
            string userId = $"{userName}{userDelimiter}{authServiceName}{teamDelimiter}{companyName}{teamDelimiter}{teamName}";

            return userId;
        }


        /// <summary>
        /// Returns team group id as a combination of authentication service name, company name, and team name. 
        /// Used for sending messages to all team members.  
        /// <code>
        ///    $"{authServiceName}/{companyName}/{teamName}"
        /// </code>
        /// <example>
        ///     Example: github.com/mycompany/myteam 
        /// </example>
        /// </summary>
        public static string GetTeamGroupId(string authServiceName, string companyName, string teamName)
        {
            string userId = $"{authServiceName}{teamDelimiter}{companyName}{teamDelimiter}{teamName}";

            return userId;
        }


        /// <summary>
        /// Returns device group id as a combination of device id, authentication service name, company name, and team name. 
        /// Used for sending messages to a device without knowing its user, but assuming it's from sender's group, when descovered in physical proximity (e.g. by BTLE MAC)
        /// <code>
        ///    $"{deviceId}:{authServiceName}/{companyName}/{teamName}"
        /// </code>
        /// <example>
        ///     Example: 00-14-22-01-23-44:github.com/mycompany/myteam 
        /// </example>
        /// </summary>
        public static string GetDeviceGroupName(string authServiceName, string companyName, string teamName, string deviceId)
        {
            string deviceUserId = $"{deviceId}{deviceGroupDelimiter}{authServiceName}{teamDelimiter}{companyName}{teamDelimiter}{teamName}";

            return deviceUserId;
        }


        /// <summary>
        /// Returns list of all groups a user should be in.
        /// <param name="authServiceName">Authentication and people graph service, e.g. 'github.com' or 'teams.microsoft.com'</param>
        /// <param name="companyName">Company name, company id or tenant id, e.g. 'microsoft' or '72f988bf-86f1-41af-91ab-2d7cd011db47'</param>
        /// <param name="teamName">Team name or team id, e.g. 'MyTeam' or '11244a09-ff97-4178-9635-4ca38dfb1015'</param>
        /// <param name="userName">User name, e.g. 'vlad' or 'vlad@microsoft.com'</param>
        /// <param name="deviceId">Device id. MAC address or GUID expected</param>
        /// </summary>
        public static IList<string> GetUserGroups(string authServiceName, string companyName, string teamName, string userName, string deviceId)
        {
            var groups = new List<string>();
            string userGroupName = GetUserGroupId(authServiceName, companyName, teamName, userName);
            string teamGroupName = GetTeamGroupId(authServiceName, companyName, teamName);
            string deviceUserId = GetDeviceGroupName(authServiceName, companyName, teamName, deviceId);

            groups.Add(userGroupName.ToLowerInvariant());
            groups.Add(teamGroupName.ToLowerInvariant());
            //groups.Add(deviceUserId.ToLowerInvariant());

            return groups;
        }


        /// <summary>
        /// Returns true if <paramref name="componentString"/> doesn't contain any special chacters (|+/:)
        /// </summary>
        public static bool IsValidUserIdComponent(string componentString)
        {
            return  componentString.IndexOf(deviceGroupDelimiter) == -1 && 
                    componentString.IndexOf(userDelimiter) == -1 &&
                    componentString.IndexOf(deviceUserDelimiter) == -1 &&
                    componentString.IndexOf(teamDelimiter) == -1;
        }


        /// <summary>
        /// Returns HMAC SHA256 hash value of <paramref name="name"/> with "u_" prefix (to comply with SignalR naming requirements)  
        /// </summary>
        public static string GetNameHash(string input, string hashKey)
        {
            StringBuilder hash = new StringBuilder("u_");
            using(System.Security.Cryptography.HMACMD5 hashAlg = new System.Security.Cryptography.HMACMD5())
            {
                hashAlg.Key = Encoding.UTF8.GetBytes(hashKey.Substring(Math.Min(0, hashKey.Length - 16)));
                byte[] bytes = hashAlg.ComputeHash(Encoding.UTF8.GetBytes(input));

                for (int i = 0; i < bytes.Length; i++)
                {
                    hash.Append(bytes[i].ToString("x2"));
                }
                return hash.ToString();
            }
        }

#endregion
    }
}