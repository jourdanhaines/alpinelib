using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Maps ushort message ids onto typed handlers. Dispatch decodes straight into a stack-allocated
    /// struct and invokes the handler through a closed generic binding, so there is no reflection, no
    /// boxing and no per-message allocation on the hot receive path.
    ///
    /// Ids are the wire contract (see the id map in the networking plan): a registered id must never be
    /// repurposed, only retired. Registering the same id twice is a programming error and throws, since
    /// silently replacing a handler hides an id collision until it corrupts a live session.
    /// </summary>
    public sealed class MessageRouter {
        private readonly Dictionary<ushort, MessageBinding> bindings = new Dictionary<ushort, MessageBinding>();

        /// <summary>Number of ids currently bound.</summary>
        public int RegisteredCount => bindings.Count;

        /// <summary>
        /// Raised when <see cref="Dispatch"/> receives an id nobody registered. Unknown ids are normal
        /// during a rolling deploy or from a stale client, so they are reported rather than thrown.
        /// </summary>
        public event Action<ushort, PeerHandle> UnknownMessageReceived;

        public void Register<TMessage>(ushort messageId, MessageHandler<TMessage> handler)
            where TMessage : struct, INetMessage {
            if (handler == null) {
                throw new ArgumentNullException(nameof(handler));
            }

            if (bindings.ContainsKey(messageId)) {
                throw new InvalidOperationException($"Message id {messageId} is already registered.");
            }

            bindings.Add(messageId, new TypedMessageBinding<TMessage>(handler));
        }

        public void Unregister(ushort messageId) {
            bindings.Remove(messageId);
        }

        public bool IsRegistered(ushort messageId) {
            return bindings.ContainsKey(messageId);
        }

        public void Clear() {
            bindings.Clear();
        }

        /// <summary>
        /// Decodes and delivers one message. Returns false for an unregistered id, leaving the reader
        /// untouched so the caller can decide whether to skip the payload or drop the datagram.
        /// </summary>
        public bool Dispatch(ushort messageId, ref NetReader reader, PeerHandle sender) {
            if (!bindings.TryGetValue(messageId, out MessageBinding binding)) {
                UnknownMessageReceived?.Invoke(messageId, sender);
                return false;
            }

            binding.Invoke(ref reader, sender);
            return true;
        }

        /// <summary>
        /// Type-erased slot in the dictionary. The generic subclass below is what keeps the message type
        /// alive at the call site, which is the whole trick that avoids boxing.
        /// </summary>
        private abstract class MessageBinding {
            public abstract void Invoke(ref NetReader reader, PeerHandle sender);
        }

        private sealed class TypedMessageBinding<TMessage> : MessageBinding where TMessage : struct, INetMessage {
            private readonly MessageHandler<TMessage> handler;

            public TypedMessageBinding(MessageHandler<TMessage> handler) {
                this.handler = handler;
            }

            public override void Invoke(ref NetReader reader, PeerHandle sender) {
                TMessage message = default;
                message.Deserialize(ref reader);
                handler(in message, sender);
            }
        }
    }
}
