using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using CgfConverter;
using CgfConverter.CryEngineCore;
using CgfConverter.PackFileSystem;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using NLog;

namespace AAEmu.Game.Models.CryEngine;

public sealed class ModelLoadProfile
{
    public bool CacheHit { get; set; }
    public bool Failed { get; set; }
    public long WaitForSharedLoadMs { get; set; }
    public long ReadFileMs { get; set; }
    public long ParseMs { get; set; }
    public long TriangleBuildMs { get; set; }
    public long MeshBuildMs { get; set; }
    public long ConvexHullBuildMs { get; set; }
    public long TotalMs { get; set; }
    public int TriangleCount { get; set; }
    public long BytesRead { get; set; }
    public int GeometryFilesRead { get; set; }
}

internal sealed class CachedModelGeometry
{
    public required CgfConverter.CryEngine.CryEngine ModelData { get; init; }
    public required List<JTriangle> TriangleList { get; init; }
    public required TriangleMesh TriangleMesh { get; init; }
    public ConvexHullShape ConvexHullTemplate { get; init; }
    public required ModelLoadProfile LoadProfile { get; init; }
}

public static class CryEngineModelHelper
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

    private const int MaxCachedModels = 512;

    // Model Data Cache
    public static ConcurrentDictionary<string, CgfConverter.CryEngine.CryEngine> CryEngineModels { get; } = [];
    public static ConcurrentDictionary<string, List<JTriangle>> CryEngineModelsTriangleListCache { get; } = [];
    public static ConcurrentDictionary<string, TriangleMesh> CryEngineModelsTriangleMeshCache { get; } = [];
    public static ConcurrentDictionary<string, ConvexHullShape> CryEngineModelsConvexHullCache { get; } = [];
    public static ConcurrentDictionary<string, JBoundingBox> CryEngineModelsLocalBoundsCache { get; } = [];
    public static ConcurrentDictionary<string, byte> CryEngineModelsFailedPaths { get; } = [];
    private static ConcurrentDictionary<string, Lazy<CachedModelGeometry>> CryEngineModelLoadCache { get; } = [];
    // public static Dictionary<string, ImageTexture> CryEngineTextureCache { get; } = [];
    // private static object _textureCacheLock = new();

    public static List<JTriangle> MakeModel(string modelFileName, string materialFile)
    {
        return MakeModel(modelFileName, materialFile, out _);
    }

    public static TriangleMesh GetTriangleMesh(string modelFileName, string materialFile)
    {
        return GetTriangleMesh(modelFileName, materialFile, out _);
    }

    public static bool TryGetModelLocalBounds(string modelFileName, string materialFile, out JBoundingBox bounds)
    {
        bounds = default;
        var key = modelFileName.Replace('\\', '/').ToLower();

        if (CryEngineModelsFailedPaths.ContainsKey(key))
            return false;

        if (CryEngineModelsLocalBoundsCache.TryGetValue(key, out bounds))
            return true;

        List<JTriangle> triangles;
        ModelLoadProfile profile;
        try
        {
            triangles = MakeModel(modelFileName, materialFile, out profile);
        }
        catch
        {
            return false;
        }

        if (profile.Failed || triangles.Count <= 0)
            return false;

        var min = triangles[0].V0;
        var max = triangles[0].V0;
        for (var i = 0; i < triangles.Count; i++)
        {
            var tri = triangles[i];
            min = JVector.Min(min, tri.V0);
            min = JVector.Min(min, tri.V1);
            min = JVector.Min(min, tri.V2);
            max = JVector.Max(max, tri.V0);
            max = JVector.Max(max, tri.V1);
            max = JVector.Max(max, tri.V2);
        }

        bounds = new JBoundingBox(min, max);
        CryEngineModelsLocalBoundsCache.TryAdd(key, bounds);
        return true;
    }

    public static ConvexHullShape GetConvexHullShape(string modelFileName, string materialFile)
    {
        return GetConvexHullShape(modelFileName, materialFile, out _);
    }

    public static ConvexHullShape GetConvexHullShape(string modelFileName, string materialFile, out ModelLoadProfile profile)
    {
        var key = modelFileName.Replace('\\', '/').ToLower();
        var inputFile = modelFileName.Replace('/', Path.DirectorySeparatorChar);
        profile = new ModelLoadProfile();
        var totalTimer = Stopwatch.StartNew();

        if (CryEngineModelsFailedPaths.ContainsKey(key))
        {
            profile.Failed = true;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return null;
        }

        if (CryEngineModelsConvexHullCache.TryGetValue(key, out var convexHull))
        {
            profile.CacheHit = true;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return convexHull.Clone();
        }

        var newLazy = new Lazy<CachedModelGeometry>(
            () => LoadModelGeometry(key, modelFileName, materialFile, modelFileName.Replace('/', Path.DirectorySeparatorChar)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var cachedLazy = CryEngineModelLoadCache.GetOrAdd(key, newLazy);
        profile.CacheHit = cachedLazy != newLazy;

        var waitTimer = Stopwatch.StartNew();
        CachedModelGeometry cachedGeometry;
        try
        {
            cachedGeometry = cachedLazy.Value;
        }
        catch (Exception ex)
        {
            waitTimer.Stop();
            profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
            HandleSharedModelLoadFailure(key, inputFile, ex, profile, totalTimer);
            return null;
        }
        waitTimer.Stop();

        profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
        profile.ReadFileMs = cachedGeometry.LoadProfile.ReadFileMs;
        profile.ParseMs = cachedGeometry.LoadProfile.ParseMs;
        profile.TriangleBuildMs = cachedGeometry.LoadProfile.TriangleBuildMs;
        profile.MeshBuildMs = cachedGeometry.LoadProfile.MeshBuildMs;
        profile.ConvexHullBuildMs = cachedGeometry.LoadProfile.ConvexHullBuildMs;
        profile.TriangleCount = cachedGeometry.LoadProfile.TriangleCount;
        profile.BytesRead = cachedGeometry.LoadProfile.BytesRead;
        profile.GeometryFilesRead = cachedGeometry.LoadProfile.GeometryFilesRead;
        profile.Failed = cachedGeometry.LoadProfile.Failed;
        profile.TotalMs = totalTimer.ElapsedMilliseconds;
        return cachedGeometry.ConvexHullTemplate?.Clone();
    }

    public static TriangleMesh GetTriangleMesh(string modelFileName, string materialFile, out ModelLoadProfile profile)
    {
        var key = modelFileName.Replace('\\', '/').ToLower();
        var inputFile = modelFileName.Replace('/', Path.DirectorySeparatorChar);
        profile = new ModelLoadProfile();
        var totalTimer = Stopwatch.StartNew();

        if (CryEngineModelsFailedPaths.ContainsKey(key))
        {
            profile.Failed = true;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return null;
        }

        if (CryEngineModelsTriangleMeshCache.TryGetValue(key, out var triangleMesh))
        {
            profile.CacheHit = true;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return triangleMesh;
        }

        var newLazy = new Lazy<CachedModelGeometry>(
            () => LoadModelGeometry(key, modelFileName, materialFile, modelFileName.Replace('/', Path.DirectorySeparatorChar)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var cachedLazy = CryEngineModelLoadCache.GetOrAdd(key, newLazy);
        profile.CacheHit = cachedLazy != newLazy;

        var waitTimer = Stopwatch.StartNew();
        CachedModelGeometry cachedGeometry;
        try
        {
            cachedGeometry = cachedLazy.Value;
        }
        catch (Exception ex)
        {
            waitTimer.Stop();
            profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
            HandleSharedModelLoadFailure(key, inputFile, ex, profile, totalTimer);
            return null;
        }
        waitTimer.Stop();

        profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
        profile.ReadFileMs = cachedGeometry.LoadProfile.ReadFileMs;
        profile.ParseMs = cachedGeometry.LoadProfile.ParseMs;
        profile.TriangleBuildMs = cachedGeometry.LoadProfile.TriangleBuildMs;
        profile.MeshBuildMs = cachedGeometry.LoadProfile.MeshBuildMs;
        profile.ConvexHullBuildMs = cachedGeometry.LoadProfile.ConvexHullBuildMs;
        profile.TriangleCount = cachedGeometry.LoadProfile.TriangleCount;
        profile.BytesRead = cachedGeometry.LoadProfile.BytesRead;
        profile.GeometryFilesRead = cachedGeometry.LoadProfile.GeometryFilesRead;
        profile.Failed = cachedGeometry.LoadProfile.Failed;
        profile.TotalMs = totalTimer.ElapsedMilliseconds;
        return cachedGeometry.TriangleMesh;
    }

    public static List<JTriangle> MakeModel(string modelFileName, string materialFile, out ModelLoadProfile profile)
    {
        var key = modelFileName.Replace('\\', '/').ToLower();
        var inputFile = modelFileName.Replace('/', Path.DirectorySeparatorChar);
        profile = new ModelLoadProfile();
        var totalTimer = Stopwatch.StartNew();

        if (CryEngineModelsFailedPaths.ContainsKey(key))
        {
            profile.Failed = true;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return [];
        }

        if (CryEngineModelsTriangleListCache.TryGetValue(key, out var triangleList))
        {
            profile.CacheHit = true;
            profile.TriangleCount = triangleList.Count;
            profile.TotalMs = totalTimer.ElapsedMilliseconds;
            return triangleList;
        }

        var newLazy = new Lazy<CachedModelGeometry>(
            () => LoadModelGeometry(key, modelFileName, materialFile, inputFile),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var cachedLazy = CryEngineModelLoadCache.GetOrAdd(key, newLazy);
        profile.CacheHit = cachedLazy != newLazy;

        var waitTimer = Stopwatch.StartNew();
        CachedModelGeometry cachedGeometry;
        try
        {
            cachedGeometry = cachedLazy.Value;
        }
        catch (Exception ex)
        {
            waitTimer.Stop();
            profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
            HandleSharedModelLoadFailure(key, inputFile, ex, profile, totalTimer);
            return [];
        }
        waitTimer.Stop();

        profile.WaitForSharedLoadMs = waitTimer.ElapsedMilliseconds;
        profile.ReadFileMs = cachedGeometry.LoadProfile.ReadFileMs;
        profile.ParseMs = cachedGeometry.LoadProfile.ParseMs;
        profile.TriangleBuildMs = cachedGeometry.LoadProfile.TriangleBuildMs;
        profile.MeshBuildMs = cachedGeometry.LoadProfile.MeshBuildMs;
        profile.TriangleCount = cachedGeometry.LoadProfile.TriangleCount;
        profile.BytesRead = cachedGeometry.LoadProfile.BytesRead;
        profile.GeometryFilesRead = cachedGeometry.LoadProfile.GeometryFilesRead;
        profile.Failed = cachedGeometry.LoadProfile.Failed;
        profile.TotalMs = totalTimer.ElapsedMilliseconds;
        return cachedGeometry.TriangleList;
    }

    private static void HandleSharedModelLoadFailure(
        string key,
        string inputFile,
        Exception ex,
        ModelLoadProfile profile,
        Stopwatch totalTimer)
    {
        ClearCachedModelArtifacts(key);
        profile.Failed = true;
        profile.TotalMs = totalTimer.ElapsedMilliseconds;
        if (CryEngineModelsFailedPaths.TryAdd(key, 0))
        {
            Logger.Warn(ex, "CryEngineModelHelper: failed to load model geometry for {0}", inputFile);
        }
    }

    private static void ClearCachedModelArtifacts(string key)
    {
        CryEngineModelLoadCache.TryRemove(key, out _);
        CryEngineModels.TryRemove(key, out _);
        CryEngineModelsTriangleListCache.TryRemove(key, out _);
        CryEngineModelsTriangleMeshCache.TryRemove(key, out _);
        CryEngineModelsConvexHullCache.TryRemove(key, out _);
        CryEngineModelsLocalBoundsCache.TryRemove(key, out _);
    }

    private static CachedModelGeometry LoadModelGeometry(string key, string modelFileName, string materialFile, string inputFile)
    {
        Utilities.EnsureDefaultLogLevels();
        materialFile = Path.ChangeExtension(materialFile, ".mtl");
        var modelData = new CgfConverter.CryEngine.CryEngine(
            modelFileName,
            new AaGamePakFileSystem(),
            null,
            null
        );

        modelData.ProcessCryengineFiles();
        CryEngineModels.TryAdd(key, modelData);

        var triangleTimer = Stopwatch.StartNew();
        var triangleList = CreateTriangleListFromCryEngineData(modelData, out _);
        triangleTimer.Stop();
        var meshTimer = Stopwatch.StartNew();
        var triangleMesh = new TriangleMesh(triangleList, ignoreDegenerated: true);
        meshTimer.Stop();
        ConvexHullShape convexHullTemplate = null;
        long convexHullBuildMs = 0;
        if (triangleList.Count > 0)
        {
            var convexHullTimer = Stopwatch.StartNew();
            convexHullTemplate = new ConvexHullShape(triangleList);
            convexHullTimer.Stop();
            convexHullBuildMs = convexHullTimer.ElapsedMilliseconds;
        }

        var loadProfile = new ModelLoadProfile
        {
            ReadFileMs = modelData.LastProcessingProfile.GeometryReadMs,
            ParseMs = modelData.LastProcessingProfile.GeometryParseMs,
            TriangleBuildMs = triangleTimer.ElapsedMilliseconds,
            MeshBuildMs = meshTimer.ElapsedMilliseconds,
            ConvexHullBuildMs = convexHullBuildMs,
            TriangleCount = triangleList.Count,
            BytesRead = modelData.LastProcessingProfile.GeometryBytesRead,
            GeometryFilesRead = modelData.LastProcessingProfile.GeometryFilesRead,
            Failed = triangleList.Count <= 0
        };

        if (triangleList.Count <= 0)
        {
            Logger.Debug($"Failed to create triangle list for {inputFile}");
            CryEngineModelsFailedPaths.TryAdd(key, 0);
        }

        CryEngineModelsTriangleListCache.TryAdd(key, triangleList);
        CryEngineModelsTriangleMeshCache.TryAdd(key, triangleMesh);
        if (convexHullTemplate != null)
        {
            CryEngineModelsConvexHullCache.TryAdd(key, convexHullTemplate);
        }

        TrimCacheIfNeeded();

        return new CachedModelGeometry
        {
            ModelData = modelData,
            TriangleList = triangleList,
            TriangleMesh = triangleMesh,
            ConvexHullTemplate = convexHullTemplate,
            LoadProfile = loadProfile
        };
    }

    private static void TrimCacheIfNeeded()
    {
        if (CryEngineModelLoadCache.Count <= MaxCachedModels)
            return;

        var toRemove = CryEngineModelLoadCache.Count - MaxCachedModels;
        var removed = 0;
        foreach (var key in CryEngineModelLoadCache.Keys)
        {
            if (removed >= toRemove)
                break;
            if (CryEngineModelLoadCache.TryRemove(key, out _))
            {
                CryEngineModels.TryRemove(key, out _);
                CryEngineModelsTriangleListCache.TryRemove(key, out _);
                CryEngineModelsTriangleMeshCache.TryRemove(key, out _);
                CryEngineModelsConvexHullCache.TryRemove(key, out _);
                CryEngineModelsLocalBoundsCache.TryRemove(key, out _);
                removed++;
            }
        }
        if (removed > 1)
            Logger.Debug($"CryEngineModelHelper: trimmed {removed} cached models, remaining={CryEngineModelLoadCache.Count}");
    }

    /// <summary>
    /// Takes the data from a CryEngine model from CGFConverter and create populate a RigidBody in the PhysicsManager
    /// </summary>
    public static List<JTriangle> CreateTriangleListFromCryEngineData(CgfConverter.CryEngine.CryEngine data, out List<string> detectedTextures)
    {
        var currentVertexPosition = 0;
        var currentIndicesPosition = 0;
        detectedTextures = [];

        if ((data.Models?.Count ?? 0) <= 0)
        {
            // Logger.Debug($"Model contains no models!");
            return [];
        }

        // NOTE: We don't load textures for the game server as it (currently) has no use

        var triangleList = new List<JTriangle>();

        // Add object to show in scene
        foreach (var node in data.NodeMap.Values)
        {
            if (node.ObjectChunk == null)
            {
                Logger.Warn($"Skipped node with missing Object {node.Name}");
                continue;
            }

            switch (node.ObjectChunk.ChunkType)
            {
                case ChunkType.Mesh:
                    // Render Meshes
                    if ((node.ParentNode != null) && (node.ParentNode.ChunkType != ChunkType.Node))
                    {
                        Logger.Debug($"Debug: Rendering {node.Name} to parent {node.ParentNode.Name}");
                    }

                    // Grab the mesh and process that.
                    WriteObjNode(triangleList, node);
                    break;

                case ChunkType.Helper:
                    // Ignore Helpers nodes
                    // TODO: Investigate if there's something we should do here
                    break;

                default:
                    // Warn us if we're skipping other nodes of interest
                    Logger.Debug($"Debug: Skipped a {node.ObjectChunk.ChunkType} chunk");
                    break;
            }
        }

        return triangleList;

        // Nested helper function
        void WriteObjNode(List<JTriangle> tl, ChunkNode chunkNode) // Pass a node to this to have it write to the Stream
        {
            // Get the Transform here. It's the node chunk Transform.m(41/42/42) divided by 100, added to the parent transform.
            // The transform of a child has to add the transforms of ALL the parents.

            if (chunkNode.ObjectChunk is not ChunkMesh tmpMesh)
                return;

            if (tmpMesh.MeshSubsetsData ==
                0) // This is probably wrong.  These may be parents with no geometry, but still have an offset
            {
                // GD.Print($"Debug: *******Found a Mesh chunk with no Submesh ID (ID: {tmpMesh.ID:X}, Name: {chunkNode.Name}).  Skipping...");
                // tmpMesh.WriteChunk();
                // Utils.Log(LogLevelEnum.Debug, "Node Chunk: {0}", chunkNode.Name);
                // transform = cgfData.GetTransform(chunkNode, transform);
                return;
            }

            if (tmpMesh.VerticesData == 0 &&
                tmpMesh.VertsUVsData ==
                0) // This is probably wrong.  These may be parents with no geometry, but still have an offset
            {
                // GD.Print($"Debug: *******Found a Mesh chunk with no Vertex info (ID: {tmpMesh.ID:X}, Name: {chunkNode.Name}).  Skipping...");
                //tmpMesh.WriteChunk();
                //Utils.Log(LogLevelEnum.Debug, "Node Chunk: {0}", chunkNode.Name);
                //transform = cgfData.GetTransform(chunkNode, transform);
                return;
            }

            // Going to assume that there is only one VerticesData datastream for now.  Need to watch for this.   
            // Some 801 types have vertices and not VertsUVs.
            var tmpMtlName = chunkNode._model.ChunkMap.GetValue(chunkNode.MaterialID, null) as ChunkMtlName;
            var tmpMeshSubsets =
                tmpMesh._model.ChunkMap.GetValue(tmpMesh.MeshSubsetsData,
                    null) as ChunkMeshSubsets; // Listed as Object ID for the Node
            var tmpIndices = tmpMesh._model.ChunkMap.GetValue(tmpMesh.IndicesData, null) as ChunkDataStream;
            var tmpVertices = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VerticesData, null) as ChunkDataStream;
            var tmpNormals = tmpMesh._model.ChunkMap.GetValue(tmpMesh.NormalsData, null) as ChunkDataStream;
            var tmpUVs = tmpMesh._model.ChunkMap.GetValue(tmpMesh.UVsData, null) as ChunkDataStream;
            var tmpVertsUVs = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VertsUVsData, null) as ChunkDataStream;
            var tmpVertMats = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VertMatsData, null) as ChunkDataStream;

            // We only use 3 things in obj files:  vertices, normals and UVs.  No need to process the Tangents.

            int numChildren = chunkNode.NumChildren; // use in a for loop to print the mesh for each child

            var tempVertexPosition = currentVertexPosition;
            var tempIndicesPosition = currentIndicesPosition;
            var transformSoFar = GetNestedTransformations(chunkNode);

            foreach (var meshSubset in tmpMeshSubsets.MeshSubsets)
            {
                if (tmpMesh.VerticesData == 0)
                {
                    // Dymek's code.  Scales the object by the bounding box.
                    var multiplerVector = System.Numerics.Vector3.Abs((tmpMesh.MinBound - tmpMesh.MaxBound) / 2f);
                    if (multiplerVector.X < 1)
                    {
                        multiplerVector.X = 1;
                    }

                    if (multiplerVector.Y < 1)
                    {
                        multiplerVector.Y = 1;
                    }

                    if (multiplerVector.Z < 1)
                    {
                        multiplerVector.Z = 1;
                    }

                    var boundaryBoxCenter = (tmpMesh.MinBound + tmpMesh.MaxBound) / 2f;

                    // Probably using VertsUVs (3.7+).  Write those vertices out. Do UVs at same time.
                    for (int j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {
                        // Let's try this using this node chunk's rotation matrix, and the transform is the sum of all the transforms.
                        // Get the transform.
                        Vector3 vertex = (tmpVertsUVs.Vertices[j] * multiplerVector) + boundaryBoxCenter;

                        // Use matrix operations for the maximum performance
                        vertex = System.Numerics.Vector3.Transform(vertex, transformSoFar);

                        // verts.Add(new  Vector3(vertex.X, vertex.Z, vertex.Y));
                        // f.WriteLine("v {0:F7} {1:F7} {2:F7}", safe(vertex.X), safe(vertex.Y), safe(vertex.Z));
                    }

                    // f.WriteLine();

                    // textures

                    for (int j = meshSubset.FirstVertex;
                         j < meshSubset.NumVertices + meshSubset.FirstVertex;
                         j++)
                    {
                        // f.WriteLine("vt {0:F7} {1:F7} 0", safe(tmpVertsUVs.UVs[j].U), safe(1 - tmpVertsUVs.UVs[j].V));
                    }

                }
                else
                {
                    for (int j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {
                        if (tmpVertices != null)
                        {
                            // Rotate/translate the vertex
                            // Use matrix operations for the maximum performance
                            var vertex = System.Numerics.Vector3.Transform(tmpVertices.Vertices[j], transformSoFar);

                            // verts.Add(new  Vector3(vertex.X, vertex.Z, vertex.Y));
                            // f.WriteLine("v {0:F7} {1:F7} {2:F7}", safe(vertex.X), safe(vertex.Y), safe(vertex.Z));
                        }
                        else
                        {
                            Logger.Debug($"Debug: Error rendering vertices for {chunkNode.Name}");
                        }
                    }

                    // f.WriteLine();

                    // textures

                    for (var j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {
                        // f.WriteLine("vt {0:F7} {1:F7} 0", safe(tmpUVs.UVs[j].U), safe(1 - tmpUVs.UVs[j].V));
                    }

                }

                // f.WriteLine();

                // Normals (we're not using these yet)

                if (tmpMesh.NormalsData != 0)
                {
                    for (var j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {

                        //f.WriteLine("vn {0:F7} {1:F7} {2:F7}",
                        //	tmpNormals.Normals[j].X,
                        //	tmpNormals.Normals[j].Y,
                        //	tmpNormals.Normals[j].Z);
                    }
                }


                // f.WriteLine("g {0}", this.GroupOverride ?? chunkNode.Name);

                //if (this.Args.Smooth)
                //{
                //	f.WriteLine("s {0}", this.FaceIndex++);
                //}

                // Now write out the faces info based on the MtlName

                for (var j = meshSubset.FirstIndex; j < meshSubset.NumIndices + meshSubset.FirstIndex; j++)
                {
                    //f.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", // Vertices, UVs, Normals
                    //	tmpIndices.Indices[j] + 1 + this.CurrentVertexPosition,
                    //	tmpIndices.Indices[j + 1] + 1 + this.CurrentVertexPosition,
                    //	tmpIndices.Indices[j + 2] + 1 + this.CurrentVertexPosition);

                    if (j >= tmpIndices.Indices.Length - 2)
                        continue; // Not sure how to handle the out-of-bounds indices

                    var i1 = tmpIndices.Indices[j] + currentVertexPosition;
                    var i2 = tmpIndices.Indices[j + 1] + currentVertexPosition;
                    var i3 = tmpIndices.Indices[j + 2] + currentVertexPosition;

                    // Validate range
                    if (i1 < 0 || i2 < 0 || i3 < 0)
                        continue;

                    if (i1 >= tmpVertices.Vertices.Length || i2 >= tmpVertices.Vertices.Length ||
                        i3 >= tmpVertices.Vertices.Length)
                        continue;

                    if (i1 >= tmpUVs.UVs.Length || i2 >= tmpUVs.UVs.Length || i3 >= tmpUVs.UVs.Length)
                        continue;

                    var v1 = tmpVertices.Vertices[i1];
                    var v2 = tmpVertices.Vertices[i2];
                    var v3 = tmpVertices.Vertices[i3];

                    var uv1 = tmpUVs.UVs[tmpIndices.Indices[j] + currentVertexPosition];
                    var uv2 = tmpUVs.UVs[tmpIndices.Indices[j + 1] + currentVertexPosition];
                    var uv3 = tmpUVs.UVs[tmpIndices.Indices[j + 2] + currentVertexPosition];

                    /*
                    //st.SetColor(col);
                    st.SetUV(new Vector2(uv1.U, uv1.V));
                    st.AddVertex(new Vector3(v1.X, v1.Z, v1.Y));

                    //st.SetColor(col);
                    st.SetUV(new Vector2(uv2.U, uv2.V));
                    st.AddVertex(new Vector3(v2.X, v2.Z, v2.Y));

                    //st.SetColor(col);
                    st.SetUV(new Vector2(uv3.U, uv3.V));
                    st.AddVertex(new Vector3(v3.X, v3.Z, v3.Y));
                    */

                    tl.Add(new JTriangle(new JVector(v1.X, v1.Z, v1.Y), new JVector(v2.X, v2.Z, v2.Y), new JVector(v3.X, v3.Z, v3.Y)));

                    j += 2;
                }

                tempVertexPosition +=
                    meshSubset.NumVertices; // add the number of vertices so future objects can start at the right place
                tempIndicesPosition += meshSubset.NumIndices; // Not really used...
            }

            // Extend the current vertex, uv and normal positions by the length of those arrays.
            currentVertexPosition = tempVertexPosition;
            currentIndicesPosition = tempIndicesPosition;

            // Generate something to display
            // f.Name = $"Model_ID_0x{tmpMesh.ID:X2}_Name_{chunkNode.Name}";

            // Use Surface Tool to create the mesh
            /*
            var tmpResMesh = new ArrayMesh();
            st.Commit(tmpResMesh);
            f.Mesh = tmpResMesh;
            f.Mesh.SurfaceSetMaterial(0, material3D);
            */
            // f.MaterialOverride = material3D;

            // f.MakeMeshFromVertex(verts, Mesh.PrimitiveType.Triangles, col);
            // LocalAddChild(f);
        }
    }

    private static Matrix4x4 GetNestedTransformations(ChunkNode node)
    {
        if (node.ParentNode != null)
        {
            return node.Transform * GetNestedTransformations(node.ParentNode);
        }
        else
        {
            // TODO: What should this be?
            return node.Transform;
        }
    }
}
