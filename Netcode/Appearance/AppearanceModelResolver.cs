using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// Maps a session member to the character model they play, for games whose
    /// <see cref="SessionMember.AvatarData"/> is an appearance model id.
    /// </summary>
    public static class AppearanceModelResolver {
        /// <summary>
        /// The model <paramref name="avatarData"/> names when the catalog knows it, else the catalog's
        /// default — so a stale or hostile join value still yields a playable model.
        /// </summary>
        public static ushort FromAvatarData(ushort avatarData, IAppearanceCatalog catalog) {
            if (catalog == null) {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (avatarData != 0 && catalog.TryGetModel(avatarData, out _)) {
                return avatarData;
            }

            return catalog.DefaultModelId;
        }

        /// <summary>
        /// Finds the connected member on <paramref name="peerId"/>. Rejoin reservations hold no peer and
        /// never match.
        /// </summary>
        public static bool TryFindMember(IReadOnlyList<SessionMember> members, int peerId, out SessionMember member) {
            member = null;
            if (members == null || peerId == SessionMember.NoPeerId) {
                return false;
            }

            for (int index = 0; index < members.Count; index++) {
                SessionMember candidate = members[index];
                if (candidate == null || !candidate.IsConnected || candidate.PeerId != peerId) continue;

                member = candidate;
                return true;
            }

            return false;
        }
    }
}
