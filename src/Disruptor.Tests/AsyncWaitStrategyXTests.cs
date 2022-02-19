using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Disruptor.Tests;

public class AsyncWaitStrategyXTests : WaitStrategyFixture<AsyncWaitStrategyX>
{
    protected override AsyncWaitStrategyX CreateWaitStrategy()
    {
        return new AsyncWaitStrategyX();
    }

    [Test]
    public void ShouldWaitFromMultipleThreadsAsync()
    {
        // Arrange
        var waitStrategy = CreateWaitStrategy();
        var waitResult1 = new TaskCompletionSource<SequenceWaitResult>();
        var waitResult2 = new TaskCompletionSource<SequenceWaitResult>();

        var dependentSequence1 = Cursor;
        var dependentSequence2 = new Sequence();

        var asyncWaitState1 = new AsyncWaitState(CancellationTokenSource);
        var waitTask1 = Task.Run(async () =>
        {
            waitResult1.SetResult(await waitStrategy.WaitForAsync(10, Cursor, dependentSequence1, asyncWaitState1));
            Thread.Sleep(1);
            dependentSequence2.SetValue(10);
        });

        var asyncWaitState2 = new AsyncWaitState(CancellationTokenSource);
        var waitTask2 = Task.Run(async () => waitResult2.SetResult(await waitStrategy.WaitForAsync(10, Cursor, dependentSequence2, asyncWaitState2)));

        // Ensure waiting tasks are blocked
        AssertIsNotCompleted(waitResult1.Task);
        AssertIsNotCompleted(waitResult2.Task);

        // Act
        Cursor.SetValue(10);
        waitStrategy.SignalAllWhenBlocking();

        // Assert
        AssertHasResult(waitResult1.Task, new SequenceWaitResult(10));
        AssertHasResult(waitResult2.Task, new SequenceWaitResult(10));
        AssertIsCompleted(waitTask1);
        AssertIsCompleted(waitTask2);
    }

    [Test]
    public void ShouldWaitFromMultipleThreadsSyncAndAsync()
    {
        // Arrange
        var waitStrategy = CreateWaitStrategy();
        var waitResult1 = new TaskCompletionSource<SequenceWaitResult>();
        var waitResult2 = new TaskCompletionSource<SequenceWaitResult>();
        var waitResult3 = new TaskCompletionSource<SequenceWaitResult>();

        var dependentSequence1 = Cursor;
        var dependentSequence2 = new Sequence();
        var dependentSequence3 = new Sequence();

        var waitTask1 = Task.Run(() =>
        {
            waitResult1.SetResult(waitStrategy.WaitFor(10, Cursor, dependentSequence1, CancellationToken));
            Thread.Sleep(1);
            dependentSequence2.SetValue(10);
        });

        var asyncWaitState2 = new AsyncWaitState(CancellationTokenSource);
        var waitTask2 = Task.Run(async () =>
        {
            waitResult2.SetResult(await waitStrategy.WaitForAsync(10, Cursor, dependentSequence2, asyncWaitState2));
            Thread.Sleep(1);
            dependentSequence3.SetValue(10);
        });

        var asyncWaitState3 = new AsyncWaitState(CancellationTokenSource);
        var waitTask3 = Task.Run(async () => waitResult3.SetResult(await waitStrategy.WaitForAsync(10, Cursor, dependentSequence3, asyncWaitState3)));

        // Ensure waiting tasks are blocked
        AssertIsNotCompleted(waitResult1.Task);
        AssertIsNotCompleted(waitResult2.Task);
        AssertIsNotCompleted(waitResult3.Task);

        // Act
        Cursor.SetValue(10);
        waitStrategy.SignalAllWhenBlocking();

        // Assert WaitFor is unblocked
        AssertHasResult(waitResult1.Task, new SequenceWaitResult(10));
        AssertHasResult(waitResult2.Task, new SequenceWaitResult(10));
        AssertHasResult(waitResult3.Task, new SequenceWaitResult(10));
        AssertIsCompleted(waitTask1);
        AssertIsCompleted(waitTask2);
        AssertIsCompleted(waitTask3);
    }

    [Test]
    public void ShouldUnblockAfterCancellationAsync()
    {
        // Arrange
        var waitStrategy = CreateWaitStrategy();
        var dependentSequence = new Sequence();
        var waitResult = new TaskCompletionSource<Exception>();
        var asyncWaitState = new AsyncWaitState(CancellationTokenSource);

        var waitTask = Task.Run(async () =>
        {
            try
            {
                await waitStrategy.WaitForAsync(10, Cursor, dependentSequence, asyncWaitState);
            }
            catch (Exception e)
            {
                waitResult.SetResult(e);
            }
        });

        // Ensure waiting tasks are blocked
        AssertIsNotCompleted(waitTask);

        // Act
        CancellationTokenSource.Cancel();
        waitStrategy.SignalAllWhenBlocking();

        // Assert
        AssertIsCompleted(waitResult.Task);
        Assert.That(waitResult.Task.Result, Is.InstanceOf<OperationCanceledException>());
        AssertIsCompleted(waitTask);
    }

    [Test]
    public void ShouldWaitMultipleTimes_1()
    {
        // Arrange
        var waitStrategy = CreateWaitStrategy();

        var waitTask = Task.Run(async () =>
        {
            var asyncWaitState = new AsyncWaitState(CancellationTokenSource);
            var dependentSequence = Cursor;

            for (var i = 0; i < 500; i++)
            {
                await waitStrategy.WaitForAsync(i, Cursor, dependentSequence, asyncWaitState).ConfigureAwait(false);
            }
        });

        // Act
        for (var i = 0; i < 500; i++)
        {
            if (i % 50 == 0)
                Thread.Sleep(1);

            Cursor.SetValue(i);
            waitStrategy.SignalAllWhenBlocking();
        }

        // Assert
        AssertIsCompleted(waitTask);
    }

    [Test]
    public void ShouldWaitMultipleTimes_2()
    {
        // Arrange
        var waitStrategy = CreateWaitStrategy();
        var sequence1 = new Sequence();

        var waitTask1 = Task.Run(async () =>
        {
            var asyncWaitState = new AsyncWaitState(CancellationTokenSource);
            var dependentSequence = Cursor;

            for (var i = 0; i < 500; i++)
            {
                await waitStrategy.WaitForAsync(i, Cursor, dependentSequence, asyncWaitState).ConfigureAwait(false);
                sequence1.SetValue(i);
            }
        });

        var waitTask2 = Task.Run(async () =>
        {
            var asyncWaitState = new AsyncWaitState(CancellationTokenSource);
            var dependentSequence = sequence1;

            for (var i = 0; i < 500; i++)
            {
                await waitStrategy.WaitForAsync(i, Cursor, dependentSequence, asyncWaitState).ConfigureAwait(false);
            }
        });

        // Act
        for (var i = 0; i < 500; i++)
        {
            if (i % 50 == 0)
                Thread.Sleep(1);

            Cursor.SetValue(i);
            waitStrategy.SignalAllWhenBlocking();
        }

        // Assert
        AssertIsCompleted(waitTask1);
        AssertIsCompleted(waitTask2);
    }
}
