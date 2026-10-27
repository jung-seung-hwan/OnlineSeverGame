using OnlineSeverGame.Server;

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
        case "-h":
        case "--help":
            Console.WriteLine("OnlineSeverGame.Server [--port 7777] [--min-players 2] [--seed N] [--exit-after-rounds N]");
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
