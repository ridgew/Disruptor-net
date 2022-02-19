using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Disruptor.Benchmarks.WaitStrategies;

[MemoryDiagnoser]
public class PingPongAsyncWaitStrategyBenchmarksX : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly AsyncWaitStrategyX _pingWaitStrategy = new();
    private readonly AsyncWaitStrategyX _pongWaitStrategy = new();
    private readonly AsyncWaitState _pingAsyncWaitState = new();
    private readonly AsyncWaitState _pongAsyncWaitState = new();
    private readonly Sequence _pingCursor = new();
    private readonly Sequence _pongCursor = new();
    private readonly Task _pongTask;

    public PingPongAsyncWaitStrategyBenchmarksX()
    {
        _pongTask = Task.Run(RunPong);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _pongTask.Wait();
    }

    private async Task RunPong()
    {
        var sequence = -1L;

        try
        {
            while (true)
            {
                sequence++;

                await _pingWaitStrategy.WaitForAsync(sequence, _pingCursor, _pingCursor, _pingAsyncWaitState).ConfigureAwait(false);

                _pongCursor.SetValue(sequence);
                _pongWaitStrategy.SignalAllWhenBlocking();
            }
        }
        catch (OperationCanceledException e)
        {
            Console.WriteLine(e);
        }
    }

    [Benchmark(OperationsPerInvoke = 10_000_000)]
    public async Task Run()
    {
        var sequence = -1L;

        for (var i = 0; i < 10_000_000; i++)
        {
            sequence++;
            _pingCursor.SetValue(sequence);
            _pingWaitStrategy.SignalAllWhenBlocking();

            await _pongWaitStrategy.WaitForAsync(sequence, _pongCursor, _pongCursor, _pongAsyncWaitState).ConfigureAwait(false);
        }
    }
}
