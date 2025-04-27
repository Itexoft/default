// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Dkon;

public static class DkonFormatters
{
    private const int longValueWrapThreshold = 150;
    private const int childDepthStep = 1;
    private static readonly string singleLineBreak = new(DkonFormat.LineFeed, 1);
    private static readonly string doubleLineBreak = new(DkonFormat.LineFeed, 2);

    public static void Beautify(DkonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var owned = new HashSet<DkonNode>(ReferenceEqualityComparer.Instance);
        var pending = new List<PendingNode>(64);
        var pendingRead = 0;

        FormatOwnedList(node, 0, ref owned, ref pending);

        while (pendingRead < pending.Count)
        {
            var current = pending[pendingRead++];

            var childDepth = current.Depth + childDepthStep;

            if (current.Node.Alt is not null)
                FormatOwnedList(current.Node.Alt, childDepth, ref owned, ref pending);

            if (current.Node.Ref is not null)
                FormatOwnedList(current.Node.Ref, childDepth, ref owned, ref pending);
        }
    }

    public static void Beautify(DkonObj node) => Beautify(node.Node);

    private static void FormatOwnedList(DkonNode head, int depth, ref HashSet<DkonNode> owned, ref List<PendingNode> pending)
    {
        var current = head;
        var firstInList = true;
        var previousWasMultiline = false;

        while (current is not null)
        {
            if (!owned.Add(current))
                return;

            ApplyNodeRules(current);
            var currentIsMultiline = IsMultilinePresentation(current);
            var currentHasStandaloneRef = current.Alt is null && current.Ref is not null;

            ref var padding = ref current.Padding;

            padding.Left = currentIsMultiline ? GetMultilineLeft(firstInList, previousWasMultiline, depth) :
                firstInList ? GetFirstLeft(depth) : GetInnerLeft(depth);

            pending.Add(new PendingNode(current, depth));

            var next = current.Next;

            if (next is null)
            {
                padding.Right = currentIsMultiline ? doubleLineBreak : GetLastRight(depth);

                return;
            }

            var nextIsMultiline = WillRenderMultiline(next);
            var currentClosesArray = current.Alt is not null;

            padding.Right = currentIsMultiline ? doubleLineBreak :
                currentHasStandaloneRef ? singleLineBreak :
                nextIsMultiline ? string.Empty :
                currentClosesArray ? doubleLineBreak : singleLineBreak;

            if (owned.Contains(next))
                return;

            previousWasMultiline = currentIsMultiline;
            firstInList = false;
            current = next;
        }
    }

    private static string GetFirstLeft(int depth)
    {
        if (depth <= 0)
            return string.Empty;

        return DkonFormat.LineFeed + GetIndent(depth);
    }

    private static string GetInnerLeft(int depth) => depth <= 0 ? string.Empty : GetIndent(depth);

    private static string GetLastRight(int depth)
    {
        if (depth <= 0)
            return string.Empty;

        return DkonFormat.LineFeed + GetIndent(depth - 1);
    }

    private static string GetIndent(int depth)
    {
        if (depth <= 0)
            return string.Empty;

        return new string(DkonFormat.Space, depth * 2);
    }

    private static string GetMultilineLeft(bool firstInList, bool previousWasMultiline, int depth)
    {
        if (firstInList)
            return depth == 0 ? string.Empty : doubleLineBreak;

        if (previousWasMultiline)
            return string.Empty;

        return doubleLineBreak;
    }

    private static void ApplyNodeRules(DkonNode node)
    {
        if (ShouldPromoteToMultiline(node))
            node.Bracing = DkonBracing.Multiline;
    }

    private static bool ShouldPromoteToMultiline(DkonNode node)
    {
        if (node.Value.Length <= longValueWrapThreshold)
            return false;

        for (var i = 0; i < node.Value.Length; i++)
        {
            if (node.Value[i] is DkonFormat.CarriageReturn or DkonFormat.LineFeed)
                return false;
        }

        return true;
    }

    private static bool IsMultilinePresentation(DkonNode node)
    {
        if (node.Bracing == DkonBracing.Multiline)
            return true;

        for (var i = 0; i < node.Value.Length; i++)
        {
            if (node.Value[i] is DkonFormat.CarriageReturn or DkonFormat.LineFeed)
                return true;
        }

        return false;
    }

    private static bool WillRenderMultiline(DkonNode node) => IsMultilinePresentation(node) || ShouldPromoteToMultiline(node);

    private readonly record struct PendingNode(DkonNode Node, int Depth);
}
