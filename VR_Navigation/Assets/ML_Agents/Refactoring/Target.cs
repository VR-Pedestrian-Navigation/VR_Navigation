using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using UnityEngine.Events;


public class Target : MonoBehaviour
{
    public Group group;
    public TargetType targetType;
    private void OnValidate()
    {
        var layerName = targetType.GetLayerName(group);
        gameObject.layer = LayerMask.NameToLayer(layerName);
        gameObject.tag = "Target";
    }
}
