using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PillarsParent : MonoBehaviour
{
    private void Start()
    {
        transform.rotation = transform.rotation * Quaternion.AngleAxis(90, Vector3.up);
    }
}
