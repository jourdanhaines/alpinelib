namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Contract for everything that travels on the wire. Implementations are always <c>struct</c>s: the
    /// router creates them on the stack, fills them in place and hands them to a typed handler, so a
    /// message never allocates and never boxes on the receive path.
    ///
    /// Serialize and Deserialize must stay exact mirrors of one another, field for field and order for
    /// order. There is no schema, no field tags and no reflection anywhere in this protocol — the two
    /// method bodies ARE the schema.
    /// </summary>
    public interface INetMessage {
        void Serialize(ref NetWriter writer);

        void Deserialize(ref NetReader reader);
    }
}
