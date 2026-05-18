using UnityEngine;
using UnityEngine.UI; // Wymagane dla RawImage
using System.Threading.Tasks;

public class PlotImageViewer : MonoBehaviour
{
    public ConnectionServiceModern connectionService;
    public RawImage plotDisplay; 

    public string defaultFilename = "wykres.png";

    public async void LoadPlotFromServer()
    {
        if (connectionService == null || plotDisplay == null) return;

        Texture2D newTexture = await connectionService.DownloadPlotImageAsync(defaultFilename);

        if (newTexture != null)
        {
            if (plotDisplay.texture != null)
            {
                Destroy(plotDisplay.texture);
            }

            plotDisplay.texture = newTexture;

            plotDisplay.SetNativeSize();
        }
    }
}