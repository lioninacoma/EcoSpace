using System.Collections.Generic;

/// <summary>
/// Hierarchy-aware position math in UniVec3 form — read-only consumer of GameObjects.
///
/// Strategy: to compute the vector between two objects in different coordinate spaces,
/// find their lowest-common ancestor (LCA), bring BOTH positions into the LCA's
/// child-frame as UniVec3 via a per-level Convert+add walk (the same pattern as
/// GameWorld.ChildPosToParentSpace), then SUBTRACT the two SAME-SCALE UniVec3 in the
/// LCA frame. Only after this exact integer Units cancellation is the small resultant
/// delta collapsed to metres via a SINGLE ToDouble3() call.
///
/// Why this matters: UniVec3.Convert() and cross-scale operator-/operator+ route through
/// EnsureSameScale → Convert → ToDouble3(), which ZEROES the Long3.Units and collapses
/// everything into a single double. An absolute-from-root value at Universe scale can be
/// ~1e30 m — far beyond the ~1e15 precision of a 64-bit double — so naive cross-scale
/// subtraction produces catastrophic cancellation. The LCA walk avoids ever forming the
/// large absolute-from-root value; each per-level step is SOI-bounded (≤ ~1e17 m) and
/// therefore precise.
///
/// Read-only consumer contract: MUST NOT mutate any UniObject, LocalPos, ChildIndices,
/// or call TranslatePos. Every method takes UniObject values and List&lt;UniObject&gt; by
/// reference only for reading.
///
/// Direction convention: <see cref="RelativePosition"/>(from, to) returns to − from
/// (the vector FROM 'from' TO 'to') in the LCA frame. Callers that need the ship→body
/// vector should call RelativePosition(ship, body, …).
///
/// WARNING — do NOT replace ToAncestorFrame's per-level Convert+add with a naive cross-scale
/// UniVec3 operator- (e.g. <c>toUni - fromUni</c> across different spaces): cross-scale
/// operator- routes through Convert → ToDouble3() which zeroes Long3 Units and collapses
/// absolute-from-root values to a single double, re-introducing catastrophic cancellation
/// at Universe scale. Always go through the LCA path in this class.
/// </summary>
public static class UniMath
{
    // ── LCA discovery ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the index of the lowest-common ancestor (LCA) of <paramref name="a"/> and
    /// <paramref name="b"/> in the UniObject hierarchy.
    ///
    /// Algorithm: build a HashSet of all of <paramref name="a"/>'s ancestor indices
    /// (including a itself) by walking ParentIndex upward; then walk <paramref name="b"/>'s
    /// chain upward, returning the first index found in that set — that is the LCA.
    /// Returns -1 if no common ancestor is found (should be impossible in a valid hierarchy —
    /// all objects share Root).
    ///
    /// T-02-02 mitigation: every loop step is guarded by (uint) bounds check and null guard;
    /// hierarchy depth ≤ 5. Read-only — MUST NOT mutate any UniObject.
    /// </summary>
    public static int FindLca(UniObject a, UniObject b, List<UniObject> objs)
    {
        // Build the ancestor set for 'a' (including 'a' itself).
        // Hierarchy depth ≤ 5 so a HashSet is acceptably cheap.
        var aAncestors = new System.Collections.Generic.HashSet<int>();
        int idx = a.Index;
        while ((uint)idx < (uint)objs.Count && objs[idx] != null)
        {
            aAncestors.Add(idx);
            int parentIdx = objs[idx].ParentIndex;
            if (parentIdx == idx) break;   // cycle guard (Root normally has ParentIndex = -1)
            idx = parentIdx;
        }

        // Walk 'b' upward until we hit an index in aAncestors.
        idx = b.Index;
        while ((uint)idx < (uint)objs.Count && objs[idx] != null)
        {
            if (aAncestors.Contains(idx)) return idx;
            int parentIdx = objs[idx].ParentIndex;
            if (parentIdx == idx) break;   // cycle guard
            idx = parentIdx;
        }

        return -1;   // no common ancestor found (should not occur in a valid hierarchy)
    }

    // ── Frame accumulation ─────────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="node"/>.LocalPos expressed in the
    /// ancestor at <paramref name="ancestorIdx"/>'s child-frame as a UniVec3.
    ///
    /// The accumulation is performed IN UNIVEC3 — the key difference from the old
    /// metres-based PositionRelativeToAncestor helpers. Each per-level step follows
    /// the exact pattern used in GameWorld.ChildPosToParentSpace:
    ///   pos = pos.Convert(parentScale) + parent.LocalPos
    /// Each Convert(newScale) produces a small SOI-bounded value (≤ ~1e17 m) then
    /// Normalize() re-splits it into integer Units — so integer precision is restored
    /// after every level rather than accumulating floating-point error.
    ///
    /// Special case: if <paramref name="node"/>.Index == <paramref name="ancestorIdx"/>,
    /// the node IS the ancestor; its contribution in its own child-frame is zero, so we
    /// return the zero vector at the ancestor's child-scale.
    ///
    /// Root guard: <c>UniObject.Scale(Space.Root)</c> returns -1; the <c>parentScale &lt;= 0</c>
    /// break ensures Root is never multiplied in, even if the LCA walk overshoots.
    ///
    /// T-02-02 mitigation: loop guarded by (uint) bounds check, null guard, and parentScale guard.
    /// Read-only — MUST NOT mutate any UniObject.
    /// </summary>
    public static UniVec3 ToAncestorFrame(UniObject node, int ancestorIdx, List<UniObject> objs)
    {
        // If the node IS the ancestor, it contributes no offset in its own child-frame.
        // Return the zero vector at the ancestor's child-scale (one level BELOW the ancestor).
        if (node.Index == ancestorIdx)
            return new UniVec3(Double3.Zero, UniObject.Scale(node.CurrentSpace));

        // Start with the node's own LocalPos (its position in its immediate parent's child-frame).
        UniVec3 pos = node.LocalPos;
        int pIdx = node.ParentIndex;

        // Walk upward, applying the per-level Convert+add pattern at each step.
        // Stop when pIdx reaches the ancestor — at that point pos is already in the
        // ancestor's child-frame (we do not need to add the ancestor's own LocalPos).
        while ((uint)pIdx < (uint)objs.Count && objs[pIdx] != null)
        {
            if (pIdx == ancestorIdx) break;   // reached the LCA — pos is in its child-frame

            var parent = objs[pIdx];
            double parentScale = UniObject.Scale(parent.CurrentSpace);
            if (parentScale <= 0) break;   // Root has Scale = -1; never multiply Root in

            // Per-level accumulation: Convert rescales pos to the parent's child-frame scale,
            // then adding parent.LocalPos lifts pos one level up. Normalize() inside the
            // UniVec3 constructor re-splits offset into integer Units after each step,
            // restoring integer precision. Each step is SOI-bounded → precise.
            pos = pos.Convert(parentScale) + parent.LocalPos;
            pIdx = parent.ParentIndex;
        }

        return pos;   // in the ancestor's child-frame, with integer Units intact
    }

    // ── Relative position and metres ───────────────────────────────────────

    /// <summary>
    /// Returns the vector from <paramref name="from"/> to <paramref name="to"/> as a
    /// UniVec3 expressed in their lowest-common ancestor's child-frame.
    ///
    /// This is the PRIMARY precision primitive. Both positions are walked to the LCA via
    /// <see cref="ToAncestorFrame"/>, producing two UniVec3 at the SAME child-scale.
    /// Subtracting them (<c>toFrame - fromFrame</c>) uses UniVec3.operator-, which performs
    /// EXACT integer Units cancellation (<c>a.Units - b.Units</c> is a 64-bit integer
    /// subtraction regardless of magnitude). The enormous common-ancestor offset that both
    /// vectors share is never explicitly formed — it cancels exactly in the integer Units.
    ///
    /// Direction: returns <c>to − from</c> (the vector FROM <paramref name="from"/>
    /// TO <paramref name="to"/>). To get ship→body, call
    /// <c>RelativePosition(ship, body, …)</c>.
    ///
    /// Returns <c>false</c> (result = zero sentinel) when no common ancestor is found —
    /// callers treat this as the coincident/disconnected case. The zero sentinel uses
    /// the Universe child-scale (Galaxy scale) as an arbitrary but documented fallback.
    ///
    /// Do NOT call ToDouble3() inside this method — the caller should use
    /// <see cref="RelativeMetres"/> when metres are needed.
    /// </summary>
    public static bool RelativePosition(
        UniObject from, UniObject to, List<UniObject> objs, out UniVec3 result)
    {
        int lca = FindLca(from, to, objs);
        if (lca < 0)
        {
            // No common ancestor — return a documented zero sentinel.
            // Scale chosen as Galaxy child-scale (Star scale) as a safe arbitrary fallback.
            result = new UniVec3(Double3.Zero, UniObject.Scale(UniObject.Space.Star));
            return false;
        }

        // Bring both ends into the LCA's child-frame as UniVec3.
        UniVec3 toFrame   = ToAncestorFrame(to,   lca, objs);
        UniVec3 fromFrame = ToAncestorFrame(from,  lca, objs);

        // Subtract at the same scale → EXACT integer Units cancellation.
        // The large common-ancestor offset shared by both operands cancels to zero
        // in the Long3 integer subtraction, leaving only the small inter-body delta.
        // This is the precision superpower: correct even at intergalactic separation.
        result = toFrame - fromFrame;
        return true;
    }

    /// <summary>
    /// Returns the vector from <paramref name="from"/> to <paramref name="to"/> in metres
    /// (Double3). This is the ONE sanctioned metres conversion in the precision model:
    /// <see cref="RelativePosition"/> is called first to produce the small differenced
    /// UniVec3 in the LCA frame, then <c>ToDouble3()</c> is applied ONCE to that small
    /// vector. Applying ToDouble3 to a small SOI-bounded delta (not an absolute-from-root
    /// accumulation) is safe and precise.
    ///
    /// Returns <c>Double3.Zero</c> when no common ancestor is found.
    ///
    /// Direction: returns <c>to − from</c> metres (vector FROM <paramref name="from"/>
    /// TO <paramref name="to"/>). For ship→body, call <c>RelativeMetres(ship, body, …)</c>.
    /// </summary>
    public static Double3 RelativeMetres(UniObject from, UniObject to, List<UniObject> objs)
        => RelativePosition(from, to, objs, out var rel) ? rel.ToDouble3() : Double3.Zero;

    // ── Distance ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the scalar distance in metres between <paramref name="a"/> and
    /// <paramref name="b"/>, computed via the LCA-relative UniVec3 path for full precision.
    /// Returns 0.0 when no common ancestor is found.
    /// </summary>
    public static double Distance(UniObject a, UniObject b, List<UniObject> objs)
        => RelativeMetres(a, b, objs).Magnitude();

    // ── Direction ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the unit direction vector from <paramref name="from"/> to <paramref name="to"/>
    /// as a Double3, safe at any scale including intergalactic distances.
    ///
    /// Strategy: normalize from the relative UniVec3's integer Units (Long3) when non-zero.
    /// All three components share the same Scale factor, so it cancels in the normalize — only
    /// the integer ratios matter. int64 → double cast is exact (int64 fits in double for values
    /// up to ~2^53 ≈ 9e15 units). This avoids the ~3e15 m float-cast error that occurs when
    /// converting raw 2.4e22 m components to float before normalizing.
    ///
    /// Falls back to Offset (sub-unit range) when Units are all zero.
    /// Returns <see cref="Double3.Zero"/> when the objects coincide.
    /// </summary>
    public static Double3 NormalizedDirection(UniObject from, UniObject to, List<UniObject> objs)
    {
        if (!RelativePosition(from, to, objs, out UniVec3 rel))
            return Double3.Zero;

        // Units all zero: sub-unit distance, fall back to Offset in metres.
        Double3 u = rel.ToDouble3Units();
        double mag = u.Magnitude();
        return mag < 1e-3 ? Double3.Zero : new Double3(u.X / mag, u.Y / mag, u.Z / mag);
    }
}
