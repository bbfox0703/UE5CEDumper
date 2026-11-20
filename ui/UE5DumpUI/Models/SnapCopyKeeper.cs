using System.Runtime.CompilerServices;

namespace UE5DumpUI.Models;

/// <summary>
/// [LIVEFUNCS-UI-SNAP-MEMORY] What one Call Trace load keeps of the parameter copies it reads. The DLL decodes every
/// copy, and a large struct parameter decodes to hundreds of values per copy, about 27 times its raw bytes. Most of them
/// recur from call to call (a camera's post-process settings rarely change), so each decoded value, each list of values
/// and each raw copy is held once and shared by every copy that has it. What is still new is counted against
/// <see cref="DecodedBudgetBytes"/>; past it, the earliest calls (by entry sequence) let their decoded values go and keep
/// their raw bytes. The count is an estimate: a value shared with a later call counts to the call that brought it first,
/// so letting that call go frees less than its count. One keeper per load: its lookup tables go with it, the slots stay.
/// </summary>
public sealed class SnapCopyKeeper
{
    // What one value, one array and one string cost the 64-bit heap, for the count.
    private const int ValueBytes = 40;
    private const int ArrayBytes = 24;
    private const int RefBytes = 8;
    private const int StringBytes = 22;

    private readonly List<SnapSlot> _slots = new();
    private readonly List<long> _cost = new();
    private readonly PriorityQueue<int, ulong> _byAge = new();
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<ValueKey, SnapValue> _values = new();
    private readonly Dictionary<ListKey, SnapValue[]> _lists = new();
    private readonly Dictionary<BytesKey, byte[]> _bytes = new();

    public SnapCopyKeeper(long decodedBudgetBytes)
    {
        DecodedBudgetBytes = decodedBudgetBytes;
    }

    public long DecodedBudgetBytes { get; }
    /// <summary>In the order they were added; a slot past the budget is replaced by one without values.</summary>
    public IReadOnlyList<SnapSlot> Slots => _slots;
    /// <summary>The decoded values held, after sharing, as counted.</summary>
    public long DecodedBytes { get; private set; }
    /// <summary>The distinct raw copies held.</summary>
    public long RawBytes { get; private set; }
    /// <summary>Slots whose decoded values were let go.</summary>
    public int Dropped { get; private set; }

    public void Add(SnapSlot slot)
    {
        long cost = 0;
        var values = slot.Values == null ? null : InternList(slot.Values, ref cost);
        int index = _slots.Count;
        _slots.Add(Copy(slot, InternBytes(slot.Data), values, dropped: false));
        _cost.Add(cost);
        if (values == null) return;
        DecodedBytes += cost;
        _byAge.Enqueue(index, slot.EntrySeq);
        while (DecodedBytes > DecodedBudgetBytes && _byAge.TryDequeue(out int oldest, out _))
        {
            DecodedBytes -= _cost[oldest];
            _slots[oldest] = Copy(_slots[oldest], _slots[oldest].Data, null, dropped: true);
            Dropped++;
        }
    }

    private static SnapSlot Copy(SnapSlot s, byte[] data, IReadOnlyList<SnapValue>? values, bool dropped) => new()
    {
        Index = s.Index, EntrySeq = s.EntrySeq, IsAfter = s.IsAfter, Len = s.Len, Flags = s.Flags, Arm = s.Arm,
        Data = data, Values = values, RawOnly = s.RawOnly, ValuesDropped = dropped,
    };

    // Bottom up: a list is shared once its values are, so two lists of the same values are the same array.
    private SnapValue[] InternList(IReadOnlyList<SnapValue> list, ref long cost)
    {
        if (list.Count == 0) return Array.Empty<SnapValue>();
        var items = new SnapValue[list.Count];
        for (int i = 0; i < items.Length; i++) items[i] = InternValue(list[i], ref cost);
        var key = new ListKey(items);
        if (_lists.TryGetValue(key, out var shared)) return shared;
        _lists.Add(key, items);
        cost += ArrayBytes + RefBytes * (long)items.Length;
        return items;
    }

    private SnapValue InternValue(SnapValue v, ref long cost)
    {
        var sub = InternList(v.Sub, ref cost);
        var text = InternText(v.Text, ref cost);
        var key = new ValueKey(text, v.Mark, sub);
        if (_values.TryGetValue(key, out var shared)) return shared;
        var made = new SnapValue { Text = text, Mark = v.Mark, Sub = sub };
        _values.Add(key, made);
        cost += ValueBytes;
        return made;
    }

    private string InternText(string s, ref long cost)
    {
        if (s.Length == 0) return "";
        if (_texts.TryGetValue(s, out var shared)) return shared;
        _texts.Add(s, s);
        cost += StringBytes + 2L * s.Length;
        return s;
    }

    private byte[] InternBytes(byte[] b)
    {
        if (b.Length == 0) return Array.Empty<byte>();
        var key = new BytesKey(b);
        if (_bytes.TryGetValue(key, out var shared)) return shared;
        _bytes.Add(key, b);
        RawBytes += ArrayBytes + b.Length;
        return b;
    }

    // A value is its text, its mark and its (already shared) members, compared by identity.
    private readonly struct ValueKey : IEquatable<ValueKey>
    {
        private readonly string _text;
        private readonly SnapMark _mark;
        private readonly SnapValue[] _sub;

        public ValueKey(string text, SnapMark mark, SnapValue[] sub)
        {
            _text = text;
            _mark = mark;
            _sub = sub;
        }

        public bool Equals(ValueKey o) =>
            _mark == o._mark && ReferenceEquals(_sub, o._sub) && string.Equals(_text, o._text, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ValueKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(_text, (int)_mark, RuntimeHelpers.GetHashCode(_sub));
    }

    // A list is its (already shared) values, in order, compared by identity.
    private readonly struct ListKey : IEquatable<ListKey>
    {
        private readonly SnapValue[] _items;
        private readonly int _hash;

        public ListKey(SnapValue[] items)
        {
            _items = items;
            var h = new HashCode();
            foreach (var v in items) h.Add(RuntimeHelpers.GetHashCode(v));
            _hash = h.ToHashCode();
        }

        public bool Equals(ListKey o)
        {
            if (_hash != o._hash || _items.Length != o._items.Length) return false;
            for (int i = 0; i < _items.Length; i++)
                if (!ReferenceEquals(_items[i], o._items[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ListKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }

    // A raw copy is its bytes.
    private readonly struct BytesKey : IEquatable<BytesKey>
    {
        private readonly byte[] _bytes;
        private readonly int _hash;

        public BytesKey(byte[] bytes)
        {
            _bytes = bytes;
            var h = new HashCode();
            h.AddBytes(bytes);
            _hash = h.ToHashCode();
        }

        public bool Equals(BytesKey o) => _hash == o._hash && _bytes.AsSpan().SequenceEqual(o._bytes);
        public override bool Equals(object? obj) => obj is BytesKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }
}
