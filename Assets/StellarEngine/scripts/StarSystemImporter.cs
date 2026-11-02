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
    [Tooltip("光点のオブジェクト")][SerializeField] private GameObject pointObj;
    [Tooltip("惑星のデフォルトテクスチャ")][SerializeField] private Texture2D celestialDefaultTex;
    [Tooltip("スケール")] [SerializeField]private double scale;
    [Tooltip("星をUnity上でどれくらいの距離に置くか")][SerializeField] private double distUnity;

    [Header("実行用中間ファイル")]
    public Star[] stars;
    public Planet[] planets;


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


            pl.parentType = SearchParent(planetData.parentBody, out pl.parentIndex);

            //ライトソースの定義
            //BaryCenterの場合は手動セットが必要
            if (planetData.lightReferenceBodys == null || planetData.lightReferenceBodys.Length == 0)
            {
                switch (pl.parentType)
                {
                    case PlanetParentTypes.Star:
                        pl.lightSourceType = new int[1];
                        pl.lightSourceIndex = new int[1];
                        pl.lightSourceBodyName = new string[1];

                        pl.lightSourceBodyName[0] = SearchParentName(pl.parentType, pl.parentIndex);
                        break;

                    case PlanetParentTypes.Planet:
                        pl.lightSourceType = new int[2];
                        pl.lightSourceIndex = new int[2];
                        pl.lightSourceBodyName = new string[2];


                        pl.lightSourceBodyName[0] = SearchParentName(pl.parentType, pl.parentIndex, 1);
                        pl.lightSourceBodyName[1] = SearchParentName(pl.parentType, pl.parentIndex);
                        break;

                    case PlanetParentTypes.Moon:
                        pl.lightSourceType = new int[3];
                        pl.lightSourceIndex = new int[3];
                        pl.lightSourceBodyName = new string[3];

                        pl.lightSourceBodyName[0] = SearchParentName(pl.parentType, pl.parentIndex, 2);
                        pl.lightSourceBodyName[1] = SearchParentName(pl.parentType, pl.parentIndex, 1);
                        pl.lightSourceBodyName[2] = SearchParentName(pl.parentType, pl.parentIndex, 0);

                        break;
                }
                pl.isLightRefSet = false;
            }
            else
            {
                pl.lightSourceType = new int[planetData.lightReferenceBodys.Length];
                pl.lightSourceIndex = new int[planetData.lightReferenceBodys.Length];
                pl.lightSourceBodyName = new string[planetData.lightReferenceBodys.Length];
                for (int i = 0; i < planetData.lightReferenceBodys.Length; i++)
                {
                    pl.lightSourceBodyName[i] = planetData.lightReferenceBodys[i];
                }
                pl.isLightRefSet = true;
            }

            //光源名からタイプ(0:恒星 1:惑星)とインデックスを求める
            ResolveLightSources(pl);



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

        //公転周期が設定されていない天体は、主星の質量から求める
        foreach (var planet in planets)
        {
            if (planet.orbitalPeriod <= 0)
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


        //Managerに移す

        //恒星

        //初期化
        manager.name_star = new string[stars.Length];
        manager.body_star = new GameObject[stars.Length];
        manager.mesh_star = new MeshRenderer[stars.Length];
        manager.point_star = new MeshRenderer[stars.Length];


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

        foreach (var star in stars)
        {
            var i = star.index;
            manager.name_star[i] = star.name;
            manager.body_star[i] = star.body;
            manager.body_star[i].transform.SetParent(manager.cSphere.transform);
            manager.mesh_star[i] = manager.body_star[i].GetComponent<MeshRenderer>();
            manager.point_star[i] = CreatePoint(manager.body_star[i]);

            manager.specClass[i] = star.specClass;
            manager.mass_star[i] = star.mass;
            manager.rad_star[i] = star.rad;
            manager.teff_star[i] = star.teff;
            manager.lum[i] = star.lum;

            manager.lightObj_star[i] = star.lightObj;

        }



        //惑星

        //初期化
        manager.name_planet = new string[planets.Length];
        manager.body_planet = new GameObject[planets.Length];
        manager.tex_planet = new Texture2D[planets.Length];

        manager.mesh_planet = new MeshRenderer[planets.Length];
        manager.mesh_planet_block = new MaterialPropertyBlock[planets.Length];
        manager.point_planet = new MeshRenderer[planets.Length];

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
            manager.point_planet[i] = CreatePoint(manager.body_planet[i]);

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
        double parentMass = 0;
        switch (planet.parentType)
        {
            case PlanetParentTypes.Star:
                if (planet.parentIndex >= 0 && planet.parentIndex < stars.Length) { parentMass = stars[planet.parentIndex].mass; }
                break;
            case PlanetParentTypes.Planet:
                if (planet.parentIndex >= 0 && planet.parentIndex < planets.Length) { parentMass = planets[planet.parentIndex].mass; }
                break;
        }

        double totalMass = parentMass + planet.mass;
        if (planet.semiMajorAxis <= 0 || totalMass <= 0)
        {
            Debug.LogWarning(planet.name + " の公転周期を求められないため、公転させません (軌道長半径と主星の質量を確認してください)");
            return 0;
        }

        double a = planet.semiMajorAxis;
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

    private string SearchParentName(PlanetParentTypes type, int index)
    {
        switch(type)
        {
            case PlanetParentTypes.Star:
                return asset.starDatas[index].name;

            case PlanetParentTypes.Planet:
                return asset.planetDatas[index].name;

            case PlanetParentTypes.Moon:
                return stars[index].name;

        }

        return "";
    }

    /// <summary>
    /// 親天体のタイプとインデックスから名前を検索する。
    /// </summary>
    /// <param name="type"></param>
    /// <param name="index"></param>
    /// <param name="level">どの程度上の階層を指定するかのレベル。1なら親の親が検索される。</param>
    /// <returns></returns>
    private string SearchParentName(PlanetParentTypes type, int index, int level)
    {
        PlanetParentTypes curType = type;
        int curIndex = index;
        //levelの数だけ親をたどる(恒星より上の親はないので、恒星に着いたら止める)
        for (int i = 0; i < level; i++)
        {
            if (curType != PlanetParentTypes.Planet)
            {
                break;
            }

            string parentBody = asset.planetDatas[curIndex].parentBody;

            int idx = FindStarIndex(parentBody);
            if (idx != -1)
            {
                curType = PlanetParentTypes.Star;
                curIndex = idx;
                continue;
            }

            idx = FindPlanetIndex(parentBody);
            if (idx != -1)
            {
                curType = PlanetParentTypes.Planet;
                curIndex = idx;
                continue;
            }

            //見つからない場合はSearchParentと同じく星系の主星にフォールバック
            curType = asset.starDatas.Count != 0 ? PlanetParentTypes.Star : PlanetParentTypes.Planet;
            curIndex = 0;
        }

        return SearchParentName(curType, curIndex);
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

            Debug.LogWarning(pl.name + " の光源「" + bodyName + "」が見つからないため、光源から除外しました");
        }

        pl.lightSourceBodyName = names.ToArray();
        pl.lightSourceType = types.ToArray();
        pl.lightSourceIndex = indexes.ToArray();
    }

    /// <summary>
    /// 光点(pointObj)を天体本体の子として作り、MeshRenderer を返す。pointObj が未設定なら null。
    /// 本体と一緒に破棄・非表示になる。位置と大きさはシェーダー側で決めるので、ローカル座標は原点にする。
    /// </summary>
    /// <param name="body">天体本体</param>
    private MeshRenderer CreatePoint(GameObject body)
    {
        if (pointObj == null) { return null; }

        var point = Instantiate(pointObj, body.transform, false);
        point.name = body.name + "_point";
        point.transform.localPosition = Vector3.zero;
        point.transform.localRotation = Quaternion.identity;

        var mesh = point.GetComponent<MeshRenderer>();
        if (mesh == null) { Debug.LogWarning("光点のオブジェクトに MeshRenderer がありません: " + pointObj.name); }
        return mesh;
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

#endif


    //-------------------------------
    //Light Module

    [SerializeField] private ObserverProfile[] profiles;
    


    // Update is called once per frame
    void Update()
    {
        
    }
}
