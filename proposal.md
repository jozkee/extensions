# [API Proposal]: Reuse hosted code interpreter containers

## Background and motivation

Hosted code execution tools can retain files and process state in provider-managed containers. OpenAI Responses code interpreter supports automatic containers and explicit reuse by passing a `cntr_...` container ID ([docs](https://developers.openai.com/api/docs/guides/tools-code-interpreter#containers)); OpenAI's hosted shell tool uses the same conceptual model via `container_auto` and `container_reference` ([docs](https://developers.openai.com/api/docs/guides/tools-shell#reuse-a-container-across-requests)). Anthropic code execution similarly accepts a request `container.id` and returns the used container on the response ([docs](https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool#containers)).

MEAI currently exposes `HostedCodeInterpreterTool` and code interpreter call/result content, but it does not expose the provider container ID. Users who want to reuse generated files, installed packages, or interpreter state must drop to provider-specific SDK types or inspect `RawRepresentation`, which is not portable and does not work consistently across streaming and non-streaming responses.

Anthropic's programmatic tool-calling flow also makes this important inside a single `FunctionInvokingChatClient` request. Code executing in a hosted container can pause, ask the client to invoke a local tool, and then resume only if the next model request targets the same container. Because `FunctionInvokingChatClient` already passes the prior assistant turns back to the inner client between iterations, adapters can lift the container ID from those messages whenever the caller leaves `HostedCodeInterpreterTool.Container` null - no MEAI middleware opt-in is required.

No related `dotnet/extensions` API proposal was found during issue search.

## API Proposal

```csharp
namespace Microsoft.Extensions.AI;

public abstract partial class ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static ExistingContainerInfo FromExisting(string containerId);

    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static AutomaticContainerInfo Automatic(IList<AIContent>? inputs = null);
}

public sealed partial class ExistingContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public string ContainerId { get; set; }
}

public sealed partial class AutomaticContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public IList<AIContent>? Inputs { get; set; }
}

public partial class HostedCodeInterpreterTool
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public ContainerInfo? Container { get; set; }
}

[Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
public sealed partial class CodeInterpreterToolCallContent
{
    public string? ContainerId { get; set; }
}
```

`HostedCodeInterpreterTool.Container == null` lets the `IChatClient` choose how to provision the container. Adapters whose backend supports container reuse may walk the supplied chat history and reuse the most recent container ID returned by the service in `CodeInterpreterToolCallContent.ContainerId`. `ContainerInfo.FromExisting(id)` requests reuse of a specific hosted container when the provider supports it. `ContainerInfo.Automatic(inputs)` delegates container provisioning to the service and carries optional inputs; the existing stable `HostedCodeInterpreterTool.Inputs` property remains as a compatibility path in the prototype, but new code should prefer `ContainerInfo`. "Automatic" matches OpenAI's `container_auto` and intentionally does not promise a brand-new container: providers may still associate the request with a container tied to the conversation or message history.

Prototype artifacts:

| Adapter | Local artifact |
| --- | --- |
| `dotnet/extensions` | Branch `api-proposal/meai-container-reuse` (not pushed) |
| OpenAI Responses | In-tree prototype in `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` |
| Anthropic | `D:\meai-proposal-scratch\container-reuse\anthropic-sdk-csharp`, branch `meai-proposal/container-reuse`, commit `5018939`, patch `D:\meai-proposal-scratch\container-reuse\anthropic-container-reuse.patch` |
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

Programmatic tool-calling with implicit container reuse via the Anthropic adapter:

```csharp
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// First request: the provider creates or chooses a container.
ChatResponse first = await client.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Use Python to load file.csv. Call get_metadata() if you need metadata."),
    ],
    new ChatOptions
    {
        Tools =
        [
            new HostedCodeInterpreterTool(),
            AIFunctionFactory.Create(GetMetadata, "get_metadata"),
        ],
    });

// Second request: the caller passes the prior assistant turns back in.
// HostedCodeInterpreterTool.Container is null, so the Anthropic adapter walks
// the supplied history in reverse and lifts the most recent
// CodeInterpreterToolCallContent.ContainerId from `first.Messages` automatically.
List<ChatMessage> history = [.. first.Messages, new ChatMessage(ChatRole.User, "Plot a histogram of the loaded data.")];

ChatResponse second = await client.GetResponseAsync(
    history,
    new ChatOptions
    {
        Tools =
        [
            new HostedCodeInterpreterTool(), // null Container => adapter performs implicit lift
        ],
    });
```

Setting `Container = ContainerInfo.FromExisting(id)` overrides the implicit lift; setting `Container = ContainerInfo.Automatic(...)` opts out of the lift so the adapter delegates container selection back to the service.

## Alternative Designs

- Use `AdditionalProperties` or provider raw SDK types only. This keeps the core API smaller, but forces non-portable code and does not give MEAI consumers a consistent response-side place to discover the container ID.
- Add a new generic `HostedContainer` abstraction. This may be useful if MEAI later adds hosted shell or broader container management APIs, but it is unnecessary for the current code interpreter reuse scenario.
- Put `ContainerId` directly on `HostedCodeInterpreterTool`. This is smaller, but it does not leave room for provider-supported configurations such as OpenAI's `container_auto` with initial files or Anthropic's container reuse semantics. A `ContainerInfo` discriminated shape separates "reuse this exact container" (`ExistingContainerInfo`) from "let the service decide" (`AutomaticContainerInfo`) and from "let the adapter pick" (`null`).
- Name the additive variant `CreateNewContainerInfo`. Empirical testing across OpenAI Responses and Anthropic showed that "automatic" mode does not always produce a fresh container - continuity often binds to the conversation or supplied message history rather than to a discrete user-visible ID. `Automatic`/`AutomaticContainerInfo` reflects what the option actually does and matches OpenAI's `container_auto` / `CreateAutomaticContainerConfiguration`.
- Have `AutomaticContainerInfo.Inputs` replace any existing container's contents. OpenAI treats `container_auto.file_ids` as additive: when combined with conversation continuity, those files are added to whichever container the service selects rather than seeding a brand-new one. The naming and docs reflect the additive nature so callers do not assume a clean container.
- Put `ContainerId` on `ChatOptions`. Container reuse is tied to a hosted code execution tool, not to every chat request or every tool kind. A tool property also composes better if a future provider supports multiple hosted tools.
- Add an opt-in `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` that copies the container ID into the next internal request. The earlier prototype tried this but found that providers like Anthropic already maintain container continuity when callers pass back the prior assistant messages - which `FunctionInvokingChatClient` already does. The middleware-level opt-in was redundant for those providers and fragile when combined with provider-specific reuse rules. The proposal instead lets adapters lift the container ID from chat history themselves when the request expresses no explicit container preference.
- Require users to manually propagate container IDs via `ContainerInfo.FromExisting`. Explicit reuse remains supported, but the adapter-side implicit lift removes boilerplate for the common case where the caller already passes prior assistant messages back in.
- Rely on `HostedFileContent.Scope`. That captures generated file scope for OpenAI file outputs, but it does not identify the reusable execution environment or preserve interpreter state.
- Rely on provider automatic reuse through conversation context. This helps within a single provider-specific flow, but it does not let applications persist and explicitly reuse a known container across requests outside that conversation.

## Risks

- Container IDs are provider-specific and ephemeral. OpenAI code interpreter containers expire after 20 minutes of idle time; Anthropic code execution containers expire after 30 days. Applications must handle provider errors by creating a new container.
- "Automatic" does not mean "fresh". OpenAI's `container_auto` and Anthropic's auto-container behavior frequently associate the request with a container bound to the current conversation or supplied message history. Callers who need a guaranteed fresh container must rotate the conversation or otherwise sever continuity at the request level - the API surface does not enforce that.
- `AutomaticContainerInfo.Inputs` is additive on OpenAI: any file IDs are added to whichever container the service selects rather than seeding a clean container. Callers should not rely on `Inputs` to imply isolation.
- Implicit adapter lifting depends on the caller passing the prior assistant turns back in. If a host trims chat history (custom truncation, summarization, dropping tool-call messages), the adapter has nothing to lift and the request behaves as if no prior container existed. Adapters intentionally do not consult anything outside the supplied messages.
- Some providers expose code execution but not reusable container IDs. Those adapters should leave `ContainerId` null and ignore request-side values unless the backend can express them.
- Streaming providers may deliver container metadata separately from code deltas. The prototype preserves the first non-null `ContainerId` during content coalescing.
- `HostedCodeInterpreterTool.Inputs` is already stable, so the prototype keeps it for compatibility and maps it as an implicit `AutomaticContainerInfo` input source when `Container` is null.
- OpenAI Responses remains an experimental OpenAI SDK surface (`OPENAI001`); this proposal adds MEAI experimental surface under the existing code interpreter diagnostic (`MEAI001`).

## Usage in Microsoft.Extensions.AI

### Updated in prototype

| File | Description |
| --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ContainerInfo.cs` | Adds factory methods for automatic and existing-container requests. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ExistingContainerInfo.cs` | Adds request-side existing container ID with whitespace validation. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\AutomaticContainerInfo.cs` | Adds request-side service-managed container with optional additive inputs. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\HostedCodeInterpreterTool.cs` | Adds request-side `Container` property. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolCallContent.cs` | Adds response-side `ContainerId` for code interpreter calls. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolResultContent.cs` | Removes the prototype result-side `ContainerId`; container IDs are call-content-only. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatResponseExtensions.cs` | Coalesces streaming code interpreter call updates without losing `ContainerId`. |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` | Maps `ExistingContainerInfo` to OpenAI explicit container references, maps `AutomaticContainerInfo.Inputs` to OpenAI automatic container file inputs (additive on the OpenAI side), and maps `CodeInterpreterCallResponseItem.ContainerId` back to MEAI call content. |
| `<anthropic-sdk-csharp>\src\Anthropic\Services\Beta\Messages\AnthropicBetaClientExtensions.cs` | Implements adapter-side implicit container lift: when `HostedCodeInterpreterTool.Container` is null, walks the supplied chat history in reverse and reuses the most recent `CodeInterpreterToolCallContent.ContainerId`. `ExistingContainerInfo` keeps priority; explicit `AutomaticContainerInfo` opts out of the lift. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Microsoft.Extensions.AI.Abstractions.json` | Updates the API baseline for the new experimental container info types, call-content `ContainerId`, and `HostedCodeInterpreterTool.Container`. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Tools\HostedCodeInterpreterToolTests.cs` | Covers request-side default, roundtrip, and validation behavior for `ContainerInfo` and the renamed `Automatic` factory. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolCallContentTests.cs` | Covers call-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolResultContentTests.cs` | Covers result-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientTests.cs` | Covers OpenAI explicit-container request serialization plus non-streaming and streaming response mapping. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIConversionTests.cs` | Covers direct `HostedCodeInterpreterTool.AsOpenAIResponseTool()` conversion for explicit container IDs and the renamed `Automatic` factory. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIHostedFileClientIntegrationTests.cs` | Replaces raw OpenAI response inspection with `CodeInterpreterToolCallContent.ContainerId`. |
| `<anthropic-sdk-csharp>\tests\Anthropic.Tests\AnthropicCodeInterpreterContainerLiftTests.cs` | New tests for the Anthropic adapter lift: single-turn no-reuse, multi-turn implicit lift, explicit `FromExisting` override, explicit `Automatic` opt-out, and prior call with null container ID. |

### Candidates or inapplicable sites

| File | Classification | Notes |
| --- | --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIAssistantsChatClient.cs` | Inapplicable | The Assistants adapter has its own code interpreter/thread model and does not expose the OpenAI Responses container reference shape used by this proposal. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientIntegrationTests.cs` | Candidate | Live Responses integration tests can add an explicit container reuse scenario after API review; not required for the local prototype. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIAssistantChatClientIntegrationTests.cs` | Inapplicable | Exercises Assistants code interpreter behavior, not Responses containers. |
| `test\Libraries\Microsoft.Extensions.AI.Tests\ChatCompletion\OpenTelemetryChatClientTests.cs` | Inapplicable | Verifies telemetry around tool lists and does not inspect hosted container state. |

## Prototype validation

- ApiChief baseline and summary generated for `Microsoft.Extensions.AI.Abstractions`; new API surface is limited to the experimental container info types (`ExistingContainerInfo`, `AutomaticContainerInfo`, factories `ContainerInfo.FromExisting` and `ContainerInfo.Automatic`), call-content `ContainerId`, and `HostedCodeInterpreterTool.Container` shown above.
- ApiChief baseline regenerated for `Microsoft.Extensions.AI`; the prototype no longer adds public surface to that assembly (the earlier `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` opt-in was removed in favor of adapter-side implicit lifting).
- `Microsoft.Extensions.AI.Abstractions` built for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.
- `Microsoft.Extensions.AI` built for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.
- `Microsoft.Extensions.AI.OpenAI` built for `net8.0`, `net9.0`, and `net10.0`. The existing `netstandard2.0` targeted build fails in `OpenAIClientExtensions.cs` on `System.Diagnostics.Activity`, unrelated to this prototype.
- Targeted tests passed:
  - `Microsoft.Extensions.AI.Abstractions.Tests` (`HostedCodeInterpreterToolTests|CodeInterpreterToolCallContentTests`): passed.
  - `Microsoft.Extensions.AI.Tests` (`FunctionInvokingChatClientTests` core defaults/roundtrip): passed.
  - `Microsoft.Extensions.AI.OpenAI.Tests` (`OpenAIConversionTests`): passed.
  - Anthropic scratch (`Anthropic.Tests` `AnthropicCodeInterpreterContainerLiftTests`): 5 tests passed (single-turn no-reuse, multi-turn implicit lift, explicit `FromExisting` override, explicit `Automatic` opt-out, prior call without container ID).
- Anthropic scratch prototype built against the local MEAI abstractions for `netstandard2.0`, `net8.0`, and `net9.0`.
- Focused multi-model review completed with `gpt-5.3-codex` and `claude-opus-4.7`. Empirical findings drove the redesign: "automatic" containers are not always fresh because providers tie continuity to the conversation/message history, OpenAI's `container_auto.file_ids` is additive rather than seeding a clean container, and an unconditional middleware-level container lift inside `FunctionInvokingChatClient` was redundant for providers that already maintain continuity through chat history. The proposal now leans on adapter-side implicit lifting, with explicit `ContainerInfo.FromExisting` for hard reuse and explicit `ContainerInfo.Automatic` to opt out of the lift.
