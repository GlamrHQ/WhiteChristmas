using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

namespace Anaglyph.DisplayCapture
{
    public class AnchorUIManager : MonoBehaviour
    {
        [SerializeField] private Toggle eraseAnchorsToggle;
        [SerializeField] private Toggle loadAnchorsToggle;

        private void Start()
        {
            eraseAnchorsToggle.onValueChanged.AddListener(async (isOn) => await OnEraseAnchorsToggleChanged(isOn));
            loadAnchorsToggle.onValueChanged.AddListener(async (isOn) => await OnLoadAnchorsToggleChanged(isOn));
        }

        private async Task OnEraseAnchorsToggleChanged(bool isOn)
        {
            if (isOn)
            {
                await SpatialAnchorManager.Instance.EraseAllSavedAnchors();
                eraseAnchorsToggle.isOn = false; // Reset toggle after operation
            }
        }

        private async Task OnLoadAnchorsToggleChanged(bool isOn)
        {
            if (isOn)
            {
                await SpatialAnchorManager.Instance.LoadSavedAnchors();
                loadAnchorsToggle.isOn = false; // Reset toggle after operation
            }
        }
    }
}