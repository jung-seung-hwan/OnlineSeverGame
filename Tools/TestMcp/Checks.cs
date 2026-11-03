using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace OnlineSeverGame.TestMcp;

/// <summary>The job bodies: what each check runs and how its logs become a summary.</summary>
public static class Checks
{
    public static JsonObject UnitTests(JobContext ctx, string filter)
    {
        ctx.Step("dotnet test");
        var args = new List<string> { "test", RepoRoot.Marker, "--logger", "trx;LogFileName=unit.trx", "--results-directory", ctx.Dir };
        if (filter != null) args.AddRange(new[] { "--filter", filter });
        int? exit = ctx.Run("test", args, TimeSpan.FromMinutes(15));

        var s = new JsonObject { ["exitCode"] = exit };
        string trx = Directory.EnumerateFiles(ctx.Dir, "*.trx", SearchOption.AllDirectories).FirstOrDefault();
        int failed = 0, total = 0;
        if (trx != null)
        {
            (total, failed) = Trx.Summarize(trx, s);
        }
        else
        {
            // No results file: the build failed (or the run was killed before any test ran).
            s["buildErrors"] = BuildErrors(ctx.LogPath("test"));
        }

        bool passed = exit == 0 && trx != null && failed == 0 && total > 0;
        s["passed"] = passed;
        s["verdict"] = passed ? $"all {total} tests passed"
            : exit == null ? "killed after 15 minutes"
            : trx == null ? "build failed, no tests ran"
            : total == 0 ? "no test matched the filter"
            : $"{failed} of {total} tests failed";
        return s;
    }

    public static JsonObject Match(JobContext ctx, MatchOptions o)
    {
        var s = new JsonObject();

        ctx.Step("dotnet build");
        int? build = ctx.Run("build", new[] { "build", RepoRoot.Marker }, TimeSpan.FromMinutes(10));
        if (build != 0)
        {
            s["passed"] = false;
            s["verdict"] = "build failed";
            s["buildErrors"] = BuildErrors(ctx.LogPath("build"));
            return s;
        }

        int port = FreePort();
        s["port"] = port;

        // --no-build: the solution was just built, and two `dotnet run` builds at once would collide.
        ctx.Step($"starting server on port {port}");
        var listening = new ManualResetEventSlim();
        using var server = ctx.Start("server", new[] { "run", "--no-build", "--project", "Server/GameServer", "--" }.Concat(o.ServerArgs(port)),
            line => { if (line.Contains("server listening")) listening.Set(); });
        var startDeadline = DateTime.UtcNow.AddSeconds(30);
        while (!listening.Wait(200, ctx.Token))
        {
            if (server.HasExited || DateTime.UtcNow > startDeadline)
            {
                s["passed"] = false;
                s["verdict"] = server.HasExited ? "server exited before listening" : "server did not start listening within 30 s";
                s["serverTail"] = Lines(LogFile.Tail(ctx.LogPath("server"), 20));
                return s;
            }
        }

        ctx.Step($"{o.Bots} bots playing {o.Rounds} round(s)");
        using var bots = ctx.Start("bots", new[] { "run", "--no-build", "--project", "Tools/BotClient", "--" }.Concat(o.BotArgs(port)));
        int? botsExit = bots.WaitExit(TimeSpan.FromSeconds(o.TimeoutSeconds), ctx.Token);
        ctx.Token.ThrowIfCancellationRequested();
        // The server stops one second after the last RoundEnd; give it a little more than that.
        int? serverExit = server.WaitExit(TimeSpan.FromSeconds(botsExit == null ? 1 : 15), ctx.Token);
        ctx.Token.ThrowIfCancellationRequested();

        ctx.Step("reading logs");
        var serverLog = LogFile.Lines(ctx.LogPath("server")).Select(StripTime).ToList();
        var botLog = LogFile.Lines(ctx.LogPath("bots"));

        // "round N END on map ..." and the indented lines after it (hit and input-queue summaries).
        var roundLines = new List<string>();
        int rounds = 0;
        for (int i = 0; i < serverLog.Count; i++)
        {
            if (!serverLog[i].StartsWith("round ") || !serverLog[i].Contains(" END on map ")) continue;
            rounds++;
            roundLines.Add(serverLog[i]);
            for (int j = i + 1; j < serverLog.Count && serverLog[j].StartsWith("    "); j++) roundLines.Add(serverLog[j]);
        }

        string[] serverBad = { "Exception", "Unhandled", "protocol error", "send queue overflow", "accept failed" };
        string[] botBad = { "Exception", "Unhandled", "connect failed", "rejected" };
        var problems = serverLog.Where(l => serverBad.Any(b => l.Contains(b))).Select(l => "server: " + l)
            .Concat(botLog.Where(l => botBad.Any(b => l.Contains(b))).Select(l => "bots: " + l))
            .Take(30).ToList();

        s["serverExitCode"] = serverExit;
        s["botsExitCode"] = botsExit;
        s["roundsCompleted"] = rounds;
        s["deaths"] = serverLog.Count(l => l.StartsWith("death #"));
        s["rounds"] = Lines(roundLines);
        s["botReport"] = Lines(botLog.Where(l => l.Contains(" sent seq ") || l.Contains("DROPPING") || l.Contains("round(s) finished")));
        s["problems"] = Lines(problems);

        bool passed = botsExit == 0 && serverExit == 0 && rounds >= o.Rounds && problems.Count == 0;
        s["passed"] = passed;
        s["verdict"] = passed ? $"{rounds} round(s) played, server and bots exited cleanly"
            : botsExit == null ? $"killed after {o.TimeoutSeconds} s with {rounds}/{o.Rounds} round(s) done"
            : problems.Count > 0 ? $"{problems.Count} problem line(s) in the logs"
            : rounds < o.Rounds ? $"only {rounds}/{o.Rounds} round(s) ended"
            : $"exit codes: server {serverExit?.ToString() ?? "killed"}, bots {botsExit}";
        return s;
    }

    private static JsonArray BuildErrors(string logPath) =>
        Lines(LogFile.Lines(logPath).Where(l => l.Contains(": error ")).Distinct().Take(20));

    private static JsonArray Lines(IEnumerable<string> lines) => new(lines.Select(l => (JsonNode)l).ToArray());

    /// <summary>Server log lines start with "[HH:mm:ss.fff] ".</summary>
    private static string StripTime(string line) =>
        line.StartsWith("[") && line.IndexOf("] ", StringComparison.Ordinal) is int end and > 0 ? line[(end + 2)..] : line;

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

public sealed class MatchOptions
{
    public int Rounds = 2;
    public int Bots = 8;
    public int? DropOneAfter;
    public int? Seed;
    public int? Map;
    public string Ai = "dodge";
    public bool Verbose;
    public int TimeoutSeconds;

    public static MatchOptions From(JsonObject a)
    {
        var o = new MatchOptions
        {
            Rounds = Args.Int(a, "rounds", 1, 10) ?? 2,
            Bots = Args.Int(a, "bots", 1, 8) ?? 8,
            DropOneAfter = Args.Int(a, "dropOneAfter", 1, 600),
            Seed = Args.Int(a, "seed", 0, int.MaxValue),
            Map = Args.Int(a, "map", 1, 4),
            Ai = Args.String(a, "ai", "dodge", "random") ?? "dodge",
            Verbose = Args.Bool(a, "verbose"),
        };
        // A round is at most 3 s countdown + 180 s + 5 s results; the build comes before the timer starts.
        o.TimeoutSeconds = Args.Int(a, "timeoutSeconds", 30, 3600) ?? o.Rounds * 200 + 60;
        if (o.DropOneAfter != null && o.Bots < 2) throw new ToolException("dropOneAfter needs at least 2 bots (the last one is dropped)");
        return o;
    }

    /// <summary>
    /// Every bot, so each round starts with the same field; one fewer when a bot gets dropped,
    /// otherwise the rounds after the drop would wait for an eighth player forever.
    /// </summary>
    public int MinPlayers => DropOneAfter != null ? Bots - 1 : Bots;

    public IEnumerable<string> ServerArgs(int port)
    {
        var a = new List<string> { "--port", port.ToString(), "--min-players", MinPlayers.ToString(), "--exit-after-rounds", Rounds.ToString() };
        if (Seed != null) a.AddRange(new[] { "--seed", Seed.ToString() });
        if (Map != null) a.AddRange(new[] { "--map", Map.ToString() });
        if (Verbose) a.Add("--verbose");
        return a;
    }

    public IEnumerable<string> BotArgs(int port)
    {
        var a = new List<string> { "--port", port.ToString(), "--count", Bots.ToString(), "--rounds", Rounds.ToString(), "--ai", Ai };
        if (DropOneAfter != null) a.AddRange(new[] { "--drop-one-after", DropOneAfter.ToString() });
        if (Seed != null) a.AddRange(new[] { "--seed", Seed.ToString() });
        return a;
    }

    public string Describe() =>
        $"match: {Bots} bots x {Rounds} round(s)" + (Map != null ? $", map {Map}" : "") +
        (DropOneAfter != null ? $", drop one after {DropOneAfter} s" : "") + (Ai != "dodge" ? $", ai {Ai}" : "");

    public JsonObject ToJson() => new()
    {
        ["rounds"] = Rounds,
        ["bots"] = Bots,
        ["dropOneAfter"] = DropOneAfter,
        ["seed"] = Seed,
        ["map"] = Map,
        ["ai"] = Ai,
        ["verbose"] = Verbose,
        ["timeoutSeconds"] = TimeoutSeconds,
    };
}

/// <summary>Reads the counters and failures out of a VSTest .trx results file.</summary>
public static class Trx
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static (int total, int failed) Summarize(string path, JsonObject s)
    {
        var doc = XDocument.Load(path);
        var counters = doc.Descendants(Ns + "Counters").FirstOrDefault();
        int Count(string name) => (int?)counters?.Attribute(name) ?? 0;
        int total = Count("total"), failed = Count("failed");

        var results = doc.Descendants(Ns + "UnitTestResult").ToList();
        s["counts"] = new JsonObject
        {
            ["total"] = total,
            ["passed"] = Count("passed"),
            ["failed"] = failed,
            ["skipped"] = results.Count(r => (string)r.Attribute("outcome") == "NotExecuted"),
        };

        var failures = new JsonArray();
        foreach (var r in results.Where(r => (string)r.Attribute("outcome") == "Failed").Take(20))
        {
            failures.Add(new JsonObject
            {
                ["test"] = (string)r.Attribute("testName"),
                ["message"] = Clip(r.Descendants(Ns + "Message").FirstOrDefault()?.Value, 800),
                ["stack"] = Clip(string.Join("\n", (r.Descendants(Ns + "StackTrace").FirstOrDefault()?.Value ?? "")
                    .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(4)), 800),
            });
        }
        s["failures"] = failures;

        var slowest = results
            .Select(r => (name: (string)r.Attribute("testName"), time: TimeSpan.TryParse((string)r.Attribute("duration"), out var d) ? d : TimeSpan.Zero))
            .OrderByDescending(r => r.time).Take(3)
            .Select(r => (JsonNode)$"{r.time.TotalSeconds:0.00} s {r.name}").ToArray();
        s["slowest"] = new JsonArray(slowest);
        s["trx"] = path;
        return (total, failed);
    }

    private static string Clip(string text, int max) =>
        text == null ? null : text.Length <= max ? text.Trim() : text[..max].Trim() + " ...";
}
