// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Dkon.Internal;

namespace Itexoft.Formats.Dkon;

public partial class DkonFormat
{
    internal const string MarkerMultilineOpen = ":\"\"\"|";
    internal const string MarkerMultilineClose = "|\"\"\":";

    internal const char Equal = '=';
    internal const char RightBracket = ']';
    internal const char LeftBracket = '[';
    internal const char Ampersand = '&';
    internal const char Zero = '0';
    internal const char Nine = '9';
    internal const char DoubleQuote = '"';
    internal const char Underscore = '_';
    internal const char Plus = '+';
    internal const char Minus = '-';
    internal const char Colon = ':';
    internal const char Space = ' ';
    internal const char Tab = '\t';
    internal const char CarriageReturn = '\r';
    internal const char LineFeed = '\n';

    public static string GetDoc(DkonSyntaxLevel syntaxLevel) => DkonSyntaxDocs.GetDoc(syntaxLevel);
    public static string GetDoc(DkonNode node) => DkonSyntaxDocs.GetDoc(GetSyntaxLevel(node));

    public static DkonNode DeserializeNodeOrEmpty(string? text) => DeserializeNode(text) ?? new DkonNode();
    public static DkonNode? DeserializeNode(string? text) => text is null ? null : DkonSerializer.Deserialize(text.AsSpan());
    public static string? SerializeNode(DkonNode? root) => DkonSerializer.Serialize(root);
    public static string SerializeNodeOrEmpty(DkonNode? root) => SerializeNode(root) ?? string.Empty;
    public static string SerializeObjOrEmpty(DkonObj root) => SerializeNodeOrEmpty(root.Node);
    public static string? SerializeObj(DkonObj? root) => SerializeNode(root?.Node);

    public static DkonObj DeserializeObj(string? text)
    {
        if (text is null)
            return default;

        var deserialized = DkonSerializer.Deserialize(text.AsSpan());

        if (deserialized is null)
            return default;

        return new(deserialized);
    }

    public static DkonSyntaxLevel GetSyntaxLevel(string? text) => GetSyntaxLevel(DeserializeNodeOrEmpty(text));

    public static DkonSyntaxLevel GetSyntaxLevel(DkonNode? root)
    {
        if (root is null || root.IsEmpty)
            return DkonSyntaxLevel.None;

        var scanner = new DkonSyntaxUtils();
        scanner.Scan(root);

        return scanner.Level;
    }
}
