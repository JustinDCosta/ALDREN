using S6.Host;

// S6 browser bridge probe.
//   chrome-extension://<id>/   how Chrome starts the host: relays between the extension and ALDREN's pipe
//   probe                      serves the test page, opens it in the test profile and drives it through the extension
//   selftest                   checks the host with this process standing in for the extension (no Chrome needed)
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return args.FirstOrDefault() switch
{
    { } origin when origin.StartsWith("chrome-extension://", StringComparison.Ordinal) =>
        await Relay.RunAsync(origin, Console.OpenStandardInput(), Console.OpenStandardOutput()),
    "probe" => await Probe.RunAsync(cts.Token),
    "selftest" => await SelfTest.RunAsync(cts.Token),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("S6.Host probe | selftest   (Chrome starts the host itself with the extension's origin)");
    return 1;
}
