
using System.Diagnostics;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using static ObserverProfile;

public class SESystemTest : UdonSharpBehaviour
{
    //private float scaledAU = 10f;
    private float LX = 218340.61f; //可視光フラックス1が何ルクスに相当するか
    [SerializeField, Tooltip("見える照度の下限")] float thresholdLx;

    [SerializeField] float intensityBase, intensityAll;
    [SerializeField] float sunBrightness;
    [SerializeField, Range(-3.0f, 1.0f)] float dynamicRange;
    [SerializeField] float fluxAll;
    [Header("光源テスト")]
    //[SerializeField] CelestialBodyLight[] lights = new CelestialBodyLight[0];
    [SerializeField] Light[] lightObj = new Light[0];
    [SerializeField] Material[] lightMat = new Material[0];
    [SerializeField] Vector3[] lightPos;
    [SerializeField] Color[] lightColor;
    [SerializeField] float[] fluxBol;
    [SerializeField] float[] flux;
    [SerializeField] float[] lx;
    [SerializeField,Range(0,30000)] float[] teff;
    [SerializeField] float[] fluxRatio;
    
    [SerializeField] private double distance;
    [SerializeField] private bool isUpdate;

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
    [Header("環境光")]
    [SerializeField,Tooltip("大気機能を使う？")]private bool isUseAtmosphere;
    [SerializeField] private float ambLightBrightness;
    public Color ambColorBase_sky;
    public Color ambColorBase_equator, ambColorBase_ground;
    private Color atmColorTint;

    [Header("デバッグ")]
    [SerializeField] private bool isDebug;
    [SerializeField] public bool debug_toggleStar2;
    [SerializeField] Scrollbar debug_tempSc, debug_lumSc;
    [SerializeField] TextMeshProUGUI debug_tempTx, debug_lumBolTx, debug_lxTx, debug_VL_Tx;
    public float debug_temp = 1f, debug_lum = 1f;
    private float debug_baseTemp, debug_baseLum;

    private float debug_tempMin = 0f, debug_tempMax = 30000f;
    private float debug_lumMin = 0f, debug_lumMax = 1f;

    private void OnValidate()
    {
        if (isInit)
        {
            LightInit();
        }
        UpdateLight();
        UpdateAtmosphere();

        if (isDebug)
        {
            debug_baseTemp = teff[0];
            debug_baseLum = fluxBol[0];
        }

        //Debug.Log("koya" + LogScaling(0.001f));
    }

    public void Debug_ToggleStar2()
    {
        debug_toggleStar2 = !debug_toggleStar2;
    }

    void UpdateDebug()
    {
        teff[0] = Mathf.Lerp(debug_tempMin, debug_tempMax, debug_tempSc.value * debug_tempSc.value);
        fluxBol[0] = Mathf.Lerp(debug_lumMin, debug_lumMax, debug_lumSc.value * debug_lumSc.value); ;

        debug_tempTx.text = teff[0].ToString() + "K";
        debug_lumBolTx.text = fluxBol[0].ToString() + "L";
        debug_lxTx.text = fluxAll * LX + " lx";
        debug_VL_Tx.text = TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], teff[0]) * 100 + " %";

        if (debug_toggleStar2)
        {
            fluxBol[1] = 0f;
        }
        else
        {
            fluxBol[1] = 0.00003f;
        }

    }

    private void FixedUpdate()
    {
        UpdateLight();

        if (isUseAtmosphere)
        {
            UpdateAtmosphere();
        }

        if (isDebug)
        {
            UpdateDebug();
        }
    }

    private void LightInit()
    {
        lightPos = new Vector3[lightObj.Length];
        lightMat = new Material[lightObj.Length];
        lightColor = new Color[lightObj.Length];
        flux = new float[lightObj.Length];
        fluxBol = new float[lightObj.Length];
        fluxRatio = new float[lightObj.Length];
        lx = new float[lightObj.Length];
        teff = new float[lightObj.Length];
    }

    private void AtmInit()
    {

    }

    private void UpdateLight()
    {
        var fx = 0f; //総フラックス

        

        for (int i = 0; i < lightObj.Length; i++)
        {
            lightObj[i].transform.LookAt(new Vector3(0, 0, 0));
            lightObj[i].transform.position = lightPos[i];
            lightColor[i] = TempToColor(profile_teff, profile_color, profile_count[currentPresetIndex], profile_index[currentPresetIndex], teff[i], 1.0f);
            lightObj[i].color = lightColor[i];
            lightMat[i].SetColor("_emissive", TempToColor(profile_teff, profile_color, profile_count[currentPresetIndex], profile_index[currentPresetIndex], teff[i], 1.0f));
            

            //可視光フラックス計算
            flux[i] = fluxBol[i] * TempToFlux(profile_teff, profile_visibleLightRatio, profile_count[currentPresetIndex], profile_index[currentPresetIndex], teff[i]);
            lx[i] = flux[i] * LX;
            fx += flux[i];
            

        }

        fluxAll = fx;
        intensityAll = intensityBase * LogScaling(fluxAll);

        for (int i = 0; i < lightObj.Length; i++)
        {
            float belowCor = 1; //下限をどれくらい下回ったか
            if (fluxAll != 0)
            {

                

                if (thresholdLx > lx[i])
                {
                    belowCor = lx[i] / thresholdLx;
                    //Debug.Log("belowCor: " + belowCor);
                }
                fluxRatio[i] = flux[i] / fluxAll;
            }
            else
            {
                fluxRatio[i] = 0;
            }
            lightObj[i].intensity = intensityAll * fluxRatio[i] * belowCor;
            lightMat[i].SetFloat("_intensity", sunBrightness * LogScaling(flux[i]) * 
                LogScaling(Mathf.Clamp(fluxRatio[i] * Mathf.Pow(fluxRatio[i],fluxRatio[i] - dynamicRange), 0, 100))
                * belowCor);
        }
        
    }

    private void UpdateAtmosphere()
    {
        //Tintを初期化
        atmColorTint = Color.white;
        //各ライトの色を取り出し、合成する
        for (int i = 0; i < lightObj.Length; i++)
        {
            var belowCor = 1f;

            if (thresholdLx > lx[i])
            {
                belowCor = lx[i] / thresholdLx;
                
            }

            

            atmColorTint += lightColor[i] * fluxRatio[i] * belowCor;
        }


        var brightnessCor = LogScaling(fluxAll) * ambLightBrightness; //明るさ補正

        //環境光の色を変える
        RenderSettings.ambientSkyColor = ambColorBase_sky * atmColorTint * brightnessCor;
        RenderSettings.ambientEquatorColor = ambColorBase_equator * atmColorTint * brightnessCor;
        RenderSettings.ambientGroundColor = ambColorBase_ground * atmColorTint * brightnessCor;
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
}







[System.Serializable]
public class CelestialBodyLight
{
    public int lightIndex;
    public Vector3 position;
    public double distance;
}
