using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DwsimService.Services;

/// <summary>
/// Thread-safe pool of DwsimEngine instances.
/// DWSIM is not thread-safe, so each request borrows an engine from the pool.
/// </summary>
public class DwsimEnginePool : IAsyncDisposable
{
    private readonly Channel<DwsimEngine> _pool;
    private readonly List<DwsimEngine> _engines = [];

    public DwsimEnginePool(string dllPath, int poolSize, ILogger<DwsimEngine> logger)
    {
        _pool = Channel.CreateBounded<DwsimEngine>(poolSize);

        for (var i = 0; i < poolSize; i++)
        {
            var engine = new DwsimEngine(dllPath, logger);
            _engines.Add(engine);
            _pool.Writer.TryWrite(engine);
        }
    }

    /// <summary>Borrow an engine, execute action, return engine to pool.</summary>
    public async Task<T> ExecuteAsync<T>(Func<DwsimEngine, T> action, CancellationToken ct = default)
    {
        var engine = await _pool.Reader.ReadAsync(ct);
        try
        {
            return action(engine);
        }
        finally
        {
            await _pool.Writer.WriteAsync(engine, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pool.Writer.Complete();
        foreach (var engine in _engines)
            engine.Dispose();
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }
}
