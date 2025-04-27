// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Text;
using Itexoft.AI.Api.OpenAI.Models;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO.Streams.Chars;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI;

internal sealed class OpenAiSseEnumerable(NetHttpResponse response, CancelToken cancelToken) : IEnumerable<OpenAiChatCompletionResponseDelta>
{
    private readonly CancelToken cancelToken = cancelToken;
    private readonly NetHttpResponse response = response.Required();
    private Latch consumed = new();

    public IEnumerator<OpenAiChatCompletionResponseDelta> GetEnumerator()
    {
        if (!this.consumed.Try())
            throw new InvalidOperationException("OpenAI SSE response stream can be consumed only once.");

        using (var enumerator = new Enumerator(this.response, this.cancelToken))
        {
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private struct Enumerator : IEnumerator<OpenAiChatCompletionResponseDelta>
    {
        private readonly CancelToken cancelToken;
        private readonly OpenAiSseLineReader lineReader;
        private readonly NetHttpResponse response;
        private Disposed disposed = new();

        internal Enumerator(NetHttpResponse response, CancelToken ownerToken)
        {
            this.response = response.Required();
            this.cancelToken = ownerToken.Branch();
            this.lineReader = new OpenAiSseLineReader(new CharStreamBr(this.response.Body.Required(), Encoding.UTF8));
        }

        public void Reset() => throw new NotSupportedException();

        object IEnumerator.Current => this.Current;
        public OpenAiChatCompletionResponseDelta Current { get; private set; } = default;

        public bool MoveNext()
        {
            this.disposed.ThrowIf();

            while (true)
            {
                this.cancelToken.ThrowIf();
                var line = this.lineReader.ReadLine(this.cancelToken);

                if (line is null)
                    return false;

                if (line.Length == 0)
                    continue;

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid OpenAI SSE line: '{line}'.");

                var payload = line.AsSpan(5).TrimStart();

                if (payload.Length == 0)
                    continue;

                if (payload.SequenceEqual("[DONE]".AsSpan()))
                    return false;

                this.Current = OpenAiJson.Deserialize<OpenAiChatCompletionResponseDelta>(payload.ToString());

                return true;
            }
        }

        public void Dispose()
        {
            if (this.disposed.Enter())
                return;

            this.lineReader.Dispose();
        }
    }
}
