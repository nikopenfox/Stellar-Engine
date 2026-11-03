Shader "StellarEngine/BodyGlareShader"
{
    //天体に重ねる光芒。視直径の大きい天体用で、光点のフェードアウトと同時にフェードインする
    //中心で最大、外縁で0になるよう放射状に減衰させる。外縁の角半径はStarSystemManagerが視直径と輝度から決める
    //惑星メッシュの上に描く(夜面に消されないよう、本体と同じ深度で後から加算する)
    //中心は位相角に応じて、照らされている側(光度中心)へずらす
    //天体本体の子のQuadに付け、オブジェクトの原点だけを使うビルボードにする(親のスケールは無視)
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _glareIntensity ("GlareIntensity", Float) = 0
        _glareAngularRadius ("GlareAngularRadius(rad)", Float) = 0 //光芒の外縁の角半径
        _glareFalloff ("GlareFalloff", Range(0.01, 1)) = 0.25 //減衰の速さ(外縁を1とした値。小さいほど中心に集まる)
        _bodyAngularRadius ("BodyAngularRadius(rad)", Float) = 0 //天体の角半径(中心のずらし量に使う)
        _phaseDir ("PhaseDir", Vector) = (0,0,0,0) //xyz:天体から光源への向き(ワールド) w:中心のずらし量(角半径に対する割合)
        _customDepthMul ("CustomDepth", Float) = 0.0001
        _orderedDepth ("OrderedDepth", Float) = 0 //描画順の深度値(1が最近傍、0が最遠)。0なら従来の深度。StarSystemManagerが設定
    }
    SubShader
    {
        //天体本体(BASEQUEUE 2510〜)の後、星空(Transparent-200)の前
        Tags { "Queue"="Transparent-300" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            #define MAX_ANGULAR_RADIUS 1.4 //tanが発散しないよう角半径(rad)を抑える

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0; //-1〜1(光芒の外縁を1とした値)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Color;
            float _glareIntensity;
            float _glareAngularRadius;
            float _glareFalloff;
            float _bodyAngularRadius;
            float4 _phaseDir;
            float _customDepthMul;
            float _orderedDepth;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.uv = v.uv * 2.0 - 1.0;

                //オブジェクトの原点をビュー空間に移す
                float3 center = UnityObjectToViewPos(float3(0, 0, 0));
                float dist = length(center);
                float bodyHalf = tan(min(_bodyAngularRadius, MAX_ANGULAR_RADIUS)) * dist;
                float glareHalf = tan(min(_glareAngularRadius, MAX_ANGULAR_RADIUS)) * dist;

                //位相角による中心のずらし(光源の向きを画面に投影した方向へ、角半径 × w)
                float2 lightDir = mul((float3x3)UNITY_MATRIX_V, _phaseDir.xyz).xy;
                float lightLen = length(lightDir);
                if (lightLen > 1e-5) { center.xy += lightDir / lightLen * _phaseDir.w * bodyHalf; }

                center.xy += o.uv * glareHalf;
                o.vertex = mul(UNITY_MATRIX_P, float4(center, 1.0));
                return o;
            }

            float4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float x = saturate(length(i.uv));
                float glare = exp(-x / _glareFalloff) * (1.0 - x) * (1.0 - x) * _glareIntensity;

                //本体と同じ深度にして、自分の円盤(夜面を含む)の上に描き、手前の天体には隠れるようにする
                outDepth = _orderedDepth > 0 ? _orderedDepth
                                             : i.vertex.z / i.vertex.w * _customDepthMul;

                return float4(_Color.rgb * glare, 1.0);
            }
            ENDCG
        }
    }
}
