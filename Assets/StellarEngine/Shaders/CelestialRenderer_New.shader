Shader "Custom/CelestialRenderer_New"
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
        _lightVector ("Light Vector", Vector) = (0,0,0) //最も明るい光源(単一光源のシェーダー向け、このシェーダーでは未使用)

        //複数光源(StarSystemManager.MAX_LIGHT_SOURCE と数を揃える)
        _lightCount ("Light Count", Float) = 1
        _lightVector0 ("Light Vector 0", Vector) = (1,0,0,0)
        _lightVector1 ("Light Vector 1", Vector) = (0,0,0,0)
        _lightVector2 ("Light Vector 2", Vector) = (0,0,0,0)
        _lightVector3 ("Light Vector 3", Vector) = (0,0,0,0)
        _lightColor0 ("Light Color 0", Color) = (1,1,1,1)
        _lightColor1 ("Light Color 1", Color) = (1,1,1,1)
        _lightColor2 ("Light Color 2", Color) = (1,1,1,1)
        _lightColor3 ("Light Color 3", Color) = (1,1,1,1)
        _lightIntensity0 ("Light Intensity 0", Float) = 1
        _lightIntensity1 ("Light Intensity 1", Float) = 0
        _lightIntensity2 ("Light Intensity 2", Float) = 0
        _lightIntensity3 ("Light Intensity 3", Float) = 0
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

        //地表・大気の両パスで使う複数光源
        CGINCLUDE
            #define MAX_LIGHT_SOURCE 4

            float _lightCount;
            float4 _lightVector0, _lightVector1, _lightVector2, _lightVector3;
            fixed4 _lightColor0, _lightColor1, _lightColor2, _lightColor3;
            float _lightIntensity0, _lightIntensity1, _lightIntensity2, _lightIntensity3;

            //k番目の光源の方向と色(明るさ込み)を取得する。使わない光源ならfalse
            bool GetLightSource(int k, out float3 lightDir, out fixed3 lightCol)
            {
                float4 v = _lightVector0;
                fixed4 c = _lightColor0;
                float s = _lightIntensity0;
                if (k == 1) { v = _lightVector1; c = _lightColor1; s = _lightIntensity1; }
                else if (k == 2) { v = _lightVector2; c = _lightColor2; s = _lightIntensity2; }
                else if (k == 3) { v = _lightVector3; c = _lightColor3; s = _lightIntensity3; }

                lightDir = float3(0, 0, 1);
                lightCol = 0;
                if (k >= _lightCount || s <= 0 || dot(v.xyz, v.xyz) <= 0) { return false; }

                lightDir = normalize(v.xyz);
                lightCol = c.rgb * s;
                return true;
            }
        ENDCG

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
        Blend SrcAlpha One
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
                float3 normal = normalize(i.worldNormal);
                float3 worldPosCenter = normalize(mul(unity_ObjectToWorld, float4(0,0,0,1))); //惑星中心
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                float ndotv = saturate(dot(normal, i.worldViewDir));

                //Diffuse 光源ごとの拡散反射と衝効果を足し合わせる
                fixed3 diffuse = 0;
                fixed3 opposition = 0;

                [unroll]
                for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
                {
                    float3 lightDir;
                    fixed3 lightCol;
                    if (!GetLightSource(k, lightDir, lightCol)) { continue; }

                    float ndotl = max(0, dot(normal, lightDir));

                    float ldotv = saturate(dot(lightDir, i.worldViewDir));

                    diffuse += lightCol * (_brightness + _hapke * 1.2) *
                                    lerp(saturate(ndotl), saturate((ndotl / (ndotl + ndotv + 0.2))), _hapke );

                    opposition += lightCol * _hapke * _hapke * _oppFrac * min(ndotl * 100, ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv * ldotv);
                }

                diffuse *= _lightColor.rgb;

                fixed4 col = tex2D(_MainTex, i.uv);
                fixed3 rgb = (diffuse + opposition) * col.rgb * _Color.rgb * _surfaceB * _brightness2;;
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
        Tags { "LightMode" = "Always" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZTest LEqual
        Cull Off
        ZWrite Off
        //Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            //#pragma multi_compile_fog
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            //#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

            

            #include "UnityCG.cginc"
            
            float _customDepthMul;
            float _lightType;
            float _brightness;
            float _brightness2;
            fixed4 _lightColor;
            float3 _lightVector;
            fixed4 _localAmbientLight;
            fixed4 _atmColor;
            fixed _atmHeight;
            fixed _atmScatter;
            fixed _atmOpacity;
            fixed _atmScaleHeight;
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
                
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                o.worldPos = worldPos;

                o.worldNormal = UnityObjectToWorldNormal(v.normal + 0.06);

                o.worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                //UNITY_TRANSFER_FOG(o,o.vertex);
                
                return o;
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);

                //大気減衰に必要な情報
                float3 cameraPos = _WorldSpaceCameraPos;
                float3 worldPosCenter = mul(unity_ObjectToWorld, float4(0,0,0,1)); //惑星中心
                //float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                //-----ここから視線のみで決まる値-----

                //角度が浅いほど明るく
                float ndotv = max(-1, dot(normal, i.worldViewDir) - 1);
                float scatter = min(_atmScatter * 1, -ndotv * (_atmScatter * 3) + _atmScatter * _atmOpacity);

                fixed atmMin = 0.001;

                //大気減衰
                float3 l = cameraPos - worldPosCenter; //中心から原点へのベクトル
                float3 d = normalize(i.worldPos - cameraPos);

                float RSurf = length(i.worldPos - worldPosCenter); //地表面の半径
                float R = RSurf * _atmHeight; //仮想大気球の半径
                

                //視線ベクトル(直線)と大気球+惑星球の二次方程式を解く
                float b = dot(d, l);
                float c = dot(l, l) - R*R;
                float discriminant = b*b - c;
                float t1 = -b - sqrt(discriminant);

                //惑星球
                float cS = dot(l, l) - RSurf*RSurf;
                float discriminantS = b*b - cS;
                float t2 = min(-b + sqrt(discriminant), -b + sqrt(discriminantS));

                float scale = R - RSurf;
                float path_view = (t2 - t1) / (R - RSurf);

                // 太陽方向の光路長を別途計算
                // 惑星中心から見た視点位置を使い、太陽方向の視線で交差を求める
                float3 l_sun = cameraPos - worldPosCenter; // カメラ→惑星中心ベクトル（lと同じ）

                //-----ここから光源ごとの値-----
                //Blend SrcAlpha One なので、各光源の (色 × 不透明度) を足し合わせれば光源ごとに描いたのと同じになる
                fixed3 atmColSum = 0;
                float tauMax = -1e10;

                [unroll]
                for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
                {
                    float3 lightDir;
                    fixed3 lightCol;
                    if (!GetLightSource(k, lightDir, lightCol)) { continue; }

                    //Diffuse計算
                    float ndotl = max(0, dot(normal, lightDir));
                    fixed3 diffuse = _lightColor.rgb * lightCol * _brightness * ndotl;

                    //レイリー散乱 -1して符号を反転し、0(薄明)で最大になるようにする
                    float pdotl = max(0, dot(normalize(i.worldPos - _WorldSpaceCameraPos), lightDir));
                    fixed reyleigh =  1 - (ndotl * ndotl * 30);
                    fixed reyR = _atmReyleigh * reyleigh * (1 - _reyleighR);
                    fixed reyG = _atmReyleigh * reyleigh * (1 - _reyleighG);
                    fixed reyB = _atmReyleigh * reyleigh * (1 - _reyleighB);

                    fixed3 reyColor = normalize(fixed3(reyR + atmMin, reyG+ atmMin, reyB+ atmMin)) * _atmReyleighB;

                    float beta = reyColor * 0.01;

                    // 太陽方向ベクトルで大気球との交差
                    float b_sun = dot(lightDir, l_sun);
                    float discriminant_sun = b_sun * b_sun - (dot(l_sun, l_sun) - R * R);
                    float discriminantS_sun = b_sun * b_sun - (dot(l_sun, l_sun) - RSurf * RSurf);

                    float t1_sun = -b_sun - sqrt(max(0, discriminant_sun));
                    float t2_sun = min(
                        -b_sun + sqrt(max(0, discriminant_sun)),
                        -b_sun + sqrt(max(0, discriminantS_sun))
                    );
                    float path_sun = clamp((t2_sun - t1_sun) / (R - RSurf), 0.01, 1000);

                    // 視線方向と太陽方向、長い方を採用
                    float path = max(path_view, path_sun);

                    float3 T_view = exp(-beta * path);
                    float3 T_sun  = exp(-beta * path * pdotl);
                    float tau = (_atmScaleHeight * _atmHeight) * 1e-8 * path * path * path * path * path * path * path;
                    float tau2 = path;

                    tauMax = max(tauMax, tau);

                    //前方散乱

                    float forward = pdotl * pdotl * pdotl * pdotl;

                    float3 inScatter = forward * forward * diffuse * 20 * _atmForward;
                    float3 reyResult = reyColor * tau2 * diffuse;

                    fixed3 atmCol = (diffuse * scatter + max(reyResult, 2 * reyResult * (inScatter * T_sun * T_view)) + inScatter ) * _atmColor.rgb * (_atmBrightness * 0.05) * _brightness * _brightness2;;
                    //fixed3 atmCol = (0.1,0.0001,0.001) * tau;
                    atmColSum += atmCol * min(1,tau);
                }

                //どの光源でも大気がなければ描かない
                clip(tauMax + 0.01);

                // apply fog
                outDepth = i.vertex.z / i.vertex.w * _customDepthMul;
                float viewFactor = min(ndotv - 0.25, 0);
                return fixed4(atmColSum * _Color.rgb * viewFactor, _Color.a * viewFactor);
            }
            ENDCG
        }
    }
}
