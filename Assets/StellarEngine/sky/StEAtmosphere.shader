Shader "StellarEngine/StEAtmosphere"
{
    //地上から見た空(スカイボックス)
    //・LUT(_AtmLUT)：AtmosphereLUTBaker が空のグラデーションから焼き込んだ、海抜0での空の色(鉛直方向の分布)
    //    幅 N × 高さ 1、u = 視線の天頂角 / 90°。太陽方向の位相関数と太陽側の透過率は焼き込みで取り除いてある
    //・空の色 = Σ光源 色 × 強さ × LUT(高度で補正した天頂角) × 密度の補正 × 位相関数 × 太陽側の透過率
    //  ・高度で補正した天頂角：観測者の高さでの視線の空気の量(その高さの鉛直方向を1とした比 m)と同じ比になる、海抜0での天頂角
    //    Chapman 関数の近似(Schüler 2012) c / (c・μ + 1) を逆に解いて μ' = 1/m - 1/c(c = √(惑星半径 / H))
    //  ・密度の補正：(1 - e^(-実際の視線の光学的厚さ)) / (1 - e^(-海抜0で同じ角度の光学的厚さ))。観測者が高いほど、密度が小さいほど暗くなる
    //  ・位相関数 × 太陽側の透過率：Rayleigh と Mie(Henyey-Greenstein) を _MieRatio で混ぜる。位相関数は等方散乱を1とした値(4π倍)
    //    Rayleigh：散乱は視線上の各高さで起きるので、視線上で積分した透過率(上空ほど太陽光の通る空気が少なく、天頂付近の赤化は弱い)
    //      太陽側の光学的厚さはその点の密度に比例するとみなし、視線を代表点で前半(代表点に近づく)と後半(離れる)に分けて積分する
    //      (下向きの視線では観測者の近くは空気が薄く、赤化が強いのは地面の近くだけ)
    //    Mie：エアロゾルは地表付近に多いので、観測者の高さから光源方向の光学的厚さ(Chapman 関数)での透過率
    //    光源が地平線の下のときは、惑星の影より上にある空気だけが照らされる(Mie は影の境目の高さでの透過率 × 影より上の空気の割合)
    //・自己遮蔽：光源の天頂角・影・透過率は、視線上で最も密度の高い点(代表点)で判定する
    //  上向きの視線は観測者、下向きは惑星に最も近づく点(地面に当たるなら地面の点)。高高度・宇宙から見た惑星の夜側は、代表点が影に入り暗くなる
    //  地面に当たる視線の空気の量は地面までの分。LUT の天頂角は地平線より下を地平線の値にする
    //・吸収層：LUT は Rayleigh + Mie(単一の H)だけの色で、惑星固有の色付けは吸収層で行う(最大 MAX_ABSORB_LAYER 層)
    //  各層は高さが一定の範囲にある球殻。τ は鉛直方向の光学的厚さ(RGB)で、殻を通る長さ / 殻の厚さ を掛けて経路の光学的厚さにする
    //  縁：1つの層を、上下の境界を縁の幅だけ少しずつ内側にずらした ABSORB_SUBSHELL 枚の入れ子の殻に分ける(密度は縁が台形に立ち上がる)
    //  視線側：視線上の各点で散乱光が空気の量に比例して生まれるとみなし、層の手前(吸収なし)・層の中((1 - e^(-τ)) / τ)・
    //          層の奥(e^(-τ))の区間ごとに、空気の量で重み付けして透過率を平均する。複数の層は掛け合わせる
    //  太陽側：代表点から光源方向に通る全層の τ の合計(地平線の下では、地表で水平な視線の片側の長さ × 2 で近似)
    //  _AbsorbLayerCount = 0 なら吸収層なしと同じ出力
    Properties
    {
        _AtmLUT ("AtmosphereLUT", 2D) = "black" {} //AtmosphereLUTBaker で作る
        _ZenithOpticalDepth ("ZenithOpticalDepth(RGB)", Vector) = (0.06, 0.12, 0.25, 0) //海抜0から天頂方向の光学的厚さ。焼き込みと揃える
        _ScaleHeight ("ScaleHeight(km)", Float) = 8 //焼き込みと揃える
        _PlanetRadius ("PlanetRadius(km)", Float) = 6371 //焼き込みと揃える
        _MieRatio ("MieRatio", Range(0, 1)) = 0.1 //位相関数に占める Mie の割合。焼き込みと揃える
        _MieG ("MieG", Range(0, 0.99)) = 0.76 //Mie の非対称係数。焼き込みと揃える
        _Density ("Density", Float) = 1 //密度の倍率(StarSystemManager の atmOpacity)
        _SeaLevelY ("SeaLevelY(Unity)", Float) = 0 //海抜0のワールドのy
        _AltitudeScale ("AltitudeScale(m/Unity)", Float) = 1 //ワールドの1を何mとするか
        _Exposure ("Exposure", Float) = 1
        _Brightness ("Brightness", Float) = 1 //全体の明るさ(StarSystemManagerが設定)

        //複数光源(StarSystemManager.MAX_LIGHT_SOURCE と数を揃える)
        //Vector：光源への向き(ワールド)、Color：大気による赤化の前の色、Intensity：大気上端での放射照度の構成比
        //色の名前は _LightColor0 にしないこと(Unity 組み込みの「メインのディレクショナルライトの色」と同じ名前で、
        //描画時に Light コンポーネントの色(大気で減衰した色 × 強さ)に上書きされ、赤化が二重にかかる)
        _LightCount ("Light Count", Float) = 1
        _LightVector0 ("Light Vector 0", Vector) = (0,1,0,0)
        _LightVector1 ("Light Vector 1", Vector) = (0,1,0,0)
        _LightVector2 ("Light Vector 2", Vector) = (0,1,0,0)
        _LightVector3 ("Light Vector 3", Vector) = (0,1,0,0)
        _SkyLightColor0 ("Sky Light Color 0", Color) = (1,1,1,1)
        _SkyLightColor1 ("Sky Light Color 1", Color) = (1,1,1,1)
        _SkyLightColor2 ("Sky Light Color 2", Color) = (1,1,1,1)
        _SkyLightColor3 ("Sky Light Color 3", Color) = (1,1,1,1)
        _LightIntensity0 ("Light Intensity 0", Float) = 1
        _LightIntensity1 ("Light Intensity 1", Float) = 0
        _LightIntensity2 ("Light Intensity 2", Float) = 0
        _LightIntensity3 ("Light Intensity 3", Float) = 0

        //吸収層(StarSystemManager.MAX_ABSORB_LAYER と数を揃える。StarSystemManager が設定)
        //Shell：x 下端の高さ(km、海抜0基準)、y 上端の高さ(km)、z 下側の縁の幅(km)、w 上側の縁の幅(km)。Tau：rgb 鉛直方向の光学的厚さ
        _AbsorbLayerCount ("Absorb Layer Count", Float) = 0
        _AbsorbShell0 ("Absorb Shell 0", Vector) = (0,1,0,0)
        _AbsorbShell1 ("Absorb Shell 1", Vector) = (0,1,0,0)
        _AbsorbShell2 ("Absorb Shell 2", Vector) = (0,1,0,0)
        _AbsorbShell3 ("Absorb Shell 3", Vector) = (0,1,0,0)
        _AbsorbTau0 ("Absorb Tau 0", Vector) = (0,0,0,0)
        _AbsorbTau1 ("Absorb Tau 1", Vector) = (0,0,0,0)
        _AbsorbTau2 ("Absorb Tau 2", Vector) = (0,0,0,0)
        _AbsorbTau3 ("Absorb Tau 3", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #define MAX_LIGHT_SOURCE 4
            #define MAX_ABSORB_LAYER 4
            #define MAX_EXPONENT 80.0 //expがあふれないよう指数を抑える
            #define MIN_SHELL_THICKNESS 1e-3 //吸収層の最小の厚さ(km)。厚さ0での割り算を避ける
            #define ABSORB_SUBSHELL 4 //1つの吸収層を分ける入れ子の殻の数(縁を柔らかくする)。重ければ2に下げる
            #define FAR_DISTANCE 1e9 //視線の長さの上限なし

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD0; //カメラから見た向き(ワールド)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _AtmLUT;
            float4 _ZenithOpticalDepth;
            float _ScaleHeight;
            float _PlanetRadius;
            float _MieRatio;
            float _MieG;
            float _Density;
            float _SeaLevelY;
            float _AltitudeScale;
            float _Exposure;
            float _Brightness;

            float _LightCount;
            float4 _LightVector0, _LightVector1, _LightVector2, _LightVector3;
            float4 _SkyLightColor0, _SkyLightColor1, _SkyLightColor2, _SkyLightColor3;
            float _LightIntensity0, _LightIntensity1, _LightIntensity2, _LightIntensity3;

            float _AbsorbLayerCount;
            float4 _AbsorbShell0, _AbsorbShell1, _AbsorbShell2, _AbsorbShell3;
            float4 _AbsorbTau0, _AbsorbTau1, _AbsorbTau2, _AbsorbTau3;

            //j番目の吸収層の外枠の半径 r1・r2、下側・上側の縁の幅 soft1・soft2(すべてH単位)と、鉛直方向の光学的厚さを取得する。使わない層ならfalse
            //縁の幅は層の厚さの半分までにクランプする
            bool GetAbsorbLayer(int j, float X, float H, out float r1, out float r2, out float soft1, out float soft2, out float3 tau)
            {
                float4 shell = _AbsorbShell0;
                tau = _AbsorbTau0.rgb;
                if (j == 1) { shell = _AbsorbShell1; tau = _AbsorbTau1.rgb; }
                else if (j == 2) { shell = _AbsorbShell2; tau = _AbsorbTau2.rgb; }
                else if (j == 3) { shell = _AbsorbShell3; tau = _AbsorbTau3.rgb; }

                float bottom = max(shell.x, 0.0);
                float top = max(shell.y, bottom + MIN_SHELL_THICKNESS); //厚さ0を避ける
                float halfThick = 0.5 * (top - bottom);
                r1 = X + bottom / H;
                r2 = X + top / H;
                soft1 = clamp(shell.z, 0.0, halfThick) / H;
                soft2 = clamp(shell.w, 0.0, halfThick) / H;
                tau = max(tau, 0.0);
                return j < _AbsorbLayerCount;
            }

            //半径 R の球の内側を、レイ(中心からの距離 r0、鉛直方向との内積 mu)が t∈[tMin, tMax] の間に通る長さ(H単位)
            //r0² - R² は桁落ちしないよう (r0 - R)(r0 + R) で計算する
            float SpherePathRange(float r0, float mu, float R, float tMin, float tMax)
            {
                float b = r0 * mu;
                float D = b * b - (r0 - R) * (r0 + R);
                if (D <= 0) { return 0.0; }
                float s = sqrt(D);
                return max(0.0, min(-b + s, tMax) - max(-b - s, tMin));
            }

            //殻 [r1, r2] の内側を t∈[tMin, tMax] の間に通る長さ(殻を2回通る場合も合計される)
            float ShellPathRange(float r0, float mu, float r1, float r2, float tMin, float tMax)
            {
                return SpherePathRange(r0, mu, r2, tMin, tMax) - SpherePathRange(r0, mu, r1, tMin, tMax);
            }

            //吸収層の、区間 t∈[tMin, tMax] での光学的厚さ(入れ子の殻の合計。_Density は掛けない)
            //k番目の殻：下端 = r1 + soft1 × (k + 0.5) / K、上端 = r2 - soft2 × (k + 0.5) / K
            //各殻の密度を揃える(τ を殻の厚さに比例して配分する：frac_k = 厚さ_k / Σ厚さ)と、
            //殻 k の寄与 τ × frac_k × 通る長さ_k / 厚さ_k は τ × 通る長さ_k / Σ厚さ になる。Σ厚さ = K × (厚さ - (soft1 + soft2) / 2)
            //重ねた密度の分布は縁が K 段で立ち上がる台形になり、鉛直方向の合計は τ と一致する(縁の幅が0なら従来の一様な殻と同じ)
            float3 LayerTau(float r1, float r2, float soft1, float soft2, float3 tau, float r0, float mu, float tMin, float tMax)
            {
                float path = 0.0;
                [unroll]
                for (int k = 0; k < ABSORB_SUBSHELL; k++)
                {
                    float f = (k + 0.5) / ABSORB_SUBSHELL;
                    path += ShellPathRange(r0, mu, r1 + soft1 * f, r2 - soft2 * f, tMin, tMax);
                }
                float sumThick = ABSORB_SUBSHELL * ((r2 - r1) - 0.5 * (soft1 + soft2));
                return tau * path / max(sumThick, 1e-6);
            }

            //k番目の光源の向き・色・強さを取得する。使わない光源ならfalse
            bool GetLightSource(int k, out float3 lightDir, out float3 lightCol, out float intensity)
            {
                float4 v = _LightVector0;
                lightCol = _SkyLightColor0.rgb;
                intensity = _LightIntensity0;
                if (k == 1) { v = _LightVector1; lightCol = _SkyLightColor1.rgb; intensity = _LightIntensity1; }
                else if (k == 2) { v = _LightVector2; lightCol = _SkyLightColor2.rgb; intensity = _LightIntensity2; }
                else if (k == 3) { v = _LightVector3; lightCol = _SkyLightColor3.rgb; intensity = _LightIntensity3; }

                lightDir = float3(0, 1, 0);
                if (k >= _LightCount || intensity <= 0 || dot(v.xyz, v.xyz) <= 0) { return false; }

                lightDir = normalize(v.xyz);
                return true;
            }

            //高さ h から天頂角 χ の向きに大気の外までの空気の柱の量(海抜0の密度 × H を1とした値。Schüler 2012 の近似)
            //X：惑星半径 / H、h：高さ / H。地平線より下(cosChi < 0)は惑星を通り抜ける経路(影は別に扱う)
            float ChapmanColumn(float X, float h, float cosChi)
            {
                float c = sqrt(X + h);
                if (cosChi >= 0) { return c / (c * cosChi + 1.0) * exp(-h); }

                float x0 = sqrt(saturate(1.0 - cosChi * cosChi)) * (X + h);
                float c0 = sqrt(x0);
                return 2.0 * c0 * exp(min(X - x0, MAX_EXPONENT)) - c / (1.0 - c * cosChi) * exp(-h);
            }

            //惑星の影の境目の高さ(/H)。光源への向きの接線が地表に接する高さ X / sinχ - X。地平線より上なら0
            float ShadowHeight(float X, float cosChi)
            {
                if (cosChi >= 0) { return 0.0; }
                float sinChi = sqrt(saturate(1.0 - cosChi * cosChi));
                return X / max(sinChi, 1e-4) - X;
            }

            //位相関数(等方散乱を1とした値)
            float PhaseRayleigh(float cosTheta)
            {
                return 0.75 * (1.0 + cosTheta * cosTheta);
            }

            float PhaseMie(float cosTheta) //Henyey-Greenstein
            {
                float g = _MieG;
                return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4), 1.5);
            }

            //視線の光学的厚さ x ∈ [x0, x1] の区間で、太陽側の光学的厚さが s0 → s1 と線形に変わるときの単一散乱 ∫ e^(-x - s(x)) dx
            //E = x + s とおくと L・(e^(-E0) - e^(-E1)) / (E1 - E0)。E0 ≒ E1 では L・e^(-E0)
            float3 SegmentScatter(float3 x0, float3 x1, float3 s0, float3 s1)
            {
                float3 L = max(x1 - x0, 0.0);
                float3 E0 = x0 + s0;
                float3 E1 = x1 + s1;
                float3 dE = E1 - E0;
                float3 e0 = exp(-min(E0, MAX_EXPONENT));
                float3 e1 = exp(-min(E1, MAX_EXPONENT));
                float3 isLarge = step(1e-4, abs(dE));
                float3 dSafe = lerp(1.0, dE, isLarge); //0除算を避ける(使わない側の値)
                return L * (isLarge * (e0 - e1) / dSafe + (1.0 - isLarge) * e0);
            }

            //Rayleigh の太陽側の透過率。散乱は視線上の各点で起き、太陽側の光学的厚さはその点の密度に比例するとみなして視線上で積分し、
            //太陽光の減衰がないときの値(1 - e^(-a))で割って、減衰がなければ1になるようにしたもの
            //視線は代表点(最も密度の高い点。視線の光学的厚さ xp の位置)で前半と後半に分かれる
            //  前半 x ∈ [0, xp]：代表点に近づくほど濃い。密度の比 f = (x + c) / (xp + c)(c は観測者より手前側の空気)
            //  後半 x ∈ [xp, a]：代表点から離れるほど薄い。f = (a - x) / (a - xp)
            //  上向きの視線は xp = 0(後半だけ)、地面に当たる視線は xp = a(前半だけ)、地平線より下で地面に当たらない視線は近地点で分かれる
            //各点の太陽側の光学的厚さ = bLit × f / us。光が届くのは f ≦ us(影の境目より上)の部分
            //a：視線の光学的厚さ、c：観測者から視線と逆向きの光学的厚さ(下向きの視線のみ)、xp：観測者から代表点までの光学的厚さ
            //bLit：影の境目の高さ(光源が地平線より上なら代表点)での光源方向の光学的厚さ、us：代表点の密度に対する影の境目の密度の比
            float3 RayleighTransmittance(float3 a, float3 c, float3 xp, float3 bLit, float us)
            {
                float usSafe = max(us, 1e-6);

                //前半
                float3 d1 = max(xp + c, 1e-6);
                float3 x1End = clamp(us * d1 - c, 0.0, xp);
                float3 scatter = SegmentScatter(0.0, x1End, bLit * (c / d1) / usSafe, bLit * ((x1End + c) / d1) / usSafe);

                //後半
                float3 d2 = max(a - xp, 1e-6);
                float3 x2Start = clamp(a - us * d2, xp, a);
                scatter += SegmentScatter(x2Start, a, bLit * ((a - x2Start) / d2) / usSafe, 0.0);

                float3 e0 = exp(-min(a, MAX_EXPONENT));
                return scatter / max(1.0 - e0, 1e-6);
            }

            //視線上の点 t での空気の量の関数 A(t)。区間 [ta, tb] の空気の量 = A(ta) - A(tb)
            //地面に当たらない視線：点 t から前向きに大気の外までの量 Cf(t)
            //地面に当たる視線：前向きは惑星を通り抜ける枝(大きな指数どうしの差)で精度が落ちるので、
            //                  逆向きの量 Cr(t)(-μ(t) > 0 で安定した枝)の符号を反転した -Cr(t) を使う
            float ViewColumnFunc(float X, float r0, float mu, float t, bool isGroundHit)
            {
                float r = sqrt(max(r0 * r0 + 2.0 * r0 * mu * t + t * t, 1e-6));
                float muT = clamp((r0 * mu + t) / r, -1.0, 1.0);
                float h = max(r - X, 0.0);
                return isGroundHit ? -ChapmanColumn(X, h, -muT) : ChapmanColumn(X, h, muT);
            }

            //(1 - e^(-τ)) / τ：層の中で一様に生まれた散乱光の平均の透過率。τ → 0 では 1 - τ/2
            float3 AvgTrans(float3 tau)
            {
                float3 t = max(tau, 1e-6);
                return lerp((1.0 - exp(-min(t, MAX_EXPONENT))) / t, 1.0 - 0.5 * tau, step(tau, 1e-4));
            }

            //吸収層1つの、視線側の平均の透過率
            //目に届く散乱光は視線上の各点で空気の量(Rayleigh と同じ単一 H の指数大気)に比例して生まれるとみなし、
            //層の手前(吸収なし)・層の中(平均の透過率)・層の奥(それまでに通った層の全量)の区間ごとに、空気の量で重み付けして平均する
            //区間の境界は層の外枠(下端・上端の球)との交差で決める。層を2回通る(リムを見る)ときは層の区間が2つになる
            //空気の量の分母は区間の量の合計を使う(比が1を超えないように)。幾何から連続的に決まるので、地平線をまたいでも段差が出ない
            float3 LayerViewTransmittance(float r1, float r2, float soft1, float soft2, float3 tau,
                float r0, float mu, float tViewMax, bool isGroundHit, float X, float density)
            {
                //外側の球(上端)との交差区間 [a1, a2]。[0, tViewMax] にクリップする
                float b = r0 * mu;
                float D2 = b * b - (r0 - r2) * (r0 + r2);
                if (D2 <= 0) { return 1.0; }
                float s2 = sqrt(D2);
                float a1 = max(-b - s2, 0.0);
                float a2 = min(-b + s2, tViewMax);
                if (a2 <= a1) { return 1.0; }

                //内側の球(下端)との交差区間 [i1, i2] を除く。交差しなければ層の区間は [a1, a2] の1つ
                //下端が地表で地面に当たる視線は、i1 が地面(≒ tViewMax)になり2つ目の区間は空になる
                float i1 = a2;
                float i2 = a2;
                float D1 = b * b - (r0 - r1) * (r0 + r1);
                if (D1 > 0)
                {
                    float s1 = sqrt(D1);
                    i1 = clamp(-b - s1, a1, a2);
                    i2 = clamp(-b + s1, a1, a2);
                }

                //区間：手前の隙間 [0, a1]・層1 [a1, i1]・隙間 [i1, i2]・層2 [i2, a2]・奥の隙間 [a2, tViewMax](空の区間は量が0)
                float A0 = ViewColumnFunc(X, r0, mu, 0.0, isGroundHit);
                float Aa1 = ViewColumnFunc(X, r0, mu, a1, isGroundHit);
                float Ai1 = ViewColumnFunc(X, r0, mu, i1, isGroundHit);
                float Ai2 = ViewColumnFunc(X, r0, mu, i2, isGroundHit);
                float Aa2 = ViewColumnFunc(X, r0, mu, a2, isGroundHit);
                float Aend = isGroundHit ? ViewColumnFunc(X, r0, mu, tViewMax, true) : 0.0; //地面に当たらない視線は大気の外で0

                float cGap0 = max(A0 - Aa1, 0.0);
                float cSeg1 = max(Aa1 - Ai1, 0.0);
                float cGap1 = max(Ai1 - Ai2, 0.0);
                float cSeg2 = max(Ai2 - Aa2, 0.0);
                float cGap2 = max(Aa2 - Aend, 0.0);

                float3 tau1 = LayerTau(r1, r2, soft1, soft2, tau, r0, mu, a1, i1) * density;
                float3 tau2 = LayerTau(r1, r2, soft1, soft2, tau, r0, mu, i2, a2) * density;
                float3 trans1 = exp(-min(tau1, MAX_EXPONENT));
                float3 trans2 = trans1 * exp(-min(tau2, MAX_EXPONENT));

                float3 num = cGap0
                    + cSeg1 * AvgTrans(tau1)
                    + cGap1 * trans1
                    + cSeg2 * trans1 * AvgTrans(tau2)
                    + cGap2 * trans2;
                float den = cGap0 + cSeg1 + cGap1 + cSeg2 + cGap2;
                return (den > 1e-8) ? num / den : 1.0;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.viewDir = -WorldSpaceViewDir(v.vertex);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 dir = normalize(i.viewDir);
                float H = max(_ScaleHeight, 1e-3);
                float X = max(_PlanetRadius, 1.0) / H;
                float c0 = sqrt(X); //海抜0での c
                float h0 = max(0.0, (_WorldSpaceCameraPos.y - _SeaLevelY) * _AltitudeScale * 0.001) / H; //観測者の高さ / H
                float3 tauZ = max(_ZenithOpticalDepth.rgb, 1e-6);
                float density = max(_Density, 0.0);

                //視線上で最も密度の高い点(代表点)。太陽の向き・影・透過率はこの点で判定する(惑星の夜側の空気が照らされないように)
                //上向きの視線は観測者、下向きは惑星に最も近づく点、地面に当たるなら地面の点
                //座標は惑星の中心を原点、観測者を (0, X + h0, 0) とした H 単位
                float rObs = X + h0;
                float tRep = 0.0;
                bool isGroundHit = false;
                if (dir.y < 0)
                {
                    float tClosest = -rObs * dir.y;
                    float rClosest = rObs * sqrt(saturate(1.0 - dir.y * dir.y));
                    isGroundHit = rClosest < X;
                    tRep = isGroundHit ? tClosest - sqrt(max(X * X - rClosest * rClosest, 0.0)) : tClosest;
                }
                float3 repPos = float3(0, rObs, 0) + dir * tRep;
                float rRep = max(length(repPos), 1e-3);
                float3 repUp = repPos / rRep;
                float hRep = max(rRep - X, 0.0);

                //視線の空気の量(海抜0の鉛直方向を1とした値)。地面に当たる視線は地面までの分
                //(地面から観測者の向きの柱の量 - 観測者から同じ向きの柱の量)
                float viewColumn = isGroundHit
                    ? max(ChapmanColumn(X, 0.0, dot(-dir, repUp)) - ChapmanColumn(X, h0, -dir.y), 0.0)
                    : ChapmanColumn(X, h0, dir.y);

                //観測者の高さの鉛直方向を1とした比 m から、海抜0で同じ比になる天頂角を求める
                float m = viewColumn * exp(min(h0, MAX_EXPONENT));
                float cosZ0 = saturate(1.0 / max(m, 1e-4) - 1.0 / c0);
                float zenith01 = acos(cosZ0) / UNITY_HALF_PI;
                float3 sky = tex2Dlod(_AtmLUT, float4(zenith01, 0.5, 0, 0)).rgb;

                //密度の補正(実際の視線の光学的厚さと、海抜0で同じ角度の光学的厚さの比。光学的に厚いと飽和する)
                float seaColumn = ChapmanColumn(X, 0.0, cosZ0);
                float3 opticalNow = tauZ * density * viewColumn;
                float3 opticalSea = tauZ * seaColumn;
                sky *= (1.0 - exp(-opticalNow)) / max(1.0 - exp(-opticalSea), 1e-6);

                //Rayleigh の積分用：観測者から代表点までの光学的厚さと、下向きの視線で観測者より手前側(視線と逆向き)の光学的厚さ
                //上向き：代表点は観測者(0)。地面に当たる：代表点は地面(視線全体)
                //地平線より下で地面に当たらない：近地点まで = 近地点の水平方向の片側の量 - 観測者から逆向きの量
                float opticalToRepCol = 0.0;
                float opticalBehindCol = 0.0;
                if (dir.y < 0)
                {
                    opticalBehindCol = ChapmanColumn(X, h0, -dir.y);
                    opticalToRepCol = isGroundHit ? viewColumn : max(ChapmanColumn(X, hRep, 0.0) - opticalBehindCol, 0.0);
                }
                float3 opticalToRep = min(tauZ * density * opticalToRepCol, opticalNow);
                float3 opticalBehind = tauZ * density * opticalBehindCol;

                //視線側の吸収。視線上の空気の量で重み付けした、層ごとの平均の透過率を掛け合わせる(層どうしの前後関係は無視する近似)
                float tViewMax = isGroundHit ? tRep : FAR_DISTANCE; //地面に当たる視線は地面まで
                float3 viewAbsorb = 1.0;
                [unroll]
                for (int j = 0; j < MAX_ABSORB_LAYER; j++)
                {
                    float r1, r2, soft1, soft2;
                    float3 layerTau;
                    if (!GetAbsorbLayer(j, X, H, r1, r2, soft1, soft2, layerTau)) { continue; }

                    viewAbsorb *= LayerViewTransmittance(r1, r2, soft1, soft2, layerTau, rObs, dir.y, tViewMax, isGroundHit, X, density);
                }
                sky *= viewAbsorb;

                float3 radiance = 0;
                [unroll]
                for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
                {
                    float3 lightDir;
                    float3 lightCol;
                    float intensity;
                    if (!GetLightSource(k, lightDir, lightCol, intensity)) { continue; }

                    //代表点での光源の天頂角、影の境目の高さと、それより上にある空気の割合(光源が地平線より上なら代表点の高さ・1)
                    float cosSun = dot(repUp, lightDir);
                    float hs = max(hRep, ShadowHeight(X, cosSun));
                    float litRatio = exp(-min(hs - hRep, MAX_EXPONENT));

                    //光源方向の光学的厚さは影の境目の高さで求める(地平線より上なら代表点。地平線より下で惑星を通り抜ける値を使わない)
                    float3 sunOpticalLit = tauZ * density * ChapmanColumn(X, hs, cosSun);

                    //Rayleigh：視線上で積分した透過率(密度の低い点で生まれた散乱光ほど赤化が弱い)
                    float3 transR = RayleighTransmittance(opticalNow, opticalBehind, opticalToRep, sunOpticalLit, litRatio);

                    //Mie：エアロゾルは地表付近に多いので、代表点の高さ(地平線の下では影の境目の高さ)での透過率
                    float3 transM = exp(-min(sunOpticalLit, MAX_EXPONENT)) * litRatio;

                    float cosTheta = dot(dir, lightDir);
                    float3 scatter = (1.0 - _MieRatio) * PhaseRayleigh(cosTheta) * transR + _MieRatio * PhaseMie(cosTheta) * transM;

                    //太陽側の吸収。太陽光は大気の外から層を順に通ってくるだけなので、全層の τ の合計(透過率の積は順番によらない)
                    //光源が地平線より上：代表点から光源方向へ
                    //光源が地平線より下：影の境目の高さでの光は地表に接する接線に沿って層を通るので、地表で水平な視線の片側の長さ × 2 で近似
                    float3 sunAbsorbTau = 0.0;
                    [unroll]
                    for (int j2 = 0; j2 < MAX_ABSORB_LAYER; j2++)
                    {
                        float r1, r2, soft1, soft2;
                        float3 layerTau;
                        if (!GetAbsorbLayer(j2, X, H, r1, r2, soft1, soft2, layerTau)) { continue; }

                        float3 layerOptical = (cosSun >= 0)
                            ? LayerTau(r1, r2, soft1, soft2, layerTau, rRep, cosSun, 0.0, FAR_DISTANCE)
                            : 2.0 * LayerTau(r1, r2, soft1, soft2, layerTau, X, 0.0, 0.0, FAR_DISTANCE);
                        sunAbsorbTau += layerOptical * density;
                    }
                    scatter *= exp(-min(sunAbsorbTau, MAX_EXPONENT));

                    radiance += lightCol * intensity * scatter;
                }

                return float4(sky * radiance * _Exposure * _Brightness, 1.0);
            }
            ENDCG
        }
    }
}
