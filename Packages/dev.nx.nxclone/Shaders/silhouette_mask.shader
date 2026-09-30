Shader "nxclone/silhouette mask" {
    SubShader {
        Tags { "Queue"="Transparent+49" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always ColorMask 0
        // Bit 0 marks the primary; bits 1-4 block and dedupe each of four ghosts.
        Stencil { Ref 31 ReadMask 31 WriteMask 31 Comp Always Pass Replace }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return 0; }
            ENDCG
        }
    }
}
