using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;

string host = "127.0.0.1";
int port = ProtocolConstants.DefaultPort;
int count = 8;
string baseName = "Bot";
int seed = Environment.TickCount;
int rounds = 0;            // exit after this many rounds (0 = never)
double dropOneAfter = -1;  // seconds into the first Playing phase; then the last bot disconnects abruptly
double duration = 0;       // give up after this many seconds (0 = never)

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--host": host = Next(); break;
        case "--port": port = int.Parse(Next()); break;
        case "--count": count = int.Parse(Next()); break;
        case "--name": baseName = Next(); break;
        case "--seed": seed = int.Parse(Next()); break;
        case "--rounds": rounds = int.Parse(Next()); break;
        case "--drop-one-after": dropOneAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--duration": duration = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "-h":
        case "--help":
            Console.WriteLine("OnlineSeverGame.BotClient [--host 127.0.0.1] [--port 7777] [--count 8] [--name Bot] [--seed N]\n" +
                              "                          [--rounds N] [--drop-one-after SECONDS] [--duration SECONDS]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown option {args[i]} (see --help)");
            return 2;
    }
}

var bots = new List<Bot>();
for (int i = 0; i < count; i++)
{
    // Bot 0 reports round events for everyone.
    var bot = new Bot(i, $"{baseName}{i + 1}", seed + i * 7919, verbose: i == 0);
    try
    {
        bot.Start(host, port);
        bots.Add(bot);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{bot.Name}] connect failed: {ex.Message}");
    }
}
Console.WriteLine($"{bots.Count}/{count} bots connected to {host}:{port}");
if (bots.Count == 0) return 1;

var reporter = bots[0];
bool dropped = false;
var stopAt = duration > 0 ? Bot.Clock.Elapsed.TotalSeconds + duration : double.MaxValue;
int exitCode = 0;
while (true)
{
    Thread.Sleep(100);

    if (!dropped && dropOneAfter >= 0 && bots.Count > 1)
    {
        long since = Interlocked.Read(ref reporter.PlayingSinceMs);
        if (since >= 0 && Bot.Clock.ElapsedMilliseconds - since >= dropOneAfter * 1000)
        {
            var victim = bots[^1];
            Console.WriteLine($"[{victim.Name}] (#{victim.PlayerId}, alive={victim.Alive}) DROPPING connection abruptly");
            victim.Drop();
            dropped = true;
        }
    }

    if (rounds > 0 && Volatile.Read(ref reporter.RoundsEnded) >= rounds)
    {
        Console.WriteLine($"{rounds} round(s) finished, exiting");
        break;
    }
    if (!reporter.Connected)
    {
        Console.WriteLine("reporter bot lost its connection, exiting");
        exitCode = 1;
        break;
    }
    if (Bot.Clock.Elapsed.TotalSeconds > stopAt)
    {
        Console.WriteLine("duration reached, exiting");
        break;
    }
}

foreach (var b in bots)
    Console.WriteLine($"[{b.Name}] #{b.PlayerId} sent seq {b.Seq}, server acked {b.LastAckSeq}, connected={b.Connected}");
foreach (var b in bots) b.Stop();
return exitCode;
