using System.Collections.Immutable;
using BCnEncoder.Shared;

namespace CgfConverter.Renderers.Gltf;

internal static class GltfRendererUtilities
{
    internal static IEnumerable<string> StripCommonParentPaths(ICollection<string> fullNames)
    {
        var namesDepths = new List<List<string>>();

        var names = new List<string>();
        for (var i = 0; i < fullNames.Count; i++)
        {
            var nameDepth = 0;
            for (; nameDepth < namesDepths.Count; nameDepth++)
            {
                if (namesDepths[nameDepth].Count(x => x == namesDepths[nameDepth][i]) < 2)
                    break;
            }

            if (nameDepth == namesDepths.Count)
                namesDepths.Add(fullNames.Select(x => string.Join('/', x.Split('/').TakeLast(nameDepth + 1))).ToList());

            names.Add(namesDepths[nameDepth][i]);
        }

        return names.ToImmutableList();
    }

    /// <summary>
    /// Determines if the alpha channel is all 0 or all 255.
    /// </summary>
    /// <param name="raw">Color array</param>
    /// <returns>True if the alpha channel has image data</returns>
    internal static bool HasMeaningfulAlphaChannel(ColorRgba32[] raw)
    {
        var allMin = true;
        var allMax = true;
        for (var i = 0; i < raw.Length && (allMin || allMax); i++)
        {
            allMin &= raw[i].a == 0;
            allMax &= raw[i].a == 255;
        }

        return !allMax && !allMin;
    }
}
