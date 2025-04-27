// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Dkon.Internal;

internal sealed class DkonSyntaxUtils
{
    private readonly HashSet<DkonNode> seen = [];
    private readonly HashSet<DkonNode> seenInsideAxis = [];
    private readonly HashSet<DkonNode> seenOutsideAxis = [];

    public DkonSyntaxFeature Features { get; private set; }
    public DkonSyntaxLevel Level => GetLevel(this.Features);

    public static DkonSyntaxLevel GetLevel(DkonSyntaxFeature feature) => feature switch
    {
        _ when Has(feature, DkonSyntaxFeature.ReferenceGraph) => DkonSyntaxLevel.References,
        _ when Has(feature, DkonSyntaxFeature.ArrayNamed) => DkonSyntaxLevel.ArrayNamed,
        _ when Has(feature, DkonSyntaxFeature.AssignmentInArray) => DkonSyntaxLevel.ArrayAssignments,
        _ when Has(feature, DkonSyntaxFeature.AssignmentChain) => DkonSyntaxLevel.AssignmentsChains,
        _ when Has(feature, DkonSyntaxFeature.Array) || Has(feature, DkonSyntaxFeature.ArrayNested) => DkonSyntaxLevel.Arrays,
        _ when Has(feature, DkonSyntaxFeature.Assignment) => DkonSyntaxLevel.Assignments,
        _ when Has(feature, DkonSyntaxFeature.Scalar) || Has(feature, DkonSyntaxFeature.ScalarMultiline) || Has(feature, DkonSyntaxFeature.Multiline)
            => DkonSyntaxLevel.Scalars,
        _ => DkonSyntaxLevel.None,
    };

    public static DkonSyntaxFeature GetFeatures(DkonSyntaxLevel level) => level switch
    {
        DkonSyntaxLevel.None => DkonSyntaxFeature.None,
        DkonSyntaxLevel.Scalars => DkonSyntaxFeature.Multiline | DkonSyntaxFeature.Scalar | DkonSyntaxFeature.ScalarMultiline,
        DkonSyntaxLevel.Assignments => GetFeatures(DkonSyntaxLevel.Scalars) | DkonSyntaxFeature.Assignment,
        DkonSyntaxLevel.Arrays => GetFeatures(DkonSyntaxLevel.Assignments) | DkonSyntaxFeature.Array | DkonSyntaxFeature.ArrayNested,
        DkonSyntaxLevel.AssignmentsChains => GetFeatures(DkonSyntaxLevel.Arrays) | DkonSyntaxFeature.AssignmentChain,
        DkonSyntaxLevel.ArrayAssignments => GetFeatures(DkonSyntaxLevel.AssignmentsChains) | DkonSyntaxFeature.AssignmentInArray,
        DkonSyntaxLevel.ArrayNamed => GetFeatures(DkonSyntaxLevel.ArrayAssignments) | DkonSyntaxFeature.ArrayNamed,
        DkonSyntaxLevel.References => GetFeatures(DkonSyntaxLevel.ArrayNamed) | DkonSyntaxFeature.ReferenceGraph,
        _ => DkonSyntaxFeature.None,
    };

    public void Scan(DkonNode root)
    {
        this.seen.Add(root);
        var stack = new Stack<NodeFrame>();
        stack.Push(new NodeFrame(root, false));

        while (stack.Count > 0)
        {
            var frame = stack.Pop();

            if (!this.TryEnter(frame.Node, frame.InAxis))
                continue;

            this.Analyze(frame.Node, frame.InAxis);
            this.PushChild(stack, frame.Node.Ref, frame.InAxis);
            this.PushChild(stack, frame.Node.Next, frame.InAxis);
            this.PushChild(stack, frame.Node.Alt, true);
        }
    }

    private bool TryEnter(DkonNode node, bool inAxis)
    {
        var visited = inAxis ? this.seenInsideAxis : this.seenOutsideAxis;

        return visited.Add(node);
    }

    private void Analyze(DkonNode node, bool inAxis)
    {
        this.Features |= DkonSyntaxFeature.Scalar;

        if (node.Bracing == DkonBracing.Multiline)
            this.Features |= DkonSyntaxFeature.ScalarMultiline;

        if (node.Ref is not null)
        {
            this.Features |= DkonSyntaxFeature.Assignment;

            if (node.Ref.Ref is not null)
                this.Features |= DkonSyntaxFeature.AssignmentChain;

            if (inAxis)
                this.Features |= DkonSyntaxFeature.AssignmentInArray;
        }

        if (!inAxis && node.Next is not null)
            this.Features |= DkonSyntaxFeature.Multiline;

        if (node.Alt is not null)
        {
            this.Features |= DkonSyntaxFeature.Array;

            if (inAxis)
                this.Features |= DkonSyntaxFeature.ArrayNested;

            if (!node.IsEmptyValue || node.Bracing != DkonBracing.Bare)
                this.Features |= DkonSyntaxFeature.ArrayNamed;
        }
    }

    private void PushChild(Stack<NodeFrame> stack, DkonNode? node, bool inAxis)
    {
        if (node is null)
            return;

        if (!this.seen.Add(node))
            this.Features |= DkonSyntaxFeature.ReferenceGraph;

        stack.Push(new NodeFrame(node, inAxis));
    }

    private static bool Has(DkonSyntaxFeature value, DkonSyntaxFeature flag) => (value & flag) == flag;

    private readonly record struct NodeFrame(DkonNode Node, bool InAxis);
}
