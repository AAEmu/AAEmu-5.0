#nullable enable
namespace CgfConverter.Utils;

public class DDSFileCombiner
{
    public static string Combine(string baseFileName, string combinedFileNameIdentifier = "combined")
    {
        if (baseFileName is null)
            return string.Empty;

        string? directory = Path.GetDirectoryName(baseFileName);

        if (directory is null)
            return baseFileName;

        string fileNameWithExtension = Path.GetFileName(baseFileName);

        // Get all files that start with the base file name and have a numeric extension
        List<string> filesToCombine = new() { Path.Combine(directory, fileNameWithExtension) };

        filesToCombine.AddRange(Directory.GetFiles(directory, $"{fileNameWithExtension}*")
                    .Where(path => Path.GetFileName(path).StartsWith($"{fileNameWithExtension}.") &&
                                   int.TryParse(Path.GetExtension(path).TrimStart('.'), out _))
                    .OrderByDescending(path => int.Parse(Path.GetExtension(path).TrimStart('.'))).ToList());


        // If no files to combine, inform the user and exit the program
        if (filesToCombine.Count == 0)
            throw new FileNotFoundException("No matching part files found.", baseFileName);

        // Create a new combined file
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileNameWithExtension);
        string extension = Path.GetExtension(fileNameWithExtension);
        string combinedFileName = $"{fileNameWithoutExtension}.{combinedFileNameIdentifier}{extension}";

        try
        {
            using FileStream combinedFileStream = new(combinedFileName, FileMode.Create);

            // Iterate over each file and append it to the combined file
            foreach (string filePath in filesToCombine)
            {
                using FileStream partFileStream = new(filePath, FileMode.Open);
                partFileStream.CopyTo(combinedFileStream);
            }

            return combinedFileName;
        }
        catch (Exception ex)
        {
            throw new IOException($"An error occurred while combining split DDS files for '{fileNameWithExtension}'.", ex);
        }
    }

    public static Stream CombineToStream(string normalizedPath)
    {
        // TODO:  Normal files have both .1 and .1a style names. Figure out how to handle these.
        // Make this more resilient.
        if (normalizedPath is null)
            throw new ArgumentException("Base file name cannot be null or empty.", nameof(normalizedPath));

        string? directory = Path.GetDirectoryName(normalizedPath);

        if (directory is null)
            throw new ArgumentException("Base file name must contain a directory.", nameof(normalizedPath));

        if (Path.GetExtension(normalizedPath) == ".dds")
        {
            string fileNameWithExtension = Path.GetFileName(normalizedPath);

            var files = Directory.GetFiles(directory, $"{fileNameWithExtension}.*");

            // Get all files that start with the base file name and have a numeric extension
            // If no files to combine, inform the user and exit
            if (files.Length == 0)
                throw new FileNotFoundException("No matching part files found.", normalizedPath);

            MemoryStream combinedStream = new();

            try
            {
                // Append the base file to the combined file first, as it contains the header
                using FileStream baseFileStream = new(normalizedPath, FileMode.Open);
                baseFileStream.CopyTo(combinedStream);

                // Iterate over each file and append it to the combined MemoryStream
                for (int i = files.Length - 1; i >= 1; i--)
                {
                    using FileStream partFileStream = new(files[i], FileMode.Open);
                    partFileStream.CopyTo(combinedStream);
                }

                // Reset the position of the MemoryStream to the beginning
                combinedStream.Position = 0;
                return combinedStream;
            }
            catch (Exception ex)
            {
                throw new IOException($"An error occurred while combining split DDS files for '{normalizedPath}'.", ex);
            }
        }
        else if (Path.GetExtension(normalizedPath) == ".1")
        {
            var headerFile = normalizedPath.Replace(".1", ".0");
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedPath);
            var files = Directory.GetFiles(directory, $"{filenameWithoutExtension}.*");

            if (files.Length == 0)
                throw new FileNotFoundException("No matching part files found.", normalizedPath);
            MemoryStream combinedStream = new();

            try
            {
                using FileStream baseFileStream = new(headerFile, FileMode.Open);
                baseFileStream.CopyTo(combinedStream);

                for (int i = files.Length - 1; i >= 1; i--)
                {
                    using FileStream partFileStream = new(files[i], FileMode.Open);
                    partFileStream.CopyTo(combinedStream);
                }

                combinedStream.Position = 0;
                return combinedStream;
            }
            catch (Exception ex)
            {
                throw new IOException($"An error occurred while combining split DDS files for '{normalizedPath}'.", ex);
            }
        }
        else
            throw new FileNotFoundException("Split DDS texture file not found.", normalizedPath);
    }

    public static bool IsSplitDDS(string fullTexturePath)
    {
        // Check if baseFileName is null or empty
        if (string.IsNullOrEmpty(fullTexturePath))
            throw new ArgumentException("Base file name cannot be null or empty.", nameof(fullTexturePath));

        // For Armored Warfare files.
        if (Path.GetExtension(fullTexturePath) == ".1")
            return File.Exists(fullTexturePath);

        // Check if a file exists that starts with the base file name and ends with '.1'
        return File.Exists($"{fullTexturePath}.1");
    }

}
