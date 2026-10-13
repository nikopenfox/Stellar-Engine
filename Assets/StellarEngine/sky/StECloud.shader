Shader "Unlit/StECloud"
{
    Properties
    {
        
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Coverage ("Coverage", Float) = 1.0
        _WindSpeed ("Wind Speed", Float) = 1.0
        _WindVector ("Wind Vector", Vector) = (1,0,0,0)
        _Brightness ("Brightness", Float) = 1.0
        _LightVector ("Light Vector", Vector) = (0,0,0)
        
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        LOD 100
        Cull Off




        

        /*Pass
        {

            Blend SrcAlpha OneMinusSrcAlpha

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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float3 worldViewDir : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed _Coverage;
            fixed _WindSpeed;
            fixed _Brightness;
            fixed3 _LightVector;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;;

                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);

                fixed xDist = abs(v.uv.x - 0.5);
                fixed yDist = abs(v.uv.y - 0.5);

                fixed time = _Time * 0.02 * _WindSpeed;
                fixed2 uvt = fixed2(v.uv.x + time, v.uv.y + time);

                float4 uvLod = float4(v.uv.x + time, v.uv.y + time, 0.0, 0.0);
                float4 texColor = tex2Dlod(_MainTex, uvLod); // 頂点でのサンプリング

                worldPos.y -= (xDist * xDist + yDist * yDist) * 300;
                worldPos.y -= texColor.a * texColor.a * 10;

                

                o.vertex = mul(unity_MatrixVP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 worldUpDir = float3(0,1,0);
                fixed dotView = max(dot(-i.worldViewDir, _LightVector), 0);
                fixed dotHorizon = clamp( abs( dot(worldUpDir, -i.worldViewDir) - 1), 0, 1 );

                dotHorizon = dotHorizon * dotHorizon * dotHorizon;

                fixed d2 = dotView * dotView;
                fixed d4 = d2 * d2;
                fixed d8 = d4 * d4;
                dotView = d8;
                
                fixed time = _Time * 0.02 * _WindSpeed;
                fixed2 uvt = fixed2(i.uv.x + time, i.uv.y + time);
                fixed2 uvwt = fixed2(i.uv.x + time * 2, i.uv.y + time * 2.35);

                // sample the texture
                fixed4 col = tex2D(_MainTex, uvt);
                fixed4 colW = tex2D(_MainTex, uvwt);
                fixed alphaO = col.a * 0.5;
                fixed alphaW = colW.a * colW.a * 0.5;

                fixed cloudThickness = clamp(col.a - alphaW - (1 - _Coverage), 0, 1) * min(1 / _Coverage, 1000);

                fixed opacity = (_Coverage);

                fixed alpha = max(1,opacity) * cloudThickness * lerp(1, 0.25 * max(1, opacity), dotHorizon);
                fixed4 res = fixed4(((col.rgb * _Color.rgb + (_Color.rgb * 0.8 - max(cloudThickness, 0.93)) + ((1.1-cloudThickness) * _Color.rgb * dotView * 2.5 / max(1,opacity * opacity))))  / max(1,opacity) * _Brightness * 1.8, 
                min(1,alpha * 1));

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return res;
            }
            ENDCG
        }
        */
        Pass
        {

            Blend SrcAlpha OneMinusSrcAlpha

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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(2)
                float4 vertex : SV_POSITION;
                float3 worldViewDir : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed _Coverage;
            fixed _WindSpeed;
            fixed2 _WindVector;
            fixed _Brightness;
            fixed3 _LightVector;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;

                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);

                fixed xDist = abs(v.uv.x - 0.5);
                fixed yDist = abs(v.uv.y - 0.5);

                worldPos.y -= (xDist * xDist + yDist * yDist) * 300;

                o.vertex = mul(unity_MatrixVP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 worldUpDir = float3(0,1,0);
                fixed dotView = max(dot(-i.worldViewDir, _LightVector), 0);
                fixed dotHorizon = clamp( abs( dot(worldUpDir, -i.worldViewDir) - 1), 0, 1 );

                dotHorizon = dotHorizon * dotHorizon * dotHorizon;

                fixed d2 = dotView * dotView;
                fixed d4 = d2 * d2;
                fixed d8 = d4 * d4;
                dotView = d8;
                
                //風

                fixed time = _Time * 0.02 * _WindSpeed;
                fixed2 uvt = fixed2(i.uv.x + time * _WindVector.x, i.uv.y + time * _WindVector.x);
                fixed2 uvwt = fixed2(i.uv.x + time * _WindVector.x * 2, i.uv.y + time *  _WindVector.y * 2.35);

                // sample the texture
                fixed4 col = tex2D(_MainTex, uvt);
                fixed4 colW = tex2D(_MainTex, uvwt);
                fixed alphaO = col.a * 0.5;
                fixed alphaW = colW.a * colW.a * 0.5;

                fixed cloudThickness = clamp(col.a - alphaW - (1 - _Coverage), 0, 1) * min(1 / _Coverage, 1000);

                fixed opacity = (_Coverage);

                fixed alpha = max(1,opacity) * cloudThickness * lerp(1, 0.25 * max(1, opacity), dotHorizon);
                fixed4 res = fixed4(((col.rgb * _Color.rgb + (_Color.rgb * 0.8 - max(cloudThickness, 0.93)) + ((1.1-cloudThickness) * _Color.rgb * dotView * 2.5 / max(1,opacity * opacity))))  / max(1,opacity) * _Brightness * 1.8, 
                min(1,alpha + (0)));

                // apply fog
                //UNITY_APPLY_FOG(i.fogCoord, res);
                return res;
            }
            ENDCG
        }
        
    }
}
