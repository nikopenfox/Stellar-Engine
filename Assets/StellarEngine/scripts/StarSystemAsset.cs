using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StellarEngine_Bodys;
using System;

[CreateAssetMenu(fileName = "NewStarSystem", menuName = "StellarEngine/StarSystem")]
public class StarSystemAsset : ScriptableObject
{
    public List<StarData> starDatas = new List<StarData>();
    public List<PlanetData> planetDatas = new List<PlanetData>();
    [Tooltip("共通重心(連星)。天体A・Bはこの重心の周りを回り、元の parentBody と軌道要素は無視される")] public List<BaryCenterData> baryCenterDatas = new List<BaryCenterData>();

    //質量の定数(kg)。名前から質量を求めるときに使う(StarSystemImporter と同じ値)
    public const double MASS_SOLAR = 1.9884E+30d;
    public const double MASS_JUPITER = 1.8981E+27d;
    public const double MASS_EARTH = 5.9722E+24d;
    public const double MASS_MOON = 7.346E+22d;


    public float testFloat;
    // Start is called before the first frame update
    void OnValidate()
    {
        foreach (var planet in planetDatas)
        {
            //planet.texMeanColor = planet.tex.pix
        }
    }

    /// <summary>
    /// 天体(恒星・惑星・共通重心)の名前から質量(kg、スケーリング前)を返す。共通重心は天体A・Bの合計。見つからなければ0。
    /// </summary>
    public double GetBodyMass(string bodyName)
    {
        return GetBodyMass(bodyName, 0);
    }

    private double GetBodyMass(string bodyName, int depth)
    {
        if (string.IsNullOrEmpty(bodyName)) { return 0; }

        foreach (var star in starDatas)
        {
            if (star.name == bodyName) { return star.isUseSolarUnit ? star.mass * MASS_SOLAR : star.mass; }
        }

        foreach (var planet in planetDatas)
        {
            if (planet.name == bodyName) { return GetPlanetMass(planet); }
        }

        //共通重心どうしの循環参照で無限に再帰しないよう、重心の数で打ち切る
        if (depth > baryCenterDatas.Count) { return 0; }
        foreach (var bary in baryCenterDatas)
        {
            if (bary.name == bodyName) { return GetBodyMass(bary.bodyA, depth + 1) + GetBodyMass(bary.bodyB, depth + 1); }
        }
        return 0;
    }

    //入力タイプに応じて惑星の質量をkgに換算する
    public static double GetPlanetMass(PlanetData planet)
    {
        switch (planet.mrInputType)
        {
            case MassRadiusType.sun: return planet.mass * MASS_SOLAR;
            case MassRadiusType.jupiter: return planet.mass * MASS_JUPITER;
            case MassRadiusType.earth: return planet.mass * MASS_EARTH;
            case MassRadiusType.moon: return planet.mass * MASS_MOON;
            default: return planet.mass;
        }
    }

    /// <summary>
    /// 共通重心(連星)。天体A・Bは互いの共通重心の周りを、同じ離心率・周期で反対側を回る。
    /// 重心からの軌道長半径は 天体A: 相対軌道長半径 × mB / (mA + mB)、天体B: 相対軌道長半径 × mA / (mA + mB)。
    /// 相対軌道長半径・天体Aの軌道長半径・天体Bの軌道長半径は、インスペクタで1つを変えると残りが連動する(BaryCenterDataDrawer)。
    /// </summary>
    [System.Serializable]
    public class BaryCenterData
    {
        public string name;
        [Tooltip("天体A (恒星・惑星・共通重心の名前)")] public string bodyA;
        [Tooltip("天体B (恒星・惑星・共通重心の名前)")] public string bodyB;
        [Tooltip("親天体の名前 (空なら星系の原点に置く。恒星・惑星・別の共通重心を指定できる)")] public string parentBody;

        [Header("連星の軌道 (天体Aと天体Bの相対軌道)")]
        [Tooltip("軌道長半径の入力タイプ")] public SMAType smaInputType;
        [Tooltip("相対軌道長半径 (天体Aと天体Bの間の軌道長半径)")] public double relativeSemiMajorAxis;
        [Tooltip("天体Aの軌道長半径 (共通重心から)")] public double semiMajorAxisA;
        [Tooltip("天体Bの軌道長半径 (共通重心から)")] public double semiMajorAxisB;
        [Tooltip("離心率")] public double eccentricity;
        [Tooltip("近点引数"), Range(0, 360)] public double argOfPeri;

        [Header("親天体の周りの軌道 (親天体を指定したときだけ使う)")]
        [Tooltip("軌道長半径の入力タイプ")] public SMAType parentSmaInputType;
        [Tooltip("軌道長半径")] public double parentSemiMajorAxis;
        [Tooltip("離心率")] public double parentEccentricity;
        [Tooltip("近点引数"), Range(0, 360)] public double parentArgOfPeri;
        [Tooltip("公転周期の入力タイプ")] public PeriodType parentPeriodInputType = PeriodType.day;
        [Tooltip("公転周期 (0なら軌道長半径と質量から計算)")] public double parentOrbitalPeriod;
    }

    [System.Serializable]
    public class StarData
    {
        public string name, nameJP;
        [Header("パラメータ")]
        [Tooltip("スペクトル型")]public string specClass;
        [Tooltip("質量")] public double mass; //kg
        [Tooltip("半径")] public double rad; //km
        [Tooltip("有効温度(K)")] public double teff; //温度、光度
        [Tooltip("光度(太陽比)")] public double lum; //温度、光度
        [Header("インポート設定")]
        [Tooltip("質量と半径の太陽比を使うか (falseでkg、km)")] public bool isUseSolarUnit;
        [Tooltip("回折スパイクを使うか (falseなら回折スパイク・PSFのオブジェクトを作らない)")] public bool isUseSpike = true;
        [Tooltip("回折スパイクに点広がり関数(PSF)のテクスチャを使うか (falseならテクスチャを使わないマテリアルを割り当てる)")] public bool isUsePSF = true;
    }

    [System.Serializable]
    public class PlanetData
    {
        public string name, nameJP;
        [Header("設定")]
        [Tooltip("テクスチャ")] public Texture2D tex;
        [Tooltip("テクスチャ平均色")] public Color texMeanColor = Color.white;
        [Tooltip("軌道長半径の入力タイプ")] public SMAType smaInputType;
        [Tooltip("質量と半径の入力タイプ")] public MassRadiusType mrInputType = MassRadiusType.earth;
        [Tooltip("光源にするか")] public bool isUseLight = true;
        [Tooltip("回折スパイクを使うか (falseなら回折スパイク・PSFのオブジェクトを作らない)")] public bool isUseSpike;
        [Tooltip("回折スパイクに点広がり関数(PSF)のテクスチャを使うか (falseならテクスチャを使わないマテリアルを割り当てる)")] public bool isUsePSF;

        [Header("軌道特性")]
        [Tooltip("主星の名前")] public string parentBody;
        [Tooltip("主星以外で光源として使う恒星")] public string[] lightReferenceBodys;
        [Tooltip("遮蔽判定(食)を行うか")] public bool isUseOcclusion = true;
        [Tooltip("遮蔽判定する天体 (未設定なら、衛星は主星、惑星は半径が最大の衛星)")] public string[] occluderBodys;
        [Tooltip("軌道長半径")] public double semiMajorAxis; //km
        [Tooltip("離心率")] public double eccentricity; //離心率
        [Tooltip("軌道傾斜角")] public double inclination; //軌道傾斜角
        [Tooltip("近点引数"), Range(0, 360)] public double argOfPeri; //近点引数
        [Tooltip("公転周期の入力タイプ")] public PeriodType periodInputType = PeriodType.day;
        [Tooltip("公転周期 (0なら軌道長半径と質量から計算)")] public double orbitalPeriod;



        [Header("物理特性")]
        [Tooltip("質量")] public double mass; //kg
        [Tooltip("半径")] public double rad; //km
        [Tooltip("アルベド(可視光)"), Range(0, 1)] public double albedoGeom;
        [Tooltip("アルベド(全波長)"), Range(0, 1)] public double albedoBond;
        [Tooltip("自転周期の入力タイプ")] public PeriodType rotationPeriodInputType = PeriodType.hour;
        [Tooltip("自転周期")] public double rotationPeriod;
        [Tooltip("潮汐固定 (自転周期を公転周期に合わせ、主星に同じ面を向ける。自転周期の入力は無視)")] public bool isTidalLock;
        [Tooltip("赤道傾斜角"), Range(-180, 180)] public double obliquity = 0;


        [Header("表面の特性")]
        [Tooltip("塵の量"), Range(0,1)] public double hapke;

        [Header("大気")]
        [Tooltip("プリセット")] public AtmPresets atmPreset;
        [Tooltip("大気圧")] public double atmPressure;
        [Tooltip("大気の高さ(km)")] public double atmHeight;
        [Tooltip("大気の明るさ")] public double atmBrightness = 1;
        [Tooltip("高度な設定"), InspectorName("高度な設定")] public AtmAdvParameters atmAdv;



    }

    //軌道長半径の入力モード
    public enum SMAType
    {
        [InspectorName("天文単位:AU")] au,
        [InspectorName("キロメートル:km")] km
    }

    //公転周期の入力モード
    public enum PeriodType
    {
        [InspectorName("時")] hour,
        [InspectorName("日")] day,
        [InspectorName("年(365.25日)")] year
    }

    public enum MassRadiusType
    {
        [InspectorName("kg,km")] dontUse,
        [InspectorName("太陽比:☉")] sun,
        [InspectorName("木星比:♃")] jupiter,
        [InspectorName("地球比:🜨")] earth,
        [InspectorName("月比:☾")] moon,
    }

    public enum AtmPresets
    {
        [InspectorName("地球")] earth,
        [InspectorName("火星")] mars,
        [InspectorName("金星")] venus,
        [InspectorName("木星")] jupiter,
        [InspectorName("タイタン")] titan,
        [InspectorName("冥王星")] pluto,
        [InspectorName("カスタム")] custom,
    }

    [System.Serializable]
    public class AtmAdvParameters
    {
        public double forward;
        public double opacity;
        public double reyleigh;
        public double reyleighR;
        public double reyleighG;
        public double reyleighB;
    }
}
