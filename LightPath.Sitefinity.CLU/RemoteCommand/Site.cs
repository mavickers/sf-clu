using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };

        /// <remarks>The offline page and a stopped pool answer 503; while Sitefinity starts, it redirects to the web project's sf:appStatusUrl.</remarks>
        private static bool IsStartupResponse(RunContext context, Uri requested, HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) return true;
            if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location) return false;

            var path = new Uri(requested, location).AbsolutePath.TrimEnd('/');

            return path.Equals(SitePath(context.Source.StatusPath).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Requests the web project's sf:HealthCheckApiEndpoint and prints the verdict from the body: the endpoint answers 200 for an unhealthy site too.</summary>
        /// <returns>False for an unhealthy site or no verdict; true when the web project sets no health path, so health leaves the exit code alone.</returns>
        private static bool ReportHealth(RunContext context)
        {
            if (context.Source.HealthPath == null)
            {
                WriteLine("Healthy: not configured (the web project sets no sf:HealthCheckApiEndpoint)");

                return true;
            }

            var uri = new Uri(new Uri(context.Profile.Site!), SitePath(context.Source.HealthPath));
            var stopwatch = Stopwatch.StartNew();

            using var client = CreateHttpClient();

            try
            {
                using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, uri));
                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = reader.ReadToEnd();
                var elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s";

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    WriteError($"Healthy: HTTP {(int)response.StatusCode} from {uri} in {elapsed}");

                    return false;
                }

                using var document = JsonDocument.Parse(body);

                static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

                if (!document.RootElement.TryGetProperty("Healthy", out var verdict) || verdict.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    WriteError($"Healthy: HTTP 200 from {uri} without a verdict");

                    return false;
                }

                var healthy = verdict.ValueKind == JsonValueKind.True;
                var failing = document.RootElement.TryGetProperty("Checks", out var checks) ? checks.EnumerateArray().Where(check => check.TryGetProperty("Passed", out var passed) && passed.ValueKind == JsonValueKind.False).ToList() : new List<JsonElement>();

                WriteLine($"Healthy: {healthy} (HTTP 200 in {elapsed})");

                foreach (var check in failing) WriteLine($"  failed: {Text(check, "Operation")} - {Text(check, "Message")}");

                return healthy;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                WriteError($"Healthy: {uri} gave no verdict: {exception.Message}");

                return false;
            }
        }

        /// <summary>An app-relative path from Web.config as a path from the site root: "~/a" and "a" become "/a".</summary>
        private static string SitePath(string path) => "/" + path.TrimStart('~').TrimStart('/');

        /// <summary>After <c>--deploy</c> removes the offline page: requests the site until Sitefinity answers with something other than its startup response.</summary>
        private static int WaitForSite(RunContext context)
        {
            var site = new Uri(context.Profile.Site!);
            var stopwatch = Stopwatch.StartNew();
            var followed = false;
            string? lastResult = null;

            using var client = CreateHttpClient();

            WriteLine($"-- {StepStartup}: requesting {site}");

            while (stopwatch.Elapsed < StartupTimeout)
            {
                try
                {
                    using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, site));

                    // A redirect to another scheme or server (an IIS http-to-https rule) comes before Sitefinity has started; follow it once.
                    if (!followed && (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { IsAbsoluteUri: true } target && Uri.Compare(target, site, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        WriteLine($"{site} redirects to {target}; requesting that address instead");
                        site = target;
                        followed = true;

                        continue;
                    }

                    if (!IsStartupResponse(context, site, response))
                    {
                        var code = (int)response.StatusCode;

                        (code < 500 ? (Action<string>)WriteLine : WriteError)($"Sitefinity answered HTTP {code} {stopwatch.Elapsed.TotalSeconds:0} s after the offline page was removed");

                        return code < 500 ? 0 : 1;
                    }

                    lastResult = $"HTTP {(int)response.StatusCode}";
                }
                catch (HttpRequestException exception) when (exception.InnerException is AuthenticationException)
                {
                    WriteError($"{site} failed certificate validation: {exception.InnerException.Message}");

                    return 1;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastResult = exception.Message;
                }

                Thread.Sleep(TimeSpan.FromSeconds(2));
            }

            WriteError($"Sitefinity did not finish starting within {StartupTimeout.TotalMinutes:0} minutes; the last result was {lastResult}");

            return 1;
        }
    }
}
