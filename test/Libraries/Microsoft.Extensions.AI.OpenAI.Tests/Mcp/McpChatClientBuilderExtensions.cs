// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

/// <summary>
/// Provides extension methods for attaching a <see cref="McpChatClient"/> to a chat pipeline.
/// </summary>
public static class McpChatClientBuilderExtensions
{
    public static ChatClientBuilder UseMcpClient(
        this ChatClientBuilder builder,
        Uri mcpServerUrl,
        string mcpServerName,
        ILoggerFactory? loggerFactory = null)
    {
        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= (ILoggerFactory)services.GetService(typeof(ILoggerFactory))!;

            var sseClientTransport = new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = mcpServerUrl,
                Name = mcpServerName,
            }, loggerFactory);

            var chatClient = new McpChatClient(sseClientTransport, innerClient, loggerFactory);
            return chatClient;
        });
    }
}
