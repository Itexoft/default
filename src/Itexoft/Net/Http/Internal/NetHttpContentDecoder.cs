// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.IO.Compression;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpContentDecoder
{
    public static IStreamR<byte> Apply(NetHttpHeaders headers, IStreamR<byte> body)
    {
        if (body is NetHttpEmptyBodyStream)
            return body;

        if (!headers.TryGetValue("Content-Encoding", out var encodingValue) || string.IsNullOrWhiteSpace(encodingValue))
            return body;

        var encodings = ParseEncodings(encodingValue.AsSpan());

        if (encodings is null || encodings.Count == 0)
            return body;

        for (var i = encodings.Count - 1; i >= 0; i--)
            body = Wrap(body, encodings[i]);

        return body;
    }

    private static List<NetHttpContentEncoding>? ParseEncodings(ReadOnlySpan<char> value)
    {
        List<NetHttpContentEncoding>? result = null;
        var span = value;

        while (true)
        {
            span = NetHttpParsing.TrimOws(span);

            if (span.IsEmpty)
                break;

            var comma = span.IndexOf(',');
            var part = comma >= 0 ? span[..comma] : span;
            part = NetHttpParsing.TrimOws(part);

            var semi = part.IndexOf(';');

            if (semi >= 0)
                part = NetHttpParsing.TrimOws(part[..semi]);

            if (IsToken(part, "gzip") || IsToken(part, "x-gzip"))
                add(NetHttpContentEncoding.Gzip);
            else if (IsToken(part, "deflate"))
                add(NetHttpContentEncoding.Deflate);
            else if (IsToken(part, "br"))
                add(NetHttpContentEncoding.Brotli);

            if (comma < 0)
                break;

            span = span[(comma + 1)..];
        }

        return result;

        void add(NetHttpContentEncoding encoding)
        {
            result ??= [];
            result.Add(encoding);
        }
    }

    private static IStreamR<byte> Wrap(IStreamR<byte> body, NetHttpContentEncoding encoding) => encoding switch
    {
        NetHttpContentEncoding.Gzip => new GZipStream(body.AsStream(), CompressionMode.Decompress, false).AsAstreamR(),
        NetHttpContentEncoding.Deflate => new NetHttpDeflateSniffStream(body),
        NetHttpContentEncoding.Brotli => new BrotliStream(body.AsStream(), CompressionMode.Decompress, false).AsAstreamR(),
        _ => body,
    };

    private static bool IsToken(ReadOnlySpan<char> value, ReadOnlySpan<char> token)
    {
        if (value.Length != token.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var a = value[i];
            var b = token[i];

            if (a == b)
                continue;

            if (a >= 'A' && a <= 'Z')
                a = (char)(a + 32);

            if (b >= 'A' && b <= 'Z')
                b = (char)(b + 32);

            if (a != b)
                return false;
        }

        return true;
    }

    private enum NetHttpContentEncoding
    {
        Gzip,
        Deflate,
        Brotli,
    }

    private sealed class NetHttpDeflateSniffStream(IStreamR<byte> inner) : IStreamR<byte>
    {
        private readonly IStreamR<byte> inner = inner.Required();
        private IStreamR<byte>? decoder;
        private Disposed disposed = new();
        private bool initialized;

        public int Read(Span<byte> buffer, CancelToken cancelToken = default)
        {
            this.disposed.ThrowIf();

            if (!this.initialized)
                this.Initialize(cancelToken);

            return this.decoder!.Read(buffer, cancelToken);
        }

        public void Dispose()
        {
            if (this.disposed.Enter())
                return;

            if (!this.initialized)
            {
                this.inner.Dispose();

                return;
            }

            if (this.decoder is not null)
                this.decoder.Dispose();
        }

        private void Initialize(CancelToken cancelToken)
        {
            if (this.initialized)
                return;

            this.initialized = true;

            var prefix = new byte[2];
            var read = this.inner.Read(prefix, cancelToken);

            if (read < 2)
            {
                var more = this.inner.Read(prefix.AsSpan(read, 2 - read), cancelToken);
                read += more;
            }

            var source = read == 0 ? this.inner : new NetHttpPrefixedReadStream(prefix, read, this.inner, false);
            var stream = source.AsStream();

            Stream decodedStream = read >= 2 && IsZlibHeader(prefix[0], prefix[1])
                ? new ZLibStream(stream, CompressionMode.Decompress, false)
                : new DeflateStream(stream, CompressionMode.Decompress, false);

            this.decoder = decodedStream.AsAstreamR();
        }

        private static bool IsZlibHeader(byte cmf, byte flg)
        {
            if ((cmf & 0x0F) != 8)
                return false;

            if ((cmf & 0xF0) > 0x70)
                return false;

            return ((cmf << 8) + flg) % 31 == 0;
        }
    }

    private sealed class NetHttpPrefixedReadStream(byte[] prefix, int length, IStreamR<byte> inner, bool leaveOpen) : IStreamR<byte>
    {
        private readonly IStreamR<byte> inner = inner.Required();
        private readonly byte[] prefix = prefix.Required();
        private Disposed disposed = new();
        private int offset;

        public int Read(Span<byte> buffer, CancelToken cancelToken = default)
        {
            var available = length - this.offset;

            if (available > 0)
            {
                var toCopy = Math.Min(available, buffer.Length);
                this.prefix.AsSpan(this.offset, toCopy).CopyTo(buffer);
                this.offset += toCopy;

                return toCopy;
            }

            return this.inner.Read(buffer, cancelToken);
        }

        public void Dispose()
        {
            if (!this.disposed.Enter())
                return;

            if (leaveOpen)
                return;

            this.inner.Dispose();
        }
    }
}
