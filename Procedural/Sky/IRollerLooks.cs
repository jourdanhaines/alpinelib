namespace AlpineLib.Procedural.Sky {
    /// <summary>Picks a spawned wind-rolled prop's look from its seed alone, so every client draws the same one. Worker-safe.</summary>
    public interface IRollerLooks {
        RollerLook LookOf(RollerSpawn spawn);
    }
}
