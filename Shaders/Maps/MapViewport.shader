// Map viewport: draws baked cell tiles from three Texture2DArray levels through a toroidal
// cell -> slice indirection texture. UI-Default skeleton (stencil, RectMask2D clip rect, alpha clip,
// vertex colour); _MAP_CIRCLE clips to the inscribed circle for a round minimap.
Shader "AlpineLib/UI/MapViewport" {
    Properties {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [NoScaleOffset] _TilesL0 ("Tiles L0", 2DArray) = "" {}
        [NoScaleOffset] _TilesL1 ("Tiles L1", 2DArray) = "" {}
        [NoScaleOffset] _TilesL2 ("Tiles L2", 2DArray) = "" {}
        [NoScaleOffset] _Indirection ("Indirection", 2D) = "black" {}

        _RectSize ("Rect Size (canvas units)", Vector) = (1, 1, 0, 0)
        _MetresPerUnit ("Metres Per Unit", Float) = 1
        _CellSize ("Cell Size", Float) = 128
        _CentreFrac ("Centre Fraction In Cell", Vector) = (0, 0, 0, 0)
        _CentreIdx ("Centre Indirection Index", Vector) = (0, 0, 0, 0)
        _WindowSize ("Indirection Window Size", Float) = 32
        _WindowMin ("Window Min Cell Offset From Centre Cell", Vector) = (-16, -16, 0, 0)
        _Rot ("Rotation (cos, sin)", Vector) = (1, 0, 0, 0)
        _Level ("Selected Level", Float) = 0
        _LevelLod ("Per-Level Lod", Vector) = (0, 0, 0, 0)
        [Toggle(_MAP_CIRCLE)] _MapCircle ("Circle", Float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader {
        Tags {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass {
            Name "Default"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma require 2darray

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ _MAP_CIRCLE

            struct Attributes {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                half4 mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_DECLARE_TEX2DARRAY(_TilesL0);
            UNITY_DECLARE_TEX2DARRAY(_TilesL1);
            UNITY_DECLARE_TEX2DARRAY(_TilesL2);
            sampler2D _Indirection;
            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            float4 _RectSize;
            float _MetresPerUnit;
            float _CellSize;
            float4 _CentreFrac;
            float4 _CentreIdx;
            float _WindowSize;
            float4 _WindowMin;
            float4 _Rot;
            float _Level;
            float4 _LevelLod;

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 clipPosition = UnityObjectToClipPos(input.vertex);
                output.worldPosition = input.vertex;
                output.vertex = clipPosition;

                float2 pixelSize = clipPosition.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = half4(input.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float EntryOf(float4 entry, int level) {
                return level == 0 ? entry.r : (level == 1 ? entry.g : entry.b);
            }

            half4 SampleTile(int level, float slice, float2 uv) {
                if (level == 0) return UNITY_SAMPLE_TEX2DARRAY_LOD(_TilesL0, float3(uv, slice), _LevelLod.x);
                if (level == 1) return UNITY_SAMPLE_TEX2DARRAY_LOD(_TilesL1, float3(uv, slice), _LevelLod.y);
                return UNITY_SAMPLE_TEX2DARRAY_LOD(_TilesL2, float3(uv, slice), _LevelLod.z);
            }

            // Selected level first, then the next coarser ones, wrapping round to the finer ones.
            half4 SampleMap(float2 texcoord) {
                float2 local = (texcoord - 0.5) * _RectSize.xy;
                float2 metres = float2(local.x * _Rot.x + local.y * _Rot.y, -local.x * _Rot.y + local.y * _Rot.x) * _MetresPerUnit;
                float2 cells = metres / _CellSize + _CentreFrac.xy;
                float2 cellOffset = floor(cells);
                float2 uv = cells - cellOffset;
                // Cells outside the indirection window would alias onto another cell's texel.
                float2 inWindow = cellOffset - _WindowMin.xy;
                if (any(inWindow < 0) || any(inWindow >= _WindowSize)) return half4(0, 0, 0, 0);

                float2 index = fmod(fmod(_CentreIdx.xy + cellOffset, _WindowSize) + _WindowSize, _WindowSize);
                float4 entry = tex2Dlod(_Indirection, float4((index + 0.5) / _WindowSize, 0, 0));

                int selected = (int)_Level;
                for (int step = 0; step < 3; step++) {
                    int level = (selected + step) % 3;
                    float slicePlusOne = EntryOf(entry, level);
                    if (slicePlusOne > 0.5) return SampleTile(level, slicePlusOne - 1, uv);
                }

                return half4(0, 0, 0, 0);
            }

            fixed4 Frag(Varyings input) : SV_Target {
                half4 tile = SampleMap(input.texcoord);
                half4 color = half4(tile.rgb * input.color.rgb, tile.a * input.color.a);

                #ifdef _MAP_CIRCLE
                float radius = min(_RectSize.x, _RectSize.y) * 0.5;
                float distance = length((input.texcoord - 0.5) * _RectSize.xy);
                color.a *= saturate((radius - distance) / max(fwidth(distance), 1e-4));
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                half2 clipMask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color.a *= clipMask.x * clipMask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
