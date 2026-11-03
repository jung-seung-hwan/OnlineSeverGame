using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace OnlineSeverGame.TestMcp;

public enum JobState
{
    Queued,
    Running,
    Passed,
    Failed,
    Cancelled,
}

/// <summary>
/// Runs jobs one at a time on its own thread, in the order they were queued. One at a time because
/// every job builds the solution, and two concurrent builds of the same projects fight over obj/ files.
/// </summary>
public sealed class JobRunner : IDisposable
{
    private readonly BlockingCollection<Job> _queue = new();
    private readonly List<Job> _all = new();
    private readonly object _gate = new();
    private readonly Thread _worker;
    private int _next;

    public string Repo { get; }
    public string JobsDir { get; }

    public JobRunner(string repo)
    {
        Repo = repo;
        JobsDir = Path.Combine(repo, "artifacts", "test-mcp");
        _worker = new Thread(Work) { IsBackground = true, Name = "jobs" };
        _worker.Start();
    }

    public Job Enqueue(string kind, string description, JsonObject options, Func<JobContext, JsonObject> body)
    {
        Job job;
        lock (_gate)
        {
            job = new Job(++_next, kind, description, options, body);
            _all.Add(job);
        }
        _queue.Add(job);
        return job;
    }

    public Job Find(string id)
    {
        lock (_gate)
            return _all.FirstOrDefault(j => j.Id == id) ?? throw new ToolException($"no job '{id}' (list_jobs shows the known ones)");
    }

    public List<Job> All()
    {
        lock (_gate) return _all.ToList();
    }

    /// <summary>Jobs that will run before <paramref name="job"/> (the running one included).</summary>
    public int Ahead(Job job)
    {
        lock (_gate) return _all.Count(j => j.Seq < job.Seq && j.State is JobState.Queued or JobState.Running);
    }

    private void Work()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
            job.Execute(Repo, JobsDir);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        foreach (var job in All()) job.Cancel();
        _worker.Join(TimeSpan.FromSeconds(5));
    }
}

public sealed class Job
{
    private readonly Func<JobContext, JsonObject> _body;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _done = new();
    private readonly object _gate = new();
    private JobState _state = JobState.Queued;
    private string _step = "queued";
    private DateTime? _started, _finished;
    private JsonObject _summary;

    public int Seq { get; }
    public string Id { get; }
    public string Kind { get; }
    public string Description { get; }
    public JsonObject Options { get; }
    public DateTime QueuedAt { get; } = DateTime.Now;
    public string Dir { get; private set; }

    public Job(int seq, string kind, string description, JsonObject options, Func<JobContext, JsonObject> body)
    {
        Seq = seq;
        Id = $"{kind}-{seq}";
        Kind = kind;
        Description = description;
        Options = options;
        _body = body;
    }

    public JobState State
    {
        get { lock (_gate) return _state; }
    }

    public bool IsFinished => _done.IsSet;

    public void Execute(string repo, string jobsRoot)
    {
        if (_cts.IsCancellationRequested)
        {
            Finish(JobState.Cancelled, new JsonObject { ["passed"] = false, ["verdict"] = "cancelled before it started" });
            return;
        }

        Dir = Path.Combine(jobsRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Id}");
        Directory.CreateDirectory(Dir);
        lock (_gate)
        {
            _state = JobState.Running;
            _started = DateTime.Now;
        }

        var ctx = new JobContext(this, repo, Dir, _cts.Token);
        try
        {
            JsonObject summary = _body(ctx);
            bool passed = summary["passed"]?.GetValue<bool>() == true;
            Finish(_cts.IsCancellationRequested ? JobState.Cancelled : passed ? JobState.Passed : JobState.Failed, summary);
        }
        catch (OperationCanceledException)
        {
            Finish(JobState.Cancelled, new JsonObject { ["passed"] = false, ["verdict"] = "cancelled" });
        }
        catch (Exception ex)
        {
            Finish(JobState.Failed, new JsonObject { ["passed"] = false, ["verdict"] = "the job itself crashed", ["error"] = ex.ToString() });
        }
        finally
        {
            ctx.KillAll();
        }
    }

    private void Finish(JobState state, JsonObject summary)
    {
        lock (_gate)
        {
            _state = state;
            _finished = DateTime.Now;
            _summary = summary;
            _step = "done";
        }
        if (Dir != null)
        {
            try { File.WriteAllText(Path.Combine(Dir, "summary.json"), Status(false).ToJsonString(McpServer.Pretty), new UTF8Encoding(false)); }
            catch (IOException ex) { Console.Error.WriteLine($"test-mcp: could not write summary for {Id}: {ex.Message}"); }
        }
        _done.Set();
    }

    public void SetStep(string step)
    {
        lock (_gate) _step = step;
    }

    public void Cancel() => _cts.Cancel();

    public bool Wait(TimeSpan timeout) => _done.Wait(timeout);

    public string[] LogNames() =>
        Dir == null || !Directory.Exists(Dir)
            ? Array.Empty<string>()
            : Directory.GetFiles(Dir, "*.log").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray();

    public JsonObject Status(bool withTail)
    {
        JobState state;
        string step;
        DateTime? started, finished;
        JsonObject summary;
        lock (_gate)
        {
            state = _state;
            step = _step;
            started = _started;
            finished = _finished;
            summary = _summary;
        }

        var o = new JsonObject
        {
            ["jobId"] = Id,
            ["kind"] = Kind,
            ["description"] = Description,
            ["state"] = state.ToString().ToLowerInvariant(),
            ["step"] = step,
            ["queuedAt"] = QueuedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ["startedAt"] = started?.ToString("yyyy-MM-dd HH:mm:ss"),
            ["finishedAt"] = finished?.ToString("yyyy-MM-dd HH:mm:ss"),
            ["elapsedSeconds"] = started == null ? null : Math.Round(((finished ?? DateTime.Now) - started.Value).TotalSeconds, 1),
            ["options"] = Options.DeepClone(),
            ["dir"] = Dir,
            ["logs"] = new JsonArray(LogNames().Select(n => (JsonNode)n).ToArray()),
        };
        if (summary != null) o["summary"] = summary.DeepClone();

        // While it runs, the last lines of whichever log moved last show what it is doing.
        if (withTail && state == JobState.Running && Dir != null)
        {
            string latest = Directory.GetFiles(Dir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest != null)
            {
                o["tailOf"] = Path.GetFileNameWithoutExtension(latest);
                o["tail"] = new JsonArray(LogFile.Tail(latest, 12).Select(l => (JsonNode)l).ToArray());
            }
        }
        return o;
    }
}

/// <summary>What a job body gets: where to write, the cancel token, and a way to start dotnet processes.</summary>
public sealed class JobContext
{
    private readonly Job _job;
    private readonly List<Proc> _procs = new();

    public string Repo { get; }
    public string Dir { get; }
    public CancellationToken Token { get; }

    public JobContext(Job job, string repo, string dir, CancellationToken token)
    {
        _job = job;
        Repo = repo;
        Dir = dir;
        Token = token;
    }

    public void Step(string step) => _job.SetStep(step);

    public string LogPath(string name) => Path.Combine(Dir, name + ".log");

    /// <summary>Starts <c>dotnet args</c> in the repo root, logging stdout and stderr to <c>name.log</c>.</summary>
    public Proc Start(string logName, IEnumerable<string> dotnetArgs, Action<string> onLine = null)
    {
        Token.ThrowIfCancellationRequested();
        var p = new Proc(Repo, LogPath(logName), dotnetArgs, onLine);
        lock (_procs) _procs.Add(p);
        return p;
    }

    /// <summary>Runs to completion. Null exit code = killed (timeout or cancel).</summary>
    public int? Run(string logName, IEnumerable<string> dotnetArgs, TimeSpan timeout)
    {
        using var p = Start(logName, dotnetArgs);
        int? exit = p.WaitExit(timeout, Token);
        Token.ThrowIfCancellationRequested();
        return exit;
    }

    public void KillAll()
    {
        lock (_procs)
            foreach (var p in _procs) p.Dispose();
    }
}

/// <summary>A child dotnet process whose output is pumped line by line into a log file.</summary>
public sealed class Proc : IDisposable
{
    private readonly Process _p;
    private readonly StreamWriter _log;
    private readonly Action<string> _onLine;
    private readonly Task _pumps;
    private bool _closed;

    public Proc(string repo, string logPath, IEnumerable<string> dotnetArgs, Action<string> onLine)
    {
        var args = dotnetArgs.ToList();
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Stdin is redirected (and closed) so a child never reads the MCP messages meant for us.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        // No long-lived MSBuild nodes or build server: they would inherit our pipes and outlive the job.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        _onLine = onLine;
        _log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        Write($"> dotnet {string.Join(' ', args)}");
        _p = Process.Start(psi) ?? throw new InvalidOperationException("dotnet did not start");
        _p.StandardInput.Close();
        _pumps = Task.WhenAll(Pump(_p.StandardOutput, ""), Pump(_p.StandardError, "[stderr] "));
    }

    public bool HasExited => _p.HasExited;

    private async Task Pump(StreamReader reader, string prefix)
    {
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            Write(prefix + line);
            _onLine?.Invoke(line);
        }
    }

    private void Write(string line)
    {
        lock (_log)
            if (!_closed) _log.WriteLine(line);
    }

    /// <summary>Exit code, or null if it was killed because of the timeout or a cancel.</summary>
    public int? WaitExit(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_p.WaitForExit(200))
        {
            if (token.IsCancellationRequested || DateTime.UtcNow > deadline)
            {
                Write(token.IsCancellationRequested ? "# killed: job cancelled" : $"# killed: still running after {timeout.TotalSeconds:0} s");
                Kill();
                return null;
            }
        }
        // A grandchild that inherited the pipe can keep it open past exit; don't wait on it forever.
        _pumps.Wait(TimeSpan.FromSeconds(3));
        Write($"# exit code {_p.ExitCode}");
        return _p.ExitCode;
    }

    public void Kill()
    {
        try
        {
            if (!_p.HasExited) _p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        Kill();
        _pumps.Wait(TimeSpan.FromSeconds(2));
        lock (_log)
        {
            if (_closed) return;
            _closed = true;
            _log.Dispose();
        }
        _p.Dispose();
    }
}

public static class LogFile
{
    /// <summary>All lines, readable while the job is still writing the file.</summary>
    public static List<string> Lines(string path)
    {
        var lines = new List<string>();
        if (!File.Exists(path)) return lines;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        string line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines;
    }

    public static List<string> Tail(string path, int count)
    {
        var lines = Lines(path);
        return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
    }
}
