// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.Formats.Dkon;

partial struct DkonObj
{
    public PairEnumerable AsPairs() => new(GetEnumerationStart(this.value));

    private static DkonNode? GetEnumerationStart(DkonNode? root)
    {
        if (root == null || root.IsEmpty)
            return null;

        if (root.Next is not null)
            return root;

        return root.Alt;
    }

    IEnumerator<DkonObj> IEnumerable<DkonObj>.GetEnumerator()
    {
        var enumerator = this.GetEnumerator();

        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        var enumerator = this.GetEnumerator();

        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    public Enumerator GetEnumerator() => new(GetEnumerationStart(this.value));

    public struct Enumerator : IEnumerator<DkonObj>
    {
        private DkonNode? current;
        private DkonNode? next;
        private readonly DkonNode? start;

        internal Enumerator(DkonNode? start)
        {
            this.current = null;
            this.next = start;
            this.start = start;
        }

        void IEnumerator.Reset()
        {
            this.current = null;
            this.next = this.start;
        }

        object IEnumerator.Current => this.Current;

        public DkonObj Current => this.current is null ? default : new DkonObj(this.current);

        public bool MoveNext()
        {
            if (this.next is null)
                return false;

            this.current = this.next;
            this.next = this.next.Next;

            return true;
        }

        void IDisposable.Dispose() { }
    }

    public readonly struct PairEnumerable : IEnumerable<KeyValuePair<DkonObj, DkonObj>>
    {
        private readonly DkonNode? start;

        internal PairEnumerable(DkonNode? start) => this.start = start;

        public PairEnumerator GetEnumerator() => new(this.start);

        IEnumerator<KeyValuePair<DkonObj, DkonObj>> IEnumerable<KeyValuePair<DkonObj, DkonObj>>.GetEnumerator() => new PairEnumerator(this.start);

        IEnumerator IEnumerable.GetEnumerator() => new PairEnumerator(this.start);
    }

    public struct PairEnumerator : IEnumerator<KeyValuePair<DkonObj, DkonObj>>
    {
        private DkonNode? current;
        private DkonNode? next;
        private readonly DkonNode? start;

        internal PairEnumerator(DkonNode? start)
        {
            this.current = null;
            this.next = start;
            this.start = start;
        }

        void IEnumerator.Reset()
        {
            this.current = null;
            this.next = this.start;
        }

        object IEnumerator.Current => this.Current;

        public KeyValuePair<DkonObj, DkonObj> Current => this.current is null
            ? default
            : new KeyValuePair<DkonObj, DkonObj>(new DkonObj(this.current), this.current.Ref is null ? new DkonObj() : new DkonObj(this.current.Ref));

        public bool MoveNext()
        {
            if (this.next is null)
                return false;

            this.current = this.next;
            this.next = this.next.Next;

            return true;
        }

        void IDisposable.Dispose() { }
    }
}
