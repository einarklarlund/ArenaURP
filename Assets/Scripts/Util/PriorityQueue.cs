using System;
using System.Collections.Generic;

public class PriorityQueue<T>
{
    private readonly List<(T item, int priority)> _heap = new List<(T, int)>();

    public int Count => _heap.Count;

    public void Enqueue(T item, int priority)
    {
        _heap.Add((item, priority));
        SiftUp(_heap.Count - 1);
    }

    public T Dequeue()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("Priority queue is empty.");

        T item = _heap[0].item;
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);

        if (_heap.Count > 0)
            SiftDown(0);

        return item;
    }

    public T Peek()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("Priority queue is empty.");

        return _heap[0].item;
    }

    public void Clear()
    {
        _heap.Clear();
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_heap[index].priority >= _heap[parent].priority)
                break;

            (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        int count = _heap.Count;
        while (true)
        {
            int smallest = index;
            int left = 2 * index + 1;
            int right = 2 * index + 2;

            if (left < count && _heap[left].priority < _heap[smallest].priority)
                smallest = left;
            if (right < count && _heap[right].priority < _heap[smallest].priority)
                smallest = right;

            if (smallest == index)
                break;

            (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
            index = smallest;
        }
    }
}
