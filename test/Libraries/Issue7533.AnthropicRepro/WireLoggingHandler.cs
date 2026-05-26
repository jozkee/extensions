// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class WireLoggingHandler(HttpMessageHandler innerHandler, Action<string> logWire) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _ = request.RequestUri ?? throw new InvalidOperationException("Expected request URI to be set.");
        logWire($"--> {request.Method} {request.RequestUri}");
        LogHeaders(request.Headers, logWire);
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            LogHeaders(request.Content.Headers, logWire, "Content-");
            string body = await request.Content.ReadAsStringAsync(cancellationToken);
            logWire(body);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        logWire($"<-- {(int)response.StatusCode} {response.ReasonPhrase}");
        LogHeaders(response.Headers, logWire);
        if (response.Content is not null)
        {
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            LogHeaders(response.Content.Headers, logWire, "Content-");
            string body = Encoding.UTF8.GetString(bytes);
            logWire(body);

            // Preserve content for downstream consumers after reading it for logging.
            var replacementContent = new ByteArrayContent(bytes);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                _ = replacementContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = replacementContent;
        }

        logWire(string.Empty);
        return response;
    }

    private static void LogHeaders(HttpHeaders headers, Action<string> logWire, string? prefix = null)
    {
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            string value = string.Join(", ", header.Value);
            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                value = "<redacted>";
            }

            logWire($"{prefix}{header.Key}: {value}");
        }
    }
}
