// Rendering API surface for the harness (compile checks only; nothing is drawn).
using System;
using Unity.Collections;

namespace UnityEngine
{
    public enum TextureFormat { RGBA32 = 4, RGBAFloat = 20, RGBAHalf = 17, RGFloat = 19, RFloat = 18, R8 = 63, Alpha8 = 1, ARGB32 = 5 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }

    public class Texture : Object
    {
        public int width { get; protected set; }
        public int height { get; protected set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
    }

    public class Texture2D : Texture
    {
        byte[] m_Data;
        public Texture2D(int w, int h) : this(w, h, TextureFormat.RGBA32, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip) : this(w, h, f, mip, false) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) { width = w; height = h; format = f; m_Data = new byte[w * h * 16]; }
        public Texture2D(int w, int h, UnityEngine.Experimental.Rendering.GraphicsFormat f, UnityEngine.Experimental.Rendering.TextureCreationFlags flags) { width = w; height = h; m_Data = new byte[w * h * 16]; }
        public TextureFormat format { get; }
        public NativeArray<T> GetPixelData<T>(int mip) where T : struct => new NativeArray<T>(width * height * 16 / System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), Allocator.Persistent);
        public void SetPixelData<T>(NativeArray<T> data, int mip, int offset = 0) where T : struct { }
        public void SetPixels32(Color32[] c) { }
        public void SetPixel(int x, int y, Color c) { }
        public Color GetPixel(int x, int y) => default;
        public Color32[] GetPixels32() => new Color32[width * height];
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable = false) { }
        public byte[] EncodeToPNG() => Array.Empty<byte>();
        public bool Reinitialize(int w, int h) { width = w; height = h; return true; }
    }

    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Default = 7, ARGBFloat = 11 }
    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int d) { width = w; height = h; }
        public RenderTexture(int w, int h, int d, RenderTextureFormat format) : this(w, h, d) { }
        public static RenderTexture active { get; set; }
        public int antiAliasing { get; set; } = 1;
        public bool Create() => true;
        public void Release() { }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
        public static int PropertyToID(string name) => name.GetHashCode();
        public bool isSupported => true;
        public static void EnableKeyword(string k) { }
        public static void DisableKeyword(string k) { }
        public static void SetGlobalFloat(int id, float v) { }
        public static void SetGlobalVector(int id, Vector4 v) { }
    }

    public class Material : Object
    {
        public Material(Shader s) { shader = s; }
        public Material(Material m) { shader = m.shader; }
        public Shader shader { get; set; }
        public int renderQueue { get; set; }
        public bool enableInstancing { get; set; }
        public Color color { get; set; }
        public void SetBuffer(int id, GraphicsBuffer b) { }
        public void SetBuffer(string n, GraphicsBuffer b) { }
        public void SetTexture(int id, Texture t) { }
        public void SetFloat(int id, float v) { }
        public void SetInt(int id, int v) { }
        public void SetInteger(int id, int v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetColor(int id, Color c) { }
        public void SetMatrix(int id, Matrix4x4 m) { }
        public void EnableKeyword(string k) { }
        public void DisableKeyword(string k) { }
        public bool IsKeywordEnabled(string k) => false;
        public void SetOverrideTag(string t, string v) { }
    }

    public sealed class MaterialPropertyBlock
    {
        public void SetBuffer(int id, GraphicsBuffer b) { }
        public void SetTexture(int id, Texture t) { }
        public void SetFloat(int id, float v) { }
        public void SetInt(int id, int v) { }
        public void SetInteger(int id, int v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetColor(int id, Color c) { }
        public void Clear() { }
    }

    public sealed class ComputeShader : Object
    {
        public int FindKernel(string name) => 0;
        public bool HasKernel(string name) => true;
        public void SetBuffer(int kernel, int id, GraphicsBuffer b) { }
        public void SetInt(int id, int v) { }
        public void SetFloat(int id, float v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetMatrix(int id, Matrix4x4 m) { }
        public void Dispatch(int kernel, int x, int y, int z) { }
        public void GetKernelThreadGroupSizes(int k, out uint x, out uint y, out uint z) { x = 64; y = 1; z = 1; }
        public void EnableKeyword(string k) { }
    }

    public sealed class GraphicsBuffer : IDisposable
    {
        [Flags] public enum Target { Vertex = 1, Index = 2, CopySource = 4, CopyDestination = 8, Structured = 16, Raw = 32, Append = 64, Counter = 128, IndirectArguments = 256, Constant = 512 }
        [Flags] public enum UsageFlags { None = 0, LockBufferForWrite = 1 }
        public struct IndirectDrawIndexedArgs { public uint indexCountPerInstance, instanceCount, startIndex, baseVertexIndex, startInstance; public const int size = 20; }
        public struct IndirectDrawArgs { public uint vertexCountPerInstance, instanceCount, startVertex, startInstance; public const int size = 16; }
        public GraphicsBuffer(Target t, int count, int stride) { this.count = count; this.stride = stride; target = t; }
        public GraphicsBuffer(Target t, UsageFlags u, int count, int stride) : this(t, count, stride) { usageFlags = u; }
        public int count { get; }
        public int stride { get; }
        public Target target { get; }
        public UsageFlags usageFlags { get; }
        public string name { get; set; }
        public bool IsValid() => true;
        public void SetData(Array data) { }
        public void SetData<T>(NativeArray<T> data) where T : struct { }
        public void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct { }
        public void SetData<T>(T[] data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct { }
        public NativeArray<T> LockBufferForWrite<T>(int start, int count) where T : struct => new NativeArray<T>(count, Allocator.Persistent);
        public void UnlockBufferAfterWrite<T>(int count) where T : struct { }
        public void Release() { }
        public void Dispose() { }
    }

    public struct RenderParams
    {
        public RenderParams(Material m) { material = m; worldBounds = default; matProps = null; layer = 0; camera = null; shadowCastingMode = Rendering.ShadowCastingMode.Off; receiveShadows = false; renderingLayerMask = 0; }
        public Material material;
        public Bounds worldBounds;
        public MaterialPropertyBlock matProps;
        public int layer;
        public Camera camera;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public uint renderingLayerMask;
    }

    public static class Graphics
    {
        public static void RenderMesh(in RenderParams p, Mesh mesh, int submesh, Matrix4x4 objectToWorld, Matrix4x4? prev = null) { }
        public static void RenderMeshIndirect(in RenderParams p, Mesh mesh, GraphicsBuffer args, int commandCount = 1, int startCommand = 0) { }
        public static void RenderPrimitives(in RenderParams p, MeshTopology t, int vertexCount, int instanceCount = 1) { }
        public static void RenderPrimitivesIndirect(in RenderParams p, MeshTopology t, GraphicsBuffer args, int commandCount = 1, int startCommand = 0) { }
        public static void RenderPrimitivesIndexed(in RenderParams p, MeshTopology t, GraphicsBuffer index, int indexCount, int startIndex = 0, int instanceCount = 1) { }
        public static void ExecuteCommandBuffer(Rendering.CommandBuffer cb) { }
    }

    [Flags] public enum MeshUpdateFlags { Default = 0, DontValidateIndices = 1, DontResetBoneBounds = 2, DontNotifyMeshUsers = 4, DontRecalculateBounds = 8 }

    public class Mesh : Object
    {
        public Mesh() { }
        public Bounds bounds { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public int vertexCount { get; private set; }
        public int subMeshCount { get; set; } = 1;
        public Vector3[] vertices { get; set; }
        public int[] triangles { get; set; }
        public void MarkDynamic() { }
        public void SetVertices(Vector3[] v) { vertexCount = v.Length; }
        public void SetUVs(int channel, Vector4[] uvs) { }
        public void SetUVs(int channel, Vector2[] uvs) { }
        public void SetColors(Color32[] c) { }
        public void SetColors(Color32[] c, int start, int length) { }
        public void SetVertices(Vector3[] v, int start, int length) { vertexCount = length; }
        public void SetIndices(int[] indices, int start, int length, MeshTopology t, int submesh, bool calcBounds = true, int baseVertex = 0) { }
        public void SetIndices(int[] indices, MeshTopology t, int submesh, bool calcBounds = true) { }
        public void SetTriangles(int[] t, int submesh) { }
        public void SetSubMesh(int index, Rendering.SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) { }
        public void SetVertexBufferParams(int count, params Rendering.VertexAttributeDescriptor[] attrs) { vertexCount = count; }
        public void SetIndexBufferParams(int count, Rendering.IndexFormat f) { }
        public void SetVertexBufferData<T>(NativeArray<T> data, int start, int meshStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferData<T>(NativeArray<T> data, int start, int meshStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct { }
        public void SetIndexBufferData<T>(T[] data, int start, int meshStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct { }
        public void RecalculateBounds() { }
        public void UploadMeshData(bool noLongerReadable) { }
        public void Clear() { }
        public uint GetIndexCount(int submesh) => 0;

        public static MeshDataArray AllocateWritableMeshData(int count) => new MeshDataArray(count);
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh mesh, MeshUpdateFlags flags = MeshUpdateFlags.Default) { }
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh[] meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) { }
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, System.Collections.Generic.List<Mesh> meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) { }

        public struct MeshDataArray : IDisposable
        {
            MeshData[] m_Data;
            public MeshDataArray(int n) { m_Data = new MeshData[n]; }
            public int Length => m_Data.Length;
            public MeshData this[int i] => m_Data[i];
            public void Dispose() { }
        }

        public struct MeshData
        {
            public int vertexCount => 0;
            public int subMeshCount { get; set; }
            public void SetVertexBufferParams(int count, params Rendering.VertexAttributeDescriptor[] attrs) { }
            public void SetIndexBufferParams(int count, Rendering.IndexFormat f) { }
            public NativeArray<T> GetVertexData<T>(int stream = 0) where T : struct => default;
            public NativeArray<T> GetIndexData<T>() where T : struct => default;
            public void SetSubMesh(int index, Rendering.SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) { }
        }
    }

    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }

    public class Camera : Behaviour
    {
        public static Camera main { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; } = 5f;
        public float aspect { get; set; } = 16f / 9f;
        public int pixelWidth => Screen.width;
        public int pixelHeight => Screen.height;
        public Color backgroundColor { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public float depth { get; set; }
        public float nearClipPlane { get; set; } = 0.3f;
        public float farClipPlane { get; set; } = 1000f;
        public bool allowMSAA { get; set; }
        public bool allowHDR { get; set; }
        public int cullingMask { get; set; }
        public RenderTexture targetTexture { get; set; }
        public Vector3 ScreenToWorldPoint(Vector3 p) => p;
        public Vector3 WorldToScreenPoint(Vector3 p) => p;
        public void Render() { }
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Null = 4, OpenGLES3 = 11, Metal = 16, Vulkan = 21, Direct3D11 = 2, Direct3D12 = 18, OpenGLCore = 17 }
    public enum ShadowCastingMode { Off, On }
    public enum IndexFormat { UInt16, UInt32 }
    public enum VertexAttribute { Position, Normal, Tangent, Color, TexCoord0, TexCoord1, TexCoord2, TexCoord3 }
    public enum VertexAttributeFormat { Float32, Float16, UNorm8, SNorm8, UInt8, SInt8, UInt16, SInt16, UInt32, SInt32 }
    public struct VertexAttributeDescriptor
    {
        public VertexAttributeDescriptor(VertexAttribute a = VertexAttribute.Position, VertexAttributeFormat f = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0) { attribute = a; format = f; this.dimension = dimension; this.stream = stream; }
        public VertexAttribute attribute; public VertexAttributeFormat format; public int dimension; public int stream;
    }
    public struct SubMeshDescriptor
    {
        public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles) { this.indexStart = indexStart; this.indexCount = indexCount; this.topology = topology; bounds = default; baseVertex = 0; firstVertex = 0; vertexCount = 0; }
        public int indexStart, indexCount, baseVertex, firstVertex, vertexCount; public MeshTopology topology; public Bounds bounds;
    }
    public enum CompareFunction { Disabled, Never, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, Always }
    public enum BlendMode { Zero, One, DstColor, SrcColor, OneMinusDstColor, SrcAlpha, OneMinusSrcColor, DstAlpha, OneMinusDstAlpha, SrcAlphaSaturate, OneMinusSrcAlpha }
    public sealed class CommandBuffer : IDisposable
    {
        public string name { get; set; }
        public void DispatchCompute(ComputeShader cs, int kernel, int x, int y, int z) { }
        public void SetComputeBufferParam(ComputeShader cs, int kernel, int id, GraphicsBuffer b) { }
        public void SetComputeIntParam(ComputeShader cs, int id, int v) { }
        public void SetComputeFloatParam(ComputeShader cs, int id, float v) { }
        public void SetComputeVectorParam(ComputeShader cs, int id, Vector4 v) { }
        public void DrawMeshInstancedIndirect(Mesh m, int sub, Material mat, int pass, GraphicsBuffer args, int argsOffset = 0, MaterialPropertyBlock p = null) { }
        public void DrawProceduralIndirect(Matrix4x4 m, Material mat, int pass, MeshTopology t, GraphicsBuffer args, int argsOffset = 0, MaterialPropertyBlock p = null) { }
        public void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int sub = 0, int pass = -1, MaterialPropertyBlock p = null) { }
        public void Clear() { }
        public void Release() { }
        public void Dispose() { }
    }
    public struct ScriptableRenderContext { public void ExecuteCommandBuffer(CommandBuffer cb) { } }
    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, Camera> beginCameraRendering { add { } remove { } }
    }
    public class RenderPipelineAsset : ScriptableObject { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline => null; public static RenderPipelineAsset defaultRenderPipeline { get; set; } }
}

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { None = 0, R8G8B8A8_UNorm = 8, R32G32B32A32_UInt = 52, R32G32B32A32_SFloat = 54 }
    [System.Flags] public enum TextureCreationFlags { None = 0, MipChain = 1 }
    [System.Flags] public enum FormatUsage { Sample = 0, Linear = 1, Render = 3, LoadStore = 8 }
}
