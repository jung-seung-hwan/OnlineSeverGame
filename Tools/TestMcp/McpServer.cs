using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OnlineSeverGame.TestMcp;

/// <summary>
/// The MCP side: newline-delimited JSON-RPC 2.0 on stdio with the four methods a tools-only server
/// needs (initialize, ping, tools/list, tools/call). Each request runs on the thread pool so a
/// blocking wait_job does not hold up other calls; writes to stdout are serialized.
/// </summary>
public sealed class McpServer
{
    public const string Name = "onlinesevergame-test";
    public const string Version = "1.0.0";

    // Newest first. A client asking for one of these gets it back; anything else gets the newest.
    private static readonly string[] SupportedVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

    internal static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<string, Tool> _tools;
    private readonly object _writeGate = new();
    private TextWriter _out;

    public McpServer(JobRunner jobs)
    {
        _tools = Tools.Create(jobs).ToDictionary(t => t.Name);
    }

    public int Run(TextReader input, TextWriter output)
    {
        _out = output;
        var pending = new List<Task>();
        string line;
        while ((line = input.ReadLine()) != null)
        {
            if (line.Trim().Length == 0) continue;
            JsonNode msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { Send(Error(null, -32700, "parse error")); continue; }
            if (msg is not JsonObject obj) { Send(Error(null, -32600, "expected one JSON-RPC object per line")); continue; }

            pending.RemoveAll(t => t.IsCompleted);
            pending.Add(Task.Run(() => Handle(obj)));
        }
        Task.WaitAll(pending.ToArray(), TimeSpan.FromSeconds(2));
        return 0;
    }

    private void Handle(JsonObject msg)
    {
        bool isRequest = msg.TryGetPropertyValue("id", out JsonNode idNode);
        JsonNode id = idNode?.DeepClone();
        string method = msg["method"]?.GetValue<string>();
        if (method == null) return; // a response; this server never sends requests

        try
        {
            JsonNode result = method switch
            {
                "initialize" => Initialize(msg["params"] as JsonObject),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = new JsonArray(_tools.Values.Select(t => (JsonNode)t.Describe()).ToArray()) },
                "tools/call" => Call(msg["params"] as JsonObject),
                _ => null,
            };
            if (!isRequest) return; // notifications (notifications/initialized, .../cancelled) get no answer
            Send(result != null
                ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }
                : Error(id, -32601, $"method not found: {method}"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"test-mcp: {method} failed: {ex}");
            if (isRequest) Send(Error(id, ex is ArgumentException ? -32602 : -32603, ex.Message));
        }
    }

    private static JsonObject Initialize(JsonObject p)
    {
        string asked = p?["protocolVersion"]?.GetValue<string>();
        string version = Array.IndexOf(SupportedVersions, asked) >= 0 ? asked : SupportedVersions[0];
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = Name, ["version"] = Version },
            ["instructions"] =
                "Runs OnlineSeverGame checks as background jobs, one at a time. run_unit_tests and run_match " +
                "return a job id at once; follow it with wait_job (blocks up to 55 s), job_status or job_log. " +
                "Logs and summary.json stay under artifacts/test-mcp/.",
        };
    }

    private JsonObject Call(JsonObject p)
    {
        string name = p?["name"]?.GetValue<string>() ?? throw new ArgumentException("tools/call needs a name");
        if (!_tools.TryGetValue(name, out Tool tool)) throw new ArgumentException($"unknown tool: {name}");
        var args = p["arguments"] as JsonObject ?? new JsonObject();

        string text;
        bool isError = false;
        try
        {
            text = tool.Run(args).ToJsonString(Pretty);
        }
        catch (ToolException ex)
        {
            // Bad arguments or an unknown job id are tool results the model can read and fix, not protocol errors.
            text = ex.Message;
            isError = true;
        }
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };
    }

    private void Send(JsonObject msg)
    {
        string s = msg.ToJsonString();
        lock (_writeGate) _out.WriteLine(s);
    }

    private static JsonObject Error(JsonNode id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
