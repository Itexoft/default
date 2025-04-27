// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;

namespace Itexoft.Formats.Dkon.Internal;

internal static class DkonSyntaxDocs
{
    public static string GetDoc(DkonSyntaxLevel syntaxLevel)
    {
        var builder = new StringBuilder();
        var hasContent = false;

        AppendSection(
            builder,
            ref hasContent,
            """
            - Output MUST be one complete DKON document and nothing else.
            - Start with DKON content immediately. Do not output prose, Markdown, code fences, comments, placeholders, ellipses, wrapper text, or diagnostic text.
            - Document = list of items separated by ` `, `\t`, `\r`, `\n`.
            - Leading, trailing, and repeated separators are ignored.
            - Only exact unescaped marker tokens of the current syntax level are structural. Similar-looking text is ordinary data.
            - Never use constructs that are not enabled by the current syntax level.
            - DKON never infers business types from names or token shapes.
            - The external contract decides which enabled DKON construct is expected at each position.
            - If the external contract expects string data, keep it as string even when it looks numeric, boolean-like, null-like, enum-like, or path-like.
            - Do not invent wrapper items, helper fields, summaries, explanations, or sentinel values not required by the external contract.
            - Prefer validity over brevity: extra quoting/escaping is allowed.
            - Safe machine-written scalar value form is quoted string.
            - If unsure, escape the scalar. Do not rely on reader recovery rules; emit complete closing markers and explicit RHS items.
            """);

        if (Includes(syntaxLevel, DkonSyntaxLevel.Scalars))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Scalars/Strings
                - Scalar forms: bare, quoted (`:"...":`), multiline (`:"""|` ... `|""":`).
                - Bare text is best for simple identifiers and short separator-free tokens.
                - Quoted string is the default escaped scalar form for values.
                - There is no generic `:value:` syntax. A plain trailing `:` is data.
                - Bare text has no closing marker. `alpha:` means bare text `alpha:`.
                - Quoted string opens with `:"` and closes only at exact unescaped `":`.
                - A lone `"` or `:` does not close a quoted string.
                - Quoted string may contain spaces, tabs, colons, quotes, slashes, and real line breaks.
                - Multiline string is an alternative scalar form for readability and collision-heavy content.
                - Multiline is not required merely because the value contains line breaks.
                - Multiline opens with `:"""|` + EOL/EOF and closes on standalone line `|""":`.
                - Writer MUST emit the closing `":` or `|""":`.
                - Reader normalizes multiline line endings to `\n`. Writer emits `\n`.
                - MUST escape: empty string, any scalar containing separators, any scalar that would be mistaken for active syntax.
                - Any scalar may be quoted or multiline even when not required.
                - DKON does not use backslash escaping. `"` and `'` are ordinary data characters.
                - To make a marker literal where it would be active, insert `_` inside the marker token. `_` is removed on read.
                - Examples:
                    `a` -> `a`
                    `a b` -> `:"a b":`
                    `` -> `:"":`
                    `:_"` -> `:"`
                    `"_:` -> `":`
                    `:_"""|` -> `:"""|`
                    multiline example -> `:"""|\nx\ny\n|""":`
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.Assignments))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignments
                - Assignment operator is the exact token `:=:`.
                - Assignment is postfix: current item is left side, next full DKON item is right side.
                - Right side is exactly one DKON item, not free-form tail text.
                - Writer MUST emit an explicit RHS item.
                - Record-like pattern: `fieldName :=: fieldValue`.
                - Field names follow normal scalar rules.
                - Canonical write: no extra separators around `:=:`.
                - For scalar values, safe default is quoted string: `fieldName :=:"value":`.
                - If a quoted RHS starts with `:"`, it MUST end with exact `":`.
                - Typical scalar RHS shapes: `name :=:"alpha":`, `path :=:"/a/b":`.
                - `entry :=:alpha:` means RHS bare text `alpha:`.
                - To encode string `alpha`, write `entry :=:"alpha":`.
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.Arrays))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Arrays
                - Array markers are exact tokens `:[` and `]:`.
                - At this syntax level, array form is an array-only item: `:[ item1 item2 ... ]:`.
                - Array items are full DKON items separated by document separators.
                - Empty array is `:[]:`.
                - Nested arrays are supported.
                - If an array is RHS of assignment, the whole array is one RHS item.
                - Writer MUST emit closing `]:`.
                - If one item has both array and assignment, canonical order is array first, then assignment.
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.AssignmentsChains))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignment Chains
                - Chains are allowed only when enabled.
                - Each `:=:` still binds exactly one RHS item.
                - Use chains only when the RHS itself must be an assignment node.
                - Example: `a :=: b :=:"c":`
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.ArrayAssignments))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignments In Arrays
                - Inside arrays, items follow the same rules as top-level items for the current syntax level.
                - Use only constructs enabled by the current syntax level inside array items.
                - `:=:` still binds exactly one RHS item inside the array.
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.ArrayNamed))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Named Arrays
                - With named arrays enabled, a scalar text item may be followed by postfix array.
                - `name :[ a b ]:` is one item with text `name` and nested array.
                - `:[ a b ]:` remains the array-only item with empty text.
                - Named array is one item, not two sibling items.
                - Canonical order with assignment is `name :[ ... ]: :=: rhs`.
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.References))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Relative Links
                - Relative link token is `:&+N&:` or `:&-N&:` where `N` is decimal digits.
                - Sign is mandatory; zero is not representable.
                - Link is service syntax, not ordinary scalar text.
                - If literal data would look like a link token, quote or escape it.
                - On read, malformed, zero, overflowed, out-of-range, or cyclic links resolve to null.
                - Emit links only when the external contract explicitly requires back-reference or shared-node reuse.
                - Writer MUST emit the full token including closing `&:`.
                """"");
        }

        return $"""
                ``` DKON Structured Data Documentation
                {
                    builder.ToString().PadLines(1, '\t')
                }
                ```
                """;
    }

    public static string GetEbnf(DkonSyntaxLevel syntaxLevel)
    {
        var builder = new StringBuilder();
        var hasContent = false;

        if (Includes(syntaxLevel, DkonSyntaxLevel.Scalars))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Scalars/Strings
                document               = sep*, [ item, { sep+, item } ], sep*, eof ;
                item                   = scalar ;
                scalar                 = bare | inline | multiline ;
                bare                   = bare_char, { bare_char } ;
                bare_char              = ? any char that is not sep and does not start an active marker ? ;
                inline                 = marker_inline_open, { inline_char | escaped_marker }, marker_inline_close ;
                inline_char            = ? any char except an unescaped marker_inline_close ? ;
                multiline              = multiline_open, { content_line }, close_line ;
                multiline_open         = marker_multiline_open, hpad, eol ;
                close_line             = hpad, marker_multiline_close, hpad, [ eol ] ;
                content_line           = { any_char }, eol ;
                marker_inline_open     = `:"` ;
                marker_inline_close    = `":` ;
                marker_multiline_open  = `:"""|` ;
                marker_multiline_close = `|""":` ;
                escaped_marker         = ? marker token with one or more `_` inserted inside token boundary ? ;
                sep                    = ` ` | `\t` | `\r` | `\n` ;
                hpad                   = { ` ` | `\t` } ;
                eol                    = `\r\n` | `\r` | `\n` ;
                eof                    = ? end of file ? ;
                any_char               = ? any character except end-of-line ? ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.Assignments))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignments
                item                   = scalar, [ assign ] ;
                assign                 = `:=:`, sep*, scalar ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.Arrays))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Arrays
                When arrays are enabled, the previous item-level `scalar` occurrences become `array_base`.
                array_base             = scalar | array_item ;
                array_item             = array ;
                array                  = `:[`, list, `]:` ;
                list                   = sep*, [ item, { sep+, item } ], sep* ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.AssignmentsChains))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignment Chains
                When assignment chains are enabled, the previous `assign` rule becomes:
                assign                 = `:=:`, sep*, item ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.ArrayAssignments))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Assignments In Arrays
                list_item              = item ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.ArrayNamed))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                Named Arrays
                When named arrays are enabled, replace `array_base` with:
                array_base             = scalar | array_item | named_array_item ;
                named_array_item       = scalar, array ;
                """"");
        }

        if (Includes(syntaxLevel, DkonSyntaxLevel.References))
        {
            AppendSection(
                builder,
                ref hasContent,
                """""
                References
                When references are enabled, `link` is a valid reference item where the external contract expects a reference.
                link                   = `:&`, sign, digits, `&:` ;
                sign                   = `+` | `-` ;
                digits                 = digit, { digit } ;
                digit                  = `0` | `1` | `2` | `3` | `4` | `5` | `6` | `7` | `8` | `9` ;
                valid_delta            = ? signed value must be non-zero and fit int32 ? ;
                """"");
        }

        return builder.ToString();
    }

    private static void AppendSection(StringBuilder builder, ref bool hasContent, string section)
    {
        if (hasContent)
            builder.Append("\n\n");

        builder.Append(section);
        hasContent = true;
    }

    private static bool Includes(DkonSyntaxLevel level, DkonSyntaxLevel required) =>
        (DkonSyntaxUtils.GetFeatures(level) & DkonSyntaxUtils.GetFeatures(required)) == DkonSyntaxUtils.GetFeatures(required);
}
