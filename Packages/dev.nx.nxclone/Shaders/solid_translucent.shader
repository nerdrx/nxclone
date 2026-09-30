Shader "nxclone/solid translucent" {
    Properties { _Color ("Color", Color) = (0.3,0.8,1,0.25) }
    SubShader {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" }
        // One accepted fragment per pixel prevents front/back surfaces from darkening
        // the silhouette. nxclone reserves low stencil bits 0 and 1: the primary
        // mask and the shared afterimage union marker.
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Stencil { Ref 0 ReadMask 3 WriteMask 2 Comp Equal Pass Invert }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return _Color; }
            ENDCG
        }
    }
}
