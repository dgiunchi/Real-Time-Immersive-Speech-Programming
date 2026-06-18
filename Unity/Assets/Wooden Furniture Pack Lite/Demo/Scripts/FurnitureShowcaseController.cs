namespace EliteIT.LPWFPLite.Demo
{
    using System.Collections.Generic;
    using UnityEngine;
    using TMPro;

    public class FurnitureShowcaseController : MonoBehaviour
    {
        [Tooltip("List of furniture GameObjects to cycle through.")]
        public List<GameObject> furnitureItems = new List<GameObject>();

        [Tooltip("UI Text element to show the active furniture name.")]
        public TextMeshProUGUI furnitureNameText;

        private int currentIndex = 0;

        void Start()
        {
            UpdateVisibility();
        }

        /// <summary>
        /// Shows the next furniture item (wraps around if needed).
        /// </summary>
        public void Next()
        {
            if (furnitureItems.Count == 0) return;

            currentIndex = (currentIndex + 1) % furnitureItems.Count;
            UpdateVisibility();
        }

        /// <summary>
        /// Shows the previous furniture item (wraps around if needed).
        /// </summary>
        public void Previous()
        {
            if (furnitureItems.Count == 0) return;

            currentIndex--;
            if (currentIndex < 0) currentIndex = furnitureItems.Count - 1;

            UpdateVisibility();
        }

        /// <summary>
        /// Hides all furniture and shows only the one at currentIndex.
        /// Updates the UI text if assigned.
        /// </summary>
        private void UpdateVisibility()
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (furnitureItems[i] != null)
                    furnitureItems[i].SetActive(i == currentIndex);
            }

            if (furnitureNameText != null && furnitureItems.Count > 0 && furnitureItems[currentIndex] != null)
            {
                furnitureNameText.text = furnitureItems[currentIndex].name;
            }
        }
    }
}