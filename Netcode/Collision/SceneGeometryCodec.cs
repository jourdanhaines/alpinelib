using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The one binary form of a <see cref="SceneGeometry"/>, written by the Unity exporter and read by
    /// the dedicated server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One codec, one format, two consumers. The exporter writes these bytes twice — once to a
    /// <c>.geo</c> file that ships in the server's config directory, once into a ScriptableObject the
    /// client loads with the scene — so there is no chance of the two ends parsing different files with
    /// different readers. It goes through <c>NetWriter</c>/<c>NetReader</c> like every other wire form in
    /// the project, for the same reason: one endianness, one place floats are laid out.
    /// </para>
    /// <para>
    /// The header is a magic number and a format version, which are separate from
    /// <c>NetProtocol.Version</c> on purpose: a <c>.geo</c> file is an artefact on disk that outlives the
    /// build that wrote it, and refusing to load one with a clear message beats misreading it.
    /// </para>
    /// <para>
    /// The layout is: magic, format version, scene name, content hash, then the <i>body</i> — the shape
    /// count and its shapes, the mover count and its movers. The body is written by exactly one method,
    /// <c>WriteBody</c>, and <see cref="ComputeContentHash"/> hashes the bytes that method produces. That
    /// is deliberate: it makes "canonical field order" a single fact about the code rather than a comment
    /// two implementations have to keep agreeing with, and it means a field added to the format is
    /// automatically covered by the hash.
    /// </para>
    /// <para>
    /// <see cref="Encode"/> always writes a freshly computed hash and <see cref="Decode"/> always verifies
    /// the one it read. Geometry is the shared truth of the simulation: two ends running subtly different
    /// worlds diverge in a way that looks like netcode jitter and takes days to trace, so a corrupt file
    /// has to fail at load, loudly, rather than half-decode into a world that is almost right.
    /// </para>
    /// </remarks>
    public static class SceneGeometryCodec {
        /// <summary>File magic, the ASCII bytes <c>AGO1</c> — "alpine geometry, one".</summary>
        public const uint Magic = 0x41474F31u;

        /// <summary>Layout version of the payload that follows the header. Bump on any field change.</summary>
        public const ushort FormatVersion = 1;

        private const uint Fnv1aOffsetBasis = 2166136261u;
        private const uint Fnv1aPrime = 16777619u;

        /// <summary>Magic, format version and content hash. The scene name's length is not fixed.</summary>
        private const int FixedHeaderByteCount = 4 + 2 + 4;

        /// <summary>A serialized <see cref="CollisionShape"/>: a type byte, five vectors, two floats.</summary>
        private const int ShapeByteCount = 1 + (5 * 12) + (2 * 4);

        /// <summary>A mover's two ids plus its local shape, before the path.</summary>
        private const int MoverHeaderByteCount = 2 + 2 + ShapeByteCount;

        /// <summary>What follows a path's waypoints: speed, loop mode, phase.</summary>
        private const int PathTailByteCount = 4 + 1 + 4;

        /// <summary>One waypoint, at full precision.</summary>
        private const int WaypointByteCount = 12;

        /// <summary>Widest a var-uint can get. Counts are written as var-uints and are almost always one byte.</summary>
        private const int MaxVarUIntByteCount = 5;

        /// <summary>Smallest a mover can encode to: header, an empty waypoint count, the tail.</summary>
        private const int MinimumMoverByteCount = MoverHeaderByteCount + 1 + PathTailByteCount;

        /// <summary>
        /// Writes a scene's geometry to its portable byte form. The content hash written is recomputed
        /// from the shapes and movers rather than copied off <paramref name="geometry"/>, so an exporter
        /// that forgot to fill the field in cannot ship a file that fails its own integrity check.
        /// </summary>
        public static byte[] Encode(SceneGeometry geometry) {
            if (geometry == null) {
                throw new ArgumentNullException(nameof(geometry));
            }

            var buffer = new byte[MaxEncodedByteCount(geometry)];
            var writer = new NetWriter(buffer);
            writer.WriteUInt(Magic);
            writer.WriteUShort(FormatVersion);
            writer.WriteString(geometry.SceneName);
            writer.WriteUInt(ComputeContentHash(geometry));
            WriteBody(ref writer, geometry);
            return writer.ToArray();
        }

        /// <summary>
        /// Reads geometry back. Throws <c>NetProtocolException</c> on a bad magic, an unknown format
        /// version or a truncated payload — a half-read world is worse than no world.
        /// </summary>
        public static SceneGeometry Decode(byte[] payload) {
            if (payload == null) {
                throw new ArgumentNullException(nameof(payload));
            }

            var reader = new NetReader(payload);
            ReadHeader(ref reader);
            string sceneName = reader.ReadString();
            uint storedContentHash = reader.ReadUInt();
            CollisionShape[] staticShapes = ReadShapes(ref reader);
            MoverDefinition[] movers = ReadMovers(ref reader);

            var geometry = new SceneGeometry(sceneName, storedContentHash, staticShapes, movers);
            VerifyContentHash(geometry, storedContentHash);
            return geometry;
        }

        /// <summary>
        /// FNV-1a over the shapes and movers in index order, ignoring the scene name and the stored hash.
        /// Both ends compute it the same way, so comparing it proves they loaded the same world.
        /// </summary>
        public static uint ComputeContentHash(SceneGeometry geometry) {
            if (geometry == null) {
                throw new ArgumentNullException(nameof(geometry));
            }

            var buffer = new byte[MaxBodyByteCount(geometry)];
            var writer = new NetWriter(buffer);
            WriteBody(ref writer, geometry);
            return Fnv1a(writer.AsSpan());
        }

        /// <summary>
        /// The hashed and round-tripped part of the format: counts and their items, nothing else. Both
        /// <see cref="Encode"/> and <see cref="ComputeContentHash"/> go through here, which is what keeps
        /// the hash's field order and the file's field order the same order by construction.
        /// </summary>
        private static void WriteBody(ref NetWriter writer, SceneGeometry geometry) {
            CollisionShape[] staticShapes = geometry.StaticShapes;
            writer.WriteVarUInt((uint)staticShapes.Length);
            for (int shapeIndex = 0; shapeIndex < staticShapes.Length; shapeIndex++) {
                writer.WriteMessage(in staticShapes[shapeIndex]);
            }

            MoverDefinition[] movers = geometry.Movers;
            writer.WriteVarUInt((uint)movers.Length);
            for (int moverIndex = 0; moverIndex < movers.Length; moverIndex++) {
                WriteMover(ref writer, movers[moverIndex]);
            }
        }

        private static void WriteMover(ref NetWriter writer, MoverDefinition mover) {
            if (mover == null || mover.Path == null) {
                throw new ArgumentException("Scene geometry holds a mover with no definition or no path.", nameof(mover));
            }

            writer.WriteUShort(mover.MoverId);
            writer.WriteUShort(mover.PrefabId);
            CollisionShape localShape = mover.LocalShape;
            writer.WriteMessage(in localShape);
            WritePath(ref writer, mover.Path);
        }

        private static void WritePath(ref NetWriter writer, MoverPath path) {
            Vector3[] waypoints = path.Waypoints;
            writer.WriteVarUInt((uint)waypoints.Length);
            for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++) {
                writer.WriteVector3(waypoints[waypointIndex]);
            }

            writer.WriteFloat(path.Speed);
            writer.WriteByte((byte)path.LoopMode);
            writer.WriteUInt(path.PhaseTicks);
        }

        private static void ReadHeader(ref NetReader reader) {
            uint magic = reader.ReadUInt();
            if (magic != Magic) {
                throw new NetProtocolException(
                    "Scene geometry has magic 0x" + magic.ToString("X8", CultureInfo.InvariantCulture) +
                    ", expected 0x" + Magic.ToString("X8", CultureInfo.InvariantCulture) + ".");
            }

            ushort formatVersion = reader.ReadUShort();
            if (formatVersion != FormatVersion) {
                throw new NetProtocolException(
                    "Scene geometry is format version " + formatVersion.ToString(CultureInfo.InvariantCulture) +
                    ", this build reads version " + FormatVersion.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private static CollisionShape[] ReadShapes(ref NetReader reader) {
            int shapeCount = ReadCount(ref reader, ShapeByteCount, "static shape");
            var shapes = new CollisionShape[shapeCount];
            for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++) {
                shapes[shapeIndex] = reader.ReadMessage<CollisionShape>();
            }

            return shapes;
        }

        private static MoverDefinition[] ReadMovers(ref NetReader reader) {
            int moverCount = ReadCount(ref reader, MinimumMoverByteCount, "mover");
            var movers = new MoverDefinition[moverCount];
            for (int moverIndex = 0; moverIndex < moverCount; moverIndex++) {
                movers[moverIndex] = ReadMover(ref reader);
            }

            return movers;
        }

        private static MoverDefinition ReadMover(ref NetReader reader) {
            ushort moverId = reader.ReadUShort();
            ushort prefabId = reader.ReadUShort();
            CollisionShape localShape = reader.ReadMessage<CollisionShape>();
            MoverPath path = ReadPath(ref reader);
            return new MoverDefinition(moverId, prefabId, in localShape, path);
        }

        private static MoverPath ReadPath(ref NetReader reader) {
            int waypointCount = ReadCount(ref reader, WaypointByteCount, "waypoint");
            var waypoints = new Vector3[waypointCount];
            for (int waypointIndex = 0; waypointIndex < waypointCount; waypointIndex++) {
                waypoints[waypointIndex] = reader.ReadVector3();
            }

            float speed = reader.ReadFloat();
            var loopMode = (MoverLoopMode)reader.ReadByte();
            uint phaseTicks = reader.ReadUInt();
            return new MoverPath(waypoints, speed, loopMode, phaseTicks);
        }

        /// <summary>
        /// Reads a var-uint count and refuses one the remaining bytes could not possibly satisfy. Without
        /// this a corrupt four-byte count would have us allocate gigabytes before the reader got as far as
        /// noticing the payload had run out.
        /// </summary>
        private static int ReadCount(ref NetReader reader, int minimumBytesPerItem, string itemName) {
            uint count = reader.ReadVarUInt();
            if (count <= (uint)(reader.Remaining / minimumBytesPerItem)) {
                return (int)count;
            }

            throw new NetProtocolException(
                "Scene geometry declares " + count.ToString(CultureInfo.InvariantCulture) + " " + itemName +
                "(s) but only " + reader.Remaining.ToString(CultureInfo.InvariantCulture) + " byte(s) remain.");
        }

        private static void VerifyContentHash(SceneGeometry geometry, uint storedContentHash) {
            uint actualContentHash = ComputeContentHash(geometry);
            if (actualContentHash == storedContentHash) {
                return;
            }

            throw new NetProtocolException(
                "Scene geometry content hash is 0x" + actualContentHash.ToString("X8", CultureInfo.InvariantCulture) +
                " but the file recorded 0x" + storedContentHash.ToString("X8", CultureInfo.InvariantCulture) +
                "; the payload is corrupt.");
        }

        private static uint Fnv1a(ReadOnlySpan<byte> bytes) {
            uint hash = Fnv1aOffsetBasis;
            for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++) {
                hash = (hash ^ bytes[byteIndex]) * Fnv1aPrime;
            }

            return hash;
        }

        /// <summary>
        /// An upper bound on the whole file. Both writers size their buffer from here and then hand back
        /// only what they wrote, so an over-estimate costs one short-lived array and never a resize.
        /// </summary>
        private static int MaxEncodedByteCount(SceneGeometry geometry) {
            int sceneNameByteCount = string.IsNullOrEmpty(geometry.SceneName)
                ? 0
                : Encoding.UTF8.GetByteCount(geometry.SceneName);
            return FixedHeaderByteCount + MaxVarUIntByteCount + sceneNameByteCount + MaxBodyByteCount(geometry);
        }

        private static int MaxBodyByteCount(SceneGeometry geometry) {
            int total = MaxVarUIntByteCount + (geometry.StaticShapes.Length * ShapeByteCount) + MaxVarUIntByteCount;
            MoverDefinition[] movers = geometry.Movers;
            for (int moverIndex = 0; moverIndex < movers.Length; moverIndex++) {
                total += MoverHeaderByteCount + MaxVarUIntByteCount + PathTailByteCount;
                total += WaypointCount(movers[moverIndex]) * WaypointByteCount;
            }

            return total;
        }

        private static int WaypointCount(MoverDefinition mover) {
            if (mover == null || mover.Path == null || mover.Path.Waypoints == null) {
                return 0;
            }

            return mover.Path.Waypoints.Length;
        }
    }
}
