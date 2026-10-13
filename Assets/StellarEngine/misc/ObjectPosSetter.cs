
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectPosSetter : UdonSharpBehaviour
{
    [SerializeField] Transform target;
    private Rigidbody rb;
    [SerializeField] private bool isUseDefaultPos;
    private Vector3 defPos;
    [SerializeField] private Vector3 targetPos;
    void Start()
    {
        defPos = target.position;
        rb = target.GetComponent<Rigidbody>();
    }

    void PositionSet()
    {
        if (isUseDefaultPos)
        {
            target.position = defPos;
        }
        else
        {
            target.position = targetPos;
        }

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
        }
    }

    public override void Interact()
    {
        base.Interact();
        PositionSet();
    }
}
