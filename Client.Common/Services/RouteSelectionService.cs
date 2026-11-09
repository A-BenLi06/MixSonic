namespace Client.Common.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.IO;
    using System.Xml;
    using System.Xml.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Client.Common.Services.Navidrome;

    /// <summary>
    /// Service that selects the best route (URL) to the Subsonic server by testing connectivity.
    /// </summary>
    public class RouteSelectionService : IRouteSelectionService
    {
        private readonly INetworkDetectionService _networkDetectionService;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        public RouteSelectionService(INetworkDetectionService networkDetectionService)
        {
            _networkDetectionService = networkDetectionService;
        }

        /// <inheritdoc />
        public Task<RouteSelectionResult> SelectBestRouteAsync(
            string primaryUrl,
            string secondaryUrl,
            string username,
            string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SelectBestRouteAsync(new SubsonicServiceConfiguration { PrimaryUrl = primaryUrl, SecondaryUrl = secondaryUrl, Username = username, Password = password }, cancellationToken);
        }

        public async Task<RouteSelectionResult> SelectBestRouteAsync(SubsonicServiceConfiguration configuration, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            var primaryUrl = configuration.PrimaryUrl;
            var secondaryUrl = configuration.SecondaryUrl;
            var username = configuration.Username;
            var password = configuration.Password;
            var authentication = configuration.AuthenticationMode;
            cancellationToken.ThrowIfCancellationRequested();
            var hasPrimary = !string.IsNullOrWhiteSpace(primaryUrl);
            var hasSecondary = !string.IsNullOrWhiteSpace(secondaryUrl);

            // No URLs configured
            if (!hasPrimary && !hasSecondary)
            {
                return new RouteSelectionResult
                {
                    Success = false,
                    FailureReason = "No server URLs configured"
                };
            }

            // Only one URL configured - just test that one
            if (hasPrimary && !hasSecondary)
            {
                return await TestSingleUrlAsync(primaryUrl, username, password, authentication, cancellationToken);
            }
            if (hasSecondary && !hasPrimary)
            {
                return await TestSingleUrlAsync(secondaryUrl, username, password, authentication, cancellationToken);
            }

            // Both URLs configured - check network type
            var networkType = _networkDetectionService.GetCurrentConnectionType();

            // On cellular network (Windows only optimization): only test secondary (assumed DDNS/external)
            // Note: On WP, we always test both addresses as per user requirement
#if WINDOWS_APP
            if (networkType == NetworkConnectionType.Cellular)
            {
                return await TestSingleUrlAsync(secondaryUrl, username, password, authentication, cancellationToken);
            }
#endif

            // WiFi/Ethernet/Unknown: test both URLs concurrently
            return await TestBothUrlsAsync(primaryUrl, secondaryUrl, username, password, authentication, cancellationToken);
        }

        private async Task<RouteSelectionResult> TestSingleUrlAsync(
            string url,
            string username,
            string password,
            SubsonicAuthenticationMode authentication,
            CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(Timeout);

                try
                {
                    var success = await PingUrlAsync(url, username, password, authentication, cts.Token);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (success)
                    {
                        return new RouteSelectionResult
                        {
                            Success = true,
                            SelectedUrl = url
                        };
                    }

                    return new RouteSelectionResult
                    {
                        Success = false,
                        FailureReason = "Server ping failed"
                    };
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new RouteSelectionResult
                    {
                        Success = false,
                        FailureReason = "Connection timeout"
                    };
                }
                catch (Exception ex)
                {
                    return new RouteSelectionResult
                    {
                        Success = false,
                        FailureReason = ex.Message
                    };
                }
            }
        }

        private async Task<RouteSelectionResult> TestBothUrlsAsync(
            string primaryUrl,
            string secondaryUrl,
            string username,
            string password,
            SubsonicAuthenticationMode authentication,
            CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(Timeout);

                var tasks = new List<Task<PingTaskResult>>
                {
                    PingUrlWithResultAsync(primaryUrl, username, password, authentication, cts.Token),
                    PingUrlWithResultAsync(secondaryUrl, username, password, authentication, cts.Token)
                };

                // Use WhenAny pattern to get first successful result
                while (tasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var result = await completedTask;
                        if (result.Success)
                        {
                            // Cancel remaining tasks
                            cts.Cancel();
                            return new RouteSelectionResult
                            {
                                Success = true,
                                SelectedUrl = result.Url
                            };
                        }
                    }
                    catch
                    {
                        // Task failed, continue waiting for others
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new RouteSelectionResult
                {
                    Success = false,
                    FailureReason = "All routes failed or timed out"
                };
            }
        }

        private async Task<bool> PingUrlAsync(
            string baseUrl,
            string username,
            string password,
            SubsonicAuthenticationMode authentication,
            CancellationToken cancellationToken)
        {
            var config = new SubsonicServiceConfiguration
            {
                BaseUrl = baseUrl,
                Username = username,
                Password = password, AuthenticationMode = authentication
            };

            var pingUrl = BuildPingUrl(config);

            using (var client = new HttpClient { Timeout = Timeout })
            {
                try
                {
                    using (var response = await client.GetAsync(pingUrl, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode) return false;
                        var root = ReadResponse(await response.Content.ReadAsStringAsync());
                        cancellationToken.ThrowIfCancellationRequested();
                        if (root == null || (string)root.Attribute("status") != "ok") return false;
                    }
                    if (authentication != SubsonicAuthenticationMode.ApiKey) return true;
                    using (var response = await client.GetAsync(new SubsonicRequest(config, "tokenInfo").Url, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode) return false;
                        var root = ReadResponse(await response.Content.ReadAsStringAsync());
                        cancellationToken.ThrowIfCancellationRequested();
                        var token = root == null ? null : root.Element(SubsonicXml.Namespace + "tokenInfo");
                        return root != null && (string)root.Attribute("status") == "ok" && token != null &&
                            !string.IsNullOrWhiteSpace((string)token.Attribute("username")) &&
                            string.Equals((string)token.Attribute("username"), username, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return false;
                }
            }
        }

        private async Task<PingTaskResult> PingUrlWithResultAsync(
            string baseUrl,
            string username,
            string password,
            SubsonicAuthenticationMode authentication,
            CancellationToken cancellationToken)
        {
            var success = await PingUrlAsync(baseUrl, username, password, authentication, cancellationToken);
            return new PingTaskResult { Url = baseUrl, Success = success };
        }

        private static string BuildPingUrl(ISubsonicServiceConfiguration config)
        {
            return new SubsonicRequest(config, "ping").Url;
        }

        private static XElement ReadResponse(string text)
        {
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                return XDocument.Load(reader).Element(SubsonicXml.Namespace + "subsonic-response");
        }

        private class PingTaskResult
        {
            public string Url { get; set; }
            public bool Success { get; set; }
        }
    }
}
