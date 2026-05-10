using ZScout.HwTest.Contracts.Models;

var mode = args.Contains("--mode") ? args.SkipWhile(a => a != "--mode").Skip(1).FirstOrDefault() : "host";
var parsedMode = string.Equals(mode, "container", StringComparison.OrdinalIgnoreCase) ? RunMode.Container : RunMode.Host;

Console.WriteLine($"zSCOUT hardware test CLI bootstrap - mode: {parsedMode}");