// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods

public class LoggingHttpHandler : DelegatingHandler
{
    public LoggingHttpHandler()
        : base(new HttpClientHandler())
    {
    }

    public LoggingHttpHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    ////private static string? _previousResponse;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine("=== HTTP REQUEST ===");
        Console.WriteLine($"{request.Method} {request.RequestUri}");

        if (request.Content != null)
        {
            string requestContent = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine("Request Content:");
            Console.WriteLine(requestContent);

            ////var node = JsonNode.Parse(requestContent)!;
            ////if (_previousResponse is not null)
            ////{
            ////    node["previous_response_id"] = _previousResponse;
            ////}

            ////requestContent = node.ToJsonString();

            // Important: Recreate the content since ReadAsStringAsync consumes it
            request.Content = new StringContent(requestContent, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        Console.WriteLine();

        // Call the next handler in the pipeline
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Log response
        Console.WriteLine("=== HTTP RESPONSE ===");
        Console.WriteLine($"Status: {response.StatusCode}");

        if (response.Content != null)
        {
            string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine("Response Content:");
            Console.WriteLine(responseContent);

            ////var node = JsonNode.Parse(responseContent)!;
            ////_previousResponse = node["id"]!.ToString();

            // Important: Recreate the content since ReadAsStringAsync consumes it
            response.Content = new StringContent(responseContent, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        Console.WriteLine("===================");
        Console.WriteLine();

        return response;
    }
}
