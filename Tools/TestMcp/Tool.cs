using System.Text.Json.Nodes;

namespace OnlineSeverGame.TestMcp;

/// <summary>One MCP tool: what tools/list shows plus the handler tools/call runs.</summary>
public sealed class Tool
{
    public string Name;
    public string Description;
    public JsonObject InputSchema;
    public Func<JsonObject, JsonNode> Run;

    public JsonObject Describe() => new()
    {
        ["name"] = Name,
        ["description"] = Description,
        ["inputSchema"] = InputSchema.DeepClone(),
    };
}

/// <summary>A failure the caller can fix (bad argument, unknown job). Reported as an isError tool result.</summary>
public sealed class ToolException : Exception
{
    public ToolException(string message) : base(message) { }
}

/// <summary>Reads tool arguments leniently: numbers may arrive as 2, 2.0 or "2".</summary>
public static class Args
{
    public static int? Int(JsonObject a, string key, int min, int max)
    {
        JsonNode node = a[key];
        if (node == null) return null;
        int value;
        if (node is JsonValue v && v.TryGetValue(out double d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue) value = (int)d;
        else if (node is JsonValue s && s.TryGetValue(out string str) && int.TryParse(str, out int parsed)) value = parsed;
        else throw new ToolException($"{key} must be an integer");
        if (value < min || value > max) throw new ToolException($"{key} must be {min}..{max}, got {value}");
        return value;
    }

    public static string String(JsonObject a, string key, params string[] allowed)
    {
        JsonNode node = a[key];
        if (node == null) return null;
        if (node is not JsonValue v || !v.TryGetValue(out string value)) throw new ToolException($"{key} must be a string");
        if (allowed.Length > 0 && Array.IndexOf(allowed, value) < 0)
            throw new ToolException($"{key} must be one of {string.Join(", ", allowed)}, got '{value}'");
        return value;
    }

    public static bool Bool(JsonObject a, string key)
    {
        JsonNode node = a[key];
        if (node == null) return false;
        if (node is JsonValue v && v.TryGetValue(out bool b)) return b;
        if (node is JsonValue s && s.TryGetValue(out string str) && bool.TryParse(str, out bool parsed)) return parsed;
        throw new ToolException($"{key} must be true or false");
    }

    public static string Required(JsonObject a, string key) =>
        String(a, key) ?? throw new ToolException($"{key} is required");
}

/// <summary>Small builder for the JSON Schema objects in tools/list.</summary>
public sealed class Schema
{
    private readonly JsonObject _props = new();
    private readonly JsonArray _required = new();

    public Schema Int(string name, string description, int min, int max)
    {
        _props[name] = new JsonObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["description"] = description };
        return this;
    }

    public Schema String(string name, string description, string[] values = null, bool required = false)
    {
        var p = new JsonObject { ["type"] = "string", ["description"] = description };
        if (values != null) p["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
        _props[name] = p;
        if (required) _required.Add(name);
        return this;
    }

    public Schema Bool(string name, string description)
    {
        _props[name] = new JsonObject { ["type"] = "boolean", ["description"] = description };
        return this;
    }

    public JsonObject Build()
    {
        var o = new JsonObject { ["type"] = "object", ["properties"] = _props.DeepClone() };
        if (_required.Count > 0) o["required"] = _required.DeepClone();
        return o;
    }
}
