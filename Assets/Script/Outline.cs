using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public enum Mode
{
    Normal,     // 4, 4, 6
    XRayOnly,   // 7, 7, 6
    XRay,       // 8, 8, 6
    XRayFill,   // 8, 4, 6
}

public class Outline : MonoBehaviour
{
    public static readonly string DefaultPath = "Assets/SmoothNormalMap/";
    public static readonly int ID_OUTLINE = Shader.PropertyToID("_OutlineColor");
    public static readonly int ID_WIDTH = Shader.PropertyToID("_OutlineWidth");
    public static readonly int ID_ZTEST = Shader.PropertyToID("_ZTest");
    public static readonly int ID_STENCIL = Shader.PropertyToID("_Stencil");
    
    [FormerlySerializedAs("Color")] [SerializeField] private Color color = Color.white;
    [SerializeField] private float outlineWidth = 10f;
    
    [Tooltip("如果为空，将在获取所有该物体及其子物体的MeshFilter")]
    [SerializeField] private MeshFilter[] meshFilters;
    
    [Tooltip("如果为空，将在获取所有该物体及其子物体的MeshRenderer")]
    [SerializeField] private MeshRenderer[] meshRenderers;
    
    [Tooltip("平滑法线是否在切线空间下，如果是，需要计算平滑法线时会转换到切线空间下，着色器会使用TBN矩阵转换法线。适合在使用蒙皮动画时使用。")]
    [SerializeField] private bool tangentSpace;
    
    [Tooltip("描边对象的层级，同层对象的描边会相连，不同层的描边互不干扰。")]
    [SerializeField] private uint layerMask;
    
    [Tooltip("描边模式(Normal-描边可见部分，XRayOnly-描边被遮挡部分，XRay-始终描边)")]
    [SerializeField] private Mode outlineMode;
    
    [SerializeField] [ReadOnly] private bool baked;
    
    private Material outline;
    private Material mask;
    
    public float OutlineWidth
    {
        get => outlineWidth;
        set
        {
            if (!Mathf.Approximately(value, outlineWidth)) return;
            outlineWidth = value;
            UpdateOutline(value, color);
        }
    }

    private void Awake()
    {
        if(meshFilters.Length == 0)
        {
            meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                throw new Exception("MeshFilter not found");
            }
        }

        if(meshRenderers.Length == 0)
        {
            meshRenderers = GetComponentsInChildren<MeshRenderer>();
            if (meshRenderers.Length == 0)
            {
                throw new Exception("MeshRenderer not found");
            }
        }
        outline = new Material(Shader.Find("Custom/OutlineFill"));
        mask = new Material(Shader.Find("Custom/OutlineMask"));
        
        if (!baked) GenerateSmoothNormal();
        UpdateOutline(outlineWidth, color);
    }

    private void OnEnable()
    {
        foreach (var meshRenderer in meshRenderers)
        {
            var materialList = meshRenderer.sharedMaterials.ToList();
            materialList.Add(outline);
            materialList.Add(mask);
            meshRenderer.sharedMaterials = materialList.ToArray();
        }
    }
    
    private void OnDisable()
    {
        foreach (var meshRenderer in meshRenderers)
        {
            var materialList = meshRenderer.sharedMaterials.ToList();
            materialList.Remove(outline);
            materialList.Remove(mask);
            meshRenderer.sharedMaterials = materialList.ToArray();
        }
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        DestroyImmediate(outline);
        DestroyImmediate(mask);
#else
        Destroy(outline);
        Destroy(mask);
#endif
    }
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        // 判断是否正在播放
        if (!Application.isPlaying) return;
        UpdateOutline(outlineWidth, color);
    }
#endif

    public void UpdateOutline(float width, Color color)
    {
#if UNITY_EDITOR
        if (!outline) outline = new Material(Shader.Find("Custom/OutlineFill"));
        if (!mask) mask = new Material(Shader.Find("Custom/OutlineMask"));
        if (tangentSpace)
        {
            outline.EnableKeyword("TANGENT_SPACE");
        }
        else
        {
            outline.DisableKeyword("TANGENT_SPACE");
        }
#endif
        outline.SetFloat(ID_WIDTH, width);
        outline.SetColor(ID_OUTLINE, color);
        outline.SetInt(ID_STENCIL, 1 << (int)layerMask);
        switch (outlineMode)
        {
            case Mode.XRay:
                outline.SetInt(ID_ZTEST, 0);
                mask.SetInt(ID_ZTEST, 8);
                break;
            case Mode.XRayOnly:
                outline.SetInt(ID_ZTEST, 5);
                mask.SetInt(ID_ZTEST, 7);
                break;
            case Mode.XRayFill:
                outline.SetInt(ID_ZTEST, 0);
                mask.SetInt(ID_ZTEST, 4);
                break;
            default:
                outline.SetInt(ID_ZTEST, 4);
                mask.SetInt(ID_ZTEST, 4);
                break;
        }
        mask.SetInt(ID_STENCIL, 1 << (int) layerMask);
    }
    
    private void GenerateSmoothNormal()
    {
        foreach (var meshFilter in meshFilters)
        {
            var mesh = meshFilter.sharedMesh;
            var smoothNormals = CalcSmoothNormals(mesh);
            if (tangentSpace) smoothNormals = GetTangentSpaceNormal(smoothNormals, mesh);
            CombineMesh(ref mesh);
            mesh.SetUVs(7, smoothNormals);
            meshFilter.sharedMesh = mesh;
        }
    }
    
    private static void CombineMesh(ref Mesh mesh)
    {
        if (mesh.subMeshCount == 1) return;
        mesh.subMeshCount += 1;
        mesh.SetTriangles(mesh.triangles, mesh.subMeshCount - 1); 
    }

    private static Vector3[] CalcSmoothNormals(Mesh mesh)
    {
        // 根据顶点位置将法线分组。位置相同的法线求和
        Dictionary<Vector3, Vector3> groups = new Dictionary<Vector3, Vector3>();
        var normals = mesh.normals;
        var vertices = mesh.vertices;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            if (groups.ContainsKey(vertices[i]))
                groups[vertices[i]] += normals[i];
            else
                groups.Add(vertices[i], normals[i]);
        }

        // 将求和后的法线全部归一化
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = groups[vertices[i]];
            normals[i].Normalize();
        }
        return normals;
    }
    
    private static Vector3[] GetTangentSpaceNormal(Vector3[] smoothedNormals, Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;

        Vector3[] smoothedNormalsTS = new Vector3[smoothedNormals.Length];

        // 根据每个顶点的切线和法线将平滑法线转换到切线空间下
        for (int i = 0; i < smoothedNormalsTS.Length; i++)
        {
            Vector3 normal  = normals[i];
            Vector3 tangent = tangents[i];

            // 史密斯特正交化计算正交化的法向量，通常模型中的法线和切线都是正交的
            Vector3.OrthoNormalize(ref normal, ref tangent);
            Vector3 bitangent = (Vector3.Cross(normal, tangent) * tangents[i].w).normalized;
            // 构建TBN矩阵
            var TBN = new Matrix4x4(tangent, bitangent, normal, Vector4.zero).transpose;
            // 将平滑法线转换到切线空间下
            smoothedNormalsTS[i] = TBN.MultiplyVector(smoothedNormals[i]);
        }

        return smoothedNormalsTS;
    }
    
    [MenuItem("CONTEXT/Outline/Bake")]
    private static void BakeNormalMap(MenuCommand cmd)
    {
        Outline outline = cmd.context as Outline;
        Assert.IsNotNull(outline);
        SerializedObject serializedOutline = new SerializedObject(outline);
        SerializedProperty bakedProperty = serializedOutline.FindProperty("baked");
        SerializedProperty meshFiltersProperty = serializedOutline.FindProperty("meshFilters");
        
        var meshFilters = outline.meshFilters;
        var tangentSpace = outline.tangentSpace;
        
        if (meshFilters.Length == 0)
        {
            meshFilters = outline.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                throw new Exception("MeshFilter not found");
            }
        }
        
        meshFiltersProperty.arraySize = meshFilters.Length;
        
        for (int i = 0; i < meshFilters.Length; i++)
        {
            var mesh = meshFilters[i].sharedMesh;
            var smoothNormals = CalcSmoothNormals(mesh);
            CombineMesh(ref mesh);
        
            if (tangentSpace) smoothNormals = GetTangentSpaceNormal(smoothNormals, mesh);
            mesh.SetUVs(7, smoothNormals);
            meshFilters[i].sharedMesh = mesh;
        
            // 设置SerializedProperty
            meshFiltersProperty.GetArrayElementAtIndex(i).objectReferenceValue = meshFilters[i];
        }
        
        bakedProperty.boolValue = true;
        serializedOutline.ApplyModifiedProperties();
    }
}
