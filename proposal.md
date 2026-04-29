# [API Proposal]: Reuse hosted code interpreter containers

## Background and motivation

Hosted code execution tools can retain files and process state in provider-managed containers. OpenAI Responses code interpreter supports automatic containers and explicit reuse by passing a `cntr_...` container ID ([docs](https://developers.openai.com/api/docs/guides/tools-code-interpreter#containers)); OpenAI's hosted shell tool uses the same conceptual model via `container_auto` and `container_reference` ([docs](https://developers.openai.com/api/docs/guides/tools-shell#reuse-a-container-across-requests)). Anthropic code execution similarly accepts a request `container.id` and returns the used container on the response ([docs](https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool#containers)).

MEAI currently exposes `HostedCodeInterpreterTool` and code interpreter call/result content, but it does not expose the provider container ID. Users who want to reuse generated files, installed packages, or interpreter state must drop to provider-specific SDK types or inspect `RawRepresentation`, which is not portable and does not work consistently across streaming and non-streaming responses.

Anthropic's programmatic tool-calling flow also makes this important inside a single `FunctionInvokingChatClient` request. Code executing in a hosted container can pause, ask the client to invoke a local tool, and then resume only if the next model request targets the same container. Because those follow-up requests are internal to `FunctionInvokingChatClient`, applications need an opt-in middleware behavior rather than only a manual extract-and-pass-back pattern.

No related `dotnet/extensions` API proposal was found during issue search.

## API Proposal

```csharp
namespace Microsoft.Extensions.AI;

public abstract partial class ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static ExistingContainerInfo FromExisting(string containerId);

    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static CreateNewContainerInfo CreateNew(IList<AIContent>? inputs = null);
}

public sealed partial class ExistingContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public string ContainerId { get; set; }
}

public sealed partial class CreateNewContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public IList<AIContent>? Inputs { get; set; }
}

public partial class HostedCodeInterpreterTool
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public virtual HostedCodeInterpreterTool Clone();

    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public ContainerInfo? Container { get; set; }
}

[Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
public sealed partial class CodeInterpreterToolCallContent
{
    public string? ContainerId { get; set; }
}

public partial class FunctionInvokingChatClient
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public bool EnableCodeInterpreterContainerReuse { get; set; }
}
```

`HostedCodeInterpreterTool.Container == null` preserves the provider's default container behavior. `ContainerInfo.FromExisting(id)` requests reuse of an existing hosted container when the provider supports it. `ContainerInfo.CreateNew(inputs)` requests a new container and carries the initial inputs currently represented by `HostedCodeInterpreterTool.Inputs`; the existing stable `Inputs` property remains as a compatibility path in the prototype, but new code should prefer `ContainerInfo`. `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` defaults to `false`; when enabled, the middleware copies the most recent non-null container ID from `CodeInterpreterToolCallContent` into existing `HostedCodeInterpreterTool` entries in `ChatOptions.Tools` before the next internal function-invocation iteration. `HostedCodeInterpreterTool.Clone()` gives the middleware a way to update the request tool without mutating the caller's original instance or losing derived tool state.

Prototype artifacts:

| Adapter | Local artifact |
| --- | --- |
| `dotnet/extensions` | Branch `api-proposal/meai-container-reuse` (not pushed) |
| OpenAI Responses | In-tree prototype in `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` |
| Anthropic | `D:\meai-proposal-scratch\container-reuse\anthropic-sdk-csharp`, branch `meai-proposal/container-reuse`, commit `96319aa`, patch `D:\meai-proposal-scratch\container-reuse\anthropic-container-reuse.patch` |
| Google Vertex AI | `D:\meai-proposal-scratch\container-reuse\google-cloud-dotnet`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |
| Google Gemini / GenAI | `D:\meai-proposal-scratch\container-reuse\dotnet-genai`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |

## API Usage

```csharp
using Microsoft.Extensions.AI;

ChatResponse firstResponse = await chatClient.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Create a CSV file named results.csv with three rows.")
    ],
    new ChatOptions
    {
        Tools = [new HostedCodeInterpreterTool()]
    });

string? containerId =
    firstResponse.Messages
        .SelectMany(m => m.Contents)
        .OfType<CodeInterpreterToolCallContent>()
        .FirstOrDefault(c => c.ContainerId is not null)
        ?.ContainerId;

if (containerId is null)
{
    throw new InvalidOperationException("The provider did not return a reusable container ID.");
}

ChatResponse secondResponse = await chatClient.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Read results.csv and calculate the row count.")
    ],
    new ChatOptions
    {
        Tools =
        [
            new HostedCodeInterpreterTool
            {
                Container = ContainerInfo.FromExisting(containerId),
            }
        ]
    });
```

Programmatic tool-calling with automatic propagation inside `FunctionInvokingChatClient`:

```csharp
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseFunctionInvocation(configure: f => f.EnableCodeInterpreterContainerReuse = true)
    .Build();

ChatResponse response = await client.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Use Python to analyze the file. Call get_metadata() if you need metadata.")
    ],
    new ChatOptions
    {
        Tools =
        [
            new HostedCodeInterpreterTool(),
            AIFunctionFactory.Create(GetMetadata, "get_metadata"),
        ]
    });
```

The first internal request lets the provider create or choose a hosted container. If the response includes both a code interpreter call with `ContainerId` and a function call, the next internal request reuses that container after the local function result is added to the chat history.

## Alternative Designs

- Use `AdditionalProperties` or provider raw SDK types only. This keeps the core API smaller, but forces non-portable code and does not give MEAI consumers a consistent response-side place to discover the container ID.
- Add a new generic `HostedContainer` abstraction. This may be useful if MEAI later adds hosted shell or broader container management APIs, but it is unnecessary for the current code interpreter reuse scenario.
- Put `ContainerId` directly on `HostedCodeInterpreterTool`. This is smaller, but it does not leave room for provider-supported creation options such as OpenAI automatic containers with initial files. A `ContainerInfo` discriminated shape separates "create new" from "reuse existing".
- Put `ContainerId` on `ChatOptions`. Container reuse is tied to a hosted code execution tool, not to every chat request or every tool kind. A tool property also composes better if a future provider supports multiple hosted tools.
- Put automatic function-invocation propagation on `ChatOptions`. The behavior is specific to `FunctionInvokingChatClient`'s internal multi-iteration loop, so a middleware property is more discoverable and avoids implying provider-wide behavior.
- Make `FunctionInvokingChatClient` propagation default-on. This could surprise applications by preserving provider-hosted execution state, files, or installed packages across internal requests. The proposal keeps the behavior opt-in.
- Require users to manually propagate container IDs during programmatic tool calling. That is not practical for the follow-up requests generated inside `FunctionInvokingChatClient`; users only see the final response unless the loop terminates.
- Rely on `HostedFileContent.Scope`. That captures generated file scope for OpenAI file outputs, but it does not identify the reusable execution environment or preserve interpreter state.
- Rely on provider automatic reuse through conversation context. This helps within a single provider-specific flow, but it does not let applications persist and explicitly reuse a known container across requests.

## Risks

- Container IDs are provider-specific and ephemeral. OpenAI code interpreter containers expire after 20 minutes of idle time; Anthropic code execution containers expire after 30 days. Applications must handle provider errors by creating a new container.
- Some providers expose code execution but not reusable container IDs. Those adapters should leave `ContainerId` null and ignore request-side values unless the backend can express them.
- Streaming providers may deliver container metadata separately from code deltas. The prototype preserves the first non-null `ContainerId` during content coalescing.
- The `FunctionInvokingChatClient` opt-in only updates `HostedCodeInterpreterTool` instances already present in `ChatOptions.Tools`; it intentionally does not add a hosted tool or persist container IDs across separate user calls.
- `HostedCodeInterpreterTool.Inputs` is already stable, so the prototype keeps it for compatibility and maps it as an implicit `CreateNewContainerInfo` input source when `Container` is null.
- OpenAI Responses remains an experimental OpenAI SDK surface (`OPENAI001`); this proposal adds MEAI experimental surface under the existing code interpreter diagnostic (`MEAI001`).

## Usage in Microsoft.Extensions.AI

### Updated in prototype

| File | Description |
| --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ContainerInfo.cs` | Adds factory methods for create-new and existing-container requests. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ExistingContainerInfo.cs` | Adds request-side existing container ID with whitespace validation. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\CreateNewContainerInfo.cs` | Adds request-side new container inputs. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\HostedCodeInterpreterTool.cs` | Adds request-side `Container` and a shallow `Clone()` helper for safe middleware updates. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolCallContent.cs` | Adds response-side `ContainerId` for code interpreter calls. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolResultContent.cs` | Removes the prototype result-side `ContainerId`; container IDs are call-content-only. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatResponseExtensions.cs` | Coalesces streaming code interpreter call updates without losing `ContainerId`. |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` | Maps `ExistingContainerInfo` to OpenAI explicit container references, maps `CreateNewContainerInfo.Inputs` to OpenAI automatic container file inputs, and maps `CodeInterpreterCallResponseItem.ContainerId` back to MEAI call content. |
| `src\Libraries\Microsoft.Extensions.AI\ChatCompletion\FunctionInvokingChatClient.cs` | Adds opt-in propagation of hosted code interpreter container IDs across internal function-invocation iterations. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Microsoft.Extensions.AI.Abstractions.json` | Updates the API baseline for the new experimental container info types, call-content `ContainerId`, `HostedCodeInterpreterTool.Container`, and `HostedCodeInterpreterTool.Clone()`. |
| `src\Libraries\Microsoft.Extensions.AI\Microsoft.Extensions.AI.json` | Updates the API baseline for the new experimental `FunctionInvokingChatClient` property. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Tools\HostedCodeInterpreterToolTests.cs` | Covers request-side default, roundtrip, validation, and clone behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolCallContentTests.cs` | Covers call-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolResultContentTests.cs` | Covers result-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientTests.cs` | Covers OpenAI explicit-container request serialization plus non-streaming and streaming response mapping. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIConversionTests.cs` | Covers direct `HostedCodeInterpreterTool.AsOpenAIResponseTool()` conversion for explicit container IDs. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIHostedFileClientIntegrationTests.cs` | Replaces raw OpenAI response inspection with `CodeInterpreterToolCallContent.ContainerId`. |
| `test\Libraries\Microsoft.Extensions.AI.Tests\ChatCompletion\FunctionInvokingChatClientTests.cs` | Covers the opt-in default, roundtrip behavior, streaming and non-streaming propagation, input preservation, and caller tool immutability. |

### Candidates or inapplicable sites

| File | Classification | Notes |
| --- | --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIAssistantsChatClient.cs` | Inapplicable | The Assistants adapter has its own code interpreter/thread model and does not expose the OpenAI Responses container reference shape used by this proposal. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientIntegrationTests.cs` | Candidate | Live Responses integration tests can add an explicit container reuse scenario after API review; not required for the local prototype. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIAssistantChatClientIntegrationTests.cs` | Inapplicable | Exercises Assistants code interpreter behavior, not Responses containers. |
| `test\Libraries\Microsoft.Extensions.AI.Tests\ChatCompletion\OpenTelemetryChatClientTests.cs` | Inapplicable | Verifies telemetry around tool lists and does not inspect hosted container state. |

## Prototype validation

- ApiChief baseline and summary generated for `Microsoft.Extensions.AI.Abstractions`; new API surface is limited to the experimental container info types, call-content `ContainerId`, `HostedCodeInterpreterTool.Container`, and `HostedCodeInterpreterTool.Clone()` shown above.
- ApiChief baseline and summary generated for `Microsoft.Extensions.AI`; new API surface is limited to the experimental `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` property shown above.
- `Microsoft.Extensions.AI.Abstractions` built for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.
- `Microsoft.Extensions.AI` built for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.
- `Microsoft.Extensions.AI.OpenAI` built for `net8.0`, `net9.0`, and `net10.0`. The existing `netstandard2.0` targeted build fails in `OpenAIClientExtensions.cs` on `System.Diagnostics.Activity`, unrelated to this prototype.
- Targeted tests passed:
  - `Microsoft.Extensions.AI.Abstractions.Tests` (`HostedCodeInterpreterToolTests`): passed.
  - `Microsoft.Extensions.AI.Tests` (`FunctionInvokingChatClientTests`): passed.
  - `Microsoft.Extensions.AI.OpenAI.Tests` (`OpenAIConversionTests|OpenAIResponseClientTests`): 203 passed.
- Anthropic scratch prototype built against the local MEAI abstractions.
- Focused multi-model review completed with `gpt-5.3-codex` and `claude-opus-4.7`; a subtype-preservation issue in the `FunctionInvokingChatClient` propagation helper was fixed by adding `HostedCodeInterpreterTool.Clone()`.
