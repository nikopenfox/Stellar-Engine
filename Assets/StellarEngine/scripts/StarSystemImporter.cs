using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using StellarEngine_Bodys;
using JetBrains.Annotations;
using static StarSystemAsset;

public class StarSystemImporter : MonoBehaviour
{
    [SerializeField] StarSystemAsset asset;
    [SerializeField] StarSystemManager manager;

    [Header("設定")]
    [SerializeField, Tooltip("天球")] private GameObject cSphereBaseObj;
    [SerializeField, Tooltip("空マテリアル")] private Material skyMaterial;
    [Tooltip("恒星モデル")][SerializeField] private GameObject starBaseObj;
    [SerializeField, Tooltip("ライトベース")] private Light lightBaseObj;
    [Tooltip("天体モデル")][SerializeField] private GameObject celestialBodyBaseObj;
    [Tooltip("光点・光芒のオブジェクト")][SerializeField] private GameObject pointObj;
    [Tooltip("回折スパイクのオブジェクト")][SerializeField] private GameObject spikeObj;
    [Tooltip("PSFを使わない天体の回折スパイクに割り当てるマテリアル(BodySpikeShader の UsePSF を OFF にしたもの)。未設定ならプレハブのまま")][SerializeField] private Material spikeMaterialNoPSF;
    [Tooltip("惑星のデフォルトテクスチャ")][SerializeField] private Texture2D celestialDefaultTex;
    [Tooltip("スケール")] [SerializeField]private double scale;
    [Tooltip("星をUnity上でどれくらいの距離に置くか")][SerializeField] private double distUnity;

    [Header("実行用中間ファイル")]
    public Star[] stars;
    public Planet[] planets;
    public BaryCenter[] barys;


    [Header("更新")]
    [SerializeField] private bool isUpdate;

    private bool isQuitting;

    //定数
    private double AU = 1.4959787E+8d;
    //private double METER = 0.001d;

    //質量の定数(kg)
    private double MASS_SOLAR = 1.9884E+30d;
    private double MASS_JUPITER = 1.8981E+27d;
    private double MASS_EARTH = 5.9722E+24d;
    private double MASS_MOON = 7.346E+22d;

    //半径の定数(km)
    private double RADIUS_SOLAR = 695700d;
    private double RADIUS_JUPITER = 69886d;
    private double RADIUS_EARTH = 6371d;
    private double RADIUS_MOON = 1737.4d;

    //公転周期の定数
    private double GRAVITY = 6.6743E-20d; //万有引力定数(km^3/kg/s^2)
    private double HOUR2SEC = 3600d;
    private double DAY2SEC = 86400d;
    private double YEAR2DAY = 365.25d;

    // Start is called before the first frame update

    void OnApplicationQuit()

    {

        isQuitting = true;

    }


#if UNITY_EDITOR

    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += _OnValidate;
    }

    void _OnValidate()
    {
        //if (!isQuitting) { return; }
        //if (!this.gameObject.scene.isLoaded) return;




        //スケーリング
        manager.scaledAU = AU * scale;

        if (!isUpdate)
        {
            return;
        }

        //天球初期化/作成
        manager.cSphere = cSphereBaseObj;

        manager.testFloat = asset.testFloat;

        //初期化

        foreach (var star in stars)
        {
            EditorApplication.delayCall += () => DestroyImmediate(star.body);
        }

        foreach (var planet in planets)
        {
            EditorApplication.delayCall += () => DestroyImmediate(planet.body);
        }

        stars = new Star[asset.starDatas.Count];
        planets = new Planet[asset.planetDatas.Count];

        Debug.Log("mofu");

        var starsTmp = new List<Star>();
        var planetsTmp = new List<Planet>();

        //初期化ここまで

        var cnt = 0;

        //恒星データを読む
        foreach (StarSystemAsset.StarData starData in asset.starDatas)
        {

            var st = new Star();

            st.name = starData.name;
            st.index = cnt;

            st.body = Instantiate(starBaseObj);
            st.body.name = starData.name;

            st.lightObj = Instantiate(lightBaseObj);
            st.lightObj.name = starData.name + "_light";
            st.lightObj.transform.SetParent(st.body.transform);

            double mass = 0, rad = 0;

            //スケーリング
            if (starData.isUseSolarUnit)
            {
                mass = starData.mass * MASS_SOLAR;
                rad = starData.rad * RADIUS_SOLAR;
            }
            else
            {
                mass = starData.mass;
                rad = starData.rad;
            }

            st.mass = mass * (scale * scale * scale); ;
            st.rad = rad * scale;

            st.teff = starData.teff;
            st.lum = starData.lum;
            st.isUseSpike = starData.isUseSpike;
            st.isUsePSF = starData.isUsePSF;

            cnt++; //配列番号を加算

            //一時保存
            starsTmp.Add(st);

        }

        cnt = 0;

        //惑星データを読む
        foreach (StarSystemAsset.PlanetData planetData in asset.planetDatas)
        {
            var pl = new Planet();

            pl.name = planetData.name;
            pl.index = cnt;
            pl.isUseLight = planetData.isUseLight;
            pl.hapke = planetData.hapke;


            if (celestialDefaultTex != null)
            {
                if (planetData.tex != null)
                {
                    pl.texture = planetData.tex;
                }
                else
                {
                    pl.texture = celestialDefaultTex;
                    Debug.LogWarning(pl.name + " のテクスチャがないため、デフォルトにセットしました");
                }
            }
            else
            {
                Debug.LogError("デフォルトテクスチャをセットしてください");
                return;
            }


            pl.body = Instantiate(celestialBodyBaseObj);
            pl.body.name = planetData.name;

            //ライトを使用する場合
            if (pl.isUseLight)
            {
                pl.lightObj = Instantiate(lightBaseObj);
                pl.lightObj.name = planetData.name + "_light";
                pl.lightObj.transform.SetParent(pl.body.transform);

            }
            pl.texMeanColor = planetData.texMeanColor; //光点の色にも使うので、ライトを使わない惑星でも設定する


            //共通重心に入っている天体は parentBody を無視し、共通重心を親にする(軌道要素はすべての天体を読んだ後に設定する)
            int memberBary = FindBaryOfMember(planetData.name);
            if (memberBary != -1)
            {
                pl.parentType = PlanetParentTypes.BaryCenter;
                pl.parentIndex = memberBary;
            }
            else
            {
                pl.parentType = SearchParent(planetData.parentBody, out pl.parentIndex);
            }

            //ライトソースの定義。未設定なら、すべての天体を読んだ後に親をたどって決める(ResolveDefaultLightSources)
            if (planetData.lightReferenceBodys == null || planetData.lightReferenceBodys.Length == 0)
            {
                pl.lightSourceBodyName = new string[0];
                pl.isLightRefSet = false;
            }
            else
            {
                pl.lightSourceBodyName = (string[])planetData.lightReferenceBodys.Clone();
                pl.isLightRefSet = true;
            }



            /*
             * 旧バージョン
            switch(pl.parentType)
            {
                case PlanetParentTypes.Star:

                    break;
            }

            //名前から主星を見つける

            pl.parentType = starsTmp.Find(st => st.name == planetData.parentBody);
            if (pl.parentType == null) 
            {
                Debug.Log(pl.name + ": 主星名「" + planetData.parentBody + "」は見つからなかったため、最初の主星をセットしました");
                pl.parentType = starsTmp[0]; 
            }

            */
            pl.argOfPeri = planetData.argOfPeri;
            pl.eccentricity = planetData.eccentricity;
            pl.inclination = planetData.inclination;

            //公転周期(秒) 0以下ならすべての天体を読んだ後に軌道長半径と質量から求める
            pl.orbitalPeriod = PeriodToSeconds(planetData.orbitalPeriod, planetData.periodInputType);

            //自転周期(秒) 0なら自転しない、負なら逆行
            pl.rotationPeriod = PeriodToSeconds(planetData.rotationPeriod, planetData.rotationPeriodInputType);
            pl.isTidalLock = planetData.isTidalLock;
            pl.isUseSpike = planetData.isUseSpike;
            pl.isUsePSF = planetData.isUsePSF;

            //遮蔽判定(食)。天体名の解決と既定値の設定は、すべての天体を読んだ後に行う(衛星の半径が要るため)
            pl.isUseOcclusion = planetData.isUseOcclusion;
            pl.occluderBodyName = planetData.occluderBodys;


            //スケーリング

            double semiMajorAxis = 0;

            switch (planetData.smaInputType)
            {
                case SMAType.au:
                    semiMajorAxis = planetData.semiMajorAxis * AU;
                    break;
                case SMAType.km:
                    semiMajorAxis = planetData.semiMajorAxis;
                    break;
                default:
                    semiMajorAxis = planetData.semiMajorAxis;
                    break;
            }

            pl.semiMajorAxis = semiMajorAxis * scale;


            double mass, rad = 0;

            switch (planetData.mrInputType)
            {
                case MassRadiusType.dontUse:

                    mass = planetData.mass;
                    rad = planetData.rad;
                    break;

                case MassRadiusType.sun:
                    mass = planetData.mass * MASS_SOLAR;
                    rad = planetData.rad * RADIUS_SOLAR;
                    break;

                case MassRadiusType.jupiter:
                    mass = planetData.mass * MASS_JUPITER;
                    rad = planetData.rad * RADIUS_JUPITER;
                    break;

                case MassRadiusType.earth:
                    mass = planetData.mass * MASS_EARTH;
                    rad = planetData.rad * RADIUS_EARTH;
                    break;

                case MassRadiusType.moon:
                    mass = planetData.mass * MASS_MOON;
                    rad = planetData.rad * RADIUS_MOON;
                    break;

                default:
                    mass = planetData.mass;
                    rad = planetData.rad;
                    break;
            }

            pl.mass = mass * (scale * scale * scale);
            pl.rad = rad * scale;

            pl.albedoBond = planetData.albedoBond;
            pl.albedoGeom = planetData.albedoGeom;

            cnt++;

            planetsTmp.Add(pl);
        }

        cnt = 0;

        //中間ファイルに保存
        stars = starsTmp.ToArray();
        planets = planetsTmp.ToArray();

        //共通重心(連星)を読み、中の天体に重心の周りの軌道を設定する(公転周期を求める前に行う)
        ReadBaryCenters();

        //公転周期が設定されていない天体は、主星の質量から求める(共通重心に入っている天体は ReadBaryCenters で設定済み)
        foreach (var planet in planets)
        {
            if (planet.orbitalPeriod <= 0 && FindBaryOfMember(planet.name) == -1)
            {
                planet.orbitalPeriod = CalcOrbitalPeriod(planet);
            }

            //潮汐固定なら自転周期 = 公転周期
            if (planet.isTidalLock)
            {
                planet.rotationPeriod = planet.orbitalPeriod;
            }
        }

        //遮蔽する天体を求める
        foreach (var planet in planets)
        {
            ResolveOccluders(planet);
        }

        //光源。未設定なら親をたどって決め、名前からタイプ(0:恒星 1:惑星)とインデックスを求める
        foreach (var planet in planets)
        {
            if (!planet.isLightRefSet)
            {
                planet.lightSourceBodyName = ResolveDefaultLightSources(planet);
            }
            ResolveLightSources(planet);
        }


        //Managerに移す

        //恒星

        //初期化
        manager.name_star = new string[stars.Length];
        manager.body_star = new GameObject[stars.Length];
        manager.mesh_star = new MeshRenderer[stars.Length];
        manager.point_star = new MeshRenderer[stars.Length];
        manager.spike_star = new MeshRenderer[stars.Length];
        manager.isUsePSF_star = new bool[stars.Length];


        manager.specClass = new string[stars.Length];
        manager.mass_star = new double[stars.Length];
        manager.rad_star = new double[stars.Length];
        manager.teff_star = new double[stars.Length];
        manager.lum = new double[stars.Length];

        manager.lightObj_star = new Light[stars.Length];
        manager.obsFlux_star = new double[stars.Length];
        manager.obsFlux_star_vis = new double[stars.Length];
        manager.obsFlux_star_surface = new double[stars.Length];
        manager.appMagn_star = new double[stars.Length];
        manager.appDiameter_star = new double[stars.Length];

        manager.position_star = new Vector3[stars.Length];
        manager.positionRelative_star = new Vector3[stars.Length];
        manager.distance_star = new float[stars.Length];
        manager.height_star = new float[stars.Length];
        manager.height_star_angle = new float[stars.Length];
        manager.view_position_starUnity = new Vector3[stars.Length];

        manager.parentType_star = new PlanetParentTypes[stars.Length];
        manager.parentIndex_star = new int[stars.Length];
        manager.semiMajorAxis_star = new double[stars.Length];
        manager.eccentricity_star = new double[stars.Length];
        manager.argOfPeri_star = new double[stars.Length];
        manager.orbitalPeriod_star = new double[stars.Length];

        foreach (var star in stars)
        {
            var i = star.index;
            manager.name_star[i] = star.name;
            manager.body_star[i] = star.body;
            manager.body_star[i].transform.SetParent(manager.cSphere.transform);
            manager.mesh_star[i] = manager.body_star[i].GetComponent<MeshRenderer>();
            manager.point_star[i] = CreateBillboard(pointObj, manager.body_star[i], "_point");
            manager.spike_star[i] = star.isUseSpike ? CreateBillboard(spikeObj, manager.body_star[i], "_spike") : null; //使わない天体はオブジェクトを作らない
            manager.isUsePSF_star[i] = star.isUsePSF;
            ApplySpikeMaterial(manager.spike_star[i], star.isUsePSF);

            manager.specClass[i] = star.specClass;
            manager.mass_star[i] = star.mass;
            manager.rad_star[i] = star.rad;
            manager.teff_star[i] = star.teff;
            manager.lum[i] = star.lum;

            manager.lightObj_star[i] = star.lightObj;

            //軌道(共通重心に入っていなければ親は -1 で、星系の原点に固定)
            manager.parentType_star[i] = star.parentType;
            manager.parentIndex_star[i] = star.parentIndex;
            manager.semiMajorAxis_star[i] = star.semiMajorAxis;
            manager.eccentricity_star[i] = star.eccentricity;
            manager.argOfPeri_star[i] = star.argOfPeri;
            manager.orbitalPeriod_star[i] = star.orbitalPeriod;

        }



        //惑星

        //初期化
        manager.name_planet = new string[planets.Length];
        manager.body_planet = new GameObject[planets.Length];
        manager.tex_planet = new Texture2D[planets.Length];

        manager.mesh_planet = new MeshRenderer[planets.Length];
        manager.mesh_planet_block = new MaterialPropertyBlock[planets.Length];
        manager.point_planet = new MeshRenderer[planets.Length];
        manager.spike_planet = new MeshRenderer[planets.Length];
        manager.isUsePSF_planet = new bool[planets.Length];

        manager.lightObj_planet = new Light[planets.Length];
        manager.lightBaseColor_planet = new Color[planets.Length];
        manager.lightIndex_planet = new int[planets.Length];

        manager.parentType_planet = new PlanetParentTypes[planets.Length];
        manager.parentIndex_planet = new int[planets.Length];
        manager.lightSourceType_planet = new int[planets.Length][];
        manager.lightSourceIndex_planet = new int[planets.Length][];
        manager.occluderIndex_planet = new int[planets.Length][];
        manager.semiMajorAxis_planet = new double[planets.Length];
        manager.eccentricity_planet = new double[planets.Length];
        manager.inclination_planet = new double[planets.Length];
        manager.argOfPeri_planet = new double[planets.Length];
        manager.orbitalPeriod_planet = new double[planets.Length];

        manager.isLanded = new bool[planets.Length];
        manager.position_planet = new Vector3[planets.Length];
        manager.positionRelative_planet = new Vector3[planets.Length];
        manager.view_position_planetUnity = new Vector3[planets.Length];
        manager.distance_planet = new double[planets.Length];
        manager.velocity_planet = new Vector3[planets.Length];
        manager.meanAnomaly_planet = new double[planets.Length];
        manager.height_planet = new float[planets.Length];
        manager.height_planet_angle = new float[planets.Length];

        manager.rotation_planet = new Quaternion[planets.Length];
        manager.rotationAxis_planet = new float[planets.Length];
        manager.rotationPeriod_planet = new double[planets.Length];
        manager.isTidalLock_planet = new bool[planets.Length];
        manager.yawing_planet = new float[planets.Length];
        manager.view_rotation_planetUnity = new Quaternion[planets.Length];

        manager.mass_planet = new double[planets.Length];
        manager.rad_planet = new double[planets.Length];
        manager.teff_planet = new double[planets.Length];
        manager.albedoBond = new double[planets.Length];
        manager.flux_planet = new double[planets.Length];
        manager.flux_planet_obs = new double[planets.Length];
        manager.flux_planet_obs_surface = new double[planets.Length];

        manager.isUseLight = new bool[planets.Length];
        manager.appDiameter_planet = new double[planets.Length];
        manager.lightObj_planet = new Light[planets.Length];
        manager.view_lightDir_planet = new Vector3[planets.Length][];
        manager.lightSourceColor_planet = new Color[planets.Length][];
        manager.lightSourceRatio_planet = new float[planets.Length][];
        manager.albedoGeom = new double[planets.Length];
        manager.hapke = new float[planets.Length];
        manager.appMagn_planet = new double[planets.Length];

        manager.scale = scale;
        manager.distUnity = distUnity;


        foreach (var planet in planets)
        {
            var i = planet.index;

            manager.name_planet[i] = planet.name;
            manager.body_planet[i] = planet.body;
            manager.body_planet[i].transform.SetParent(manager.cSphere.transform);

            manager.isUseLight[i] = planet.isUseLight;

            manager.lightObj_planet[i] = planet.lightObj;
            manager.lightBaseColor_planet[i] = planet.texMeanColor;


            manager.mesh_planet[i] = manager.body_planet[i].GetComponent<MeshRenderer>();
            manager.mesh_planet_block[i] = new MaterialPropertyBlock();
            manager.point_planet[i] = CreateBillboard(pointObj, manager.body_planet[i], "_point");
            manager.spike_planet[i] = planet.isUseSpike ? CreateBillboard(spikeObj, manager.body_planet[i], "_spike") : null; //使わない天体はオブジェクトを作らない
            manager.isUsePSF_planet[i] = planet.isUsePSF;
            ApplySpikeMaterial(manager.spike_planet[i], planet.isUsePSF);

            manager.tex_planet[i] = planet.texture;

            manager.parentType_planet[i] = planet.parentType;
            manager.parentIndex_planet[i] = planet.parentIndex;

            //光源リスト
            manager.lightSourceType_planet[i] = (int[])planet.lightSourceType.Clone();
            manager.lightSourceIndex_planet[i] = (int[])planet.lightSourceIndex.Clone();

            //遮蔽する天体
            manager.occluderIndex_planet[i] = (int[])planet.occluderIndex.Clone();

            //光源ごとの方向・色・明るさ比(値はManagerがランタイムで計算する)
            //シェーダーに渡せる数を超える光源は使わない
            int lightSourceCount = Mathf.Min(planet.lightSourceType.Length, StarSystemManager.MAX_LIGHT_SOURCE);
            if (planet.lightSourceType.Length > StarSystemManager.MAX_LIGHT_SOURCE)
            {
                Debug.LogWarning(planet.name + " の光源が" + StarSystemManager.MAX_LIGHT_SOURCE + "個を超えているため、先頭の" + StarSystemManager.MAX_LIGHT_SOURCE + "個だけを使います");
            }
            manager.view_lightDir_planet[i] = new Vector3[lightSourceCount];
            manager.lightSourceColor_planet[i] = new Color[lightSourceCount];
            manager.lightSourceRatio_planet[i] = new float[lightSourceCount];

            manager.semiMajorAxis_planet[i] = planet.semiMajorAxis;
            manager.eccentricity_planet[i] = planet.eccentricity;
            manager.argOfPeri_planet[i] = planet.argOfPeri;
            manager.orbitalPeriod_planet[i] = planet.orbitalPeriod;
            manager.rotationPeriod_planet[i] = planet.rotationPeriod;
            manager.isTidalLock_planet[i] = planet.isTidalLock;
            Debug.Log("argofperi" + planet.argOfPeri);

            manager.mass_planet[i] = planet.mass;
            manager.hapke[i] = (float)planet.hapke;
            manager.rad_planet[i] = planet.rad;
            manager.albedoGeom[i] = planet.albedoGeom;
            manager.albedoBond[i] = planet.albedoBond;

        }

        //共通重心(連星)
        manager.name_bary = new string[barys.Length];
        manager.parentType_bary = new PlanetParentTypes[barys.Length];
        manager.parentIndex_bary = new int[barys.Length];
        manager.semiMajorAxis_bary = new double[barys.Length];
        manager.eccentricity_bary = new double[barys.Length];
        manager.argOfPeri_bary = new double[barys.Length];
        manager.orbitalPeriod_bary = new double[barys.Length];
        manager.mass_bary = new double[barys.Length];
        manager.rootStar_bary = new int[barys.Length];
        manager.position_bary = new Vector3[barys.Length];

        foreach (var bary in barys)
        {
            var i = bary.index;
            manager.name_bary[i] = bary.name;
            manager.parentType_bary[i] = bary.parentType;
            manager.parentIndex_bary[i] = bary.parentIndex;
            manager.semiMajorAxis_bary[i] = bary.semiMajorAxis;
            manager.eccentricity_bary[i] = bary.eccentricity;
            manager.argOfPeri_bary[i] = bary.argOfPeri;
            manager.orbitalPeriod_bary[i] = bary.orbitalPeriod;
            manager.mass_bary[i] = bary.mass;
            manager.rootStar_bary[i] = bary.rootStar;
        }

        //公転の計算順(親が先)
        BuildOrbitOrder();

        isUpdate = false;




        //-------------------------------
        //Light Module

        //プロファイル初期化

        manager.profile_profileName = new string[profiles.Length];
        manager.profile_count = new int[profiles.Length];
        manager.profile_index = new int[profiles.Length];

        int dataCount = 0;

        foreach (var profile in profiles)
        {
            dataCount += profile.tempPresets.Length;
        }

        manager.profile_name = new string[dataCount];
        manager.profile_teff = new float[dataCount];
        manager.profile_visibleLightRatio = new float[dataCount];
        manager.profile_color = new Color[dataCount];

        //代入

        int curIdx = 0; //プロファイルの開始インデックス

        for (int i = 0; i < profiles.Length; i++)
        {

            dataCount = 0; //インデックスを記録するため初期化

            manager.profile_profileName[i] = profiles[i].name;

            //温度プリセットを読む
            for (int j = 0; j < profiles[i].tempPresets.Length; j++)
            {

                dataCount++;
                manager.profile_name[j + curIdx] = profiles[i].tempPresets[j].name;
                manager.profile_teff[j + curIdx] = profiles[i].tempPresets[j].teff;
                manager.profile_visibleLightRatio[j + curIdx] = profiles[i].tempPresets[j].visibleLightRatio;
                manager.profile_color[j + curIdx] = profiles[i].tempPresets[j].color;
            }

            manager.profile_index[i] = curIdx;
            curIdx += dataCount;
            //データ数とインデックスを記録
            manager.profile_count[i] = dataCount;


        }

        manager.skyMaterial = skyMaterial;
    }

    /// <summary>
    /// 親天体の名前からタイプとインデックスを検索する。
    /// </summary>
    /// <param name="parentName"></param>
    /// <param name="parentIndex"></param>
    /// <returns></returns>
    //入力タイプに応じて周期を秒に換算する
    private double PeriodToSeconds(double value, PeriodType type)
    {
        switch (type)
        {
            case PeriodType.hour:
                return value * HOUR2SEC;
            case PeriodType.year:
                return value * YEAR2DAY * DAY2SEC;
            default:
                return value * DAY2SEC;
        }
    }

    //ケプラーの第3法則 T = 2π√(a^3 / G(M+m)) で公転周期(秒)を求める
    //軌道長半径はscale倍、質量はscale^3倍されているので、スケーリング後の値のまま計算しても周期は変わらない
    private double CalcOrbitalPeriod(Planet planet)
    {
        //主星が共通重心なら、中の天体の質量の合計
        double parentMass = GetScaledMass(planet.parentType, planet.parentIndex, 0);
        return CalcKeplerPeriod(planet.semiMajorAxis, parentMass + planet.mass, planet.name);
    }

    //ケプラーの第3法則 T = 2π√(a^3 / GM) (aはkm、Mはkg。どちらもスケーリング後の値でよい)。求められなければ0
    private double CalcKeplerPeriod(double semiMajorAxis, double totalMass, string bodyName)
    {
        double a = System.Math.Abs(semiMajorAxis);
        if (a <= 0 || totalMass <= 0)
        {
            Debug.LogWarning(bodyName + " の公転周期を求められないため、公転させません (軌道長半径と主星の質量を確認してください)");
            return 0;
        }
        return 2 * System.Math.PI * System.Math.Sqrt(a * a * a / (GRAVITY * totalMass));
    }

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

        //共通重心(連星の周りを回る惑星)
        parentIndex = FindBaryIndex(parentName);

        if (parentIndex != -1)
        {
            isFound = true;
            return PlanetParentTypes.BaryCenter;
        }

        parentIndex = 0;

        //恒星があるか判定、なければ惑星を主星に
        if (stars.Length != 0)
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

    private int FindStarIndex(string name)
    {
        for (int i = 0; i < asset.starDatas.Count; i++)
        {
            if (asset.starDatas[i].name == name)
            {
                return i;
            }
        }

        Debug.Log(name + "は見つかりませんでした");
        return -1;
    }

    private int FindPlanetIndex(string name)
    {
        for (int i = 0; i < asset.planetDatas.Count; i++)
        {
            if (asset.planetDatas[i].name == name)
            {
                return i;
            }

        }

        Debug.Log(name + "は見つかりませんでした");
        return -1;

    }

    /// <summary>
    /// lightSourceBodyName から lightSourceType(0:恒星 1:惑星) と lightSourceIndex を求める。見つからない天体は光源から除外する。
    /// </summary>
    /// <param name="pl">対象の惑星</param>
    private void ResolveLightSources(Planet pl)
    {
        var names = new List<string>();
        var types = new List<int>();
        var indexes = new List<int>();

        foreach (var bodyName in pl.lightSourceBodyName)
        {
            int idx = FindStarIndex(bodyName);
            if (idx != -1)
            {
                names.Add(bodyName);
                types.Add((int)PlanetParentTypes.Star);
                indexes.Add(idx);
                continue;
            }

            idx = FindPlanetIndex(bodyName);
            if (idx != -1)
            {
                names.Add(bodyName);
                types.Add((int)PlanetParentTypes.Planet);
                indexes.Add(idx);
                continue;
            }

            //共通重心なら、中の恒星すべて(重い順)
            idx = FindBaryIndex(bodyName);
            if (idx != -1)
            {
                foreach (int starIdx in GetStarsInBary(idx))
                {
                    if (names.Contains(stars[starIdx].name)) { continue; } //すでに光源にある恒星は重ねない
                    names.Add(stars[starIdx].name);
                    types.Add((int)PlanetParentTypes.Star);
                    indexes.Add(starIdx);
                }
                continue;
            }

            Debug.LogWarning(pl.name + " の光源「" + bodyName + "」が見つからないため、光源から除外しました");
        }

        pl.lightSourceBodyName = names.ToArray();
        pl.lightSourceType = types.ToArray();
        pl.lightSourceIndex = indexes.ToArray();
    }

    /// <summary>
    /// 光点・光芒のビルボード(prefab)を天体本体の子として作り、MeshRenderer を返す。prefab が未設定なら null。
    /// 本体と一緒に破棄・非表示になる。位置と大きさはシェーダー側で決めるので、ローカル座標は原点にする。
    /// </summary>
    /// <param name="prefab">pointObj または spikeObj</param>
    /// <param name="body">天体本体</param>
    /// <param name="suffix">オブジェクト名の末尾("_point" など)</param>
    private MeshRenderer CreateBillboard(GameObject prefab, GameObject body, string suffix)
    {
        if (prefab == null) { return null; }

        var obj = Instantiate(prefab, body.transform, false);
        obj.name = body.name + suffix;
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        var mesh = obj.GetComponent<MeshRenderer>();
        if (mesh == null) { Debug.LogWarning("ビルボードのオブジェクトに MeshRenderer がありません: " + prefab.name); }
        return mesh;
    }

    /// <summary>
    /// PSFを使わない天体の回折スパイクに、テクスチャを使わないバリアントのマテリアル(spikeMaterialNoPSF)を割り当てる。
    /// PSFを使う天体はプレハブのマテリアル(UsePSF を ON にしたもの)のままにする。
    /// </summary>
    private void ApplySpikeMaterial(MeshRenderer spike, bool isUsePSF)
    {
        if (spike == null || isUsePSF || spikeMaterialNoPSF == null) { return; }
        spike.sharedMaterial = spikeMaterialNoPSF;
    }

    /// <summary>
    /// occluderBodyName から遮蔽する天体(惑星番号)を求める。
    /// 未設定なら、衛星は主星、惑星は半径が最大の衛星を使う。isUseOcclusion が false なら空にする。
    /// </summary>
    /// <param name="pl">対象の惑星</param>
    private void ResolveOccluders(Planet pl)
    {
        var indexes = new List<int>();

        if (!pl.isUseOcclusion)
        {
            pl.occluderIndex = indexes.ToArray();
            return;
        }

        if (pl.occluderBodyName != null && pl.occluderBodyName.Length > 0)
        {
            foreach (var bodyName in pl.occluderBodyName)
            {
                int idx = FindPlanetIndex(bodyName);
                if (idx == -1 || idx == pl.index)
                {
                    Debug.LogWarning(pl.name + " の遮蔽天体「" + bodyName + "」が惑星・衛星に見つからないため、除外しました");
                    continue;
                }
                indexes.Add(idx);
            }
        }
        else if (pl.parentType == PlanetParentTypes.Planet)
        {
            //衛星: 主星
            if (pl.parentIndex != pl.index) { indexes.Add(pl.parentIndex); }
        }
        else
        {
            //惑星: 半径が最大の衛星
            int largest = -1;
            double largestRad = 0;
            foreach (var moon in planets)
            {
                if (moon.parentType != PlanetParentTypes.Planet || moon.parentIndex != pl.index || moon.index == pl.index) { continue; }
                if (largest == -1 || moon.rad > largestRad)
                {
                    largest = moon.index;
                    largestRad = moon.rad;
                }
            }
            if (largest != -1) { indexes.Add(largest); }
        }

        pl.occluderIndex = indexes.ToArray();
    }

    //---------------- 共通重心(連星) ----------------

    /// <summary>
    /// 共通重心(連星)を読み、中の天体A・Bに重心の周りの軌道を設定する。
    /// 天体A・Bは元の parentBody と軌道要素を無視して重心を親にする。重心からの軌道長半径は質量比で分け(天体Aは負にして、重心をはさんで天体Bの反対側に置く)、
    /// 離心率・近点引数は重心の設定を使い、公転周期は天体A・Bの質量の合計と相対軌道長半径からケプラーの第3法則で求める。
    /// </summary>
    private void ReadBaryCenters()
    {
        int count = asset.baryCenterDatas.Count;
        barys = new BaryCenter[count];

        //名前・天体A・B・親を解決し、軌道要素を換算する
        for (int i = 0; i < count; i++)
        {
            var data = asset.baryCenterDatas[i];
            var bary = new BaryCenter();
            bary.name = data.name;
            bary.index = i;

            string[] memberNames = { data.bodyA, data.bodyB };
            for (int k = 0; k < 2; k++)
            {
                PlanetParentTypes type;
                int index;
                if (FindBody(memberNames[k], out type, out index) && !(type == PlanetParentTypes.BaryCenter && index == i))
                {
                    bary.memberType[k] = type;
                    bary.memberIndex[k] = index;
                }
                else
                {
                    Debug.LogWarning("共通重心「" + data.name + "」の天体「" + memberNames[k] + "」が見つからないため、無視しました");
                }
            }

            //親: 別の共通重心に入っていればその重心、そうでなければ parentBody(空なら星系の原点)
            int memberBary = FindBaryOfMember(data.name);
            if (memberBary != -1 && memberBary != i)
            {
                bary.parentType = PlanetParentTypes.BaryCenter;
                bary.parentIndex = memberBary;
            }
            else if (!string.IsNullOrEmpty(data.parentBody))
            {
                PlanetParentTypes type;
                int index;
                if (FindBody(data.parentBody, out type, out index) && !(type == PlanetParentTypes.BaryCenter && index == i))
                {
                    bary.parentType = type;
                    bary.parentIndex = index;
                }
                else
                {
                    Debug.LogWarning("共通重心「" + data.name + "」の親天体「" + data.parentBody + "」が見つからないため、星系の原点に置きます");
                }
            }

            //親の周りの軌道(別の共通重心に入っているときは、後で上書きする)
            bary.semiMajorAxis = ToKm(data.parentSemiMajorAxis, data.parentSmaInputType) * scale;
            bary.eccentricity = data.parentEccentricity;
            bary.argOfPeri = data.parentArgOfPeri;
            bary.orbitalPeriod = PeriodToSeconds(data.parentOrbitalPeriod, data.parentPeriodInputType);

            //連星の軌道(天体Aと天体Bの相対軌道)
            bary.binarySemiMajorAxis = ToKm(data.relativeSemiMajorAxis, data.smaInputType) * scale;
            bary.binaryEccentricity = data.eccentricity;
            bary.binaryArgOfPeri = data.argOfPeri;

            barys[i] = bary;
        }

        //質量(天体A・Bの合計。共通重心の入れ子は再帰で合計する)
        foreach (var bary in barys)
        {
            bary.mass = GetScaledMass(PlanetParentTypes.BaryCenter, bary.index, 0);
        }

        //天体A・Bに重心の周りの軌道を設定する
        foreach (var bary in barys)
        {
            double massA = GetScaledMass(bary.memberType[0], bary.memberIndex[0], 0);
            double massB = GetScaledMass(bary.memberType[1], bary.memberIndex[1], 0);
            double total = massA + massB;
            double a = bary.binarySemiMajorAxis;

            bary.binaryPeriod = CalcKeplerPeriod(a, total, bary.name);

            //重心からの軌道長半径。天体Aは負にして、重心をはさんで天体Bの反対側に置く
            double smaA = total > 0 ? -a * massB / total : 0;
            double smaB = total > 0 ? a * massA / total : 0;
            SetMemberOrbit(bary, 0, smaA);
            SetMemberOrbit(bary, 1, smaB);
        }

        //親の周りの公転周期が0なら、親と重心の質量から求める(別の共通重心に入っている重心は設定済み)
        foreach (var bary in barys)
        {
            if (bary.parentIndex < 0 || bary.orbitalPeriod > 0 || FindBaryOfMember(bary.name) != -1) { continue; }
            double parentMass = GetScaledMass(bary.parentType, bary.parentIndex, 0);
            bary.orbitalPeriod = CalcKeplerPeriod(bary.semiMajorAxis, parentMass + bary.mass, bary.name);
        }

        //光源とみなす恒星(中の最も重い恒星。恒星がなければ親をたどった先の恒星)
        foreach (var bary in barys)
        {
            bary.rootStar = FindRootStar(PlanetParentTypes.BaryCenter, bary.index, 0);
        }
    }

    //共通重心の天体A(k=0)・天体B(k=1)に、重心の周りの軌道を設定する
    private void SetMemberOrbit(BaryCenter bary, int k, double semiMajorAxis)
    {
        int idx = bary.memberIndex[k];
        if (idx < 0) { return; }

        switch (bary.memberType[k])
        {
            case PlanetParentTypes.Star:
                var star = stars[idx];
                star.parentType = PlanetParentTypes.BaryCenter;
                star.parentIndex = bary.index;
                star.semiMajorAxis = semiMajorAxis;
                star.eccentricity = bary.binaryEccentricity;
                star.argOfPeri = bary.binaryArgOfPeri;
                star.orbitalPeriod = bary.binaryPeriod;
                break;

            case PlanetParentTypes.Planet:
                var planet = planets[idx];
                planet.parentType = PlanetParentTypes.BaryCenter;
                planet.parentIndex = bary.index;
                planet.semiMajorAxis = semiMajorAxis;
                planet.eccentricity = bary.binaryEccentricity;
                planet.argOfPeri = bary.binaryArgOfPeri;
                planet.orbitalPeriod = bary.binaryPeriod;
                break;

            case PlanetParentTypes.BaryCenter:
                var inner = barys[idx];
                inner.parentType = PlanetParentTypes.BaryCenter;
                inner.parentIndex = bary.index;
                inner.semiMajorAxis = semiMajorAxis;
                inner.eccentricity = bary.binaryEccentricity;
                inner.argOfPeri = bary.binaryArgOfPeri;
                inner.orbitalPeriod = bary.binaryPeriod;
                break;
        }
    }

    //軌道長半径を入力タイプに応じてkmに換算する(スケーリング前)
    private double ToKm(double value, SMAType type)
    {
        return type == SMAType.au ? value * AU : value;
    }

    //天体のスケーリング後の質量(kg)。共通重心は中の天体の合計。見つからなければ0
    private double GetScaledMass(PlanetParentTypes type, int index, int depth)
    {
        if (index < 0) { return 0; }
        switch (type)
        {
            case PlanetParentTypes.Star:
                return index < stars.Length ? stars[index].mass : 0;
            case PlanetParentTypes.Planet:
                return index < planets.Length ? planets[index].mass : 0;
            case PlanetParentTypes.BaryCenter:
                //循環参照で無限に再帰しないよう、重心の数で打ち切る
                if (barys == null || index >= barys.Length || depth > barys.Length) { return 0; }
                var bary = barys[index];
                return GetScaledMass(bary.memberType[0], bary.memberIndex[0], depth + 1)
                     + GetScaledMass(bary.memberType[1], bary.memberIndex[1], depth + 1);
        }
        return 0;
    }

    //天体名が天体A・Bとして入っている共通重心の番号。入っていなければ -1
    private int FindBaryOfMember(string bodyName)
    {
        if (string.IsNullOrEmpty(bodyName)) { return -1; }
        for (int i = 0; i < asset.baryCenterDatas.Count; i++)
        {
            var data = asset.baryCenterDatas[i];
            if (data.bodyA == bodyName || data.bodyB == bodyName) { return i; }
        }
        return -1;
    }

    //共通重心の名前から番号を返す。見つからなければ -1
    private int FindBaryIndex(string name)
    {
        if (string.IsNullOrEmpty(name)) { return -1; }
        for (int i = 0; i < asset.baryCenterDatas.Count; i++)
        {
            if (asset.baryCenterDatas[i].name == name) { return i; }
        }
        return -1;
    }

    //天体名から、タイプ(恒星・惑星・共通重心)と番号を求める。見つからなければ false
    private bool FindBody(string name, out PlanetParentTypes type, out int index)
    {
        type = PlanetParentTypes.Star;
        index = -1;
        if (string.IsNullOrEmpty(name)) { return false; }

        for (int i = 0; i < asset.starDatas.Count; i++)
        {
            if (asset.starDatas[i].name == name) { type = PlanetParentTypes.Star; index = i; return true; }
        }
        for (int i = 0; i < asset.planetDatas.Count; i++)
        {
            if (asset.planetDatas[i].name == name) { type = PlanetParentTypes.Planet; index = i; return true; }
        }
        index = FindBaryIndex(name);
        if (index != -1) { type = PlanetParentTypes.BaryCenter; return true; }
        return false;
    }

    //共通重心の中の恒星(入れ子の重心も含む)を、重い順に返す
    private List<int> GetStarsInBary(int baryIndex)
    {
        var result = new List<int>();
        CollectStarsInBary(baryIndex, result, 0);
        result.Sort((x, y) => stars[y].mass.CompareTo(stars[x].mass));
        return result;
    }

    private void CollectStarsInBary(int baryIndex, List<int> result, int depth)
    {
        if (barys == null || baryIndex < 0 || baryIndex >= barys.Length || depth > barys.Length) { return; }
        var bary = barys[baryIndex];
        for (int k = 0; k < 2; k++)
        {
            int idx = bary.memberIndex[k];
            if (idx < 0) { continue; }
            if (bary.memberType[k] == PlanetParentTypes.Star && !result.Contains(idx)) { result.Add(idx); }
            else if (bary.memberType[k] == PlanetParentTypes.BaryCenter) { CollectStarsInBary(idx, result, depth + 1); }
        }
    }

    //光源とみなす恒星を親をたどって求める。共通重心は中の最も重い恒星(なければさらに親をたどる)。見つからなければ -1
    private int FindRootStar(PlanetParentTypes type, int index, int depth)
    {
        if (index < 0 || depth > stars.Length + planets.Length + barys.Length) { return -1; }
        switch (type)
        {
            case PlanetParentTypes.Star:
                return index;
            case PlanetParentTypes.Planet:
                return FindRootStar(planets[index].parentType, planets[index].parentIndex, depth + 1);
            case PlanetParentTypes.BaryCenter:
                var inner = GetStarsInBary(index);
                if (inner.Count > 0) { return inner[0]; }
                return FindRootStar(barys[index].parentType, barys[index].parentIndex, depth + 1);
        }
        return -1;
    }

    /// <summary>
    /// lightReferenceBodys が未設定の惑星の光源を、親をたどって決める。
    /// 恒星に着いたらその恒星、共通重心に着いたら中の恒星すべて(重い順)を光源にする。途中の惑星(衛星から見た主星など)も、上の階層から順に光源に加える。
    /// </summary>
    private string[] ResolveDefaultLightSources(Planet pl)
    {
        var starNames = new List<string>();
        var planetNames = new List<string>(); //上の階層が先
        PlanetParentTypes type = pl.parentType;
        int index = pl.parentIndex;

        //循環参照で無限ループしないよう、天体の数で打ち切る
        int limit = stars.Length + planets.Length + barys.Length + 1;
        for (int n = 0; n < limit && index >= 0; n++)
        {
            if (type == PlanetParentTypes.Star)
            {
                if (index < stars.Length) { starNames.Add(stars[index].name); }
                break;
            }

            if (type == PlanetParentTypes.Planet)
            {
                if (index >= planets.Length) { break; }
                planetNames.Insert(0, planets[index].name);
                type = planets[index].parentType;
                index = planets[index].parentIndex;
                continue;
            }

            if (type == PlanetParentTypes.BaryCenter)
            {
                if (index >= barys.Length) { break; }
                var inner = GetStarsInBary(index);
                if (inner.Count > 0)
                {
                    foreach (int starIdx in inner) { starNames.Add(stars[starIdx].name); }
                    break;
                }
                //恒星のない重心(二重惑星など)は、さらに親をたどる
                type = barys[index].parentType;
                index = barys[index].parentIndex;
                continue;
            }

            break;
        }

        starNames.AddRange(planetNames);
        return starNames.ToArray();
    }

    /// <summary>
    /// 公転の計算順(親が先)を作り、Manager の orbitOrderType / orbitOrderIndex に入れる。
    /// 恒星・惑星・共通重心を、親をたどって深さ優先で並べる。親子関係が循環していれば警告する。
    /// </summary>
    private void BuildOrbitOrder()
    {
        var stateStar = new int[stars.Length]; //0:未訪問 1:訪問中 2:済み
        var statePlanet = new int[planets.Length];
        var stateBary = new int[barys.Length];
        var types = new List<int>();
        var indexes = new List<int>();

        for (int i = 0; i < barys.Length; i++) { VisitOrbit(PlanetParentTypes.BaryCenter, i, stateStar, statePlanet, stateBary, types, indexes); }
        for (int i = 0; i < stars.Length; i++) { VisitOrbit(PlanetParentTypes.Star, i, stateStar, statePlanet, stateBary, types, indexes); }
        for (int i = 0; i < planets.Length; i++) { VisitOrbit(PlanetParentTypes.Planet, i, stateStar, statePlanet, stateBary, types, indexes); }

        manager.orbitOrderType = types.ToArray();
        manager.orbitOrderIndex = indexes.ToArray();
    }

    private void VisitOrbit(PlanetParentTypes type, int index, int[] stateStar, int[] statePlanet, int[] stateBary, List<int> types, List<int> indexes)
    {
        int[] state = null;
        PlanetParentTypes parentType = PlanetParentTypes.Star;
        int parentIndex = -1;
        string bodyName = "";

        switch (type)
        {
            case PlanetParentTypes.Star:
                state = stateStar;
                if (index >= 0 && index < stars.Length) { parentType = stars[index].parentType; parentIndex = stars[index].parentIndex; bodyName = stars[index].name; }
                break;
            case PlanetParentTypes.Planet:
                state = statePlanet;
                if (index >= 0 && index < planets.Length) { parentType = planets[index].parentType; parentIndex = planets[index].parentIndex; bodyName = planets[index].name; }
                break;
            case PlanetParentTypes.BaryCenter:
                state = stateBary;
                if (index >= 0 && index < barys.Length) { parentType = barys[index].parentType; parentIndex = barys[index].parentIndex; bodyName = barys[index].name; }
                break;
        }

        if (state == null || index < 0 || index >= state.Length || state[index] == 2) { return; }
        if (state[index] == 1)
        {
            Debug.LogWarning("公転の親子関係が循環しています: " + bodyName);
            return;
        }

        state[index] = 1;
        VisitOrbit(parentType, parentIndex, stateStar, statePlanet, stateBary, types, indexes); //親を先に並べる
        state[index] = 2;

        types.Add((int)type);
        indexes.Add(index);
    }

#endif


    //-------------------------------
    //Light Module

    [SerializeField] private ObserverProfile[] profiles;
    


    // Update is called once per frame
    void Update()
    {
        
    }
}
