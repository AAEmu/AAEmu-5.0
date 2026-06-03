using Newtonsoft.Json;

namespace CgfConverter.Renderers.Gltf.Models;

/// <summary>
/// Base object for a GLTF file. This gets serialized to the gltf/glb file.
/// </summary>
public class GltfRoot
{
    [JsonProperty("asset")]
    public GltfAsset Asset = new();

    [JsonProperty("extensionsUsed")]
    public HashSet<string> ExtensionsUsed = new();

    [JsonProperty("scene")]
    public int Scene;

    [JsonProperty("scenes")]
    public List<GltfScene> Scenes = new();

    [JsonProperty("nodes")]
    public List<GltfNode> Nodes = new();

    [JsonProperty("animations")]
    public List<GltfAnimation> Animations = new();

    [JsonProperty("materials")]
    public List<GltfMaterial> Materials = new();

    [JsonProperty("meshes")]
    public List<GltfMesh> Meshes = new();

    [JsonProperty("textures")]
    public List<GltfTexture> Textures = new();

    [JsonProperty("images")]
    public List<GltfImage> Images = new();

    [JsonProperty("skins")]
    public List<GltfSkin> Skins = new();

    [JsonProperty("accessors")]
    public List<GltfAccessor> Accessors = new();

    [JsonProperty("bufferViews")]
    public List<GltfBufferView> BufferViews = new();

    [JsonProperty("samplers")]
    public List<GltfSampler> Samplers = new();

    [JsonProperty("buffers")]
    public List<GltfBuffer> Buffers = new();

    public bool ShouldSerializeExtensionsUsed() => ExtensionsUsed.Count != 0;
    public bool ShouldSerializeScenes() => Scenes.Count != 0;
    public bool ShouldSerializeNodes() => Nodes.Count != 0;
    public bool ShouldSerializeAnimations() => Animations.Count != 0;
    public bool ShouldSerializeMaterials() => Materials.Count != 0;
    public bool ShouldSerializeMeshes() => Meshes.Count != 0;
    public bool ShouldSerializeTextures() => Textures.Count != 0;
    public bool ShouldSerializeImages() => Images.Count != 0;
    public bool ShouldSerializeSkins() => Skins.Count != 0;
    public bool ShouldSerializeAccessors() => Accessors.Count != 0;
    public bool ShouldSerializeBufferViews() => BufferViews.Count != 0;
    public bool ShouldSerializeSamplers() => Samplers.Count != 0;
    public bool ShouldSerializeBuffers() => Buffers.Count != 0;
}