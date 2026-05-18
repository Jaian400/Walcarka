using UnityEngine;
using XCharts.Runtime;

public class ChartManager : MonoBehaviour
{
    public LineChart chart;

    public enum MetricType { Velocity, Current, Torque }
    public MetricType selectedMetric;

    private void Start()
    {
        if (chart == null) chart = GetComponent<LineChart>();
        chart.series[0].animation.enable = false;
        chart.series[0].symbol.show = false;
        ConfigureAxes();
    }

    public void ConfigureAxes()
    {
        var xAxis = chart.GetChartComponent<XAxis>();
        var yAxis = chart.GetChartComponent<YAxis>();

        xAxis.animation.show = false;
        yAxis.animation.show = false;

        if (yAxis != null)
        {
            yAxis.minMaxType = Axis.AxisMinMaxType.Default;

            yAxis.splitNumber = 5; 
            yAxis.axisLabel.show = true;
            yAxis.axisLabel.textStyle.fontSize = 24;

            yAxis.axisName.name = GetAxisName();
        }

        if (xAxis != null)
        {
            xAxis.type = Axis.AxisType.Time;
            xAxis.axisLabel.show = true;
            xAxis.axisLabel.textStyle.fontSize = 24;

            xAxis.axisName.name = "Czas";
        }

        chart.RefreshChart();
    }

    private string GetAxisName()
    {
        switch (selectedMetric)
        {
            case MetricType.Velocity: return "Prêdkoœæ [m/min]";
            case MetricType.Current: return "Pr¹d [A]";
            case MetricType.Torque: return "Moment [kNm]";
            default: return "";
        }
    }

    private void OnEnable()
    {
        ConnectionServiceModern.OnDataReceived += UpdateChart;
    }

    private void OnDisable()
    {
        ConnectionServiceModern.OnDataReceived -= UpdateChart;
    }

    private void UpdateChart(RollerData newData)
    {
        if (chart == null) return;

        float valueToChart = 0f;
        switch (selectedMetric)
        {
            case MetricType.Velocity: valueToChart = newData.velocity; break;
            case MetricType.Current: valueToChart = newData.current; break;
            case MetricType.Torque: valueToChart = newData.torque; break;
        }

        chart.AddXAxisData(newData.time);
        chart.AddData(0, valueToChart);
    }
}