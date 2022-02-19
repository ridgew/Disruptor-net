using System.Threading;
using System.Threading.Tasks;

namespace Disruptor;

public class AsyncWaitState
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly AsyncWaitOperation _asyncWaitOperation;

    public AsyncWaitState()
        : this(new CancellationTokenSource())
    {
    }

    public AsyncWaitState(CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _asyncWaitOperation = new(cancellationTokenSource.Token);
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public void ThrowIfCancellationRequested()
    {
        CancellationToken.ThrowIfCancellationRequested();
    }

    public void Notify()
    {
        _asyncWaitOperation.SetResult();
    }

    public ValueTask<SequenceWaitResult> Wait(long sequence, ISequence dependentSequence)
    {
        _asyncWaitOperation.OwnAndReset(sequence, dependentSequence);

        return _asyncWaitOperation.ValueTask;
    }
}
