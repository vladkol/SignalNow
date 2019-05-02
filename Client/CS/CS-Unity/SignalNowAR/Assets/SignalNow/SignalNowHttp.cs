using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.SignalNow.Client
{
    public class SignalNowHttp : IDisposable
    {
        private static readonly TimeSpan clientRenewalTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan disposeWaitingTime = TimeSpan.FromMilliseconds(500);
        private static readonly long httpRequestsBeforeReseed = 3000;
        private static readonly TimeSpan lifeTimeBeforeReseed = TimeSpan.FromMinutes(1);

        protected class SignalNowHttpClientInternal : IDisposable
        {
            private DateTime _lifeStartTime = DateTime.UtcNow;
            private long _activeRequests = 0;
            private long _totalRequests = 0;
            private bool recycling = false;
            private Mutex recyclingMutex = new Mutex();

            protected HttpClient _httpClient = new HttpClient();

            public bool TimeToRecycle
            {
                get
                {
                    return (_totalRequests >= httpRequestsBeforeReseed
                            ||
                                (_totalRequests > 0
                                 && DateTime.UtcNow - _lifeStartTime >= lifeTimeBeforeReseed));
                }
            }

            public SignalNowHttpClientInternal(CancellationToken cancellation)
            {
                _httpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue()
                {
                    NoCache = true
                };
                _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            }

            public Task<HttpResponseMessage> SendRequestLiteAsync(HttpRequestMessage request, bool readResponse, CancellationToken cancellation)
            {
                Interlocked.Increment(ref _activeRequests);
                Interlocked.Increment(ref _totalRequests);
                return _httpClient.SendAsync(request, 
                                    readResponse ? 
                                            HttpCompletionOption.ResponseContentRead :
                                            HttpCompletionOption.ResponseHeadersRead, 
                                    cancellation);
            }

            public void RequestCompleted()
            {
                Interlocked.Decrement(ref _activeRequests);
            }

            public bool StartRecycling()
            {
                bool waited = false;

                try
                {
                    if (!recycling)
                    {
                        recyclingMutex.WaitOne();
                        waited = true;
                    }

                    if (recycling)
                    {
                        return false;
                    }
                    else
                    {
                        recycling = true;
                        return true;
                    }
                }
                finally
                {
                    if(waited)
                    {
                        recyclingMutex.ReleaseMutex();
                    }
                }
            }

            public void CancelRecycling()
            {
                recycling = false;
            }

            public void ResetLifeTime()
            {
                _lifeStartTime = DateTime.UtcNow;
            }

            public void Dispose()
            {
                if (_httpClient == null)
                    return;

                Task.Run(() =>
                {
                    while (_activeRequests > 0)
                    {
                        Task.Delay(disposeWaitingTime).Wait();
                    }
                    try
                    {
                        _httpClient.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error when disposing a SignalNowHttp: {ex.Message}");
                    }
                    _httpClient = null;
                });
            }
        }


        private CancellationTokenSource _cancellation = new CancellationTokenSource();
        public CancellationToken CancellationToken
        {
            get
            {
                return _cancellation.Token;
            }
        }

        private SignalNowHttpClientInternal _client;
        private string _serverUri;

        private bool _disposed = false;
        private Action<string, Exception> requestFailedHandler = null;

        private SignalNowHttp()
        {
#if DEBUG
            throw new Exception("This SignalNowHttp constructor is not supposed to be called");
#endif
        }

        public SignalNowHttp(string serverUri, Action<string, Exception> requestFailedHandler)
        {
            _serverUri = serverUri;
            _client = new SignalNowHttpClientInternal(_cancellation.Token);
            this.requestFailedHandler = requestFailedHandler;

            Task.Run(() =>
            {
                do
                {
                    Task.Delay(clientRenewalTimeout, _cancellation.Token).Wait();
                    if (!_cancellation.IsCancellationRequested 
                            && _client.TimeToRecycle
                            && _client.StartRecycling())
                    {
                        SignalNowHttpClientInternal newClient = new SignalNowHttpClientInternal(_cancellation.Token);

                        // "warming up" HttpClient 
                        using (var request = new HttpRequestMessage(HttpMethod.Get, _serverUri))
                        {
                            newClient.SendRequestLiteAsync(request, true, 
                                                            _cancellation.Token).ContinueWith((t) =>
                            {
                                // swapping old client with a new one 
                                if (!_cancellation.IsCancellationRequested && !t.IsFaulted)
                                {
                                    var oldClient = _client;
                                    newClient.ResetLifeTime();
                                    _client = newClient;
                                    oldClient.Dispose();
                                    newClient = null;

                                    t.Result.Dispose();
                                }
                                else
                                {
                                    _client.CancelRecycling();

                                    if (!t.IsFaulted)
                                    {
                                        try
                                        {
                                            t.Result?.Dispose();
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Exception when disposing a HttpResponseMessage: {ex.Message}");
                                        }
                                    }

                                    newClient.Dispose();
                                }
                            }).Wait();
                        }
                    }
                }
                while (!_cancellation.IsCancellationRequested);

            }, _cancellation.Token);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Cancel();
            _client.Dispose();
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public HttpRequestMessage CreateHttpRequest(string relativeUri, HttpMethod httpMethod)
        {
            CheckDisposed();

            Uri fullUri = new Uri(new Uri(_serverUri), relativeUri);
            HttpRequestMessage request = new HttpRequestMessage(httpMethod, fullUri);
#if NETCOREAPP2_2 || NETCOREAPP2_1
            request.Version = System.Net.HttpVersion.Version20;
#endif
            return request;
        }

        public async Task<bool> SendRequestLiteAsync(HttpRequestMessage httpRequest, bool disposeRequest = false)
        {
            return (await SendLiteAsync(httpRequest, false, disposeRequest) != null);
        }

        public async Task<string> SendRequestLiteWithResultAsync(HttpRequestMessage httpRequest, bool disposeRequest = false)
        {
            return await SendLiteAsync(httpRequest, true, disposeRequest);
        }

        // Performes HttpClient.SendAsync, only returns non-null if request was successful 
        // returns empty string is readResponse is false 
        private Task<string> SendLiteAsync(HttpRequestMessage httpRequest, bool readResponse, bool disposeRequest)
        {
            CheckDisposed();

            return _client.SendRequestLiteAsync(httpRequest, readResponse, CancellationToken).ContinueWith((t) =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"Exception when making HTTP request: {t.Exception.Message}");
                        requestFailedHandler?.Invoke(t.Exception.Message, t.Exception);
                        return null;
                    }

                    using (HttpResponseMessage response = t.Result)
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorString = $"HTTP request failed with ${response.StatusCode}. ";
                            string contentErrorString = string.Empty;

                            if (response.Content != null)
                            {
                                var ct = response.Content.ReadAsStringAsync();
                                ct.Wait();
                                contentErrorString = ct.Result;
                            }

                            if (string.IsNullOrWhiteSpace(contentErrorString) && response.ReasonPhrase != null)
                            {
                                contentErrorString = response.ReasonPhrase;
                            }

                            if (!string.IsNullOrWhiteSpace(contentErrorString))
                            {
                                errorString += contentErrorString;
                            }

                            Debug.WriteLine(errorString);
                            requestFailedHandler?.Invoke(errorString, new HttpRequestException(errorString));

                            return null;
                        }

                        string responseString = string.Empty;
                        if (readResponse)
                        {
                            var ct = response.Content.ReadAsStringAsync();
                            ct.Wait();
                            if(ct.Result != null)
                            {
                                responseString = ct.Result;
                            }
                        }

                        return responseString;
                    }
                    
                }
                finally
                {
                    _client.RequestCompleted();
                    if(disposeRequest)
                    {
                        httpRequest.Dispose();
                    }
                }
            });
        }
    }
}
