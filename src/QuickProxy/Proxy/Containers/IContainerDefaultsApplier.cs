namespace QuickProxy.Proxy.Containers;

public interface IContainerDefaultsApplier
{
    bool ApplyToRequest(ContainerEditRequest request);
    Task<int> ApplyForDefaultsSetAsync(string defaultsSetId, CancellationToken cancellationToken);

    Task<ContainerDefaultsApplyResult> ApplyForStartAsync(string containerName, bool startAfterApply,
        CancellationToken cancellationToken);
}

public sealed record ContainerDefaultsApplyResult(bool Applied, bool StartedByApply);