// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;

#pragma warning disable OPENAI001 // Experimental OpenAI APIs

// Parse mode from args: responses-openai | completions-openai | completions-anthropic
string mode = args.Length > 0 ? args[0] : "completions-anthropic";
if (mode is not ("responses-openai" or "completions-openai" or "completions-anthropic"))
{
    Console.Error.WriteLine("Usage: <program> [responses-openai|completions-openai|completions-anthropic]");
    return 1;
}

// Resolve endpoint, model, and API key based on mode.
(string endpoint, string defaultModel, string apiKeyEnvVar) = mode switch
{
    "completions-anthropic" => (
        Environment.GetEnvironmentVariable("ANTHROPIC_ENDPOINT") ?? "https://api.anthropic.com/v1",
        Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-5",
        "ANTHROPIC_API_KEY"),
    _ => (
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1",
        Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini",
        "OPENAI_API_KEY"),
};

string modelId = defaultModel;
string timestamp = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
string logPath = Path.Combine(AppContext.BaseDirectory, $"issue7533-{mode}-{timestamp}.log");
string wireLogPath = Path.Combine(AppContext.BaseDirectory, $"issue7533-{mode}-wire-{timestamp}.log");
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
    string? apiKey = Environment.GetEnvironmentVariable(apiKeyEnvVar);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Log($"Missing {apiKeyEnvVar}.");
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

        // Build the inner IChatClient based on mode.
        IChatClient innerClient = mode == "responses-openai"
            ? openAIClient.GetResponsesClient().AsIChatClient(modelId)
            : openAIClient.GetChatClient(modelId).AsIChatClient();

        // One approval-required tool: write_note.
        var writeTool = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(
                (string value) =>
                {
                    Log($"  [tool executed] write_note(\"{value}\") => saved:{value}");
                    return $"saved:{value}";
                },
                "write_note",
                "Saves a note. Requires user approval."));

        IChatClient chatClient = new ChatClientBuilder(innerClient)
            .UseFunctionInvocation()
            .Build();

        var options = new ChatOptions
        {
            Tools = [writeTool],
            Temperature = 0,
        };

        // The caller owns the transcript across stateless turns.
        // We'll queue up multiple user prompts that each trigger write_note,
        // building up resolved approval pairs in the transcript.
        Queue<string> userPrompts = new([
            "Please save a note with the value 'first'.",
            "Now save another note with the value 'second'.",
            "Save one more note with the value 'third'.",
        ]);

        List<ChatMessage> transcript =
        [
            new ChatMessage(ChatRole.User, userPrompts.Dequeue()),
        ];

        Log($"=== Approval Flow Repro ({mode}) ===");
        Log($"Endpoint: {endpoint}");
        Log($"Model: {modelId}");
        Log(string.Empty);

        // Multi-turn loop: send transcript, handle approval requests, resume.
        for (int turn = 1; turn <= 20; turn++)
        {
            Log($"--- Turn {turn} ---");
            Log($"  Transcript has {transcript.Count} message(s)");

            ChatResponse response = await chatClient.GetResponseAsync(transcript, options);
            Log($"  Response has {response.Messages.Count} message(s)");

            // Append response messages to transcript.
            foreach (ChatMessage msg in response.Messages)
            {
                transcript.Add(msg);
                string contentSummary = string.Join(", ", msg.Contents.Select(static c => c.GetType().Name));
                Log($"  [{msg.Role}] {contentSummary}");
            }

            // Check if the response contains any approval requests.
            var approvalRequests = response.Messages
                .SelectMany(static m => m.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            if (approvalRequests.Count > 0)
            {
                Log($"  => {approvalRequests.Count} approval request(s) found. Auto-approving...");
                List<AIContent> approvalResponses = [];
                foreach (var req in approvalRequests)
                {
                    Log($"     Approving: {req.RequestId} (callId: {req.ToolCall.CallId})");
                    approvalResponses.Add(
                        new ToolApprovalResponseContent(req.RequestId, approved: true, req.ToolCall));
                }

                transcript.Add(new ChatMessage(ChatRole.User, approvalResponses));
                continue; // Next turn will process the approval and execute the tool.
            }

            // No approvals pending — we got a text response.
            string? finalText = response.Messages
                .SelectMany(static m => m.Contents)
                .OfType<TextContent>()
                .Select(static t => t.Text)
                .LastOrDefault();

            if (finalText is not null)
            {
                Log($"  => Model replied: {finalText}");
            }

            // If there are more prompts, append the next one to trigger another tool call.
            if (userPrompts.Count > 0)
            {
                string nextPrompt = userPrompts.Dequeue();
                Log($"  => Injecting next user message: \"{nextPrompt}\"");
                transcript.Add(new ChatMessage(ChatRole.User, nextPrompt));
                continue;
            }

            // All prompts exhausted — done.
            Log(string.Empty);
            Log($"=== All 3 approval rounds completed successfully.");
            break;
        }
    }
}
catch (ClientResultException ex)
{
    Log($"Request failed: {ex.Message}");
    exitCode = 2;
}
finally
{
    await File.WriteAllTextAsync(logPath, logBuilder.ToString());
    await File.WriteAllTextAsync(wireLogPath, wireLogBuilder.ToString());
    Console.WriteLine($"Log written to: {logPath}");
    Console.WriteLine($"Wire log written to: {wireLogPath}");
}

return exitCode;
