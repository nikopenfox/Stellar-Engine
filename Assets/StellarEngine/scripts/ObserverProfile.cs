using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewObserverProfile", menuName = "StellarEngine/ObserverProfile")]

public class ObserverProfile : ScriptableObject 
{
    public float minLength;
    public float maxLength;
    public TempColorRelations[] tempPresets;

    // Start is called before the first frame update
    void OnValidate()
    {
        foreach (TempColorRelations rel in tempPresets)
        {
            if (rel.name == "" || rel.name == null) { continue; } 
            rel.teff = float.Parse(rel.name);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    [System.Serializable]
    public class TempColorRelations
    {
        public string name;
        public float teff;
        public float visibleLightRatio;
        public Color color;
    }

    public Color TempToColor(TempColorRelations[] presets, float teff, float alpha) //温度を色に変換
    {
        Color returnColor = new Color(0, 0, 0, 0);
        float lerpFactor; //補間係数
        //Debug.Log(presets[0].teff);
        for (int i = 1; i < presets.Length; i++)
        {

            //Debug.Log(presets[i].teff);

            if (teff < presets[i].teff) //どの区間にあるか一つずつ走査する
            {
                lerpFactor = Mathf.InverseLerp(presets[i - 1].teff, presets[i].teff, teff); //現在の区間の温度とその先の温度から補間係数を計算
                returnColor = Color.Lerp(presets[i - 1].color, presets[i].color, lerpFactor); //色を線形補間
                //Debug.Log("temp : " + teff + " color : " + returnColor);
                //Debug.Log("indextemp" + presets[i].teff + "lerpF : " + lerpFactor);
                returnColor = new Color(returnColor.r, returnColor.g, returnColor.b, alpha);
                return returnColor;
            }
        }
        returnColor = presets[presets.Length - 1].color; //どこにも入らなかったら最後の温度より上 = 最後の色を代入
        Color returnColorWithAlpha = new Color(returnColor.r, returnColor.g, returnColor.b, alpha); //アルファをかける
        return returnColorWithAlpha;
    }
}
