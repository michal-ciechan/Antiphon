using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

public interface IHerdrPaneDisposalOwnership
{
    Task<IAsyncDisposable> AcquireAsync(HerdrPaneDisposalPreview preview, CancellationToken cancellationToken);
}
