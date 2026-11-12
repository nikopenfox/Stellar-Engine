using UdonSharp;
using UnityEngine;

//ボタンの OnClick から OnClick を呼び、StarSystemManager の時間倍率を変える
//(ボタンの OnClick からは引数を渡せないので、倍率はこのスクリプトに持たせる)
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TimeFactorButton : UdonSharpBehaviour
{
    [SerializeField] private StarSystemManager mn;
    [SerializeField, Tooltip("ON: 今の時間倍率にこの値を掛ける / OFF: 時間倍率をこの値にする")] private bool isMultiply = true;
    [SerializeField, Tooltip("掛ける倍率、または設定する時間倍率")] private double value = 10d;

    public void OnClick()
    {
        if (mn == null) { Debug.Log("StE_TimeFactorButton: StarSystemManager が未設定です"); return; }

        if (isMultiply)
        {
            mn.MultiplyTimeFactor(value);
        }
        else
        {
            mn.SetTimeFactor(value);
        }
    }
}
