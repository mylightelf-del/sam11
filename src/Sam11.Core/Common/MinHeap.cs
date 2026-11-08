using System.Collections.Generic;

namespace Sam11.Core.Common
{
    /// <summary>우선순위 큐 (작은 값 먼저). .NET Standard 2.1에는 PriorityQueue가 없어 직접 둔다.</summary>
    public sealed class MinHeap<T>
    {
        private readonly List<(T Item, int Priority)> _items = new List<(T, int)>();

        public int Count => _items.Count;

        public void Push(T item, int priority)
        {
            _items.Add((item, priority));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_items[p].Priority <= _items[i].Priority) break;
                (_items[p], _items[i]) = (_items[i], _items[p]);
                i = p;
            }
        }

        public (T Item, int Priority) Pop()
        {
            var top = _items[0];
            var last = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            if (_items.Count > 0)
            {
                _items[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _items.Count && _items[l].Priority < _items[m].Priority) m = l;
                    if (r < _items.Count && _items[r].Priority < _items[m].Priority) m = r;
                    if (m == i) break;
                    (_items[m], _items[i]) = (_items[i], _items[m]);
                    i = m;
                }
            }
            return top;
        }
    }
}
