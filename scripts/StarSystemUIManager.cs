
using Newtonsoft.Json;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class StarSystemUIManager : UdonSharpBehaviour
{
    [Header("データ元")]
    [SerializeField] private StarSystemManager mn;

    [Header("更新頻度")]
    [SerializeField] private float updateInterval = 0.2f;

    [Header("天体情報スクリーン")]
    [SerializeField] private GameObject starScreenRootObj;

    private string[] screenTargetNames;
    [SerializeField] private string[] screenParamNames;
    [SerializeField] private TextMeshProUGUI[] screenParameterTexts;

    private float updateTimer;
    void Start()
    {
        if (starScreenRootObj == null) { Debug.Log("StE_UI: Root Object is null."); return; }

        // 配列初期化
        screenTargetNames = new string[]
        {
        nameof(ScreenParameterIndexs.Temperature),
        nameof(ScreenParameterIndexs.AppDiameter)
        };

        // ルートの子要素スキャン
        var allTexts = starScreenRootObj.GetComponentsInChildren<TextMeshProUGUI>(true);

        var tempTexts = new TextMeshProUGUI[screenTargetNames.Length];
        var tempTypes = new string[screenTargetNames.Length];
        var textsValidCount = 0; // 見つかった数を数えるカウンター

        // スキャンし、実在するものを左詰めでインデックスに紐付けながら格納
        for (int i = 0; i < screenTargetNames.Length; i++)
        {
            foreach (var text in allTexts)
            {
                if (text.name == screenTargetNames[i])
                {
                    // 見つかった順のインデックスに詰める
                    tempTexts[textsValidCount] = text;
                    tempTypes[textsValidCount] = screenTargetNames[i];
                    textsValidCount++;
                    break;
                }
            }
        }

        // textsValidCountでジャストサイズの SoA 配列を確保
        screenParameterTexts = new TextMeshProUGUI[textsValidCount];
        screenParamNames = new string[textsValidCount];

        for (int i = 0; i < textsValidCount; i++)
        {
            screenParameterTexts[i] = tempTexts[i];
            screenParamNames[i] = tempTypes[i];
        }

    }

    //-----天体スクリーン-----

    void FixedUpdate()
    {
        updateTimer += Time.deltaTime;

        //定期更新
        if (updateInterval > updateTimer) { return; }

        for (int i = 0; i < screenParameterTexts.Length; i++)
        {
            screenParameterTexts[i].text = nameof(screenParamNames);
            
        }
        
    }
}
//スクリーン要素SoAの定義
public enum ScreenParameterIndexs
{
    Temperature,
    AppDiameter
}

