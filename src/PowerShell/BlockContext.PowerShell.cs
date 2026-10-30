namespace StatusLine.Blocks.Execution;

internal sealed partial record BlockContext {
    public Guid RunspaceId { get; init; } = System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? Guid.Empty;
}
