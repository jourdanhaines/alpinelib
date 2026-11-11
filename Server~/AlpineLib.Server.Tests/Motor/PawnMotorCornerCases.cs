using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Acute concave corners lifted from a consuming game's generated mesas: only the shapes near the walk, the state a
    /// few ticks before the pawn reaches the corner, and the input it keeps pushing. Coordinates are the game's own.
    /// </summary>
    public static class PawnMotorCornerCases {
        /// <summary>Every case, in a fixed order.</summary>
        public static CornerCase[] All() {
            return new[] { CleftTail(), VNotch(), TiltedBoxA(), TiltedBoxB(), BoxOnTalus(), RockSlot() };
        }

        /// <summary>MesaMedium v0 cleft tail, walk 6/8; the old motor is left 0.194 m inside.</summary>
        public static CornerCase CleftTail() {
            var start = new PawnState(new Vector3(1.5843995f, 0f, -6.4099655f), 0f, new Vector3(1.2857741f, 0f, 3.5319226f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakePlane(0f),
                CollisionShape.MakePrism(new Vector3(6.44045f, 3.7375f, -2.2564f), 4.3125f, new[] { new Vector2(-4.5174f, -8.897f), new Vector2(3.3186f, -10.7761f), new Vector2(4.5174f, -2.3911f), new Vector2(3.8704f, 9.6853f), new Vector2(-2.1133f, 10.7761f) }),
                CollisionShape.MakePrism(new Vector3(-4.94965f, 3.7375f, 3.06555f), 4.3125f, new[] { new Vector2(-9.2768f, 1.9858f), new Vector2(-6.99f, -3.0346f), new Vector2(7.806f, -6.5828f), new Vector2(9.2768f, 5.4542f), new Vector2(3.0858f, 6.5828f) }),
                CollisionShape.MakePrism(new Vector3(-4.87715f, 3.7375f, -5.2771f), 4.3125f, new[] { new Vector2(-7.0624f, 5.308f), new Vector2(-3.0434f, -3.5156f), new Vector2(4.4308f, -5.308f), new Vector2(7.0624f, 1.9208f) }),
                CollisionShape.MakePrism(new Vector3(-1.89715f, 10.895847f, -2.63575f), 2.8458474f, new[] { new Vector2(-11.3092f, 7.2578f), new Vector2(-5.3962f, -5.4017f), new Vector2(10.9964f, -9.4217f), new Vector2(11.3092f, -7.521f), new Vector2(-5.68f, 9.4217f) }),
                CollisionShape.MakePrism(new Vector3(2.5572f, 10.895847f, -0.742f), 2.8458474f, new[] { new Vector2(-7.7524f, 5.1526f), new Vector2(6.8549f, -9.4147f), new Vector2(7.7524f, -3.96f), new Vector2(6.7614f, 7.2958f), new Vector2(-2.9729f, 9.4147f) }),
                CollisionShape.MakePrism(new Vector3(-1.45985f, 16.5876f, -2.6708f), 2.8459055f, new[] { new Vector2(-10.4718f, 6.7763f), new Vector2(-5.5422f, -4.945f), new Vector2(10.0218f, -8.5916f), new Vector2(10.4718f, -6.0278f), new Vector2(-5.5582f, 8.5916f) }),
                CollisionShape.MakePrism(new Vector3(2.30075f, 16.5876f, -0.4921f), 2.8459055f, new[] { new Vector2(-7.4104f, 4.6722f), new Vector2(6.7112f, -8.2065f), new Vector2(7.4104f, -4.2217f), new Vector2(6.3499f, 6.4583f), new Vector2(-3.0076f, 8.2065f) }),
                CollisionShape.MakePrism(new Vector3(6.9474f, -0.20124999f, -2.3911f), 0.37375f, new[] { new Vector2(-5.1647f, -9.9111f), new Vector2(3.7713f, -12.0542f), new Vector2(5.1647f, -2.3075f), new Vector2(4.4631f, 10.7884f), new Vector2(-2.4806f, 12.0542f) }),
                CollisionShape.MakePrism(new Vector3(-5.6591f, -0.20124999f, 3.65955f), 0.37375f, new[] { new Vector2(-10.1259f, 2.0392f), new Vector2(-7.6992f, -3.2884f), new Vector2(8.5154f, -7.1768f), new Vector2(10.1259f, 6.0035f), new Vector2(3.6905f, 7.1768f) }),
                CollisionShape.MakePrism(new Vector3(-5.5865f, -0.20124999f, -5.6508f), 0.37375f, new[] { new Vector2(-7.7718f, 6.0219f), new Vector2(-3.1481f, -4.1293f), new Vector2(4.7441f, -6.0219f), new Vector2(7.7718f, 2.2945f) }),
                CollisionShape.MakePrism(new Vector3(-0.66275f, 27.107973f, -2.2549f), 7.6744666f, new[] { new Vector2(-10.6447f, 6.0923f), new Vector2(-4.81f, -5.8611f), new Vector2(9.5215f, -9.4552f), new Vector2(10.6447f, -2.3567f), new Vector2(9.9908f, 7.4779f), new Vector2(-0.9476f, 9.4552f) }),
                CollisionShape.MakeBox(new Vector3(1.3162187f, 6.2363186f, -8.456316f), new Vector3(0.9424988f, 0.0969171f, -0.3198485f), new Vector3(0.0359258f, 0.9221047f, 0.3852692f), new Vector3(0.3322729f, -0.3746066f, 0.8656007f), new Vector3(1.1513771f, 1.1797f, 1.6651506f)),
            };
            return new CornerCase("CleftTail", shapes, in start, 232u, 105, new Vector2(1.8369701E-16f, 1f));
        }

        /// <summary>seed 7 cell (2,-4) MesaMedium v5 heading 43; the old motor is left 0.124 m inside.</summary>
        public static CornerCase VNotch() {
            var start = new PawnState(new Vector3(-4.3183737f, 0f, -6.9735646f), 0f, new Vector3(-1.5699818f, 0f, -3.3611178f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakeBox(new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(64f, 0.5f, 64f)),
                CollisionShape.MakePrism(new Vector3(-18.77945f, 3.6075f, -12.150701f), 4.1625f, new[] { new Vector2(5.0586f, -13.8182f), new Vector2(15.6722f, 8.9042f), new Vector2(9.2061f, 13.8182f), new Vector2(-15.6722f, 11.0699f) }),
                CollisionShape.MakePrism(new Vector3(-4.9972f, 3.6075f, -19.554f), 4.1625f, new[] { new Vector2(-5.3659f, -10.44615f), new Vector2(8.7237f, 3.17355f), new Vector2(-0.8478f, 10.44615f), new Vector2(-8.7237f, -6.41485f) }),
                CollisionShape.MakePrism(new Vector3(1.5054499f, 3.6075f, -10.84185f), 4.1625f, new[] { new Vector2(2.2211f, -5.5386f), new Vector2(6.8202f, -1.0928f), new Vector2(-1.9062f, 5.5386f), new Vector2(-6.8202f, 1.3311f) }),
                CollisionShape.MakePrism(new Vector3(-13.064651f, 10.429283f, -14.342401f), 2.6592827f, new[] { new Vector2(2.6907f, -16.2605f), new Vector2(21.9265f, 2.4241f), new Vector2(3.5648f, 16.2605f), new Vector2(-21.9265f, 13.479f) }),
                CollisionShape.MakePrism(new Vector3(-12.57265f, 15.747791f, -13.98085f), 2.6592264f, new[] { new Vector2(2.3575f, -13.6762f), new Vector2(18.3298f, 1.8847f), new Vector2(2.4819f, 13.6762f), new Vector2(-18.3298f, 11.4679f) }),
                CollisionShape.MakePrism(new Vector3(-19.62645f, -0.19425f, -12.1304f), 0.36075002f, new[] { new Vector2(5.3865f, -14.9498f), new Vector2(17f, 9.9128f), new Vector2(10.3716f, 14.9498f), new Vector2(-17f, 11.9261f) }),
                CollisionShape.MakePrism(new Vector3(-4.80965f, -0.19425f, -20.3685f), 0.36075002f, new[] { new Vector2(-5.6413f, -11.26055f), new Vector2(9.4304f, 3.3085501f), new Vector2(-1.0353f, 11.26055f), new Vector2(-9.4304f, -6.71175f) }),
                CollisionShape.MakePrism(new Vector3(2.3562999f, -0.19425f, -10.81225f), 0.36075002f, new[] { new Vector2(2.26435f, -6.2476f), new Vector2(7.67095f, -1.0214f), new Vector2(-1.8944501f, 6.2476f), new Vector2(-7.67095f, 1.3015f) }),
                CollisionShape.MakePrism(new Vector3(-12.738f, 26.17985f, -13.84095f), 7.772831f, new[] { new Vector2(2.541f, -13.24f), new Vector2(17.7473f, 1.7394f), new Vector2(2.561f, 13.24f), new Vector2(-17.7473f, 11.1618f) }),
                CollisionShape.MakeBox(new Vector3(-3.5471208f, 5.943784f, -6.6313934f), new Vector3(-0.8223951f, -0.0837794f, 0.5627142f), new Vector3(-0.2226465f, 0.9576038f, -0.1828209f), new Vector3(-0.5235406f, -0.2756374f, -0.8061819f), new Vector3(1.4835106f, 1.3861715f, 1.3696054f)),
            };
            return new CornerCase("VNotch", shapes, in start, 198u, 105, new Vector2(-0.7313537f, -0.6819984f));
        }

        /// <summary>seed 7 cell (0,-2) MesaSmall v6 heading 198; the old motor is left 0.212 m inside.</summary>
        public static CornerCase TiltedBoxA() {
            var start = new PawnState(new Vector3(43.44105f, 0.14625001f, -9.102661f), 0f, new Vector3(3.8985193f, 0f, 0.7378779f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakeBox(new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(64f, 0.5f, 64f)),
                CollisionShape.MakePrism(new Vector3(40.71545f, 3.16875f, -6.83925f), 3.65625f, new[] { new Vector2(5.9912f, 2.7114f), new Vector2(-5.9912f, 0.4432f), new Vector2(-2.2224f, -2.7114f), new Vector2(5.7482f, -1.2028f) }),
                CollisionShape.MakePrism(new Vector3(51.107403f, 3.16875f, -5.258f), 3.65625f, new[] { new Vector2(3.8261f, -1.1808f), new Vector2(1.8613f, 2.3156f), new Vector2(-3.8261f, 1.239f), new Vector2(-2.1694f, -2.3156f) }),
                CollisionShape.MakePrism(new Vector3(43.26295f, 8.259128f, -1.2539f), 1.4341276f, new[] { new Vector2(10.6823f, -4.708f), new Vector2(5.9194f, 3.8139f), new Vector2(-4.9666f, 7.4853f), new Vector2(-10.6823f, -2.4284f), new Vector2(-4.55f, -7.4853f) }),
                CollisionShape.MakePrism(new Vector3(43.34785f, 11.070394f, -1.1844f), 1.3771386f, new[] { new Vector2(11.4758f, -5.1877f), new Vector2(6.2296f, 4.2145f), new Vector2(-5.3698f, 8.2225f), new Vector2(-11.4758f, -2.6262f), new Vector2(-4.827f, -8.2225f) }),
                CollisionShape.MakePrism(new Vector3(40.343803f, -0.170625f, -7.1523f), 0.316875f, new[] { new Vector2(6.3628f, 3.02455f), new Vector2(-6.3628f, 0.61564994f), new Vector2(-2.014f, -3.02455f), new Vector2(6.0824f, -1.49205f) }),
                CollisionShape.MakePrism(new Vector3(51.561802f, -0.170625f, -5.4741f), 0.316875f, new[] { new Vector2(4.2805f, -1.388f), new Vector2(2.0135f, 2.6466f), new Vector2(-4.2805f, 1.4551f), new Vector2(-2.3688f, -2.6466f) }),
                CollisionShape.MakePrism(new Vector3(44.07035f, 12.624203f, -6.2809f), 0.66417f, new[] { new Vector2(7.7516f, 3.03215f), new Vector2(-7.784f, -1.1306499f), new Vector2(-5.5248f, -3.03215f), new Vector2(4.6786f, -1.1327499f), new Vector2(7.1668f, 0.79015005f), new Vector2(7.784f, 2.10495f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(41.3143f, 1.1665858f, -11.3326f), -0.29250002f, 0.78777516f, -0.5215508f, 0.2056634f, new[] { new Vector2(2.82125f, 2.8499f), new Vector2(-2.82125f, 1.7819f), new Vector2(0.57364994f, -2.8499f) }),
                CollisionShape.MakeBox(new Vector3(46.285267f, 0.02320932f, -9.521557f), new Vector3(0.8846562f, -0.0170051f, 0.4659337f), new Vector3(-0.0901825f, 0.9742217f, 0.2067832f), new Vector3(-0.4574391f, -0.2249511f, 0.8603177f), new Vector3(0.87066215f, 0.27307275f, 0.71327364f)),
                CollisionShape.MakeBox(new Vector3(46.273712f, 0.52481675f, -9.458628f), new Vector3(0.8846562f, -0.0170051f, 0.4659337f), new Vector3(-0.0901825f, 0.9742217f, 0.2067832f), new Vector3(-0.4574391f, -0.2249511f, 0.8603177f), new Vector3(0.83501923f, 0.22976238f, 0.6313673f)),
                CollisionShape.MakeBox(new Vector3(46.207623f, 0.8158525f, -9.458618f), new Vector3(0.8846562f, -0.0170051f, 0.4659337f), new Vector3(-0.0901825f, 0.9742217f, 0.2067832f), new Vector3(-0.4574391f, -0.2249511f, 0.8603177f), new Vector3(0.70063287f, 0.05973307f, 0.5174964f)),
            };
            return new CornerCase("TiltedBoxA", shapes, in start, 199u, 103, new Vector2(0.95105654f, 0.309017f));
        }

        /// <summary>seed 42 cell (-2,0) MesaSmall v6 heading 122; the old motor is left 0.218 m inside.</summary>
        public static CornerCase TiltedBoxB() {
            var start = new PawnState(new Vector3(-29.379814f, 0.1551f, 31.877855f), 0f, new Vector3(1.7907915f, 0f, -3.5577374f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakeBox(new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(64f, 0.5f, 64f)),
                CollisionShape.MakePrism(new Vector3(-21.88325f, 3.3605f, 34.8284f), 3.8775f, new[] { new Vector2(1.4644f, -10.7204f), new Vector2(6.6216f, -5.5029f), new Vector2(7.3896f, 7.5676f), new Vector2(-5.6804f, 10.7204f), new Vector2(-7.3896f, 6.869f) }),
                CollisionShape.MakePrism(new Vector3(-27.4225f, 3.3605f, 35.473103f), 3.8775f, new[] { new Vector2(3.9646f, -5.3277f), new Vector2(-1.8503f, 6.2243f), new Vector2(-3.9646f, 1.4602f), new Vector2(-0.0966f, -6.2243f) }),
                CollisionShape.MakePrism(new Vector3(-22.9257f, 8.758911f, 33.4529f), 1.5209107f, new[] { new Vector2(-0.2859f, -11.2231f), new Vector2(7.0093f, -3.8765f), new Vector2(7.5698f, 8.2943f), new Vector2(-4.2073f, 11.2231f), new Vector2(-7.5698f, 3.4934f) }),
                CollisionShape.MakePrism(new Vector3(-22.9294f, 11.740294f, 33.28765f), 1.4604732f, new[] { new Vector2(-0.4436f, -12.0732f), new Vector2(7.6078f, -3.9766f), new Vector2(8.3029f, 9.0199f), new Vector2(-4.5414f, 12.0732f), new Vector2(-8.3029f, 3.6592f) }),
                CollisionShape.MakePrism(new Vector3(-21.73865f, -0.18095f, 34.8982f), 0.33605f, new[] { new Vector2(1.6141f, -11.3749f), new Vector2(7.0826f, -5.8425f), new Vector2(7.8948f, 7.9793f), new Vector2(-6.1823f, 11.3749f), new Vector2(-7.8948f, 7.5158f) }),
                CollisionShape.MakePrism(new Vector3(-27.765501f, -0.18095f, 35.76245f), 0.33605f, new[] { new Vector2(4.30765f, -5.6171f), new Vector2(-1.8678501f, 6.6516f), new Vector2(-4.30765f, 1.1541f), new Vector2(-0.37875006f, -6.6516f) }),
                CollisionShape.MakePrism(new Vector3(-23.46305f, -0.18095f, 24.812002f), 0.33605f, new[] { new Vector2(-0.1117f, -4.7792f), new Vector2(3.3386f, -1.2887f), new Vector2(0.2842f, 4.7792f), new Vector2(-3.3386f, 1.6314f) }),
                CollisionShape.MakePrism(new Vector3(-26.09785f, 13.388129f, 32.345802f), 0.70436084f, new[] { new Vector2(5.0316f, -7.1581f), new Vector2(-3.7534f, 7.4623f), new Vector2(-5.0316f, 4.6033f), new Vector2(-0.1127f, -5.243f), new Vector2(2.575f, -7.2174f), new Vector2(4.0957f, -7.4623f) }),
                CollisionShape.MakePrism(new Vector3(-21.88085f, 13.526736f, 31.357351f), 0.84296846f, new[] { new Vector2(3.0503f, -5.4429f), new Vector2(3.903f, -4.5856f), new Vector2(5.6962f, -1.9662f), new Vector2(5.9211f, -1.4598f), new Vector2(6.5998f, 1.3654f), new Vector2(-6.5998f, 6.1697f), new Vector2(0.8145f, -6.1697f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(-31.8827f, 1.2372047f, 34.2134f), -0.31020004f, 1.24005f, 0.0539375f, 0.5580353f, new[] { new Vector2(3.2338f, -2.71995f), new Vector2(0.4956f, 2.71995f), new Vector2(-3.2338f, -2.09485f) }),
                CollisionShape.MakeBox(new Vector3(-29.079573f, 0.02461378f, 28.998156f), new Vector3(0.6917285f, -0.0170051f, -0.7219574f), new Vector3(0.1739151f, 0.9742217f, 0.1436862f), new Vector3(0.7009032f, -0.2249511f, 0.6768544f), new Vector3(0.92334837f, 0.28959715f, 0.7564358f)),
                CollisionShape.MakeBox(new Vector3(-29.018799f, 0.5565749f, 29.028332f), new Vector3(0.6917285f, -0.0170051f, -0.7219574f), new Vector3(0.1739151f, 0.9742217f, 0.1436862f), new Vector3(0.7009032f, -0.2249511f, 0.6768544f), new Vector3(0.8855486f, 0.24366595f, 0.6695732f)),
                CollisionShape.MakeBox(new Vector3(-29.038107f, 0.86522204f, 29.095707f), new Vector3(0.6917285f, -0.0170051f, -0.7219574f), new Vector3(0.1739151f, 0.9742217f, 0.1436862f), new Vector3(0.7009032f, -0.2249511f, 0.6768544f), new Vector3(0.74303013f, 0.06334769f, 0.54881155f)),
            };
            return new CornerCase("TiltedBoxB", shapes, in start, 211u, 105, new Vector2(0.52991927f, -0.8480481f));
        }

        /// <summary>seed 42 cell (-1,3) MesaSmall v11 heading 94; the old motor is left 0.136 m inside.</summary>
        public static CornerCase BoxOnTalus() {
            var start = new PawnState(new Vector3(38.49344f, 1.8401885f, -39.30302f), 0f, new Vector3(-0.9436364f, 0f, -0.31382298f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakeBox(new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(64f, 0.5f, 64f)),
                CollisionShape.MakePrism(new Vector3(44.76975f, 3.1005f, -43.55755f), 3.5774999f, new[] { new Vector2(7.9022f, 3.7974f), new Vector2(2.4086f, 6.637f), new Vector2(-6.214f, 3.7694f), new Vector2(-7.9022f, -6.637f), new Vector2(2.9685f, -6.4822f) }),
                CollisionShape.MakePrism(new Vector3(44.85305f, 7.724204f, -43.45295f), 1.0462042f, new[] { new Vector2(8.1684f, 3.8132f), new Vector2(2.3872f, 6.897f), new Vector2(-6.5096f, 3.8456f), new Vector2(-8.1684f, -6.897f), new Vector2(3.0499f, -6.852f) }),
                CollisionShape.MakePrism(new Vector3(44.872547f, 9.816612f, -43.4724f), 1.0462037f, new[] { new Vector2(7.9434f, 3.7694f), new Vector2(2.3358f, 6.6663f), new Vector2(-6.4556f, 3.7765f), new Vector2(-7.9434f, -6.6663f), new Vector2(2.8504f, -6.5186f) }),
                CollisionShape.MakePrism(new Vector3(44.83435f, 12.260713f, -43.5291f), 1.3978963f, new[] { new Vector2(8.3219f, 3.9336f), new Vector2(2.368f, 6.9705f), new Vector2(-6.447f, 3.9202f), new Vector2(-8.3219f, -6.9705f), new Vector2(3.1456f, -6.9f) }),
                CollisionShape.MakePrism(new Vector3(44.8108f, -0.16695f, -43.53895f), 0.31005f, new[] { new Vector2(8.6177f, 4.0321f), new Vector2(2.416f, 7.2376f), new Vector2(-6.7646f, 4.1846f), new Vector2(-8.6177f, -7.2376f), new Vector2(3.2901f, -7.068f) }),
                CollisionShape.MakePrism(new Vector3(44.81265f, 14.023132f, -43.87165f), 0.84152347f, new[] { new Vector2(6.2863f, 4.1214f), new Vector2(4.7652f, 5.8804f), new Vector2(2.465f, 6.5182f), new Vector2(-2.3262f, 4.6718f), new Vector2(-4.6256f, 3.0989f), new Vector2(-4.9854f, 2.6752f), new Vector2(-6.165f, 0.1516f), new Vector2(-6.6158f, -2.467f), new Vector2(-6.075f, -4.4488f), new Vector2(-4.4152f, -6.5182f), new Vector2(0.738f, -6.4866f), new Vector2(2.5816f, -5.9446f), new Vector2(2.862f, -5.7668f), new Vector2(4.1368f, -4.3292f), new Vector2(6.4601f, 0.5332f), new Vector2(6.6158f, 3.1485f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(42.20535f, 0.9623662f, -36.3646f), -0.28620002f, 1.0577698f, 0.1527033f, -0.4591719f, new[] { new Vector2(3.5594f, 3.6951f), new Vector2(-5.0634f, 0.8275f), new Vector2(-3.5594f, -3.6951f), new Vector2(5.0634f, -0.8275f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(36.3446f, 0.8931446f, -37.6627f), -0.28620002f, 0.34063244f, 0.4307367f, -0.3667074f, new[] { new Vector2(0.7973f, 2.12555f), new Vector2(-2.2111f, -1.40815f), new Vector2(2.2111f, -2.12555f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(35.6417f, 0.96237147f, -44.6556f), -0.28620002f, 1.0577494f, 0.4776564f, -0.0774904f, new[] { new Vector2(3.19645f, 4.8216f), new Vector2(-1.5082499f, 5.5848f), new Vector2(-3.19645f, -4.8215f), new Vector2(1.5082501f, -5.5848f) }),
                CollisionShape.MakeBox(new Vector3(36.26801f, 1.6289881f, -41.37025f), new Vector3(0.3918846f, -0.127141f, 0.9111869f), new Vector3(-0.3308878f, 0.904655f, 0.2685382f), new Vector3(-0.8584519f, -0.4067366f, 0.3124509f), new Vector3(0.9019436f, 0.92413074f, 1.3044138f)),
            };
            return new CornerCase("BoxOnTalus", shapes, in start, 268u, 28, new Vector2(0.06975647f, -0.9975641f));
        }

        /// <summary>survey MesaSmall v5 x1.15 yaw0 heading 201 rock vs wall; the old motor is left 0.331 m inside.</summary>
        public static CornerCase RockSlot() {
            var start = new PawnState(new Vector3(-0.22810765f, 0.17250001f, -9.124546f), 0f, new Vector3(3.802715f, 0f, 1.2203526f), PawnState.PackFlags(WireLocomotion.Walk, false, true));
            var shapes = new[] {
                CollisionShape.MakePrism(new Vector3(-4.5888f, 3.7375f, -0.9767f), 4.3125f, new[] { new Vector2(-7.1313f, -0.7975f), new Vector2(1.04f, -8.7094f), new Vector2(7.1313f, -6.7546f), new Vector2(5.4376f, 8.7094f) }),
                CollisionShape.MakePrism(new Vector3(6.3945f, 3.7375f, 0.51495f), 4.3125f, new[] { new Vector2(-5.5457f, 7.2178f), new Vector2(-4.5699f, -1.691f), new Vector2(-1.3393f, -7.44f), new Vector2(5.5457f, -5.2304f), new Vector2(2.946f, 7.1296f), new Vector2(-5.2519f, 7.44f) }),
                CollisionShape.MakePrism(new Vector3(0.07815f, 9.847105f, -0.9365f), 1.7971047f, new[] { new Vector2(-12.7099f, -0.7875f), new Vector2(-3.829f, -9.5019f), new Vector2(12.7099f, -4.248f), new Vector2(9.8492f, 9.2536f), new Vector2(0.8797f, 9.5019f) }),
                CollisionShape.MakePrism(new Vector3(-0.1071f, 12.923297f, -0.9458f), 1.2790879f, new[] { new Vector2(-13.3787f, -0.7378f), new Vector2(-3.7246f, -9.9631f), new Vector2(13.3787f, -4.5351f), new Vector2(10.4226f, 9.7295f), new Vector2(0.9061f, 9.9631f) }),
                CollisionShape.MakePrism(new Vector3(-5.0798f, -0.20124999f, -0.96895f), 0.37375f, new[] { new Vector2(-7.6989f, -0.7408f), new Vector2(1.3482f, -9.5006f), new Vector2(7.6989f, -7.4626f), new Vector2(5.8411f, 9.5006f) }),
                CollisionShape.MakePrism(new Vector3(6.7524f, -0.20124999f, 0.5574f), 0.37375f, new[] { new Vector2(-5.9911f, 7.9742f), new Vector2(-4.9278f, -1.7335f), new Vector2(-1.3522f, -8.0963f), new Vector2(5.9911f, -5.7398f), new Vector2(3.1524f, 7.7564f), new Vector2(-5.8296f, 8.0963f) }),
                CollisionShape.MakePrism(new Vector3(0.9499f, 14.97921f, 0.63605f), 1.3518249f, new[] { new Vector2(-11.2063f, -2.5404f), new Vector2(5.1734f, -8.2769f), new Vector2(7.5267f, -7.53f), new Vector2(10.0519f, -5.512f), new Vector2(10.6829f, -4.434f), new Vector2(11.2063f, -1.2441f), new Vector2(9.8594f, 5.2542f), new Vector2(8.7673f, 7.0772f), new Vector2(8.4193f, 7.3674f), new Vector2(6.43f, 8.1162f), new Vector2(-0.1175f, 8.2769f), new Vector2(-9.5501f, 1.2108f), new Vector2(-11.1607f, -1.7808f) }),
                CollisionShape.MakeSlopedPrism(new Vector3(-6.59415f, 1.0350562f, 4.8162f), -0.345f, 1.1500494f, 0.3041602f, -0.4021267f, new[] { new Vector2(-7.945f, -2.8635f), new Vector2(-4.9178f, -6.8656f), new Vector2(7.945f, 2.8635f), new Vector2(4.9178f, 6.8656f) }),
                CollisionShape.MakeBox(new Vector3(-1.6301101f, 0.48728812f, -12.221127f), new Vector3(-0.5892388f, -0.0870363f, -0.8032573f), new Vector3(-0.0090492f, 0.9948294f, -0.1011557f), new Vector3(0.8079082f, -0.052336f, -0.5869797f), new Vector3(1.1499916f, 0.9832792f, 1.6712812f)),
                CollisionShape.MakeBox(new Vector3(-1.649045f, 1.6664004f, -12.327845f), new Vector3(-0.5892388f, -0.0870363f, -0.8032573f), new Vector3(-0.0090492f, 0.9948294f, -0.1011557f), new Vector3(0.8079082f, -0.052336f, -0.5869797f), new Vector3(1.1554937f, 0.20082517f, 1.4765694f)),
                CollisionShape.MakeBox(new Vector3(6.2962046f, 0.4153727f, -8.184244f), new Vector3(-0.5310672f, -0.1562201f, -0.8328042f), new Vector3(-0.1285526f, 0.9863347f, -0.1030437f), new Vector3(0.8375212f, 0.052336f, -0.5438926f), new Vector3(0.6969347f, 0.7140788f, 1.0079248f)),
                CollisionShape.MakeBox(new Vector3(1.8579495f, 0.1838094f, -8.755713f), new Vector3(-0.7744609f, 0.2237188f, 0.5917435f), new Vector3(0.2418549f, 0.9690324f, -0.049825f), new Vector3(-0.5845654f, 0.1045285f, -0.8045851f), new Vector3(0.22462033f, 0.2794721f, 0.36991367f)),
                CollisionShape.MakeBox(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(400f, 1f, 400f)),
            };
            return new CornerCase("RockSlot", shapes, in start, 243u, 95, new Vector2(0.9335804f, 0.35836795f), 278u, new Vector2(-0.35836795f, 0.9335804f));
        }
    }
}
