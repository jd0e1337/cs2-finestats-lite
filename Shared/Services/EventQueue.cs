using System.Threading.Channels;
using Finestats.Events;

namespace Finestats.Services;

public sealed class EventQueue
{
    private readonly Channel<StatsEvent> _channel;
    private long _dropped;
    private long _accepted;
    public ChannelReader<StatsEvent> Reader => _channel.Reader;
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Accepted => Interlocked.Read(ref _accepted);
    public int Count => _channel.Reader.Count;

    public EventQueue(int capacity)
    {
        _channel = Channel.CreateBounded<StatsEvent>(new BoundedChannelOptions(capacity)
        {
            // Wait mode + TryWrite gives an observable drop-new policy WITHOUT ever waiting.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryEnqueue(StatsEvent value)
    {
        if (_channel.Writer.TryWrite(value)) { Interlocked.Increment(ref _accepted); return true; }
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public void Complete() => _channel.Writer.TryComplete();
}
