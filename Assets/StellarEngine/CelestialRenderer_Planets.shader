Shader "Custom/CelestialRenderer_Planets_Old"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _customDepth ("customDepth", Float) = 0.9
        _lightType ("Light Type", float) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+55" "RenderType"="Opaque" }

        /*Blend OneMinusDstColor One
        ZTest LEqual
        ZWrite On
        Cull Off*/

        /*Pass
        {
            // Zバッファだけ書くパス
            ZTest LEqual
            ZWrite On
            //ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            float _customDepth = 0.99;

            struct appdata
            {
                float4 vertex : POSITION;
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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                return (0,0,0,0);
            }
            ENDCG
        }*/

        Pass
        {
        Tags { "LightMode" = "ForwardAdd" }
        Blend OneMinusDstColor One
        ZTest LEqual
        ZWrite On
        //Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            //#pragma multi_compile_fog
            #pragma multi_compile_fwdadd
            //#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

            uniform fixed4 _LightColor0;

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            float _customDepth = 0.99;
            float _lightType;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                //UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                //UNITY_TRANSFER_FOG(o,o.vertex);
                TRANSFER_SHADOW(o)
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir;
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                if (_lightType == 0.0) {
                    // Directional Light
                    lightDir = normalize(_WorldSpaceLightPos0.xyz);
                }else {
                    //Point Light or Spot Light
                    lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
                    }
                // sample the texture

                //シャドウ係数
                float shadow = SHADOW_ATTENUATION(i);

                //ディフューズのみ　環境光なし

                float ndotl = max(0, dot(normal, lightDir));
                fixed3 diffuse = _LightColor0.rgb * ndotl * shadow;

                fixed4 col = tex2D(_MainTex, i.uv);
                fixed3 rgb = diffuse * col.rgb * _Color.rgb;
                fixed4 tex = fixed4(rgb, 1.0);
                // apply fog
                outDepth = _customDepth;
                return tex;
            }
            ENDCG
        }
    }
}
