// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading.Tasks;

namespace Itexoft.Threading;

public readonly partial struct CancelToken
{
    public static readonly CancelToken None = default;

    public bool IsNone => this.source.IsNull;

    public bool IsRequested => this.source.IsRequested;

    public bool IsTimedOut => this.source.IsTimedOut;

    public bool Cancel() => this.source.Cancel();

    public bool Register(Action action)
    {
        action.Required();

        if (this.source.IsNull)
            return false;

        if (this.IsRequested)
        {
            action();

            return true;
        }

        this.source.Register(action);

        return true;
    }

    public IDisposable? Bridge(out CancellationToken cancellationToken)
    {
        if (this.source.IsNull)
        {
            cancellationToken = CancellationToken.None;

            return null;
        }

        return this.source.Bridge(out cancellationToken);
    }

    public CancelToken ThrowIf(
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) => new(this.source.ThrowIf(callerMember, callerFile, callerLine));

    public CancelToken Branch() => new(this.source.Branch());
    public CancelToken Branch(TimeSpan timeout) => new(this.source.Branch(timeout));

    public override bool Equals(object? obj) => obj is CancelToken other && this.source.Equals(other.source);

    public override int GetHashCode() => this.source.GetHashCode();

    public static bool operator ==(CancelToken left, CancelToken right) => left.source.Equals(right.source);

    public static bool operator !=(CancelToken left, CancelToken right) => !left.source.Equals(right.source);

    public static implicit operator CancelToken(CancellationToken cancellationToken) => new(cancellationToken);

    public override string ToString() => this.source.IsRequested.ToString();

    public static implicit operator ValuePromise(in CancelToken cancelToken) => cancelToken.source.GetAwaiter();
    public static implicit operator PromiseAwaiter(in CancelToken cancelToken) => cancelToken.source.GetAwaiter();
}
