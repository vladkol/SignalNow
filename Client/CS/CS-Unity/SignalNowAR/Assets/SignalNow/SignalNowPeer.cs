using System;

namespace Microsoft.SignalNow.Client
{
    public class SignalNowPeer
    {
#region private const declarations
        private const char deviceUserDelimiter = '|';
        private const char userDelimiter = '+'; 
        private const char teamDelimiter = '/'; 
        private const char deviceGroupDelimiter = ':'; // delimiter between deviceId and the rest of the group name when there is no username specified 
#endregion

        public string UserId { get; private set; }
        public string DeviceId { get; private set; }
        public string Company { get; private set; }
        public string Team { get; private set; }
        public string AuthService { get; private set; }
        public string UserName 
        { 
            get; 
            private set;
        }
        public PeerStatus Status { get; internal set; }
        public TimeSpan StatusExpirationTimeout  {get; internal set;}
        public DateTime LastStatusTime {get; internal set;}
        public DateTime LastDataMessageTime {get; internal set;}

        public SignalNowPeer(string userId)
        {
            SetUserId(userId);
            Status = PeerStatus.Offline;
            StatusExpirationTimeout = TimeSpan.MaxValue;
            LastStatusTime = DateTime.UtcNow;
            LastDataMessageTime = DateTime.MinValue;
        }

        public SignalNowPeer(string userId, PeerStatus status, TimeSpan statusExpirationTimeout)
        {
            SetUserId(userId);
            Status = status;
            StatusExpirationTimeout = statusExpirationTimeout;
            LastStatusTime = DateTime.UtcNow;
            LastDataMessageTime = DateTime.MinValue;
        }

        internal void Resurrect(PeerStatus status = PeerStatus.Online)
        {
            LastStatusTime = DateTime.UtcNow;
            Status = status;
        }

        private void SetUserId(string userId)
        {
            this.UserId = userId;
            string userName, deviceId, company, team, authService;
            ParseUserId(userId, out userName, out deviceId, out company, out team, out authService);
            UserName = userName;
            DeviceId = deviceId;
            Company = company;
            Team = team;
            AuthService = authService;
        }


        /// <summary>
        /// Parses full user id provided as $"{deviceId}|{userName}+{authServiceName}/{companyName}/{teamName}" to separate components:
        /// </summary>
        internal static void ParseUserId(string userId, out string userName, out string deviceId, out string company, out string team, out string authServiceName)
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


    }
}