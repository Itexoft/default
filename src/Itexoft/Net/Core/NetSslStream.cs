// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public sealed class NetSslStream(
    INetStream innerStream,
    bool leaveInnerStreamOpen,
    RemoteCertificateValidationCallback? userCertificateValidationCallback = null,
    LocalCertificateSelectionCallback? userCertificateSelectionCallback = null,
    EncryptionPolicy encryptionPolicy = EncryptionPolicy.RequireEncryption) : INetStream
{
    private readonly SslStream sslStream = new(
        innerStream.AsStream(),
        leaveInnerStreamOpen,
        userCertificateValidationCallback,
        userCertificateSelectionCallback,
        encryptionPolicy);

    public NetSslStream(NetStream innerStream) : this(innerStream, false, null, null) { }

    public NetSslStream(NetStream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback? userCertificateValidationCallback) :
        this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, null, EncryptionPolicy.RequireEncryption) { }

    public NetSslStream(
        NetStream innerStream,
        bool leaveInnerStreamOpen,
        RemoteCertificateValidationCallback? userCertificateValidationCallback,
        LocalCertificateSelectionCallback? userCertificateSelectionCallback) : this(
        innerStream,
        leaveInnerStreamOpen,
        userCertificateValidationCallback,
        userCertificateSelectionCallback,
        EncryptionPolicy.RequireEncryption) { }

    public bool IsAuthenticated => this.sslStream.IsAuthenticated;

    public TimeSpan ReadTimeout
    {
        get => TimeSpan.FromMilliseconds(this.sslStream.ReadTimeout);
        set => this.sslStream.ReadTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan WriteTimeout
    {
        get => TimeSpan.FromMilliseconds(this.sslStream.WriteTimeout);
        set => this.sslStream.WriteTimeout = value.TimeoutMilliseconds;
    }

    public int Read(Span<byte> span, CancelToken cancelToken = default)
    {
        if (span.IsEmpty)
            return 0;

        cancelToken.ThrowIf();

        return this.sslStream.Read(span);
    }

    public void Flush(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        this.sslStream.Flush();
    }

    public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.sslStream.Write(buffer);
    }

    public void Dispose() => this.sslStream.Dispose();

    public void AuthenticateAsClient(SslClientAuthenticationOptions sslOptions, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            this.sslStream.AuthenticateAsClientAsync(sslOptions, token).GetAwaiter().GetResult();
    }

    public void AuthenticateAsServer(X509Certificate2 serverCertificate, bool b, SslProtocols none, bool b1) =>
        this.sslStream.AuthenticateAsServerAsync(serverCertificate, b, none, b1).GetAwaiter().GetResult();
}
