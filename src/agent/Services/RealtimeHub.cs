using System.Collections.Concurrent;
using System.Threading.Channels;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Services;

public sealed class RealtimeHub
{
    private readonly ConcurrentDictionary<Guid, Channel<RealtimeEnvelope>> _clients = new();

    public (Guid Id, ChannelReader<RealtimeEnvelope> Reader) Subscribe(int capacity = 8)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RealtimeEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _clients[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_clients.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(RealtimeEnvelope envelope)
    {
        foreach (var channel in _clients.Values) channel.Writer.TryWrite(envelope);
    }
}