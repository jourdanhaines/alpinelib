namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// Which primitive a <see cref="CollisionShape"/> is. The numeric values are a wire
    /// contract — they are written into every exported <c>.geo</c> file — so append new kinds at the end
    /// and never renumber the existing ones.
    /// </summary>
    /// <remarks>
    /// A short list of fixed-size, closed-form primitives is the whole vocabulary on purpose. A trimesh
    /// collider would drag an acceleration structure, a triangle-order dependency and a pile of
    /// degenerate-case arithmetic onto the sim path, and every one of those is a place where the server and
    /// the client could disagree by a last bit. The prism is still such a primitive: at most sixteen plan
    /// vertices stored inline, tested by one fixed loop; the sloped prism adds one plane to it. The exporter rejects mesh and terrain colliders.
    /// </remarks>
    public enum CollisionShapeType : byte {
        /// <summary>An infinite horizontal plane at <c>Center.Y</c>. The cheap default floor.</summary>
        Plane = 0,

        /// <summary>An oriented box, described by a precomputed orthonormal basis and half extents.</summary>
        Box = 1,

        /// <summary>A sphere at <c>Center</c> with <c>Radius</c>.</summary>
        Sphere = 2,

        /// <summary>A capsule: a segment of <c>HalfLength</c> either side of <c>Center</c> along <c>AxisUp</c>, swept by <c>Radius</c>.</summary>
        Capsule = 3,

        /// <summary>
        /// A vertical convex prism: a 3–16 vertex convex plan (<c>Plan</c>, offsets from <c>Center</c> in world
        /// XZ) extruded <c>HalfExtents.Y</c> either side of <c>Center.Y</c>.
        /// </summary>
        Prism = 4,

        /// <summary>
        /// A convex prism with a flat bottom at <c>BottomY</c>, vertical sides on a 3–16 vertex <c>Plan</c> and a planar
        /// top <c>y = TopY0 + TopSlopeX·(x − Center.X) + TopSlopeZ·(z − Center.Z)</c>, at most 45° steep. Still a
        /// closed-form primitive: one plan loop, one plane, one square root for its normal.
        /// </summary>
        SlopedPrism = 5
    }
}
