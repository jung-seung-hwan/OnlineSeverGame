using OnlineSeverGame.Server;
using OnlineSeverGame.Shared.Simulation;

var options = new ServerOptions();
for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--port": options.Port = int.Parse(Next()); break;
        case "--min-players": options.MinPlayers = Math.Clamp(int.Parse(Next()), 1, 8); break;
        case "--seed": options.Seed = int.Parse(Next()); break;
        case "--exit-after-rounds": options.ExitAfterRounds = int.Parse(Next()); break;
        case "--verbose": options.Verbose = true; break;
        case "--maps":
            string mode = Next();
            options.Rotation = mode switch
            {
                "cycle" => MapRotation.Cycle,
                "random" => MapRotation.Random,
                _ => throw new ArgumentException("--maps takes cycle or random"),
            };
            break;
        case "--map":
            options.FixedMap = Stage.ById(byte.Parse(Next())) ?? throw new ArgumentException("--map takes 1..4");
            options.Rotation = MapRotation.Fixed;
            break;
        case "-h":
        case "--help":
            Console.WriteLine("OnlineSeverGame.Server [--port 7777] [--min-players 2] [--seed N] [--exit-after-rounds N] [--verbose] [--maps cycle|random] [--map 1..4]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown option {args[i]} (see --help)");
            return 2;
    }
}

var server = new GameServer(options);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.Info("Ctrl+C, stopping");
    server.Stop();
};
server.Start();
server.WaitForExit();
return 0;
