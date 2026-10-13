Shader "StellarEngine/StEStarMap"
{
    Properties
    {
        _LightVector ("Light Vector", Vector) = (0,0,0)
        _BrightStars ("BrightStars", 2D) = "black" {}
        _MidStars ("MidStars", 2D) = "black" {}
        _DimStars ("DimStars", 2D) = "black" {}
        _colorVary ("ColorVary", Color) = (1,1,1,1)
        _brightness ("Brightness", float) = 1.0
        _atmOpacity ("Atmosphere Opacity", float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue" = "Transparent-200" }
        LOD 100

        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            fixed _brightness;

            fixed brightMin;
            fixed midMin;
            fixed dimMin;

            fixed brightness_inter;

            fixed sl_b;
            fixed sl_m;
            fixed sl_d;

            fixed sa_b;
            fixed sa_m;
            fixed sa_d;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv1 : TEXCOORD0;
                float2 uv2 : TEXCOORD0;
                fixed3 worldViewDir : TEXCOORD2;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            fixed4 _LightVector;
            fixed4 _colorVary;
            sampler2D _BrightStars;
            float4 _BrightStars_ST;
            sampler2D _MidStars;
            float4 _MidStars_ST;
            sampler2D _DimStars;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv1 = TRANSFORM_TEX(v.uv, _BrightStars);
                o.uv2 = TRANSFORM_TEX(v.uv, _MidStars);
                o.worldViewDir = UnityWorldSpaceViewDir(mul(unity_ObjectToWorld, v.vertex).xyz);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed ilerp(fixed a, fixed b, fixed value)
            {
                return saturate((value - a) / (b - a));
            }

            fixed4 frag (v2f i, out float outDepth : SV_Depth) : SV_Target
            {
                // sample the texture
                fixed4 starB = tex2D(_BrightStars, i.uv1);
                fixed4 starM = tex2D(_MidStars, i.uv2);
                fixed4 starD = tex2D(_DimStars, i.uv2);

                //
                fixed3 worldUpDir = fixed3(0,1,0);
                fixed3 skyDir = normalize(-i.worldViewDir);

                fixed dotHorizon = saturate( abs( dot(worldUpDir, skyDir) + 0.4));

                dotHorizon = dotHorizon;

                //fixed attenuationLight = (normalize(i.worldViewDir), _LightVector);

                //attenuationLight = saturate(attenuationLight * attenuationLight);

                //fixed4 tex1 = fixed4(starB.rgb * _colorVary.rgb, starB.a * clamp(_brightness + 0.5, 0, 1));
                //fixed4 tex2 = fixed4(starM.rgb * _colorVary.rgb, starM.a * clamp(_brightness - 0.175, 0, 1));
                //fixed4 tex3 = fixed4(starD.rgb * _colorVary.rgb, starD.a * clamp(_brightness - 0.55, 0, 1));

                fixed t1b = (1 - ilerp(sa_b, sl_b, _brightness));
                fixed t2b = (1 - ilerp(sa_m, sl_m, _brightness));
                fixed t3b = (1 - ilerp(sa_d, sl_d, _brightness));

                fixed4 tex1 = saturate(fixed4(starB.rgb * _colorVary.rgb, starB.a * t1b * t1b));
                fixed4 tex2 = saturate(fixed4(starM.rgb * _colorVary.rgb, starM.a * t2b * t2b));
                fixed4 tex3 = saturate(fixed4(starD.rgb * _colorVary.rgb, starD.a * t3b * t3b));

                outDepth = 0;
                return (tex1 + tex2 + tex3) * saturate(dotHorizon + 1) * lerp(fixed4(1,0.2,0,1), fixed4(1,1,1,1), dotHorizon);
            }

            

            ENDCG
        }
    }
}
