# Proposal: add ToolCallContent and ToolResultContent as a base for all call/result contents.
As an alternative to https://github.com/dotnet/extensions/pull/7245.


## What is solving: 
Have a Tool hierarchy that doesn't step into each other and is sustainable.
Don't corner Function contents, having Mcp contents extend Function contents will put function- like server calls in the same bucket.  This may end up being awkward and corner Function contents if it needs to add more local-specific members.
Future proofing for potential paths.

## Cost:
More time invested as review and discussions, the design has two controversial parts *needing* discussion.

## Proposal

https://spa.apiview.dev/review/e533c3e1e8314fe0a4f442459c48e80e?activeApiRevisionId=d2cf62c399bb4ee0a699ae3303004b25&diffApiRevisionId=8c4622ad64d74818a33bf52feae77dc8&diffStyle=trees

### Issue 1: How to constraint ToolApprovalRequestContent to Mcp calls and function calls.
The challenge is that we need the json polymorphism attributes in all these types to support roundtrippable serialization, this constrains ToolApprovalRequestContent to provide one constructor for deserialization, so we need to make compromises in the design.

Two approaches were ruled out: using two concrete ctors with custom converters doesn't work because System.Text.Json doesn't support custom converters with polymorphism; and using `[JsonConstructor] internal ctor()` doesn't work because external assemblies can't access internal constructors, preventing roundtripping.

Here's a table of the viable approaches and how they satisfy the constraints:

| # | Approach | Polymorphism | Type-safe ctors | Source Gen | External roundtrip | Future groupings |
|---|----------|:---:|:---:|:---:|:---:|:---:|
| 1 | `[JsonConstructor] public ctor(string, ToolCallContent)` + EB.Never | ✅ | ⚠️ Runtime only | ✅ | ✅ | ✅ |
| 2 | Contract customization (CreateObject via reflection + WithAddedModifier) | ✅ | ✅ | ⚠️ Hybrid | ⚠️ Consumer must call WithAddedModifier | ✅ |
| 3 | Abstract `ApprovableToolCallContent` + single ctor | ✅ | ✅ | ✅ | ✅ | ❌ Single grouping only |
| 4 | Marker interface + single ctor (FDG: AVOID) | ✅ | ⚠️ Needs runtime check | ✅ | ✅ | ✅ |

**My recommendation is #1**, provide 3 ctors:
```cs
public ToolApprovalRequestContent(string requestId, FunctionCallContent functionCall);
public ToolApprovalRequestContent(string requestId, McpServerToolCallContent mcpServerToolCall);
[JsonConstructor]
[EditorBrowsable(EditorBrowsableState.Never)]
public ToolApprovalRequestContent(string requestId, ToolCallContent toolCall)
{
    if (toolCall is not FunctionCallContent and not McpServerToolCallContent)
    {
        Throw.ArgumentException(nameof(toolCall), $"Unsupported type '{toolCall.GetType().Name}'.");
    }    
} 
```

Alternatively, I liked how using marker interfaces worked for this case since a tool like MCPSTCC can be IApprovableToolCall and IRemoteToolCall. FDG says, AVOID using them.

### Issue 2: There's a mishmash of get-only/nullability ids for call contents.  
I propose flipping the nullable settable to non-nullable get-only.

Both Claude and OpenAI return an ID, I presumably non-empty empty.

https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool#streaming
https://developers.openai.com/api/reference/resources/responses/methods/create - need to scroll down to "Returns".

For Image generation as tool, values passed are never null.
Here we use FCC.CallId which is never null: 
https://github.com/dotnet/extensions/blob/9974fbf7a3fede68d7e5f22b9b249aebd819a26d/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ImageGeneratingChatClient.cs#L336-L354

For Responses, openai-dotnet is not nullable aware, but if we go with the responses documentation I linked above, it mentions that an id is returned.
https://github.com/dotnet/extensions/blob/9974fbf7a3fede68d7e5f22b9b249aebd819a26d/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L415-L419
https://github.com/dotnet/extensions/blob/9974fbf7a3fede68d7e5f22b9b249aebd819a26d/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L1415-L1425

NOTE: Gemini IImageGenerator does not return ids but that's used in conjunction with the ImageGenerationChatClient which expose it as function to a model.
https://github.com/googleapis/dotnet-genai/blob/0af5cf527989fb4ae6bc782afae51fc7f7fc3ea8/Google.GenAI/GoogleGenAIImageGenerator.cs#L56-L94
https://docs.cloud.google.com/vertex-ai/generative-ai/docs/model-reference/imagen-api


## Tree view

```plaintext
AIContent
├── ToolCallContent                    
│   ├── FunctionCallContent            
│   ├── McpServerToolCallContent       
│   ├── CodeInterpreterToolCallContent 
│   └── ImageGenerationToolCallContent 
├── ToolResultContent                  
│   ├── FunctionResultContent           
│   ├── McpServerToolResultContent     
│   ├── CodeInterpreterToolResultContent 
│   └── ImageGenerationToolResultContent 
├── InputRequestContent
│   └── ToolApprovalRequestContent     
│       ctors: (requestId, FunctionCallContent)
│              (requestId, McpServerToolCallContent)
│              (requestId, ToolCallContent)
└── InputResponseContent
    └── ToolApprovalResponseContent    
        ctors: (requestId, approved, FunctionCallContent)
               (requestId, approved, McpServerToolCallContent)
               (requestId, approved, ToolCallContent)

Future non-breaking changes:

ToolCallContent                        
├── FunctionCallContent                
│   └── McpServerToolCallContent       ← can later move under FCC (compilation-compatible, gains IS-A FCC)
├── RemoteToolCallContent              ← NEW, non-breaking
│   ├── McpServerToolCallContent       ← OR move here instead
│   ├── CodeInterpreterToolCallContent
│   └── ImageGenerationToolCallContent
```

## diff

### Core hierarchy changes

```diff
  namespace Microsoft.Extensions.AI { 
      [JsonPolymorphic] 
      [JsonDerivedType(typeof(DataContent), "data")] 
      [JsonDerivedType(typeof(ErrorContent), "error")] 
      [JsonDerivedType(typeof(FunctionCallContent), "functionCall")] 
      [JsonDerivedType(typeof(FunctionResultContent), "functionResult")] 
      [JsonDerivedType(typeof(HostedFileContent), "hostedFile")] 
      [JsonDerivedType(typeof(HostedVectorStoreContent), "hostedVectorStore")] 
      [JsonDerivedType(typeof(TextContent), "text")] 
      [JsonDerivedType(typeof(TextReasoningContent), "reasoning")] 
      [JsonDerivedType(typeof(UriContent), "uri")] 
      [JsonDerivedType(typeof(UsageContent), "usage")] 
+     [JsonDerivedType(typeof(ToolApprovalRequestContent), "toolApprovalRequest")] 
+     [JsonDerivedType(typeof(ToolApprovalResponseContent), "toolApprovalResponse")] 
+     [JsonDerivedType(typeof(McpServerToolCallContent), "mcpServerToolCall")] 
+     [JsonDerivedType(typeof(McpServerToolResultContent), "mcpServerToolResult")] 
      public class AIContent { ... } 
      
+     // ── New base classes ──
+
+     [JsonDerivedType(typeof(FunctionCallContent), "functionCall")] 
+     [JsonDerivedType(typeof(McpServerToolCallContent), "mcpServerToolCall")] 
+     public class ToolCallContent : AIContent { 
+         internal ToolCallContent(string callId);
+         public string CallId { get; }
+     } 
+
+     [JsonDerivedType(typeof(FunctionResultContent), "functionResult")] 
+     [JsonDerivedType(typeof(McpServerToolResultContent), "mcpServerToolResult")] 
+     public class ToolResultContent : AIContent { 
+         internal ToolResultContent(string callId);
+         public string CallId { get; }
+     } 
+
+     // ── Rebased types ──
      
-     public class FunctionCallContent : AIContent { 
+     public class FunctionCallContent : ToolCallContent { 
          public FunctionCallContent(string callId, string name, IDictionary<string, object?>? arguments = null); 
          public IDictionary<string, object?>? Arguments { get; set; }
-         public string CallId { get; }
          public Exception? Exception { get; set; }
          public bool InformationalOnly { get; set; }
          public string Name { get; }
      } 
      
-     public class FunctionResultContent : AIContent { 
+     public class FunctionResultContent : ToolResultContent { 
          public FunctionResultContent(string callId, object? result); 
-         public string CallId { get; }
          public Exception? Exception { get; set; }
          public object? Result { get; set; }
      } 
      
-     public sealed class McpServerToolCallContent : AIContent { 
+     public sealed class McpServerToolCallContent : ToolCallContent { 
-         public McpServerToolCallContent(string callId, string toolName, string? serverName); 
+         public McpServerToolCallContent(string callId, string name, string? serverName); 
-         public IReadOnlyDictionary<string, object?>? Arguments { get; set; }
+         public IDictionary<string, object?>? Arguments { get; set; }
-         public string CallId { get; }
+         public string Name { get; }
          public string? ServerName { get; }
-         public string ToolName { get; }
      } 
      
-     public sealed class McpServerToolResultContent : AIContent { 
+     public sealed class McpServerToolResultContent : ToolResultContent { 
          public McpServerToolResultContent(string callId); 
-         public string CallId { get; }
-         public IList<AIContent>? Output { get; set; }
+         public IList<AIContent>? Outputs { get; set; }
      } 
      
      [Experimental] 
-     public sealed class CodeInterpreterToolCallContent : AIContent { 
+     public sealed class CodeInterpreterToolCallContent : ToolCallContent { 
+         public CodeInterpreterToolCallContent(string callId); 
-         public CodeInterpreterToolCallContent(); 
-         public string? CallId { get; set; }
          public IList<AIContent>? Inputs { get; set; }
      } 
      
      [Experimental] 
-     public sealed class CodeInterpreterToolResultContent : AIContent { 
+     public sealed class CodeInterpreterToolResultContent : ToolResultContent { 
+         public CodeInterpreterToolResultContent(string callId); 
-         public CodeInterpreterToolResultContent(); 
-         public string? CallId { get; set; }
          public IList<AIContent>? Outputs { get; set; }
      } 
      
      [Experimental] 
-     public sealed class ImageGenerationToolCallContent : AIContent { 
+     public sealed class ImageGenerationToolCallContent : ToolCallContent { 
+         public ImageGenerationToolCallContent(string callId); 
-         public ImageGenerationToolCallContent(); 
-         public string? ImageId { get; set; }
      } 
      
      [Experimental] 
-     public sealed class ImageGenerationToolResultContent : AIContent { 
+     public sealed class ImageGenerationToolResultContent : ToolResultContent { 
+         public ImageGenerationToolResultContent(string callId); 
-         public ImageGenerationToolResultContent(); 
-         public string? ImageId { get; set; }
          public IList<AIContent>? Outputs { get; set; }
      } 
      
+     // ── Unified approval types (replace removed types below) ──
+
+     public class InputRequestContent : AIContent { 
+         protected InputRequestContent(string requestId); 
+         public string RequestId { get; }
+     } 
+
+     public class InputResponseContent : AIContent { 
+         protected InputResponseContent(string requestId); 
+         public string RequestId { get; }
+     } 
+
+     public sealed class ToolApprovalRequestContent : InputRequestContent { 
+         public ToolApprovalRequestContent(string requestId, FunctionCallContent functionCall); 
+         public ToolApprovalRequestContent(string requestId, McpServerToolCallContent mcpServerToolCall); 
+         [JsonConstructor] 
+         [EditorBrowsable(EditorBrowsableState.Never)] 
+         public ToolApprovalRequestContent(string requestId, ToolCallContent toolCall); 
+         public ToolCallContent ToolCall { get; }
+         public ToolApprovalResponseContent CreateResponse(bool approved, string? reason = null); 
+     } 
+
+     public sealed class ToolApprovalResponseContent : InputResponseContent { 
+         public ToolApprovalResponseContent(string requestId, bool approved, FunctionCallContent functionCall); 
+         public ToolApprovalResponseContent(string requestId, bool approved, McpServerToolCallContent mcpServerToolCall); 
+         [JsonConstructor]
+         [EditorBrowsable(EditorBrowsableState.Never)] 
+         public ToolApprovalResponseContent(string requestId, bool approved, ToolCallContent toolCall); 
+         public bool Approved { get; }
+         public string? Reason { get; set; }
+         public ToolCallContent ToolCall { get; }
+     } 
+
+     // ── Removed types ──

-     [Experimental] 
-     public sealed class FunctionApprovalRequestContent : UserInputRequestContent { 
-         public FunctionApprovalRequestContent(string id, FunctionCallContent functionCall); 
-         public FunctionCallContent FunctionCall { get; }
-         public FunctionApprovalResponseContent CreateResponse(bool approved, string? reason = null); 
-     } 
-     [Experimental] 
-     public sealed class FunctionApprovalResponseContent : UserInputResponseContent { 
-         public FunctionApprovalResponseContent(string id, bool approved, FunctionCallContent functionCall); 
-         public bool Approved { get; }
-         public FunctionCallContent FunctionCall { get; }
-         public string? Reason { get; set; }
-     } 
-     [Experimental] 
-     public sealed class McpServerToolApprovalRequestContent : UserInputRequestContent { 
-         public McpServerToolApprovalRequestContent(string id, McpServerToolCallContent toolCall); 
-         public McpServerToolCallContent ToolCall { get; }
-         public McpServerToolApprovalResponseContent CreateResponse(bool approved); 
-     } 
-     [Experimental] 
-     public sealed class McpServerToolApprovalResponseContent : UserInputResponseContent { 
-         public McpServerToolApprovalResponseContent(string id, bool approved); 
-         public bool Approved { get; }
-     } 
-     [Experimental] 
-     [JsonPolymorphic] 
-     public class UserInputRequestContent : AIContent { 
-         protected UserInputRequestContent(string id); 
-         public string Id { get; }
-     } 
-     [Experimental] 
-     [JsonPolymorphic] 
-     public class UserInputResponseContent : AIContent { 
-         protected UserInputResponseContent(string id); 
-         public string Id { get; }
-     } 
  } 
```

### Other changes in this PR

```diff
-     [Experimental] 
      public class HostedMcpServerTool : AITool { 
-         public HostedMcpServerTool(string serverName, Uri serverUrl); 
+         public HostedMcpServerTool(string serverName, Uri serverAddress); 
-         public HostedMcpServerTool(string serverName, Uri serverUrl, IReadOnlyDictionary<string, object?>? additionalProperties); 
+         public HostedMcpServerTool(string serverName, Uri serverAddress, IReadOnlyDictionary<string, object?>? additionalProperties); 
-         public string? AuthorizationToken { get; set; }
-         public IDictionary<string, string> Headers { get; }
+         public IDictionary<string, string>? Headers { get; set; }
          // ... remaining members unchanged
      } 
```
