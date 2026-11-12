using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// StEAtmosphere 用のLUT(海抜0での視線経路の色の鉛直方向の分布)を焼き込む。
/// LUT には吸収層の色を含めない(Rayleigh + Mie、単一 H だけの色)。惑星固有の色付けはシェーダーの吸収層で行う
/// ・LUT：幅 lutWidth × 高さ 1、RGBAFloat。画素中心が天頂角 (x + 0.5) / 幅 × 90°
/// ・物理パラメータから焼くモード：吸収のない単一散乱の視線経路の色 = 明るさ × (1 - e^(-τ × 視線の空気の量))
///   (位相関数と太陽側の透過率はシェーダーが掛けるので含めない)
/// ・グラデーションから焼くモード(後方互換)：吸収層による色付けを含まないグラデーション(薄青〜白っぽい地平線)を使う
///   中央が地平線、上端が天頂。上半分(太陽側の鉛直面)だけを使い、横方向は平均する
///   シェーダーは LUT に「位相関数 × 太陽側の透過率」(Rayleigh と Mie を mieRatio で混ぜたもの)を掛けるので、焼き込みでは
///   グラデーションを写したときの太陽高度での値(散乱角 = |視線の高度角 - 太陽高度|、観測者は海抜0)で割って取り除く
///   透過率は Rayleigh が視線上で積分したもの、Mie が観測者の高さでのもの(シェーダーと同じ式)
/// </summary>
public class AtmosphereLUTBaker : EditorWindow
{
    private const float MAX_EXPONENT = 80f;
    private const float MIN_DIVISOR = 1e-6f;

    [SerializeField] private bool isPhysicalMode = true;
    [SerializeField] private float physicalBrightness = 1f;
    [SerializeField] private Texture2D sourceTexture;
    [SerializeField] private float sunElevation = 45f; //度
    [SerializeField] private bool isRemovePhase = true;
    [SerializeField] private bool isRemoveSunTransmittance = true;
    [SerializeField] private Vector3 zenithOpticalDepth = new Vector3(0.06f, 0.12f, 0.25f); //RGB
    [SerializeField] private float scaleHeight = 8f; //km
    [SerializeField] private float planetRadius = 6371f; //km
    [SerializeField] private float mieRatio = 0.1f;
    [SerializeField] private float mieG = 0.76f;
    [SerializeField] private int lutWidth = 128;
    [SerializeField] private string outputPath = "Assets/StellarEngine/sky/AtmosphereLUT.asset";
    [SerializeField] private Material targetMaterial;

    [MenuItem("StellarEngine/Atmosphere LUT Baker")]
    private static void Open()
    {
        GetWindow<AtmosphereLUTBaker>("Atmosphere LUT Baker");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("焼き込み方", EditorStyles.boldLabel);
        isPhysicalMode = EditorGUILayout.Toggle(new GUIContent("物理パラメータから焼く", "ON: 天頂の光学的厚さなどから計算する / OFF: 空のグラデーションから焼く(後方互換)"), isPhysicalMode);

        if (isPhysicalMode)
        {
            physicalBrightness = EditorGUILayout.FloatField(new GUIContent("明るさ", "視線経路の色 = 明るさ × (1 - e^(-τ × 視線の空気の量))。地平線(光学的に厚い方向)の色がほぼこの値になる"), physicalBrightness);
        }
        else
        {
            sourceTexture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("空のグラデーション", "中央が地平線、上端が天頂。上半分を使う。吸収層による色付けを含まないもの(薄青〜白っぽい地平線)"), sourceTexture, typeof(Texture2D), false);
            sunElevation = EditorGUILayout.Slider(new GUIContent("太陽高度(度)", "グラデーションの空を写したときの太陽の高度。上半分を太陽側の鉛直面とみなす"), sunElevation, 0f, 90f);
            isRemovePhase = EditorGUILayout.Toggle(new GUIContent("位相関数を取り除く", "OFF にすると、グラデーションを太陽の向きによらない空の色とみなす(太陽の周りの明るさがないグラデーションで、太陽高度付近が暗くなる場合に)"), isRemovePhase);
            isRemoveSunTransmittance = EditorGUILayout.Toggle(new GUIContent("太陽側の透過率を取り除く", "グラデーションの色に含まれる、太陽光の大気による減衰を取り除く"), isRemoveSunTransmittance);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("大気(マテリアルと揃える)", EditorStyles.boldLabel);
        zenithOpticalDepth = EditorGUILayout.Vector3Field(new GUIContent("天頂の光学的厚さ(RGB)", "海抜0から天頂方向の光学的厚さ"), zenithOpticalDepth);
        scaleHeight = EditorGUILayout.FloatField(new GUIContent("スケールハイト(km)"), scaleHeight);
        planetRadius = EditorGUILayout.FloatField(new GUIContent("惑星半径(km)"), planetRadius);
        mieRatio = EditorGUILayout.Slider(new GUIContent("Mie の割合", "位相関数に占める Mie の割合"), mieRatio, 0f, 1f);
        mieG = EditorGUILayout.Slider(new GUIContent("Mie の非対称係数 g"), mieG, 0f, 0.99f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
        lutWidth = EditorGUILayout.IntSlider(new GUIContent("LUTの幅", "天頂角の分割数"), lutWidth, 16, 1024);
        outputPath = EditorGUILayout.TextField(new GUIContent("保存先", "Assets/ から始まる .asset のパス"), outputPath);
        targetMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("設定するマテリアル", "指定すると、LUT と大気の値を設定する(StEAtmosphere のマテリアル)"), targetMaterial, typeof(Material), false);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!isPhysicalMode && sourceTexture == null))
        {
            if (GUILayout.Button("焼き込む")) { Bake(); }
        }
    }

    private void Bake()
    {
        if (scaleHeight <= 0 || planetRadius <= 0)
        {
            Debug.LogError("StE_AtmosphereLUTBaker: スケールハイト・惑星半径は正の値にしてください");
            return;
        }
        if (!outputPath.StartsWith("Assets/") || !outputPath.EndsWith(".asset"))
        {
            Debug.LogError("StE_AtmosphereLUTBaker: 保存先は Assets/ から始まる .asset のパスにしてください");
            return;
        }

        float X = planetRadius / scaleHeight;
        float[] tau = { zenithOpticalDepth.x, zenithOpticalDepth.y, zenithOpticalDepth.z };
        Color[] lut = new Color[lutWidth];

        if (isPhysicalMode)
        {
            //吸収のない単一散乱の視線経路の色 = 明るさ × (1 - e^(-τ × 視線の空気の量))
            for (int x = 0; x < lutWidth; x++)
            {
                float zenith = (x + 0.5f) / lutWidth * 90f;
                float viewColumn = ChapmanColumn(X, 0f, Mathf.Cos(zenith * Mathf.Deg2Rad));
                Color result = Color.white;
                for (int c = 0; c < 3; c++)
                {
                    result[c] = physicalBrightness * (1f - Mathf.Exp(-Mathf.Min(Mathf.Max(tau[c], 0f) * viewColumn, MAX_EXPONENT)));
                }
                lut[x] = result;
            }

            SaveLUT(lut);
            Debug.Log(string.Format("StE_AtmosphereLUTBaker: 物理パラメータから焼き込みました {0}\n天頂 {1} / 地平線 {2}", outputPath, lut[0], lut[lutWidth - 1]));
            return;
        }

        Color[] rows = ReadRowAverages(sourceTexture);
        float sunColumn = ChapmanColumn(X, 0f, Mathf.Sin(sunElevation * Mathf.Deg2Rad));

        for (int x = 0; x < lutWidth; x++)
        {
            float zenith = (x + 0.5f) / lutWidth * 90f;
            float elevation = 90f - zenith;
            Color col = SampleUpperHalf(rows, elevation);
            float viewColumn = ChapmanColumn(X, 0f, Mathf.Sin(elevation * Mathf.Deg2Rad));

            //位相関数(取り除かないなら1)
            float phaseR = 1f, phaseM = 1f;
            if (isRemovePhase)
            {
                float cosTheta = Mathf.Cos(Mathf.Abs(elevation - sunElevation) * Mathf.Deg2Rad);
                phaseR = PhaseRayleigh(cosTheta);
                phaseM = PhaseMie(cosTheta);
            }

            //シェーダーと同じく、Rayleigh は視線上で積分した透過率、Mie は観測者の高さ(海抜0)での透過率で割る
            Color result = Color.white;
            for (int c = 0; c < 3; c++)
            {
                float transR = 1f, transM = 1f;
                if (isRemoveSunTransmittance)
                {
                    float a = tau[c] * viewColumn;
                    float b = tau[c] * sunColumn;
                    transR = RayleighTransmittance(a, b, 1f);
                    transM = Mathf.Exp(-Mathf.Min(b, MAX_EXPONENT));
                }
                float divisor = (1f - mieRatio) * phaseR * transR + mieRatio * phaseM * transM;
                result[c] = col[c] / Mathf.Max(divisor, MIN_DIVISOR);
            }
            lut[x] = result;
        }

        SaveLUT(lut);
        Debug.Log(string.Format("StE_AtmosphereLUTBaker: 焼き込みました {0}\n天頂 {1} / 地平線 {2}", outputPath, lut[0], lut[lutWidth - 1]));
    }

    //上半分(中央が地平線、上端が天頂)から高度角(度)の色を線形補間で読む
    private static Color SampleUpperHalf(Color[] rows, float elevation)
    {
        int h = rows.Length;
        float v = 0.5f + Mathf.Clamp(elevation, 0f, 90f) / 180f; //0が下端
        float fy = Mathf.Clamp(v * h - 0.5f, 0f, h - 1);
        int y0 = Mathf.FloorToInt(fy);
        int y1 = Mathf.Min(y0 + 1, h - 1);
        return Color.Lerp(rows[y0], rows[y1], fy - y0);
    }

    //グラデーションの各行の平均色(線形)。読み取り不可のテクスチャでも読めるよう、RenderTexture を経由する
    private static Color[] ReadRowAverages(Texture2D src)
    {
        int w = src.width, h = src.height;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tmp = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tmp.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tmp.GetPixels();
        DestroyImmediate(tmp);

        bool isGamma = PlayerSettings.colorSpace == ColorSpace.Gamma && !IsLinearTexture(src);
        Color[] rows = new Color[h];
        for (int y = 0; y < h; y++)
        {
            Color sum = Color.clear;
            for (int x = 0; x < w; x++) { sum += pixels[y * w + x]; }
            Color avg = sum / w;
            rows[y] = isGamma ? avg.linear : avg; //ガンマ空間のプロジェクトでは Blit で線形にならないので変換する
        }
        return rows;
    }

    //インポート設定で sRGB をオフにしたテクスチャか
    private static bool IsLinearTexture(Texture2D src)
    {
        string path = AssetDatabase.GetAssetPath(src);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        return importer != null && !importer.sRGBTexture;
    }

    private void SaveLUT(Color[] lut)
    {
        Texture2D tex = new Texture2D(lutWidth, 1, TextureFormat.RGBAFloat, false, true);
        tex.name = Path.GetFileNameWithoutExtension(outputPath);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels(lut);
        tex.Apply(false, false);

        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        Texture2D saved;
        if (existing != null)
        {
            EditorUtility.CopySerialized(tex, existing); //GUIDを保つため上書きする
            DestroyImmediate(tex);
            EditorUtility.SetDirty(existing);
            saved = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(tex, outputPath);
            saved = tex;
        }
        AssetDatabase.SaveAssets();

        if (targetMaterial != null)
        {
            Undo.RecordObject(targetMaterial, "Bake Atmosphere LUT");
            targetMaterial.SetTexture("_AtmLUT", saved);
            targetMaterial.SetVector("_ZenithOpticalDepth", new Vector4(zenithOpticalDepth.x, zenithOpticalDepth.y, zenithOpticalDepth.z, 0f));
            targetMaterial.SetFloat("_ScaleHeight", scaleHeight);
            targetMaterial.SetFloat("_PlanetRadius", planetRadius);
            targetMaterial.SetFloat("_MieRatio", mieRatio);
            targetMaterial.SetFloat("_MieG", mieG);
            EditorUtility.SetDirty(targetMaterial);
        }
    }

    //---- 以下は StEAtmosphere.shader と同じ式 ----

    //高さ h から天頂角 χ の向きに大気の外までの空気の柱の量(海抜0の密度 × H を1とした値。Schüler 2012 の近似)
    private static float ChapmanColumn(float X, float h, float cosChi)
    {
        float c = Mathf.Sqrt(X + h);
        if (cosChi >= 0) { return c / (c * cosChi + 1f) * Mathf.Exp(-h); }

        float x0 = Mathf.Sqrt(Mathf.Clamp01(1f - cosChi * cosChi)) * (X + h);
        float c0 = Mathf.Sqrt(x0);
        return 2f * c0 * Mathf.Exp(Mathf.Min(X - x0, MAX_EXPONENT)) - c / (1f - c * cosChi) * Mathf.Exp(-h);
    }

    //位相関数(等方散乱を1とした値)
    private static float PhaseRayleigh(float cosTheta)
    {
        return 0.75f * (1f + cosTheta * cosTheta);
    }

    private float PhaseMie(float cosTheta) //Henyey-Greenstein
    {
        float g = mieG;
        return (1f - g * g) / Mathf.Pow(Mathf.Max(1f + g * g - 2f * g * cosTheta, 1e-4f), 1.5f);
    }

    //Rayleigh の太陽側の透過率(視線上で積分し、太陽光の減衰がなければ1)
    private static float RayleighTransmittance(float a, float b, float us)
    {
        float d = a - b;
        float e1 = Mathf.Exp(-Mathf.Min(a * (1f - us) + b * us, MAX_EXPONENT));
        float e0 = Mathf.Exp(-Mathf.Min(a, MAX_EXPONENT));
        float scatter = Mathf.Abs(d) <= 1e-4f ? a * us * e0 : a * (e1 - e0) / d;
        return scatter / Mathf.Max(1f - e0, 1e-6f);
    }
}
