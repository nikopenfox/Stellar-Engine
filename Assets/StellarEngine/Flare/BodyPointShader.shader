Shader "StellarEngine/BodyPointShader"
{
    //天体に重ねる光点と光芒
    //・光点：視直径の小さい天体用。芯(ガウス)は計算、BrightPoint(明るい点の装飾)はテクスチャを加算する。大きさは画面ピクセルで一定
    //・光芒：視直径の大きい天体用。光点のフェードアウトと同時にフェードインする。中心で最大、外縁で0になるよう放射状に減衰させる
    //  外縁の角半径はStarSystemManagerが視直径と輝度から決める。惑星メッシュの上に描く(夜面に消されないよう、本体と同じ深度で後から加算する)
    //光点と光芒は別のパスで描き、Quadの大きさも計算も互いに影響させない(使わない側のパスは頂点を1点にまとめて描かない)
    //中心は位相角に応じて、照らされている側(光度中心)へずらす
    //天体本体の子のQuadに付け、オブジェクトの原点だけを使うビルボードにする(親のスケールは無視)
    Properties
    {
        _BrightPointTex ("BrightPointTexture", 2D) = "black" {} //BrightPoint(アルファを使う)
        _brightPointStrength ("BrightPointStrength", Float) = 0
        _Color ("Color", Color) = (1,1,1,1)
        _pointIntensity ("PointIntensity", Float) = 0
        _pointSize ("PointSize(px)", Float) = 4 //光点の半径(画面ピクセル)
        _coreSigma ("CoreSigma", Range(0.05, 1)) = 0.25 //芯の広がり(光点の半径を1とした値)
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

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        CGINCLUDE
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
            float2 q : TEXCOORD0; //中心からの位置(光点・光芒それぞれの半径を1とした値)
            float4 vertex : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        sampler2D _BrightPointTex;
        float _brightPointStrength;
        float4 _Color;
        float _pointIntensity;
        float _pointSize;
        float _coreSigma;
        float _glareIntensity;
        float _glareAngularRadius;
        float _glareFalloff;
        float _bodyAngularRadius;
        float4 _phaseDir;
        float _customDepthMul;
        float _orderedDepth;

        //オブジェクトの原点をビュー空間に移し、位相角による中心のずらし(光源の向きを画面に投影した方向へ、角半径 × w)を加える
        float3 BillboardCenter(out float dist, out float pixelToView)
        {
            float3 center = UnityObjectToViewPos(float3(0, 0, 0));
            dist = length(center);
            pixelToView = 2.0 * abs(center.z) / (_ScreenParams.y * UNITY_MATRIX_P[1][1]);
            float bodyHalf = tan(min(_bodyAngularRadius, MAX_ANGULAR_RADIUS)) * dist;

            float2 lightDir = mul((float3x3)UNITY_MATRIX_V, _phaseDir.xyz).xy;
            float lightLen = length(lightDir);
            if (lightLen > 1e-5) { center.xy += lightDir / lightLen * _phaseDir.w * bodyHalf; }
            return center;
        }

        //中心から半径 quadHalf(ビュー空間)のQuadを作る。isVisible が偽なら頂点を1点にまとめて描かない
        v2f BillboardVert(appdata v, float3 center, float quadHalf, bool isVisible)
        {
            v2f o;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float2 uv = v.uv * 2.0 - 1.0;
            o.q = uv;
            center.xy += isVisible ? uv * quadHalf : float2(0, 0);
            o.vertex = mul(UNITY_MATRIX_P, float4(center, 1.0));
            return o;
        }

        //本体と同じ描画順の深度を使い、自分の円盤(夜面を含む)の上に描き、手前の天体の円盤に隠れるようにする
        float OrderedDepth(float4 pos)
        {
            return _orderedDepth > 0 ? _orderedDepth : pos.z / pos.w * _customDepthMul;
        }
        ENDCG

        //光点
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float dist, pixelToView;
                float3 center = BillboardCenter(dist, pixelToView);
                bool isVisible = _pointIntensity > 0 && _pointSize > 0;
                return BillboardVert(v, center, _pointSize * pixelToView, isVisible);
            }

            float4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float pr2 = dot(i.q, i.q);
                float core = exp(-pr2 / (2.0 * _coreSigma * _coreSigma));
                float brightPoint = tex2D(_BrightPointTex, i.q * 0.5 + 0.5).a * _brightPointStrength;
                float pointLight = (core + brightPoint) * saturate(1.0 - pr2) * _pointIntensity; //縁で0にする(四角く切れるのを防ぐ)

                outDepth = OrderedDepth(i.vertex);
                return float4(_Color.rgb * pointLight, 1.0);
            }
            ENDCG
        }

        //光芒
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float dist, pixelToView;
                float3 center = BillboardCenter(dist, pixelToView);
                bool isVisible = _glareIntensity > 0 && _glareAngularRadius > 0;
                return BillboardVert(v, center, tan(min(_glareAngularRadius, MAX_ANGULAR_RADIUS)) * dist, isVisible);
            }

            float4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                float x = saturate(length(i.q));
                float glare = exp(-x / max(_glareFalloff, 1e-3)) * (1.0 - x) * (1.0 - x) * _glareIntensity;

                outDepth = OrderedDepth(i.vertex);
                return float4(_Color.rgb * glare, 1.0);
            }
            ENDCG
        }
    }
}
