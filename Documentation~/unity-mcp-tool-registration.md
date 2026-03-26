---
uid: unity-mcp-tool-registration
---

# Register custom MCP tools

Create custom tools that AI agents can invoke through the Unity MCP bridge.

Unity MCP uses attributes and interfaces to identify methods or classes as MCP tools. When the Unity Editor starts, `McpToolRegistry` scans assemblies using `TypeCache` and registers tools automatically.

Each tool defines:

- A unique **name** exposed to MCP clients.
- A **description** of what the tool does.
- **Input parameters** that describe the data the tool requires.
- An optional **output schema** that defines the structure of returned data.

## Registration methods

Unity MCP supports four registration approaches.

| Method | Best for | Description |
|--------|----------|-------------|
| Static method with typed parameters | Simple tools | Schema auto-generated from parameter types. |
| Static method with `JObject` parameters | Flexible tools | Custom or dynamic schemas. |
| Class-based tool | Stateful tools | Maintain state and helper methods within a tool class. |
| Runtime API | Dynamic tools | Register or unregister tools programmatically at runtime. |

### Static method with typed parameters

The simplest approach. Define a static method with the `[McpTool]` attribute and a typed parameter class:

```csharp
[McpTool("my_tool", "Description of what this tool does")]
public static object MyTool(MyParameters parameters)
{
    return new { success = true, message = $"Processed {parameters.Action} for {parameters.Name}" };
}
```

You can improve the schema description further by using the `[McpDescription]` attribute.
```csharp
public class MyParameters
{
    [McpDescription("Action to perform", Required = true, EnumType = typeof(ActionType))]
    public string Action { get; set; }

    [McpDescription("Target name")]
    public string Name { get; set; } = "default";
}

public enum ActionType { Create, Update, Delete }
```

Unity MCP automatically:
- Discovers and registers the tool at editor startup.
- Generates a JSON schema from the parameter type.
- Validates parameters and converts them to the appropriate types.
- Exposes the tool to connected MCP clients.

### Static method with `JObject` parameters

Use this when you need flexible or nested data structures with a custom schema:

```csharp
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;

[McpTool("flexible_tool", "Tool with custom schema")]
public static object HandleFlexibleTool(JObject parameters)
{
    var action = parameters["action"]?.ToString();
    var data = parameters["data"];
    return new { processed = action, data };
}

[McpSchema("flexible_tool")]
public static object GetFlexibleToolSchema()
{
    return new
    {
        type = "object",
        properties = new
        {
            action = new { type = "string", @enum = new[] { "process", "validate" } },
            data = new { type = "object", description = "Flexible data object" }
        },
        required = new[] { "action" }
    };
}
```

### Class-based tool

Use this for tools that need internal state or helper methods:

```csharp
using Unity.AI.MCP.Editor.ToolRegistry;

[McpTool("stateful_tool", "Tool with internal state")]
public class StatefulTool : IUnityMcpTool<StatefulParams>
{
    readonly Dictionary<string, object> _cache = new();

    public Task<object> ExecuteAsync(StatefulParams parameters)
    {
        var result = new { id = parameters.Id, processed = true };
        _cache[parameters.Id] = result;
        return Task.FromResult<object>(result);
    }
}

public class StatefulParams
{
    [McpDescription("Unique identifier", Required = true)]
    public string Id { get; set; }
}
```

You can also implement `IUnityMcpTool` (without generics) for tools that accept `JObject` parameters and provide custom schemas via `GetInputSchema()` and `GetOutputSchema()`.

### Runtime API registration

Register or unregister tools dynamically:

```csharp
using Unity.AI.MCP.Editor.ToolRegistry;

// Register a typed tool
var tool = new MyTool();
McpToolRegistry.RegisterTool<MyParams>("dynamic_tool", tool, "A dynamically registered tool");

// Unregister when no longer needed
McpToolRegistry.UnregisterTool("dynamic_tool");
```

## Attribute reference

### `[McpTool(name, description)]`

Marks a static method or class as an MCP tool.

| Property | Type | Description |
|----------|------|-------------|
| `Name` | string | Required. Unique tool identifier exposed to MCP clients. |
| `Description` | string | Optional. Human-readable description. |
| `Title` | string | Optional. Display title (defaults to description). |
| `Groups` | string[] | Optional. Category tags for organizing tools. |

### `[McpDescription(description)]`

Adds descriptions and constraints to parameter properties.

| Property | Type | Description |
|----------|------|-------------|
| `Description` | string | Required. Human-readable description of the parameter. |
| `Required` | bool | Optional. Marks the parameter as mandatory (default: false). |
| `EnumType` | Type | Optional. Enum type for constraining string values. |
| `Default` | object | Optional. Explicit default value for the schema. |

### `[McpSchema(toolName)]`

Links a static method to a tool as its custom input schema provider. Use with `JObject`-parameter tools.

### `[McpOutputSchema(toolName)]`

Links a static method to a tool as its custom output schema provider.

## Registry events

Subscribe to `McpToolRegistry.ToolsChanged` to react when tools are added, removed, or updated:

```csharp
McpToolRegistry.ToolsChanged += (args) =>
{
    switch (args.ChangeType)
    {
        case McpToolRegistry.ToolChangeType.Added:
            Debug.Log($"Tool added: {args.ToolName}");
            break;
        case McpToolRegistry.ToolChangeType.Removed:
            Debug.Log($"Tool removed: {args.ToolName}");
            break;
        case McpToolRegistry.ToolChangeType.Refreshed:
            Debug.Log("All tools refreshed");
            break;
    }
};
```

## Schema generation

For typed parameters, Unity MCP generates JSON schemas automatically. A parameter class like:

```csharp
public class ExampleParams
{
    [McpDescription("Name of the object", Required = true)]
    public string Name { get; set; }

    [McpDescription("Scale multiplier", Default = 1.0)]
    public float Scale { get; set; } = 1.0f;

    [McpDescription("Object type", EnumType = typeof(ObjectType))]
    public string Type { get; set; }
}

public enum ObjectType { Cube, Sphere, Cylinder }
```

Generates:

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Name of the object" },
    "scale": { "type": "number", "description": "Scale multiplier", "default": 1.0 },
    "type": { "type": "string", "description": "Object type", "enum": ["cube", "sphere", "cylinder"] }
  },
  "required": ["name"]
}
```

## Additional resources

- [Unity MCP overview](xref:unity-mcp-overview)
- [Get started with Unity MCP](xref:unity-mcp-get-started)
- [Troubleshoot Unity MCP issues](xref:unity-mcp-troubleshooting)
