using BenchmarkDotNet.Running;
using OpenSpecification.Spike;

if (args.Length > 0 && args[0] == "bench")
{
    BenchmarkSwitcher.FromAssembly(typeof(Checks).Assembly).Run(args[1..]);
    return 0;
}

return await Checks.RunAsync();
