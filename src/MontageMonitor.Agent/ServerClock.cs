namespace MontageMonitor.Agent;

// Event times and shift expiry must use the same clock as the server. After an
// HTTPS response, measure elapsed time monotonically so a Windows clock change
// cannot move an event before the start of its freshly confirmed shift.
internal sealed class ServerClock
{
    private readonly TimeProvider _localTime;
    private readonly object _gate = new();
    private DateTimeOffset _anchorUtc;
    private long _anchorTimestamp;

    public ServerClock(TimeProvider? localTime = null)
    {
        _localTime = localTime ?? TimeProvider.System;
        Synchronize(_localTime.GetUtcNow());
    }

    public DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _anchorUtc + _localTime.GetElapsedTime(_anchorTimestamp);
        }
    }

    public long Synchronize(DateTimeOffset serverTimeUtc)
    {
        lock (_gate)
        {
            _anchorUtc = serverTimeUtc.ToUniversalTime();
            _anchorTimestamp = _localTime.GetTimestamp();
            return (_anchorUtc - _localTime.GetUtcNow()).Ticks;
        }
    }

    public void RestoreOffset(long offsetTicks) =>
        Synchronize(_localTime.GetUtcNow().AddTicks(offsetTicks));
}
