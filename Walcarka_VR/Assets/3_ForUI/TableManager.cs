using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements; 

public class TableManager : MonoBehaviour
{
    [Header("UI Templates")]
    public VisualTreeAsset rowTemplate;

    private ListView listView;
    private List<RollerData> dataList = new List<RollerData>();

    private void OnEnable()
    {
        var uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        listView = root.Q<ListView>("DataListView");

        if (listView == null)
        {
            Debug.LogError("Nie znaleziono DataListView! SprawdŸ nazwê w UI Builderze.");
            return;
        }

        listView.makeItem = () =>
        {
            return rowTemplate.Instantiate();
        };

        listView.bindItem = (VisualElement element, int index) =>
        {
            var data = dataList[index];

            element.Q<Label>("TimeLabel").text = data.time;

            element.Q<Label>("VelocityLabel").text = data.velocity.ToString("F3");
            element.Q<Label>("CurrentLabel").text = data.current.ToString("F3");
            element.Q<Label>("TorqueLabel").text = data.torque.ToString("F3");
        };

        listView.itemsSource = dataList;

        ConnectionServiceModern.OnDataReceived += HandleNewData;
    }

    private void OnDisable()
    {
        ConnectionServiceModern.OnDataReceived -= HandleNewData;
    }

    private void HandleNewData(RollerData newData)
    {
        dataList.Add(newData);

        if (dataList.Count > 1000)
        {
            dataList.RemoveAt(0);
        }

        listView.RefreshItems();

        listView.ScrollToItem(dataList.Count - 1);
    }
}