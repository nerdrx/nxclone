Shader "nxclone/solid translucent" {
    Properties {
        _Color ("Color", Color) = (0.3,0.8,1,0.25)
        _CameraFadeStart ("Camera Fade Start", Float) = 0.25
        _CameraFadeEnd ("Camera Fade End", Float) = 0.6
        _StencilBit ("Afterimage Stencil Bit", Float) = 2
    }
    SubShader {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" }
        // One accepted fragment per pixel prevents front/back surfaces from darkening
        // each silhouette. Bit 0 protects the primary; bits 1-4 identify four ghosts.
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            Stencil { Ref 0 ReadMask [_StencilBit] WriteMask [_StencilBit] Comp Equal Pass Invert }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _CameraFadeStart, _CameraFadeEnd;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 viewPos : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex = UnityObjectToClipPos(v.vertex); o.viewPos = UnityObjectToViewPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 color = _Color;
                color.a *= smoothstep(_CameraFadeStart, max(_CameraFadeEnd, _CameraFadeStart + 0.001), length(i.viewPos));
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }
}
