Shader "Custom/CelestialRenderer_Ring"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _localAmbientLight ("Local Ambient Light", Color) = (0,0,0,0)
        _MainTex ("Texture", 2D) = "white" {}
        _customDepthMul ("customDepthMul", Float) = 0.0001
        _brightness ("Brightness", Float) = 1.0
        _brightness2 ("Brightness2", Float) = 1.0
        _surfaceB ("Surface Brightness", Float) = 1.0
        _lightColor ("Light Color", Color) = (1,1,1,1)
        _lightVector ("Light Vector", Vector) = (0,0,0)
        _hapke ("Hapke", Float) = 0
        _oppFrac ("Opposition Frac", Float) = 1

        _atmColor ("Atmosphere Color", Color) = (1,1,1,1)
        _atmBrightness ("Atmosphere Brightness", Float) = 1.0
        _atmHeight ("Atmosphere Height", Float) = 1.015
        _atmScatter ("Atmosphere Scatter Strength", Float) = 1
        _atmOpacity ("Atmosphere Opacity", Float) = 0.1
        _atmScaleHeight ("Atmosphere Scale Height", Float) = 1

        _atmReyleigh ("Reyleigh Scatter Strength", Float) = 0.001
        _atmReyleighB ("Reyleigh Scatter Brightness", Float) = 0.04
        _reyleighR ("Reyleigh Strength R", Float) = 0.58
        _reyleighG ("Reyleigh Strength G", Float) = 1.9
        _reyleighB ("Reyleigh Strength B", Float) = 5.1
        _atmForward ("Forward Scatter Strength", Float) = 1
        
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+60" "RenderType"="Opaque" }

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
        Tags { "LightMode" = "Always" }
        Blend One One
        ZTest LEqual
        Cull Off
        ZWrite On
        //Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            //#pragma multi_compile_fog
            #pragma multi_compile_fwdadd
            //#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

            

            #include "UnityCG.cginc"
            

            float _customDepthMul;
            float _lightType;
            float _brightness;
            float _brightness2;
            float _surfaceB;
            float _hapke;
            float _oppFrac;
            fixed4 _lightColor;
            float3 _lightVector;
            fixed4 _localAmbientLight;

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
                float3 worldViewDir : TEXCOORD3;
                
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = worldPos;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                o.worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));

                //UNITY_TRANSFER_FOG(o,o.vertex);
                
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float3 normal = (1,1,1);
                float3 lightDir;
                float3 worldPosCenter = normalize(mul(unity_ObjectToWorld, float4(0,0,0,1))); //惑星中心
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                
                    // Directional Light
                    lightDir = normalize(_lightVector.xyz);
                
                // sample the texture

                
                

                //Diffuse

                float ndotl = max(0, dot(normal, lightDir));

                float ndotv = saturate(dot(normal, i.worldViewDir));

                float ldotv = saturate(dot(lightDir, i.worldViewDir));

                fixed3 diffuse = _lightColor.rgb * (_brightness + _hapke * 1.2) * 
                                    lerp(saturate(ndotl), saturate((ndotl / (ndotl + ndotv + 0.2))), _hapke );

                fixed opposition = _hapke * _hapke * _oppFrac * min(ndotl * 100, ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv);

                fixed4 col = tex2D(_MainTex, i.uv);
                fixed3 rgb = (diffuse + opposition) * col.rgb * _Color.rgb * _surfaceB * _brightness2;;
                fixed4 tex = fixed4(rgb, 1.0) + _localAmbientLight;
                // apply fog
                outDepth = i.vertex.z / i.vertex.w * _customDepthMul;
                return tex;
            }
            ENDCG
        
        }
    }
}
