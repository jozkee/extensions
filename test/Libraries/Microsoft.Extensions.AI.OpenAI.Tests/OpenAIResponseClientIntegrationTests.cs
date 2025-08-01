// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.TestUtilities;
using OpenAI;
using OpenAI.Responses;
using Xunit;

namespace Microsoft.Extensions.AI;

public class OpenAIResponseClientIntegrationTests : ChatClientIntegrationTests
{
    protected override IChatClient? CreateChatClient() =>
        IntegrationTestHelpers.GetOpenAIClient()
        ?.GetOpenAIResponseClient(TestRunnerConfiguration.Instance["OpenAI:ChatModel"] ?? "gpt-4o-mini")
        .AsIChatClient();

    public override bool FunctionInvokingChatClientSetsConversationId => true;

    // Test structure doesn't make sense with Responses.
    public override Task Caching_AfterFunctionInvocation_FunctionOutputUnchangedAsync() => Task.CompletedTask;

    [ConditionalFact]
    public async Task UseWebSearch_AnnotationsReflectResults()
    {
        SkipIfNotEnabled();

        var response = await ChatClient.GetResponseAsync(
            "Write a paragraph about the three most recent blog posts on the .NET blog. Cite your sources.",
            new() { Tools = [new HostedWebSearchTool()] });

        ChatMessage m = Assert.Single(response.Messages);
        TextContent tc = m.Contents.OfType<TextContent>().First();
        Assert.NotNull(tc.Annotations);
        Assert.NotEmpty(tc.Annotations);
        Assert.All(tc.Annotations, a =>
        {
            CitationAnnotation ca = Assert.IsType<CitationAnnotation>(a);
            var regions = Assert.IsType<List<AnnotatedRegion>>(ca.AnnotatedRegions);
            Assert.NotNull(regions);
            Assert.Single(regions);
            var region = Assert.IsType<TextSpanAnnotatedRegion>(regions[0]);
            Assert.NotNull(region);
            Assert.NotNull(region.StartIndex);
            Assert.NotNull(region.EndIndex);
            Assert.NotNull(ca.Url);
            Assert.NotNull(ca.Title);
            Assert.NotEmpty(ca.Title);
        });
    }

    [ConditionalFact]
    public async Task RemoteMcp_ListTools()
    {
        SkipIfNotEnabled();

        ChatOptions chatOptions = new()
        {
            // Replace this with HostedMcpServerTool once that's exposed.
            // https://github.com/openai/openai-dotnet/issues/406
            RawRepresentationFactory = (_) =>
            {
                var r = new ResponseCreationOptions();
                r.Tools.Add(GetInternalMcpTool("wiki_tools", "https://mcp.deepwiki.com/mcp"));
                return r;
            }
        };

        ChatResponse response = await CreateChatClient()!.GetResponseAsync("Which tools are available on the wiki_tools MCP server?", chatOptions);

        Assert.Contains("read_wiki_structure", response.Text);
        Assert.Contains("read_wiki_contents", response.Text);
        Assert.Contains("ask_question", response.Text);
    }

    [ConditionalFact]
    public async Task RemoteMcp_CallTool()
    {
        SkipIfNotEnabled();
        Debugger.Launch();

        using LoggingHttpHandler handler = new();
        using HttpClient httpClient = new(handler);
        var options = new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient)
        };

        ChatOptions chatOptions = new()
        {
            // Replace this with HostedMcpServerTool once that's exposed.
            // https://github.com/openai/openai-dotnet/issues/406
            RawRepresentationFactory = (_) =>
            {
                var r = new ResponseCreationOptions();
                r.Tools.Add(GetInternalMcpTool("my-mcp-server", "https://my-mcp-hyegfqe0d5hjhpge.westus3-01.azurewebsites.net/sse"));
                return r;
            }
        };

        var client = new AzureOpenAIClient(
            new Uri("https://dacan-m8dpzcmb-eastus2.openai.azure.com/"),
            new DefaultAzureCredential(true),
            options).GetOpenAIResponseClient("gpt-4o-mini").AsIChatClient();

        List<ChatMessage> chatHistory = [new(ChatRole.User, "Convert 100F to Celsius")];
        ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);

        while (true)
        {
            chatHistory.AddRange(response.Messages);

            IEnumerable<HostedMcpServerToolApprovalRequestContent> approvalRequests = response.Messages
                .SelectMany(m => m.Contents
                .OfType<HostedMcpServerToolApprovalRequestContent>());

            if (!approvalRequests.Any())
            {
                break;
            }

            var req = Assert.Single(approvalRequests);
            chatHistory.Add(req.Approve());

            response = await client.GetResponseAsync(chatHistory, chatOptions);
        }

        ////Assert.Contains("src/Libraries/Microsoft.Extensions.AI.Abstractions/README.md", response.Text);

        Type t = GetInternalOpenAIType("OpenAI.Responses.InternalMCPCallItemResource")!;
        IEnumerable<AIContent> contents = response.Messages
            .SelectMany(m => m.Contents
            .Where(c => (c.RawRepresentation?.GetType().Equals(t) ?? false) &&
                t.GetProperty("Name")!.GetValue(c.RawRepresentation)!.Equals("fahrenheit_to_celsius")));

        object rawRepresentation = Assert.Single(contents).RawRepresentation!;
        string callId = (string)t.GetProperty("Id")!.GetValue(rawRepresentation)!;

        HostedMcpServerToolCallContent mcpToolCall = new(callId,
            (string)t.GetProperty("Name")!.GetValue(rawRepresentation)!,
            (string)t.GetProperty("ServerLabel")!.GetValue(rawRepresentation)!)
        {
            Arguments = JsonSerializer.Deserialize<IReadOnlyDictionary<string, object?>>((string)t.GetProperty("Arguments")!.GetValue(rawRepresentation)!),
            RawRepresentation = rawRepresentation
        };

        HostedMcpServerToolResultContent mcpToolResult = new(callId)
        {
            Output = [new TextContent((string?)t.GetProperty("Output")!.GetValue(rawRepresentation))],
            IsError = (string?)t.GetProperty("Error")!.GetValue(rawRepresentation) is null
        };

        Assert.NotNull(mcpToolResult.Output);
        ////Assert.False(mcpToolResult.IsError);

        Assert.Equal("fahrenheit_to_celsius", mcpToolCall.Name);
        ////Assert.Equal("deepwiki", mcpToolCall.ServerName);
        Assert.NotNull(mcpToolCall.Arguments);
        ////Assert.Equal("dotnet/extensions", mcpToolCall.Arguments["repoName"]?.ToString());
        ////Assert.True(mcpToolCall.Arguments.ContainsKey("question"));
    }

    private static Type GetInternalOpenAIType(string fqName)
        => typeof(ResponseTool).Assembly.GetType(fqName)!;

    private static ResponseTool GetInternalMcpTool(string name, string url)
    {
        Type mcpToolType = GetInternalOpenAIType("OpenAI.Responses.InternalMCPTool")!;
        object instance = Activator.CreateInstance(mcpToolType, name, url)!;

        // Disable approvals until we have the necessary abstraction.
        ////mcpToolType.GetProperty("RequireApproval")?.SetValue(instance, BinaryData.FromString("\"never\""));

        return (ResponseTool)instance;
    }

    [ConditionalFact]
    public async Task LocalMcp_CallTool()
    {
        SkipIfNotEnabled();
        Debugger.Launch();

        using LoggingHttpHandler handler = new();
        using HttpClient httpClient = new(handler);
        var options = new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient)
        };

        var client = new AzureOpenAIClient(
            new Uri("https://dacan-m8dpzcmb-eastus2.openai.azure.com/"),
            new DefaultAzureCredential(true),
            options).GetOpenAIResponseClient("gpt-4o-mini")
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions((o) => { })
            .UseMcpClient(new Uri("https://my-mcp-hyegfqe0d5hjhpge.westus3-01.azurewebsites.net/sse"), "my-mcp-server")
            .UseFunctionInvocation()
            .Build();

        ChatOptions chatOptions = new();
        List<ChatMessage> chatHistory = [new(ChatRole.User, "Convert 33.8 °F to Celsius, respond with digits only.")];
        ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);
        Assert.Equal("1", response.Text);
    }
}
