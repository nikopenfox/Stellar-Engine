
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class SEPresetChanger : UdonSharpBehaviour
{
    [SerializeField] StarSystemManager pref;
    [SerializeField] int targetIndex = 0;
    //[SerializeField] string text;
    public override void Interact()
    {
        base.Interact();
        //this.InteractionText = text;
        pref.currentPresetIndex = targetIndex;
    }
}
