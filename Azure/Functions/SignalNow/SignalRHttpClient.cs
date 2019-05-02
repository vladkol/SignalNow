// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http;

namespace Microsoft.SignalNow
{
    public static class SignalRHttpClient
    {
        private static HttpClient _httpClient;
        static SignalRHttpClient()
        {
            //System.AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", false); // https://stackoverflow.com/questions/53764083/use-http-2-with-httpclient-in-net

            System.Net.HttpWebRequest.DefaultWebProxy = null;
            try
            {
                _httpClient = new HttpClient(new WinHttpHandler()); // To be switched to the default HTTP Handler
            }
            catch(System.PlatformNotSupportedException)
            {
                _httpClient = new HttpClient();
            }
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        }

        public static HttpClient HttpClient
        {
            get
            {
                return _httpClient;
            } 
        }
    }
}