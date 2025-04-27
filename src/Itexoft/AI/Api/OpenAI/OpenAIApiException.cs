// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiApiException(int statusCode, string path, string? responseBody, Exception? innerException = null)
    : Exception(BuildMessage(statusCode, path, responseBody), innerException)
{
    public int StatusCode { get; } = statusCode;

    public string Path { get; } = path.RequiredNotWhiteSpace();

    public string? ResponseBody { get; } = responseBody;

    private static string BuildMessage(int statusCode, string path, string? responseBody)
    {
        path = path.RequiredNotWhiteSpace();

        if (string.IsNullOrWhiteSpace(responseBody))
            return $"OpenAI API request failed with status {statusCode} for '{path}'.";

        return $"OpenAI API request failed with status {statusCode} for '{path}'. Body: {responseBody}";
    }
}
