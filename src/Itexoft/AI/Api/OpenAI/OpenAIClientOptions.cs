// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Dns;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiClientOptions
{
    public Uri BaseUri { get; set; } = new("https://api.openai.com/v1/", UriKind.Absolute);

    public string? ApiKey { get; set; }

    public string? UserAgent { get; set; } = "Itexoft.OpenAI/1.0";

    public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Ignore;

    public INetDnsResolver DnsResolver { get; set; } = NetDnsResolver.Default;
}
