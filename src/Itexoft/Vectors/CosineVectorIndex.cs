// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#region

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;

#endregion

namespace Itexoft.Vectors;

public sealed class CosineVectorIndex(int dimension)
{
    private static readonly IComparer<(string Value, float Score)> scoreDescendingComparer =
        Comparer<(string Value, float Score)>.Create(static (left, right) => right.Score.CompareTo(left.Score));

    private readonly int dimension = dimension.RequiredPositive();

    private Node? head;

    public void Insert(float[] vector, string value) => this.InsertInternal(vector.RequiredNotEmpty().AsSpan().ToArray(), value.RequiredNotEmpty());

    public void Insert(ReadOnlyMemory<float> vector, string value) =>
        this.InsertInternal(vector.RequiredNotEmpty().ToArray(), value.RequiredNotEmpty());

    public void Insert(ReadOnlySpan<float> vector, string value) =>
        this.InsertInternal(vector.RequiredNotEmpty().ToArray(), value.RequiredNotEmpty());

    public bool Remove(string value)
    {
        value.RequiredNotEmpty();

        while (true)
        {
            var snapshot = Atomic.Read(ref this.head);

            if (snapshot is null)
                return false;

            if (!TryRemove(snapshot, value, out var updatedHead))
                return false;

            if (Interlocked.CompareExchange(ref this.head, updatedHead, snapshot) == snapshot)
                return true;
        }
    }

    public string? Select(ReadOnlySpan<float> query)
    {
        var snapshot = this.PrepareQuery(query, out var queryNorm);

        if (snapshot is null)
            return null;

        return TrySelectSingle(snapshot, query, queryNorm, false, out var selected) ? selected : null;
    }

    public IEnumerable<string> Rank(ReadOnlySpan<float> query)
    {
        var snapshot = this.PrepareQuery(query, out var queryNorm);

        if (snapshot is null)
            return [];

        var count = 0;

        for (var cursor = snapshot; cursor is not null; cursor = cursor.next)
        {
            if (!IsExactVectorMatch(query, cursor.vector.Span))
                count++;
        }

        if (count == 0)
            return [];

        var ranked = new (string Value, float Score)[count];
        var index = 0;

        for (var cursor = snapshot; cursor is not null; cursor = cursor.next)
        {
            if (!IsExactVectorMatch(query, cursor.vector.Span))
                ranked[index++] = (cursor.value, VectorMath.Dot(query, cursor.vector.Span) / (queryNorm * cursor.norm));
        }

        Array.Sort(ranked, scoreDescendingComparer);

        var result = new string[count];

        for (var i = 0; i < count; i++)
            result[i] = ranked[i].Value;

        return result;
    }

    public IEnumerable<string> Rank(ReadOnlyMemory<float> query, int topK, bool includeExactMatch = false)
    {
        topK.RequiredPositive();

        var snapshot = this.PrepareQuery(query.Span, out var queryNorm);

        if (snapshot is null)
            yield break;

        if (topK == 1)
        {
            if (TrySelectSingle(snapshot, query.Span, queryNorm, includeExactMatch, out var selected))
                yield return selected;

            yield break;
        }

        var heap = new (string Value, float Score)[topK];
        var count = 0;

        for (var cursor = snapshot; cursor is not null; cursor = cursor.next)
        {
            if (ShouldSkipVector(query.Span, cursor.vector.Span, includeExactMatch))
                continue;

            var item = (cursor.value, VectorMath.Dot(query.Span, cursor.vector.Span) / (queryNorm * cursor.norm));

            if (count < topK)
                HeapPushMin(heap, ref count, item);
            else if (item.Item2 > heap[0].Score)
                HeapReplaceMin(heap, count, item);
        }

        if (count == 0)
            yield break;

        Array.Sort(heap, 0, count, scoreDescendingComparer);

        for (var i = 0; i < count; i++)
            yield return heap[i].Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertInternal(ReadOnlyMemory<float> vector, string value)
    {
        if (this.dimension != vector.Length)
            throw new ArgumentException($"Vector length mismatch. Expected {this.dimension}, got {vector.Length}.", nameof(vector));

        var norm = VectorMath.NormL2(vector.Span);

        if (norm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", nameof(vector));

        while (true)
        {
            var currentHead = Atomic.Read(ref this.head);
            var node = new Node(vector, value, norm, currentHead);

            if (Interlocked.CompareExchange(ref this.head, node, currentHead) == currentHead)
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Node? PrepareQuery(ReadOnlySpan<float> query, out float queryNorm)
    {
        query.RequiredNotEmpty();

        if (query.Length != this.dimension)
            throw new ArgumentException($"Vector length mismatch. Expected {this.dimension}, got {query.Length}.", nameof(query));

        queryNorm = VectorMath.NormL2(query);

        if (queryNorm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", nameof(query));

        return Atomic.Read(ref this.head);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySelectSingle(Node snapshot, ReadOnlySpan<float> query, float queryNorm, bool includeExactMatch, out string selectedValue)
    {
        selectedValue = string.Empty;
        var selectedScore = 0f;
        var found = false;

        for (var cursor = snapshot; cursor is not null; cursor = cursor.next)
        {
            if (ShouldSkipVector(query, cursor.vector.Span, includeExactMatch))
                continue;

            var score = VectorMath.Dot(query, cursor.vector.Span) / (queryNorm * cursor.norm);

            if (!found || score > selectedScore)
            {
                found = true;
                selectedScore = score;
                selectedValue = cursor.value;
            }
        }

        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExactVectorMatch(ReadOnlySpan<float> query, ReadOnlySpan<float> indexed) => query.SequenceEqual(indexed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldSkipVector(ReadOnlySpan<float> query, ReadOnlySpan<float> indexed, bool includeExactMatch) =>
        !includeExactMatch && IsExactVectorMatch(query, indexed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryRemove(Node snapshot, string value, out Node? updatedHead)
    {
        Node? reversedPrefix = null;

        for (var cursor = snapshot; cursor is not null; cursor = cursor.next)
        {
            if (string.Equals(cursor.value, value, StringComparison.Ordinal))
            {
                updatedHead = cursor.next;

                for (var prefixCursor = reversedPrefix; prefixCursor is not null; prefixCursor = prefixCursor.next)
                    updatedHead = new Node(prefixCursor.vector, prefixCursor.value, prefixCursor.norm, updatedHead);

                return true;
            }

            reversedPrefix = new Node(cursor.vector, cursor.value, cursor.norm, reversedPrefix);
        }

        updatedHead = null;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HeapPushMin((string Value, float Score)[] heap, ref int count, (string Value, float Score) value)
    {
        var index = count;
        heap[index] = value;
        count++;

        while (index > 0)
        {
            var parent = (index - 1) >> 1;

            if (heap[parent].Score <= heap[index].Score)
                return;

            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HeapReplaceMin((string Value, float Score)[] heap, int count, (string Value, float Score) value)
    {
        heap[0] = value;

        var index = 0;

        while (true)
        {
            var left = (index << 1) + 1;

            if (left >= count)
                return;

            var right = left + 1;
            var smallest = right < count && heap[right].Score < heap[left].Score ? right : left;

            if (heap[index].Score <= heap[smallest].Score)
                return;

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private sealed class Node
    {
        internal readonly Node? next;
        internal readonly float norm;
        internal readonly string value;
        internal readonly ReadOnlyMemory<float> vector;

        internal Node(ReadOnlyMemory<float> vector, string value, float norm, Node? next)
        {
            this.vector = vector;
            this.value = value;
            this.norm = norm;
            this.next = next;
        }
    }
}
