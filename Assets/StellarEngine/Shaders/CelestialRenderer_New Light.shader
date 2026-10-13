Shader "Custom/CelestialRenderer_New_Light"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _localAmbientLight ("Local Ambient Light", Color) = (0,0,0,0)
        _MainTex ("Texture", 2D) = "white" {}
        _customDepthMul ("customDepthMul", Float) = 0.01
        _brightness ("Brightness", Float) = 1.0
        _surfaceB ("Surface Brightness", Float) = 1.0
        _lightColor ("Light Color", Color) = (1,1,1,1)
        _lightVector ("Light Vector", Vector) = (0,0,0)
        _atmColor ("Atmosphere Color", Color) = (1,1,1,1)
        _atmBrightness ("Atmosphere Brightness", Float) = 1.0
        _atmHeight ("Atmosphere Height", Float) = 1.02
        _atmScatter ("Atmosphere Scatter Strength", Float) = 0.5
        _atmOpacity ("Atmosphere Opacity", Float) = 0.5

        _atmReyleigh ("Reyleigh Scatter Strength", Float) = 1
        _atmReyleighB ("Reyleigh Scatter Brightness", Float) = 1
        _reyleighR ("Reyleigh Strength R", Float) = 0.58
        _reyleighG ("Reyleigh Strength G", Float) = 1.35
        _reyleighB ("Reyleigh Strength B", Float) = 3.31
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

            

            #include "UnityCG.cginc"
            

            float _customDepthMul;
            float _lightType;
            float _brightness;
            float _surfaceB;
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
                
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir;
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                
                    // Directional Light
                    lightDir = normalize(_lightVector.xyz);
                
                // sample the texture

                
                

                //ディフューズのみ　環境光なし

                float ndotl = max(0, dot(normal, lightDir));
                fixed3 diffuse = _lightColor.rgb * _brightness * ndotl;

                fixed4 col = tex2D(_MainTex, i.uv);
                fixed3 rgb = diffuse * col.rgb * _Color.rgb * _surfaceB;
                fixed4 tex = fixed4(rgb, 1.0) + _localAmbientLight;
                // apply fog
                outDepth = i.vertex.z / i.vertex.w * _customDepthMul;
                return tex;
            }
            ENDCG
        }



        //大気
        Pass
        {
        Tags { "LightMode" = "Always" }
        Blend One One
        ZTest LEqual
        Cull Off
        ZWrite Off
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
            fixed4 _lightColor;
            float3 _lightVector;
            fixed4 _localAmbientLight;
            fixed4 _atmColor;
            fixed _atmHeight;
            fixed _atmScatter;
            fixed _atmOpacity;
            fixed _atmBrightness;

            fixed _atmReyleigh;
            fixed _atmReyleighB;
            fixed _reyleighR;
            fixed _reyleighG;
            fixed _reyleighB;

            float reyleigh;

            fixed _atmForward;

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

            //float3 worldViewDir;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex * _atmHeight);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldPos = worldPos;

                o.worldNormal = UnityObjectToWorldNormal(v.normal + 0.06);

                o.worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                //UNITY_TRANSFER_FOG(o,o.vertex);
                
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir;
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                
                    // Directional Light
                    lightDir = normalize(_lightVector.xyz);
                
                // sample the texture

                
                

                
                //Diffuse計算
                float ndotl = max(0, dot(normal, lightDir));
                float ndotl2 = dot(normal, lightDir);
                fixed3 diffuse = _lightColor.rgb * _brightness * ndotl;

                //角度が浅いほど明るく
                float ndotv = max(-1, dot(normal, i.worldViewDir) - 1);
                float scatter = min(_atmScatter * 1, -ndotv * (_atmScatter * 3) + _atmScatter * _atmOpacity);

                //レイリー散乱 -1して符号を反転し、0(薄明)で最大になるようにする
                fixed atmMin = 0.001;
                float pdotl = max(0, dot(normalize(i.worldPos - _WorldSpaceCameraPos), lightDir));
                fixed reyleigh =  1 - (ndotl * ndotl * 30); 
                fixed reyR = _atmReyleigh * reyleigh * (1 - _reyleighR);
                fixed reyG = _atmReyleigh * reyleigh * (1 - _reyleighG);
                fixed reyB = _atmReyleigh * reyleigh * (1 - _reyleighB);

                fixed3 reyColor = normalize(fixed3(reyR + atmMin, reyG+ atmMin, reyB+ atmMin)) * _atmReyleighB;

                //前方散乱

                float forward = pdotl * pdotl * pdotl * pdotl;

                fixed3 atmCol = (diffuse * scatter + (reyColor * diffuse)  + (forward * diffuse * 20 * _atmForward)) * _atmColor.rgb * (_atmBrightness * 0.05) * _brightness;
                //fixed3 atmCol = reyColor;
                fixed4 tex = fixed4(atmCol, 1.0) + _localAmbientLight;
                // apply fog
                outDepth = i.vertex.z / i.vertex.w * _customDepthMul;
                return tex;
            }
            ENDCG
        }
    }
}
