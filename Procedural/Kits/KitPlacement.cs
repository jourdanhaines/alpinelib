using System;

namespace AlpineLib.Procedural.Kits {
    /// <summary>One kit part to instantiate: which part, what to call the instance, which group it joins, and where.</summary>
    public sealed class KitPlacement {
        /// <summary>
        /// Builds a placement. <paramref name="partName"/> is required; a null instance name or group
        /// becomes empty.
        /// </summary>
        public KitPlacement(string partName, string instanceName, string group, KitPose pose) {
            if (string.IsNullOrEmpty(partName)) {
                throw new ArgumentException("A placement needs a part name.", nameof(partName));
            }
            PartName = partName;
            InstanceName = instanceName ?? string.Empty;
            Group = group ?? string.Empty;
            Pose = pose;
        }

        /// <summary>The kit part to instantiate.</summary>
        public string PartName { get; }

        /// <summary>The name the instance should carry.</summary>
        public string InstanceName { get; }

        /// <summary>The group (parent bucket) the instance belongs to.</summary>
        public string Group { get; }

        /// <summary>Where the part sits in the kit frame.</summary>
        public KitPose Pose { get; }

        /// <summary>"Group/InstanceName (PartName) pose".</summary>
        public override string ToString() {
            return $"{Group}/{InstanceName} ({PartName}) {Pose}";
        }
    }
}
