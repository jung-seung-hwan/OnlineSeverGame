using System.Text;
using OnlineSeverGame.TestMcp;

// stdio MCP server: one JSON-RPC message per line on stdin/stdout. Diagnostics go to stderr only,
// because any stray line on stdout would break the client's parser.
string repo = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--repo" && i + 1 < args.Length) repo = Path.GetFullPath(args[++i]);
    else
    {
        Console.Error.WriteLine($"unknown option {args[i]} (usage: OnlineSeverGame.TestMcp [--repo PATH])");
        return 2;
    }
}
repo ??= RepoRoot.Find(AppContext.BaseDirectory) ?? RepoRoot.Find(Directory.GetCurrentDirectory());
if (repo == null)
{
    Console.Error.WriteLine($"test-mcp: {RepoRoot.Marker} not found above {AppContext.BaseDirectory}; pass --repo");
    return 2;
}

var utf8 = new UTF8Encoding(false);
var input = new StreamReader(Console.OpenStandardInput(), utf8);
var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
Console.Error.WriteLine($"test-mcp {McpServer.Version}: repo {repo}");

// Closing stdin (the client went away) ends Run; disposing the runner kills any job still running,
// so no server or bot process outlives the session that started it.
using var jobs = new JobRunner(repo);
return new McpServer(jobs).Run(input, output);
