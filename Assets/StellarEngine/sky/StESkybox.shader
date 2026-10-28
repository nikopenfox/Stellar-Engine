Shader "StellarEngine/StESkybox"
{
    Properties
    {
        _LightVector("Light Vector", Vector) = (0,0,0,0) //最も明るい光源(このシェーダーでは未使用)

        //複数光源(StarSystemManager.MAX_LIGHT_SOURCE と数を揃える) Weightは各光源の構成比
        _LightCount("Light Count", float) = 1
        _LightVector0("Light Vector 0", Vector) = (0,0,0,0)
        _LightVector1("Light Vector 1", Vector) = (0,0,0,0)
        _LightVector2("Light Vector 2", Vector) = (0,0,0,0)
        _LightVector3("Light Vector 3", Vector) = (0,0,0,0)
        _LightColor0("Light Color 0", Color) = (1,1,1,1)
        _LightColor1("Light Color 1", Color) = (1,1,1,1)
        _LightColor2("Light Color 2", Color) = (1,1,1,1)
        _LightColor3("Light Color 3", Color) = (1,1,1,1)
        _LightWeight0("Light Weight 0", float) = 1
        _LightWeight1("Light Weight 1", float) = 0
        _LightWeight2("Light Weight 2", float) = 0
        _LightWeight3("Light Weight 3", float) = 0
        _Day ("Day", 2D) = "white" {}
        _Twilight ("Twilight", 2D) = "orange" {}
        _Night ("Night", 2D) = "black" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _LightColor("Light Color", Color) = (1,1,1,1)
        _LightColAffect("Light Affect", float) = 0.3 //ライト色が空へどれだけ影響するか
        _Brightness("Brightness", float) = 1
        _dayBlend ("DayBlend", float) = 0
        _twilightBlend ("TwilightBlend", float) = 0
        _nightBlend ("NightBlend", float) = 0
        _exposure ("Exposure", float) = 0
        _opacity ("Opacity", float) = 1
        _fwdScatterMultiplier ("Scatter", float) = 1
        _haze ("Haze", float) = 0.1
        _fwdScatterColor ("ForwardScatterColor", Color) = (0,0,0,0)
        _fwdScatterColorDay ("ForwardScatterColorDay", Color) = (0,0,0,0)
        _horizonColor ("Horizon Color", Color ) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Always" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
                float3 worldViewDir : TEXCOORD4;
            };

            fixed4 _LightVector;

            fixed4 _Tint;
            float _Brightness;
            fixed4 _LightColor;
            fixed _LightColAffect;
            sampler2D _Day;
            sampler2D _Twilight;
            sampler2D _Night;
            float4 _Day_ST;
            float4 _Twilight_ST;
            float4 _Night_ST;
            fixed _dayBlend, _twilightBlend, _nightBlend, _exposure;
            fixed _opacity;
            fixed3 _exposureBlend;
            fixed _fwdScatterMultiplier;
            fixed _haze;
            fixed4 _fwdScatterColor;
            fixed4 _fwdScatterColorDay;
            fixed4 _horizonColor;

            float3 n;

            #define MAX_LIGHT_SOURCE 4

            float _LightCount;
            float4 _LightVector0, _LightVector1, _LightVector2, _LightVector3;
            fixed4 _LightColor0, _LightColor1, _LightColor2, _LightColor3;
            float _LightWeight0, _LightWeight1, _LightWeight2, _LightWeight3;

            //k番目の光源の方向・色・構成比を取得する。使わない光源ならfalse
            bool GetLightSource(int k, out float3 lightDir, out fixed4 lightCol, out float weight)
            {
                float4 v = _LightVector0;
                lightCol = _LightColor0;
                weight = _LightWeight0;
                if (k == 1) { v = _LightVector1; lightCol = _LightColor1; weight = _LightWeight1; }
                else if (k == 2) { v = _LightVector2; lightCol = _LightColor2; weight = _LightWeight2; }
                else if (k == 3) { v = _LightVector3; lightCol = _LightColor3; weight = _LightWeight3; }

                lightDir = float3(0, 1, 0);
                if (k >= _LightCount || weight <= 0 || dot(v.xyz, v.xyz) <= 0) { return false; }

                lightDir = normalize(v.xyz);
                return true;
            }


            v2f vert (appdata v)
            {
                v2f o;

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _Day);
                //UNITY_TRANSFER_FOG(o,o.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos =  mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = worldPos;

                o.worldViewDir = UnityWorldSpaceViewDir(worldPos);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //太陽の方向を明るくする(散乱)

                float3 zeroVec = float3(0,0,0);
                float3 worldUpDir = float3(0,1,0);

                float3 normal = normalize(i.worldNormal); //空の座標
                float3 skyPos = normalize(i.worldPos); //空の座標
                float3 cameraPos = normalize(_WorldSpaceCameraPos); //カメラ位置
                float3 skyDir;
                float dotHorizon;

                fixed horizonDirectivity; //薄い大気の地平線方向依存性

                skyDir = normalize(-i.worldViewDir);
                dotHorizon = clamp( abs( dot(worldUpDir, skyDir) - 1), 0, 1 );
                dotHorizon = dotHorizon * dotHorizon;

                //光源ごとの散乱を、構成比(weight)で重み付けして足し合わせる
                float forwardScatterStrength = 0; //前方散乱
                fixed4 forwardScatterLight = 0; //前方散乱 × 光源色
                fixed4 forwardScatterWeakLight = 0; //弱い前方散乱 × 光源色
                float horizonReyleigh = 0, horizonReyleighBack = 0;

                [unroll]
                for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
                {
                    float3 sunDir; //太陽の方向
                    fixed4 lightCol;
                    float weight;
                    if (!GetLightSource(k, sunDir, lightCol, weight)) { continue; }

                    fixed horizonStrength = min(1, abs(sunDir.y - 1) * abs(sunDir.y - 1));   //地平線強度

                    float fwd = max(0, dot(skyDir, sunDir));
                    float back = max(0, dot(skyDir, -sunDir));

                    fwd = fwd * fwd * fwd * _fwdScatterMultiplier * lerp(1, 0.2, sunDir.y); //2乗して太陽の方向のみ明るく
                    float fwdWeak = max(0, dot(skyDir, sunDir));
                    back = max(back * back * back - 0.75, 0);

                    forwardScatterStrength += fwd * weight;
                    forwardScatterLight += fwd * lightCol * weight;
                    forwardScatterWeakLight += fwdWeak * lightCol * weight;

                    //地平線付近の赤化
                    horizonReyleigh += dotHorizon * fwd * horizonStrength * weight;
                    horizonReyleighBack += dotHorizon * back * horizonStrength * weight;
                }

                //ライト色を密度で計算
                float lightColor = lerp(float4(1,1,1,1), float4(0,0,0,0), _opacity);

                horizonDirectivity = lerp(clamp(dotHorizon * _exposure, 0, 1), 1, _exposure * _exposure);
                

                // sample the texture
                fixed4 dayCol = lerp(float4(1,1,1,1), tex2D(_Day, i.uv) * horizonDirectivity, _dayBlend) + (forwardScatterStrength *  (clamp(_haze, 0, 1.6) / _fwdScatterMultiplier) * _fwdScatterColorDay);
                fixed4 twilightCol = tex2D(_Twilight, i.uv) + (forwardScatterStrength  * _fwdScatterColor);
                fixed4 nightCol = tex2D(_Night, i.uv);

                //fixed3 exposureBlend = (1,1,1) * _exposure;

                fixed4 backScatter = _fwdScatterColor * horizonReyleighBack * _Brightness;
                fixed4 multiScatter = _LightColor * _Brightness * (_opacity * 0.2);

                fixed4 tex = fixed4((dayCol.rgb * _dayBlend * _Brightness * _Tint * lightColor) + 
                (twilightCol * _twilightBlend * _Brightness * _exposure) + 
                (nightCol * _nightBlend * _Brightness * _exposure), 1.0)
                + (_dayBlend * _fwdScatterColorDay * forwardScatterLight * 0.25 * _Brightness)
                + (dotHorizon * forwardScatterWeakLight * _Brightness * 0.3) + (_horizonColor * horizonReyleigh * 1 * _Brightness * 4)
                + multiScatter;
                
                fixed4 lAffect = normalize(lerp(float4(1,1,1,1), _LightColor, _LightColAffect )) * 2 * (1 + _LightColAffect  * _LightColAffect * 0.6);

                return tex * lAffect * _exposure;
            }
            ENDCG
        }
    }
}


