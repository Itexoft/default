// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Itexoft.Formats.Dkon.Internal;

internal static partial class DkonSerializer
{
    internal static DkonNode? Deserialize(ReadOnlySpan<char> source)
    {
        if (source.Length == 0)
            return null;

        var reader = new Reader(source);
        var tempEntries = new TempEntry[32];
        var tempCount = 0;

        var frames = new ParseFrame[16];
        var frameCount = 0;
        PushListFrame(StopKind.Eof, ref frames, ref frameCount);

        var childResult = 0;
        var childReady = false;
        var rootRef = 0;

        while (frameCount > 0)
        {
            var top = frameCount - 1;
            var frameKind = frames[top].Kind;

            if (frameKind == ParseFrameKind.List)
            {
                var state = (ListParseState)frames[top].State;

                if (state == ListParseState.Init)
                {
                    frames[top].Pending = ReadSeparatorRegion(ref reader);
                    frames[top].State = (byte)ListParseState.NeedItem;

                    continue;
                }

                if (state == ListParseState.NeedItem)
                {
                    if (reader.Index >= source.Length || IsAtStop(ref reader, frames[top].Stop))
                    {
                        if (frames[top].PrevSourceKind == SourceKind.Node)
                            tempEntries[frames[top].PrevNodeRef].Right = frames[top].Pending;

                        childResult = frames[top].HeadRef;
                        childReady = true;
                        frameCount--;

                        if (frameCount == 0)
                        {
                            rootRef = childResult;
                            childReady = false;
                        }

                        continue;
                    }

                    frames[top].State = (byte)ListParseState.AfterItem;
                    PushItemFrame(frames[top].Stop, ref frames, ref frameCount);

                    continue;
                }

                if (!childReady)
                    throw new InvalidOperationException("Parse pipeline is inconsistent.");

                var itemRef = childResult;
                childReady = false;

                if (frames[top].HeadRef == 0)
                    frames[top].HeadRef = itemRef;

                var itemKind = tempEntries[itemRef].Kind;

                if (itemKind == EntryKind.Node)
                {
                    if (frames[top].PrevSourceKind == SourceKind.Node)
                    {
                        SplitInterItemRegion(source, frames[top].Pending, out var prevRight, out var currentLeft);
                        tempEntries[frames[top].PrevNodeRef].Right = prevRight;
                        tempEntries[itemRef].Left = currentLeft;
                        tempEntries[frames[top].PrevNodeRef].NextRef = itemRef;
                    }
                    else
                        tempEntries[itemRef].Left = frames[top].Pending;

                    frames[top].PrevSourceKind = SourceKind.Node;
                    frames[top].PrevNodeRef = itemRef;
                }
                else
                {
                    if (frames[top].PrevSourceKind == SourceKind.Node)
                    {
                        tempEntries[frames[top].PrevNodeRef].NextRef = itemRef;
                        tempEntries[frames[top].PrevNodeRef].Right = frames[top].Pending;
                    }

                    frames[top].PrevSourceKind = SourceKind.Link;
                    frames[top].PrevNodeRef = 0;
                }

                frames[top].Pending = ReadSeparatorRegion(ref reader);
                frames[top].State = (byte)ListParseState.NeedItem;

                continue;
            }

            var itemState = (ItemParseState)frames[top].State;

            if (itemState == ItemParseState.Start)
            {
                if (reader.TryReadOpenLink())
                {
                    var linkRef = ParseLinkEntry(ref reader, ref tempEntries, ref tempCount);
                    childResult = linkRef;
                    childReady = true;
                    frameCount--;

                    continue;
                }

                var nodeRef = AddNodeEntry(ref tempEntries, ref tempCount);
                frames[top].NodeRef = nodeRef;
                frames[top].Flags = 0;

                if (reader.TryReadMultilineOpenLine())
                {
                    tempEntries[nodeRef].Bracing = DkonBracing.Multiline;
                    tempEntries[nodeRef].Value = ParseMultilineValue(ref reader);
                    frames[top].State = (byte)ItemParseState.NodePostfix;

                    continue;
                }

                if (reader.TryReadOpenInline())
                {
                    tempEntries[nodeRef].Bracing = DkonBracing.Inline;
                    tempEntries[nodeRef].Value = ParseInlineValue(ref reader);
                    frames[top].State = (byte)ItemParseState.NodePostfix;

                    continue;
                }

                if (reader.TryReadOpenArray())
                {
                    SetAltFlag(ref frames[top], true);
                    frames[top].State = (byte)ItemParseState.WaitAltList;
                    PushListFrame(StopKind.ArrayClose, ref frames, ref frameCount);

                    continue;
                }

                if (reader.TryReadRef())
                {
                    SetRefFlag(ref frames[top], true);
                    _ = ReadSeparatorRegion(ref reader);

                    if (reader.Index >= source.Length || IsAtStop(ref reader, frames[top].Stop))
                    {
                        tempEntries[nodeRef].RefRef = 0;
                        frames[top].State = (byte)ItemParseState.NodePostfix;

                        continue;
                    }

                    frames[top].State = (byte)ItemParseState.WaitRefItem;
                    PushItemFrame(frames[top].Stop, ref frames, ref frameCount);

                    continue;
                }

                tempEntries[nodeRef].Bracing = DkonBracing.Bare;
                tempEntries[nodeRef].Value = ParseBareValue(ref reader, frames[top].Stop);
                frames[top].State = (byte)ItemParseState.NodePostfix;

                continue;
            }

            if (itemState == ItemParseState.WaitAltList)
            {
                if (!childReady)
                    throw new InvalidOperationException("List child was not returned.");

                tempEntries[frames[top].NodeRef].AltRef = childResult;
                childReady = false;
                _ = reader.TryReadCloseArray();
                frames[top].State = (byte)ItemParseState.NodePostfix;

                continue;
            }

            if (itemState == ItemParseState.WaitRefItem)
            {
                if (!childReady)
                    throw new InvalidOperationException("Ref child was not returned.");

                tempEntries[frames[top].NodeRef].RefRef = childResult;
                childReady = false;
                frames[top].State = (byte)ItemParseState.NodePostfix;

                continue;
            }

            var savedIndex = reader.Index;
            var savedReusableColon = reader.ReusableColon;
            _ = ReadSeparatorRegion(ref reader);

            if (!HasAltFlag(in frames[top]) && reader.TryReadOpenArray())
            {
                SetAltFlag(ref frames[top], true);
                frames[top].State = (byte)ItemParseState.WaitAltList;
                PushListFrame(StopKind.ArrayClose, ref frames, ref frameCount);

                continue;
            }

            if (!HasRefFlag(in frames[top]) && reader.TryReadRef())
            {
                SetRefFlag(ref frames[top], true);
                _ = ReadSeparatorRegion(ref reader);

                if (reader.Index >= source.Length || IsAtStop(ref reader, frames[top].Stop))
                {
                    tempEntries[frames[top].NodeRef].RefRef = 0;
                    frames[top].State = (byte)ItemParseState.NodePostfix;

                    continue;
                }

                frames[top].State = (byte)ItemParseState.WaitRefItem;
                PushItemFrame(frames[top].Stop, ref frames, ref frameCount);

                continue;
            }

            reader.Index = savedIndex;
            reader.ReusableColon = savedReusableColon;
            childResult = frames[top].NodeRef;
            childReady = true;
            frameCount--;
        }

        if (rootRef == 0)
            return null;

        var resolveCache = new int[tempCount + 1];
        var resolveState = new byte[tempCount + 1];
        var resolvePath = new int[16];

        for (var i = 1; i <= tempCount; i++)
        {
            if (tempEntries[i].Kind != EntryKind.Node)
                continue;

            tempEntries[i].NextRef = ResolveEntry(
                tempEntries[i].NextRef,
                tempEntries,
                tempCount,
                ref resolveCache,
                ref resolveState,
                ref resolvePath);

            tempEntries[i].AltRef = ResolveEntry(tempEntries[i].AltRef, tempEntries, tempCount, ref resolveCache, ref resolveState, ref resolvePath);
            tempEntries[i].RefRef = ResolveEntry(tempEntries[i].RefRef, tempEntries, tempCount, ref resolveCache, ref resolveState, ref resolvePath);
        }

        rootRef = ResolveEntry(rootRef, tempEntries, tempCount, ref resolveCache, ref resolveState, ref resolvePath);

        if (rootRef == 0)
            return null;

        var reachable = new bool[tempCount + 1];
        var reachStack = new int[32];
        var reachCount = 0;
        PushInt(rootRef, ref reachStack, ref reachCount);

        while (reachCount > 0)
        {
            var current = reachStack[--reachCount];

            if (current == 0 || current > tempCount)
                continue;

            if (reachable[current])
                continue;

            if (tempEntries[current].Kind != EntryKind.Node)
                continue;

            reachable[current] = true;
            PushInt(tempEntries[current].NextRef, ref reachStack, ref reachCount);
            PushInt(tempEntries[current].AltRef, ref reachStack, ref reachCount);
            PushInt(tempEntries[current].RefRef, ref reachStack, ref reachCount);
        }

        var materialized = new DkonNode?[tempCount + 1];

        for (var i = 1; i <= tempCount; i++)
        {
            if (!reachable[i] || tempEntries[i].Kind != EntryKind.Node)
                continue;

            var node = new DkonNode();
            node.Value = tempEntries[i].Value;
            node.Bracing = tempEntries[i].Bracing;
            ref var padding = ref node.Padding;
            padding.Left = SliceToString(source, tempEntries[i].Left);
            padding.Right = SliceToString(source, tempEntries[i].Right);
            materialized[i] = node;
        }

        for (var i = 1; i <= tempCount; i++)
        {
            if (!reachable[i] || tempEntries[i].Kind != EntryKind.Node)
                continue;

            var node = materialized[i]!;
            node.Next = tempEntries[i].NextRef == 0 ? null : materialized[tempEntries[i].NextRef];
            node.Alt = tempEntries[i].AltRef == 0 ? null : materialized[tempEntries[i].AltRef];
            node.Ref = tempEntries[i].RefRef == 0 ? null : materialized[tempEntries[i].RefRef];
        }

        return materialized[rootRef];
    }

    internal static string? Serialize(DkonNode? root)
    {
        if (root is null)
            return null;

        var state = new SerializationState
        {
            Nodes = new DkonNode?[32],
            NodeRecords = new NodeRecord[32],
            HashKeys = new DkonNode?[32],
            HashIds = new int[32],
            PlanItems = new PlanItem[64],
            PendingPlanNodes = new int[32],
        };

        var rootId = GetOrAddNodeId(root, ref state);
        var rootPlanHead = EmitListLinear(rootId, ref state);

        while (state.PendingCount > 0)
        {
            var planNodeIndex = state.PendingPlanNodes[--state.PendingCount];
            var nodeId = state.PlanItems[planNodeIndex].NodeId;
            var node = state.Nodes[nodeId]!;

            if (node.Alt is not null)
            {
                var altId = GetOrAddNodeId(node.Alt, ref state);
                state.PlanItems[planNodeIndex].AltHead = EmitListLinear(altId, ref state);
            }

            if (node.Ref is not null)
            {
                var refId = GetOrAddNodeId(node.Ref, ref state);
                state.PlanItems[planNodeIndex].RefItem = BuildRefItem(refId, ref state);
            }
        }

        for (var i = 1; i <= state.PlanCount; i++)
        {
            if (state.PlanItems[i].Kind != PlanItemKind.Link)
                continue;

            if (state.NodeRecords[state.PlanItems[i].NodeId].OwnerPlanItem == 0)
                throw new InvalidOperationException("Graph is outside representable DKON norm: link target has no list ownership.");
        }

        AssignPlanIndices(rootPlanHead, ref state);

        var writer = new ArrayBufferWriter<char>(32);
        var frames = new WriteFrame[32];
        var frameCount = 0;
        PushWriteListFrame(rootPlanHead, StopKind.Eof, ref frames, ref frameCount);

        while (frameCount > 0)
        {
            var top = frameCount - 1;

            if (frames[top].Kind == WriteFrameKind.List)
            {
                var listState = (WriteListState)frames[top].State;

                if (listState == WriteListState.Init)
                {
                    var firstNodeItem = FindFirstNodeItem(frames[top].ListHead, state.PlanItems);

                    if (firstNodeItem != 0)
                    {
                        var firstNode = state.Nodes[state.PlanItems[firstNodeItem].NodeId]!;
                        writer.Write(GetLeft(firstNode));
                    }

                    frames[top].Current = frames[top].ListHead;
                    frames[top].Prev = 0;
                    frames[top].State = (byte)WriteListState.Iterate;

                    continue;
                }

                if (listState == WriteListState.Iterate)
                {
                    if (frames[top].Current == 0)
                    {
                        if (frames[top].Prev != 0 && state.PlanItems[frames[top].Prev].Kind == PlanItemKind.Node)
                        {
                            var lastNode = state.Nodes[state.PlanItems[frames[top].Prev].NodeId]!;
                            var lastRight = GetRight(lastNode);

                            if (lastRight.Length > 0)
                                writer.Write(lastRight);

                            if (frames[top].Stop == StopKind.ArrayClose
                                && WritesMultilineScalar(frames[top].Prev, in state)
                                && !HasNewLine(lastRight))
                                writer.WriteLineFeed();
                        }

                        frameCount--;

                        continue;
                    }

                    var currentItem = frames[top].Current;

                    if (frames[top].Prev != 0 && state.PlanItems[frames[top].Prev].Kind == PlanItemKind.Node)
                    {
                        var prevNode = state.Nodes[state.PlanItems[frames[top].Prev].NodeId]!;

                        if (state.PlanItems[currentItem].Kind == PlanItemKind.Node)
                        {
                            var currentNode = state.Nodes[state.PlanItems[currentItem].NodeId]!;
                            var prevRight = GetRight(prevNode);
                            var currentLeft = GetLeft(currentNode);
                            var prevWritesMultiline = WritesMultilineScalar(frames[top].Prev, in state);
                            var normalizeHorizontalGap = !prevWritesMultiline && !HasNewLine(prevRight) && !HasNewLine(currentLeft);
                            var writeCurrentLeft = true;
                            writer.Write(prevRight);

                            if (normalizeHorizontalGap && prevRight.Length > 0 && currentLeft.Length > 0)
                                writeCurrentLeft = false;

                            if (NeedsStructuralSeparator(prevRight, currentLeft, prevWritesMultiline))
                            {
                                if (HasNewLine(prevRight) || HasNewLine(currentLeft) || prevWritesMultiline)
                                    writer.WriteLineFeed();
                                else
                                    writer.WriteSpace();
                            }

                            if (writeCurrentLeft)
                                writer.Write(currentLeft);
                        }
                        else
                        {
                            var prevRight = GetRight(prevNode);
                            var prevWritesMultiline = WritesMultilineScalar(frames[top].Prev, in state);

                            if (prevRight.Length > 0)
                                writer.Write(prevRight);

                            if (NeedsStructuralSeparator(prevRight, ReadOnlySpan<char>.Empty, prevWritesMultiline))
                            {
                                if (HasNewLine(prevRight) || prevWritesMultiline)
                                    writer.WriteLineFeed();
                                else
                                    writer.WriteSpace();
                            }
                        }
                    }

                    frames[top].ItemIndex = currentItem;
                    frames[top].State = (byte)WriteListState.AfterItem;
                    PushWriteItemFrame(currentItem, frames[top].Stop, ref frames, ref frameCount);

                    continue;
                }

                frames[top].Prev = frames[top].ItemIndex;
                frames[top].Current = state.PlanItems[frames[top].ItemIndex].Next;
                frames[top].State = (byte)WriteListState.Iterate;

                continue;
            }

            var itemIndex = frames[top].ItemIndex;
            var item = state.PlanItems[itemIndex];
            var itemState = (WriteItemState)frames[top].State;

            if (itemState == WriteItemState.Start)
            {
                if (item.Kind == PlanItemKind.Link)
                {
                    WriteLink(itemIndex, in item, ref writer, in state);
                    frameCount--;

                    continue;
                }

                var node = state.Nodes[item.NodeId]!;
                var hasAlt = item.AltHead != 0;
                var hasRef = item.RefItem != 0;
                var writesMultiline = WritesMultilineScalar(in node, hasAlt, hasRef);
                WriteNodeScalar(in node, hasAlt, hasRef, ref writer);

                if (hasAlt)
                {
                    if (writesMultiline)
                        writer.WriteLineFeed();

                    writer.WriteColon();
                    writer.WriteLeftBracket();
                    frames[top].State = (byte)WriteItemState.AfterAlt;
                    PushWriteListFrame(item.AltHead, StopKind.ArrayClose, ref frames, ref frameCount);

                    continue;
                }

                if (hasRef)
                {
                    if (writesMultiline)
                        writer.WriteLineFeed();

                    writer.WriteColon();
                    writer.WriteEqual();
                    writer.WriteColon();

                    frames[top].State = (byte)WriteItemState.AfterRef;
                    PushWriteItemFrame(item.RefItem, frames[top].Stop, ref frames, ref frameCount);

                    continue;
                }

                frameCount--;

                continue;
            }

            if (itemState == WriteItemState.AfterAlt)
            {
                writer.WriteRightBracket();
                writer.WriteColon();

                if (item.RefItem != 0)
                {
                    writer.WriteColon();
                    writer.WriteEqual();
                    writer.WriteColon();

                    frames[top].State = (byte)WriteItemState.AfterRef;
                    PushWriteItemFrame(item.RefItem, frames[top].Stop, ref frames, ref frameCount);

                    continue;
                }
            }

            frameCount--;
        }

        return new string(writer.WrittenSpan);
    }

    private static bool HasAltFlag(in ParseFrame frame) => (frame.Flags & 0x1) != 0;

    private static bool HasRefFlag(in ParseFrame frame) => (frame.Flags & 0x2) != 0;

    private static void SetAltFlag(ref ParseFrame frame, bool value)
    {
        if (value)
            frame.Flags |= 0x1;
        else
            frame.Flags &= unchecked((byte)~0x1);
    }

    private static void SetRefFlag(ref ParseFrame frame, bool value)
    {
        if (value)
            frame.Flags |= 0x2;
        else
            frame.Flags &= unchecked((byte)~0x2);
    }

    private static int ParseLinkEntry(ref Reader reader, ref TempEntry[] entries, ref int count)
    {
        var source = reader.Source;
        var valid = true;
        var sign = 0;

        if (reader.Index < source.Length)
        {
            var signChar = source[reader.Index];

            if (signChar == DkonFormat.Plus)
            {
                sign = 1;
                reader.Index++;
                reader.ReusableColon = false;
            }
            else if (signChar == DkonFormat.Minus)
            {
                sign = -1;
                reader.Index++;
                reader.ReusableColon = false;
            }
            else
                valid = false;
        }
        else
            valid = false;

        long magnitude = 0;
        var hasDigits = false;

        while (reader.Index < source.Length)
        {
            var c = source[reader.Index];

            if (c is < DkonFormat.Zero or > DkonFormat.Nine)
                break;

            hasDigits = true;

            if (magnitude > int.MaxValue / 10L || (magnitude == int.MaxValue / 10L && c - DkonFormat.Zero > int.MaxValue % 10))
                valid = false;
            else if (valid)
                magnitude = magnitude * 10L + (c - DkonFormat.Zero);

            reader.Index++;
            reader.ReusableColon = false;
        }

        if (!hasDigits || sign == 0)
            valid = false;

        while (reader.Index < source.Length && !TryMatchMarker(ref reader, [DkonFormat.Ampersand, DkonFormat.Colon]))
        {
            valid = false;
            reader.Index++;
            reader.ReusableColon = false;
        }

        _ = reader.TryReadCloseLink();

        var delta = 0;

        if (valid)
        {
            var signed = sign > 0 ? magnitude : -magnitude;

            if (signed is 0 or > int.MaxValue or < int.MinValue)
                valid = false;
            else
                delta = (int)signed;
        }

        return AddLinkEntry(delta, valid, ref entries, ref count);
    }

    private static string ParseBareValue(ref Reader reader, StopKind stop)
    {
        var buffer = new ArrayBufferWriter<char>(32);
        var consumed = false;

        while (reader.Index < reader.Source.Length)
        {
            if (TryReadAnyEscapedMarker(ref reader, ref buffer))
            {
                consumed = true;

                continue;
            }

            if (IsAtStop(ref reader, stop))
                break;

            if (TryMatchMarker(ref reader, [DkonFormat.Colon, DkonFormat.LeftBracket])
                || TryMatchMarker(ref reader, [DkonFormat.Colon, DkonFormat.Equal, DkonFormat.Colon]))
                break;

            var c = reader.Source[reader.Index];

            if (IsSeparator(c))
                break;

            buffer.Write(c);
            reader.Index++;
            reader.ReusableColon = false;
            consumed = true;
        }

        if (!consumed && reader.Index < reader.Source.Length)
        {
            buffer.Write(reader.Source.Slice(reader.Index, 1));
            reader.Index++;
            reader.ReusableColon = false;
        }

        return new string(buffer.WrittenSpan);
    }

    private static string ParseInlineValue(ref Reader reader)
    {
        var buffer = new ArrayBufferWriter<char>(32);

        while (reader.Index < reader.Source.Length)
        {
            if (TryReadAnyEscapedMarker(ref reader, ref buffer))
                continue;

            if (TryConsumeMarker(ref reader, [DkonFormat.DoubleQuote, DkonFormat.Colon]))
                break;

            buffer.Write(reader.Source.Slice(reader.Index, 1));
            reader.Index++;
            reader.ReusableColon = false;
        }

        return new string(buffer.WrittenSpan);
    }

    private static string ParseMultilineValue(ref Reader reader)
    {
        var buffer = new ArrayBufferWriter<char>(64);
        var source = reader.Source;

        while (reader.Index < source.Length)
        {
            var lineStart = reader.Index;
            ReadLogicalLine(source, lineStart, out var lineEnd, out var eolLength);

            if (TryReadMultilineCloseLine(
                    source,
                    lineStart,
                    lineEnd,
                    DkonFormat.MarkerMultilineClose.AsSpan(),
                    out var closeIndex,
                    out var consumesRemainder))
            {
                reader.Index = consumesRemainder ? lineEnd + eolLength : closeIndex;
                reader.ReusableColon = !consumesRemainder;

                return NormalizeMultilineValue(buffer);
            }

            if (!TryReadEscapedStandaloneMarkerLine(source, lineStart, lineEnd, DkonFormat.MarkerMultilineClose.AsSpan(), ref buffer))
                AppendDecodedEscapedMarkers(source.Slice(lineStart, lineEnd - lineStart), ref buffer);

            if (eolLength > 0)
            {
                var nextLineStart = lineEnd + eolLength;
                var shouldAppendLineFeed = true;

                if (nextLineStart < source.Length)
                {
                    ReadLogicalLine(source, nextLineStart, out var nextLineEnd, out _);

                    if (TryReadMultilineCloseLine(source, nextLineStart, nextLineEnd, DkonFormat.MarkerMultilineClose.AsSpan(), out _, out _))
                        shouldAppendLineFeed = false;
                }

                if (shouldAppendLineFeed)
                    buffer.WriteLineFeed();
            }

            reader.Index = lineEnd + eolLength;
            reader.ReusableColon = false;
        }

        return NormalizeMultilineValue(buffer);
    }

    private static string NormalizeMultilineValue(ArrayBufferWriter<char> buffer)
    {
        var span = buffer.WrittenSpan;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c != DkonFormat.Space && c != DkonFormat.Tab && c != DkonFormat.LineFeed && c != DkonFormat.CarriageReturn)
                return new string(buffer.WrittenSpan);
        }

        return string.Empty;
    }

    private static bool TryReadEscapedStandaloneMarkerLine(
        ReadOnlySpan<char> source,
        int lineStart,
        int lineEnd,
        ReadOnlySpan<char> marker,
        ref ArrayBufferWriter<char> buffer)
    {
        var markerStart = lineStart;

        while (markerStart < lineEnd && IsHorizontalPadding(source[markerStart]))
            markerStart++;

        var markerEnd = lineEnd;

        while (markerEnd > markerStart && IsHorizontalPadding(source[markerEnd - 1]))
            markerEnd--;

        if (markerEnd <= markerStart)
            return false;

        var core = source.Slice(markerStart, markerEnd - markerStart);

        if (core[0] != marker[0] || core.Length < marker.Length + 1)
            return false;

        var decoded = new ArrayBufferWriter<char>(core.Length + 2);

        if (!TryAppendDecodedEscapedMarker(core, marker, ref decoded))
            return false;

        buffer.Write(source.Slice(lineStart, markerStart - lineStart));
        buffer.Write(decoded.WrittenSpan);
        buffer.Write(source.Slice(markerEnd, lineEnd - markerEnd));

        return true;
    }

    private static bool TryReadAnyEscapedMarker(ref Reader reader, ref ArrayBufferWriter<char> buffer)
    {
        if (TryConsumeEscapedMarker(ref reader, DkonFormat.MarkerMultilineOpen.AsSpan(), ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.Colon, DkonFormat.Equal, DkonFormat.Colon], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.Colon, DkonFormat.LeftBracket], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.RightBracket, DkonFormat.Colon], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.Colon, DkonFormat.DoubleQuote], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.DoubleQuote, DkonFormat.Colon], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.Colon, DkonFormat.Ampersand], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, [DkonFormat.Ampersand, DkonFormat.Colon], ref buffer))
            return true;

        if (TryConsumeEscapedMarker(ref reader, DkonFormat.MarkerMultilineClose.AsSpan(), ref buffer))
            return true;

        return false;
    }

    private static void AppendDecodedEscapedMarkers(ReadOnlySpan<char> text, ref ArrayBufferWriter<char> buffer)
    {
        var reader = new Reader(text);

        while (reader.Index < reader.Source.Length)
        {
            if (TryReadAnyEscapedMarker(ref reader, ref buffer))
                continue;

            buffer.Write(reader.Source.Slice(reader.Index, 1));
            reader.Index++;
            reader.ReusableColon = false;
        }
    }

    private static int ResolveEntry(int entryIndex, TempEntry[] entries, int count, ref int[] cache, ref byte[] state, ref int[] path)
    {
        if (entryIndex == 0)
            return 0;

        if (entryIndex < 1 || entryIndex > count)
            return 0;

        if (entries[entryIndex].Kind == EntryKind.Node)
            return entryIndex;

        if (state[entryIndex] == 2)
            return cache[entryIndex];

        var current = entryIndex;
        var pathCount = 0;
        var resolved = 0;

        while (true)
        {
            if (current > count)
            {
                resolved = 0;

                break;
            }

            var kind = entries[current].Kind;

            if (kind == EntryKind.Node)
            {
                resolved = current;

                break;
            }

            if (state[current] == 2)
            {
                resolved = cache[current];

                break;
            }

            if (ContainsInt(path, pathCount, current))
            {
                resolved = 0;

                break;
            }

            EnsureCapacity(ref path, pathCount + 1);
            path[pathCount++] = current;
            state[current] = 1;

            if (!entries[current].LinkSyntaxValid)
            {
                resolved = 0;

                break;
            }

            var target = (long)current + entries[current].Delta;

            if (target <= 0 || target > count || target == current)
            {
                resolved = 0;

                break;
            }

            current = (int)target;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var index = path[i];
            cache[index] = resolved;
            state[index] = 2;
        }

        return resolved;
    }

    private static void AssignPlanIndices(int rootPlanHead, ref SerializationState state)
    {
        var frames = new IndexFrame[32];
        var frameCount = 0;
        PushIndexList(rootPlanHead, ref frames, ref frameCount);
        var sourceIndex = 0;

        while (frameCount > 0)
        {
            var top = frameCount - 1;

            if (frames[top].Kind == IndexFrameKind.List)
            {
                if (frames[top].Current == 0)
                {
                    frameCount--;

                    continue;
                }

                var item = frames[top].Current;
                frames[top].Current = state.PlanItems[item].Next;
                state.PlanItems[item].SourceIndex = ++sourceIndex;

                if (state.PlanItems[item].Kind == PlanItemKind.Node)
                {
                    var nodeId = state.PlanItems[item].NodeId;
                    state.NodeRecords[nodeId].OwnerSourceIndex = state.PlanItems[item].SourceIndex;
                    PushIndexNodeChildren(state.PlanItems[item].AltHead, state.PlanItems[item].RefItem, ref frames, ref frameCount);
                }

                continue;
            }

            if (frames[top].State == 0)
            {
                frames[top].State = 1;

                if (frames[top].AltHead != 0)
                    PushIndexList(frames[top].AltHead, ref frames, ref frameCount);

                continue;
            }

            if (frames[top].State == 1)
            {
                frames[top].State = 2;

                if (frames[top].RefItem != 0)
                    PushIndexList(frames[top].RefItem, ref frames, ref frameCount);

                continue;
            }

            frameCount--;
        }
    }

    private static int EmitListLinear(int headNodeId, ref SerializationState state)
    {
        var listHead = 0;
        var listTail = 0;
        var currentNodeId = headNodeId;

        while (currentNodeId != 0)
        {
            var node = state.Nodes[currentNodeId]!;
            var itemIndex = 0;

            if (state.NodeRecords[currentNodeId].OwnerPlanItem != 0)
            {
                itemIndex = AddPlanLink(currentNodeId, ref state);
                AppendToList(itemIndex, ref listHead, ref listTail, ref state);

                break;
            }

            itemIndex = AddPlanNode(currentNodeId, ref state);
            state.NodeRecords[currentNodeId].OwnerPlanItem = itemIndex;
            AppendToList(itemIndex, ref listHead, ref listTail, ref state);
            PushPendingPlanNode(itemIndex, ref state);
            currentNodeId = node.Next is null ? 0 : GetOrAddNodeId(node.Next, ref state);
        }

        return listHead;
    }

    private static int BuildRefItem(int nodeId, ref SerializationState state)
    {
        var node = state.Nodes[nodeId]!;

        if (state.NodeRecords[nodeId].OwnerPlanItem != 0)
            return AddPlanLink(nodeId, ref state);

        if (node.Next is null)
        {
            var item = AddPlanNode(nodeId, ref state);
            state.NodeRecords[nodeId].OwnerPlanItem = item;
            PushPendingPlanNode(item, ref state);

            return item;
        }

        return AddPlanLink(nodeId, ref state);
    }

    private static void WriteNodeScalar(in DkonNode node, bool hasAlt, bool hasRef, ref ArrayBufferWriter<char> writer)
    {
        var value = node.Value.AsSpan();

        if (value.Length == 0 && node.Bracing == DkonBracing.Bare && (hasAlt || hasRef))
            return;

        var mode = ChooseBracing(value, node.Bracing);

        if (mode == DkonBracing.Bare)
        {
            Span<char> span = stackalloc char[]
            {
                DkonFormat.DoubleQuote,
                DkonFormat.Ampersand,
                DkonFormat.LeftBracket,
                DkonFormat.RightBracket,
                DkonFormat.Equal,
            };

            WriteContent(value, span, ref writer);

            return;
        }

        if (mode == DkonBracing.Inline)
        {
            writer.WriteColon();
            writer.WriteDoubleQuote();

            Span<char> span = stackalloc char[]
            {
                DkonFormat.DoubleQuote,
            };

            WriteContent(value, span, ref writer);
            writer.WriteDoubleQuote();
            writer.WriteColon();

            return;
        }

        if (mode == DkonBracing.Multiline)
        {
            writer.WriteColon();
            writer.Write(DkonFormat.MarkerMultilineOpen.AsSpan()[1..]);
            writer.WriteLineFeed();
            WriteContent(value, ReadOnlySpan<char>.Empty, ref writer);
            writer.WriteLineFeed();
            writer.WriteMarkerMultilineClose();
        }
    }

    private static void WriteContent(ReadOnlySpan<char> value, ReadOnlySpan<char> rules, ref ArrayBufferWriter<char> writer)
    {
        var i = 0;

        while (i + 1 < value.Length)
        {
            var c0 = value[i];
            var c1 = value[i + 1];

            for (var r = 0; r < rules.Length; r++)
            {
                var rule = rules[r];

                if ((c0 == DkonFormat.Colon && c1 == rule) || (c0 == rule && c1 == DkonFormat.Colon))
                {
                    writer.Write(c0);
                    writer.WriteUnderscore();
                    writer.Write(c1);
                    i += 2;

                    goto Next;
                }
            }

            if (value.Length - i >= DkonFormat.MarkerMultilineOpen.Length
                && value.Slice(i, DkonFormat.MarkerMultilineOpen.Length).SequenceEqual(DkonFormat.MarkerMultilineOpen))
            {
                writer.Write(DkonFormat.MarkerMultilineOpen[0]);
                writer.WriteUnderscore();
                writer.Write(DkonFormat.MarkerMultilineOpen.AsSpan()[1..DkonFormat.MarkerMultilineOpen.Length]);
                i += DkonFormat.MarkerMultilineOpen.Length;

                goto Next;
            }

            if (value.Length - i >= DkonFormat.MarkerMultilineClose.Length
                && value.Slice(i, DkonFormat.MarkerMultilineClose.Length).SequenceEqual(DkonFormat.MarkerMultilineClose))
            {
                writer.Write(DkonFormat.MarkerMultilineClose.AsSpan()[..^1]);
                writer.WriteUnderscore();
                writer.Write(DkonFormat.MarkerMultilineClose.AsSpan()[^1]);
                i += DkonFormat.MarkerMultilineClose.Length;

                goto Next;
            }

            writer.Write(c0);

            i++;

            Next: ;
        }

        if (i < value.Length)
            writer.Write(value[i]);
    }

    internal static DkonBracing ChooseBracing(ReadOnlySpan<char> value, DkonBracing requested)
    {
        var minimum = DkonBracing.Inline;

        if (HasLogicalEol(value))
            minimum = DkonBracing.Multiline;
        else if (CanWriteBare(value))
            minimum = DkonBracing.Bare;

        return (DkonBracing)Math.Max((int)minimum, (int)requested);
    }

    private static bool CanWriteBare(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (IsSeparator(value[i]))
                return false;
        }

        return true;
    }

    private static void WriteLink(int linkItemIndex, in PlanItem linkItem, ref ArrayBufferWriter<char> writer, in SerializationState state)
    {
        var ownerIndex = state.NodeRecords[linkItem.NodeId].OwnerSourceIndex;
        var delta = ownerIndex - state.PlanItems[linkItemIndex].SourceIndex;

        if (delta == 0)
            throw new InvalidOperationException("Zero link delta is not representable.");

        writer.WriteColon();
        writer.WriteAmpersand();

        if (delta > 0)
        {
            writer.Write(DkonFormat.Plus);
            WriteUnsignedInt((uint)delta, ref writer);
        }
        else
        {
            writer.Write(DkonFormat.Minus);
            WriteUnsignedInt((uint)-delta, ref writer);
        }

        writer.WriteAmpersand();
        writer.WriteColon();
    }

    private static void WriteUnsignedInt(uint value, ref ArrayBufferWriter<char> writer)
    {
        Span<char> tmp = stackalloc char[10];
        var p = tmp.Length;
        var v = value;

        do
        {
            var digit = v % 10;
            v /= 10;
            tmp[--p] = (char)(DkonFormat.Zero + digit);
        }
        while (v != 0);

        writer.Write(tmp[p..]);
    }

    private static int FindFirstNodeItem(int listHead, PlanItem[] planItems)
    {
        var current = listHead;

        while (current != 0)
        {
            if (planItems[current].Kind == PlanItemKind.Node)
                return current;

            current = planItems[current].Next;
        }

        return 0;
    }

    private static bool StartsOrEndsWith(ReadOnlySpan<char> value, ReadOnlySpan<char> marker)
    {
        if (value.Length < marker.Length)
            return false;

        for (int i = marker.Length - 1, count = 0; i >= 0; i--)
        {
            if (value[i] != marker[i])
                count++;

            if (count == marker.Length)
                return true;
        }

        for (int i = 0, count = 0; i < marker.Length; i++)
        {
            if (value[i] == marker[i])
                count++;

            if (count == marker.Length)
                return true;
        }

        return false;
    }

    private static bool HasLogicalEol(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == DkonFormat.CarriageReturn || value[i] == DkonFormat.LineFeed)
                return true;
        }

        return false;
    }

    private static bool HasNewLine(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == DkonFormat.CarriageReturn || value[i] == DkonFormat.LineFeed)
                return true;
        }

        return false;
    }

    private static bool NeedsStructuralSeparator(ReadOnlySpan<char> prevRight, ReadOnlySpan<char> currentLeft, bool prevWritesMultiline)
    {
        var hasRight = prevRight.Length > 0;
        var hasLeft = currentLeft.Length > 0;

        if (!hasRight && !hasLeft)
            return true;

        if (!prevWritesMultiline && !HasNewLine(prevRight) && !HasNewLine(currentLeft))
            return !hasRight && !hasLeft;

        if (EndsWithLineFeed(prevRight) || StartsWithLogicalEol(currentLeft))
            return false;

        return true;
    }

    private static bool EndsWithLineFeed(ReadOnlySpan<char> value) => value.Length > 0 && value[^1] == DkonFormat.LineFeed;

    private static bool StartsWithLogicalEol(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return false;

        return value[0] is DkonFormat.CarriageReturn or DkonFormat.LineFeed;
    }

    private static bool WritesMultilineScalar(int planItemIndex, in SerializationState state)
    {
        if (planItemIndex == 0)
            return false;

        var item = state.PlanItems[planItemIndex];

        if (item.Kind != PlanItemKind.Node)
            return false;

        var node = state.Nodes[item.NodeId]!;

        return WritesMultilineScalar(in node, item.AltHead != 0, item.RefItem != 0);
    }

    private static bool WritesMultilineScalar(in DkonNode node, bool hasAlt, bool hasRef)
    {
        var value = node.Value.AsSpan();

        if (value.Length == 0 && node.Bracing == DkonBracing.Bare && (hasAlt || hasRef))
            return false;

        return ChooseBracing(value, node.Bracing) == DkonBracing.Multiline;
    }

    private static bool IsAtStop(ref Reader reader, StopKind stop)
    {
        if (stop != StopKind.ArrayClose)
            return false;

        return reader.TryMatch([DkonFormat.RightBracket, DkonFormat.Colon]);
    }

    private static Slice ReadSeparatorRegion(ref Reader reader)
    {
        var start = reader.Index;
        var source = reader.Source;

        while (reader.Index < source.Length && IsSeparator(source[reader.Index]))
            reader.Index++;

        var length = reader.Index - start;

        if (length > 0)
            reader.ReusableColon = false;

        return new Slice(start, length);
    }

    private static bool IsSeparator(char c) => c is DkonFormat.Space or DkonFormat.Tab or DkonFormat.CarriageReturn or DkonFormat.LineFeed;

    private static bool IsLine(char c) => c is DkonFormat.CarriageReturn or DkonFormat.LineFeed;

    private static bool IsHorizontalPadding(char c) => c is DkonFormat.Space or DkonFormat.Tab;

    private static bool TryConsumeEscapedMarker(ref Reader reader, ReadOnlySpan<char> marker, ref ArrayBufferWriter<char> buffer)
    {
        if (marker.Length < 2)
            return false;

        var source = reader.Source;
        var index = reader.Index;
        var p = index;

        var coalesced = reader.ReusableColon
                        && marker[0] == DkonFormat.Colon
                        && index > 0
                        && source[index - 1] == DkonFormat.Colon
                        && (index >= source.Length || source[index] != marker[0]);

        if (!coalesced)
        {
            if (p >= source.Length || source[p] != marker[0])
                return false;

            p++;
        }

        Span<int> runs = stackalloc int[16];

        if (marker.Length - 1 > runs.Length)
            return false;

        var escaped = false;

        for (var i = 0; i < marker.Length - 1; i++)
        {
            var run = 0;

            while (p < source.Length && source[p] == DkonFormat.Underscore)
            {
                run++;
                p++;
            }

            runs[i] = run;

            if (run > 0)
                escaped = true;

            if (p >= source.Length || source[p] != marker[i + 1])
                return false;

            p++;
        }

        if (!escaped)
            return false;

        buffer.Write(marker[0]);

        for (var i = 0; i < marker.Length - 1; i++)
        {
            for (var j = 1; j < runs[i]; j++)
                buffer.WriteUnderscore();

            buffer.Write(marker.Slice(i + 1, 1));
        }

        reader.Index = p;
        reader.ReusableColon = marker[^1] == DkonFormat.Colon;

        return true;
    }

    private static bool TryAppendDecodedEscapedMarker(ReadOnlySpan<char> encoded, ReadOnlySpan<char> marker, ref ArrayBufferWriter<char> buffer)
    {
        if (marker.Length < 2 || encoded.Length < marker.Length + 1)
            return false;

        var p = 0;

        if (encoded[p] != marker[0])
            return false;

        p++;
        Span<int> runs = stackalloc int[16];

        if (marker.Length - 1 > runs.Length)
            return false;

        var escaped = false;

        for (var i = 0; i < marker.Length - 1; i++)
        {
            var run = 0;

            while (p < encoded.Length && encoded[p] == DkonFormat.Underscore)
            {
                run++;
                p++;
            }

            runs[i] = run;

            if (run > 0)
                escaped = true;

            if (p >= encoded.Length || encoded[p] != marker[i + 1])
                return false;

            p++;
        }

        if (!escaped || p != encoded.Length)
            return false;

        buffer.Write(marker[0]);

        for (var i = 0; i < marker.Length - 1; i++)
        {
            for (var j = 1; j < runs[i]; j++)
                buffer.WriteUnderscore();

            buffer.Write(marker.Slice(i + 1, 1));
        }

        return true;
    }

    private static bool TryMatchMarker(ref Reader reader, ReadOnlySpan<char> marker)
    {
        if (marker.Length == 0)
            return true;

        var source = reader.Source;
        var index = reader.Index;

        if (reader.ReusableColon
            && marker[0] == DkonFormat.Colon
            && index > 0
            && source[index - 1] == DkonFormat.Colon
            && (index >= source.Length || source[index] != marker[0]))
        {
            if (index + marker.Length - 1 > source.Length)
                return false;

            for (var i = 1; i < marker.Length; i++)
            {
                if (source[index + i - 1] != marker[i])
                    return false;
            }

            return true;
        }

        if (index + marker.Length > source.Length)
            return false;

        for (var i = 0; i < marker.Length; i++)
        {
            if (source[index + i] != marker[i])
                return false;
        }

        return true;
    }

    private static bool TryConsumeMarker(ref Reader reader, ReadOnlySpan<char> marker)
    {
        if (!TryMatchMarker(ref reader, marker))
            return false;

        if (reader.ReusableColon
            && marker.Length > 0
            && marker[0] == DkonFormat.Colon
            && reader.Index > 0
            && reader.Source[reader.Index - 1] == DkonFormat.Colon
            && (reader.Index >= reader.Source.Length || reader.Source[reader.Index] != marker[0]))
            reader.Index += marker.Length - 1;
        else
            reader.Index += marker.Length;

        reader.ReusableColon = marker.Length > 0 && marker[^1] == DkonFormat.Colon;

        return true;
    }

    private static bool TryReadMultilineCloseLine(
        ReadOnlySpan<char> source,
        int lineStart,
        int lineEnd,
        ReadOnlySpan<char> marker,
        out int closeIndex,
        out bool consumesRemainder)
    {
        closeIndex = 0;
        consumesRemainder = false;
        var i = lineStart;

        while (i < lineEnd && IsHorizontalPadding(source[i]))
            i++;

        if (lineEnd - i < marker.Length)
            return false;

        for (var j = 0; j < marker.Length; j++)
        {
            if (source[i + j] != marker[j])
                return false;
        }

        closeIndex = i + marker.Length;
        i = closeIndex;

        while (i < lineEnd && IsHorizontalPadding(source[i]))
            i++;

        if (i < lineEnd && i == closeIndex)
            return false;

        consumesRemainder = i == lineEnd;

        return true;
    }

    private static void ReadLogicalLine(ReadOnlySpan<char> source, int start, out int lineEnd, out int eolLength)
    {
        var i = start;

        while (i < source.Length && source[i] != DkonFormat.CarriageReturn && source[i] != DkonFormat.LineFeed)
            i++;

        lineEnd = i;

        if (i >= source.Length)
        {
            eolLength = 0;

            return;
        }

        if (source[i] == DkonFormat.CarriageReturn && i + 1 < source.Length && source[i + 1] == DkonFormat.LineFeed)
        {
            eolLength = 2;

            return;
        }

        eolLength = 1;
    }

    private static void SplitInterItemRegion(ReadOnlySpan<char> source, Slice region, out Slice prevRight, out Slice currentLeft)
    {
        if (region.Length == 0)
        {
            prevRight = default;
            currentLeft = default;

            return;
        }

        var sepOffset = -1;
        var sepLength = 0;

        for (var i = 0; i < region.Length; i++)
        {
            var c = source[region.Start + i];

            if (c == DkonFormat.CarriageReturn)
            {
                sepOffset = i;
                sepLength = i + 1 < region.Length && source[region.Start + i + 1] == DkonFormat.LineFeed ? 2 : 1;

                break;
            }

            if (c == DkonFormat.LineFeed)
            {
                sepOffset = i;
                sepLength = 1;

                break;
            }
        }

        if (sepOffset < 0)
        {
            sepOffset = 0;
            sepLength = 1;
        }

        prevRight = new Slice(region.Start, sepOffset);
        currentLeft = new Slice(region.Start + sepOffset + sepLength, region.Length - sepOffset - sepLength);
    }

    private static string SliceToString(ReadOnlySpan<char> source, Slice slice)
    {
        if (slice.Length <= 0)
            return string.Empty;

        return new string(source.Slice(slice.Start, slice.Length));
    }

    private static string GetLeft(DkonNode node)
    {
        ref var padding = ref node.Padding;

        return padding.Left ?? string.Empty;
    }

    private static string GetRight(DkonNode node)
    {
        ref var padding = ref node.Padding;

        return padding.Right ?? string.Empty;
    }

    private static int AddNodeEntry(ref TempEntry[] entries, ref int count)
    {
        EnsureCapacity(ref entries, count + 2);
        var index = ++count;

        entries[index] = new TempEntry
        {
            Kind = EntryKind.Node,
            Value = string.Empty,
            Bracing = DkonBracing.Bare,
        };

        return index;
    }

    private static int AddLinkEntry(int delta, bool syntaxValid, ref TempEntry[] entries, ref int count)
    {
        EnsureCapacity(ref entries, count + 2);
        var index = ++count;

        entries[index] = new TempEntry
        {
            Kind = EntryKind.Link,
            Delta = delta,
            LinkSyntaxValid = syntaxValid,
        };

        return index;
    }

    private static int GetOrAddNodeId(DkonNode node, ref SerializationState state)
    {
        if (state.HashKeys.Length == 0)
        {
            state.HashKeys = new DkonNode?[32];
            state.HashIds = new int[32];
        }

        if ((state.HashUsed + 1) * 4 >= state.HashKeys.Length * 3)
            Rehash(ref state, state.HashKeys.Length * 2);

        var mask = state.HashKeys.Length - 1;
        var slot = RuntimeHelpers.GetHashCode(node) & mask;

        while (true)
        {
            var key = state.HashKeys[slot];

            if (key is null)
            {
                EnsureCapacity(ref state.Nodes, state.NodeCount + 2);
                EnsureCapacity(ref state.NodeRecords, state.NodeCount + 2);

                var id = ++state.NodeCount;
                state.Nodes[id] = node;
                state.NodeRecords[id] = default;
                state.HashKeys[slot] = node;
                state.HashIds[slot] = id;
                state.HashUsed++;

                return id;
            }

            if (ReferenceEquals(key, node))
                return state.HashIds[slot];

            slot = (slot + 1) & mask;
        }
    }

    private static void Rehash(ref SerializationState state, int newSize)
    {
        if ((newSize & (newSize - 1)) != 0)
            throw new InvalidOperationException("Hash table size must be power of two.");

        var oldKeys = state.HashKeys;
        var oldIds = state.HashIds;
        state.HashKeys = new DkonNode?[newSize];
        state.HashIds = new int[newSize];
        state.HashUsed = 0;

        var mask = newSize - 1;

        for (var i = 0; i < oldKeys.Length; i++)
        {
            var key = oldKeys[i];

            if (key is null)
                continue;

            var slot = RuntimeHelpers.GetHashCode(key) & mask;

            while (state.HashKeys[slot] is not null)
                slot = (slot + 1) & mask;

            state.HashKeys[slot] = key;
            state.HashIds[slot] = oldIds[i];
            state.HashUsed++;
        }
    }

    private static int AddPlanNode(int nodeId, ref SerializationState state)
    {
        EnsureCapacity(ref state.PlanItems, state.PlanCount + 2);
        var index = ++state.PlanCount;

        state.PlanItems[index] = new PlanItem
        {
            Kind = PlanItemKind.Node,
            NodeId = nodeId,
        };

        return index;
    }

    private static int AddPlanLink(int nodeId, ref SerializationState state)
    {
        EnsureCapacity(ref state.PlanItems, state.PlanCount + 2);
        var index = ++state.PlanCount;

        state.PlanItems[index] = new PlanItem
        {
            Kind = PlanItemKind.Link,
            NodeId = nodeId,
        };

        return index;
    }

    private static void PushPendingPlanNode(int itemIndex, ref SerializationState state)
    {
        EnsureCapacity(ref state.PendingPlanNodes, state.PendingCount + 1);
        state.PendingPlanNodes[state.PendingCount++] = itemIndex;
    }

    private static void AppendToList(int itemIndex, ref int head, ref int tail, ref SerializationState state)
    {
        if (head == 0)
        {
            head = itemIndex;
            tail = itemIndex;

            return;
        }

        state.PlanItems[tail].Next = itemIndex;
        tail = itemIndex;
    }

    private static bool ContainsInt(int[] data, int count, int value)
    {
        for (var i = 0; i < count; i++)
        {
            if (data[i] == value)
                return true;
        }

        return false;
    }

    private static void PushListFrame(StopKind stop, ref ParseFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new ParseFrame
        {
            Kind = ParseFrameKind.List,
            State = (byte)ListParseState.Init,
            Stop = stop,
        };
    }

    private static void PushItemFrame(StopKind stop, ref ParseFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new ParseFrame
        {
            Kind = ParseFrameKind.Item,
            State = (byte)ItemParseState.Start,
            Stop = stop,
        };
    }

    private static void PushWriteListFrame(int listHead, StopKind stop, ref WriteFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new WriteFrame
        {
            Kind = WriteFrameKind.List,
            State = (byte)WriteListState.Init,
            Stop = stop,
            ListHead = listHead,
        };
    }

    private static void PushWriteItemFrame(int itemIndex, StopKind stop, ref WriteFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new WriteFrame
        {
            Kind = WriteFrameKind.Item,
            State = (byte)WriteItemState.Start,
            Stop = stop,
            ItemIndex = itemIndex,
        };
    }

    private static void PushIndexList(int head, ref IndexFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new IndexFrame
        {
            Kind = IndexFrameKind.List,
            Current = head,
        };
    }

    private static void PushIndexNodeChildren(int altHead, int refItem, ref IndexFrame[] frames, ref int frameCount)
    {
        EnsureCapacity(ref frames, frameCount + 1);

        frames[frameCount++] = new IndexFrame
        {
            Kind = IndexFrameKind.NodeChildren,
            AltHead = altHead,
            RefItem = refItem,
            State = 0,
        };
    }

    private static void PushInt(int value, ref int[] values, ref int count)
    {
        if (value == 0)
            return;

        EnsureCapacity(ref values, count + 1);
        values[count++] = value;
    }

    private static void EnsureCapacity<T>(ref T[] data, int requiredLength)
    {
        if (data.Length >= requiredLength)
            return;

        var target = data.Length == 0 ? 4 : data.Length;

        while (target < requiredLength)
            target <<= 1;

        Array.Resize(ref data, target);
    }

    private enum StopKind : byte
    {
        Eof = 0,
        ArrayClose = 1,
    }

    private enum EntryKind : byte
    {
        None = 0,
        Node = 1,
        Link = 2,
    }

    private enum SourceKind : byte
    {
        None = 0,
        Node = 1,
        Link = 2,
    }

    private enum ParseFrameKind : byte
    {
        List = 1,
        Item = 2,
    }

    private enum ListParseState : byte
    {
        Init = 0,
        NeedItem = 1,
        AfterItem = 2,
    }

    private enum ItemParseState : byte
    {
        Start = 0,
        NodePostfix = 1,
        WaitAltList = 2,
        WaitRefItem = 3,
    }

    private enum PlanItemKind : byte
    {
        None = 0,
        Node = 1,
        Link = 2,
    }

    private enum WriteFrameKind : byte
    {
        List = 1,
        Item = 2,
    }

    private enum WriteListState : byte
    {
        Init = 0,
        Iterate = 1,
        AfterItem = 2,
    }

    private enum WriteItemState : byte
    {
        Start = 0,
        AfterAlt = 1,
        AfterRef = 2,
    }

    private enum IndexFrameKind : byte
    {
        List = 1,
        NodeChildren = 2,
    }

    private struct Slice(int start, int length)
    {
        public int Start = start;
        public int Length = length;
    }

    internal ref struct Reader(ReadOnlySpan<char> source)
    {
        public ReadOnlySpan<char> Source = source;
        public int Index = 0;
        public bool ReusableColon = false;
    }

    private struct TempEntry
    {
        public EntryKind Kind;
        public int NextRef;
        public int AltRef;
        public int RefRef;
        public Slice Left;
        public Slice Right;
        public string Value;
        public DkonBracing Bracing;
        public int Delta;
        public bool LinkSyntaxValid;
    }

    private struct ParseFrame
    {
        public ParseFrameKind Kind;
        public byte State;
        public StopKind Stop;
        public int HeadRef;
        public int PrevNodeRef;
        public SourceKind PrevSourceKind;
        public Slice Pending;
        public int NodeRef;
        public byte Flags;
    }

    private struct SerializationState
    {
        public DkonNode?[] Nodes;
        public NodeRecord[] NodeRecords;
        public int NodeCount;

        public DkonNode?[] HashKeys;
        public int[] HashIds;
        public int HashUsed;

        public PlanItem[] PlanItems;
        public int PlanCount;

        public int[] PendingPlanNodes;
        public int PendingCount;
    }

    private struct NodeRecord
    {
        public int OwnerPlanItem;
        public int OwnerSourceIndex;
    }

    private struct PlanItem
    {
        public PlanItemKind Kind;
        public int NodeId;
        public int Next;
        public int AltHead;
        public int RefItem;
        public int SourceIndex;
    }

    private struct IndexFrame
    {
        public IndexFrameKind Kind;
        public int Current;
        public int AltHead;
        public int RefItem;
        public byte State;
    }

    private struct WriteFrame
    {
        public WriteFrameKind Kind;
        public byte State;
        public StopKind Stop;
        public int ListHead;
        public int Current;
        public int Prev;
        public int ItemIndex;
    }
}

file static class ReaderExtensions
{
    private static bool IsHorizontalPadding(char c) => c is DkonFormat.Space or DkonFormat.Tab;

    private static bool TryConsume(ref DkonSerializer.Reader reader, ReadOnlySpan<char> marker)
    {
        if (!reader.TryMatch(marker))
            return false;

        if (reader.ReusableColon
            && marker.Length > 0
            && marker[0] == DkonFormat.Colon
            && reader.Index > 0
            && reader.Source[reader.Index - 1] == DkonFormat.Colon
            && (reader.Index >= reader.Source.Length || reader.Source[reader.Index] != marker[0]))
            reader.Index += marker.Length - 1;
        else
            reader.Index += marker.Length;

        reader.ReusableColon = marker.Length > 0 && marker[^1] == DkonFormat.Colon;

        return true;
    }

    extension(ref DkonSerializer.Reader reader)
    {
        public bool TryReadOpenInline() => TryConsume(ref reader, [DkonFormat.Colon, DkonFormat.DoubleQuote]);

        public bool TryReadOpenArray() => TryConsume(ref reader, [DkonFormat.Colon, DkonFormat.LeftBracket]);
        public bool TryReadCloseArray() => TryConsume(ref reader, [DkonFormat.RightBracket, DkonFormat.Colon]);
        public bool TryReadRef() => TryConsume(ref reader, [DkonFormat.Colon, DkonFormat.Equal, DkonFormat.Colon]);
        public bool TryReadOpenLink() => TryConsume(ref reader, [DkonFormat.Colon, DkonFormat.Ampersand]);
        public bool TryReadCloseLink() => TryConsume(ref reader, [DkonFormat.Ampersand, DkonFormat.Colon]);

        public bool TryReadMultilineOpenLine()
        {
            var savedIndex = reader.Index;
            var savedReusable = reader.ReusableColon;

            if (!TryConsume(ref reader, DkonFormat.MarkerMultilineOpen.AsSpan()))
                return false;

            while (reader.Index < reader.Source.Length && IsHorizontalPadding(reader.Source[reader.Index]))
            {
                reader.Index++;
                reader.ReusableColon = false;
            }

            if (reader.Index >= reader.Source.Length)
                return true;

            if (reader.TryReadLogicalEol())
                return true;

            reader.Index = savedIndex;
            reader.ReusableColon = savedReusable;

            return false;
        }

        private bool TryReadLogicalEol()
        {
            if (reader.Index >= reader.Source.Length)
                return false;

            var c = reader.Source[reader.Index];

            if (c == DkonFormat.CarriageReturn)
            {
                reader.Index++;

                if (reader.Index < reader.Source.Length && reader.Source[reader.Index] == DkonFormat.LineFeed)
                    reader.Index++;

                reader.ReusableColon = false;

                return true;
            }

            if (c == DkonFormat.LineFeed)
            {
                reader.Index++;
                reader.ReusableColon = false;

                return true;
            }

            return false;
        }

        public bool TryMatch(ReadOnlySpan<char> marker)
        {
            if (marker.Length == 0)
                return true;

            var source = reader.Source;
            var index = reader.Index;

            if (reader.ReusableColon
                && marker[0] == DkonFormat.Colon
                && index > 0
                && source[index - 1] == DkonFormat.Colon
                && (index >= source.Length || source[index] != marker[0]))
            {
                if (index + marker.Length - 1 > source.Length)
                    return false;

                for (var i = 1; i < marker.Length; i++)
                {
                    if (source[index + i - 1] != marker[i])
                        return false;
                }

                return true;
            }

            if (index + marker.Length > source.Length)
                return false;

            for (var i = 0; i < marker.Length; i++)
            {
                if (source[index + i] != marker[i])
                    return false;
            }

            return true;
        }
    }
}

file static class ArrayBufferWriterExtensions
{
    extension(ArrayBufferWriter<char> abw)
    {
        public void WriteColon()
        {
            if (abw.WrittenCount > 0 && abw.WrittenSpan[^1] == DkonFormat.Colon)
                return;

            abw.Write(DkonFormat.Colon);
        }

        public void WriteUnderscore() => abw.Write(DkonFormat.Underscore);

        public void WriteLineFeed() => abw.Write(DkonFormat.LineFeed);

        public void WriteAmpersand() => abw.Write(DkonFormat.Ampersand);

        public void WriteDoubleQuote() => abw.Write(DkonFormat.DoubleQuote);

        public void WriteLeftBracket() => abw.Write(DkonFormat.LeftBracket);

        public void WriteSpace() => abw.Write(DkonFormat.Space);

        public void WriteRightBracket() => abw.Write(DkonFormat.RightBracket);

        public void WriteMarkerMultilineOpen() => abw.Write(DkonFormat.MarkerMultilineOpen);

        public void WriteMarkerMultilineClose() => abw.Write(DkonFormat.MarkerMultilineClose);

        public void WriteEqual() => abw.Write(DkonFormat.Equal);

        public void Write(char c) => abw.Write(stackalloc[] { c });
    }
}
