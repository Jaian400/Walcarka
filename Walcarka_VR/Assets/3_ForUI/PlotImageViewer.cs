using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class PlotImageViewer : MonoBehaviour
{
    public ConnectionServiceModern connectionService;
    public RawImage plotDisplay; 

    public string defaultFilename = "wykres.png";

    public async void LoadPlotFromServer()
    {
        Texture2D newTexture = await connectionService.DownloadPlotImageAsync(defaultFilename);

        if (newTexture != null)
        {
            Texture2D oldTexture = plotDisplay.texture as Texture2D;
            plotDisplay.texture = newTexture;

            if (oldTexture != null)
                Destroy(oldTexture);
        }
    }
}