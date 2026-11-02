using Aldren.Bench.Commands;

return args.FirstOrDefault() switch
{
    "smoke" => await SmokeCommand.RunAsync(args[1..]),
    "models" => await ModelsCommand.RunAsync(args[1..]),
    "gateway" => await GatewayCommand.RunAsync(args[1..]),
    "idle" => await IdleCommand.RunAsync(args[1..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: aldren-bench <command> [options]");
    Console.Error.WriteLine("  smoke --model <path.gguf> [--ctx 4096]                       load a model and time one short reply");
    Console.Error.WriteLine("  models [--only a,b] [--skip-switch] [--allow-battery]         benchmark the Phase 0 candidate models");
    Console.Error.WriteLine("  gateway [--url http://127.0.0.1:<port>] [--checks abcdefghikl] check a running FreeLLMAPI gateway (S8)");
    Console.Error.WriteLine("  idle [--minutes 10] [--out <file.md>]                         idle CPU and memory of a running ALDREN (Phase 1)");
    return 2;
}
