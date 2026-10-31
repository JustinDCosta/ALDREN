using Aldren.Bench.Commands;

return args.FirstOrDefault() switch
{
    "smoke" => await SmokeCommand.RunAsync(args[1..]),
    "models" => await ModelsCommand.RunAsync(args[1..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: aldren-bench <command> [options]");
    Console.Error.WriteLine("  smoke --model <path.gguf> [--ctx 4096]                       load a model and time one short reply");
    Console.Error.WriteLine("  models [--only a,b] [--skip-switch] [--allow-battery]         benchmark the Phase 0 candidate models");
    return 2;
}
