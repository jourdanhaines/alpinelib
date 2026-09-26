namespace AlpineLib.Procedural.Kits {
    /// <summary>How a layout's kit frame maps onto Unity's axes.</summary>
    public enum KitAxisConvention {
        /// <summary>Kit axes are Unity axes; yaw is Unity's own yaw about +Y.</summary>
        UnityNative,

        /// <summary>
        /// Kit frame is Blender's (x across, y along, z up, yaw counter-clockwise from above) as the FBX
        /// importer lands it: Unity = (−x, z, −y), yaw about +Y = −θ.
        /// </summary>
        BlenderZUp
    }
}
