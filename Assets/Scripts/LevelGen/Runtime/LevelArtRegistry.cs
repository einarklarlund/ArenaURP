using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// The level presets and themes in the build. A level ticket names its preset (for prop prefabs) and theme by index
    /// here, so every peer builds with the same art; the geometry comes from the ticket's own config.
    /// </summary>
    [CreateAssetMenu(menuName = "Arena/Level Art Registry", fileName = "LevelArtRegistry")]
    public class LevelArtRegistry : ScriptableObject
    {
        public List<LevelGenConfig> presets = new();
        public List<LevelTheme> themes = new();

        public LevelGenConfig Preset(int index) => index >= 0 && index < presets.Count ? presets[index] : null;
        public LevelTheme Theme(int index) => index >= 0 && index < themes.Count ? themes[index] : null;
    }
}
