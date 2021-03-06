﻿using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wikiled.Common.Utilities.Helpers;

namespace Wikiled.Common.Utilities.Auth.OAuth
{
    public class OAuthHelper : IOAuthHelper
    {
        private readonly ILogger<OAuthHelper> logger;
        private string redirectUri;

        public OAuthHelper(ILogger<OAuthHelper> logger, int? port = null)
        {
            this.logger = logger;
            RedirectUri = $"http://{IPAddress.Loopback}:{port ?? GetRandomUnusedPort()}/";
            logger.LogInformation("redirect URI: " + RedirectUri);
        }

        public string RedirectUri
        {
            get => redirectUri;
            set
            {
                if (redirectUri != value)
                {
                    logger.LogInformation("Changing redirect URI: " + RedirectUri);
                    redirectUri = value;
                }
            }
        }

        public string Code { get; private set; }

        public bool IsSuccessful { get; private set; }

        public async Task Start(string serviceUrl, string state = null)
        {
            IsSuccessful = false;
            var http = new HttpListener();
            http.Prefixes.Add(RedirectUri);
            logger.LogInformation("Listening...");
            http.Start();

            // Opens request in the browser.
            ExternaApp.OpenUrl(serviceUrl);

            // Waits for the OAuth authorization response.
            HttpListenerContext context = await http.GetContextAsync().ConfigureAwait(false);

            // Sends an HTTP response to the browser.
            HttpListenerResponse response = context.Response;
            var responseString = "<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>Please return to the app.</body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            System.IO.Stream responseOutput = response.OutputStream;
            Task responseTask = responseOutput.WriteAsync(buffer, 0, buffer.Length)
                                              .ContinueWith(
                                                  task =>
                                                  {
                                                      responseOutput.Close();
                                                      http.Stop();
                                                      logger.LogInformation("HTTP server stopped.");
                                                  });

            // Checks for errors.
            if (context.Request.QueryString.Get("error") != null)
            {
                logger.LogInformation(($"OAuth authorization error: {context.Request.QueryString.Get("error")}"));
                return;
            }

            if (context.Request.QueryString.Get("code") == null || context.Request.QueryString.Get("state") == null)
            {
                logger.LogInformation("Malformed authorization response. " + context.Request.QueryString);
                return;
            }

            // extracts the code
            var code = context.Request.QueryString.Get("code");
            var incomingState = context.Request.QueryString.Get("state");

            // Compares the received state to the expected value, to ensure that
            // this app made the request which resulted in authorization.
            if (state != null &&
                incomingState != state)
            {
                logger.LogInformation($"Received request with invalid state ({incomingState})");
                return;
            }

            logger.LogInformation("Authorization code: " + code);
            Code = code;
            IsSuccessful = true;
        }

        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
