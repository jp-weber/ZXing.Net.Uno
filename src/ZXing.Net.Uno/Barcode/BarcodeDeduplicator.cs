namespace ZXing.Net.Uno;

public sealed class BarcodeDeduplicator
{
    private readonly object _syncRoot = new();
    private readonly TimeSpan _deduplicationPeriod;
    private readonly Dictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);

    public BarcodeDeduplicator(TimeSpan? deduplicationPeriod = null)
    {
        _deduplicationPeriod = deduplicationPeriod ?? TimeSpan.FromSeconds(2);
        if (_deduplicationPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deduplicationPeriod));
        }
    }

    public bool IsDuplicate(string? barcodeValue)
    {
        if (string.IsNullOrWhiteSpace(barcodeValue))
        {
            return true;
        }

        var now = DateTime.UtcNow;
        lock (_syncRoot)
        {
            if (_lastSeen.TryGetValue(barcodeValue, out var lastSeen)
                && (now - lastSeen) < _deduplicationPeriod)
            {
                return true;
            }

            _lastSeen[barcodeValue] = now;
            return false;
        }
    }

    public BarcodeResult[] FilterNewResults(BarcodeResult[]? results)
    {
        if (results is null || results.Length == 0)
        {
            return [];
        }

        var filtered = new List<BarcodeResult>();
        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            if (!IsDuplicate(result.Value))
            {
                filtered.Add(result);
            }
        }

        return [.. filtered];
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _lastSeen.Clear();
        }
    }
}
