
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

public class StarSystemManager : UdonSharpBehaviour
{
    private const float MIN_F = 1e-30f;　//floatの最小値を入れたいときの値
    private const int BASEQUEUE = 2510; //最近傍天体のRenderQueue
    private const float BASEDEPTH = 0.0001f; //最遠天体の深度値
    private const float BASEDEPTH_INC = 0.00015f;

    public double scale, scaledAU;
    public double distUnity;
    
    [SerializeField] private Vector3 bodys_baseRotation;
    private Quaternion bodys_baseRotationQ;
    [HideInInspector] public GameObject cSphere;
    [Header("設定")]
    [SerializeField] private bool isRealTimeMode;
    [SerializeField] private float updateInterval, lightUpdateInterval; //何秒ごとに更新するか
    private float time, L_time;

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
    private bool isSyncRun, isOwner; //オーナー用
    private bool isSynced; //他のプレイヤー用
    [SerializeField, UdonSynced] private float onr_rotationAxis;
    [SerializeField, UdonSynced] private float onr_latitude;
    [SerializeField, UdonSynced] private float onr_longitude;
    [SerializeField, UdonSynced] private float onr_yawing;

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
    public double ext_teff_star, ext_lum; //温度、光度

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
    [HideInInspector] private GameObject[] point_star;
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

    [Header("軌道特性")]
     public double[] semiMajorAxis_planet; //km
    [HideInInspector] public double[] eccentricity_planet; //離心率
    [HideInInspector] public double[] inclination_planet; //軌道傾斜角
    [HideInInspector] public double[] argOfPeri_planet; //近点引数

    [Header("自転のパラメータ")]
    [HideInInspector] public Quaternion[] rotation_planet;
    [HideInInspector] public float[] rotationAxis_planet;
    [HideInInspector] public float[] rotationPeriod_planet;
    [HideInInspector] public float[] yawing_planet;

    [Header("座標")]
    [HideInInspector] public PlanetParentTypes[] parentType_planet;
    [HideInInspector] public int[] parentIndex_planet;
    [HideInInspector] public int[][] lightSourceType_planet;
    [HideInInspector] public int[][] lightSourceIndex_planet;
    [HideInInspector] public bool[] isLanded; 
    [HideInInspector] public Vector3[] position_planet;
    [HideInInspector] public Vector3[] positionRelative_planet;
    [HideInInspector] public Vector3[] velocity_planet;
    [HideInInspector] public double[] distance_planet;
    [HideInInspector] public double[] meanAnomaly_planet;
    [HideInInspector] public float[] height_planet, height_planet_angle;


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
    [HideInInspector] public Vector3[] view_lightDir_planet;
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

        UpdateStar();
        UpdatePlanetLightning();
        UpdateLight();

        AtmosphereInit();
        CloudInit();
        StarMapInit();

        //---------------------
        sd_res = 1000L;

        nowDate = GetJ2000Seconds(DateTime.Now);
        sDate = (long)nowDate * sd_res;

        //Debug.Log("now: " + nowDate);
    }

    

    private void FixedUpdate()
    {
        STimeUpdate();
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

        

        RotationSet();

        if (isUseSystemMap)
        {
            StarSystemMapUpdate();
        }

        
        PlanetPositionUpdate();
        UpdatePlanet();

        UpdateStar();

        

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
        UpdatePlanetLightning();
        UpdateLight();

        if (isUseAtmosphere)
        {
            UpdateAtmosphere();
        }

        UpdateCloud();
        UpdateStarMap();

    }

    //同期処理初期化
    public void InitSync()
    {

        if (Networking.IsMaster)
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        else
        {
            
        }
    }

    public void RunSync()
    {
        if (Networking.IsOwner(gameObject)) { return; }


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
        sDate += sd_res / (long)(1 / Time.deltaTime);

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
            position_star[i] = new Vector3(0,0,0);

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

    public void PlanetPositionUpdate()
    {
        for (int i = 0; i < name_planet.Length; i++)
        {

            Vector3 parentPos = Vector3.zero;

            switch (parentType_planet[i])
            {
                case PlanetParentTypes.Star:
                    parentPos = position_star[parentIndex_planet[i]];
                    break;
                case PlanetParentTypes.Planet:
                    parentPos = position_planet[parentIndex_planet[i]];
                    break;
                case PlanetParentTypes.Moon:
                    break;
            }

            position_planet[i] = new Vector3(Mathf.Cos(Mathf.Deg2Rad * (float)argOfPeri_planet[i]) * (float)semiMajorAxis_planet[i] + parentPos.x,
                                             Mathf.Sin(Mathf.Deg2Rad * (float)argOfPeri_planet[i]) * (float)semiMajorAxis_planet[i] + parentPos.y, 0);

            rotation_planet[i] = Quaternion.Euler(rotationAxis_planet[i], 0, 0);

        }
    }

    public void UpdatePlanet()
    {
        for (int i = 0;i < name_planet.Length;i++)
        {
            //着陸時の処理
            if (isLanded[i])
            {
                body_planet[i].SetActive(false);

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
                L_airmass[lightIndex_planet[i]] = 1f;

                L_exposureness[lightIndex_planet[i]] = 0f;

                L_isLanded[lightIndex_planet[i]] = true;

                //Debug.Log("landed:" + body_planet[obs_body_index].name);

                continue;
            }

            //if (position_planet[i] == obs_position) { continue; }

            body_planet[i].SetActive(true);

            positionRelative_planet[i] = position_planet[i] - obs_position; //相対位置を求める


            distance_planet[i] = positionRelative_planet[i].magnitude;

            //視直径を求める

            float dRatio = (float)rad_planet[i] / (float)distance_planet[i];

            appDiameter_planet[i] = 2f * Mathf.Rad2Deg * Mathf.Asin(dRatio);

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

            double distFromStar = (position_star[parentIndex_planet[i]] - position_planet[i]).magnitude / scaledAU;

            //Debug.Log(body_planet[i].name + ", distFromStar:" + distFromStar);
            flux_planet[i] = lum[parentIndex_planet[i]] / (distFromStar * distFromStar) * (1 - albedoGeom[i]);
            //Debug.Log(name_planet[i] + " " + flux_planet[i]);

            float phase = 1;


            //太陽→惑星
            var stp = (position_planet[i] - position_star[parentIndex_planet[i]]).normalized;
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
            height_planet_angle[i] = (180 / Mathf.PI) * Mathf.Asin(height_planet[i]);

            var airmass = CalcAirMass(height_planet[i], appDiameter_planet[i], false);
            //大気密度による地平線下での散乱強さ
            float scatterDensity = atmOpacity * atmOpacity;
            float exposureness = CalcExposureness(height_planet_angle[i], appDiameter_planet[i], 0f);
            float surfaceFlux = (float)CalcFluxWithAirMass(flux_planet_obs[i], airmass, 0.15);

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
                L_airmass[lightIndex_planet[i]] = (float)airmass;
                L_lightColorBase[lightIndex_planet[i]] = lightBaseColor_planet[i];

                L_exposureness[lightIndex_planet[i]] = exposureness;

                L_isLanded[lightIndex_planet[i]] = false;
            }


            appMagn_planet[i] = CalcVisMagnitude(flux_planet_obs[i]);

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

            Vector4 lv = view_surfaceQUnity * new Vector4(view_lightDir_planet[i].x, view_lightDir_planet[i].z, view_lightDir_planet[i].y, 0);
            mesh_planet_block[i].SetFloat("_hapke", hapke[i]);
            mesh_planet_block[i].SetTexture("_MainTex", tex_planet[i]);

            mesh_planet[i].SetPropertyBlock(mesh_planet_block[i]);
        }
    }

    

    public void UpdateStar()
    {
        for (int i = 0; i < name_star.Length; i++)
        {

            position_star[i] = new Vector3(0, 0, 0);

            positionRelative_star[i] = position_star[i] - obs_position; //相対位置を求める



            var distCor = (float)distUnity / positionRelative_star[i].magnitude; //距離正規化のための補正値


            view_position_starUnity[i] = view_surfaceQUnity * new Vector3(positionRelative_star[i].x * (float)distCor,
                                                positionRelative_star[i].z * (float)distCor,
                                                positionRelative_star[i].y * (float)distCor);

            body_star[i].transform.position = cSphere.transform.position + view_position_starUnity[i];

            body_star[i].transform.localScale = new Vector3((float)rad_star[i], (float)rad_star[i], (float)rad_star[i]) * distCor;

            //視直径
            float dRatio = (float)rad_star[i] / (float)distance_star[i];

            appDiameter_star[i] = 2f * Mathf.Rad2Deg * Mathf.Asin(dRatio);


            var scaledAuPos = positionRelative_star[i] / (float)scaledAU;
            distance_star[i] = scaledAuPos.magnitude;
            obsFlux_star[i] =  lum[i] / (distance_star[i] * distance_star[i]);
            obsFlux_star_vis[i] = obsFlux_star[i] * TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], (float)teff_star[i]);
            appMagn_star[i] = CalcVisMagnitude(obsFlux_star_vis[i]);


            //高度の計算
            var ots = (position_star[i] - obs_position).normalized;
            //ots = new Vector3 (ots.x, ots.z, ots.y);
            sunVecObj.rotation = Quaternion.LookRotation(ots);
            height_star[i] = Vector3.Dot(surfaceVec, ots);
            height_star_angle[i] = (180 / Mathf.PI) * Mathf.Asin(height_star[i]);
            //Debug.Log("ots: " + ots + " surface: " + surfaceVec);

            var airmass = CalcAirMass(height_star[i], appDiameter_star[i], false);

            //大気密度による地平線下での散乱強さ
            float scatterDensity = atmOpacity * atmOpacity;
            float exposureness = CalcExposureness(height_star_angle[i], appDiameter_star[i], 0f);
            float surfaceFlux = (float)CalcFluxWithAirMass(obsFlux_star_vis[i], airmass, 0.15);

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
            L_airmass[i] = (float)airmass;
            L_absLum[i] = (float)lum[i];
            L_dist[i] = distance_star[i];

            L_exposureness[i] = exposureness;


            //Debug.Log("obsFlux: " + obsFlux_star[i]);
        }
    }

    private void UpdatePlanetLightning()
    {
        for (int i = 0; i < name_planet.Length;i++)
        {
            //恒星の方向を求める
            view_lightDir_planet[i] = position_star[parentIndex_planet[i]] - position_planet[i];

            

            //Debug.Log("meshBlock: " + mesh_planet_block[i]);

            //ブロック取得
            mesh_planet[i].GetPropertyBlock(mesh_planet_block[i]);

            Vector4 lv = view_surfaceQUnity * new Vector4(view_lightDir_planet[i].x, view_lightDir_planet[i].z, view_lightDir_planet[i].y, 0); 
            mesh_planet_block[i].SetVector("_lightVector", lv);

            mesh_planet[i].SetPropertyBlock(mesh_planet_block[i]);

        }

        

        //view_lightDir_planet[0] = position_star[parentBodyIdx[0]] - position_planet[0];
        //body_planet[0].

    }

    private void StarSystemMapUpdate()
    {

        Vector3 obsMarkerPos = (systemMap_Base.position - systemMap_obsMarker.position) * systemMap_scale;


        if (isDebug && obs_body_index == -1)
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
        ext_lum = lum[star];
        ext_lightObj_star = lightObj_star[star];
        ext_obsFlux_star = obsFlux_star[star];
        ext_obsFlux_star_vis = obsFlux_star_vis[star];
        ext_obsFlux_star_surface = obsFlux_star_surface[star];
        ext_height_star = height_star[star];
        ext_height_star_angle = height_star_angle[star];
        ext_appMagn_star = appMagn_star[star];
        ext_appDiameter_star = appDiameter_star[star];
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
        float r = (float)300;
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

    //天体高度を地表照度に変換
    private double CalcFluxWithAirMass(double flux, double airmass, double optThick)
    {
        var am = airmass;

        return Mathf.Clamp((float)flux * Mathf.Exp((float)-optThick * (float)am), 0, (float)flux);
    }

    private float CalcExposureness(double height, double diameter, double horizonHeight)
    {
        return Mathf.Clamp01((float)height  / (float)diameter + 0.5f);
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
    [SerializeField] bool[] L_isLanded;
    [SerializeField] float[] L_fluxBol, L_absLum, L_absLum_v;
    [SerializeField] float[] L_flux;
    [SerializeField] float[] L_flux_light; //位相なしのフラックス
    [SerializeField] float[] L_flux_surface; //位相込みのフラックス(地上)
    [SerializeField] float[] L_lightPhase; //天体の位相(恒星は常に1)
    [SerializeField] float[] L_airmass;
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
        L_flux = new float[lightNum];
        L_flux_light = new float[lightNum];
        L_flux_surface = new float[lightNum];
        L_lightPhase = new float[lightNum];
        L_airmass = new float[lightNum];
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
                var rey = GetReyleighColor(L_airmass[i], 0.35f);
                L_lightObj[i].color = col * rey;
                L_lightColor[i] = col * rey;
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
                var rey = GetReyleighColor(L_airmass[i], 0.35f);
                Color baseCol = ColorScaling(L_lightColorBase[i] * rey);
                Color col = ColorScaling(Color.white * rey);
                L_lightColor[i] = baseCol * rey;
                L_lightObj[i].color = baseCol * rey;
                L_matBlock_planet[pi].SetColor("_Color", col * rey);
                //L_lightMesh_planet[pi].GetPropertyBlock(L_matBlock_planet[pi]);

                //L_matBlock_planet
            }
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
        intensityAll = intensityBase * LogScaling(fluxAllSurface);

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

            L_lightObj[i].intensity = intensityAll * L_fluxRatio_surface[i] * belowCor * L_exposureness[i];


            //ブロック取得
            //恒星のとき
            if (i < planetLightStartIdx)
            {
                

                L_matBlock_star[i].SetFloat("_intensity", sunBrightness * LogScaling(L_flux_surface[i]) *
                    LogScaling(Mathf.Clamp(L_fluxRatio_surface[i] * Mathf.Pow(L_fluxRatio_surface[i], L_fluxRatio_surface[i] - dynamicRange), 0, 100))
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

                L_matBlock_planet[pi].SetFloat("_brightness2", 1 * LogScaling(L_flux_light[i]) *
                    LogScaling(Mathf.Clamp(L_fluxRatio_light[i] * Mathf.Pow(L_fluxRatio_light[i], L_fluxRatio_light[i] - dynamicRange), 0, 100))
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

        atmColorTint = Color.Lerp(L_meanLightColor, L_ambColorBase_sky, ambLightBrightness / 2) * Mathf.Clamp01(GetBelow(fluxAllSurface_lx)); ;

        
        var scaledFlux = LogScaling(fluxAllSurface);
        var brightnessCor = scaledFlux * LogScaling(ambLightBrightness) * LogScaling(Mathf.Clamp01(atmOpacity * atmOpacity)); //明るさ補正

        //環境光の色を変える

        RenderSettings.ambientSkyColor = L_ambColorBase_sky * atmColorTint * brightnessCor;
        RenderSettings.ambientEquatorColor = L_ambColorBase_equator * atmColorTint * brightnessCor;
        RenderSettings.ambientGroundColor = L_ambColorBase_ground * atmColorTint * brightnessCor;
        RenderSettings.fogColor = Color.Lerp(L_meanLightColor, L_ambColorBase_equator, 0.8f) * atmColorTint * brightnessCor;
        RenderSettings.fogDensity = 0.01f * atmHaze;

        if (atmOpacity <= 0) {
            RenderSettings.skybox.SetFloat("_exposure", 0f);
            return; 
        }

        //大気
        belowCor = GetBelowT(fluxAllSurface_lx, thresholdLx * 15);
        float skyBrightness = skyLightBase * scaledFlux * Mathf.Clamp01(belowCor);
        RenderSettings.skybox.SetVector("_LightVector", currentLightVec);
        RenderSettings.skybox.SetFloat("_Brightness", skyBrightness);
        RenderSettings.skybox.SetColor("_LightColor", L_meanLightColor);

        RenderSettings.skybox.SetFloat("_opacity", (atmOpacity - 1) * 0.1f);

        float scatterBase = 0.3f, scatterFrac = 0.3f;
        RenderSettings.skybox.SetFloat("_fwdScatterMultiplier", scatterBase + (atmOpacity - 1) * scatterFrac);
        RenderSettings.skybox.SetFloat("_haze", atmHaze);
        RenderSettings.skybox.SetFloat("_exposure", Mathf.Clamp01(LogScaling(atmOpacity * atmOpacity)));

        

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

        starMapMat.SetFloat("_brightness", 1 * (intensityAll * atmCor / intensityBase));
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

public enum PlanetParentTypes
{
    Star,
    Planet,
    Moon
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
}