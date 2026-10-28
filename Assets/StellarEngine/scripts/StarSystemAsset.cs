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


    public float testFloat;
    // Start is called before the first frame update
    void OnValidate()
    {
        foreach (var planet in planetDatas)
        {
            //planet.texMeanColor = planet.tex.pix
        }
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
