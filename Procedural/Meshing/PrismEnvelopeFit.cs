using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>The prisms of a <see cref="PrismEnvelopeFitter"/> run, bottom band first, and how they sit around the look.</summary>
    public sealed class PrismEnvelopeFit {
        internal PrismEnvelopeFit(List<EnvelopePrism> prisms, List<double> bandEdges, EnvelopeMetrics metrics, double standoffP90, double standoffMax, double ledgeAirMax, double worstOutside, bool meetsTargets, string report) {
            Prisms = prisms.AsReadOnly();
            BandEdges = bandEdges.AsReadOnly();
            Metrics = metrics;
            StandoffP90 = standoffP90;
            StandoffMax = standoffMax;
            LedgeAirMax = ledgeAirMax;
            WorstOutside = worstOutside;
            MeetsTargets = meetsTargets;
            Report = report;
        }

        /// <summary>The prisms, in band order and within a band in cut order.</summary>
        public IReadOnlyList<EnvelopePrism> Prisms { get; }

        /// <summary>Band edges in the look's space, ascending: band n spans edges n to n + 1.</summary>
        public IReadOnlyList<double> BandEdges { get; }

        /// <summary>Metrics of the prisms around the look at <see cref="PrismEnvelopeSettings.SampleSpacing"/>.</summary>
        public EnvelopeMetrics Metrics { get; }

        /// <summary>90th percentile of exposed side standoff in the walking band, look metres.</summary>
        public double StandoffP90 { get; }

        /// <summary>Largest exposed side standoff in the walking band, look metres.</summary>
        public double StandoffMax { get; }

        /// <summary>Most air under an exposed reachable cap, look metres.</summary>
        public double LedgeAirMax { get; }

        /// <summary>Furthest any containment sample of the look lay outside every prism, look metres.</summary>
        public double WorstOutside { get; }

        /// <summary>
        /// True when the count is within the budget, walking-band standoff p90 and max within their targets and
        /// reachable ledge air within <see cref="PrismEnvelopeSettings.LedgeAirMax"/>. Consumers gate on it.
        /// </summary>
        public bool MeetsTargets { get; }

        /// <summary>One line per band: its heights, prisms and gate numbers.</summary>
        public string Report { get; }
    }
}
