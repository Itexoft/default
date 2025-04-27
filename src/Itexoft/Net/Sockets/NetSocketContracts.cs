// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Sockets;

public enum NetSocketType
{
    Unknown = 0,
    Stream = 1,
    Dgram = 2,
}

internal enum NetSocketProtocol
{
    Unknown = 0,
    Tcp = 6,
    Udp = 17,
}

[Flags]
public enum NetSocketFlags
{
    None = 0,
    Peek = 0x02,
}

public enum NetSocketShutdown
{
    Receive = 0,
    Send = 1,
    Both = 2,
}

public enum NetSocketOptionLevel
{
    Socket = 0,
    Tcp = 1,
    Ipv6 = 2,
}

public enum NetSocketOptionName
{
    ReuseAddress = 0,
    SendBufferSize = 1,
    ReceiveBufferSize = 2,
    SendTimeout = 3,
    ReceiveTimeout = 4,
    NoDelay = 5,
    Ipv6Only = 6,
    ReusePort = 7,
}

public enum NetSocketError
{
    Success = 0,
    AccessDenied = 1,
    AddressAlreadyInUse = 2,
    AddressNotAvailable = 3,
    AddressFamilyNotSupported = 4,
    AlreadyInProgress = 5,
    ConnectionAborted = 6,
    ConnectionRefused = 7,
    ConnectionReset = 8,
    Disconnecting = 9,
    HostNotFound = 10,
    HostUnreachable = 11,
    InProgress = 12,
    Interrupted = 13,
    InvalidArgument = 14,
    IsConnected = 15,
    MessageSize = 16,
    NetworkDown = 17,
    NetworkUnreachable = 18,
    NoBufferSpaceAvailable = 19,
    NotConnected = 20,
    OperationAborted = 21,
    ProtocolNotSupported = 22,
    Shutdown = 23,
    SocketError = 24,
    TimedOut = 25,
    TryAgain = 26,
    WouldBlock = 27,
}

public readonly struct NetLingerOption(bool enabled, int lingerTime)
{
    public bool Enabled { get; } = enabled;

    public int LingerTime { get; } = lingerTime;
}

public sealed class NetSocketException(NetSocketError socketErrorCode, int? nativeErrorCode, string? message, Exception? innerException)
    : Exception(message ?? CreateMessage(socketErrorCode, nativeErrorCode), innerException)
{
    public NetSocketException(NetSocketError socketErrorCode) : this(socketErrorCode, null, null, null) { }

    public NetSocketException(NetSocketError socketErrorCode, int? nativeErrorCode) : this(socketErrorCode, nativeErrorCode, null, null) { }

    public NetSocketError SocketErrorCode { get; } = socketErrorCode;

    public int? NativeErrorCode { get; } = nativeErrorCode;

    private static string CreateMessage(NetSocketError socketErrorCode, int? nativeErrorCode) =>
        nativeErrorCode.HasValue ? $"Socket error: {socketErrorCode} (native: {nativeErrorCode.Value})." : $"Socket error: {socketErrorCode}.";
}
