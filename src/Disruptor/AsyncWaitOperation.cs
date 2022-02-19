using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Disruptor;

#if NETCOREAPP
public class AsyncWaitOperation : IValueTaskSource<SequenceWaitResult>, IThreadPoolWorkItem
#else
internal class AsyncWaitOperation : IValueTaskSource<SequenceWaitResult>
#endif
{
    private static readonly Action<object?> _completedSentinel = CompletedSentinel;
    private static readonly Action<object?> _availableSentinel = AvailableSentinel;

    private readonly CancellationToken _cancellationToken;
    private ExceptionDispatchInfo? _error;
    private Action<object?>? _continuation;
    private object? _continuationState;
    private ExecutionContext? _executionContext;
    private short _currentId;
    private long _sequence;
    private ISequence? _dependentSequence;

    public AsyncWaitOperation(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _continuation = _availableSentinel;
    }

    public ValueTask<SequenceWaitResult> ValueTask => new(this, _currentId);

    private bool IsCompleted => ReferenceEquals(_continuation, _completedSentinel);

    public SequenceWaitResult GetResult(short token)
    {
        if (_currentId != token)
        {
            ThrowIncorrectCurrentIdException();
        }

        if (!IsCompleted)
        {
            ThrowIncompleteOperationException();
        }

        var error = _error;
        _currentId++;

        Volatile.Write(ref _continuation, _availableSentinel);

        error?.Throw();

        return ComputeResult();
    }

    private SequenceWaitResult ComputeResult()
    {
        var sequence = _sequence;
        var dependentSequence = _dependentSequence!;
        var aggressiveSpinWait = new AggressiveSpinWait();
        long availableSequence;

        while ((availableSequence = dependentSequence.Value) < sequence)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            aggressiveSpinWait.SpinOnce();
        }

        return availableSequence;
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        if (_currentId != token)
        {
            ThrowIncorrectCurrentIdException();
        }

        return
            !IsCompleted ? ValueTaskSourceStatus.Pending :
            _error == null ? ValueTaskSourceStatus.Succeeded :
            _error.SourceException is OperationCanceledException ? ValueTaskSourceStatus.Canceled :
            ValueTaskSourceStatus.Faulted;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        if (_currentId != token)
        {
            ThrowIncorrectCurrentIdException();
        }

        if (_continuationState != null)
        {
            ThrowMultipleContinuations();
        }

        _continuationState = state;

        Debug.Assert(_executionContext == null);

        if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
        {
            _executionContext = ExecutionContext.Capture();
        }

        var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
        if (prevContinuation != null)
        {
            Debug.Assert(IsCompleted, "Expected IsCompleted");

            if (!ReferenceEquals(prevContinuation, _completedSentinel))
            {
                Debug.Assert(prevContinuation != _availableSentinel, "Continuation was the available sentinel.");
                ThrowMultipleContinuations();
            }

            if (_executionContext == null)
            {
                UnsafeQueueUserWorkItem(continuation, state);
            }
            else
            {
                QueueUserWorkItem(continuation, state);
            }
        }
    }

    public void SetResult()
    {
        SignalCompletion();
    }

    private void SignalCompletion()
    {
        if (_continuation != null || Interlocked.CompareExchange(ref _continuation, _completedSentinel, null) != null)
        {
            Debug.Assert(_continuation != _completedSentinel, "The continuation was the completion sentinel.");
            Debug.Assert(_continuation != _availableSentinel, "The continuation was the available sentinel.");

            UnsafeQueueSetCompletionAndInvokeContinuation();
        }
    }

    public void OwnAndReset(long sequence, ISequence dependentSequence)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _continuation, null, _availableSentinel), _availableSentinel))
        {
            ThrowTODO();
        }

        _continuationState = null;
        _error = null;
        _executionContext = null;
        _sequence = sequence;
        _dependentSequence = dependentSequence;
    }

#if NETCOREAPP
    public void Execute()
    {
        SetCompletionAndInvokeContinuation();
    }

    private void UnsafeQueueSetCompletionAndInvokeContinuation()
        => ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);

    private void UnsafeQueueUserWorkItem(Action<object?> action, object? state)
        => ThreadPool.UnsafeQueueUserWorkItem(action, state, preferLocal: false);

    private static void QueueUserWorkItem(Action<object?> action, object? state)
        => ThreadPool.QueueUserWorkItem(action, state, preferLocal: false);
#else
    private void UnsafeQueueSetCompletionAndInvokeContinuation()
        => ThreadPool.UnsafeQueueUserWorkItem(static s => ((AsyncWaitOperation)s).SetCompletionAndInvokeContinuation(), this);

    private void UnsafeQueueUserWorkItem(Action<object?> action, object? state)
        => QueueUserWorkItem(action, state);

    private static void QueueUserWorkItem(Action<object?> action, object? state)
        => Task.Factory.StartNew(action, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
#endif

    private void SetCompletionAndInvokeContinuation()
    {
        if (_executionContext == null)
        {
            var c = _continuation!;
            _continuation = _completedSentinel;
            c.Invoke(_continuationState);
        }
        else
        {
            ExecutionContext.Run(_executionContext,
                                 static s =>
                                 {
                                     var thisRef = (AsyncWaitOperation)s!;
                                     var c = thisRef._continuation!;
                                     thisRef._continuation = _completedSentinel;
                                     c.Invoke(thisRef._continuationState);
                                 },
                                 this);
        }
    }

    private static void ThrowIncorrectCurrentIdException()
        => throw new InvalidOperationException("The result of the operation was already consumed and may not be used again");

    private static void ThrowIncompleteOperationException()
        => throw new InvalidOperationException("The asynchronous operation has not completed.");

    private static void ThrowTODO()
        => throw new InvalidOperationException("TODO.");

    private static void ThrowMultipleContinuations()
        => throw new InvalidOperationException("Another continuation was already registered.");

    private static void CompletedSentinel(object? s)
    {
    }

    private static void AvailableSentinel(object? s)
    {
    }
}
