Shader "Custom/SunRenderer"
{

    Properties 
    {

        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex("Texture", 2D) = "white" {}
        _customDepthMul("CustomDepth", float) = 0.001
        _orderedDepth("OrderedDepth", float) = 0 //Render-order depth value (1: nearest, 0: farthest). 0 = legacy depth. Set by StarSystemManager
        _emissive("EmissiveCol", Color) = (1,1,1,1)
        _intensity("Intensity", float) = 0.0
        }

    SubShader
    {
        Tags { "Queue"="AlphaTest+70" "RenderType"="Opaque" }

        Pass
        {
        Blend SrcAlpha One
        ZTest LEqual
        ZWrite On
        //Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            fixed _customDepthMul;
            float _orderedDepth;
            fixed _intensity;
            fixed4 _color;
            fixed4 _emissive;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                fixed3 rgb = col.rgb * _Color.rgb * (_emissive.rgb * _intensity);
                fixed4 tex = fixed4(rgb, 1.0);
                //If render order is set, use the render-order depth value for the whole body
                outDepth = _orderedDepth > 0 ? _orderedDepth
                                                : i.vertex.z / i.vertex.w * _customDepthMul;
                return tex;
            }
            ENDCG
        }
    }
}
