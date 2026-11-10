namespace AllAboard.GoldenRecorder {
    /// <summary>One recorded apron lay as case text, with how much of the builder it exercises.</summary>
    public sealed class TalusCandidate {
        public TalusCandidate(string text, int score) {
            Text = text;
            Score = score;
        }

        public string Text { get; }

        public int Score { get; }
    }
}
