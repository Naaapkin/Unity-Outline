using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("Addition-post-processing/Outline")]
public class OutlineVolume : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter outlineWidth = new ClampedFloatParameter(0, 0, 5);
    public FloatParameter distanceThreshold = new FloatParameter(1);
    public FloatParameter normalThreshold = new FloatParameter(1);
    public FloatParameter normalThresholdScale = new FloatParameter(2);
    public ColorParameter outlineColor = new ColorParameter(Color.black, true, false, true);
    public BoolParameter onlyOutline = new BoolParameter(false);
    
    public bool IsActive()
    {
        return outlineWidth.value > 0;
    }

    public bool IsTileCompatible()
    {
        return false;
    }
}
