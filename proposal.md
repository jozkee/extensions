# [API Proposal]: Reuse hosted code interpreter containers

## Background and motivation

Hosted code execution tools can retain files and process state in provider-managed containers. OpenAI Responses code interpreter supports automatic containers and explicit reuse by passing a `cntr_...` container ID ([docs](https://developers.openai.com/api/docs/guides/tools-code-interpreter#containers)); OpenAI's hosted shell tool uses the same conceptual model via `container_auto` and `container_reference` ([docs](https://developers.openai.com/api/docs/guides/tools-shell#reuse-a-container-across-requests)). Anthropic code execution similarly accepts a request-level `container.id` and returns the used container on the response ([docs](https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool#containers)).

MEAI currently exposes `HostedCodeInterpreterTool` and code interpreter call/result content, but it does not expose the provider container ID. Users who want to reuse generated files, installed packages, or interpreter state must drop to provider-specific SDK types or inspect `RawRepresentation`, which is not portable and does not work consistently across streaming and non-streaming responses.

Empirical testing also showed that hosted containers are a per-request resource, not a per-tool resource: when both `code_interpreter` and `shell` tools are sent in the same OpenAI Responses request, the API returns HTTP 400 `mutually_exclusive_parameters` for every combination - same container ID, different container IDs, even `shell.environment=local` (script and run log: `D:\meai-stabilization\proposal1-prototype\code-execution-features\multi-tool-containers`). Anthropic models the same constraint by exposing `container` as a top-level property on `MessageCreateParams` shared by `code_execution` and `bash`. The right place for the container in MEAI is therefore `ChatOptions`, not a single tool instance.

Anthropic's programmatic tool-calling flow also makes container continuity important inside a single `FunctionInvokingChatClient` request. Code executing in a hosted container can pause, ask the client to invoke a local tool, and then resume only if the next model request targets the same container. Because `FunctionInvokingChatClient` already passes the prior assistant turns back to the inner client between iterations, adapters can lift the container ID from those messages whenever the caller opts in via `ContainerInfo.Automatic()` - no MEAI middleware opt-in is required.

No related `dotnet/extensions` API proposal was found during issue search.

## API Proposal

```csharp
namespace Microsoft.Extensions.AI;

public abstract partial class ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static ExistingContainerInfo FromExisting(string containerId);

    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static AutomaticContainerInfo Automatic();
}

public sealed partial class ExistingContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public string ContainerId { get; set; }
}

public sealed partial class AutomaticContainerInfo : ContainerInfo
{
}

public partial class ChatOptions
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

`ChatOptions.Container == null` lets the `IChatClient` choose how to provision the container; adapters do not lift anything from history in this mode. `ContainerInfo.FromExisting(id)` requests reuse of a specific hosted container when the provider supports it. `ContainerInfo.Automatic()` is a marker that delegates container provisioning to the service and additionally opts in to adapter-side implicit lifting: if the supplied chat history contains a prior `CodeInterpreterToolCallContent.ContainerId`, the adapter walks the history in reverse and reuses the most recent one. The `Container` property lives on `ChatOptions` so it is shared across all container-aware hosted tools in the request, matching how OpenAI and Anthropic actually validate the field server-side.

The existing stable `HostedCodeInterpreterTool.Inputs` property is unchanged and remains the way to seed `container_auto.file_ids` for OpenAI when the caller wants automatic provisioning with initial files. `CodeInterpreterToolCallContent.ContainerId` is the response-side surface that lets callers discover the container the service used, so they can pass it back in a later request via `ContainerInfo.FromExisting`.

Prototype artifacts:

| Adapter | Local artifact |
| --- | --- |
| `dotnet/extensions` | Branch `api-proposal/meai-container-options` (not pushed) at `D:\extensions-container-options` |
| OpenAI Responses | In-tree prototype in `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` |
| Anthropic | `D:\meai-proposal-scratch\container-reuse\anthropic-sdk-csharp`, branch `meai-proposal/container-reuse`, patch `D:\meai-proposal-scratch\container-reuse\anthropic-container-reuse.patch` |
| Google Vertex AI | `D:\meai-proposal-scratch\container-reuse\google-cloud-dotnet`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |
| Google Gemini / GenAI | `D:\meai-proposal-scratch\container-reuse\dotnet-genai`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |

## API Usage

Explicit container reuse across two requests:

```csharp
using Microsoft.Extensions.AI;

ChatResponse firstResponse = await chatClient.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Create a CSV file named results.csv with three rows.")
    ],
    new ChatOptions
    {
        Tools = [new HostedCodeInterpreterTool()],
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
        Container = ContainerInfo.FromExisting(containerId),
        Tools = [new HostedCodeInterpreterTool()],
    });
```

Programmatic tool-calling with implicit container reuse via the Anthropic adapter:

```csharp
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// First request: opt in to history-based container reuse with Automatic().
ChatResponse first = await client.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Use Python to load file.csv. Call get_metadata() if you need metadata."),
    ],
    new ChatOptions
    {
        Container = ContainerInfo.Automatic(),
        Tools =
        [
            new HostedCodeInterpreterTool(),
            AIFunctionFactory.Create(GetMetadata, "get_metadata"),
        ],
    });

// Second request: the caller passes the prior assistant turns back in.
// Container == Automatic() opts in to lifting; the Anthropic adapter walks the
// supplied history in reverse and lifts the most recent
// CodeInterpreterToolCallContent.ContainerId from `first.Messages`.
List<ChatMessage> history = [.. first.Messages, new ChatMessage(ChatRole.User, "Plot a histogram of the loaded data.")];

ChatResponse second = await client.GetResponseAsync(
    history,
    new ChatOptions
    {
        Container = ContainerInfo.Automatic(), // opts in to implicit lift
        Tools = [new HostedCodeInterpreterTool()],
    });
```

`Container = ContainerInfo.FromExisting(id)` overrides the implicit lift. `Container = null` disables both explicit reuse and the lift, so the adapter sends no container hint and the provider applies its defaults.

## Alternative Designs

- Per-tool container slot on `HostedCodeInterpreterTool`. The original prototype put `Container` on the hosted tool. Empirical testing showed OpenAI Responses rejects every combination of `code_interpreter` + `shell` in the same request with HTTP 400 `mutually_exclusive_parameters` - same container ID, different container IDs, and even `shell.environment=local`. The container is a per-request resource, not a per-tool resource, so a per-tool API surface would let callers express something the provider cannot honor. Anthropic models the same constraint by exposing `container` at the top of `MessageCreateParams`, shared by `code_execution` and `bash`.
- Use `AdditionalProperties` or provider raw SDK types only. This keeps the core API smaller, but forces non-portable code and does not give MEAI consumers a consistent response-side place to discover the container ID.
- Add a new generic `HostedContainer` abstraction. This may be useful if MEAI later adds richer hosted container management APIs, but it is unnecessary for the current code interpreter reuse scenario.
- Put `ContainerId` directly on `ChatOptions` as a string. This is smaller, but it does not leave room for provider-supported configurations such as opting in to history-based reuse via `Automatic()`. A `ContainerInfo` discriminated shape separates "reuse this exact container" (`ExistingContainerInfo`) from "let the service decide and optionally lift from history" (`AutomaticContainerInfo`) and from "no container hint" (`null`).
- Name the additive variant `CreateNewContainerInfo`. Empirical testing across OpenAI Responses and Anthropic showed that "automatic" mode does not always produce a fresh container - continuity often binds to the conversation or supplied message history rather than to a discrete user-visible ID. `Automatic`/`AutomaticContainerInfo` reflects what the option actually does and matches OpenAI's `container_auto` / `CreateAutomaticContainerConfiguration`.
- Carry initial inputs on `AutomaticContainerInfo`. The earlier prototype had `AutomaticContainerInfo.Inputs`. OpenAI treats `container_auto.file_ids` as additive: combined with conversation continuity, files are added to whichever container the service selects rather than seeding a brand-new one. Carrying inputs on the container info also conflicts with the new `ChatOptions.Container` location, since file inputs are conceptually a tool-level concern. The proposal drops the property and routes file inputs through the existing stable `HostedCodeInterpreterTool.Inputs`.
- Add an opt-in `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` that copies the container ID into the next internal request. The earlier prototype tried this but found that providers like Anthropic already maintain container continuity when callers pass back the prior assistant messages - which `FunctionInvokingChatClient` already does. The middleware-level opt-in was redundant for those providers and fragile when combined with provider-specific reuse rules. The proposal instead lets adapters lift the container ID from chat history themselves when `Container = Automatic()`.
- Lift implicitly when `Container == null`. The earlier prototype used `null` as the lift trigger, but that conflated "no container preference" with "please reuse from history" and surprised callers who passed history for context but did not want continuity. The proposal switches the lift trigger to the explicit `AutomaticContainerInfo` opt-in.
- Rely on `HostedFileContent.Scope`. That captures generated file scope for OpenAI file outputs, but it does not identify the reusable execution environment or preserve interpreter state.
- Rely on provider automatic reuse through conversation context. This helps within a single provider-specific flow, but it does not let applications persist and explicitly reuse a known container across requests outside that conversation.

## Risks

- Container IDs are provider-specific and ephemeral. OpenAI code interpreter containers expire after 20 minutes of idle time; Anthropic code execution containers expire after 30 days. Applications must handle provider errors by creating a new container.
- "Automatic" does not mean "fresh". OpenAI's `container_auto` and Anthropic's auto-container behavior frequently associate the request with a container bound to the current conversation or supplied message history. Callers who need a guaranteed fresh container must rotate the conversation or otherwise sever continuity at the request level - the API surface does not enforce that.
- OpenAI's `container_auto.file_ids` (sourced from `HostedCodeInterpreterTool.Inputs`) is additive: file IDs are added to whichever container the service selects rather than seeding a clean container. Callers should not rely on `Inputs` to imply isolation.
- Implicit adapter lifting depends on the caller passing the prior assistant turns back in. If a host trims chat history (custom truncation, summarization, dropping tool-call messages), the adapter has nothing to lift and the request behaves as if no prior container existed. Adapters intentionally do not consult anything outside the supplied messages.
- The container is a per-request, single-slot resource on every provider validated so far. Callers who set `Container` and request multiple container-aware hosted tools in the same request (for example, `code_interpreter` + a future `shell` tool) will hit provider-side rejection rather than getting two independent containers. This is the main reason the property lives on `ChatOptions` rather than per tool.
- Some providers expose code execution but not reusable container IDs. Those adapters should leave `ContainerId` null on the response and ignore request-side `Container` values unless the backend can express them.
- Streaming providers may deliver container metadata separately from code deltas. The prototype preserves the first non-null `ContainerId` observed during streaming for code interpreter call updates.
- `HostedCodeInterpreterTool.Inputs` is already stable, so the prototype keeps it as the single way to pass `container_auto.file_ids` to OpenAI. Adapters route those file IDs whenever `ChatOptions.Container` is null or `AutomaticContainerInfo`.
- OpenAI Responses remains an experimental OpenAI SDK surface (`OPENAI001`); this proposal adds MEAI experimental surface under the existing code interpreter diagnostic (`MEAI001`).

## Usage in Microsoft.Extensions.AI

### Updated in prototype

| File | Description |
| --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ContainerInfo.cs` | Adds factory methods for automatic and existing-container requests; `Automatic()` is parameterless. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ExistingContainerInfo.cs` | Adds request-side existing container ID with whitespace validation. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\AutomaticContainerInfo.cs` | Marker subclass; opts in to adapter-side implicit container lifting when used with `ChatOptions.Container`. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatOptions.cs` | Adds the experimental `Container` property and clones it in the protected copy ctor. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\HostedCodeInterpreterTool.cs` | Updated remarks to point at `ChatOptions.Container`; the stable `Inputs` property is unchanged. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolCallContent.cs` | Adds response-side `ContainerId` for code interpreter calls. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolResultContent.cs` | Removes the prototype result-side `ContainerId`; container IDs are call-content-only. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatResponseExtensions.cs` | Coalesces streaming code interpreter call updates without losing `ContainerId`. |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` | Maps `ChatOptions.Container is ExistingContainerInfo` to OpenAI explicit container references; falls back to `HostedCodeInterpreterTool.Inputs` for `container_auto.file_ids`; captures `CodeInterpreterCallResponseItem.ContainerId` on streaming `output_item.added` and threads it through subsequent code-delta updates. |
| `<anthropic-sdk-csharp>\src\Anthropic\Services\Beta\Messages\AnthropicBetaClientExtensions.cs` | Resolves the Anthropic top-level container from `ChatOptions.Container`: `ExistingContainerInfo` maps to its ID, `AutomaticContainerInfo` triggers a reverse walk of the supplied chat history to lift the most recent `CodeInterpreterToolCallContent.ContainerId`, and `null` sends no container hint. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Microsoft.Extensions.AI.Abstractions.json` | Updates the API baseline for the new experimental container info types, call-content `ContainerId`, and `ChatOptions.Container`. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Tools\HostedCodeInterpreterToolTests.cs` | Covers default state, `Inputs` roundtrip, and `ContainerInfo.FromExisting` / `Automatic` factories. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\ChatCompletion\ChatOptionsTests.cs` | Covers `ChatOptions.Container` default, roundtrip, and clone behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolCallContentTests.cs` | Covers call-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolResultContentTests.cs` | Covers result-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientTests.cs` | Covers OpenAI explicit-container request serialization (`ChatOptions.Container`) plus non-streaming and streaming response mapping. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIConversionTests.cs` | Covers `HostedCodeInterpreterTool.AsOpenAIResponseTool()` for `Inputs`-backed automatic container configuration. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIHostedFileClientIntegrationTests.cs` | Replaces raw OpenAI response inspection with `CodeInterpreterToolCallContent.ContainerId`. |
| `<anthropic-sdk-csharp>\src\Anthropic.Tests\AnthropicClientBetaExtensionsTests.cs` | Tests for the Anthropic adapter container resolution: single-turn no-reuse, multi-turn implicit lift on `Automatic()`, explicit `FromExisting` override, and prior call without container ID. |

### Candidates or inapplicable sites

| File | Classification | Notes |
| --- | --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIAssistantsChatClient.cs` | Inapplicable | The Assistants adapter has its own code interpreter/thread model and does not expose the OpenAI Responses container reference shape used by this proposal. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientIntegrationTests.cs` | Candidate | Live Responses integration tests can add an explicit container reuse scenario after API review; not required for the local prototype. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIAssistantChatClientIntegrationTests.cs` | Inapplicable | Exercises Assistants code interpreter behavior, not Responses containers. |
| `test\Libraries\Microsoft.Extensions.AI.Tests\ChatCompletion\OpenTelemetryChatClientTests.cs` | Inapplicable | Verifies telemetry around tool lists and does not inspect hosted container state. |

## Prototype validation

- ApiChief baseline regenerated for `Microsoft.Extensions.AI.Abstractions`. The diff against the previous prototype drops `HostedCodeInterpreterTool.Container` and `AutomaticContainerInfo.Inputs`, makes `ContainerInfo.Automatic()` parameterless, and adds `ChatOptions.Container`.
- ApiChief baseline for `Microsoft.Extensions.AI` is unchanged; the prototype no longer adds public surface to that assembly.
- `Microsoft.Extensions.AI.Abstractions` and `Microsoft.Extensions.AI.OpenAI` built for `net10.0` (`--no-dependencies`).
- Targeted tests passed:
  - `Microsoft.Extensions.AI.Abstractions.Tests` (`HostedCodeInterpreterToolTests` + `ChatOptionsTests`): 15 passed.
  - `Microsoft.Extensions.AI.OpenAI.Tests` (`!~IntegrationTests`): 357 passed.
  - Anthropic scratch (`Anthropic.Tests` `AnthropicClientBetaExtensionsTests`): 216 passed on `net8.0`, 216 on `net472`.
- Multi-tool container experiment recorded at `D:\meai-stabilization\proposal1-prototype\code-execution-features\multi-tool-containers` shows OpenAI rejects every `code_interpreter` + `shell` combination with HTTP 400 `mutually_exclusive_parameters`, motivating the move from a per-tool `Container` to `ChatOptions.Container`.
- Focused multi-model review completed with `gpt-5.3-codex` and `claude-opus-4.7`. Empirical findings drove the redesign: "automatic" containers are not always fresh because providers tie continuity to the conversation/message history; `container_auto.file_ids` is additive rather than seeding a clean container; the container is a per-request, single-slot resource validated server-side; and an unconditional middleware-level container lift inside `FunctionInvokingChatClient` was redundant for providers that already maintain continuity through chat history. The proposal therefore promotes the container to `ChatOptions`, drops `AutomaticContainerInfo.Inputs`, and uses adapter-side implicit lifting triggered by `ContainerInfo.Automatic()`.
