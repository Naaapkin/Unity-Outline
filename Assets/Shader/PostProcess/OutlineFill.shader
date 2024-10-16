Shader "Custom/OutlineFill"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Int) = 0
        _Stencil("Stencil", Int) = 1
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+110"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "OutlineFill"
            ZTest [_ZTest]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref [_Stencil]
                ReadMask [_Stencil]
                Comp NotEqual
            }
                  
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 tangent : TANGENT;
                float4 normal : NORMAL;
                float4 positionOS : POSITION;
                float4 smoothNormal : TEXCOORD7;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            #pragma multi_compile _ TANGENT_SPACE

            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 smoothNormalOS = 0;
                #if defined(TANGENT_SPACE)
                float3 tangentWS = TransformObjectToWorldDir(i.tangent.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normal.xyz);
                tangentWS = Orthonormalize(tangentWS, normalWS);
                // 计算TBN矩阵
                float3x3 tangentToWorld = CreateTangentToWorld(normalWS, tangentWS, i.tangent.w);
                // 计算模型空间法线
                smoothNormalOS = TransformTangentToObject(i.smoothNormal, tangentToWorld);
                #else
                smoothNormalOS = i.smoothNormal;
                #endif
                
                float3 positionVS = mul(UNITY_MATRIX_MV, i.positionOS).xyz;
                float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, smoothNormalOS)).xyz;

                // 将外拓距离乘以深度，避免透视除法导致描边宽度随距离变化
                positionVS.xyz = positionVS.xyz + normalVS * -positionVS.z * _OutlineWidth * 0.001;

                o.positionHCS = TransformWViewToHClip(positionVS);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
