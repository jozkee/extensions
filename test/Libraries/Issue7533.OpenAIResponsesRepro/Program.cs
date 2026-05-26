// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;

#pragma warning disable OPENAI001 // Experimental OpenAI APIs

string endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1";
string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
string logPath = Environment.GetEnvironmentVariable("OPENAI_RESPONSES_REPRO_LOG_PATH") ??
    Path.Combine(AppContext.BaseDirectory, $"issue7533-openai-responses-repro-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
string wireLogPath = Environment.GetEnvironmentVariable("OPENAI_RESPONSES_WIRE_LOG_PATH") ??
    Path.Combine(AppContext.BaseDirectory, $"issue7533-openai-responses-wire-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
var logBuilder = new StringBuilder();
var wireLogBuilder = new StringBuilder();

void Log(string message)
{
    Console.WriteLine(message);
    _ = logBuilder.AppendLine($"{DateTimeOffset.UtcNow:O} {message}");
}

void LogWire(string message) => _ = wireLogBuilder.AppendLine($"{DateTimeOffset.UtcNow:O} {message}");

int exitCode = 0;
try
{
    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Log("Missing OPENAI_API_KEY.");
        exitCode = 1;
    }
    else
    {
        using var innerHttpHandler = new HttpClientHandler();
        using var wireLoggingHandler = new WireLoggingHandler(innerHttpHandler, LogWire);
        using var httpClient = new HttpClient(wireLoggingHandler, disposeHandler: false);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        IChatClient chatClient = new ChatClientBuilder(openAIClient.GetResponsesClient().AsIChatClient(model))
            .UseFunctionInvocation()
            .Build();

        var writeTool = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(
                (string value) => $"saved:{value}",
                "write_note"));

        var options = new ChatOptions
        {
            Tools = [writeTool],
            ToolMode = ChatToolMode.RequireSpecific("write_note"),
            Temperature = 0,
        };

        List<ChatMessage> transcript = BuildTranscriptWithResolvedApprovalsAndNoFccMessages();

        Log("Issuing OpenAI Responses repro call...");
        Log($"Endpoint: {endpoint}");
        Log($"Model: {model}");

        try
        {
            ChatResponse response = await chatClient.GetResponseAsync(transcript, options);
            Log("Call succeeded.");
            Log($"Returned messages: {response.Messages.Count}");
            foreach (ChatMessage message in response.Messages)
            {
                Log($"{message.Role}: {string.Join(", ", message.Contents.Select(static c => c.GetType().Name))}");
            }
        }
        catch (ClientResultException ex)
        {
            Log("Call failed:");
            Log(ex.Message);
            exitCode = 2;
        }
    }
}
finally
{
    await File.WriteAllTextAsync(logPath, logBuilder.ToString());
    await File.WriteAllTextAsync(wireLogPath, wireLogBuilder.ToString());
    Console.WriteLine($"Log written to: {logPath}");
    Console.WriteLine($"Wire log written to: {wireLogPath}");
}

return exitCode;

// This shape intentionally mirrors the problematic transcript: prior approvals are resolved
// via TARC + TAResp + FRC, but standalone FCC assistant messages are absent.
static List<ChatMessage> BuildTranscriptWithResolvedApprovalsAndNoFccMessages() =>
    [
        new ChatMessage(ChatRole.User, "Seed turn."),

        new ChatMessage(
            ChatRole.Assistant,
            [new ToolApprovalRequestContent("ficc_callId1", new FunctionCallContent("callId1", "write_note", new Dictionary<string, object?> { ["value"] = "first" }))])
        { MessageId = "resp1" },
        new ChatMessage(
            ChatRole.User,
            [new ToolApprovalResponseContent("ficc_callId1", approved: true, new FunctionCallContent("callId1", "write_note", new Dictionary<string, object?> { ["value"] = "first" }))]),
        new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("callId1", "saved:first")]),

        new ChatMessage(
            ChatRole.Assistant,
            [new ToolApprovalRequestContent("ficc_callId2", new FunctionCallContent("callId2", "write_note", new Dictionary<string, object?> { ["value"] = "second" }))])
        { MessageId = "resp2" },
        new ChatMessage(
            ChatRole.User,
            [new ToolApprovalResponseContent("ficc_callId2", approved: true, new FunctionCallContent("callId2", "write_note", new Dictionary<string, object?> { ["value"] = "second" }))]),
        new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("callId2", "saved:second")]),

        // New approval to process on this turn.
        new ChatMessage(
            ChatRole.Assistant,
            [new ToolApprovalRequestContent("ficc_callId3", new FunctionCallContent("callId3", "write_note", new Dictionary<string, object?> { ["value"] = "third" }))])
        { MessageId = "resp3" },
        new ChatMessage(
            ChatRole.User,
            [new ToolApprovalResponseContent("ficc_callId3", approved: true, new FunctionCallContent("callId3", "write_note", new Dictionary<string, object?> { ["value"] = "third" }))]),
    ];
