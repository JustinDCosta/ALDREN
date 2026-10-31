using Aldren.Bench.Commands;

return args.FirstOrDefault() switch
{
    "smoke" => await SmokeCommand.RunAsync(args[1..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: aldren-bench <command> [options]");
    Console.Error.WriteLine("  smoke --model <path.gguf> [--ctx 4096]   load a model and time one short reply");
    return 2;
}
