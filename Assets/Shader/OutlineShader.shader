Shader "Addition/Post-processing/Outline"
{
    Properties
    {
        _Width ("Width", Range(0, 3)) = 1
        [HDR]_Color ("Color", Color) = (1,1,1,1)
        _DistanceThreshold ("Distance Threshold", Float) = 1
        _NormalThreshold ("Normal Threshold", Float) = 1
        _NormalThresholdScale ("Normal Threshold Scale", Float) = 2
        _OnlyOutline ("Only Outline", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ Outline_Only
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Width;
            float4 _Color;
            float _DistanceThreshold;
            float _NormalThreshold;
            float _DistanceLimit;
            float _NormalThresholdScale;
            CBUFFER_END

            struct VertexInfo
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragInfo
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FragInfo vert(VertexInfo input)
            {
                FragInfo output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.texcoord   = uv;
                output.viewDir = ComputeWorldSpacePosition(output.texcoord, 1, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos;
                return output;
            }
            
            float4 frag (FragInfo i) : SV_Target
            {
                // 计算四个采样点坐标
                float2 rtsp = i.texcoord + _Width * _ScreenSize.zw;
                float2 lbsp = i.texcoord + _Width * -_ScreenSize.zw;
                float2 ltsp = i.texcoord + _Width * float2(-_ScreenSize.z, _ScreenSize.w);
                float2 rbsp = i.texcoord + _Width * float2(_ScreenSize.z, -_ScreenSize.w);
                // 采样深度
                const float depth = SampleSceneDepth(i.texcoord).x;
                const float depth01 = Linear01Depth(depth, _ZBufferParams);
                const float lb = SampleSceneDepth(lbsp).x;
                const float lt = SampleSceneDepth(ltsp).x;
                const float rt = SampleSceneDepth(rtsp).x;
                const float rb = SampleSceneDepth(rbsp).x;
                const float4 depths = 1.0 / (_ZBufferParams.x * float4(lb, lt, rt, rb) + _ZBufferParams.w);
                // 采样法线
                const float3 normalVS = SampleSceneNormals(i.texcoord).xyz;
                const float3 lbN = SampleSceneNormals(lbsp).xyz;
                const float3 ltN = SampleSceneNormals(ltsp).xyz;
                const float3 rtN = SampleSceneNormals(rtsp).xyz;
                const float3 rbN = SampleSceneNormals(rbsp).xyz;
                // 计算法线边缘
                float conversionNormals = sqrt(Length2(lbN - rtN) + Length2(ltN - rbN)) * 5;
                // sqrt(Sq(lt - rb) + Sq(rt - lb)) * 60: 使用深度计算边缘, dot(viewDir, -normalVS)避免在接近平行于视角方向上的平面产生伪影, Sq(1 - depth01)减小非正交透视导致的斜面斜率随深度增加的变化
                #if UNITY_REVERSED_Z
                float conversionDepth = length(depths.xy - depths.zw) * dot(i.viewDir, -normalVS) * 300 * Sq(1 - depth01);
                float dynamicDistanceThreshold = _DistanceThreshold * depth01;
                #else
                float conversionDepth = length(depths.xy - depths.zw) * dot(i.viewDir, -normalVS) * 300 * Sq(depth01);
                float dynamicDistanceThreshold = _DistanceThreshold * (1 - depth01);
                #endif
                float dynamicNormalThreshold = _NormalThreshold * lerp(_NormalThresholdScale, 1, depth);
                float edge = max(step(dynamicDistanceThreshold, conversionDepth), step(dynamicNormalThreshold, conversionNormals));
                
                #ifdef Outline_Only
                return edge;
                #else
                return float4(lerp(SampleSceneColor(i.texcoord), _Color.rgb, edge), 1);
                #endif
            }
            ENDHLSL
        }
    }
}
