using System;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The session params blob survives the create request and the join acceptance byte for byte, and the
    /// cap holds on both the writing and the reading side.
    /// </summary>
    public sealed class SessionParamsCodecTests {
        private static readonly byte[] SampleParams = { 1, 0xEF, 0xBE, 0xAD, 0xDE, 0, 0, 0, 0, 7, 0 };

        [Fact]
        public void CreateSessionRequestRoundTripsItsParams() {
            CreateSessionRequest decoded = RoundTrip(new CreateSessionRequest("profile", SampleParams));

            Assert.Equal("profile", decoded.ProfileId);
            Assert.Equal(SampleParams, decoded.Params);
        }

        [Fact]
        public void CreateSessionRequestWithoutParamsReadsBackEmpty() {
            Assert.Empty(RoundTrip(new CreateSessionRequest("profile")).Params);
            Assert.Empty(RoundTrip(new CreateSessionRequest { ProfileId = "p", Params = null }).Params);
        }

        [Fact]
        public void CreateSessionRequestAcceptsParamsExactlyAtTheCap() {
            byte[] atCap = BuildParams(CreateSessionRequest.MaxParamsLength);

            Assert.Equal(atCap, RoundTrip(new CreateSessionRequest(string.Empty, atCap)).Params);
        }

        [Fact]
        public void CreateSessionRequestRefusesToWriteOversizedParams() {
            CreateSessionRequest oversized = new CreateSessionRequest(string.Empty, BuildParams(SessionParamsCodec.MaxLength + 1));

            Assert.Throws<NetProtocolException>(() => RoundTrip(oversized));
        }

        [Fact]
        public void CreateSessionRequestRefusesToReadOversizedParams() {
            byte[] buffer = new byte[1024];
            NetWriter writer = new NetWriter(buffer);
            writer.WriteString("p");
            writer.WriteBytes(BuildParams(SessionParamsCodec.MaxLength + 1));

            Assert.Throws<NetProtocolException>(() => Decode<CreateSessionRequest>(buffer, writer.Written));
        }

        [Fact]
        public void JoinAcceptedRoundTripsParamsInTheLobby() {
            JoinAccepted decoded = RoundTrip(new JoinAccepted { Phase = SessionPhase.Lobby, SessionParams = SampleParams });

            Assert.Equal(SampleParams, decoded.SessionParams);
            Assert.Null(decoded.MatchContext);
        }

        [Fact]
        public void JoinAcceptedRoundTripsParamsBehindAMatchContext() {
            JoinAccepted original = new JoinAccepted {
                Phase = SessionPhase.MatchActive,
                IsRejoin = true,
                MatchContext = new MatchContextData { MatchId = "m", SceneName = "Scene", MatchSequence = 3 },
                SessionParams = SampleParams
            };

            JoinAccepted decoded = RoundTrip(original);

            Assert.Equal(SampleParams, decoded.SessionParams);
            Assert.Equal("Scene", decoded.MatchContext.SceneName);
            Assert.True(decoded.IsRejoin);
        }

        [Fact]
        public void JoinAcceptedWithoutParamsCostsNothingAndReadsBackEmpty() {
            int withNull = Encode(new JoinAccepted { SessionParams = null }, new byte[1024]);
            int withEmpty = Encode(new JoinAccepted { SessionParams = Array.Empty<byte>() }, new byte[1024]);

            Assert.Equal(withNull, withEmpty);
            Assert.Empty(RoundTrip(new JoinAccepted()).SessionParams);
        }

        [Fact]
        public void TheProtocolVersionMovedForTheNewFields() {
            Assert.Equal(7, NetProtocol.Version);
        }

        private static byte[] BuildParams(int length) {
            byte[] bytes = new byte[length];

            for (int index = 0; index < length; index++) {
                bytes[index] = (byte)(index * 31 + 7);
            }

            return bytes;
        }

        private static int Encode<TMessage>(TMessage message, byte[] buffer) where TMessage : struct, INetMessage {
            NetWriter writer = new NetWriter(buffer);
            message.Serialize(ref writer);
            return writer.Written;
        }

        private static TMessage Decode<TMessage>(byte[] buffer, int written) where TMessage : struct, INetMessage {
            NetReader reader = new NetReader(buffer, 0, written);
            TMessage decoded = default;
            decoded.Deserialize(ref reader);

            Assert.True(reader.IsExhausted, "The reader did not consume every byte the writer produced.");
            return decoded;
        }

        private static TMessage RoundTrip<TMessage>(TMessage message) where TMessage : struct, INetMessage {
            byte[] buffer = new byte[2048];
            int written = Encode(message, buffer);
            return Decode<TMessage>(buffer, written);
        }
    }
}
