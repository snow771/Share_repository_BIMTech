// このファイルは、選択・検索の古い非同期要求を取り消し、最新結果だけを採用する所有管理です。
// CancellationTokenSourceに依存し、変更は取消とDisposeの競合安全性に影響するため、必ずストレステストを併用します。
namespace Bui.Common;

public sealed class LatestRequestCoordinator : IDisposable
{
    private readonly object _gate = new();
    private RequestLease? _current;
    private long _version;
    private bool _disposed;

    public RequestLease Begin(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var lease = new RequestLease(this, source, ++_version);
            var previous = _current;
            _current = lease;
            // CancelとDisposeは同じロック下で実行されるため、破棄済みSourceを触りません。
            previous?.CancelWhileOwned();
            return lease;
        }
    }

    public void CancelCurrent()
    {
        lock (_gate) _current?.CancelWhileOwned();
    }

    internal bool IsLatest(RequestLease lease)
    {
        lock (_gate) return ReferenceEquals(_current, lease) && !lease.IsDisposed;
    }

    internal void Release(RequestLease lease)
    {
        lock (_gate)
        {
            if (lease.IsDisposed) return;
            if (ReferenceEquals(_current, lease)) _current = null;
            lease.DisposeWhileOwned();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _current?.CancelWhileOwned();
        }
    }

    public sealed class RequestLease : IDisposable
    {
        private readonly LatestRequestCoordinator _owner;
        private readonly CancellationTokenSource _source;

        internal RequestLease(LatestRequestCoordinator owner, CancellationTokenSource source, long version)
        { _owner = owner; _source = source; Version = version; }

        public long Version { get; }
        public CancellationToken Token => _source.Token;
        public bool IsLatest => _owner.IsLatest(this);
        internal bool IsDisposed { get; private set; }
        internal void CancelWhileOwned() { if (!IsDisposed) _source.Cancel(); }
        internal void DisposeWhileOwned() { if (!IsDisposed) { IsDisposed = true; _source.Dispose(); } }
        public void Dispose() => _owner.Release(this);
    }
}
