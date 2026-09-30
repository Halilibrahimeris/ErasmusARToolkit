using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Where a placeable object goes when the user taps.</summary>
    public enum PlaceOn
    {
        /// <summary>On floors and tables.</summary>
        Floor,

        /// <summary>On walls, facing into the room (pictures, posters, clocks).</summary>
        Wall,
    }

    /// <summary>An object the user can place on a surface by tapping.</summary>
    [Serializable]
    public sealed class PlaceableItem
    {
        [SerializeField, Tooltip("Name shown on the HUD button. Leave empty to use the prefab name.")]
        string displayName = string.Empty;
        [SerializeField] GameObject prefab;
        [SerializeField, Tooltip("Optional icon for the HUD button.")]
        Sprite icon;
        [SerializeField, Min(0.001f), Tooltip("Uniform scale applied when the object is placed.")]
        float defaultScale = 1f;
        [SerializeField] InfoLabelData info = new InfoLabelData();
        [SerializeField, Tooltip("Floor: on floors and tables. Wall: hangs on walls, its front (+Z) facing the room.")]
        PlaceOn placeOn = PlaceOn.Floor;

        public PlaceableItem()
        {
        }

        public PlaceableItem(string displayName, GameObject prefab, float defaultScale = 1f,
            InfoLabelData info = null, Sprite icon = null, PlaceOn placeOn = PlaceOn.Floor)
        {
            this.displayName = displayName ?? string.Empty;
            this.prefab = prefab;
            this.defaultScale = defaultScale;
            this.info = info ?? new InfoLabelData();
            this.icon = icon;
            this.placeOn = placeOn;
        }

        public string DisplayName => displayName ?? string.Empty;
        public GameObject Prefab => prefab;
        public Sprite Icon => icon;
        public float DefaultScale => defaultScale;
        public InfoLabelData Info => info ?? new InfoLabelData();
        public PlaceOn PlaceOn => placeOn;

        internal void SetPlaceOn(PlaceOn value)
        {
            placeOn = value;
        }

        /// <summary>The display name, or the prefab name when no display name is set.</summary>
        public string ResolvedName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName.Trim();
                }

                return prefab != null ? prefab.name : string.Empty;
            }
        }
    }
}
