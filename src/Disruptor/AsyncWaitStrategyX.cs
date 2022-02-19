using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Disruptor;

/// <summary>
/// Blocking wait strategy that uses <c>Monitor.Wait</c> for event processors waiting on a barrier.
///
/// Can be configured to generate timeouts. Activating timeouts is only useful if your event handler
/// handles timeouts (<see cref="IAsyncBatchEventHandler{T}.OnTimeout"/>).
/// </summary>
/// <remarks>
/// This strategy can be used when throughput and low-latency are not as important as CPU resource.
/// </remarks>
public sealed class AsyncWaitStrategyX : IWaitStrategy
{
    private readonly List<AsyncWaitState> _asyncWaitCoordinators = new();
    private readonly object _gate = new();
    private readonly int _timeoutMilliseconds;
    private bool _hasSyncWaiter;

    /// <summary>
    /// Creates an async wait strategy without timeouts.
    /// </summary>
    public AsyncWaitStrategyX()
        : this(Timeout.Infinite)
    {
    }

    /// <summary>
    /// Creates an async wait strategy with timeouts.
    /// </summary>
    public AsyncWaitStrategyX(TimeSpan timeout)
        : this(ToMilliseconds(timeout))
    {
    }

    private AsyncWaitStrategyX(int timeoutMilliseconds)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public bool IsBlockingStrategy => true;

    public SequenceWaitResult WaitFor(long sequence, Sequence cursor, ISequence dependentSequence, CancellationToken cancellationToken)
    {
        var timeout = _timeoutMilliseconds;
        if (cursor.Value < sequence)
        {
            lock (_gate)
            {
                _hasSyncWaiter = true;
                while (cursor.Value < sequence)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var waitSucceeded = Monitor.Wait(_gate, timeout);
                    if (!waitSucceeded)
                    {
                        return SequenceWaitResult.Timeout;
                    }
                }
            }
        }

        var aggressiveSpinWait = new AggressiveSpinWait();
        long availableSequence;
        while ((availableSequence = dependentSequence.Value) < sequence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            aggressiveSpinWait.SpinOnce();
        }

        return availableSequence;
    }

    public void SignalAllWhenBlocking()
    {
        lock (_gate)
        {
            if (_hasSyncWaiter)
            {
                Monitor.PulseAll(_gate);
            }

            foreach (var coordinator in _asyncWaitCoordinators)
            {
                coordinator.Notify();
            }
            _asyncWaitCoordinators.Clear();
        }
    }

    public ValueTask<SequenceWaitResult> WaitForAsync(long sequence, Sequence cursor, ISequence dependentSequence, AsyncWaitState asyncWaitToken)
    {
        while (cursor.Value < sequence)
        {
            lock (_gate)
            {
                if (cursor.Value >= sequence)
                    break;

                _asyncWaitCoordinators.Add(asyncWaitToken);

                return asyncWaitToken.Wait(sequence, dependentSequence);
            }
        }

        var aggressiveSpinWait = new AggressiveSpinWait();
        long availableSequence;
        while ((availableSequence = dependentSequence.Value) < sequence)
        {
            asyncWaitToken.ThrowIfCancellationRequested();
            aggressiveSpinWait.SpinOnce();
        }

        return new ValueTask<SequenceWaitResult>(availableSequence);
    }

    private Task AddTimeout(Task task)
    {
        if (_timeoutMilliseconds == Timeout.Infinite)
        {
            return task;
        }

        return Task.WhenAny(task, Task.Delay(_timeoutMilliseconds));
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        if (totalMilliseconds <= 0 || totalMilliseconds >= int.MaxValue)
        {
            throw new ArgumentOutOfRangeException();
        }

        return (int)totalMilliseconds;
    }
}
