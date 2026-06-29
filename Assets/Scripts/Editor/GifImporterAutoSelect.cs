using System;
using UnityEditor;

/// <summary>
/// Unity's built-in TextureImporter claims the .gif extension, and a ScriptedImporter's
/// overrideFileExtensions only makes <see cref="GifImporter"/> an *available* importer, not the *default*
/// one. This postprocessor closes that gap: whenever a .gif is imported with any importer other than
/// GifImporter, it switches the asset's importer override to GifImporter and lets it reimport.
///
/// Net effect: drop a .gif into the project and it just becomes a <see cref="GifClip"/> ??? no manual
/// "Importer" dropdown step. (On the import that follows SetImporterOverride the importer is already
/// GifImporter, so this no-ops and never loops.)
/// </summary>
public class GifImporterAutoSelect : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
        {
            if (!path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) continue;
            if (AssetImporter.GetAtPath(path) is GifImporter) continue; // already using our importer
            AssetDatabase.SetImporterOverride<GifImporter>(path);
        }
    }
}
