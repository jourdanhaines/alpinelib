using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AlpineLib.Sessions {
    /// <summary>
    /// A path as the filesystem lays it out, every link followed — the question a launcher and a build
    /// step both ask when the folder they were handed may be a link to somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>realpath(3)</c> follows every component of the path, ancestors included. It is reached through
    /// libc because the .NET 6 link API answers for the last component only and Unity's scripting
    /// runtime does not have it at all. The path is marshalled as UTF-8 bytes so a project living under a
    /// non-ASCII path is not mangled by the default charset, and the buffer <c>realpath(NULL)</c>
    /// allocates is freed.
    /// </para>
    /// <para>
    /// Deliberately not the working directory: <c>chdir(2)</c> is process-global, so every other thread
    /// in the editor — an in-flight import, a launched process inheriting the directory — would resolve
    /// its own relative paths against the folder for as long as it was set. Windows has no libc, so the
    /// call fails there and the caller keeps the path as written.
    /// </para>
    /// </remarks>
    public static class PhysicalPath {
        /// <summary>
        /// The physical location of <paramref name="path"/>, or null when this runtime cannot report one.
        /// </summary>
        /// <remarks>
        /// The path need not exist — a publish destination does not on a first build — so the walk climbs
        /// to the deepest folder that does exist, resolves that, and puts the rest back on the end. A
        /// link anywhere above the leaf moves the leaf just as surely as one at it, which is why the
        /// whole path is resolved rather than its last component. Resolve the link before climbing out
        /// of it: <c>..</c> is normalised lexically first, so <c>link/..</c> names the link's parent
        /// rather than its target's.
        /// </remarks>
        public static string Resolve(string path) {
            if (string.IsNullOrEmpty(path)) return null;

            string fullPath = Path.GetFullPath(path);

            if (Directory.Exists(fullPath)) return ResolveDirectory(fullPath);

            string parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent)) return fullPath;

            string resolvedParent = Resolve(parent);
            if (resolvedParent == null) return null;

            return Path.Combine(resolvedParent, Path.GetFileName(fullPath));
        }

        /// <summary>An existing directory's physical location, or null when libc cannot be asked.</summary>
        private static string ResolveDirectory(string directory) {
            try {
                IntPtr resolved = ResolveRealPath(Encoding.UTF8.GetBytes(directory + "\0"), IntPtr.Zero);
                if (resolved == IntPtr.Zero) return null;

                try {
                    return Marshal.PtrToStringUTF8(resolved);
                } finally {
                    FreeRealPath(resolved);
                }
            } catch (Exception) {
                // No libc, or no such entry point: the path as written is that platform's answer.
                return null;
            }
        }

        [DllImport("libc", EntryPoint = "realpath")]
        private static extern IntPtr ResolveRealPath(byte[] path, IntPtr resolved);

        [DllImport("libc", EntryPoint = "free")]
        private static extern void FreeRealPath(IntPtr pointer);
    }
}
