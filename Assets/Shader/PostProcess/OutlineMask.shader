Shader "Custom/OutlineMask"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
        _Stencil("Stencil", Int) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue" = "Transparent+100"
        }

        Pass
        {
            Name "OutlineMask"
            ZWrite Off
            Cull Back
            Blend Zero One
            ZTest [_ZTest]
            ColorMask 0
            
            Stencil
            {
                Ref [_Stencil]
                ReadMask [_Stencil]
                WriteMask [_Stencil]        // 避免层间干扰，也许可以去掉这行实现层间描边交互
                Comp Always
                Pass Replace
                ZFail Keep
            }
        }
    }
}
