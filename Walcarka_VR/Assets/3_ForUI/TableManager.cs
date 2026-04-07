using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements; 

public class TableManager : MonoBehaviour
{
    [Header("UI Templates")]
    public VisualTreeAsset rowTemplate; // Tutaj przeci¹gniemy RowTemplate.uxml

    private ListView listView;
    private List<RollerData> dataList = new List<RollerData>();

    private void OnEnable()
    {
        // 1. Podpinamy siê pod g³ówne drzewo UI
        var uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 2. Szukamy naszego ListView po nazwie
        listView = root.Q<ListView>("DataListView");

        if (listView == null)
        {
            Debug.LogError("Nie znaleziono DataListView! SprawdŸ nazwê w UI Builderze.");
            return;
        }

        // 3. Konfiguracja ListView: Jak ma tworzyæ nowe wiersze
        listView.makeItem = () =>
        {
            // Tworzy nowy wiersz na podstawie naszego szablonu
            return rowTemplate.Instantiate();
        };

        // 4. Konfiguracja ListView: Jak ma przypisywaæ dane do wiersza (Data Binding)
        listView.bindItem = (VisualElement element, int index) =>
        {
            var data = dataList[index];

            // Szukamy Labeli wewn¹trz TEGO JEDNEGO wiersza i podmieniamy tekst
            element.Q<Label>("TimeLabel").text = data.time;

            // Formatowanie "F3" zaokr¹gla do 3 miejsc po przecinku (np. 11.661)
            element.Q<Label>("VelocityLabel").text = data.velocity.ToString("F3");
            element.Q<Label>("CurrentLabel").text = data.current.ToString("F3");
            element.Q<Label>("TorqueLabel").text = data.torque.ToString("F3");
        };

        // 5. Mówimy tabeli, sk¹d ma braæ dane
        listView.itemsSource = dataList;

        // Subskrybujemy nowe dane z Twojego skryptu TCP
        ConnectionServiceModern.OnDataReceived += HandleNewData;
    }

    private void OnDisable()
    {
        // Pamiêtaj o odpiêciu zdarzenia, ¿eby unikn¹æ wycieków pamiêci!
        ConnectionServiceModern.OnDataReceived -= HandleNewData;
    }

    private void HandleNewData(RollerData newData)
    {
        // Dodajemy now¹ paczkê danych do listy
        dataList.Add(newData);

        // ZABEZPIECZENIE: Trzymamy w tabeli tylko 1000 ostatnich rekordów
        // Jeœli bêdziesz trzyma³ w nieskoñczonoœæ, po godzinie aplikacja zje ca³y RAM
        if (dataList.Count > 1000)
        {
            dataList.RemoveAt(0);
        }

        // Informujemy ListView, ¿e dane siê zmieni³y i musi siê odrysowaæ
        listView.RefreshItems();

        // Automatyczne przewijanie tabeli na sam dó³ (do najnowszych danych)
        listView.ScrollToItem(dataList.Count - 1);
    }
}