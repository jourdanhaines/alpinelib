using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode {
    /// <summary>
    /// The framing both facades share: a two-byte message id followed by the body, and the reverse.
    /// </summary>
    /// <remarks>
    /// It lives on its own because <see cref="NetServer"/> and <see cref="NetClient"/> must agree on it
    /// byte for byte — a client and a server that framed differently would fail in the least debuggable
    /// way there is, and a single implementation makes that impossible by construction.
    /// </remarks>
    public static class NetEnvelope {
        /// <summary>Bytes the id header occupies at the front of every payload.</summary>
        public const int HeaderSize = 2;

        /// <summary>Writes id plus body into a buffer and reports how many bytes to hand the transport.</summary>
        public static int Frame<TMessage>(byte[] buffer, ushort messageId, in TMessage message)
            where TMessage : struct, INetMessage {
            var writer = new NetWriter(buffer);
            writer.WriteUShort(messageId);
            message.Serialize(ref writer);
            return writer.Written;
        }

        /// <summary>
        /// Writes id plus an already-encoded body, with no length prefix of its own — the datagram
        /// boundary is the length, which is what lets the chat codec own its own framing entirely.
        /// </summary>
        public static int FrameRaw(byte[] buffer, ushort envelopeId, ReadOnlySpan<byte> payload) {
            var writer = new NetWriter(buffer);
            writer.WriteUShort(envelopeId);
            writer.WriteRaw(payload);
            return writer.Written;
        }

        /// <summary>
        /// Splits a received payload and hands the body to whoever claimed its id: a raw handler if one
        /// registered for that envelope, otherwise the typed router.
        /// </summary>
        /// <remarks>
        /// Raw handlers are checked first so an envelope id can never be shadowed by a typed
        /// registration — the chat pipe must reach the chat codec whatever else is bound.
        /// </remarks>
        public static void Deliver(
            ArraySegment<byte> payload,
            PeerHandle sender,
            MessageRouter router,
            IReadOnlyDictionary<ushort, RawMessageHandler> rawHandlers) {
            var reader = new NetReader(payload);
            ushort messageId = reader.ReadUShort();

            if (rawHandlers.TryGetValue(messageId, out RawMessageHandler rawHandler)) {
                rawHandler(messageId, BodyOf(payload), sender);
                return;
            }

            router.Dispatch(messageId, ref reader, sender);
        }

        private static ArraySegment<byte> BodyOf(ArraySegment<byte> payload) {
            return new ArraySegment<byte>(payload.Array, payload.Offset + HeaderSize, payload.Count - HeaderSize);
        }
    }
}
