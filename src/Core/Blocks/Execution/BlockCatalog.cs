using StatusLine.Blocks.BuiltIns.Context;
using StatusLine.Blocks.BuiltIns.Machine;
using StatusLine.Blocks.BuiltIns.Project;
using StatusLine.Blocks.BuiltIns.Runtime;
using StatusLine.Blocks.BuiltIns.Session;
using StatusLine.Blocks.BuiltIns.Time;

namespace StatusLine.Blocks.Execution;

internal static class BlockCatalog {
    private static readonly IBuiltInBlock[] Entries = Build();

    public static IBuiltInBlock Get(StatusLineBlockKind kind) => Entries[(int)kind];

    private static IBuiltInBlock[] Build() {
        IBuiltInBlock[] implementations = [
        new AwsBlock(),
        new AzureBlock(),
        new BatteryBlock(),
        new ClockBlock(),
        new CpuBlock(),
        new GpuBlock(),
        new DateBlock(),
        new DirectoryBlock(),
        new DockerBlock(),
        new DotnetBlock(),
        new DurationBlock(),
        new ElevationBlock(),
        new ErrorsBlock(),
        new GitBlock(),
        new GoBlock(),
        new HistoryIdBlock(),
        new HostBlock(),
        new JavaBlock(),
        new KubernetesBlock(),
        new NestedPromptBlock(),
        new NetworkBlock(),
        new NodeBlock(),
        new OsBlock(),
        new PackageBlock(),
        new PowerShellBlock(),
        new ProcessMemoryBlock(),
        new PythonBlock(),
        new RubyBlock(),
        new RunspacesBlock(),
        new RustBlock(),
        new ShellBlock(),
        new StackCountBlock(),
        new StatusBlock(),
        new SystemMemoryBlock(),
        new UptimeBlock(),
        new UserBlock(),
        new VirtualEnvBlock(),
        new WeatherBlock(),
        ];
        var entries = new IBuiltInBlock[Enum.GetValues<StatusLineBlockKind>().Length];
        foreach (IBuiltInBlock block in implementations) {
            int index = (int)block.Metadata.Kind;
            if (entries[index] is not null || (block is IImmediateBlock) == (block is IRefreshingBlock)) {
                throw new InvalidOperationException("Duplicate block or invalid evaluation capability.");
            }

            entries[index] = block;
        }

        if (Array.Exists(entries, entry => entry is null)) {
            throw new InvalidOperationException("Every built-in must have an implementation.");
        }

        return entries;
    }
}
