using System;
using System.IO;
using AlpineLib.Netcode.Sessions;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// Where the local player's stable identity comes from and goes back to.
    /// </summary>
    /// <remarks>
    /// The player id must survive quitting the game: a rejoin is recognised by the server matching the
    /// id against a roster reservation, so a player who reconnects with a fresh id is a new stranger
    /// rather than the member whose pawn is being held for them.
    /// </remarks>
    public interface IIdentityStore {
        /// <summary>
        /// Reads the stored identity, minting and persisting a new one the first time.
        /// </summary>
        /// <param name="defaultDisplayName">Name given to a freshly minted identity.</param>
        PlayerIdentity Load(string defaultDisplayName);

        /// <summary>Writes an identity back, replacing whatever was stored.</summary>
        void Save(PlayerIdentity identity);
    }

    /// <summary>
    /// Keeps the identity in a small JSON file under <see cref="Application.persistentDataPath"/>.
    /// </summary>
    /// <remarks>
    /// JSON — and <see cref="JsonUtility"/> — appear here and nowhere below: the shared netcode
    /// assemblies carry no serializer but their own binary writer, and identity on the wire travels
    /// through that. This file is a local convenience, read once at startup and rewritten when the
    /// player renames themselves, so its shape is nobody's compatibility contract but this class's.
    ///
    /// Two instances of the game on one machine share a persistent data path, and a shared identity
    /// file would hand both of them the same player id — which the server rightly refuses, because one
    /// player cannot occupy two seats. So the default store first claims an instance slot, guarded by
    /// an exclusively-opened lock file held for the life of the process: the first instance claims slot
    /// 0 and the historical <see cref="DefaultFileName"/>, each further instance claims the next slot
    /// and its own identity file, and therefore its own stable id. A lone instance always lands on
    /// slot 0, so ordinary players never see any of this. Platform accounts (Steam) will replace this
    /// store outright — an account already is a machine-independent identity.
    ///
    /// Every failure path degrades to a fresh identity rather than throwing. A corrupt or unreadable
    /// file must not stop the game from starting; the worst case is that one player's rejoin
    /// reservation is missed once.
    /// </remarks>
    public class FileIdentityStore : IIdentityStore {
        /// <summary>File name used when the caller does not name one.</summary>
        public const string DefaultFileName = "identity.json";

        /// <summary>How many same-machine instances get distinct identities before they start sharing.</summary>
        public const int MaxInstanceSlots = 8;

        private static FileStream _slotLock;
        private static int _claimedSlot = -1;

        private readonly string _filePath;

        /// <summary>
        /// Creates a store over this instance's slot file in the persistent data path — slot 0 is
        /// <see cref="DefaultFileName"/>; concurrent instances each get their own file and id.
        /// </summary>
        public FileIdentityStore()
            : this(Path.Combine(Application.persistentDataPath, FileNameForSlot(ClaimInstanceSlot()))) { }

        /// <summary>Creates a store over an explicit file path, bypassing slot claiming.</summary>
        public FileIdentityStore(string filePath) {
            _filePath = filePath;
        }

        /// <summary>Absolute path of the file this store reads and writes.</summary>
        public string FilePath => _filePath;

        /// <inheritdoc />
        public PlayerIdentity Load(string defaultDisplayName) {
            IdentityRecord record = ReadRecord();

            if (record != null && PlayerId.TryParse(record.playerId, out PlayerId storedId)) {
                string storedName = string.IsNullOrWhiteSpace(record.displayName)
                    ? defaultDisplayName
                    : record.displayName;

                return new PlayerIdentity(storedId, PlayerIdentity.Sanitize(storedName), AuthMethod.Anonymous);
            }

            var identity = new PlayerIdentity(
                PlayerId.NewId(), PlayerIdentity.Sanitize(defaultDisplayName), AuthMethod.Anonymous
            );

            Save(identity);
            return identity;
        }

        /// <inheritdoc />
        public void Save(PlayerIdentity identity) {
            if (identity == null) {
                Debug.LogError("FileIdentityStore::Save->No identity to store.");
                return;
            }

            var record = new IdentityRecord {
                playerId = identity.PlayerId.ToString(),
                displayName = identity.DisplayName
            };

            try {
                string directory = Path.GetDirectoryName(_filePath);

                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_filePath, JsonUtility.ToJson(record, true));
            } catch (Exception error) {
                Debug.LogWarning($"FileIdentityStore::Save->Could not write {_filePath}: {error.Message}");
            }
        }

        /// <summary>Identity file name for a claimed slot; slot 0 keeps the historical name.</summary>
        public static string FileNameForSlot(int slot) {
            if (slot <= 0) return DefaultFileName;

            return $"identity.{slot.ToString()}.json";
        }

        /// <summary>
        /// Claims the lowest free instance slot for this process, once; later calls reuse the claim.
        /// The lock stream is deliberately never disposed — the OS releases it when the process (or,
        /// in the editor, the reloaded domain's finalizers) lets go, which is exactly the lifetime a
        /// running instance's claim should have.
        /// </summary>
        private static int ClaimInstanceSlot() {
            if (_claimedSlot >= 0) return _claimedSlot;

            string directory = Application.persistentDataPath;

            try {
                Directory.CreateDirectory(directory);
            } catch (Exception error) {
                Debug.LogWarning($"FileIdentityStore::ClaimInstanceSlot->Could not create {directory}: {error.Message}");
                _claimedSlot = 0;
                return _claimedSlot;
            }

            for (int slot = 0; slot < MaxInstanceSlots; slot++) {
                if (!TryLockSlot(directory, slot)) continue;

                _claimedSlot = slot;
                GC.KeepAlive(_slotLock);
                if (slot > 0) {
                    Debug.Log($"FileIdentityStore::ClaimInstanceSlot->Another instance holds the primary identity; this one is instance {slot.ToString()} using '{FileNameForSlot(slot)}'.");
                }

                return _claimedSlot;
            }

            // Every slot is held. Sharing an id and being refused a seat beats failing to start.
            Debug.LogWarning("FileIdentityStore::ClaimInstanceSlot->All identity slots are locked; falling back to the shared identity file.");
            _claimedSlot = 0;
            return _claimedSlot;
        }

        /// <remarks>
        /// Two mechanisms, because neither is honoured everywhere: the exclusive share mode is enforced
        /// across processes on Windows but silently ignored by Mono on Linux and macOS, and the byte
        /// lock is a POSIX <c>fcntl</c> advisory lock that those platforms do enforce. A slot counts as
        /// claimed only when both accept — a platform that supports neither degrades to today's shared
        /// identity rather than refusing to start.
        /// </remarks>
        private static bool TryLockSlot(string directory, int slot) {
            string lockPath = Path.Combine(directory, $"identity.slot{slot.ToString()}.lock");
            FileStream probe = null;

            try {
                probe = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                LockFirstByte(probe);
                _slotLock = probe;
                return true;
            } catch (IOException) {
                probe?.Dispose();
                return false;
            } catch (Exception error) {
                Debug.LogWarning($"FileIdentityStore::TryLockSlot->Could not probe {lockPath}: {error.Message}");
                probe?.Dispose();
                return false;
            }
        }

        private static void LockFirstByte(FileStream probe) {
            try {
                probe.Lock(0, 1);
            } catch (IOException) {
                throw;
            } catch (Exception) {
                // Byte locks unsupported here; the share mode above is the only guard this platform gets.
            }
        }

        /// <summary>Reads the stored record, or null when there is nothing usable on disk.</summary>
        private IdentityRecord ReadRecord() {
            try {
                if (!File.Exists(_filePath)) return null;

                return JsonUtility.FromJson<IdentityRecord>(File.ReadAllText(_filePath));
            } catch (Exception error) {
                Debug.LogWarning($"FileIdentityStore::ReadRecord->Could not read {_filePath}: {error.Message}");
                return null;
            }
        }

        /// <summary>
        /// On-disk shape. Nested and private because nothing outside this store may depend on it, and
        /// serialized by field name because that is all <see cref="JsonUtility"/> understands.
        /// </summary>
        [Serializable]
        private class IdentityRecord {
            public string playerId;
            public string displayName;
        }
    }
}
