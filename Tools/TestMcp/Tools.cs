using System.Text.Json.Nodes;

namespace OnlineSeverGame.TestMcp;

/// <summary>The tool list. Starting tools only queue a job and return its id; the rest read job state.</summary>
public static class Tools
{
    public static List<Tool> Create(JobRunner jobs) => new()
    {
        new Tool
        {
            Name = "run_unit_tests",
            Description =
                "Queue `dotnet test` on the whole solution (xUnit, Server/GameServer.Tests) as a background job and return its id " +
                "at once. The summary has pass/fail/skip counts and, for each failing test, its message and top stack lines. " +
                "Follow with wait_job.",
            InputSchema = new Schema()
                .String("filter", "Optional `dotnet test --filter` expression, e.g. FullyQualifiedName~PhysicsTests or Name=Dash_KnocksBack")
                .Build(),
            Run = a =>
            {
                string filter = Args.String(a, "filter");
                string description = filter == null ? "dotnet test" : $"dotnet test --filter {filter}";
                return Started(jobs, jobs.Enqueue("unit", description, (JsonObject)a.DeepClone(), ctx => Checks.UnitTests(ctx, filter)));
            },
        },
        new Tool
        {
            Name = "run_match",
            Description =
                "Queue a real match as a background job: build the solution, start the game server on a free local port " +
                "with --exit-after-rounds, connect console bots (Tools/BotClient) and wait until both exit. The summary says " +
                "whether every round ended, the round-end and input-queue lines from the server log, deaths, and any exception, " +
                "protocol error or rejected bot. Follow with wait_job.",
            InputSchema = new Schema()
                .Int("rounds", "Rounds to play before server and bots exit (default 2)", 1, 10)
                .Int("bots", "Bots to connect (default 8); a round starts when all of them are in (all but one with dropOneAfter)", 1, 8)
                .Int("dropOneAfter", "Abruptly disconnect the last bot this many seconds into the first round (default off)", 1, 600)
                .Int("seed", "Seed for both server and bots (default: random)", 0, int.MaxValue)
                .Int("map", "Play only this map, 1..4 (default: the server's cycle)", 1, 4)
                .String("ai", "Bot brain (default dodge)", new[] { "dodge", "random" })
                .Bool("verbose", "Pass --verbose to the server (log every dash, push, stomp, near miss)")
                .Int("timeoutSeconds", "Kill the match if it is still running after this long (default rounds x 200 + 60)", 30, 3600)
                .Build(),
            Run = a =>
            {
                var o = MatchOptions.From(a);
                return Started(jobs, jobs.Enqueue("match", o.Describe(), o.ToJson(), ctx => Checks.Match(ctx, o)));
            },
        },
        new Tool
        {
            Name = "wait_job",
            Description = "Block until the job finishes or timeoutSeconds pass (max 55), then return its status. Call again if it is still running.",
            InputSchema = new Schema()
                .String("jobId", "Id from run_unit_tests / run_match, e.g. unit-1", required: true)
                .Int("timeoutSeconds", "How long to wait (default 45)", 1, 55)
                .Build(),
            Run = a =>
            {
                var job = jobs.Find(Args.Required(a, "jobId"));
                job.Wait(TimeSpan.FromSeconds(Args.Int(a, "timeoutSeconds", 1, 55) ?? 45));
                return job.Status(true);
            },
        },
        new Tool
        {
            Name = "job_status",
            Description = "State, current step and elapsed time of a job; the summary once it is done, the last log lines while it runs.",
            InputSchema = new Schema().String("jobId", "Job id", required: true).Build(),
            Run = a => jobs.Find(Args.Required(a, "jobId")).Status(true),
        },
        new Tool
        {
            Name = "job_log",
            Description =
                "Lines from a job's logs (build, test, server, bots). Without `log` every log of the job is returned. " +
                "`grep` keeps only lines containing it (case-insensitive) before taking the last `tail` lines.",
            InputSchema = new Schema()
                .String("jobId", "Job id", required: true)
                .String("log", "Which log: build, test, server or bots (what job_status lists under logs)")
                .String("grep", "Only lines containing this text")
                .Int("tail", "Last N lines per log (default 80)", 1, 2000)
                .Build(),
            Run = a => JobLog(jobs.Find(Args.Required(a, "jobId")), Args.String(a, "log"), Args.String(a, "grep"), Args.Int(a, "tail", 1, 2000) ?? 80),
        },
        new Tool
        {
            Name = "list_jobs",
            Description = "Every job this server has queued since it started, newest first, with state and verdict.",
            InputSchema = new Schema().Build(),
            Run = _ => new JsonObject
            {
                ["jobs"] = new JsonArray(jobs.All().OrderByDescending(j => j.Seq).Select(j =>
                {
                    var s = j.Status(false);
                    return (JsonNode)new JsonObject
                    {
                        ["jobId"] = j.Id,
                        ["description"] = j.Description,
                        ["state"] = s["state"]?.DeepClone(),
                        ["verdict"] = s["summary"]?["verdict"]?.DeepClone(),
                        ["queuedAt"] = s["queuedAt"]?.DeepClone(),
                        ["elapsedSeconds"] = s["elapsedSeconds"]?.DeepClone(),
                    };
                }).ToArray()),
            },
        },
        new Tool
        {
            Name = "cancel_job",
            Description = "Cancel a queued job, or kill a running one (its whole process tree).",
            InputSchema = new Schema().String("jobId", "Job id", required: true).Build(),
            Run = a =>
            {
                var job = jobs.Find(Args.Required(a, "jobId"));
                job.Cancel();
                job.Wait(TimeSpan.FromSeconds(10));
                return job.Status(false);
            },
        },
    };

    private static JsonObject Started(JobRunner jobs, Job job) => new()
    {
        ["jobId"] = job.Id,
        ["state"] = job.State.ToString().ToLowerInvariant(),
        ["description"] = job.Description,
        ["jobsAhead"] = jobs.Ahead(job),
        ["next"] = $"wait_job {{\"jobId\":\"{job.Id}\"}} until state is passed, failed or cancelled",
    };

    private static JsonObject JobLog(Job job, string log, string grep, int tail)
    {
        string[] names = job.LogNames();
        if (log != null && Array.IndexOf(names, log) < 0)
            throw new ToolException($"job {job.Id} has no '{log}' log (it has: {(names.Length == 0 ? "none yet" : string.Join(", ", names))})");

        var o = new JsonObject { ["jobId"] = job.Id, ["state"] = job.State.ToString().ToLowerInvariant() };
        foreach (string name in log != null ? new[] { log } : names)
        {
            var lines = LogFile.Lines(Path.Combine(job.Dir, name + ".log"));
            if (grep != null) lines = lines.Where(l => l.Contains(grep, StringComparison.OrdinalIgnoreCase)).ToList();
            o[name] = new JsonObject
            {
                ["matchingLines"] = lines.Count,
                ["lines"] = new JsonArray(lines.Skip(Math.Max(0, lines.Count - tail)).Select(l => (JsonNode)l).ToArray()),
            };
        }
        return o;
    }
}
