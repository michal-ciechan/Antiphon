namespace Antiphon.Agents.Pty;

public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
    }

    public T[] Snapshot()
    {
        lock (_lock)
        {
            var result = new T[_count];
            int start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }
            return result;
        }
    }
}
