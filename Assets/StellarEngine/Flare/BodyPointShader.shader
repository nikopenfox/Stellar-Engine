Shader "StellarEngine/BodyPointShader"
{
    //天体に重ねる光点と回折スパイク
    //・光点：視直径の小さい天体用。芯(ガウス)は計算、BrightPoint(明るい点の装飾)はテクスチャを加算する。大きさは画面ピクセルで一定
    //・回折スパイク：表面輝度が非常に高い天体に、光点とは関係なく描く光条。本数・角度・長さ(px)・太さ(px)を計算で描く
    //中心は位相角に応じて、照らされている側(光度中心)へずらす
    //天体本体の子のQuadに付け、オブジェクトの原点だけを使うビルボードにする(親のスケールは無視)
    Properties
    {
        _BrightPointTex ("BrightPointTexture", 2D) = "black" {} //BrightPoint(アルファを使う)
        _brightPointStrength ("BrightPointStrength", Float) = 0
        _Color ("Color", Color) = (1,1,1,1)
        _pointIntensity ("PointIntensity", Float) = 1
        _pointSize ("PointSize(px)", Float) = 4 //光点の半径(画面ピクセル)
        _coreSigma ("CoreSigma", Range(0.05, 1)) = 0.25 //芯の広がり(光点の半径を1とした値)
        _spikeIntensity ("SpikeIntensity", Float) = 0
        _spikeCount ("SpikeCount", Float) = 4 //光条の本数
        _spikeAngle ("SpikeAngle(deg)", Float) = 0 //光条の回転角
        _spikeLength ("SpikeLength(px)", Float) = 0 //光条の長さ(画面ピクセル)
        _spikeWidthRatio ("SpikeWidthRatio", Float) = 0.05 //光条の太さ(視直径のピクセル数に対する割合)
        _spikeWidthMin ("SpikeWidthMin(px)", Float) = 1 //光条の太さの下限(画面ピクセル)
        _bodyAngularRadius ("BodyAngularRadius(rad)", Float) = 0 //天体の角半径
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
                float2 px : TEXCOORD0; //中心からの位置(画面ピクセル)
                float spikeWidth : TEXCOORD1; //光条の太さ(画面ピクセル)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _BrightPointTex;
            float _brightPointStrength;
            float4 _Color;
            float _pointIntensity;
            float _pointSize;
            float _coreSigma;
            float _spikeIntensity;
            float _spikeCount;
            float _spikeAngle;
            float _spikeLength;
            float _spikeWidthRatio;
            float _spikeWidthMin;
            float _bodyAngularRadius;
            float4 _phaseDir;
            float _customDepthMul;
            float _orderedDepth;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float2 uv = v.uv * 2.0 - 1.0;

                //オブジェクトの原点をビュー空間に移す
                float3 center = UnityObjectToViewPos(float3(0, 0, 0));
                float dist = length(center);
                float pixelToView = 2.0 * abs(center.z) / (_ScreenParams.y * UNITY_MATRIX_P[1][1]);
                float bodyHalf = tan(min(_bodyAngularRadius, MAX_ANGULAR_RADIUS)) * dist;

                //位相角による中心のずらし(光源の向きを画面に投影した方向へ、角半径 × w)
                float2 lightDir = mul((float3x3)UNITY_MATRIX_V, _phaseDir.xyz).xy;
                float lightLen = length(lightDir);
                if (lightLen > 1e-5) { center.xy += lightDir / lightLen * _phaseDir.w * bodyHalf; }

                //Quadの半径(px)は光点と光条の長い方
                float halfPx = max(max(_pointSize, _spikeLength), 1e-3);
                o.px = uv * halfPx;
                //光条の太さ: 視直径のピクセル数に比例、下限あり
                o.spikeWidth = max(_spikeWidthMin, _spikeWidthRatio * 2.0 * bodyHalf / pixelToView);

                center.xy += uv * halfPx * pixelToView;
                o.vertex = mul(UNITY_MATRIX_P, float4(center, 1.0));
                return o;
            }

            float4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                //光点(光点の半径を1とした座標)
                float2 pq = i.px / max(_pointSize, 1e-3);
                float pr2 = dot(pq, pq);
                float core = exp(-pr2 / (2.0 * _coreSigma * _coreSigma));
                float inPoint = step(max(abs(pq.x), abs(pq.y)), 1.0); //光点の外ではテクスチャを読まない
                float brightPoint = tex2D(_BrightPointTex, pq * 0.5 + 0.5).a * _brightPointStrength * inPoint;
                float pointLight = (core + brightPoint) * saturate(1.0 - pr2) * _pointIntensity; //縁で0にする(四角く切れるのを防ぐ)

                //回折スパイク: 最も近い光条からの距離で求める
                float spike = 0;
                if (_spikeIntensity > 0 && _spikeLength > 0)
                {
                    float r = length(i.px);
                    float step_ = UNITY_TWO_PI / max(_spikeCount, 1.0);
                    float theta = atan2(i.px.y, i.px.x) - radians(_spikeAngle);
                    float a = theta - step_ * round(theta / step_); //最も近い光条との角度差
                    float along = r * cos(a); //光条に沿った距離
                    float across = r * abs(sin(a)); //光条からの距離
                    float w = i.spikeWidth;
                    float fade = saturate(1.0 - along / _spikeLength);
                    spike = exp(-(across * across) / (w * w)) * fade * fade * step(0.0, along) * _spikeIntensity;
                }

                //本体と同じ描画順の深度を使い、手前の天体の円盤に隠れるようにする
                outDepth = _orderedDepth > 0 ? _orderedDepth
                                             : i.vertex.z / i.vertex.w * _customDepthMul;

                return float4(_Color.rgb * (pointLight + spike), 1.0);
            }
            ENDCG
        }
    }
}
