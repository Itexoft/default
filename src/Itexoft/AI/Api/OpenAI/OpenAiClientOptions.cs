// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Dns;

namespace Itexoft.AI.Api.OpenAI;

public readonly struct OpenAiClientOptions(Uri baseUri, string? apiKey = null)
{
    public OpenAiClientOptions() : this("https://api.openai.com/v1/", null) { }

    public OpenAiClientOptions(string uri, string? apiKey = null) : this(new Uri(uri, UriKind.Absolute), apiKey) { }

    public Uri BaseUri { get; init; } = baseUri;

    public string? ApiKey { get; init; } = apiKey;

    public string? UserAgent { get; init; } = "Itexoft.OpenAi/1.0";

    public TimeSpan RequestTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    public RetryPolicy RetryPolicy { get; init; } = RetryPolicy.Ignore;

    public INetDnsResolver DnsResolver { get; init; } = NetDnsResolver.Default;
}
