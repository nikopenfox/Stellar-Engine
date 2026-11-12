
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Serialization.OdinSerializer;
using StellarEngine_Bodys;
using UnityEngine.UI;
using Unity.Mathematics;
using System.Collections;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class StarSystemManager : UdonSharpBehaviour
{
    private const float MIN_F = 1e-30f;　//floatの最小値を入れたいときの値
    private const int BASEQUEUE = 2510; //最近傍天体のRenderQueue。遠い天体ほど1ずつ増やす(近い天体が先に深度を書き込み、奥の天体を隠す)
    private const float BASEDEPTH = 0.00001f; //最遠天体の深度値(深度は1が最近傍、0が最遠)
    private const float BASEDEPTH_INC = 0.0000015f; //描画順が1つ近くなるごとに足す深度値
    private const float MIN_DIAMETER_DEG = 1e-9f; //CalcExposurenessで0除算を避けるための視直径(度)の下限
    private const float PHOTOCENTER_COEF = 0.4244132f; //4/(3π)。位相角αのとき、照らされた部分の重心は中心から 角半径 × この値 × (1 - cosα) だけ光源側にある
    private const float SUN_DIAMETER_ARCMIN = 31.97f; //1AUから見た太陽の視直径(分角)。表面輝度の等級の基準
    private const float ATM_DEPTH_RATIO = 0.5f; //大気パスの表側を本体より手前、裏側を奥にずらす量(BASEDEPTH_INC に対する割合。1未満にして隣の天体との間に収める)
    public const int MAX_LIGHT_SOURCE = 4; //惑星シェーダーに渡せる光源の最大数(シェーダー側のスロット数と揃える)
    private const int KEPLER_ITERATION = 20; //ケプラー方程式の反復回数の上限
    private const double TIDAL_LOCK_OFFSET = 90.0; //潮汐固定時、経度0で主星が天頂に来るためのヨーイングのずれ(度)

    public double scale, scaledAU;
    public double distUnity;
    
    [SerializeField] private Vector3 bodys_baseRotation;
    private Quaternion bodys_baseRotationQ;
    [HideInInspector] public GameObject cSphere;
    [Header("設定")]
    [SerializeField] private bool isRealTimeMode;
    [SerializeField, Range(0f, 10f), Tooltip("視直径(分角)がこれ以下の天体はメッシュを描画しない (0で常に描画)")] private float meshCullDiameter = 1f;
    [SerializeField] private float updateInterval, lightUpdateInterval; //何秒ごとに更新するか
    private float time, L_time;

    [Header("光点 (視直径の小さい天体)")]
    [SerializeField, Tooltip("視直径の小さい天体に光点を描く")] private bool isUsePoint = true;
    [SerializeField, Tooltip("限界等級: 光点はこの等級で消え、これより暗い天体は描かない")] private float pointMagnLimit = 6f;
    [SerializeField, Tooltip("光点が最大の大きさ・明るさになる等級")] private float pointMagnBright = -4f;
    [SerializeField, Tooltip("昼間の限界等級: 地上の全光量の等級にこの値を足した等級より暗い天体は描かない")] private float pointDaylightRange = 22f;
    [SerializeField, Range(0.5f, 32f), Tooltip("最も暗い光点の半径(画面ピクセル)")] private float pointSizeMin = 3f;
    [SerializeField, Range(0.5f, 64f), Tooltip("最も明るい光点の半径(画面ピクセル)")] private float pointSizeMax = 12f;
    [SerializeField, Range(0f, 10f), Tooltip("光点の明るさの倍率")] private float pointBrightness = 1f;
    [SerializeField, Range(1.1f, 10f), Tooltip("光点が消える視直径 (meshCullDiameterに対する倍率)。この間でメッシュと重ねて切り替える。光芒はここから出始める")] private float pointFadeRatio = 3f;
    [SerializeField, Tooltip("BrightPoint(光点の明るい点の装飾)が出始める等級")] private float brightPointMagn = 0f;
    [SerializeField, Range(0f, 4f), Tooltip("BrightPointの最大の強さ")] private float brightPointStrength = 1f;

    [Header("回折スパイク (表面輝度が非常に高い天体)")]
    [SerializeField, Tooltip("表面輝度が非常に高い天体に回折スパイクを描く(光点の表示とは関係なく出る)")] private bool isUseSpike = true;
    [SerializeField, Range(0.01f, 60f), Tooltip("分解能(分角)。これより小さい天体は、光がこの大きさの円盤に広がったとみなして表面輝度を求める")] private float resolvingPower = 1f;
    [SerializeField, Tooltip("回折スパイクが出始める表面輝度の等級 (太陽と同じ視直径だったときの等級。太陽は約-26.7)")] private float spikeMagnThreshold = -20f;
    [SerializeField, Range(0.1f, 20f), Tooltip("出始めから最大の明るさになるまでの等級の幅")] private float spikeFadeMagn = 3f;
    [SerializeField, Range(0f, 10f), Tooltip("回折スパイクの明るさの倍率")] private float spikeBrightness = 1f;
    [SerializeField, Range(1, 16), Tooltip("光条の本数")] private int spikeCount = 4;
    [SerializeField, Range(0f, 360f), Tooltip("光条の回転角(度)")] private float spikeAngle = 0f;
    [SerializeField, Range(0f, 200f), Tooltip("出始めの表面輝度から1等明るくなるごとに伸ばす長さ(画面ピクセル)")] private float spikeLengthPerMagn = 30f;
    [SerializeField, Range(1f, 2000f), Tooltip("光条の長さの上限(画面ピクセル)")] private float spikeLengthMax = 300f;
    [SerializeField, Range(0.1f, 16f), Tooltip("光条の太さの下限(画面ピクセル)。視直径が分解能 resolvingPower 以下のときの太さ")] private float spikeWidthMin = 1f;
    [SerializeField, Range(0f, 2f), Tooltip("光条の太さの指数: 太さ = spikeWidthMin × (視直径 / resolvingPower)^この値。0で一定、1で視直径に比例")] private float spikeWidthExponent = 0.5f;
    [SerializeField, Tooltip("点広がり関数(PSF)のテクスチャ。観測者の光学系の細かな特性を表す(RGBを使い、中心が天体の位置)。未設定ならマテリアルのものを使う")] private Texture2D observerPSF;
    [SerializeField, Tooltip("PSFが出始める等級 (点光源とみなしたときの相対輝度。視直径によらない)")] private float psfMagnThreshold = 0f;
    [SerializeField, Range(0.1f, 20f), Tooltip("PSFの出始めから最大の明るさになるまでの等級の幅")] private float psfFadeMagn = 3f;
    [SerializeField, Range(0f, 10f), Tooltip("PSFの明るさの倍率")] private float psfBrightness = 1f;
    [SerializeField, Range(0f, 200f), Tooltip("出始めの等級から1等明るくなるごとに広げるPSFの半径(画面ピクセル)")] private float psfSizePerMagn = 4f;
    [SerializeField, Range(1f, 2000f), Tooltip("PSFの半径の上限(画面ピクセル)")] private float psfSizeMax = 200f;
    [SerializeField, Tooltip("遮蔽判定の深度(_CameraDepthTexture の生の値。1が最近傍、0が最遠)。発光中心の深度がこれ以上なら地表のオブジェクトに隠されたとみなして描かない。目安は ニアクリップ距離 / 遮蔽物までの距離")] private float spikeOcclusionDepth = 1e-7f;

    [Header("光芒 (視直径の大きい天体)")]
    [SerializeField, Tooltip("視直径の大きい天体に光芒を描く(光点が消えた視直径から出始める)")] private bool isUseGlare = true;
    [SerializeField, Range(1.1f, 10f), Tooltip("光芒が最大になる視直径 (光点が消える視直径 meshCullDiameter × pointFadeRatio に対する倍率)。この間でフェードインする")] private float glareFadeRatio = 2f;
    [SerializeField, Range(0f, 10f), Tooltip("光芒の明るさの倍率")] private float glareBrightness = 1f;
    [SerializeField, Range(0f, 20f), Tooltip("最も明るいときの光芒の広がり (天体の半径に対する倍率。外縁 = 半径 × (1 + この値))")] private float glareSizeScale = 4f;
    [SerializeField, Tooltip("光芒が出始める表面輝度の等級 (太陽と同じ視直径だったときの等級)")] private float glareMagnDim = -10f;
    [SerializeField, Tooltip("光芒が最大になる表面輝度の等級 (太陽は約-26.7)")] private float glareMagnBright = -26.7f;
    [SerializeField, Range(0.5f, 30f), Tooltip("全体輝度による光芒の抑制幅(等級): 地上の全光量よりこの等級だけ暗い天体は光芒が0になる (pointDaylightRangeより小さいほど光点より強く効く)")] private float glareDaylightRange = 10f;

    [Header("処理時間の計測")]
    [SerializeField, Tooltip("FixedUpdate内の各処理の時間を計測してログに出す")] private bool debug_isProfile;
    [SerializeField, Tooltip("ログを出す間隔(秒)")] private float debug_profileLogInterval = 5f;
    private System.Diagnostics.Stopwatch debug_profileWatch;
    private double debug_profileLogStart; //集計を始めた時刻(ms)
    private double[] debug_profileSum; //区間ごとの合計時間(ms)
    private int[] debug_profileCount; //区間ごとの呼び出し回数
    private string[] debug_profileName;
    //計測区間の番号
    private const int PROF_TIME = 0;
    private const int PROF_ORBIT = 1;
    private const int PROF_PLANET = 2;
    private const int PROF_STAR = 3;
    private const int PROF_BODY_OTHER = 4;
    private const int PROF_PLANET_LIGHT = 5;
    private const int PROF_LIGHT = 6;
    private const int PROF_ATM = 7;
    private const int PROF_CLOUD = 8;
    private const int PROF_STARMAP = 9;
    private const int PROF_TOTAL = 10;
    private const int PROF_NUM = 11;

    [Header("時間")]
    private const int YEAR2SEC = 31557600;
    private const int MONTH2SEC = 2629800;
    private const int DAY2SEC = 86400;
    private const int HOUR2SEC = 3600;
    private const int MIN2SEC = 60;
    private const int YEAR2MONTH = 12;
    private const double MONTH2DAY = 30.4375;
    private const int DAY2HOUR = 24;
    private const int HOUR2MIN = 60;

    //sDateは通常1ミリ秒単位
    private double MILLISEC = 0.001f;
    [SerializeField] private long sDate;
    [SerializeField] private string sDateText = "2000/1/1 12:00";
    [SerializeField, Tooltip("時間の早送り倍率 (1で実時間、負の値で逆再生)")] private double timeFactor = 1d;
    [SerializeField] private double sDateResolution = 1d; //時間解像度
    private long sd_res; //その内部値
    private double nowDate;

    [Header("観測者の位置")]
    [SerializeField] private string obs_body_name;
    private int obs_body_index = -1;
    public Vector3 obs_position;
    [SerializeField] private float altitude = 0f;
    


    [Header("観測者の回転"),Space(10)]
    [SerializeField, Tooltip("自転軸"), Range(0, 360)] private float rotationAxis;
    [SerializeField, Tooltip("緯度"), Range(-90, 90)] private float latitude;
    [SerializeField, Tooltip("経度"), Range(-180, 180)] private float longitude;
    [SerializeField, Tooltip("ヨーイング(回転)"), Range(0, 360)] private float yawing;
    private Quaternion rotQ, view_rotQ;
    private Quaternion lat_lonQ, view_lat_lonQ;
    private Quaternion surfaceQ, view_surfaceQUnity;
    private Vector3 surfaceVec, view_surfaceVecUnity; //地表面のベクトル



    [SerializeField, Tooltip("ライトを更新する")] private bool isLightUpdate;
    [SerializeField, Tooltip("プレイモード中に観測器を更新")] private bool isExtractOnPlaymode;

    [Header("星系図")]
    [SerializeField] private bool isUseSystemMap;
    [SerializeField] private Transform systemMap_Base;
    [SerializeField] private Transform systemMap_obsMarker;
    [SerializeField] private float systemMap_scale;
    [SerializeField] private Transform surfVecObj, sunVecObj;
    private Vector3 overrideObsPos;

    [Header("同期")]
    private const float FREE_SYNC_INTERVAL = 0.5f; //フリーモード(惑星にいない)のとき、オーナーが同期する間隔(秒)
    private const float SYNC_EPSILON = 1e-3f; //これ以下の差は値の変化とみなさない(スライダーとの往復の誤差を無視する)
    private bool isSynced; //他のプレイヤー用: オーナーの値を一度でも受け取ったか
    private float sync_freeTime; //フリーモードの同期タイマー
    //オーナーが前回送った値(変化の検出用)
    private double sync_prevTimeFactor;
    private float sync_prevRotationAxis, sync_prevLatitude, sync_prevLongitude, sync_prevAtmOpacity;
    private string sync_prevObsBodyName;
    [SerializeField, UdonSynced] private long onr_sDate; //送信時のsDate
    [SerializeField, UdonSynced] private double onr_serverTime; //送信時のサーバー時刻(秒)。受信側で遅延の分の経過時間を補正する
    [SerializeField, UdonSynced] private double onr_timeFactor;
    [SerializeField, UdonSynced] private string onr_obsBodyName = "";
    [SerializeField, UdonSynced] private Vector3 onr_obsPosition; //惑星にいないときの観測者の座標
    [SerializeField, UdonSynced] private float onr_rotationAxis;
    [SerializeField, UdonSynced] private float onr_latitude;
    [SerializeField, UdonSynced] private float onr_longitude;
    [SerializeField, UdonSynced] private float onr_atmOpacity; //大気密度

    //観測器

    [Space(40),Header("恒星観測器")]
    [Tooltip("恒星名を入力")]
    //ext = extractor
    [SerializeField]private string targetStarName;

    public string ext_name_star;
    public GameObject ext_body_star;
    [Header("物理特性")]
    public string ext_specClass;
    public double ext_mass_star; //kg
    public double ext_rad_star; //km
    public double ext_teff_star, ext_lum_star; //温度、光度

    [Header("光学的要素")]
    [SerializeField] private Light ext_lightObj_star;
    [SerializeField] private double ext_obsFlux_star;
    [SerializeField] private double ext_obsFlux_star_vis;
    [SerializeField] private double ext_obsFlux_star_surface;
    [SerializeField] private double ext_appMagn_star; //実視等級
    [SerializeField] private double ext_appDiameter_star; //実視等級


    [Header("座標")]
    [SerializeField] private double ext_height_star, ext_height_star_angle;




    [Space(40),Header("惑星観測器")]

    [Tooltip("惑星名を入力")]
    [SerializeField] private string targetPlanetName;

    public string ext_name_planet;
    public GameObject ext_body_planet;

    [Header("軌道特性")]
    public int ext_parentBodyIdx;
    public double ext_semiMajorAxis_planet; //km
    public double ext_eccentricity_planet; //離心率
    public double ext_inclination_planet; //軌道傾斜角
    public double ext_argOfPeri_planet; //近点引数
    public double ext_orbitalPeriod_planet; //公転周期(日)


    [Header("座標")]
    [SerializeField] public Vector3 ext_position_planet;
    [SerializeField] public Vector3 ext_position_planetUnity;
    [SerializeField] public Vector3 ext_velocity_planet;
    [SerializeField] public double ext_distance_planet;
    [SerializeField] public double ext_meanAnomaly_planet;

    [Header("物理特性")]
    public double ext_mass_planet; //kg
    public double ext_rad_planet; //km
    public double ext_teff_planet; //温度
    public double ext_albedoBond;
    [SerializeField] public double ext_flux; //放射束(地球比)

    [Header("光学的要素")]
    public bool ext_isUseLight;
    public double ext_appDiameter_planet;
    [SerializeField] public Light ext_lightObj_planet;
    public double ext_albedoGeom;
    public double ext_flux_planet_obs;
    public double ext_appMagn_planet;








    //以下疑似的な実体　nameの配列番号を実体として扱う

    //恒星ここから
    [Header("恒星")]

    public string[] name_star;
    [HideInInspector] public GameObject[] body_star;
    [HideInInspector] public MeshRenderer[] mesh_star;
    [HideInInspector] public MeshRenderer[] point_star; //光点・光芒(本体の子)。Importerが設定
    [HideInInspector] public MeshRenderer[] spike_star; //回折スパイク(本体の子)。Importerが設定
    [HideInInspector] public bool[] isUsePSF_star; //回折スパイクにPSFテクスチャを使うか。Importerが設定
    [Header("物理特性")]
    [HideInInspector] public string[] specClass;
    [HideInInspector] public double[] mass_star; //kg
    [HideInInspector] public double[] rad_star; //km
    [HideInInspector] public double[] teff_star, lum; //温度、光度

    [Header("光学的要素")]
    [HideInInspector] public Light[] lightObj_star;
    [HideInInspector] private int[] lightIndex_star;
    [HideInInspector] public double[] obsFlux_star; //観測者の放射束(地球比)
    [HideInInspector] public double[] obsFlux_star_vis;
    [HideInInspector] public double[] obsFlux_star_surface;
    [HideInInspector] public double[] appMagn_star; //実視等級 
    [HideInInspector] public double[] appDiameter_star;

    [Header("軌道 (共通重心に入っている恒星だけが公転する)")]
    [HideInInspector] public PlanetParentTypes[] parentType_star; //親(共通重心)のタイプ
    [HideInInspector] public int[] parentIndex_star; //親の番号。-1なら星系の原点に固定
    [HideInInspector] public double[] semiMajorAxis_star; //km。負なら重心をはさんで反対側(連星の天体A)
    [HideInInspector] public double[] eccentricity_star; //離心率
    [HideInInspector] public double[] argOfPeri_star; //近点引数
    [HideInInspector] public double[] orbitalPeriod_star; //公転周期(秒) 0なら公転しない

    [Header("座標")]
    [HideInInspector] public bool[] isStatic;
    [HideInInspector] public Vector3[] position_star;
    [HideInInspector] public Vector3[] positionRelative_star;
    [HideInInspector] public float[] distance_star;
    [HideInInspector] public float[] height_star, height_star_angle;
    [HideInInspector] private int nearOrder; //どれくらい近いか

    [Header("View")]
    [HideInInspector] public Vector3[] view_position_starUnity;

    //惑星ここから
    [Header("惑星")]

    public string[] name_planet;
    [HideInInspector] public GameObject[] body_planet;
    [HideInInspector] public MeshRenderer[] mesh_planet;
    [HideInInspector] public Texture2D[] tex_planet;
    [HideInInspector] public MaterialPropertyBlock[] mesh_planet_block;
    [HideInInspector] public MeshRenderer[] point_planet; //光点・光芒(本体の子)。Importerが設定
    [HideInInspector] public MeshRenderer[] spike_planet; //回折スパイク(本体の子)。Importerが設定
    [HideInInspector] public bool[] isUsePSF_planet; //回折スパイクにPSFテクスチャを使うか。Importerが設定
    private MaterialPropertyBlock[] pointBlock_planet, pointBlock_star;
    private MaterialPropertyBlock[] spikeBlock_planet, spikeBlock_star;
    private float[] spikeIntensity_planet, spikeIntensity_star; //回折スパイク・PSFの強さの大きい方(天体どうしの遮蔽判定の前)
    private Vector3[] spikeDir_planet, spikeDir_star; //回折スパイクの発光中心の向き(天球の中心から、Unity座標)

    //描画順(RenderQueueはMaterialPropertyBlockで変えられないため、実行時に天体ごとのマテリアルを複製して持つ)
    private Material[] mat_planet, mat_star;
    private int[] renderQueue_planet, renderQueue_star;
    private int[] depthRank_planet, depthRank_star; //深度の順位(自分より遠い天体の数)
    private MaterialPropertyBlock[] renderBlock_planet, renderBlock_star; //描画順の深度を渡す

    [Header("軌道特性")]
     public double[] semiMajorAxis_planet; //km。負なら重心をはさんで反対側(連星の天体A)
    [HideInInspector] public double[] eccentricity_planet; //離心率
    [HideInInspector] public double[] inclination_planet; //軌道傾斜角
    [HideInInspector] public double[] argOfPeri_planet; //近点引数
    [HideInInspector] public double[] orbitalPeriod_planet; //公転周期(秒) 0なら公転しない

    [Header("自転のパラメータ")]
    [HideInInspector] public Quaternion[] rotation_planet;
    [HideInInspector] public float[] rotationAxis_planet;
    [HideInInspector] public double[] rotationPeriod_planet; //自転周期(秒) 0なら自転しない、負なら逆行
    [HideInInspector] public bool[] isTidalLock_planet; //潮汐固定(主星に同じ面を向ける)
    [HideInInspector] public float[] yawing_planet;

    [Header("座標")]
    [HideInInspector] public PlanetParentTypes[] parentType_planet;
    [HideInInspector] public int[] parentIndex_planet;
    [HideInInspector] public int[][] lightSourceType_planet;
    [HideInInspector] public int[][] lightSourceIndex_planet;
    [HideInInspector] public int[][] occluderIndex_planet; //遮蔽判定(食)をする天体の惑星番号。i は惑星番号
    [HideInInspector] public float[] occlusion_planet; //主星をたどった先の恒星の光が届いている割合(1:食なし 0:皆既)
    [HideInInspector] public float[] occlusion_star; //観測者から見て恒星の円盤が見えている割合(1:食なし 0:皆既)
    [HideInInspector] public bool[] isLanded; 
    [HideInInspector] public Vector3[] position_planet;
    [HideInInspector] public Vector3[] positionRelative_planet;
    [HideInInspector] public Vector3[] velocity_planet;
    [HideInInspector] public double[] distance_planet;
    [HideInInspector] public double[] meanAnomaly_planet;
    [HideInInspector] public float[] height_planet, height_planet_angle;

    //共通重心(連星)ここから。天体A・Bは重心の周りを同じ離心率・周期で反対側を回る(軌道要素はImporterが天体側に設定する)
    [Header("共通重心")]
    [HideInInspector] public string[] name_bary;
    [HideInInspector] public PlanetParentTypes[] parentType_bary; //親のタイプ
    [HideInInspector] public int[] parentIndex_bary; //親の番号。-1なら星系の原点に固定
    [HideInInspector] public double[] semiMajorAxis_bary; //親の周りの軌道長半径(km)。負なら親の重心をはさんで反対側
    [HideInInspector] public double[] eccentricity_bary; //離心率
    [HideInInspector] public double[] argOfPeri_bary; //近点引数
    [HideInInspector] public double[] orbitalPeriod_bary; //公転周期(秒) 0なら公転しない
    [HideInInspector] public double[] mass_bary; //中の天体の質量の合計(kg)
    [HideInInspector] public int[] rootStar_bary; //光源とみなす恒星(中の最も重い恒星。恒星がなければ親をたどった先の恒星)
    [HideInInspector] public Vector3[] position_bary;

    //公転の計算順(親が先)。type は PlanetParentTypes(恒星・惑星・共通重心)の数値、index はその番号。Importerが設定
    [HideInInspector] public int[] orbitOrderType;
    [HideInInspector] public int[] orbitOrderIndex;


    [Header("物理特性")]
    [HideInInspector] public double[] mass_planet; //kg
    [HideInInspector] public double[] rad_planet; //km
    [HideInInspector] public double[] teff_planet; //温度
    [HideInInspector] public double[] albedoBond;
    [HideInInspector] public double[] flux_planet; //放射束(地球比)
     public double[] flux_planet_obs;
    [HideInInspector] public double[] flux_planet_obs_surface;


    [Header("光学的要素")]
    [HideInInspector] public bool[] isUseLight;
    [HideInInspector] public int[] lightIndex_planet; //Light Moduleのライト番号
    [HideInInspector] public Color[] lightBaseColor_planet;
    [HideInInspector] public double[] appDiameter_planet;
    [HideInInspector] public Light[] lightObj_planet;
    [HideInInspector] public Vector3[][] view_lightDir_planet; //[惑星][光源] 惑星から光源への方向
    [HideInInspector] public Color[][] lightSourceColor_planet; //[惑星][光源] 光源の色
    [HideInInspector] public float[][] lightSourceRatio_planet; //[惑星][光源] 最も明るい光源を1とした明るさの比
    private string[] shaderProp_lightVector, shaderProp_lightColor, shaderProp_lightIntensity;
    [HideInInspector] public double[] albedoGeom;
    [HideInInspector] public float[] hapke;
    [HideInInspector] public double[] appMagn_planet;

    [Header("View")]
    [HideInInspector] public Vector3[] view_position_planetUnity;
    [HideInInspector] public Quaternion[] view_rotation_planetUnity;

    [Header("デバッグ")]
    public float testFloat;
    [SerializeField] private Vector3 testRotationEuler;
    [SerializeField] private bool isDebug, isAutoRotationMode, isOpacityControl;
    private Quaternion testRotation;
    [SerializeField] private float debug_rotationSpeed;
    [SerializeField] public TextMeshProUGUI testText;
    [SerializeField] private Scrollbar debug_rotAxisSc, debug_latSc, debug_lonSc, debug_yawSc, debug_atmOpacitySc;
    [SerializeField] private TextMeshProUGUI debug_rotAxisT, debug_latT, debug_lonT, debug_yawT, debug_atmOpacityT;
    //[SerializeField] private TextMeshProUGUI appDiameterT;

    

    VRCPlayerApi player;
    Vector3 playerPos;


    void OnValidate()
    {
        //光源リストの確認用ログ
        if (lightSourceType_planet == null || lightSourceIndex_planet == null)
        {
            Debug.Log("StE_LightSource: 光源リストが未初期化です");
        }
        else
        {
            for (int i = 0; i < lightSourceType_planet.Length; i++)
            {
                string planetName = (name_planet != null && i < name_planet.Length) ? name_planet[i] : "?";
                int[] types = lightSourceType_planet[i];
                int[] indexes = i < lightSourceIndex_planet.Length ? lightSourceIndex_planet[i] : null;

                if (types == null || indexes == null)
                {
                    Debug.Log("StE_LightSource: [" + i + "] " + planetName + " : 光源リストが未設定です");
                    continue;
                }

                string log = "StE_LightSource: [" + i + "] " + planetName + " :";
                for (int j = 0; j < types.Length && j < indexes.Length; j++)
                {
                    //type 0:恒星 1:惑星
                    string[] names = types[j] == 0 ? name_star : name_planet;
                    string bodyName = (names != null && indexes[j] >= 0 && indexes[j] < names.Length) ? names[indexes[j]] : "?";
                    log += " [" + j + "] type=" + types[j] + " index=" + indexes[j] + " (" + bodyName + ")";
                }
                Debug.Log(log);
            }
        }


        if (isDebug)
        {
            testRotation = Quaternion.Euler(testRotationEuler);

            rotationAxis = Mathf.Lerp(0, 360, debug_rotAxisSc.value);
            latitude = Mathf.Lerp(-90, 90, debug_latSc.value);
            longitude = Mathf.Lerp(-180, 180, debug_lonSc.value);
            yawing = Mathf.Lerp(0, 360, debug_yawSc.value);

            debug_rotAxisT.text = rotationAxis.ToString() + "°";
            debug_latT.text = latitude.ToString() + "°";
            debug_lonT.text = longitude.ToString() + "°";
            debug_yawT.text = yawing.ToString() + "°";
        }

        UpdateYawing(FindPlanetIndex(obs_body_name));
        RotationSet();

        if (isUseSystemMap)
        {
            StarSystemMapUpdate();
        }

        StarInit();


        PlanetInit();
        PlanetPositionUpdate();
        LightInit();

        UpdatePlanet();
        UpdateStar();

        ExtractStar();
        ExtractPlanet();

        //Light Module
        
        UpdateAtmosphere();

        if (L_isDebug)
        {
            debug_baseTemp = L_teff[0];
            debug_baseLum = L_fluxBol[0];
        }

        if (!isLightUpdate)
        {
            return;
        }
        else
        {
            isLightUpdate = false;
        }
        //以下エディターでの処理



        InitLighting();
        UpdatePlanetLightning();
        UpdateLight();




    }

    private void Start()
    {
        InitLighting();
        LightInit();
        InitRenderQueue();

        UpdateStar();
        UpdatePlanetLightning();
        UpdateLight();

        AtmosphereInit();
        CloudInit();
        StarMapInit();

        //---------------------
        sd_res = 1000L;

        //Startより前にオーナーの値を受け取っていれば、そちらの時刻を使う
        if (!isSynced)
        {
            nowDate = GetJ2000Seconds(DateTime.Now);
            sDate = (long)nowDate * sd_res;
        }

        //Debug.Log("now: " + nowDate);

        InitSync();
    }

    

    private void FixedUpdate()
    {
        double profTotal = ProfileNow();
        double prof = profTotal;

        CheckSyncTrigger();
        FixedUpdateBody(prof);

        ProfileAdd(PROF_TOTAL, profTotal);
        ProfileLog();
    }

    private void FixedUpdateBody(double prof)
    {
        STimeUpdate();
        prof = ProfileAdd(PROF_TIME, prof);
        //Debug.Log("now: " + sDateText);
        VRCPlayerApi player = Networking.LocalPlayer;
        playerPos = player.GetBonePosition(HumanBodyBones.Head);

        //天球の位置を更新
        if (player != null)
        {
            cSphere.transform.position = playerPos;
            altitude = playerPos.y;

        }

        //リアルタイム更新しない場合処理を終了
        if (!isRealTimeMode)
        {
            //静的モードでの処理
            return;
        }

        //インターバルごとに更新
        time += Time.deltaTime;
        L_time += Time.deltaTime;
        if (time < updateInterval)
        {
            return;
        }
        else
        {
            time = 0;
        }

        prof = ProfileNow();


        if (isDebug)
        {
            testRotation = Quaternion.Euler(testRotationEuler);

            //オーナー以外がスライダーを動かすと、CheckSyncTrigger が値の変化を見てオーナーを取る
            //(受信時は ApplySync がスライダーを同期した値に合わせる)
            rotationAxis = Mathf.Lerp(0, 360, debug_rotAxisSc.value);
            latitude = Mathf.Lerp(-90, 90, debug_latSc.value);
            longitude = Mathf.Lerp(-180, 180, debug_lonSc.value);
            yawing = Mathf.Lerp(0, 360, debug_yawSc.value);




            debug_rotAxisT.text = rotationAxis.ToString() + "°";
            debug_latT.text = latitude.ToString() + "°";
            debug_lonT.text = longitude.ToString() + "°";
            debug_yawT.text = yawing.ToString() + "°";

            //自動自転モード（テスト）
            if (isAutoRotationMode)
            {
                debug_yawSc.value += (debug_rotationSpeed / 360) * Mathf.Max(updateInterval, 0.005f);
                debug_yawSc.value %= 1;
            }

            if (isOpacityControl)
            {
                debug_atmOpacityT.text = atmOpacity + " x";
                atmOpacity = Mathf.Lerp(0, 10, debug_atmOpacitySc.value * debug_atmOpacitySc.value);
            }

            surfVecObj.rotation = surfaceQ;
            //Debug.Log("surfaceQ" + surfaceQ);

            //appDiameterT.text = "視直径: " + appDiameter_planet[FindPlanetIndex(ext_name_planet)].ToString() + "°";

        }

        if (testText != null)
        {
            testText.text = "デバッグ: " + testFloat.ToString();
        }

        if (isDebug)
        {
            testRotation = Quaternion.Euler(testRotationEuler);
        }

        UpdateYawing(obs_body_index);

        RotationSet();

        if (isUseSystemMap)
        {
            StarSystemMapUpdate();
        }

        prof = ProfileAdd(PROF_BODY_OTHER, prof);

        PlanetPositionUpdate();
        prof = ProfileAdd(PROF_ORBIT, prof);
        UpdatePlanet();
        prof = ProfileAdd(PROF_PLANET, prof);

        UpdateStar();
        prof = ProfileAdd(PROF_STAR, prof);

        UpdateRenderQueue();



        if (isExtractOnPlaymode)
        {
            ExtractStar();
            ExtractPlanet();
        }


        //Debug.Log("obs" + isLanded[1]);

        //観測者の位置を惑星に設定
        obs_body_index = FindPlanetIndex(obs_body_name);

        if (obs_body_index != -1)
        {
            for (int i = 0; i < body_planet.Length; i++)
            {
                if (i == obs_body_index)
                {
                    isLanded[i] = true;
                }
                else
                {
                    isLanded[i] = false;
                }
            }

            //Debug.Log("配列インスタンスID Fixed: " + position_planet.GetHashCode());

            obs_position = new Vector3(position_planet[obs_body_index].x, position_planet[obs_body_index].y, position_planet[obs_body_index].z);
        }

        ProfileAddNoCount(PROF_BODY_OTHER, prof);

        //Light Module

        if (L_time < lightUpdateInterval)
        {
            return;
        }
        else
        {
            L_time = 0;
        }

        if (L_isDebug)
        {
            UpdateDebug();
        }
        prof = ProfileNow();
        UpdatePlanetLightning();
        prof = ProfileAdd(PROF_PLANET_LIGHT, prof);
        UpdateLight();
        prof = ProfileAdd(PROF_LIGHT, prof);

        if (isUseAtmosphere)
        {
            UpdateAtmosphere();
            prof = ProfileAdd(PROF_ATM, prof);
        }

        UpdateCloud();
        prof = ProfileAdd(PROF_CLOUD, prof);
        UpdateStarMap();
        ProfileAdd(PROF_STARMAP, prof);

    }

    //---------------- 処理時間の計測 ----------------

    //計測中なら現在時刻(ms)を返す。計測していなければ0
    private double ProfileNow()
    {
        if (!debug_isProfile) { return 0; }

        if (debug_profileWatch == null)
        {
            debug_profileWatch = new System.Diagnostics.Stopwatch();
            debug_profileWatch.Start();
            debug_profileSum = new double[PROF_NUM];
            debug_profileCount = new int[PROF_NUM];
            debug_profileName = new string[] { "時刻更新", "公転", "惑星更新", "恒星更新", "天体その他", "惑星の光源", "ライト", "大気", "雲", "星空", "FixedUpdate全体" };
            debug_profileLogStart = debug_profileWatch.Elapsed.TotalMilliseconds;
        }
        return debug_profileWatch.Elapsed.TotalMilliseconds;
    }

    //startからの経過時間を区間idxに加算し、現在時刻を返す(次の区間の開始時刻にする)
    private double ProfileAdd(int idx, double start)
    {
        if (!debug_isProfile || debug_profileWatch == null) { return 0; }

        double now = debug_profileWatch.Elapsed.TotalMilliseconds;
        debug_profileSum[idx] += now - start;
        debug_profileCount[idx]++;
        return now;
    }

    //呼び出し回数を数えずに時間だけ加算する(1回の更新で同じ区間を2度測るとき用)
    private void ProfileAddNoCount(int idx, double start)
    {
        if (!debug_isProfile || debug_profileWatch == null) { return; }

        debug_profileSum[idx] += debug_profileWatch.Elapsed.TotalMilliseconds - start;
    }

    //一定間隔で、区間ごとの「1回あたり」と「1秒あたり」の時間をログに出してリセットする
    private void ProfileLog()
    {
        if (!debug_isProfile || debug_profileWatch == null) { return; }

        double now = debug_profileWatch.Elapsed.TotalMilliseconds;
        double span = now - debug_profileLogStart;
        if (span < debug_profileLogInterval * 1000.0) { return; }

        string log = "StE_Profile (" + (span / 1000.0).ToString("F1") + "秒間)";
        for (int i = 0; i < PROF_NUM; i++)
        {
            int cnt = debug_profileCount[i];
            double perCall = cnt > 0 ? debug_profileSum[i] / cnt : 0;
            double perSec = debug_profileSum[i] / (span / 1000.0);
            log += "\n" + debug_profileName[i] + ": " + perCall.ToString("F3") + " ms/回 × " + cnt + "回, " + perSec.ToString("F2") + " ms/秒";

            debug_profileSum[i] = 0;
            debug_profileCount[i] = 0;
        }
        Debug.Log(log);

        debug_profileLogStart = now;
    }

    //----- 同期 -----
    //同期するのは 時間(sDate)・時間倍率・座標(いる天体の名前、惑星にいないときの座標)・自転軸・緯度・経度・大気密度 のみ
    //オーナーが値を変えたら RequestSerialization し、他のプレイヤーは OnDeserialization でオーナーの値をコピーする

    //ローカルプレイヤーがオーナーか(ネットワークがないときはオーナー扱い)
    private bool IsSyncOwner()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) { return true; }
        return Networking.IsOwner(local, gameObject);
    }

    //同期処理初期化(Startから呼ぶ)。オーナーは初期状態を送る。他のプレイヤーは OnDeserialization で受け取る
    public void InitSync()
    {
        if (IsSyncOwner())
        {
            SendSyncTrigger();
        }
        else if (isSynced)
        {
            //Startより前に受け取った値を、sd_resが決まってから当て直す
            ApplySync();
        }
    }

    //明示的に同期を走らせる(オーナーのみ有効)
    public void SendSyncTrigger()
    {
        if (!IsSyncOwner()) { return; }

        RecordSyncPrev();
        sync_freeTime = 0;
        RequestSerialization();
    }

    //観測者のいる天体を変える(UIの入力欄などから呼ぶ)。空文字ならフリーモード(惑星にいない)
    //見つからない名前は無視する(入力途中の文字列で観測者が動かないように)
    //値が変わると CheckSyncTrigger が検出して同期する(オーナー以外ならオーナーを取る)
    public bool SetObsBodyName(string bodyName)
    {
        if (bodyName == null) { bodyName = ""; }
        if (bodyName == obs_body_name) { return true; }

        if (bodyName != "" && !HasPlanetName(bodyName)) { return false; }

        obs_body_name = bodyName;
        obs_body_index = bodyName == "" ? -1 : FindPlanetIndex(bodyName);
        return true;
    }

    public string GetObsBodyName()
    {
        return obs_body_name;
    }

    //----- 時間倍率 (UI・ボタンから呼ぶ) -----
    //値が変わると CheckSyncTrigger が検出して同期する(オーナー以外ならオーナーを取る)

    public double GetTimeFactor()
    {
        return timeFactor;
    }

    //時間倍率を指定した値にする(NaN・無限大は無視)
    public void SetTimeFactor(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { return; }
        timeFactor = value;
    }

    //時間倍率に指定した倍率を掛ける
    public void MultiplyTimeFactor(double multiplier)
    {
        SetTimeFactor(timeFactor * multiplier);
    }

    //現在のシミュレーション時刻の文字列(STimeUpdate で更新)
    public string GetCurrentTimeText()
    {
        return sDateText;
    }

    //----- 表示用 (StarSystemUIManager から呼ぶ) -----

    //恒星の観測等級(食・可視光比率・大気減光・地平線下の散乱を考慮したあとの等級。光点と同じ値)
    public double GetObsMagnitudeStar(int i)
    {
        if (obsFlux_star_surface == null || i < 0 || i >= obsFlux_star_surface.Length) { return 0; }
        return CalcVisMagnitude(obsFlux_star_surface[i]);
    }

    //惑星の観測等級(位相・食・大気減光・地平線下の散乱を考慮したあとの等級。光点と同じ値)
    public double GetObsMagnitudePlanet(int i)
    {
        if (flux_planet_obs_surface == null || i < 0 || i >= flux_planet_obs_surface.Length) { return 0; }
        return CalcVisMagnitude(flux_planet_obs_surface[i]);
    }

    //惑星の位相角(度、恒星-惑星-観測者)。0で満月(満ちている)、180で新月。観測者のいる惑星などで求められないときは -1
    public float GetPhaseAnglePlanet(int i)
    {
        if (position_planet == null || i < 0 || i >= position_planet.Length) { return -1f; }
        if (isLanded != null && i < isLanded.Length && isLanded[i]) { return -1f; }

        int rootStar = GetRootStarIndex(i);
        if (rootStar < 0 || position_star == null || rootStar >= position_star.Length) { return -1f; }

        //UpdatePlanet の cosPhase と同じ向き(恒星→惑星、観測者→惑星)
        Vector3 stp = (position_planet[i] - position_star[rootStar]).normalized;
        Vector3 otp = (position_planet[i] - obs_position).normalized;
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(stp, otp), -1f, 1f)) * Mathf.Rad2Deg;
    }

    //ログを出さずに惑星名があるかを調べる
    private bool HasPlanetName(string bodyName)
    {
        if (name_planet == null) { return false; }
        for (int i = 0; i < name_planet.Length; i++)
        {
            if (name_planet[i] == bodyName) { return true; }
        }
        return false;
    }

    //操作したプレイヤーをオーナーにしてから同期する(同期する値をオーナー以外が操作したとき用)
    private void TakeOwnershipAndSync()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (Utilities.IsValid(local) && !Networking.IsOwner(local, gameObject))
        {
            Networking.SetOwner(local, gameObject);
        }
        SendSyncTrigger();
    }

    //時刻を変える(J2000からの経過秒数)。オーナー以外が呼んだときは、呼んだプレイヤーがオーナーになる
    public void ChangeTime(double j2000Seconds)
    {
        long res = sd_res > 0 ? sd_res : 1000L;
        sDate = (long)Math.Round(j2000Seconds * res);
        TakeOwnershipAndSync();
    }

    //毎フレーム呼ぶ: 同期する値の変化と、フリーモードの定期同期を確認する
    //オーナー以外が値を変えた(スライダーを動かした など)ときは、そのプレイヤーがオーナーになって送る
    private void CheckSyncTrigger()
    {
        bool isOwner = IsSyncOwner();

        //オーナー以外は、オーナーの値を受け取るまでは比べない(初期値の違いでオーナーを奪わないように)
        if (!isOwner && !isSynced) { return; }

        bool isChanged = timeFactor != sync_prevTimeFactor
            || IsSyncValueChanged(rotationAxis, sync_prevRotationAxis)
            || IsSyncValueChanged(latitude, sync_prevLatitude)
            || IsSyncValueChanged(longitude, sync_prevLongitude)
            || IsSyncValueChanged(atmOpacity, sync_prevAtmOpacity)
            || obs_body_name != sync_prevObsBodyName;

        if (!isOwner)
        {
            if (isChanged)
            {
                TakeOwnershipAndSync();
            }
            return;
        }

        //フリーモード(惑星にいない)のときは一定間隔で座標を送る
        if (!isChanged && obs_body_index == -1)
        {
            sync_freeTime += Time.deltaTime;
            isChanged = sync_freeTime >= FREE_SYNC_INTERVAL;
        }

        if (isChanged)
        {
            SendSyncTrigger();
        }
    }

    //スライダーとの往復(InverseLerp → Lerp)で出る誤差は変化とみなさない
    private bool IsSyncValueChanged(float current, float prev)
    {
        return Mathf.Abs(current - prev) > SYNC_EPSILON;
    }

    //変化の検出用に、今の値を覚えておく
    private void RecordSyncPrev()
    {
        sync_prevTimeFactor = timeFactor;
        sync_prevRotationAxis = rotationAxis;
        sync_prevLatitude = latitude;
        sync_prevLongitude = longitude;
        sync_prevAtmOpacity = atmOpacity;
        sync_prevObsBodyName = obs_body_name;
    }

    //送信の直前に、オーナーの値を同期変数にコピーする
    public override void OnPreSerialization()
    {
        onr_sDate = sDate;
        onr_serverTime = Networking.GetServerTimeInSeconds();
        onr_timeFactor = timeFactor;
        onr_obsBodyName = obs_body_name == null ? "" : obs_body_name;
        onr_obsPosition = obs_position;
        onr_rotationAxis = rotationAxis;
        onr_latitude = latitude;
        onr_longitude = longitude;
        onr_atmOpacity = atmOpacity;
    }

    public override void OnDeserialization()
    {
        if (IsSyncOwner()) { return; }

        isSynced = true;
        ApplySync();
    }

    //同期変数の値(オーナーの値)を自分の値にコピーする
    private void ApplySync()
    {
        long res = sd_res > 0 ? sd_res : 1000L;

        //送信からの経過時間 × 時間倍率 だけ進めて、遅延を補正する
        double elapsed = Networking.GetServerTimeInSeconds() - onr_serverTime;
        if (elapsed < 0) { elapsed = 0; }
        sDate = onr_sDate + (long)Math.Round(res * elapsed * onr_timeFactor);

        timeFactor = onr_timeFactor;
        obs_body_name = onr_obsBodyName;
        obs_body_index = FindPlanetIndex(obs_body_name);
        if (obs_body_index == -1)
        {
            obs_position = onr_obsPosition;
        }
        rotationAxis = onr_rotationAxis;
        latitude = onr_latitude;
        longitude = onr_longitude;
        atmOpacity = onr_atmOpacity;

        //デバッグUIのスライダーも合わせる(スライダーから値を読み直しても同じ値になるように)
        SetRotationSliders();
        SetAtmOpacitySlider();

        //受け取った値を基準にする(ここから変わったら自分の操作とみなしてオーナーを取る)
        RecordSyncPrev();
    }

    //自転軸・緯度・経度のスライダーを今の値に合わせる
    private void SetRotationSliders()
    {
        if (debug_rotAxisSc != null) { debug_rotAxisSc.value = Mathf.InverseLerp(0, 360, rotationAxis); }
        if (debug_latSc != null) { debug_latSc.value = Mathf.InverseLerp(-90, 90, latitude); }
        if (debug_lonSc != null) { debug_lonSc.value = Mathf.InverseLerp(-180, 180, longitude); }
    }

    //大気密度のスライダーを今の値に合わせる(atmOpacity = 10 × value² の逆)
    private void SetAtmOpacitySlider()
    {
        if (debug_atmOpacitySc != null) { debug_atmOpacitySc.value = Mathf.Sqrt(Mathf.InverseLerp(0, 10, atmOpacity)); }
    }

    //後から入ったプレイヤーに、今の時刻を送り直す
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || player.isLocal) { return; }
        SendSyncTrigger();
    }

    //オーナーが代わったら、新しいオーナーが今の値を送る
    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) { return; }
        SendSyncTrigger();
    }

    private DateTime STimeToDateTime()
    {
        /*DateTime date = DateTime.MinValue;
        var sd = 0L;
        date.AddYears((int)(sDate / YEAR2SEC / sd_res));
        */

        return new DateTime();
    }

    public void STimeUpdate()
    {
        sDate += (long)Math.Round(sd_res * Time.deltaTime * timeFactor); //経過時間 × 倍率

        if (sDate < 252463597999999L) //9999年12月31日
        {
            //var N2MI = 10000;
            DateTime d = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc); ;
            d = d.AddSeconds(sDate / sd_res); //年を引いて2000を足す (long)(YEAR2SEC * 1e7 * 1999)
            sDateText = d.ToString();
        }
        else
        {
            sDateText = ((double)sDate / YEAR2SEC / sd_res).ToString();
        }

    }

    private static long Mod(long a, long b)
    {
        while (a >= b)
        {
            a -= b;
        }
        return a;
    }

    //観測者のいる惑星の自転周期から、J2000からの経過時間に応じたヨーイング(度)を求める
    //自転周期が0の惑星や、惑星にいないときは yawing をそのまま使う(インスペクタ・デバッグUIの値)
    //負の自転周期は逆行自転として逆向きに回る
    private void UpdateYawing(int bodyIndex)
    {
        if (bodyIndex < 0 || rotationPeriod_planet == null || bodyIndex >= rotationPeriod_planet.Length) { return; }

        //潮汐固定：平均近点角に合わせて一様に自転する(離心率があれば秤動する)
        //緯度0・経度0の地点で主星が天頂に来るように90度ずらす(近点が+x、自転0度で地表が+y向きのため)
        if (isTidalLock_planet != null && bodyIndex < isTidalLock_planet.Length && isTidalLock_planet[bodyIndex])
        {
            double period_orbit = orbitalPeriod_planet != null && bodyIndex < orbitalPeriod_planet.Length ? orbitalPeriod_planet[bodyIndex] : 0;
            double m = CalcMeanAnomaly(GetSimSeconds(), period_orbit, argOfPeri_planet[bodyIndex]);
            double y = m + TIDAL_LOCK_OFFSET; //mは0〜360
            //連星の天体A(軌道長半径が負)は重心をはさんで反対側にいるので、向きも反転する
            if (semiMajorAxis_planet != null && bodyIndex < semiMajorAxis_planet.Length && semiMajorAxis_planet[bodyIndex] < 0) { y += 180.0; }
            y -= Math.Floor(y / 360.0) * 360.0;
            yawing = (float)y;
            return;
        }

        double period = rotationPeriod_planet[bodyIndex];
        if (period == 0) { return; }

        double phase = GetSimSeconds() / period;
        phase -= Math.Floor(phase); //周回数を除いて精度を保つ
        yawing = (float)(phase * 360.0);
    }

    public void RotationSet()
    {
        //自転軸
        rotQ = Quaternion.Euler(rotationAxis, 0, 0);
        view_rotQ = Quaternion.Euler(rotationAxis, 0, 0);

        //緯度経度
        lat_lonQ = rotQ * Quaternion.Euler(latitude-90, 0, 0);
        view_lat_lonQ = view_rotQ * Quaternion.Euler(latitude-90, 0, 0);
        //ヨーイング
        surfaceQ = lat_lonQ * Quaternion.Euler(0, 0, yawing + longitude) * Quaternion.Euler(0,0,90);
        view_surfaceQUnity = view_lat_lonQ * Quaternion.Euler(0, yawing + longitude, 0);
        //地表面ベクトルを計算
        //スタック汚染対策として一度Vector3に変換
        Vector3 axis = lat_lonQ * Vector3.forward;
        surfaceVec = Quaternion.AngleAxis(yawing + longitude, Vector3.forward) * axis;

        view_surfaceVecUnity = view_surfaceQUnity * Vector3.forward;

        //Debug.Log("sq:" + surfaceQ);

        bodys_baseRotationQ = Quaternion.Euler(bodys_baseRotation.x, bodys_baseRotation.y, bodys_baseRotation.z);

    }

    public void StarInit()
    {

        if (obs_position == Vector3.zero)
        {
            return;
        }

        for (int i = 0; i < name_star.Length; i++)
        {
            //恒星の位置は PlanetPositionUpdate が決める(共通重心に入っていなければ原点)
            positionRelative_star[i] = position_star[i] - obs_position; //相対位置を求める



            var distCor = (float)distUnity / positionRelative_star[i].magnitude; //距離正規化のための補正値


            view_position_starUnity[i] = view_surfaceQUnity * new Vector3(positionRelative_star[i].x * (float)distCor,
                                                positionRelative_star[i].z * (float)distCor,
                                                positionRelative_star[i].y * (float)distCor);

            body_star[i].transform.position = cSphere.transform.position + view_position_starUnity[i];

            body_star[i].transform.localScale = new Vector3((float)rad_star[i], (float)rad_star[i], (float)rad_star[i]) * distCor;

            L_lightPhase[i] = 1; //恒星は常に１
            //Debug.Log(L_lightPhase[i] + " lp");
        }
    }

    public void PlanetInit()
    {
        //主星を検索する
        //SearchParent()
    }

    /// <summary>
    /// 主星名から主星情報を取得する。
    /// </summary>
    /// <param name="parentName">主星名</param>
    /// <param name="parentIndex">主星インデックス（戻り値）</param>
    public PlanetParentTypes SearchParent(string parentName, out int parentIndex)
    {
        bool isFound = false;

        parentIndex = FindStarIndex(parentName);

        if (parentIndex != -1)
        {
            isFound = true;
            return PlanetParentTypes.Star;
        }

        parentIndex = FindPlanetIndex(parentName);

        if (parentIndex != -1)
        {
            isFound = true;
            return PlanetParentTypes.Planet;
        }

        parentIndex = 0;

        //恒星があるか判定、なければ惑星を主星に
        if (name_star.Length != 0)
        {
            Debug.Log($"恒星「{parentName}」は存在しないため、星系の主星にフォールバックしました");
            return PlanetParentTypes.Star;
        }
        else
        {
            Debug.Log($"惑星「{parentName}」は存在しないため、星系の主星にフォールバックしました");
            return PlanetParentTypes.Planet;
        }
    }

    /// <summary>
    /// 恒星・惑星・共通重心の位置を公転で求める。親の位置が先に決まるよう、Importer が並べた orbitOrderType / orbitOrderIndex の順に計算する。
    /// 共通重心に入っていない恒星は星系の原点に固定する。
    /// </summary>
    public void PlanetPositionUpdate()
    {
        double simSeconds = GetSimSeconds();

        //再インポート前で計算順がないときは、従来どおり惑星だけを2回に分けて計算する(主星が惑星以外 → 主星が惑星)
        if (orbitOrderType == null || orbitOrderIndex == null || orbitOrderType.Length != orbitOrderIndex.Length || orbitOrderType.Length == 0)
        {
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < name_planet.Length; i++)
            {
                bool isPlanetParent = parentType_planet[i] == PlanetParentTypes.Planet;
                if (isPlanetParent != (pass == 1)) { continue; }
                UpdatePlanetOrbit(i, simSeconds);
            }
            return;
        }

        for (int k = 0; k < orbitOrderType.Length; k++)
        {
            int type = orbitOrderType[k];
            int idx = orbitOrderIndex[k];
            if (type == (int)PlanetParentTypes.Planet) { UpdatePlanetOrbit(idx, simSeconds); }
            else if (type == (int)PlanetParentTypes.Star) { UpdateStarOrbit(idx, simSeconds); }
            else if (type == (int)PlanetParentTypes.BaryCenter) { UpdateBaryOrbit(idx, simSeconds); }
        }
    }

    //惑星の位置: 親の位置 + 公転軌道上の位置。平均近点角 = 経過時間 / 公転周期 + 近点引数
    private void UpdatePlanetOrbit(int i, double simSeconds)
    {
        //Importerで再インポートする前は公転周期がないので、公転させない
        double period = orbitalPeriod_planet != null && i < orbitalPeriod_planet.Length ? orbitalPeriod_planet[i] : 0;
        Vector3 parentPos = GetBodyPosition(parentType_planet[i], parentIndex_planet[i]);

        meanAnomaly_planet[i] = CalcMeanAnomaly(simSeconds, period, argOfPeri_planet[i]);
        position_planet[i] = parentPos + CalcOrbitPosition(semiMajorAxis_planet[i], eccentricity_planet[i], meanAnomaly_planet[i]);

        rotation_planet[i] = Quaternion.Euler(rotationAxis_planet[i], 0, 0);
    }

    //恒星の位置: 共通重心に入っていなければ星系の原点
    private void UpdateStarOrbit(int i, double simSeconds)
    {
        if (parentIndex_star == null || i >= parentIndex_star.Length || parentIndex_star[i] < 0)
        {
            position_star[i] = Vector3.zero;
            return;
        }

        Vector3 parentPos = GetBodyPosition(parentType_star[i], parentIndex_star[i]);
        double m = CalcMeanAnomaly(simSeconds, orbitalPeriod_star[i], argOfPeri_star[i]);
        position_star[i] = parentPos + CalcOrbitPosition(semiMajorAxis_star[i], eccentricity_star[i], m);
    }

    //共通重心の位置: 親がなければ星系の原点
    private void UpdateBaryOrbit(int i, double simSeconds)
    {
        if (parentIndex_bary[i] < 0)
        {
            position_bary[i] = Vector3.zero;
            return;
        }

        Vector3 parentPos = GetBodyPosition(parentType_bary[i], parentIndex_bary[i]);
        double m = CalcMeanAnomaly(simSeconds, orbitalPeriod_bary[i], argOfPeri_bary[i]);
        position_bary[i] = parentPos + CalcOrbitPosition(semiMajorAxis_bary[i], eccentricity_bary[i], m);
    }

    //天体(恒星・惑星・共通重心)の位置。番号が範囲外なら原点
    private Vector3 GetBodyPosition(PlanetParentTypes type, int index)
    {
        if (index < 0) { return Vector3.zero; }
        switch (type)
        {
            case PlanetParentTypes.Star:
                return index < position_star.Length ? position_star[index] : Vector3.zero;
            case PlanetParentTypes.Planet:
                return index < position_planet.Length ? position_planet[index] : Vector3.zero;
            case PlanetParentTypes.BaryCenter:
                return position_bary != null && index < position_bary.Length ? position_bary[index] : Vector3.zero;
        }
        return Vector3.zero;
    }

    //J2000からの経過秒数(sd_resはStartで決まるので、それまではsDateをミリ秒として扱う)
    private double GetSimSeconds()
    {
        if (sd_res > 0) { return (double)sDate / sd_res; }
        return (double)sDate * MILLISEC;
    }

    //平均近点角(度、0〜360)を求める。公転周期が0以下なら近点引数の位置で止まる
    private double CalcMeanAnomaly(double seconds, double period, double argOfPeri)
    {
        double phase = 0;
        if (period > 0)
        {
            phase = seconds / period;
            phase -= Math.Floor(phase); //周回数を除いて精度を保つ
        }

        double m = phase * 360.0 + argOfPeri;
        return m - Math.Floor(m / 360.0) * 360.0;
    }

    //平均近点角(度)からケプラー方程式を解き、主星から見た位置を返す(近点は+x方向)
    private Vector3 CalcOrbitPosition(double semiMajorAxis, double eccentricity, double meanAnomalyDeg)
    {
        double e = eccentricity;
        if (e < 0) { e = 0; }
        if (e > 0.99) { e = 0.99; } //放物線・双曲線軌道は扱わない

        double m = meanAnomalyDeg * Math.PI / 180.0;

        //真円軌道では E = M なので、ケプラー方程式を解かずに円周上に置く
        if (e == 0)
        {
            return new Vector3((float)(semiMajorAxis * Math.Cos(m)), (float)(semiMajorAxis * Math.Sin(m)), 0);
        }

        //ニュートン法で離心近点角Eを求める(M = E - e sinE)
        double ea = e < 0.8 ? m : Math.PI;
        for (int k = 0; k < KEPLER_ITERATION; k++)
        {
            double delta = (ea - e * Math.Sin(ea) - m) / (1 - e * Math.Cos(ea));
            ea -= delta;
            if (Math.Abs(delta) < 1e-10) { break; }
        }

        double x = semiMajorAxis * (Math.Cos(ea) - e);
        double y = semiMajorAxis * Math.Sqrt(1 - e * e) * Math.Sin(ea);
        return new Vector3((float)x, (float)y, 0);
    }

    //視直径(度)がmeshCullDiameter(分角)以下ならメッシュの描画を止める。状態が変わったときだけ切り替える
    private void SetMeshVisible(MeshRenderer mesh, float appDiameterDeg)
    {
        if (mesh == null) { return; }

        bool isVisible = !(appDiameterDeg * 60f <= meshCullDiameter); //NaN(天体の内側など)は描画する側に倒す
        if (mesh.enabled != isVisible)
        {
            mesh.enabled = isVisible;
        }
    }

    public void UpdatePlanet()
    {
        EnsureOcclusionArrays();
        UpdateAtmParams(); //地上の光の透過率を空(StEAtmosphere)と同じ大気で求めるため
        EnsurePointBlocks();

        for (int i = 0;i < name_planet.Length;i++)
        {
            //着陸時の処理
            if (isLanded[i])
            {
                body_planet[i].SetActive(false);

                occlusion_planet[i] = 1f;

                flux_planet_obs[i] = MIN_F;
                flux_planet_obs_surface[i] = MIN_F;

                appDiameter_planet[i] = 180f;
                height_planet[i] = -1f;
                height_planet_angle[i] = -90f;
                distance_planet[i] = MIN_F;

                L_isLanded[lightIndex_planet[i]] = true;

                L_flux[lightIndex_planet[i]] = MIN_F;
                L_flux_light[lightIndex_planet[i]] = MIN_F;
                L_flux_surface[lightIndex_planet[i]] = MIN_F;
                L_surfaceBrightness[lightIndex_planet[i]] = MIN_F;
                L_airmass[lightIndex_planet[i]] = 1f;
                L_transmittance[lightIndex_planet[i]] = Vector3.one;

                L_exposureness[lightIndex_planet[i]] = 0f;

                L_isLanded[lightIndex_planet[i]] = true;

                if (spikeIntensity_planet != null && i < spikeIntensity_planet.Length) { spikeIntensity_planet[i] = 0f; }

                //Debug.Log("landed:" + body_planet[obs_body_index].name);

                continue;
            }

            //if (position_planet[i] == obs_position) { continue; }

            body_planet[i].SetActive(true);

            positionRelative_planet[i] = position_planet[i] - obs_position; //相対位置を求める


            distance_planet[i] = positionRelative_planet[i].magnitude;

            //視直径を求める

            //Asinは半径より近いと引数が1を超えてNaNになるので、Atanで求める(距離0なら180度)
            appDiameter_planet[i] = distance_planet[i] > 0 ? 2f * Mathf.Rad2Deg * Mathf.Atan((float)(rad_planet[i] / distance_planet[i])) : 180f;

            //視直径が小さい惑星はメッシュを描画しない
            SetMeshVisible(mesh_planet[i], (float)appDiameter_planet[i]);

            var distCor = (float)distUnity / distance_planet[i]; //距離正規化のための補正値

            //Debug.Log(name_planet[i] + ": distCor: " + distCor);
            //Debug.Log(name_planet[i] + ": cos: " + Mathf.Cos((float)argOfPeri_planet[i]));



            view_position_planetUnity[i] = view_surfaceQUnity * new Vector3(positionRelative_planet[i].x * (float)distCor,
                                                            positionRelative_planet[i].z * (float)distCor,
                                                            positionRelative_planet[i].y * (float)distCor);

            body_planet[i].transform.position = cSphere.transform.position + view_position_planetUnity[i];

            body_planet[i].transform.localScale = new Vector3((float)rad_planet[i], (float)rad_planet[i], (float)rad_planet[i]) * (float)distCor;

            view_rotation_planetUnity[i] =  rotation_planet[i] * view_surfaceQUnity * bodys_baseRotationQ;

            body_planet[i].transform.rotation = view_rotation_planetUnity[i];

            //恒星からの放射束を求める

            int rootStar = GetRootStarIndex(i); //衛星の場合も、主星をたどった先の恒星を光源にする

            double distFromStar = (position_star[rootStar] - position_planet[i]).magnitude / scaledAU;

            //Debug.Log(body_planet[i].name + ", distFromStar:" + distFromStar);
            flux_planet[i] = lum[rootStar] / (distFromStar * distFromStar) * (1 - albedoGeom[i]);

            //食: 遮蔽天体に隠されて恒星の光が届かない分を減らす
            occlusion_planet[i] = CalcOcclusion(position_planet[i], position_star[rootStar], (float)rad_star[rootStar], i);
            flux_planet[i] *= occlusion_planet[i];
            //Debug.Log(name_planet[i] + " " + flux_planet[i]);

            float phase = 1;


            //太陽→惑星
            var stp = (position_planet[i] - position_star[rootStar]).normalized;
            //観測者→惑星
            var otp = (position_planet[i] - obs_position).normalized;

            phase = Mathf.InverseLerp(-1, 1, Vector3.Dot(stp, otp));


            //Debug.Log(body_planet[i].name + ", phase:" + phase);

            L_lightPhase[lightIndex_planet[i]] = phase;
            var flux = CalcPlanetObsFlux(rad_planet[i], flux_planet[i], distance_planet[i], 1);
            flux_planet_obs[i] = flux * phase;
            //Debug.Log(body_planet[i].name + ", flux:" + flux_planet_obs[i]);

            //高度の計算
            height_planet[i] = Vector3.Dot(surfaceVec, otp);
            height_planet_angle[i] = (180 / Mathf.PI) * Mathf.Asin(Mathf.Clamp(height_planet[i], -1f, 1f)); //内積は誤差で1を超えることがある

            var airmass = CalcAirMass(height_planet[i], appDiameter_planet[i], false);
            Vector3 trans = atm_isValid ? CalcStETransmittance((float)height_planet[i]) : Vector3.one; //空と同じ大気での透過率
            //大気密度による地平線下での散乱強さ
            float scatterDensity = atmOpacity * atmOpacity;
            float exposureness = CalcExposureness(height_planet_angle[i], appDiameter_planet[i], 0f);
            float surfaceFlux = CalcSurfaceFlux(flux_planet_obs[i], airmass, trans);

            flux_planet_obs_surface[i] = Mathf.Lerp(surfaceFlux * scatterDensity, surfaceFlux, exposureness);

            //Debug.Log(body_planet[i].name + ", obsflux:" + flux_planet_obs_surface[i]);

            //Debug.Log(i + " " + body_planet[i].name + ", flux:" + (float)flux * phase);
            //Debug.Log("配列インスタンスID Planet: " + position_planet.GetHashCode());

            //if (phase == float.NaN || phase == float.PositiveInfinity || phase == 0) { Debug.Log("phase:" + phase); }


            //Light Module更新
            if (isUseLight[i])
            {
                //L_lightVector[lightIndex_planet[i]] = StarSystemManager.Vec3Mul(otp, view_surfaceVecUnity);
                L_flux[lightIndex_planet[i]] = (float)flux * phase;
                L_flux_light[lightIndex_planet[i]] = (float)flux_planet_obs_surface[i] / phase;
                L_flux_surface[lightIndex_planet[i]] = (float)flux_planet_obs_surface[i];
                L_surfaceBrightness[lightIndex_planet[i]] = CalcSurfaceBrightness(L_flux_light[lightIndex_planet[i]], rad_planet[i], distance_planet[i]);
                L_airmass[lightIndex_planet[i]] = (float)airmass;
                L_transmittance[lightIndex_planet[i]] = trans;
                L_lightColorBase[lightIndex_planet[i]] = lightBaseColor_planet[i];

                L_exposureness[lightIndex_planet[i]] = exposureness;

                L_isLanded[lightIndex_planet[i]] = false;
            }


            appMagn_planet[i] = CalcVisMagnitude(flux_planet_obs[i]);

            //光点・回折スパイク・光芒(大気減光後の明るさで描く)
            {
                Color pointCol = lightBaseColor_planet[i] * GetAtmReddening((float)airmass, trans);
                //位相角による中心のずらし: 光源の向き(Unity座標)と、光度中心のずれ(角半径に対する割合)
                Vector3 toLight = position_star[rootStar] - position_planet[i];
                Vector3 toLightUnity = (view_surfaceQUnity * new Vector3(toLight.x, toLight.z, toLight.y)).normalized;
                float cosPhase = Vector3.Dot(stp, otp); //位相角(恒星-惑星-観測者)のcos。1で満ちている
                Vector4 phaseDir = new Vector4(toLightUnity.x, toLightUnity.y, toLightUnity.z, PHOTOCENTER_COEF * (1f - cosPhase));

                float spikeIntensity = UpdatePoint(GetRenderer(point_planet, i), GetBlock(pointBlock_planet, i),
                    GetRenderer(spike_planet, i), GetBlock(spikeBlock_planet, i),
                    (float)appDiameter_planet[i], CalcVisMagnitude(flux_planet_obs_surface[i]), pointCol, phaseDir, GetFlag(isUsePSF_planet, i));
                if (spikeIntensity_planet != null && i < spikeIntensity_planet.Length)
                {
                    spikeIntensity_planet[i] = spikeIntensity;
                    spikeDir_planet[i] = CalcPhotocenterDir(view_position_planetUnity[i], phaseDir, (float)appDiameter_planet[i]);
                }
            }

            //Debug.Log(i + " " + body_planet[i].name + ", flux:" + flux * phase);

        }
    }

    public void InitLighting()
    {
        mesh_planet_block = new MaterialPropertyBlock[name_planet.Length];
        for (int i = 0; i < name_planet.Length; i++)
        {
            mesh_planet_block[i] = new MaterialPropertyBlock();

            //ブロック取得
            mesh_planet[i].GetPropertyBlock(mesh_planet_block[i]);

            mesh_planet_block[i].SetFloat("_hapke", hapke[i]);
            mesh_planet_block[i].SetTexture("_MainTex", tex_planet[i]);

            mesh_planet[i].SetPropertyBlock(mesh_planet_block[i]);
        }

        InitLightSource();
    }

    /// <summary>
    /// 惑星ごとの光源(方向・色・明るさ比)の配列を、lightSource****_planet の長さに合わせて確保する。
    /// 光源リストが未設定の惑星は、主星をたどった先の恒星1つを光源にする。
    /// </summary>
    private void InitLightSource()
    {
        //シェーダーのプロパティ名(毎回の文字列連結を避けるため先に作る)
        shaderProp_lightVector = new string[MAX_LIGHT_SOURCE];
        shaderProp_lightColor = new string[MAX_LIGHT_SOURCE];
        shaderProp_lightIntensity = new string[MAX_LIGHT_SOURCE];
        for (int j = 0; j < MAX_LIGHT_SOURCE; j++)
        {
            shaderProp_lightVector[j] = "_lightVector" + j;
            shaderProp_lightColor[j] = "_lightColor" + j;
            shaderProp_lightIntensity[j] = "_lightIntensity" + j;
        }

        int planetNum = name_planet.Length;

        if (lightSourceType_planet == null || lightSourceIndex_planet == null
            || lightSourceType_planet.Length != planetNum || lightSourceIndex_planet.Length != planetNum)
        {
            Debug.LogWarning("StE_LightSource: 光源リストが惑星数と一致しないため、主星のみを光源にします");
            lightSourceType_planet = new int[planetNum][];
            lightSourceIndex_planet = new int[planetNum][];
        }

        view_lightDir_planet = new Vector3[planetNum][];
        lightSourceColor_planet = new Color[planetNum][];
        lightSourceRatio_planet = new float[planetNum][];

        for (int i = 0; i < planetNum; i++)
        {
            int[] types = lightSourceType_planet[i];
            int[] indexes = lightSourceIndex_planet[i];

            //未設定なら主星をたどった先の恒星を光源にする
            if (types == null || indexes == null || types.Length == 0 || types.Length != indexes.Length)
            {
                lightSourceType_planet[i] = new int[] { (int)PlanetParentTypes.Star };
                lightSourceIndex_planet[i] = new int[] { GetRootStarIndex(i) };
            }

            int count = lightSourceType_planet[i].Length;
            if (count > MAX_LIGHT_SOURCE)
            {
                Debug.LogWarning("StE_LightSource: " + name_planet[i] + " の光源が" + MAX_LIGHT_SOURCE + "個を超えているため、先頭の" + MAX_LIGHT_SOURCE + "個だけを使います");
                count = MAX_LIGHT_SOURCE;
            }

            view_lightDir_planet[i] = new Vector3[count];
            lightSourceColor_planet[i] = new Color[count];
            lightSourceRatio_planet[i] = new float[count];
        }
    }

    /// <summary>
    /// 恒星の色(最大成分を1に正規化)を返す。
    /// </summary>
    /// <param name="starIndex">恒星インデックス</param>
    private Color GetStarColor(int starIndex)
    {
        return ColorScaling(TempToColor(profile_teff, profile_color, profile_count[currentPresetIndex], profile_index[currentPresetIndex], (float)teff_star[starIndex], 1.0f));
    }

    

    public void UpdateStar()
    {
        EnsureOcclusionArrays();
        UpdateAtmParams(); //地上の光の透過率を空(StEAtmosphere)と同じ大気で求めるため
        EnsurePointBlocks();

        for (int i = 0; i < name_star.Length; i++)
        {
            //恒星の位置は PlanetPositionUpdate が決める(共通重心に入っていなければ原点)
            positionRelative_star[i] = position_star[i] - obs_position; //相対位置を求める



            var distCor = (float)distUnity / positionRelative_star[i].magnitude; //距離正規化のための補正値


            view_position_starUnity[i] = view_surfaceQUnity * new Vector3(positionRelative_star[i].x * (float)distCor,
                                                positionRelative_star[i].z * (float)distCor,
                                                positionRelative_star[i].y * (float)distCor);

            body_star[i].transform.position = cSphere.transform.position + view_position_starUnity[i];

            body_star[i].transform.localScale = new Vector3((float)rad_star[i], (float)rad_star[i], (float)rad_star[i]) * distCor;

            //視直径。半径と同じ単位(スケール済みkm)の相対位置から求める
            //(distance_starはAUで、しかもこの後で更新されるので使わない。Asinは半径より近いとNaNになるのでAtanを使う)
            float starDist = positionRelative_star[i].magnitude;
            float starDiameterDeg = starDist > 0 ? 2f * Mathf.Rad2Deg * Mathf.Atan((float)rad_star[i] / starDist) : 180f;
            appDiameter_star[i] = starDiameterDeg;

            //視直径が小さい恒星はメッシュを描画しない
            SetMeshVisible(mesh_star[i], starDiameterDeg);


            var scaledAuPos = positionRelative_star[i] / (float)scaledAU;
            distance_star[i] = scaledAuPos.magnitude;
            obsFlux_star[i] =  lum[i] / (distance_star[i] * distance_star[i]);

            //食: 観測者のいる天体の遮蔽天体に隠されている分を減らす
            occlusion_star[i] = CalcOcclusion(obs_position, position_star[i], (float)rad_star[i], obs_body_index);
            obsFlux_star[i] *= occlusion_star[i];
            obsFlux_star_vis[i] = obsFlux_star[i] * TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], (float)teff_star[i]);
            appMagn_star[i] = CalcVisMagnitude(obsFlux_star_vis[i]);


            //高度の計算
            var ots = (position_star[i] - obs_position).normalized;
            //ots = new Vector3 (ots.x, ots.z, ots.y);
            sunVecObj.rotation = Quaternion.LookRotation(ots);
            height_star[i] = Vector3.Dot(surfaceVec, ots);
            height_star_angle[i] = (180 / Mathf.PI) * Mathf.Asin(Mathf.Clamp(height_star[i], -1f, 1f)); //内積は誤差で1を超えることがある
            //Debug.Log("ots: " + ots + " surface: " + surfaceVec);

            var airmass = CalcAirMass(height_star[i], appDiameter_star[i], false);
            Vector3 trans = atm_isValid ? CalcStETransmittance((float)height_star[i]) : Vector3.one; //空と同じ大気での透過率

            //大気密度による地平線下での散乱強さ
            float scatterDensity = atmOpacity * atmOpacity;
            float exposureness = CalcExposureness(height_star_angle[i], appDiameter_star[i], 0f);
            float surfaceFlux = CalcSurfaceFlux(obsFlux_star_vis[i], airmass, trans);

            obsFlux_star_surface[i] = Mathf.Lerp(surfaceFlux * scatterDensity, surfaceFlux, exposureness);

            //lightObj_star[i].transform.LookAt(cSphere.transform.position);
            //lightObj_star[i].intensity = LogScaling((float)obsFlux_star[i]);



            //Light Module更新

            //L_lightVector[i] = StarSystemManager.Vec3Mul( ots, view_surfaceVecUnity);
            L_teff[i] = (float)teff_star[i];
            L_lightPhase[i] = 1f; //恒星は常に1
            L_fluxBol[i] = (float)obsFlux_star[i];
            L_flux_light[i] = (float)obsFlux_star_surface[i];
            L_flux_surface[i] = (float)obsFlux_star_surface[i];
            //見えている部分の表面輝度は食で変わらないので、隠された分を戻して求める
            double unoccludedFlux = occlusion_star[i] > 0f ? obsFlux_star_surface[i] / occlusion_star[i] : 0;
            L_surfaceBrightness[i] = CalcSurfaceBrightness(unoccludedFlux, rad_star[i] / scaledAU, distance_star[i]);
            L_airmass[i] = (float)airmass;
            L_transmittance[i] = trans;
            L_absLum[i] = (float)lum[i];
            L_dist[i] = distance_star[i];

            L_exposureness[i] = exposureness;

            //光点・回折スパイク・光芒(大気減光後の明るさで描く。恒星は自ら光るので中心はずらさない)
            {
                Color pointCol = TempToColor(profile_teff, profile_color, profile_count[currentPresetIndex], profile_index[currentPresetIndex], (float)teff_star[i], 1.0f)
                    * GetAtmReddening((float)airmass, trans);
                float spikeIntensity = UpdatePoint(GetRenderer(point_star, i), GetBlock(pointBlock_star, i),
                    GetRenderer(spike_star, i), GetBlock(spikeBlock_star, i),
                    starDiameterDeg, CalcVisMagnitude(obsFlux_star_surface[i]), pointCol, Vector4.zero, GetFlag(isUsePSF_star, i));
                if (spikeIntensity_star != null && i < spikeIntensity_star.Length)
                {
                    spikeIntensity_star[i] = spikeIntensity;
                    spikeDir_star[i] = view_position_starUnity[i].normalized;
                }
            }


            //Debug.Log("obsFlux: " + obsFlux_star[i]);
        }

        //回折スパイクの天体どうしの遮蔽判定(惑星・恒星の位置がすべて決まった後に行う)
        UpdateSpikeOcclusion();
    }

    /// <summary>
    /// 描画順を変えるため、天体ごとのマテリアルを複製して保持する。
    /// renderer.material はエディタ上で呼ぶとシーンにマテリアルが漏れるので、実行時(Start)だけで呼ぶ。
    /// </summary>
    private void InitRenderQueue()
    {
        mat_planet = new Material[name_planet.Length];
        renderQueue_planet = new int[name_planet.Length];
        renderBlock_planet = new MaterialPropertyBlock[name_planet.Length];
        for (int i = 0; i < name_planet.Length; i++)
        {
            renderBlock_planet[i] = new MaterialPropertyBlock();
            if (mesh_planet[i] == null) { continue; }
            mat_planet[i] = mesh_planet[i].material;
            renderQueue_planet[i] = -1; //初回は必ず設定する
        }
        depthRank_planet = new int[name_planet.Length];

        mat_star = new Material[name_star.Length];
        renderQueue_star = new int[name_star.Length];
        renderBlock_star = new MaterialPropertyBlock[name_star.Length];
        for (int i = 0; i < name_star.Length; i++)
        {
            renderBlock_star[i] = new MaterialPropertyBlock();
            if (mesh_star[i] == null) { continue; }
            mat_star[i] = mesh_star[i].material;
            renderQueue_star[i] = -1;
        }
        depthRank_star = new int[name_star.Length];
    }

    /// <summary>
    /// 描画順(rank: 自分より遠い天体の数)に合わせた深度をシェーダーに渡す。
    /// 天体全体の深度値を BASEDEPTH + rank × BASEDEPTH_INC の一定値にする(深度は1が最近傍、0が最遠なので、近い天体ほど大きい)。
    /// 大気パスは同じ深度だと本体と競合するので、ATM_DEPTH_RATIO × BASEDEPTH_INC だけずらす。
    /// 表側の面は本体より手前、裏側の面は本体より奥(惑星表面に隠れる)にし、どちらも隣の天体の深度との間に収める。
    /// </summary>
    private void SetOrderedDepth(MeshRenderer mesh, MaterialPropertyBlock block, int rank)
    {
        float depth = BASEDEPTH + rank * BASEDEPTH_INC;
        mesh.GetPropertyBlock(block);
        block.SetFloat("_orderedDepth", depth);
        block.SetFloat("_orderedDepthAtmOffset", BASEDEPTH_INC * ATM_DEPTH_RATIO);
        mesh.SetPropertyBlock(block);
    }

    /// <summary>
    /// 天体の描画順と深度を、観測者からの距離に合わせて設定する。
    /// 本体のパスは加算合成なので、後から描いても手前の天体は奥の天体を覆えない。
    /// そのため近い天体から先に描いて深度を書き込み、奥の天体を深度テストで隠す。
    /// RenderQueue は最近傍天体を BASEQUEUE とし、遠い順に 1 ずつ増やす。深度値は近い天体ほど大きくする(SetOrderedDepth)。
    /// 天体番号は惑星 0〜(惑星数-1)、恒星はその後ろに続けて数える。
    /// </summary>
    private void UpdateRenderQueue()
    {
        if (mat_planet == null || mat_star == null) { return; }

        int planetNum = name_planet.Length;
        int total = planetNum + name_star.Length;

        for (int a = 0; a < total; a++)
        {
            double distA = GetRenderDistance(a);
            if (distA < 0) { continue; }

            //自分より近い天体の数(描画順)と遠い天体の数(深度の順位)。同じ距離なら番号の小さい方を遠いとみなす
            int nearer = 0;
            int farther = 0;
            for (int b = 0; b < total; b++)
            {
                if (b == a) { continue; }
                double distB = GetRenderDistance(b);
                if (distB < 0) { continue; }
                if (distB > distA || (distB == distA && b < a)) { farther++; }
                else { nearer++; }
            }

            int queue = BASEQUEUE + nearer;

            //変わったときだけ設定する
            if (a < planetNum)
            {
                if (mat_planet[a] != null && (renderQueue_planet[a] != queue || depthRank_planet[a] != farther))
                {
                    mat_planet[a].renderQueue = queue;
                    renderQueue_planet[a] = queue;
                    depthRank_planet[a] = farther;
                    SetOrderedDepth(mesh_planet[a], renderBlock_planet[a], farther);
                    //光点・光芒も本体と同じ深度にして、手前の天体に隠れるようにする(回折スパイクは UpdateSpikeOcclusion で判定する)
                    SetOrderedDepthIfExists(GetRenderer(point_planet, a), GetBlock(pointBlock_planet, a), farther);
                }
            }
            else
            {
                int s = a - planetNum;
                if (mat_star[s] != null && (renderQueue_star[s] != queue || depthRank_star[s] != farther))
                {
                    mat_star[s].renderQueue = queue;
                    renderQueue_star[s] = queue;
                    depthRank_star[s] = farther;
                    SetOrderedDepth(mesh_star[s], renderBlock_star[s], farther);
                    SetOrderedDepthIfExists(GetRenderer(point_star, s), GetBlock(pointBlock_star, s), farther);
                }
            }
        }
    }

    /// <summary>
    /// UpdateRenderQueue 用の、観測者から天体までの距離(スケール済みkm)。観測者のいる天体は -1(順位の対象外)。
    /// </summary>
    /// <param name="k">惑星 0〜(惑星数-1)、恒星はその後ろに続く通し番号</param>
    private double GetRenderDistance(int k)
    {
        int planetNum = name_planet.Length;
        if (k < planetNum) { return isLanded[k] ? -1 : distance_planet[k]; }
        return positionRelative_star[k - planetNum].magnitude;
    }

    /// <summary>
    /// 食の結果を入れる配列を、天体数に合わせて確保する(値は1:食なし)。
    /// </summary>
    private void EnsureOcclusionArrays()
    {
        if (occlusion_planet == null || occlusion_planet.Length != name_planet.Length)
        {
            occlusion_planet = new float[name_planet.Length];
            for (int i = 0; i < occlusion_planet.Length; i++) { occlusion_planet[i] = 1f; }
        }
        if (occlusion_star == null || occlusion_star.Length != name_star.Length)
        {
            occlusion_star = new float[name_star.Length];
            for (int i = 0; i < occlusion_star.Length; i++) { occlusion_star[i] = 1f; }
        }
    }

    /// <summary>
    /// 光点・光芒と回折スパイクの MaterialPropertyBlock と、遮蔽判定用の配列を確保する(エディタ上の OnValidate からも呼ばれるため、必要なときに作る)。
    /// </summary>
    private void EnsurePointBlocks()
    {
        pointBlock_planet = EnsureBlocks(pointBlock_planet, point_planet);
        pointBlock_star = EnsureBlocks(pointBlock_star, point_star);
        spikeBlock_planet = EnsureBlocks(spikeBlock_planet, spike_planet);
        spikeBlock_star = EnsureBlocks(spikeBlock_star, spike_star);

        if (spikeIntensity_planet == null || spikeIntensity_planet.Length != name_planet.Length)
        {
            spikeIntensity_planet = new float[name_planet.Length];
            spikeDir_planet = new Vector3[name_planet.Length];
        }
        if (spikeIntensity_star == null || spikeIntensity_star.Length != name_star.Length)
        {
            spikeIntensity_star = new float[name_star.Length];
            spikeDir_star = new Vector3[name_star.Length];
        }
    }

    /// <summary>
    /// 回折スパイクの発光中心(光度中心)の向き。天体の向きを、光源の方向へ角半径 × phaseDir.w だけ傾ける(シェーダーの中心のずらしと同じ)。
    /// </summary>
    /// <param name="viewPos">天球の中心から天体への位置(Unity座標)</param>
    /// <param name="phaseDir">xyz:天体から光源への向き w:光度中心のずれ(角半径に対する割合)</param>
    /// <param name="appDiameterDeg">視直径(度)</param>
    private Vector3 CalcPhotocenterDir(Vector3 viewPos, Vector4 phaseDir, float appDiameterDeg)
    {
        Vector3 dir = viewPos.normalized;
        Vector3 toLight = new Vector3(phaseDir.x, phaseDir.y, phaseDir.z);
        Vector3 side = toLight - Vector3.Dot(toLight, dir) * dir; //視線に垂直な成分(画面に投影した光源の向き)
        if (side.sqrMagnitude < 1e-10f) { return dir; }
        float tanHalf = Mathf.Tan(Mathf.Min(appDiameterDeg * 0.5f * Mathf.Deg2Rad, 1.4f));
        return (dir + side.normalized * tanHalf * phaseDir.w).normalized;
    }

    /// <summary>
    /// 回折スパイクの表示を決める。発光中心が手前の天体の円盤に重なっていれば描かない。
    /// 天体本体は _CameraDepthTexture に入らない(加算合成の透明物)ため、天体どうしの遮蔽はここで判定する。地表のオブジェクトによる遮蔽はシェーダーで判定する。
    /// すべての天体の位置が決まった後(UpdateStar の最後)に呼ぶ。
    /// </summary>
    private void UpdateSpikeOcclusion()
    {
        int planetNum = name_planet.Length;
        int total = planetNum + name_star.Length;
        for (int a = 0; a < total; a++)
        {
            bool isPlanet = a < planetNum;
            int idx = isPlanet ? a : a - planetNum;
            MeshRenderer spike = isPlanet ? GetRenderer(spike_planet, idx) : GetRenderer(spike_star, idx);
            if (spike == null) { continue; }

            float intensity = isPlanet ? spikeIntensity_planet[idx] : spikeIntensity_star[idx];
            bool isVisible = intensity > 0f; //NaNは描かない側に倒す
            if (isVisible)
            {
                Vector3 dir = isPlanet ? spikeDir_planet[idx] : spikeDir_star[idx];
                double distA = GetRenderDistance(a);
                for (int b = 0; b < total; b++)
                {
                    if (b == a) { continue; }
                    double distB = GetRenderDistance(b);
                    if (distB < 0 || distB >= distA) { continue; } //手前の天体だけ
                    bool isPlanetB = b < planetNum;
                    int idxB = isPlanetB ? b : b - planetNum;
                    Vector3 posB = isPlanetB ? view_position_planetUnity[idxB] : view_position_starUnity[idxB];
                    float radB = (float)(isPlanetB ? appDiameter_planet[idxB] : appDiameter_star[idxB]) * 0.5f; //角半径(度)
                    if (Vector3.Angle(dir, posB) < radB)
                    {
                        isVisible = false;
                        break;
                    }
                }
            }

            if (spike.enabled != isVisible)
            {
                spike.enabled = isVisible;
            }
        }
    }

    //renderers と同じ長さの MaterialPropertyBlock 配列を返す(長さが合っていればそのまま)
    private MaterialPropertyBlock[] EnsureBlocks(MaterialPropertyBlock[] blocks, MeshRenderer[] renderers)
    {
        if (renderers == null) { return blocks; }
        if (blocks != null && blocks.Length == renderers.Length) { return blocks; }

        blocks = new MaterialPropertyBlock[renderers.Length];
        for (int i = 0; i < blocks.Length; i++) { blocks[i] = new MaterialPropertyBlock(); }
        return blocks;
    }

    //光点・光芒など、Importer で未設定のことがある Renderer に描画順の深度を渡す
    private void SetOrderedDepthIfExists(MeshRenderer mesh, MaterialPropertyBlock block, int rank)
    {
        if (mesh == null || block == null) { return; }
        SetOrderedDepth(mesh, block, rank);
    }

    //配列が未設定・範囲外なら null を返す(光点・光芒は Importer で未設定のことがある)
    private MeshRenderer GetRenderer(MeshRenderer[] renderers, int i)
    {
        return (renderers != null && i < renderers.Length) ? renderers[i] : null;
    }

    private MaterialPropertyBlock GetBlock(MaterialPropertyBlock[] blocks, int i)
    {
        return (blocks != null && i < blocks.Length) ? blocks[i] : null;
    }

    //配列が未設定・範囲外なら false を返す(再インポート前の古いデータでも動くようにする)
    private bool GetFlag(bool[] flags, int i)
    {
        return flags != null && i < flags.Length && flags[i];
    }

    /// <summary>
    /// 光点・光芒(point)と回折スパイク(spike)の色・明るさ・大きさを設定し、回折スパイクの強さを返す。
    /// 光点：大きさと明るさは等級に合わせて、限界等級(昼間は地上の全光量から下げる)で0、pointMagnBright で最大にする。
    ///       視直径が meshCullDiameter 以下では最大、meshCullDiameter × pointFadeRatio で0になり、その間はメッシュと重ねて切り替える。
    /// 光芒：光点が消えた視直径(meshCullDiameter × pointFadeRatio)から出始め、その glareFadeRatio 倍で最大になる。外縁は角半径 × (1 + glareSizeScale × 輝度係数)、強さも輝度係数に比例。
    ///       輝度係数は表面輝度の等級(glareMagnDim で0、glareMagnBright で1)に、全体輝度による抑制(光点より強い)を掛けたもの。
    /// 回折スパイク：表面輝度の等級(分解能 resolvingPower でクリップ)が spikeMagnThreshold より明るいとき、光点とは関係なく描く。
    ///       明るさと長さは表面輝度で決める(長さに上限のピクセル数)。太さは spikeWidthMin × (視直径 / 分解能)^spikeWidthExponent(視直径は分解能でクリップ)。
    ///       点広がり関数(PSF、observerPSF)も同じオブジェクトに描く。強さと大きさは点光源とみなしたときの相対輝度(等級)から決め、視直径は使わない。
    ///       表示/非表示は天体どうしの遮蔽と合わせて UpdateSpikeOcclusion で決める。
    /// </summary>
    /// <param name="appDiameterDeg">視直径(度)</param>
    /// <param name="appMagn">大気減光後の等級</param>
    /// <param name="phaseDir">xyz:天体から光源への向き(Unity座標) w:光度中心のずれ(角半径に対する割合)。恒星は0</param>
    /// <param name="isUsePSF">PSFを使うか(falseならPSFの計算もテクスチャの設定もしない。マテリアルもテクスチャを使わないバリアント)</param>
    private float UpdatePoint(MeshRenderer point, MaterialPropertyBlock pointBlock, MeshRenderer spike, MaterialPropertyBlock spikeBlock,
        float appDiameterDeg, double appMagn, Color col, Vector4 phaseDir, bool isUsePSF)
    {
        float diameterArcmin = appDiameterDeg * 60f;
        float angRad = appDiameterDeg * 0.5f * Mathf.Deg2Rad;
        float m = (float)appMagn;

        //視直径による光点のフェード(分角で比べる。1:最大 0:消える)
        float cull = Mathf.Max(meshCullDiameter, 0.01f);
        float fade = 1f - Mathf.InverseLerp(cull, cull * pointFadeRatio, diameterArcmin);

        //地上の全光量の等級(全体輝度)。空が明るいほど小さい
        bool isAllMagnValid = fluxAllSurface > 0f;
        float allMagn = isAllMagnValid ? (float)CalcVisMagnitude(fluxAllSurface) : 0f;

        if (point != null && pointBlock != null)
        {
            //光点。限界等級は、昼間は空が明るいので全体輝度の等級 + pointDaylightRange まで下げる
            float limit = pointMagnLimit;
            if (isAllMagnValid) { limit = Mathf.Min(limit, allMagn + pointDaylightRange); }
            float t = Mathf.InverseLerp(limit, pointMagnBright, m); //0:限界等級 1:最大
            float pointIntensity = (isUsePoint && fade > 0f && m < limit) ? pointBrightness * t * fade : 0f; //NaNは0に倒す

            //光芒の輝度係数。表面輝度の等級 = 等級 + 5 log10(視直径 / 太陽の視直径)
            float glareT = 0f;
            if (diameterArcmin > 0f)
            {
                float sbMagn = m + 5f * Mathf.Log10(diameterArcmin / SUN_DIAMETER_ARCMIN);
                glareT = Mathf.InverseLerp(glareMagnDim, glareMagnBright, sbMagn);
            }
            //全体輝度による抑制(光点より強い)。天体が全体輝度と同じ明るさで1、glareDaylightRange 等暗いと0
            if (isAllMagnValid)
            {
                glareT *= Mathf.Clamp01((allMagn + glareDaylightRange - m) / glareDaylightRange);
            }
            //光点が消えた視直径(meshCullDiameter × pointFadeRatio)から出始め、その glareFadeRatio 倍で最大になる
            float glareStart = cull * pointFadeRatio;
            float glareFade = Mathf.InverseLerp(glareStart, glareStart * glareFadeRatio, diameterArcmin);
            float glareIntensity = isUseGlare ? glareBrightness * glareT * glareFade : 0f;

            bool isPointVisible = pointIntensity > 0f || glareIntensity > 0f; //NaNは描かない側に倒す
            if (point.enabled != isPointVisible)
            {
                point.enabled = isPointVisible;
            }
            if (isPointVisible)
            {
                //BrightPoint は brightPointMagn から2等かけて強める
                float brightPoint = brightPointStrength * Mathf.Clamp01((brightPointMagn - m) * 0.5f);

                point.GetPropertyBlock(pointBlock);
                pointBlock.SetColor("_Color", col);
                pointBlock.SetFloat("_pointIntensity", pointIntensity);
                pointBlock.SetFloat("_pointSize", Mathf.Lerp(pointSizeMin, pointSizeMax, t));
                pointBlock.SetFloat("_brightPointStrength", brightPoint);
                pointBlock.SetFloat("_glareIntensity", glareIntensity);
                pointBlock.SetFloat("_glareAngularRadius", angRad * (1f + glareSizeScale * glareT));
                pointBlock.SetFloat("_bodyAngularRadius", angRad);
                pointBlock.SetVector("_phaseDir", phaseDir);
                point.SetPropertyBlock(pointBlock);
            }
        }

        float spikeIntensity = 0f;
        float psfIntensity = 0f;
        if (spike != null && spikeBlock != null && isUseSpike)
        {
            //回折スパイク。表面輝度の等級 = 等級 + 5 log10(視直径 / 太陽の視直径)。分解能より小さい天体は分解能の円盤に光が広がるとみなす
            float sourceArcmin = Mathf.Max(diameterArcmin, resolvingPower);
            float sbMagn = m + 5f * Mathf.Log10(sourceArcmin / SUN_DIAMETER_ARCMIN);
            float over = spikeMagnThreshold - sbMagn; //出始めより何等明るいか
            float spikeLength = 0f;
            if (over > 0f)
            {
                spikeIntensity = spikeBrightness * Mathf.Clamp01(over / spikeFadeMagn);
                spikeLength = Mathf.Min(spikeLengthMax, spikeLengthPerMagn * over);
            }

            //PSF。点光源とみなしたときの相対輝度(等級)から決め、視直径は使わない
            float psfOver = psfMagnThreshold - m; //出始めより何等明るいか
            float psfSize = 0f;
            if (isUsePSF && psfOver > 0f)
            {
                psfIntensity = psfBrightness * Mathf.Clamp01(psfOver / psfFadeMagn);
                psfSize = Mathf.Min(psfSizeMax, psfSizePerMagn * psfOver);
            }

            if (spikeIntensity > 0f || psfIntensity > 0f) //NaNは描かない側に倒す
            {
                spike.GetPropertyBlock(spikeBlock);
                spikeBlock.SetColor("_Color", col);
                spikeBlock.SetFloat("_spikeIntensity", spikeIntensity);
                spikeBlock.SetFloat("_spikeCount", spikeCount);
                spikeBlock.SetFloat("_spikeAngle", spikeAngle);
                spikeBlock.SetFloat("_spikeLength", spikeLength);
                //太さは分解能の大きさで spikeWidthMin、そこから視直径に応じて太くする(画面の解像度・視野角によらない)
                spikeBlock.SetFloat("_spikeWidth", spikeWidthMin * Mathf.Pow(sourceArcmin / Mathf.Max(resolvingPower, 0.01f), spikeWidthExponent));
                if (isUsePSF && observerPSF != null) { spikeBlock.SetTexture("_observerPSF", observerPSF); }
                spikeBlock.SetFloat("_psfIntensity", psfIntensity);
                spikeBlock.SetFloat("_psfSize", psfSize);
                spikeBlock.SetFloat("_bodyAngularRadius", angRad);
                spikeBlock.SetVector("_phaseDir", phaseDir);
                spikeBlock.SetFloat("_occlusionDepth", spikeOcclusionDepth);
                spike.SetPropertyBlock(spikeBlock);
            }
        }
        //スパイクの Renderer は回折スパイクかPSFのどちらかがあれば表示する(UpdateSpikeOcclusion で判定)
        return Mathf.Max(spikeIntensity, psfIntensity);
    }

    /// <summary>
    /// 視点から見た光源の円盤のうち、bodyIndex の遮蔽天体(occluderIndex_planet)に隠されずに見えている割合(0〜1)を返す。
    /// </summary>
    /// <param name="viewPos">視点(スケール済みkm)</param>
    /// <param name="lightPos">光源の位置</param>
    /// <param name="lightRad">光源の半径(スケール済みkm)</param>
    /// <param name="bodyIndex">遮蔽天体のリストを使う惑星番号(-1なら食なし)</param>
    private float CalcOcclusion(Vector3 viewPos, Vector3 lightPos, float lightRad, int bodyIndex)
    {
        if (bodyIndex < 0 || occluderIndex_planet == null || bodyIndex >= occluderIndex_planet.Length) { return 1f; }

        int[] occluders = occluderIndex_planet[bodyIndex];
        if (occluders == null) { return 1f; }

        float visible = 1f;
        for (int k = 0; k < occluders.Length; k++)
        {
            int o = occluders[k];
            if (o < 0 || o >= position_planet.Length) { continue; }

            //複数の遮蔽天体が同時に重なる場合は掛け合わせる(近似)
            visible *= CalcDiskVisibility(viewPos, lightPos, lightRad, position_planet[o], (float)rad_planet[o]);
        }
        return visible;
    }

    /// <summary>
    /// 視点から見た光源の円盤が、手前の天体の円盤に隠されずに見えている面積の割合(0〜1)を返す。
    /// 2つの円盤の角半径と離角から、重なった面積を求める(本影・半影・金環食を含む)。
    /// </summary>
    private float CalcDiskVisibility(Vector3 viewPos, Vector3 lightPos, float lightRad, Vector3 occPos, float occRad)
    {
        Vector3 toLight = lightPos - viewPos;
        Vector3 toOcc = occPos - viewPos;
        float distLight = toLight.magnitude;
        float distOcc = toOcc.magnitude;

        //遮蔽天体が光源より遠い、または視点が天体の中にある(判定できない)場合は隠れない
        if (distOcc >= distLight || distOcc <= occRad || distLight <= lightRad) { return 1f; }

        //角半径(rad)
        float rL = Mathf.Asin(lightRad / distLight);
        float rO = Mathf.Asin(occRad / distOcc);

        //離角(rad)。小さい角度でも精度が落ちないよう、外積と内積から求める
        Vector3 nL = toLight / distLight;
        Vector3 nO = toOcc / distOcc;
        float sep = Mathf.Atan2(Vector3.Cross(nL, nO).magnitude, Vector3.Dot(nL, nO));

        if (sep >= rL + rO) { return 1f; }

        float overlap;
        if (sep <= Mathf.Abs(rL - rO))
        {
            //片方の円盤がもう片方に収まっている(皆既・金環)
            float rMin = Mathf.Min(rL, rO);
            overlap = Mathf.PI * rMin * rMin;
        }
        else
        {
            //2つの円の重なり(レンズ形)の面積
            float rL2 = rL * rL;
            float rO2 = rO * rO;
            float sep2 = sep * sep;
            float angL = Mathf.Acos(Mathf.Clamp((sep2 + rL2 - rO2) / (2f * sep * rL), -1f, 1f));
            float angO = Mathf.Acos(Mathf.Clamp((sep2 + rO2 - rL2) / (2f * sep * rO), -1f, 1f));
            float tri = (-sep + rL + rO) * (sep + rL - rO) * (sep - rL + rO) * (sep + rL + rO);
            overlap = rL2 * angL + rO2 * angO - 0.5f * Mathf.Sqrt(Mathf.Max(tri, 0f));
        }

        return Mathf.Clamp01(1f - overlap / (Mathf.PI * rL * rL));
    }

    private void UpdatePlanetLightning()
    {
        EnsureOcclusionArrays();

        for (int i = 0; i < name_planet.Length;i++)
        {
            int count = view_lightDir_planet[i].Length;
            double maxPower = 0;
            int brightest = 0;

            //光源ごとの方向・色・明るさ(恒星の光度/AU^2 を単位とする放射束)を求める
            for (int j = 0; j < count; j++)
            {
                int srcIdx = lightSourceIndex_planet[i][j];
                double power = 0;

                if (lightSourceType_planet[i][j] == (int)PlanetParentTypes.Star)
                {
                    //恒星: 光度 / 距離^2
                    view_lightDir_planet[i][j] = position_star[srcIdx] - position_planet[i];
                    double distAU = view_lightDir_planet[i][j].magnitude / scaledAU;
                    power = distAU > 0 ? lum[srcIdx] / (distAU * distAU) : 0;
                    //食
                    power *= CalcOcclusion(position_planet[i], position_star[srcIdx], (float)rad_star[srcIdx], i);
                    lightSourceColor_planet[i][j] = GetStarColor(srcIdx);
                }
                else
                {
                    //惑星照: 光源惑星が受ける光 × アルベド(可視光) × (半径/距離)^2 × 位相
                    int starIdx = GetRootStarIndex(srcIdx);
                    Vector3 srcPos = position_planet[srcIdx];
                    view_lightDir_planet[i][j] = srcPos - position_planet[i];

                    double dist = view_lightDir_planet[i][j].magnitude; //rad_planetと同じスケール済みkm
                    double starDistAU = (position_star[starIdx] - srcPos).magnitude / scaledAU;

                    if (dist > 0 && starDistAU > 0)
                    {
                        //恒星→光源惑星 と この惑星→光源惑星 の向きが揃うほど満ちている
                        float phase = Mathf.InverseLerp(-1, 1, Vector3.Dot((srcPos - position_star[starIdx]).normalized, (srcPos - position_planet[i]).normalized));
                        double radRatio = rad_planet[srcIdx] / dist;
                        power = lum[starIdx] / (starDistAU * starDistAU) * albedoGeom[srcIdx] * radRatio * radRatio * phase;
                        //光源惑星が食で暗くなっている分
                        power *= occlusion_planet[srcIdx];
                    }

                    //テクスチャ平均色(未設定なら白) × 照らしている恒星の色
                    Color baseCol = lightBaseColor_planet[srcIdx];
                    if (Mathf.Max(baseCol.r, Mathf.Max(baseCol.g, baseCol.b)) <= 0f) { baseCol = Color.white; }
                    lightSourceColor_planet[i][j] = ColorScaling(baseCol * GetStarColor(starIdx));
                }

                lightSourceRatio_planet[i][j] = (float)power;
                if (power > maxPower)
                {
                    maxPower = power;
                    brightest = j;
                }
            }

            //最も明るい光源を1とした比にする
            for (int j = 0; j < count; j++)
            {
                lightSourceRatio_planet[i][j] = maxPower > 0 ? (float)(lightSourceRatio_planet[i][j] / maxPower) : 0f;
            }

            //Debug.Log("meshBlock: " + mesh_planet_block[i]);

            //ブロック取得
            mesh_planet[i].GetPropertyBlock(mesh_planet_block[i]);

            mesh_planet_block[i].SetFloat("_lightCount", count);
            for (int j = 0; j < MAX_LIGHT_SOURCE; j++)
            {
                if (j < count)
                {
                    Vector3 d = view_lightDir_planet[i][j];
                    Vector4 lv = view_surfaceQUnity * new Vector3(d.x, d.z, d.y);
                    mesh_planet_block[i].SetVector(shaderProp_lightVector[j], lv);
                    mesh_planet_block[i].SetColor(shaderProp_lightColor[j], lightSourceColor_planet[i][j]);
                    //比は線形のまま保持し、シェーダーには対数で圧縮して渡す(1→1、0.001→0.25 程度。暗い惑星照も見えるようにする)
                    float ratio = lightSourceRatio_planet[i][j];
                    mesh_planet_block[i].SetFloat(shaderProp_lightIntensity[j], ratio > 0f ? LogScaling(ratio) : 0f);
                }
                else
                {
                    mesh_planet_block[i].SetFloat(shaderProp_lightIntensity[j], 0f);
                }
            }

            //単一光源のシェーダー(リング等)向けに、最も明るい光源の方向も従来の名前で渡す
            if (count > 0)
            {
                Vector3 bd = view_lightDir_planet[i][brightest];
                Vector4 blv = view_surfaceQUnity * new Vector3(bd.x, bd.z, bd.y);
                mesh_planet_block[i].SetVector("_lightVector", blv);
            }

            mesh_planet[i].SetPropertyBlock(mesh_planet_block[i]);

        }

        

        //view_lightDir_planet[0] = position_star[parentBodyIdx[0]] - position_planet[0];
        //body_planet[0].

    }

    private void StarSystemMapUpdate()
    {

        Vector3 obsMarkerPos = (systemMap_Base.position - systemMap_obsMarker.position) * systemMap_scale;


        //フリーモードの座標はオーナーのマーカーで決め、他のプレイヤーは同期した値を使う
        if (isDebug && obs_body_index == -1 && IsSyncOwner())
        {
            obs_position = new Vector3(-obsMarkerPos.x, -obsMarkerPos.z, -obsMarkerPos.y);

        }
    }

    private int FindStarIndex(string name)
    {
        for (int i = 0; i < name_star.Length; i++)
        {
            if (name_star[i] == name)
            {
                return i;
            }
        }

        Debug.Log(name + "は見つかりませんでした");
        return -1;
    } 

    private void ExtractStar()
    {
        var star = FindStarIndex(targetStarName);

        if (star == -1)
        {
            ext_name_star = "---Nothing---";
            return;
        }

        ext_name_star = name_star[star];
        ext_body_star = body_star[star];
        ext_specClass = specClass[star];
        ext_mass_star = mass_star[star];
        ext_rad_star = rad_star[star];
        ext_teff_star = teff_star[star];
        ext_lum_star = lum[star];
        ext_lightObj_star = lightObj_star[star];
        ext_obsFlux_star = obsFlux_star[star];
        ext_obsFlux_star_vis = obsFlux_star_vis[star];
        ext_obsFlux_star_surface = obsFlux_star_surface[star];
        ext_height_star = height_star[star];
        ext_height_star_angle = height_star_angle[star];
        ext_appMagn_star = appMagn_star[star];
        ext_appDiameter_star = appDiameter_star[star];
    }

    /// <summary>
    /// 主星をたどり、惑星（衛星）が属する恒星のインデックスを返す。たどれない場合は0。
    /// </summary>
    /// <param name="planetIndex">惑星インデックス</param>
    private int GetRootStarIndex(int planetIndex)
    {
        int idx = planetIndex;

        //循環参照で無限ループしないよう、惑星数で打ち切る
        for (int n = 0; n <= name_planet.Length; n++)
        {
            if (parentType_planet[idx] == PlanetParentTypes.Star)
            {
                return parentIndex_planet[idx];
            }

            //共通重心: Importer が決めた光源の恒星(中の最も重い恒星か、親をたどった先の恒星)
            if (parentType_planet[idx] == PlanetParentTypes.BaryCenter)
            {
                int bary = parentIndex_planet[idx];
                if (rootStar_bary != null && bary >= 0 && bary < rootStar_bary.Length && rootStar_bary[bary] >= 0) { return rootStar_bary[bary]; }
                break;
            }

            if (parentType_planet[idx] != PlanetParentTypes.Planet)
            {
                break;
            }

            idx = parentIndex_planet[idx];
        }

        return 0;
    }

    private int FindPlanetIndex(string name)
    {
        for (int i = 0; i < name_planet.Length; i++)
        {
            if (name_planet[i] == name)
            {
                return i;
            }
        }

        Debug.Log(name + "は見つかりませんでした");
        return -1;
    }

    private void ExtractPlanet()
    {
        var planet = FindPlanetIndex(targetPlanetName);

        if (planet == -1)
        {
            ext_name_planet = "---Nothing---";
            return;
        }

        ext_name_planet = name_planet[planet];
        ext_body_planet = body_planet[planet];

        ext_parentBodyIdx = parentIndex_planet[planet];
        ext_semiMajorAxis_planet = semiMajorAxis_planet[planet];
        ext_eccentricity_planet = eccentricity_planet[planet];
        ext_inclination_planet = inclination_planet[planet];
        ext_argOfPeri_planet = argOfPeri_planet[planet];
        ext_orbitalPeriod_planet = orbitalPeriod_planet != null && planet < orbitalPeriod_planet.Length ? orbitalPeriod_planet[planet] / DAY2SEC : 0;

        ext_position_planet = position_planet[planet];
        ext_position_planetUnity = view_position_planetUnity[planet];
        ext_velocity_planet = velocity_planet[planet];
        ext_distance_planet = distance_planet[planet];
        ext_meanAnomaly_planet = meanAnomaly_planet[planet];

        ext_mass_planet = mass_planet[planet];
        ext_rad_planet = rad_planet[planet];
        ext_teff_planet = teff_planet[planet];
        ext_albedoBond = albedoBond[planet];
        ext_flux = flux_planet[planet];

        ext_isUseLight = isUseLight[planet];
        ext_appDiameter_planet = appDiameter_planet[planet];
        ext_lightObj_planet = lightObj_planet[planet];
        ext_albedoGeom = albedoGeom[planet];
        ext_flux_planet_obs = flux_planet_obs[planet];
        ext_appMagn_planet = appMagn_planet[planet];
    }

    //惑星の見かけの明るさを計算する
    private double CalcPlanetObsFlux(double rad, double pFlux, double dist, double phaseAngle)
    {
        return phaseAngle * pFlux * ( (rad / dist) * (rad / dist) );
    }

    //明るさを等級スケールに変換する
    private double CalcVisMagnitude(double fluxObs)
    {
        var sunMagnitude = -26.74;
        return sunMagnitude + -Math.Log(fluxObs, 2.5);
    }

    private double CalcAirMass(double hSin, double appDiameter, bool isSimpleMethod)
    {
        double am = 0;
        float r = (float)700;
        float c = (float)hSin;
        if (!isSimpleMethod)
        {
            am = Mathf.Sqrt(r * r * c * c + 2.0f * r + 1.0f) - r * c;
            //am = 1 / (hSin + 0.025 * Mathf.Exp(-11.0f * (float)hSin));
        }
        else
        {
            //am = 1.0 / hSin + Mathf.Sqrt();
        }

        //Debug.Log("am" + am);

        return am * atmOpacity;
    }

    //---- StEAtmosphere と同じ大気での光路長(太陽側の透過率) ----
    //空のシェーダー(StEAtmosphere.shader)の ChapmanColumn / SpherePathRange / ShellPathRange / LayerTau と同じ式。変えるときは両方揃える
    private const int ABSORB_SUBSHELL = 4; //シェーダーの #define ABSORB_SUBSHELL と揃える
    private const float ATM_MAX_EXPONENT = 80f; //シェーダーの MAX_EXPONENT と揃える
    private const float ATM_MIN_SHELL_THICKNESS = 1e-3f; //km。シェーダーの MIN_SHELL_THICKNESS と揃える
    private bool atm_isValid; //StEAtmosphere の値を読めたか(false なら従来の CalcAirMass を使う)
    private float atm_X, atm_H, atm_h0; //惑星半径 / H、スケールハイト(km)、観測者の高さ / H
    private Vector3 atm_tauZ; //海抜0から天頂方向の光学的厚さ(RGB)

    /// <summary>
    /// 空のマテリアル(StEAtmosphere)から大気のパラメータを読み、観測者の高さを求める。
    /// 観測者の高さはシェーダーと同じく、天球(プレイヤーの頭)の y と _SeaLevelY・_AltitudeScale から求める
    /// </summary>
    private void UpdateAtmParams()
    {
        atm_isValid = false;
        if (!isUseStEAtmosphere) { return; }
        Material sky = RenderSettings.skybox;
        if (sky == null) { return; }

        atm_H = Mathf.Max(sky.GetFloat("_ScaleHeight"), 1e-3f);
        atm_X = Mathf.Max(sky.GetFloat("_PlanetRadius"), 1f) / atm_H;
        Vector4 tz = sky.GetVector("_ZenithOpticalDepth");
        atm_tauZ = new Vector3(Mathf.Max(tz.x, 1e-6f), Mathf.Max(tz.y, 1e-6f), Mathf.Max(tz.z, 1e-6f));
        float camY = cSphere != null ? cSphere.transform.position.y : 0f;
        atm_h0 = Mathf.Max(0f, (camY - sky.GetFloat("_SeaLevelY")) * sky.GetFloat("_AltitudeScale") * 0.001f) / atm_H;
        atm_isValid = true;
    }

    //高さ h から天頂角 χ の向きに大気の外までの空気の柱の量(海抜0の密度 × H を1とした値。Schüler 2012 の近似)。X・h は H 単位
    private float ChapmanColumn(float X, float h, float cosChi)
    {
        float c = Mathf.Sqrt(X + h);
        if (cosChi >= 0f) { return c / (c * cosChi + 1f) * Mathf.Exp(-h); }

        float x0 = Mathf.Sqrt(Mathf.Clamp01(1f - cosChi * cosChi)) * (X + h);
        float c0 = Mathf.Sqrt(x0);
        return 2f * c0 * Mathf.Exp(Mathf.Min(X - x0, ATM_MAX_EXPONENT)) - c / (1f - c * cosChi) * Mathf.Exp(-h);
    }

    //半径 R の球の内側を、レイ(中心からの距離 r0、鉛直方向との内積 mu)が t∈[0, ∞) に通る長さ(H単位)。r0² - R² は (r0 - R)(r0 + R) で計算する
    private float SpherePath(float r0, float mu, float R)
    {
        float b = r0 * mu;
        float D = b * b - (r0 - R) * (r0 + R);
        if (D <= 0f) { return 0f; }
        float s = Mathf.Sqrt(D);
        return Mathf.Max(0f, (-b + s) - Mathf.Max(-b - s, 0f));
    }

    //吸収層1つを通る長さ / 殻の厚さ(入れ子の殻の合計。シェーダーの LayerTau の τ を除いた部分)
    private float LayerPathRatio(float r1, float r2, float soft1, float soft2, float r0, float mu)
    {
        float path = 0f;
        for (int k = 0; k < ABSORB_SUBSHELL; k++)
        {
            float f = (k + 0.5f) / ABSORB_SUBSHELL;
            path += SpherePath(r0, mu, r2 - soft2 * f) - SpherePath(r0, mu, r1 + soft1 * f);
        }
        float sumThick = ABSORB_SUBSHELL * ((r2 - r1) - 0.5f * (soft1 + soft2));
        return path / Mathf.Max(sumThick, 1e-6f);
    }

    //使う吸収層の数(ApplyAbsorbLayers と同じ)
    private int GetAbsorbLayerCount()
    {
        if (absorbBottom_layer == null || absorbTop_layer == null || absorbTau_layer == null) { return 0; }
        return Mathf.Min(Mathf.Min(absorbBottom_layer.Length, absorbTop_layer.Length), Mathf.Min(absorbTau_layer.Length, MAX_ABSORB_LAYER));
    }

    /// <summary>
    /// 大気上端から観測者までの光の透過率(RGB)。空のシェーダーの太陽側の計算を、代表点を観測者として行ったもの
    /// 光学的厚さ = _ZenithOpticalDepth × atmOpacity × Chapman(観測者の高さ, 光源の天頂角) + Σ吸収層の τ × atmOpacity × 通る長さ / 厚さ
    /// (吸収層は、光源が地平線より下なら地表で水平な視線の片側の長さ × 2。シェーダーと同じ近似)
    /// </summary>
    private Vector3 CalcStETransmittance(float cosSun)
    {
        float density = Mathf.Max(atmOpacity, 0f);
        float rObs = atm_X + atm_h0;
        Vector3 tau = atm_tauZ * (density * ChapmanColumn(atm_X, atm_h0, cosSun));

        int n = GetAbsorbLayerCount();
        for (int j = 0; j < n; j++)
        {
            //シェーダーの GetAbsorbLayer と同じクランプ
            float bottom = Mathf.Max(absorbBottom_layer[j], 0f);
            float top = Mathf.Max(absorbTop_layer[j], bottom + ATM_MIN_SHELL_THICKNESS);
            float halfThick = 0.5f * (top - bottom);
            float softBottom = (absorbSoftBottom_layer != null && j < absorbSoftBottom_layer.Length) ? absorbSoftBottom_layer[j] : 0f;
            float softTop = (absorbSoftTop_layer != null && j < absorbSoftTop_layer.Length) ? absorbSoftTop_layer[j] : 0f;
            float r1 = atm_X + bottom / atm_H;
            float r2 = atm_X + top / atm_H;
            float soft1 = Mathf.Clamp(softBottom, 0f, halfThick) / atm_H;
            float soft2 = Mathf.Clamp(softTop, 0f, halfThick) / atm_H;
            Vector3 lt = absorbTau_layer[j];
            Vector3 layerTau = new Vector3(Mathf.Max(lt.x, 0f), Mathf.Max(lt.y, 0f), Mathf.Max(lt.z, 0f));

            float ratio = cosSun >= 0f
                ? LayerPathRatio(r1, r2, soft1, soft2, rObs, cosSun)
                : 2f * LayerPathRatio(r1, r2, soft1, soft2, atm_X, 0f);
            tau += layerTau * (ratio * density);
        }

        return new Vector3(
            Mathf.Exp(-Mathf.Min(tau.x, ATM_MAX_EXPONENT)),
            Mathf.Exp(-Mathf.Min(tau.y, ATM_MAX_EXPONENT)),
            Mathf.Exp(-Mathf.Min(tau.z, ATM_MAX_EXPONENT)));
    }

    //透過率(RGB)の可視光での平均(輝度の重み)
    private float TransmittanceLuminance(Vector3 t)
    {
        return 0.2126f * t.x + 0.7152f * t.y + 0.0722f * t.z;
    }

    /// <summary>
    /// 大気による光の色の変化(赤化)。StEAtmosphere の値を読めたら透過率から、読めなければ従来の GetReyleighColor から求める。
    /// 明るさの変化はフラックス側で扱うので、色は最大の成分で割ってから従来と同じく NormalizeRGB で正規化する
    /// </summary>
    private Color GetAtmReddening(float airmass, Vector3 trans)
    {
        if (!atm_isValid) { return GetReyleighColor(airmass, 0.35f); }
        return NormalizeRGB(ColorScaling(new Color(trans.x, trans.y, trans.z, 1f)));
    }

    //大気の外からのフラックスを地上のフラックスに変換する(StEAtmosphere の値を読めたら透過率、読めなければ従来の光学的厚さ 0.15)
    private float CalcSurfaceFlux(double flux, double airmass, Vector3 trans)
    {
        if (!atm_isValid) { return (float)CalcFluxWithAirMass(flux, airmass, 0.15); }
        return Mathf.Clamp((float)flux * TransmittanceLuminance(trans), 0f, (float)flux);
    }

    //天体高度を地表照度に変換
    private double CalcFluxWithAirMass(double flux, double airmass, double optThick)
    {
        var am = airmass;

        return Mathf.Clamp((float)flux * Mathf.Exp((float)-optThick * (float)am), 0, (float)flux);
    }

    private float CalcExposureness(double height, double diameter, double horizonHeight)
    {
        //視直径0(半径0)で0除算にならないよう下限を付ける
        return Mathf.Clamp01((float)height  / Mathf.Max((float)diameter, MIN_DIAMETER_DEG) + 0.5f);
    }

    //寄与度 r を dynamicRange で強調した値(0〜100)。r=0 で dynamicRange>0 のとき 0×∞ でNaNになるのを避ける
    private float CalcDynamicRangeRatio(float r)
    {
        if (!(r > 0f)) { return 0f; }
        return Mathf.Clamp(r * Mathf.Pow(r, r - dynamicRange), 0, 100);
    }

    //NaN・無限大なら0にする(1つの光源の異常値が合計を通して全光源に広がるのを防ぐ安全弁)
    //NaN・無限大は v - v が0にならないことで判定する(float.IsNaN の Udon 対応を避ける)
    private float SanitizeFlux(float v, int lightIdx, string label)
    {
        if (v - v == 0f) { return v; }

        if (!debug_isWarnedInvalidFlux)
        {
            debug_isWarnedInvalidFlux = true; //毎回出すとログが溢れるので最初の1回だけ
            Debug.LogWarning("StE_Light: " + label + " が異常値(" + v + ")のため0にしました。ライト: " + L_lightObj[lightIdx].name);
        }
        return 0f;
    }

    //フラックスを表面輝度に換算する(視直径の2乗で割る)。radとdistは同じ単位
    //1AUから見た太陽と同じ視直径のときに元のフラックスと一致する
    private float CalcSurfaceBrightness(double flux, double rad, double dist)
    {
        if (rad <= 0) { return MIN_F; }
        double r = RAD_SUN_AU * dist / rad;
        return (float)(flux * r * r);
    }

    private float LogScaling(float value)
    {
        var scaledLog = Mathf.Log10((float)value);

        if (scaledLog > 0)
        {
            return 1 * Mathf.Abs(scaledLog + 1);
        }
        else
        {
            return 1 / Mathf.Abs(scaledLog - 1);
        }
    }







    //-------------------------------
    //Light Module
    [Space(100)]

    private const float LX = 218340.61f; //可視光フラックス1が何ルクスに相当するか
    private const float LXBOL = 100000.0f;
    private const double RAD_SUN_AU = 0.00465047; //太陽半径(AU) 表面輝度の基準(1AUから見た太陽の視半径)
    //レイリー散乱の色別強度
    private const float TAU_R = 0.35f;
    private const float TAU_G = 0.18f;
    private const float TAU_B = 0.10f;

    [Space(100),Header("ライトモジュール"),SerializeField, Tooltip("見える照度の下限")] float thresholdLx;
    [SerializeField] private float starLightLx = 0.00013f, lightPollutionLx; //星明かりと光害の強さ
    private float starLight, lightPollution;

    [SerializeField] float intensityBase = 2f, intensityAll;
    [SerializeField] float skyLightBase = 1f;
    [SerializeField] float sunBrightness = 100f, planetBrightness = 30f;
    [SerializeField, Range(-3.0f, 1.0f)] float dynamicRange;
    private bool debug_isWarnedInvalidFlux; //SanitizeFluxの警告を出したか(最初の1回だけ出す)
    [SerializeField, Range(0f, 0.1f), Tooltip("地上の全光量に対する寄与度がこれ未満の恒星は、ワールドライトを無効にする(惑星ライトは対象外)")] float lightCullRatio = 0.005f;
    [SerializeField] float fluxAll, fluxAllLight, fluxAllSurface, fluxAllSurface_lx;
    [SerializeField]private Color L_meanLightColor;
    [Header("光源テスト")]
    //[SerializeField] CelestialBodyLight[] lights = new CelestialBodyLight[0];
    [SerializeField] Light[] L_lightObj = new Light[0];
    [SerializeField] int planetLightStartIdx; //惑星ライトと恒星ライトの境目の番号
    [SerializeField] int[] planetLightRelationIdx; //そのライトがどの惑星に対応するか(-1で恒星)
    [SerializeField] MeshRenderer[] L_lightMesh_star = new MeshRenderer[0];
    [SerializeField] MeshRenderer[] L_lightMesh_planet = new MeshRenderer[0];
    [SerializeField] MaterialPropertyBlock[] L_matBlock_star = new MaterialPropertyBlock[0];
    [SerializeField] MaterialPropertyBlock[] L_matBlock_planet = new MaterialPropertyBlock[0];

    [SerializeField] Vector3[] L_lightVector;
    [SerializeField] Color[] L_lightColorBase;
    [SerializeField] Color[] L_lightColor;
    [SerializeField] Color[] L_lightColor_top; //大気による赤化の前の色(StEAtmosphere はシェーダーで赤化を計算する)
    [SerializeField] bool[] L_isLanded;
    [SerializeField] float[] L_fluxBol, L_absLum, L_absLum_v;
    [SerializeField] float[] L_flux;
    [SerializeField] float[] L_flux_light; //位相なしのフラックス
    [SerializeField] float[] L_flux_surface; //位相込みのフラックス(地上)
    [SerializeField] float[] L_surfaceBrightness; //表面輝度(地上) 観測者からの距離によらない。1AUから見た太陽と同じ視直径のときのフラックスに換算
    [SerializeField] float[] L_lightPhase; //天体の位相(恒星は常に1)
    [SerializeField] float[] L_airmass;
    [SerializeField] Vector3[] L_transmittance; //大気上端から観測者までの透過率(RGB)。StEAtmosphere 使用時に、空のシェーダーと同じ大気(Chapman 関数 + 吸収層)で求める
    [SerializeField] float[] L_exposureness; //露出面積　天体が見えている
    [SerializeField] float[] L_dist;
    [SerializeField] float[] L_lx, L_lx_light, L_lx_surface;
    [SerializeField, Range(0, 30000)] float[] L_teff;
    [SerializeField] float[] L_fluxRatio, L_fluxRatio_light, L_fluxRatio_surface;

    [SerializeField] private double distance;
    [SerializeField] private bool isUpdate;
    [SerializeField] private bool L_isDebug;

    [SerializeField] private bool isInit;

    [Header("光源テスト: 色温度")]
    public int currentPresetIndex = 0;
    public string[] profile_profileName;
    public int[] profile_count; //プロファイルのデータ数
    public int[] profile_index; //プロファイルの開始インデックス

    public string[] profile_name;
    public float[] profile_teff;
    public float[] profile_visibleLightRatio;
    public Color[] profile_color;

    [Header("大気")]
    //[SerializeField, Tooltip("")]
    [HideInInspector] public Material skyMaterial;
    [HideInInspector] public MaterialPropertyBlock skyMaterialBlock;
    private Vector3 currentLightVec;
    private int[] skyLightIdx; //空に渡しているライト番号(L_lightObjの番号)
    private float[] skyLightEff; //ライトごとの空への寄与の見積もり(大気上端の放射束 × EstimateSkyLuminance。L_lightObjの番号)
    private float skyLightEffMax; //skyLightEff の最大
    private float skyLightFluxAll; //空の光源の候補すべての大気上端の放射束の合計(順応なしの明るさに使う)
    private bool isSkyLightFluxOrder; //true: 見積もりが使えないので大気上端の放射束の順で選んでいる(順応しない)
    private string[] skyProp_lightVector, skyProp_lightColor, skyProp_lightWeight;
    private string[] skyProp_lightIntensity; //StEAtmosphere の _LightIntensity0〜3
    private string[] skyProp_skyLightColor; //StEAtmosphere の _SkyLightColor0〜3(_LightColor0 は Unity 組み込みの名前で、描画時に Light の色に上書きされるため使わない)
    [SerializeField, Tooltip("ON: 空に StEAtmosphere(単一散乱の近似)を使う。RenderSettings.skybox を StEAtmosphere のマテリアルにしておくこと")] private bool isUseStEAtmosphere;

    //StEAtmosphere の相対ライティング(目の順応)。光源どうしの比は変えず、空全体に1つのゲインだけを掛ける
    [Header("空の相対ライティング(StEAtmosphere)")]
    [SerializeField, Range(0f, 1f), Tooltip("0: 順応なし(大気上端の放射束の合計で明るさを決める)、1: 地上と同じく、順応量(地表の全光量 + 空の明るさの見積もり)で明るさを決める。間は対数で混ぜる")] private float skyAdaptStrength = 1f;
    [SerializeField, Tooltip("空の明るさの見積もりを順応量にどれだけ入れるか。小さいと薄明や月夜の空が明るく(飛びやすく)なる")] private float skyAdaptWeight = 1f;
    [SerializeField, Range(0f, 0.1f), Tooltip("空への寄与が最も大きいライトのこの倍率未満のライトは、空に渡さない(ほぼ見えない弱いライトの情報落ちによるちらつきを防ぐ)")] private float skyLightCullRatio = 1e-4f;
    [SerializeField, Tooltip("ON: 地上のライトと環境光も、空と同じ順応量(地表の全光量 + 空の明るさ)で明るさを決める。薄明で空が明るい間は地上も暗くなりすぎない")] private bool isUseSkyAdaptGround = true;
    [SerializeField] private float skyDiffuseFlux; //空の明るさの見積もり(地表を照らす天空光とみなす。確認用)
    [SerializeField] private float skyAdaptFlux; //順応量(確認用)。地表の全光量 + skyDiffuseFlux
    [SerializeField] private float skyAdaptLx; //順応量の照度(lx、確認用)
    private float groundAdaptFlux, groundAdaptLx; //地上のライト・環境光の順応量(isUseSkyAdaptGround なら skyAdaptFlux / skyAdaptLx、OFF なら地表の全光量)

    //StEAtmosphere の吸収層(高さが一定の範囲にある一様な球殻)。同じ番号が同じ層。最大 MAX_ABSORB_LAYER 層(3つの配列の短い方の数だけ使う)
    private const int MAX_ABSORB_LAYER = 4; //シェーダーの #define MAX_ABSORB_LAYER と揃える
    [Header("大気の吸収層(StEAtmosphere)")]
    [SerializeField, Tooltip("吸収層の下端の高さ(km、海抜0基準)")] private float[] absorbBottom_layer = new float[0];
    [SerializeField, Tooltip("吸収層の上端の高さ(km、海抜0基準)")] private float[] absorbTop_layer = new float[0];
    [SerializeField, Tooltip("吸収層の鉛直方向の光学的厚さ(RGB)。色と強さの両方を表す(天頂方向の透過率は e^(-τ))")] private Vector3[] absorbTau_layer = new Vector3[0];
    [SerializeField, Tooltip("吸収層の下側の縁の幅(km)。縁を柔らかくする。地表から始まる層は0。層の厚さの半分まで。足りない番号は0")] private float[] absorbSoftBottom_layer = new float[0];
    [SerializeField, Tooltip("吸収層の上側の縁の幅(km)。縁を柔らかくする。層の厚さの半分まで。足りない番号は0")] private float[] absorbSoftTop_layer = new float[0];
    private string[] skyProp_absorbShell, skyProp_absorbTau; //StEAtmosphere の _AbsorbShell0〜3 / _AbsorbTau0〜3

    [Header("大気")]
    [SerializeField, Tooltip("大気機能を使う？")] private bool isUseAtmosphere;
    [SerializeField] private float ambLightBrightness;
    [SerializeField, Range(0,10)] private float atmOpacity = 1f, atmHaze = 0.1f;
    public Color L_ambColorBase_sky;
    public Color L_ambColorBase_equator, L_ambColorBase_ground;
    private Color atmColorTint;

    [Header("雲")]
    public MeshRenderer cloudMesh;
    [HideInInspector]public MaterialPropertyBlock cloudMaterialBlock;
    [SerializeField, Range(0f,2f)] private float coverage;

    private MaterialPropertyBlock starMapMat;
    [Header("星空")]
    [SerializeField] private MeshRenderer starMap;
    [SerializeField] private float starLimitBright = 5.0f;
    [SerializeField] private float starAttenuationBright = 0.25f;
    [SerializeField] private float starLimitMid = 0.2f;
    [SerializeField] private float starAttenuationMid = 0.001f;
    [SerializeField] private float starLimitDim = 0.005f;
    [SerializeField] private float starAttenuationDim = 0.0001f;

    private float starBrt = 0.001f; //星のベース輝度(計算用)

    [Header("デバッグ")]
    [SerializeField] public bool debug_toggleStar2;
    [SerializeField] Scrollbar debug_tempSc, debug_lumSc;
    [SerializeField] TextMeshProUGUI debug_tempTx, debug_lumBolTx, debug_lxTx, debug_VL_Tx;
    public float debug_temp = 1f, debug_lum = 1f;
    private float debug_baseTemp, debug_baseLum;

    private float debug_tempMin = 0f, debug_tempMax = 30000f;
    private float debug_lumMin = 0f, debug_lumMax = 1f;


    public void Debug_ToggleStar2()
    {
        debug_toggleStar2 = !debug_toggleStar2;
    }

    void UpdateDebug()
    {
        //L_teff[0] = Mathf.Lerp(debug_tempMin, debug_tempMax, debug_tempSc.value * debug_tempSc.value);
        //L_fluxBol[0] *= Mathf.Lerp(debug_lumMin, debug_lumMax, debug_lumSc.value * debug_lumSc.value); ;

        debug_tempTx.text = L_teff[0].ToString() + "K";
        debug_lumBolTx.text = L_fluxBol[0].ToString() + "";
        debug_lxTx.text = fluxAllSurface * LX + " lx";
        debug_VL_Tx.text = TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], L_teff[0]) * 100 + " %";

        if (debug_toggleStar2)
        {
            L_fluxBol[1] = 0f;
        }
        else
        {
            //fluxBol[1] = 0.00003f;
        }

    }


    private void LightInit()
    {

        //Light Moduleの初期化

        int lightNum = 0; //ライトの数
        int currentLightIdx = 0; //現在追加しているライトの番号

        starLight = starLightLx / LX;
        lightPollution = lightPollutionLx / LX;

        lightIndex_star = new int[name_star.Length];
        lightIndex_planet = new int[name_planet.Length];

        lightNum += name_star.Length;
        planetLightStartIdx = lightNum;
        planetLightRelationIdx = new int[0];

        for (int i = 0; i < name_planet.Length; i++)
        {
            if (isUseLight[i])
            {
                lightNum++;
            }
        }

        L_lightObj = new Light[lightNum];
        L_lightMesh_star = new MeshRenderer[name_star.Length];
        L_lightMesh_planet = new MeshRenderer[name_planet.Length];



        L_matBlock_star = new MaterialPropertyBlock[name_star.Length];
        L_matBlock_planet = new MaterialPropertyBlock[name_planet.Length];
        L_isLanded = new bool[lightNum];
        L_lightColorBase = new Color[lightNum];
        L_lightVector = new Vector3[lightNum];
        L_lightColor = new Color[lightNum];
        L_lightColor_top = new Color[lightNum];
        L_flux = new float[lightNum];
        L_flux_light = new float[lightNum];
        L_flux_surface = new float[lightNum];
        L_surfaceBrightness = new float[lightNum];
        L_lightPhase = new float[lightNum];
        L_airmass = new float[lightNum];
        L_transmittance = new Vector3[lightNum];
        for (int j = 0; j < lightNum; j++) { L_transmittance[j] = Vector3.one; }
        L_exposureness = new float[lightNum];
        L_fluxBol = new float[lightNum];
        L_dist = new float[lightNum];
        L_absLum = new float[lightNum];
        L_absLum_v = new float[lightNum];
        L_fluxRatio = new float[lightNum];
        L_fluxRatio_light = new float[lightNum];
        L_fluxRatio_surface = new float[lightNum];
        L_lx = new float[lightNum];
        L_lx_light = new float[lightNum];
        L_lx_surface = new float[lightNum];
        L_teff = new float[lightNum];

        //ライトを取得して代入
        //恒星
        for (int i = 0; i < name_star.Length; i++)
        {
            //Debug.Log(i);
            L_lightObj[currentLightIdx] = lightObj_star[i];
            L_lightMesh_star[currentLightIdx] = mesh_star[i];
            L_matBlock_star[currentLightIdx] = new MaterialPropertyBlock();

            LightRelationAdd(-1); //恒星

            currentLightIdx++;
        }
        //惑星
        for (int i = 0; i < name_planet.Length; i++)
        {
            if (isUseLight[i])
            {
                //Debug.Log("planet:"+ i +" idx:"+currentLightIdx);
                //Debug.Log("Plight" + lightObj_planet[i].name);
                L_lightObj[currentLightIdx] = lightObj_planet[i];
                L_lightMesh_planet[i] = mesh_planet[i];
                L_matBlock_planet[i] = new MaterialPropertyBlock();
                lightIndex_planet[i] = currentLightIdx;

                LightRelationAdd(i);

                currentLightIdx++;
            }
        }
        
    }

    //ライト-惑星関係表に追加するメソッド
    private void LightRelationAdd(int addValue)
    {
        if (planetLightRelationIdx == null)
        {
            planetLightRelationIdx = new int[1];
        }

        var tmp = planetLightRelationIdx;
        var tmpLength = tmp.Length;

        

        planetLightRelationIdx = new int[tmp.Length + 1]; //追加

        for (int i = 0; i < tmpLength; i++)
        {
            planetLightRelationIdx[i] = tmp[i];
        }

        planetLightRelationIdx[tmp.Length] = addValue;

        //Debug.Log("plrel idx:" + planetLightRelationIdx.Length);
    }

    private void AtmosphereInit()
    {
        //skyMaterialBlock = new MaterialPropertyBlock();
    }

    private void CloudInit()
    {
        cloudMaterialBlock = new MaterialPropertyBlock();
    }

    public void StarMapInit()
    {
        starMapMat = new MaterialPropertyBlock();
        float starBrtR = starLimitBright / LX;
        starBrt = LogScaling(starBrtR);//星マップのベース
        //Debug.Log("starbrt:" + starBrt);

        //各明るさパラメータがstarBrtの何倍か求める
        float sl_b, sl_m, sl_d, sa_b, sa_m, sa_d;
        sl_b = LogScaling(starBrtR);
        sl_m = LogScaling(starLimitMid * starBrtR);
        sl_d = LogScaling(starLimitDim * starBrtR);

        sa_b = LogScaling(starAttenuationBright * starBrtR);
        sa_m = LogScaling(starAttenuationMid * starBrtR);
        sa_d = LogScaling(starAttenuationDim * starBrtR);

        //Debug.Log("sl_b" + sl_b + " sl_m" + sl_m + " sl_d" + sl_d + " sa_b" + sa_b + " sa_m" + sa_m + " sa_d" + sa_d);

        starMapMat.SetFloat("sl_b", sl_b);
        starMapMat.SetFloat("sl_m", sl_m);
        starMapMat.SetFloat("sl_d", sl_d);
        starMapMat.SetFloat("sa_b", sa_b);
        starMapMat.SetFloat("sa_m", sa_m);
        starMapMat.SetFloat("sa_d", sa_d);

        starMap.SetPropertyBlock(starMapMat);
    }

    //レイリー散乱の減衰結果を取得する
    private Color GetReyleighColor(float airmass, float strength)
    {
        var r = Mathf.Exp(TAU_R * airmass * strength);
        var g = Mathf.Exp(TAU_G * airmass * strength);
        var b = Mathf.Exp(TAU_B * airmass * strength);

        var rey = new Color(r, g, b, 1);
        return NormalizeRGB(rey);
    }

    //閾値以下を線形に暗くするメソッド
    private float GetBelow(float lx)
    {
        return lx / thresholdLx;
    }

    private float GetBelowT(float lx, float threshold)
    {
        return lx / threshold;
    }

    

    private void UpdateLight()
    {
        float fx = 0f, fxLight = 0f, fxSurf = 0f; //総フラックス



        for (int i = 0; i < L_lightObj.Length; i++)
        {
            //
            L_lightObj[i].transform.LookAt(cSphere.transform);
            L_lightVector[i] = Vector3.Normalize(L_lightObj[i].transform.position - cSphere.transform.position);

            //恒星のとき
            //マテリアルの色更新
            if (i < planetLightStartIdx)
            {
                L_lightMesh_star[i].GetPropertyBlock(L_matBlock_star[i]);

                var col = TempToColor(profile_teff, profile_color, profile_count[currentPresetIndex], profile_index[currentPresetIndex], L_teff[i], 1.0f) * 1f;
                var rey = GetAtmReddening(L_airmass[i], L_transmittance[i]);
                L_lightObj[i].color = col * rey;
                L_lightColor[i] = col * rey;
                L_lightColor_top[i] = col;
                L_matBlock_star[i].SetColor("_emissive", col * rey);

                //可視光フラックス計算
                L_flux[i] = L_fluxBol[i] * TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], L_teff[i]);
                //L_absLum_v[i] = L_absLum[i] * TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], L_teff[i]); ;
                appMagn_star[0] = CalcVisMagnitude(L_flux[0]);


            }
            else
            {              
                var pi = planetLightRelationIdx[i]; //惑星ライトのインデックス
                
                //Debug.Log("pi"+pi);
                L_lightMesh_planet[pi].GetPropertyBlock(L_matBlock_planet[pi]);
                //var col = 
                var rey = GetAtmReddening(L_airmass[i], L_transmittance[i]);
                Color baseCol = ColorScaling(L_lightColorBase[i] * rey);
                Color col = ColorScaling(Color.white * rey);
                L_lightColor[i] = baseCol * rey;
                L_lightColor_top[i] = ColorScaling(L_lightColorBase[i]);
                L_lightObj[i].color = baseCol * rey;
                L_matBlock_planet[pi].SetColor("_Color", col * rey);
                //L_lightMesh_planet[pi].GetPropertyBlock(L_matBlock_planet[pi]);

                //L_matBlock_planet
            }
            //安全弁: 異常値の光源は0にして、合計(fluxAll*)と全光源の比が壊れないようにする
            L_flux[i] = SanitizeFlux(L_flux[i], i, "L_flux");
            L_flux_light[i] = SanitizeFlux(L_flux_light[i], i, "L_flux_light");
            L_flux_surface[i] = SanitizeFlux(L_flux_surface[i], i, "L_flux_surface");
            L_surfaceBrightness[i] = SanitizeFlux(L_surfaceBrightness[i], i, "L_surfaceBrightness");
            L_exposureness[i] = SanitizeFlux(L_exposureness[i], i, "L_exposureness");

            fx += L_flux[i];
            fxLight += L_flux_light[i];
            fxSurf += L_flux_surface[i];
            L_lx[i] = L_flux[i] * LX;
            L_lx_light[i] = L_flux_light[i] * LX;
            L_lx_surface[i] = L_flux_surface[i] * LX;


        }

        var ambient = starLight + lightPollution; //環境光（天体以外の光）

        fluxAll = fx + ambient;
        fluxAllLight = fxLight + ambient;
        fluxAllSurface = fxSurf + ambient;
        fluxAllSurface_lx = fxSurf * LX + ambient;

        //順応量。空(StEAtmosphere)と共通で、地表の全光量に空の明るさ(天空光)を足したもの
        //isUseSkyAdaptGround が OFF・StEAtmosphere を使わないときは従来どおり地表の全光量(skyDiffuseFlux = 0)
        CalcSkyLightEff();
        groundAdaptFlux = isUseSkyAdaptGround ? skyAdaptFlux : fluxAllSurface;
        groundAdaptLx = isUseSkyAdaptGround ? skyAdaptLx : fluxAllSurface_lx;
        intensityAll = intensityBase * LogScaling(groundAdaptFlux);

        for (int i = 0; i < L_lightObj.Length; i++)
        {
            float belowCor = 1; //下限をどれくらい下回ったか
            if (fluxAll != 0)
            {



                if (thresholdLx > L_lx[i])
                {
                    belowCor = GetBelow(L_lx[i]);
                    //Debug.Log("belowCor: " + belowCor);
                }
                L_fluxRatio[i] = L_flux[i] / fluxAll;

                if (thresholdLx > L_lx_surface[i])
                {
                    belowCor = GetBelow(L_lx_surface[i]);
                    //Debug.Log("belowCor: " + belowCor);
                }
                L_fluxRatio_surface[i] = L_flux_surface[i] / fluxAllSurface;

                if (thresholdLx > L_lx_light[i])
                {
                    belowCor = GetBelow(L_lx_light[i]);
                    //Debug.Log("belowCor: " + belowCor);
                }
                L_fluxRatio_light[i] = L_flux_light[i] / fluxAllLight;
            }
            else
            {
                L_fluxRatio[i] = 0;
            }

            //寄与度の低い光源はワールドライトを切る(リアルタイムライトの描画負荷を減らす)
            //天体メッシュ・惑星シェーダー・空の光源計算はこのライトのenabledに依存しない
            //惑星ライト(isUseLightで指定したもの)は間引かない
            bool isLightActive = i >= planetLightStartIdx || L_fluxRatio_surface[i] >= lightCullRatio;
            if (L_lightObj[i].enabled != isLightActive)
            {
                L_lightObj[i].enabled = isLightActive;
            }
            if (isLightActive)
            {
                //順応量に対する割合(地上の全光量に対する寄与度 L_fluxRatio_surface は、ライトの間引きや天体メッシュのためにそのまま残す)
                float adaptRatio = groundAdaptFlux > 0f ? L_flux_surface[i] / groundAdaptFlux : 0f;
                L_lightObj[i].intensity = intensityAll * adaptRatio * belowCor * L_exposureness[i];
            }


            //ブロック取得
            //恒星のとき
            if (i < planetLightStartIdx)
            {
                

                L_matBlock_star[i].SetFloat("_intensity", sunBrightness * LogScaling(L_surfaceBrightness[i]) *
                    LogScaling(CalcDynamicRangeRatio(L_fluxRatio_surface[i]))
                    * belowCor);
                /*
                  Debug.Log("lightIntensity: " + sunBrightness * LogScaling(L_flux_surface[i]) *
                    LogScaling(Mathf.Clamp(L_fluxRatio_surface[i] * Mathf.Pow(L_fluxRatio_surface[i], L_fluxRatio_surface[i] - dynamicRange), 0, 100))
                    * belowCor);
                    */
                //Debug.Log(debug_lumSc.value);
                L_lightMesh_star[i].SetPropertyBlock(L_matBlock_star[i]);
            }
            else
            {
                var pi = planetLightRelationIdx[i]; //惑星ライトのインデックス
                //Debug.Log("pi"+pi);



                //Debug.Log("lightname" + L_lightMesh_planet[pi].name);

                L_matBlock_planet[pi].SetFloat("_brightness2", 1 * LogScaling(L_surfaceBrightness[i]) *
                    LogScaling(CalcDynamicRangeRatio(L_fluxRatio_light[i]))
                    * belowCor * planetBrightness);
                L_lightMesh_planet[pi].SetPropertyBlock(L_matBlock_planet[pi]);

                /*L_matBlock_planet[pi].SetFloat("_brightness", 1 * LogScaling(L_flux[i] * (L_dist[i] * L_dist[i])) *
                    LogScaling(Mathf.Clamp(L_fluxRatio[i] * Mathf.Pow(L_fluxRatio[i], L_fluxRatio[i] - dynamicRange), 0, 100))
                    * belowCor);*/
            }

        }

        //平均ライト色を取得
        L_meanLightColor = Color.white;
        for (int i = 0; i < L_lightObj.Length; i++)
        {
            float lightThreshold = 1e-30f;
            //明るさゼロならスキップ
            if (L_fluxRatio_surface[i] < lightThreshold) { continue; }
            L_meanLightColor = Color.Lerp(L_meanLightColor, L_lightColor[i], L_fluxRatio_surface[i]);


        }

    }

    private void UpdateAtmosphere()
    {
        //Tintを初期化
        atmColorTint = Color.white;
        var belowCor = 1f;
        float brightestFlux = 0;
        int brightestLight = 0;
        //各ライトの色を取り出し、合成する
        for (int i = 0; i < L_lightObj.Length; i++)
        {

            if (thresholdLx > L_lx_surface[i])
            {
                belowCor = L_lx_surface[i] / thresholdLx;

            }

            atmColorTint += L_lightColor[i] * L_fluxRatio_surface[i] * belowCor;

            //最も明るいライトを検索
            if (brightestFlux < L_flux_surface[i])
            {
                brightestLight = i;
                if (L_lightVector[i] != Vector3.zero) { currentLightVec = new Vector3(L_lightVector[i].x, L_lightVector[i].y, L_lightVector[i].z); }
                brightestFlux = L_flux_surface[i];
            }
        }

        //環境光は空と同じ順応量(groundAdaptLx / groundAdaptFlux)で決める(薄明で空が明るい間は環境光も残る)
        //エディタの OnValidate では UpdateLight より先に呼ばれるので、未計算なら地表の全光量を使う
        float adaptFlux = groundAdaptFlux > 0f ? groundAdaptFlux : fluxAllSurface;
        float adaptLx = groundAdaptFlux > 0f ? groundAdaptLx : fluxAllSurface_lx;
        atmColorTint = Color.Lerp(L_meanLightColor, L_ambColorBase_sky, ambLightBrightness / 2) * Mathf.Clamp01(GetBelow(adaptLx)); ;


        var scaledFlux = LogScaling(adaptFlux);
        var brightnessCor = scaledFlux * LogScaling(ambLightBrightness) * LogScaling(Mathf.Clamp01(atmOpacity * atmOpacity)); //明るさ補正

        //環境光の色を変える

        RenderSettings.ambientSkyColor = L_ambColorBase_sky * atmColorTint * brightnessCor;
        RenderSettings.ambientEquatorColor = L_ambColorBase_equator * atmColorTint * brightnessCor;
        RenderSettings.ambientGroundColor = L_ambColorBase_ground * atmColorTint * brightnessCor;
        RenderSettings.fogColor = Color.Lerp(L_meanLightColor, L_ambColorBase_equator, 0.8f) * atmColorTint * brightnessCor;
        RenderSettings.fogDensity = 0.01f * atmHaze;

        //新しい大気(StEAtmosphere)は散乱と減衰をシェーダーで計算するので、光源と密度だけを渡す
        if (isUseStEAtmosphere)
        {
            UpdateStEAtmosphere();
            return;
        }

        if (atmOpacity <= 0) {
            RenderSettings.skybox.SetFloat("_exposure", 0f);
            return; 
        }

        //大気
        belowCor = GetBelowT(fluxAllSurface_lx, thresholdLx * 15);
        float skyBrightness = skyLightBase * scaledFlux * Mathf.Clamp01(belowCor);
        RenderSettings.skybox.SetVector("_LightVector", currentLightVec);
        SetSkyLightSources();
        RenderSettings.skybox.SetFloat("_Brightness", skyBrightness);
        RenderSettings.skybox.SetColor("_LightColor", L_meanLightColor);

        RenderSettings.skybox.SetFloat("_opacity", (atmOpacity - 1) * 0.1f);

        float scatterBase = 0.3f, scatterFrac = 0.3f;
        RenderSettings.skybox.SetFloat("_fwdScatterMultiplier", scatterBase + (atmOpacity - 1) * scatterFrac);
        RenderSettings.skybox.SetFloat("_haze", atmHaze);
        RenderSettings.skybox.SetFloat("_exposure", Mathf.Clamp01(LogScaling(atmOpacity * atmOpacity)));

        

    }

    /// <summary>
    /// 空(StESkybox)に複数光源を渡す。地表での放射束が大きい順に最大 MAX_LIGHT_SOURCE 個のライトを選び、
    /// 選んだライトの中での構成比を影響度(_LightWeight)にする。
    /// </summary>
    private void SetSkyLightSources()
    {
        //プロパティ名(毎回の文字列連結を避けるため初回だけ作る)
        if (skyProp_lightVector == null)
        {
            skyProp_lightVector = new string[MAX_LIGHT_SOURCE];
            skyProp_lightColor = new string[MAX_LIGHT_SOURCE];
            skyProp_lightWeight = new string[MAX_LIGHT_SOURCE];
            for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
            {
                skyProp_lightVector[k] = "_LightVector" + k;
                skyProp_lightColor[k] = "_LightColor" + k;
                skyProp_lightWeight[k] = "_LightWeight" + k;
            }
        }
        if (skyLightIdx == null) { skyLightIdx = new int[MAX_LIGHT_SOURCE]; }

        //放射束の大きい順にライトを選ぶ
        int count = 0;
        float totalFlux = 0f;
        for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
        {
            int best = -1;
            float bestFlux = 0f;
            for (int i = 0; i < L_lightObj.Length; i++)
            {
                if (L_flux_surface[i] <= bestFlux || L_lightVector[i] == Vector3.zero) { continue; }

                bool isSelected = false;
                for (int m = 0; m < count; m++)
                {
                    if (skyLightIdx[m] == i) { isSelected = true; break; }
                }
                if (isSelected) { continue; }

                best = i;
                bestFlux = L_flux_surface[i];
            }

            if (best == -1) { break; }

            skyLightIdx[count] = best;
            totalFlux += bestFlux;
            count++;
        }

        Material sky = RenderSettings.skybox;

        //どのライトも届いていない(地平線下など)ときは、従来どおり直前の最も明るいライトの方向だけを使う
        if (count == 0)
        {
            sky.SetFloat("_LightCount", 1);
            sky.SetVector(skyProp_lightVector[0], currentLightVec);
            sky.SetColor(skyProp_lightColor[0], L_meanLightColor);
            sky.SetFloat(skyProp_lightWeight[0], 1f);
            for (int k = 1; k < MAX_LIGHT_SOURCE; k++)
            {
                sky.SetFloat(skyProp_lightWeight[k], 0f);
            }
            return;
        }

        sky.SetFloat("_LightCount", count);
        for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
        {
            if (k < count)
            {
                int i = skyLightIdx[k];
                sky.SetVector(skyProp_lightVector[k], L_lightVector[i]);
                sky.SetColor(skyProp_lightColor[k], L_lightColor[i]);
                sky.SetFloat(skyProp_lightWeight[k], L_flux_surface[i] / totalFlux); //構成比
            }
            else
            {
                sky.SetFloat(skyProp_lightWeight[k], 0f);
            }
        }
    }

    /// <summary>
    /// 空(StEAtmosphere)に光源と大気密度を渡す。空への寄与の見積もり(L_flux × EstimateSkyLuminance)の大きい順に最大 MAX_LIGHT_SOURCE 個のライトを選び、
    /// 最も大きいものの skyLightCullRatio 倍未満は使わない(地平線の下の光源も上空の空気を照らすので候補にする)。強さ(_LightIntensity)は選んだライトの大気上端の放射束の構成比、
    /// 色は大気による赤化の前の色 L_lightColor_top(赤化と減衰はシェーダーが計算する)。
    /// 全体の明るさ(_Brightness)は skyLightBase × CalcSkyExposure(地上と同じ相対ライティング。光源どうしの比は変えない)
    /// </summary>
    private void UpdateStEAtmosphere()
    {
        Material sky = RenderSettings.skybox;
        if (sky == null) { return; }

        //プロパティ名(毎回の文字列連結を避けるため初回だけ作る)
        if (skyProp_lightIntensity == null)
        {
            skyProp_lightVector = new string[MAX_LIGHT_SOURCE];
            skyProp_lightColor = new string[MAX_LIGHT_SOURCE];
            skyProp_lightWeight = new string[MAX_LIGHT_SOURCE];
            skyProp_lightIntensity = new string[MAX_LIGHT_SOURCE];
            skyProp_skyLightColor = new string[MAX_LIGHT_SOURCE];
            for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
            {
                skyProp_lightVector[k] = "_LightVector" + k;
                skyProp_lightColor[k] = "_LightColor" + k;
                skyProp_lightWeight[k] = "_LightWeight" + k;
                skyProp_lightIntensity[k] = "_LightIntensity" + k;
                skyProp_skyLightColor[k] = "_SkyLightColor" + k;
            }
        }
        if (skyLightIdx == null) { skyLightIdx = new int[MAX_LIGHT_SOURCE]; }
        //空への寄与の見積もりは UpdateLight で求めている(エディタの OnValidate では UpdateLight より先に呼ばれるので、未計算ならここで求める)
        if (skyLightEff == null || skyLightEff.Length != L_lightObj.Length) { CalcSkyLightEff(); }

        //空への寄与の大きい順に選ぶ。最も大きいライトの skyLightCullRatio 倍未満は使わない
        //(ほぼ見えない弱いライトが、シェーダーで情報落ちを起こしてちらつくのを防ぐ。強いライトは変えない)
        float effLimit = skyLightEffMax * skyLightCullRatio;
        int count = 0;
        float totalFlux = 0f; //選んだライトの大気上端の放射束の合計
        for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
        {
            int best = -1;
            float bestEff = 0f;
            for (int i = 0; i < L_lightObj.Length; i++)
            {
                if (skyLightEff[i] <= bestEff || skyLightEff[i] < effLimit) { continue; }

                bool isSelected = false;
                for (int m = 0; m < count; m++)
                {
                    if (skyLightIdx[m] == i) { isSelected = true; break; }
                }
                if (isSelected) { continue; }

                best = i;
                bestEff = skyLightEff[i];
            }

            if (best == -1) { break; }

            skyLightIdx[count] = best;
            totalFlux += L_flux[best];
            count++;
        }

        sky.SetFloat("_Density", Mathf.Max(atmOpacity, 0f));
        sky.SetFloat("_LightCount", count);
        for (int k = 0; k < MAX_LIGHT_SOURCE; k++)
        {
            if (k < count)
            {
                int i = skyLightIdx[k];
                sky.SetVector(skyProp_lightVector[k], L_lightVector[i]);
                sky.SetColor(skyProp_skyLightColor[k], L_lightColor_top[i]);
                sky.SetFloat(skyProp_lightIntensity[k], L_flux[i] / totalFlux); //構成比
            }
            else
            {
                sky.SetFloat(skyProp_lightIntensity[k], 0f);
            }
        }
        sky.SetFloat("_Brightness", count > 0 ? skyLightBase * CalcSkyExposure(totalFlux) : 0f);

        ApplyAbsorbLayers(sky);
    }

    /// <summary>
    /// StEAtmosphere に吸収層を渡す。層ごとに個別のプロパティ(_AbsorbShell0〜3 / _AbsorbTau0〜3)に入れ、使わない層は 0 にする。
    /// 層の数は absorbBottom_layer / absorbTop_layer / absorbTau_layer の短い方(最大 MAX_ABSORB_LAYER)
    /// _AbsorbShell の zw には縁の幅(absorbSoftBottom_layer / absorbSoftTop_layer。足りない番号は0)を、層の厚さの半分までにクランプして入れる
    /// </summary>
    private void ApplyAbsorbLayers(Material sky)
    {
        //プロパティ名(毎回の文字列連結を避けるため初回だけ作る)
        if (skyProp_absorbShell == null)
        {
            skyProp_absorbShell = new string[MAX_ABSORB_LAYER];
            skyProp_absorbTau = new string[MAX_ABSORB_LAYER];
            for (int j = 0; j < MAX_ABSORB_LAYER; j++)
            {
                skyProp_absorbShell[j] = "_AbsorbShell" + j;
                skyProp_absorbTau[j] = "_AbsorbTau" + j;
            }
        }

        int n = 0;
        if (absorbBottom_layer != null && absorbTop_layer != null && absorbTau_layer != null)
        {
            n = Mathf.Min(Mathf.Min(absorbBottom_layer.Length, absorbTop_layer.Length), Mathf.Min(absorbTau_layer.Length, MAX_ABSORB_LAYER));
        }

        for (int j = 0; j < MAX_ABSORB_LAYER; j++)
        {
            if (j < n)
            {
                Vector3 tau = absorbTau_layer[j];
                float bottom = absorbBottom_layer[j];
                float top = absorbTop_layer[j];
                float halfThick = Mathf.Max(top - bottom, 0f) * 0.5f;
                float softBottom = (absorbSoftBottom_layer != null && j < absorbSoftBottom_layer.Length) ? absorbSoftBottom_layer[j] : 0f;
                float softTop = (absorbSoftTop_layer != null && j < absorbSoftTop_layer.Length) ? absorbSoftTop_layer[j] : 0f;
                softBottom = Mathf.Clamp(softBottom, 0f, halfThick);
                softTop = Mathf.Clamp(softTop, 0f, halfThick);
                sky.SetVector(skyProp_absorbShell[j], new Vector4(bottom, top, softBottom, softTop));
                sky.SetVector(skyProp_absorbTau[j], new Vector4(tau.x, tau.y, tau.z, 0f));
            }
            else
            {
                sky.SetVector(skyProp_absorbShell[j], Vector4.zero);
                sky.SetVector(skyProp_absorbTau[j], Vector4.zero);
            }
        }
        sky.SetFloat("_AbsorbLayerCount", n);
    }

    /// <summary>
    /// 空(StEAtmosphere)の露出(_Brightness の skyLightBase を除いた部分)。地上のライトと同じ相対ライティング。
    /// 地上は各ライトに G = LogScaling(F) / F(F は地表の全光量)を掛けているのと同じなので、空も選んだライトの放射束の合計に
    /// 1つのゲインを掛ける(光源どうしの比 _LightIntensity は変えない。明るい光源だけを弱めると、薄明が消える・月が太陽並みになるなどで破綻する)。
    /// 順応量 F_adapt = skyAdaptFlux(fluxAllSurface + 空の明るさの見積もり skyDiffuseFlux。CalcSkyLightEff で求め、地上のライト・環境光と共通)。
    /// 空自身の明るさを含めるので、日没直後に地表の全光量が落ちても、太陽に照らされた空のゲインは上がりすぎない。
    /// skyAdaptStrength で、順応なし(LogScaling(合計))と順応ありを対数で混ぜる
    /// </summary>
    private float CalcSkyExposure(float totalFlux)
    {
        //順応なし：候補すべての合計で決めた明るさ(弱いライトを切っても、残ったライトの明るさが変わらないよう、選んだ分の割合を掛ける)
        float bNoAdapt = skyLightFluxAll > 0f ? LogScaling(skyLightFluxAll) * (totalFlux / skyLightFluxAll) : 0f;
        if (!atm_isValid || isSkyLightFluxOrder || skyAdaptStrength <= 0f || !(totalFlux > 0f)) { return bNoAdapt; }

        float fAdapt = skyAdaptFlux;
        if (!(fAdapt > MIN_F)) { return bNoAdapt; }

        float bAdapt = totalFlux * LogScaling(fAdapt) / fAdapt;

        //見える照度の下限(thresholdLx)を下回ったら、地上のライト(GetBelow)と同じく照度に比例して暗くする
        //(ゲインは空自身の明るさで正規化されるので、これがないとどんなに暗い薄明でも順応後の明るさまで持ち上がる)
        if (thresholdLx > 0f && skyAdaptLx < thresholdLx) { bAdapt *= Mathf.Max(skyAdaptLx, 0f) / thresholdLx; }

        if (!(bAdapt > 0f) || !(bNoAdapt > 0f)) { return bAdapt > 0f ? bAdapt : 0f; }

        //対数で混ぜる(露出は比で効くので)
        return Mathf.Pow(bNoAdapt, 1f - skyAdaptStrength) * Mathf.Pow(bAdapt, skyAdaptStrength);
    }

    /// <summary>
    /// ライトごとの空への寄与の見積もり skyLightEff(大気上端の放射束 × EstimateSkyLuminance)と、順応量を求める(UpdateLight から呼ぶ。fluxAllSurface を求めた後)。
    /// skyDiffuseFlux = skyAdaptWeight × Σ skyLightEff(空の明るさ。地表を照らす天空光とみなす)、
    /// skyAdaptFlux = fluxAllSurface + skyDiffuseFlux、skyAdaptLx はその照度。空の露出と、地上のライト・環境光(isUseSkyAdaptGround)で共通に使う。
    /// StEAtmosphere を使わないとき・見積もりがすべて0(宇宙から見下ろすときなど)は、skyLightEff を大気上端の放射束にし、skyDiffuseFlux = 0
    /// </summary>
    private void CalcSkyLightEff()
    {
        int n = L_lightObj.Length;
        if (skyLightEff == null || skyLightEff.Length != n) { skyLightEff = new float[n]; }

        Material sky = RenderSettings.skybox;
        bool isEstimate = isUseStEAtmosphere && atm_isValid && sky != null;
        float mieRatio = isEstimate ? sky.GetFloat("_MieRatio") : 0f;
        float mieG = isEstimate ? sky.GetFloat("_MieG") : 0f;

        skyLightFluxAll = 0f;
        skyLightEffMax = 0f;
        float sumEff = 0f;
        for (int i = 0; i < n; i++)
        {
            skyLightEff[i] = 0f;
            if (!(L_flux[i] > 0f) || L_lightVector[i] == Vector3.zero) { continue; }
            skyLightFluxAll += L_flux[i];
            skyLightEff[i] = isEstimate ? L_flux[i] * EstimateSkyLuminance(L_lightVector[i], mieRatio, mieG) : L_flux[i];
            if (!(skyLightEff[i] - skyLightEff[i] == 0f)) { skyLightEff[i] = 0f; } //NaN・無限大は0(SanitizeFlux と同じ判定)
            sumEff += skyLightEff[i];
            if (skyLightEff[i] > skyLightEffMax) { skyLightEffMax = skyLightEff[i]; }
        }

        //見積もりがすべて0なら、大気上端の放射束の順で選ぶ(順応はしない)
        isSkyLightFluxOrder = !isEstimate || !(skyLightEffMax > MIN_F);
        if (isSkyLightFluxOrder && isEstimate)
        {
            skyLightEffMax = 0f;
            for (int i = 0; i < n; i++)
            {
                skyLightEff[i] = (L_flux[i] > 0f && L_lightVector[i] != Vector3.zero) ? L_flux[i] : 0f;
                if (skyLightEff[i] > skyLightEffMax) { skyLightEffMax = skyLightEff[i]; }
            }
        }

        skyDiffuseFlux = isSkyLightFluxOrder ? 0f : skyAdaptWeight * sumEff;
        skyAdaptFlux = fluxAllSurface + skyDiffuseFlux;
        skyAdaptLx = fluxAllSurface_lx + skyDiffuseFlux * LX;
    }

    /// <summary>
    /// 光源1つ(大気上端の放射束を1とする)が照らす空の明るさの見積もり(輝度の重みの平均)。
    /// シェーダーの計算を代表点 = 観測者として簡略化したもの(LUT の形・密度の補正・吸収層は省く)。
    /// 天頂と、光源の方位の地平線の2方向で求め、明るい方を使う(薄明は光源側の地平線が最も明るいため)
    /// </summary>
    private float EstimateSkyLuminance(Vector3 lightVec, float mieRatio, float mieG)
    {
        if (lightVec == Vector3.zero) { return 0f; }
        Vector3 l = lightVec.normalized;
        float cosSun = l.y;
        float density = Mathf.Max(atmOpacity, 0f);

        //影の境目の高さと、それより上にある空気の割合(シェーダーの ShadowHeight と同じ)
        float hs = atm_h0;
        if (cosSun < 0f)
        {
            float sinChi = Mathf.Sqrt(Mathf.Clamp01(1f - cosSun * cosSun));
            hs = Mathf.Max(atm_h0, atm_X / Mathf.Max(sinChi, 1e-4f) - atm_X);
        }
        float litRatio = Mathf.Exp(-Mathf.Min(hs - atm_h0, ATM_MAX_EXPONENT));
        Vector3 sunOptical = atm_tauZ * (density * ChapmanColumn(atm_X, hs, cosSun));

        //天頂(光源との角の cos は cosSun)と、光源の方位の地平線(cos は sin(天頂角))
        float zenith = SkyDirLuminance(ChapmanColumn(atm_X, atm_h0, 1f), cosSun, density, sunOptical, litRatio, mieRatio, mieG);
        float cosH = Mathf.Sqrt(Mathf.Clamp01(1f - cosSun * cosSun));
        float horizon = SkyDirLuminance(ChapmanColumn(atm_X, atm_h0, 0f), cosH, density, sunOptical, litRatio, mieRatio, mieG);

        return Mathf.Max(zenith, horizon);
    }

    /// <summary>
    /// 1方向(上向き・地平線の視線。代表点は観測者)の空の明るさ。輝度の重みの平均
    /// = (1 - e^(-視線の光学的厚さ)) × ((1 - Mie比) × Rayleigh の位相 × Rayleigh の透過率 + Mie比 × Mie の位相 × Mie の透過率)
    /// Rayleigh の透過率はシェーダーの RayleighTransmittance の上向きの場合(後半だけ)と同じく視線上で積分したもの
    /// (上空ほど空気が薄く赤化が弱いので、観測者の位置の透過率より明るい)。Mie は観測者(影の境目)の高さでの透過率 × litRatio
    /// </summary>
    private float SkyDirLuminance(float viewColumn, float cosTheta, float density, Vector3 sunOptical, float litRatio, float mieRatio, float mieG)
    {
        //位相関数(シェーダーの PhaseRayleigh / PhaseMie と同じ。等方散乱を1とした値)
        float phaseR = (1f - mieRatio) * 0.75f * (1f + cosTheta * cosTheta);
        float phaseM = mieRatio * (1f - mieG * mieG) / Mathf.Pow(Mathf.Max(1f + mieG * mieG - 2f * mieG * cosTheta, 1e-4f), 1.5f);

        Vector3 opt = atm_tauZ * (density * viewColumn);
        Vector3 scat = new Vector3(
            SkyChannel(opt.x, sunOptical.x, litRatio, phaseR, phaseM),
            SkyChannel(opt.y, sunOptical.y, litRatio, phaseR, phaseM),
            SkyChannel(opt.z, sunOptical.z, litRatio, phaseR, phaseM));
        return TransmittanceLuminance(scat);
    }

    //SkyDirLuminance の1色分。a：視線の光学的厚さ、b：光源方向の光学的厚さ(影の境目の高さ)、us：影の境目より上にある空気の割合
    private float SkyChannel(float a, float b, float us, float phaseR, float phaseM)
    {
        float amount = 1f - Mathf.Exp(-Mathf.Min(a, ATM_MAX_EXPONENT)); //散乱の量

        //Rayleigh：シェーダーの RayleighTransmittance(c = 0, xp = 0)。後半 x ∈ [a(1 - us), a] で太陽側が b → 0 と変わる
        float usSafe = Mathf.Max(us, 1e-6f);
        float x0 = Mathf.Clamp(a - us * a, 0f, a);
        float transR = SegmentScatterF(x0, a, b * ((a - x0) / Mathf.Max(a, 1e-6f)) / usSafe, 0f) / Mathf.Max(amount, 1e-6f);

        //Mie：観測者(影の境目)の高さでの透過率
        float transM = Mathf.Exp(-Mathf.Min(b, ATM_MAX_EXPONENT)) * us;

        return amount * (phaseR * transR + phaseM * transM);
    }

    //シェーダーの SegmentScatter の1色分。∫ e^(-x - s(x)) dx(x ∈ [x0, x1]、s は s0 → s1 と線形)
    private float SegmentScatterF(float x0, float x1, float s0, float s1)
    {
        float L = Mathf.Max(x1 - x0, 0f);
        float E0 = x0 + s0;
        float E1 = x1 + s1;
        float dE = E1 - E0;
        float e0 = Mathf.Exp(-Mathf.Min(E0, ATM_MAX_EXPONENT));
        float e1 = Mathf.Exp(-Mathf.Min(E1, ATM_MAX_EXPONENT));
        if (Mathf.Abs(dE) < 1e-4f) { return L * e0; }
        return L * (e0 - e1) / dE;
    }

    public void UpdateStarMap()
    {
        //starMapMat.SetFloat("_brightness");
        starMap.transform.rotation = view_surfaceQUnity;

        float atmCor;

        if (atmOpacity != 0)
        {
            atmCor = LogScaling(LogScaling(fluxAllSurface_lx) * atmOpacity * atmOpacity);
        }
        else
        {
            atmCor = 0;
        }

        //星空は従来どおり地表の全光量で決める(intensityAll は順応量を使うので、ここでは使わない)
        starMapMat.SetFloat("_brightness", 1 * LogScaling(fluxAllSurface) * atmCor);
        starMapMat.SetVector("_LightVector", currentLightVec);
        //Debug.Log("starBrt" + 1 * (intensityAll * atmCor / intensityBase));
        starMap.SetPropertyBlock(starMapMat);
    }

    public void UpdateCloud()
    {
        var scaledFlux = LogScaling(fluxAllSurface);
        float belowCor = GetBelowT(fluxAllSurface_lx, thresholdLx * 15);
        float cloudBrightness = scaledFlux * Mathf.Clamp01(belowCor);
        cloudMaterialBlock.SetFloat("_Brightness", cloudBrightness);
        cloudMaterialBlock.SetFloat("_Coverage", coverage);
        cloudMaterialBlock.SetColor("_Color", Color.Lerp(L_meanLightColor, Color.white, 0.78f));
        cloudMaterialBlock.SetVector("_LightVector", currentLightVec);

        cloudMesh.SetPropertyBlock(cloudMaterialBlock);
    }


    public Color TempToColor(float[] profTeff, Color[] profColor, int profCount, int profIndex, float teff, float intensity) //温度を色に変換
    {
        Color returnColor = new Color(0, 0, 0, 1);
        float lerpFactor; //補間係数
        //Debug.Log(presets[0].teff);
        for (int i = 1 + profIndex; i < profCount + profIndex; i++)
        {

            //Debug.Log(presets[i].teff);

            if (teff < profTeff[i]) //どの区間にあるか一つずつ走査する
            {
                lerpFactor = Mathf.InverseLerp(profTeff[i - 1], profTeff[i], teff); //現在の区間の温度とその先の温度から補間係数を計算
                returnColor = Color.Lerp(profColor[i - 1], profColor[i], lerpFactor); //色を線形補間
                                                                                      //Debug.Log("temp : " + teff + " color : " + returnColor);
                                                                                      //Debug.Log("indextemp" + presets[i].teff + "lerpF : " + lerpFactor);

                return returnColor * intensity;
            }
        }
        returnColor = profColor[profColor.Length - 1]; //どこにも入らなかったら最後の温度より上 = 最後の色を代入
        return returnColor * intensity;
    }

    public float TempToFlux(float[] profTeff, float[] profVL, int profCount, int profIndex, float teff) //温度を色に変換
    {
        float returnCor; //倍率
        float lerpFactor; //補間係数
        //Debug.Log(presets[0].teff);
        for (int i = 1 + profIndex; i < profCount + profIndex; i++)
        {

            //Debug.Log(presets[i].teff);

            if (teff < profTeff[i]) //どの区間にあるか一つずつ走査する
            {
                lerpFactor = Mathf.InverseLerp(profTeff[i - 1], profTeff[i], teff); //現在の区間の温度とその先の温度から補間係数を計算
                returnCor = Mathf.Lerp(profVL[i - 1], profVL[i], lerpFactor);
                return returnCor;
            }
        }
        returnCor = profVL[profVL.Length - 1]; //どこにも入らなかったら最後の温度より上 = 最後の色を代入
        return returnCor;
    }

    public long GetJ2000Seconds(DateTime dateTime)
    {
        // J2000.0の基準時刻: 2000年1月1日 正午
        DateTime j2000Epoch = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // 対象時刻と基準時刻の差分(TimeSpan)を計算
        TimeSpan difference = dateTime - j2000Epoch;

        DateTime result = j2000Epoch.AddSeconds(830417395);
        Debug.Log("res:"+result);

        // TotalSecondsプロパティで秒単位の数値を取得
        return (long)difference.TotalSeconds;
    }

    public long GetJ2000Seconds(double unixSeconds)
    {
        // UnixエポックとJ2000の差（秒）
        // Unix: 1970/01/01 00:00 UTC
        // J2000: 2000/01/01 12:00 UTC
        long j2000Seconds = (long)unixSeconds - 946728000L;
        return j2000Seconds;
    }

    public long GetJ2000Seconds(DateTime dateTime, double tResolution)
    {
        // J2000.0の基準時刻: 2000年1月1日 正午
        DateTime j2000Epoch = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // 対象時刻と基準時刻の差分(TimeSpan)を計算
        TimeSpan difference = dateTime - j2000Epoch;

        // TotalSecondsプロパティで秒単位の数値を取得
        return (long)(difference.TotalSeconds * tResolution);
    }

    

    public static Color NormalizeRGB(Color color)
    {
        float magnitude = Mathf.Sqrt(color.r * color.r + color.g * color.g + color.b * color.b);

        if (magnitude == 0f)
            return new Color(0f, 0f, 0f, color.a);

        return new Color(color.r / magnitude, color.g / magnitude, color.b / magnitude, color.a);
    }

    public Color ColorScaling(Color color)
    {
        // RGBの中で最も大きい値を検索
        float maxComponent = Mathf.Max(color.r, Mathf.Max(color.g, color.b));

        // 最大値が0（完全に真っ黒）でなければ、その最大値で各成分を割る
        if (maxComponent > 0f)
        {
            // 各成分を maxComponent で割ることで、最大の成分がちょうど 1.0 になる
            return new Color(color.r / maxComponent, color.g / maxComponent, color.b / maxComponent, color.a);
        }

        // 真っ黒（0, 0, 0）の場合はそのまま黒を返す
        return Color.black;
    }

    public static Vector3 Vec3Mul(Vector3 a, Vector3 b)
    {
        return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    }

}

//数値でシリアライズされるので、追加は末尾に行う
public enum PlanetParentTypes
{
    Star,
    Planet,
    Moon,
    BaryCenter //共通重心(連星)
}

namespace StellarEngine_Bodys
{
    [System.Serializable]
    public class Star
    {
        public string name;
        public int index;
        public GameObject body;
        [Header("物理特性")]
        public string specClass;
        public double mass; //kg
        public double rad; //km
        public double teff, lum; //温度、光度
        public bool isUseSpike; //回折スパイクを使うか
        public bool isUsePSF; //回折スパイクにPSFテクスチャを使うか

        [Header("軌道 (共通重心に入っているときだけ使う)")]
        public PlanetParentTypes parentType;
        public int parentIndex = -1; //-1なら星系の原点に固定
        public double semiMajorAxis; //km。負なら重心をはさんで反対側
        public double eccentricity;
        public double argOfPeri;
        public double orbitalPeriod; //秒

        [Header("光学的要素")]
        [SerializeField] public Light lightObj;
        [SerializeField] private double appMagn; //実視等級
    }

    [System.Serializable]
    public class Planet
    {

        public string name;
        public int index;
        public GameObject body;
        public bool isUseLight;
        public bool isUseSpike; //回折スパイクを使うか
        public bool isUsePSF; //回折スパイクにPSFテクスチャを使うか
        public Texture2D texture;

        [Header("軌道特性")]
        public PlanetParentTypes parentType;
        public int parentIndex;
        public int[] lightSourceType;
        public int[] lightSourceIndex;
        public string[] lightSourceBodyName;
        public bool isLightRefSet;
        public double semiMajorAxis; //km
        public double eccentricity; //離心率
        public double inclination; //軌道傾斜角
        public double argOfPeri; //近点引数
        public double orbitalPeriod; //公転周期(秒)
        public double rotationPeriod; //自転周期(秒)
        public bool isTidalLock; //潮汐固定
        public bool isUseOcclusion; //遮蔽判定(食)を行うか
        public string[] occluderBodyName;
        public int[] occluderIndex; //遮蔽する天体(惑星番号)


        [Header("座標")]
        [SerializeField] private Vector3 position;
        [SerializeField] private Vector3 velocity;
        [SerializeField] private double meanAnomaly;

        [Header("物理特性")]
        public double mass; //kg
        public double rad; //km
        public double teff; //温度
        public double albedoBond;
        [SerializeField] private double flux; //放射束(地球比)

        [Header("光学的要素")]
        public bool useLight;
        [SerializeField] public Light lightObj;
        public Color texMeanColor;
        public double albedoGeom;
        public double hapke;
    }

    //共通重心(連星)
    [System.Serializable]
    public class BaryCenter
    {
        public string name;
        public int index;
        public PlanetParentTypes[] memberType = new PlanetParentTypes[2]; //天体A・Bのタイプ
        public int[] memberIndex = new int[] { -1, -1 }; //天体A・Bの番号(-1は見つからない)
        public double mass; //天体A・Bの質量の合計(kg、スケーリング後)
        public int rootStar = -1; //光源とみなす恒星

        [Header("連星の軌道 (天体Aと天体Bの相対軌道)")]
        public double binarySemiMajorAxis; //相対軌道長半径(km、スケーリング後)
        public double binaryEccentricity;
        public double binaryArgOfPeri;
        public double binaryPeriod; //秒

        [Header("親天体の周りの軌道")]
        public PlanetParentTypes parentType;
        public int parentIndex = -1; //-1なら星系の原点に固定
        public double semiMajorAxis; //km。負なら親の重心をはさんで反対側
        public double eccentricity;
        public double argOfPeri;
        public double orbitalPeriod; //秒
    }
}