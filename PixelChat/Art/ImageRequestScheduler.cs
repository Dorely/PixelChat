using Microsoft.Extensions.Options;

namespace PixelChat.Art;

/// <summary>One app-wide limit for provider requests, including direct frame edits.</summary>
public sealed class ImageRequestScheduler(IOptions<ImageGenerationOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _requests = new(Math.Max(1, options.Value.MaxParallelRequests));
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _requests.WaitAsync(cancellationToken);
        return new Lease(_requests);
    }
    public void Dispose() => _requests.Dispose();
    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release(); }
    }
}
