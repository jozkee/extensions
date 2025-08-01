// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

public class McpChatClient : DelegatingChatClient
{
    private readonly IClientTransport _clientTransport;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger _logger;
    private IMcpClient? _mcpClient;
    private IList<McpClientTool>? _tools;

    public McpChatClient(IClientTransport clientTransport, IChatClient innerClient, ILoggerFactory? loggerFactory)
        : base(innerClient)
    {
        _clientTransport = clientTransport ?? throw new ArgumentNullException(nameof(clientTransport));
        _loggerFactory = loggerFactory;
        _logger = (ILogger?)loggerFactory?.CreateLogger<McpChatClient>() ?? NullLogger.Instance;
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _mcpClient ??= await McpClientFactory.CreateAsync(_clientTransport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
        _tools ??= await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        options = options?.Clone() ?? new ChatOptions();
        options.Tools ??= new List<AITool>();
        IList<AITool> optionsTools = options.Tools;

        foreach (McpClientTool tool in _tools)
        {
            _logger.LogTrace($"Tool: {tool.Name}, Description: {tool.Description}");
            optionsTools.Add(new AIFunctionApproval(tool, static (object? ctx) =>
            {
                Console.WriteLine(ctx);
                return true;
            }));
        }

        return await base.GetResponseAsync(messages, options, cancellationToken);
    }
}
