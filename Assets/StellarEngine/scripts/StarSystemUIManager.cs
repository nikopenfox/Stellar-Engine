
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

    [Header("天体情報の表示板")]
    [SerializeField, Tooltip("表示板。子の Display1(項目名)・Display2(値) の TMP を Start で探す")] private GameObject bodyDisplayObj;
    [SerializeField, Tooltip("表示板で出す天体の名前(恒星・惑星)")] private string displaySearchBodyName;
    private TextMeshProUGUI bodyDisplay1, bodyDisplay2; //項目名・値
    private string lastObsBodyName; //入力欄に最後に合わせた、観測者のいる天体の名前

    [Header("時間")]
    [SerializeField, Tooltip("現在のシミュレーション時刻を出す TMP")] private TextMeshProUGUI currentTimeDisplay;
    [SerializeField, Tooltip("時間倍率を表示・入力する入力欄 (On End Edit か OnValueChanged で OnTimeFactorChanged を呼ぶ)")] private TMP_InputField timeFactorInputField;
    private double lastTimeFactor; //入力欄に最後に合わせた時間倍率

    [Header("入力欄")]
    [SerializeField, Tooltip("観測者のいる天体の名前を入れる入力欄 (OnValueChanged で OnObsBodyNameChanged を呼ぶ)")] private TMP_InputField obsBodyNameField;
    [SerializeField, Tooltip("表示板で出す天体の名前を入れる入力欄 (OnValueChanged で OnDisplaySearchBodyNameChanged を呼ぶ)")] private TMP_InputField displaySearchBodyNameField;

    private const string DISPLAY1_NAME = "Display1";
    private const string DISPLAY2_NAME = "Display2";
    private const double DISTANCE_AU_THRESHOLD = 0.05; //距離がこれ(AU)未満なら km か m で出す

    private float updateTimer;
    void Start()
    {
        InitTimeUI();
        InitBodyDisplay();
        InitScreen();
    }

    //-----時間-----

    private void InitTimeUI()
    {
        if (mn == null) { return; }
        lastTimeFactor = mn.GetTimeFactor();
        if (timeFactorInputField != null) { timeFactorInputField.text = lastTimeFactor.ToString(); }
        UpdateTimeUI();
    }

    //時刻の表示と、時間倍率が(ボタンや同期で)変わったときの入力欄の書き換え
    //倍率が変わったときだけ書き換えるので、入力途中の文字は消さない
    private void UpdateTimeUI()
    {
        if (mn == null) { return; }

        if (currentTimeDisplay != null) { currentTimeDisplay.text = mn.GetCurrentTimeText(); }

        if (timeFactorInputField == null) { return; }
        double current = mn.GetTimeFactor();
        if (current == lastTimeFactor) { return; }

        lastTimeFactor = current;
        double fieldValue;
        if (!double.TryParse(timeFactorInputField.text, out fieldValue) || fieldValue != current)
        {
            timeFactorInputField.text = current.ToString();
        }
    }

    //時間倍率の入力欄から呼ぶ。数値として読めない入力(入力途中の "-" など)は無視する
    public void OnTimeFactorChanged()
    {
        if (timeFactorInputField == null || mn == null) { return; }

        double value;
        if (!double.TryParse(timeFactorInputField.text, out value)) { return; }
        mn.SetTimeFactor(value);
    }

    //-----天体情報の表示板-----

    //表示板の子から Display1・Display2 の TMP を探す
    private void InitBodyDisplay()
    {
        if (bodyDisplayObj == null) { Debug.Log("StE_UI: 表示板(bodyDisplayObj)が未設定です"); return; }

        var texts = bodyDisplayObj.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var text in texts)
        {
            if (bodyDisplay1 == null && text.name == DISPLAY1_NAME) { bodyDisplay1 = text; }
            else if (bodyDisplay2 == null && text.name == DISPLAY2_NAME) { bodyDisplay2 = text; }
        }

        if (bodyDisplay1 == null) { Debug.Log("StE_UI: " + DISPLAY1_NAME + " が見つかりません"); }
        if (bodyDisplay2 == null) { Debug.Log("StE_UI: " + DISPLAY2_NAME + " が見つかりません"); }

        //入力欄の初期値を今の値に合わせる
        if (displaySearchBodyNameField != null) { displaySearchBodyNameField.text = displaySearchBodyName; }
        if (obsBodyNameField != null && mn != null)
        {
            lastObsBodyName = mn.GetObsBodyName();
            obsBodyNameField.text = lastObsBodyName;
        }

        UpdateBodyDisplay();
    }

    //観測者のいる天体の入力欄の OnValueChanged から呼ぶ
    public void OnObsBodyNameChanged()
    {
        if (obsBodyNameField == null || mn == null) { return; }
        mn.SetObsBodyName(obsBodyNameField.text);
    }

    //観測者のいる天体が(同期などで)変わったら、入力欄の文字を合わせる
    //変わったときだけ書き換えるので、入力途中の文字(まだ存在しない名前)は消さない
    //書き換えると OnValueChanged → SetObsBodyName が呼ばれるが、同じ名前なので何も起きない
    private void UpdateObsBodyNameField()
    {
        if (obsBodyNameField == null || mn == null) { return; }

        string current = mn.GetObsBodyName();
        if (current == null) { current = ""; }
        if (current == lastObsBodyName) { return; }

        lastObsBodyName = current;
        if (obsBodyNameField.text != current) { obsBodyNameField.text = current; }
    }

    //表示板で出す天体の入力欄の OnValueChanged から呼ぶ
    public void OnDisplaySearchBodyNameChanged()
    {
        if (displaySearchBodyNameField == null) { return; }
        displaySearchBodyName = displaySearchBodyNameField.text;
        UpdateBodyDisplay();
    }

    //表示板の更新。恒星→惑星の順に名前を探す
    //質量・半径はスケーリング前の実際の値に戻して出す(Importer で 質量は scale³ 倍、半径は scale 倍している)
    private void UpdateBodyDisplay()
    {
        if (bodyDisplay1 == null || bodyDisplay2 == null || mn == null) { return; }

        if (displaySearchBodyName == null || displaySearchBodyName == "")
        {
            bodyDisplay1.text = "";
            bodyDisplay2.text = "";
            return;
        }

        double scale = mn.scale > 0 ? mn.scale : 1.0;
        double auKm = mn.scaledAU / scale; //1AU(km)

        int si = FindIndex(mn.name_star, displaySearchBodyName);
        if (si != -1)
        {
            //distance_star はAU
            double distKm = (mn.distance_star != null && si < mn.distance_star.Length) ? mn.distance_star[si] * auKm : -1;

            bodyDisplay1.text = "名前\n質量\n半径\n有効温度\n光度\n実視等級\n観測等級\n視直径\n距離";
            bodyDisplay2.text = mn.name_star[si]
                + "\n" + FormatValue(mn.mass_star, si, 1.0 / (scale * scale * scale), "0.000E+00", " kg")
                + "\n" + FormatValue(mn.rad_star, si, 1.0 / scale, "#,0", " km")
                + "\n" + FormatValue(mn.teff_star, si, 1.0, "0", " K")
                + "\n" + FormatValue(mn.lum, si, 1.0, "0.0000", " L☉")
                + "\n" + FormatMagnitude(GetValue(mn.appMagn_star, si))
                + "\n" + FormatMagnitude(mn.GetObsMagnitudeStar(si))
                + "\n" + FormatAngle(mn.appDiameter_star, si)
                + "\n" + FormatDistance(distKm, auKm);
            return;
        }

        int pi = FindIndex(mn.name_planet, displaySearchBodyName);
        if (pi != -1)
        {
            bool isLanded = mn.isLanded != null && pi < mn.isLanded.Length && mn.isLanded[pi];

            //distance_planet はスケール済みkm
            double distKm = (mn.distance_planet != null && pi < mn.distance_planet.Length) ? mn.distance_planet[pi] / scale : -1;

            bodyDisplay1.text = "名前\n質量\n半径\n実視等級\n観測等級\n視直径\n距離\n位相";
            bodyDisplay2.text = mn.name_planet[pi]
                + "\n" + FormatValue(mn.mass_planet, pi, 1.0 / (scale * scale * scale), "0.000E+00", " kg")
                + "\n" + FormatValue(mn.rad_planet, pi, 1.0 / scale, "#,0", " km")
                + "\n" + (isLanded ? "-" : FormatMagnitude(GetValue(mn.appMagn_planet, pi)))
                + "\n" + (isLanded ? "-" : FormatMagnitude(mn.GetObsMagnitudePlanet(pi)))
                + "\n" + (isLanded ? "-" : FormatAngle(mn.appDiameter_planet, pi))
                + "\n" + (isLanded ? "観測者のいる天体" : FormatDistance(distKm, auKm))
                + "\n" + FormatPhase(mn.GetPhaseAnglePlanet(pi));
            return;
        }

        bodyDisplay1.text = "名前";
        bodyDisplay2.text = displaySearchBodyName + " は見つかりません";
    }

    //ログを出さずに名前の配列番号を探す(見つからなければ -1)
    private int FindIndex(string[] names, string target)
    {
        if (names == null) { return -1; }
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == target) { return i; }
        }
        return -1;
    }

    //配列の値に倍率を掛けて書式付きの文字列にする(配列がなければ "-")
    private string FormatValue(double[] values, int index, double mul, string format, string unit)
    {
        if (values == null || index >= values.Length) { return "-"; }
        return (values[index] * mul).ToString(format) + unit;
    }

    //視直径(度)を、大きさに応じて 度・分角・秒角 で出す
    private string FormatAngle(double[] values, int index)
    {
        if (values == null || index >= values.Length) { return "-"; }
        double deg = values[index];
        if (deg >= 1.0) { return deg.ToString("0.00") + "°"; }
        if (deg * 60.0 >= 1.0) { return (deg * 60.0).ToString("0.00") + "′"; }
        return (deg * 3600.0).ToString("0.00") + "″";
    }

    //配列の値を取り出す(配列がなければ NaN)
    private double GetValue(double[] values, int index)
    {
        if (values == null || index < 0 || index >= values.Length) { return double.NaN; }
        return values[index];
    }

    //等級を出す。+∞(光が届かない)は「∞」、値がなければ「-」
    private string FormatMagnitude(double magn)
    {
        if (double.IsNaN(magn)) { return "-"; }
        if (double.IsPositiveInfinity(magn)) { return "∞ 等"; }
        if (double.IsNegativeInfinity(magn)) { return "-∞ 等"; }
        return magn.ToString("0.00") + " 等";
    }

    //距離(km)を、0.05AU 以上なら AU、1km 以上なら km、それ未満なら m で出す
    private string FormatDistance(double km, double auKm)
    {
        if (km < 0) { return "-"; }
        if (auKm > 0 && km >= auKm * DISTANCE_AU_THRESHOLD) { return (km / auKm).ToString("0.0000") + " AU"; }
        if (km >= 1.0) { return km.ToString("#,0") + " km"; }
        return (km * 1000.0).ToString("#,0") + " m";
    }

    //位相角(度)と輝面比(照らされて見える面積の割合 = (1 + cos位相角) / 2)を出す
    private string FormatPhase(float phaseAngle)
    {
        if (phaseAngle < 0) { return "-"; }
        float litRatio = (1f + Mathf.Cos(phaseAngle * Mathf.Deg2Rad)) * 0.5f;
        return phaseAngle.ToString("0.0") + "° (輝面比 " + (litRatio * 100f).ToString("0.0") + "%)";
    }

    //-----天体スクリーン-----

    private void InitScreen()
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

    void FixedUpdate()
    {
        updateTimer += Time.deltaTime;

        //定期更新
        if (updateInterval > updateTimer) { return; }
        updateTimer = 0;

        UpdateTimeUI();
        UpdateObsBodyNameField();
        UpdateBodyDisplay();

        if (screenParameterTexts == null) { return; }
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

