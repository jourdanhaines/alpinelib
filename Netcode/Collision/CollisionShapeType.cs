namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// Which of the four primitives a <see cref="CollisionShape"/> is. The numeric values are a wire
    /// contract — they are written into every exported <c>.geo</c> file — so append new kinds at the end
    /// and never renumber the existing ones.
    /// </summary>
    /// <remarks>
    /// Four primitives is the whole vocabulary on purpose. A trimesh collider would drag an acceleration
    /// structure, a triangle-order dependency and a pile of degenerate-case arithmetic onto the sim path,
    /// and every one of those is a place where the server and the client could disagree by a last bit.
    /// The exporter therefore rejects mesh and terrain colliders outright rather than approximating them.
    /// </remarks>
    public enum CollisionShapeType : byte {
        /// <summary>An infinite horizontal plane at <c>Center.Y</c>. The cheap default floor.</summary>
        Plane = 0,

        /// <summary>An oriented box, described by a precomputed orthonormal basis and half extents.</summary>
        Box = 1,

        /// <summary>A sphere at <c>Center</c> with <c>Radius</c>.</summary>
        Sphere = 2,

        /// <summary>A capsule: a segment of <c>HalfLength</c> either side of <c>Center</c> along <c>AxisUp</c>, swept by <c>Radius</c>.</summary>
        Capsule = 3
    }
}
