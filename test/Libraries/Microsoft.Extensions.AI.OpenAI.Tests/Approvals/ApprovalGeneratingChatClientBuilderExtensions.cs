// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides extension methods for attaching a <see cref="McpChatClient"/> to a chat pipeline.
/// </summary>
public static class ApprovalGeneratingChatClientBuilderExtensions
{
    public static ChatClientBuilder UseFunctionApprovalGeneration(
        this ChatClientBuilder builder,
        ILoggerFactory? loggerFactory = null)
    {
        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= (ILoggerFactory)services.GetService(typeof(ILoggerFactory))!;

            var chatClient = new ApprovalGeneratingChatClient(innerClient, loggerFactory);
            return chatClient;
        });
    }
}
