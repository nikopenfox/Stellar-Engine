Shader "StellarEngine/BodySpikeShader"
{
    //天体に重ねる回折スパイクと点広がり関数(PSF)
    //・回折スパイク：表面輝度が非常に高い天体に、光点とは関係なく描く光条。本数・角度・長さ(px)・太さ(px)を計算で描く
    //・PSF：観測者の光学系の細かな特性を表すテクスチャ(_observerPSF)。強さと大きさ(px)は点光源とみなしたときの相対輝度(等級)から決める
    //  PSFはキーワード _USE_PSF のバリアントだけで描く。キーワードなしのバリアントはテクスチャを宣言も参照もしない(PSFを使わない天体にImporterが割り当てる)
    //回折スパイクとPSFは別のパスで描き、Quadの大きさも計算も互いに影響させない(使わない側のパスは頂点を1点にまとめて描かない)
    //地表のオブジェクトに描画で隠されないよう、Overlay直前のキューで深度テストなしに描く
    //遮蔽は発光中心(位相角でずらした中心)の _CameraDepthTexture を頂点シェーダーで1点だけ読んで判定する
    //深度が _occlusionDepth 未満(何も描かれていない場合を含む)なら全体を描き、そうでなければ頂点を1点にまとめて隠す
    //天体どうしの遮蔽(天体本体は深度テクスチャに入らない)はStarSystemManagerが判定してRendererを無効にする
    //天体本体の子のQuadに付け、オブジェクトの原点だけを使うビルボードにする(親のスケールは無視)
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _spikeIntensity ("SpikeIntensity", Float) = 0
        _spikeCount ("SpikeCount", Float) = 4 //光条の本数
        _spikeAngle ("SpikeAngle(deg)", Float) = 0 //光条の回転角
        _spikeLength ("SpikeLength(px)", Float) = 0 //光条の長さ(画面ピクセル)
        _spikeWidth ("SpikeWidth(px)", Float) = 1 //光条の太さ(画面ピクセル)。StarSystemManagerが視直径から決める
        [Toggle(_USE_PSF)] _usePSF ("UsePSF", Float) = 0 //PSFを描くバリアントにするか
        _observerPSF ("ObserverPSF", 2D) = "black" {} //点広がり関数(RGBを使う。中心が天体の位置)
        _psfIntensity ("PSFIntensity", Float) = 0
        _psfSize ("PSFSize(px)", Float) = 0 //PSFテクスチャの半径(画面ピクセル)
        _bodyAngularRadius ("BodyAngularRadius(rad)", Float) = 0 //天体の角半径(中心のずらし量に使う)
        _phaseDir ("PhaseDir", Vector) = (0,0,0,0) //xyz:天体から光源への向き(ワールド) w:中心のずらし量(角半径に対する割合)
        _occlusionDepth ("OcclusionDepth", Float) = 0.0000001 //遮蔽判定の深度(1が最近傍、0が最遠)。これ以上なら隠す
    }
    SubShader
    {
        //地表のオブジェクト(不透明・透明)より後に描く
        Tags { "Queue"="Overlay-300" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off

        CGINCLUDE
        #pragma target 3.0
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
            float4 vertex : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _Color;
        float _spikeIntensity;
        float _spikeCount;
        float _spikeAngle;
        float _spikeLength;
        float _spikeWidth;
        float _psfIntensity;
        float _psfSize;
        float _bodyAngularRadius;
        float4 _phaseDir;
        float _occlusionDepth;

        //中心から半径 halfPx(画面ピクセル)のQuadを作る
        //発光中心の深度を1点だけ読み、地表のオブジェクトに隠れているか、isActive が偽なら頂点を1点にまとめて描かない
        v2f BillboardVert(appdata v, float halfPx, bool isActive)
        {
            v2f o;
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

            //発光中心の深度を1点だけ読む。画面外では読めないので描く側に倒す
            float4 centerClip = mul(UNITY_MATRIX_P, float4(center, 1.0));
            float4 screenPos = ComputeScreenPos(centerClip);
            float2 suv = screenPos.xy / screenPos.w;
            bool isOnScreen = centerClip.w > 0 && all(suv >= 0) && all(suv <= 1);
            float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(suv, 0, 0));
            #if !defined(UNITY_REVERSED_Z)
            rawDepth = 1.0 - rawDepth; //1が最近傍にそろえる
            #endif
            bool isVisible = isActive && (!isOnScreen || rawDepth < _occlusionDepth);

            o.px = uv * halfPx;

            //隠れているときは頂点を1点にまとめる(面積0になり描かれない)
            center.xy += isVisible ? uv * halfPx * pixelToView : float2(0, 0);
            o.vertex = mul(UNITY_MATRIX_P, float4(center, 1.0));
            return o;
        }
        ENDCG

        //回折スパイク
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return BillboardVert(v, max(_spikeLength, 1e-3), _spikeIntensity > 0 && _spikeLength > 0);
            }

            float4 frag (v2f i) : SV_Target
            {
                //最も近い光条からの距離で求める
                float r = length(i.px);
                float step_ = UNITY_TWO_PI / max(_spikeCount, 1.0);
                float theta = atan2(i.px.y, i.px.x) - radians(_spikeAngle);
                float a = theta - step_ * round(theta / step_); //最も近い光条との角度差
                float along = r * cos(a); //光条に沿った距離
                float across = r * abs(sin(a)); //光条からの距離
                float w = max(_spikeWidth, 1e-3);
                float fade = saturate(1.0 - along / max(_spikeLength, 1e-3));
                float spike = exp(-(across * across) / (w * w)) * fade * fade * step(0.0, along) * _spikeIntensity;

                return float4(_Color.rgb * spike, 1.0);
            }
            ENDCG
        }

        //点広がり関数(PSF)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_PSF

            #ifdef _USE_PSF
            sampler2D _observerPSF;
            #endif

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
            #ifdef _USE_PSF
                return BillboardVert(v, max(_psfSize, 1e-3), _psfIntensity > 0 && _psfSize > 0);
            #else
                return BillboardVert(v, 1e-3, false); //PSFを使わないバリアントは描かない
            #endif
            }

            float4 frag (v2f i) : SV_Target
            {
            #ifdef _USE_PSF
                float2 q = i.px / max(_psfSize, 1e-3); //PSFの半径を1とした座標
                float3 psf = tex2D(_observerPSF, q * 0.5 + 0.5).rgb * _psfIntensity;
                return float4(_Color.rgb * psf, 1.0);
            #else
                return float4(0, 0, 0, 1);
            #endif
            }
            ENDCG
        }
    }
}
