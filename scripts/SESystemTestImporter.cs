using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SESystemTestImporter : MonoBehaviour
{
    [SerializeField] private ObserverProfile[] profiles;
    [SerializeField] private SESystemTest seTest;

    // Start is called before the first frame update
    void OnValidate()
    {
        //プロファイル初期化

        seTest.profile_profileName = new string[profiles.Length];
        seTest.profile_count = new int[profiles.Length];
        seTest.profile_index = new int[profiles.Length];

        int dataCount = 0;

        foreach (var profile in profiles)
        {
            dataCount += profile.tempPresets.Length;
        }

        seTest.profile_name = new string[dataCount];
        seTest.profile_teff = new float[dataCount];
        seTest.profile_visibleLightRatio = new float[dataCount];
        seTest.profile_color = new Color[dataCount];

        //代入

        int curIdx = 0; //プロファイルの開始インデックス

        for (int i = 0; i < profiles.Length; i++)
        {
            
            dataCount = 0; //インデックスを記録するため初期化

            seTest.profile_profileName[i] = profiles[i].name;

            //温度プリセットを読む
            for (int j = 0; j < profiles[i].tempPresets.Length; j++)
            {
                
                dataCount++;
                seTest.profile_name[j + curIdx] = profiles[i].tempPresets[j].name;
                seTest.profile_teff[j + curIdx] = profiles[i].tempPresets[j].teff;
                seTest.profile_visibleLightRatio[j + curIdx] = profiles[i].tempPresets[j].visibleLightRatio;
                seTest.profile_color[j + curIdx] = profiles[i].tempPresets[j].color;
            }

            seTest.profile_index[i] = curIdx;
            curIdx += dataCount;
            //データ数とインデックスを記録
            seTest.profile_count[i] = dataCount;
            

        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
