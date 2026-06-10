import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns

file_path_image = "data/image_transfer_results.csv"
file_path_chart = "data/chart_profiler_results.csv"

data_image = pd.read_csv(file_path_image, delimiter=";", decimal=",")
data_chart = pd.read_csv(file_path_chart, delimiter=";", decimal=",")

data_image['Timestamp'] = pd.to_datetime(data_image['Timestamp'])
data_chart['Timestamp'] = pd.to_datetime(data_chart['Timestamp'])

print(data_image.head())
print(data_chart.head())

# sns.histplot(data=data_chart, x="Metric", hue="FPS")
# sns.scatterplot(data=data_chart, x="Timestamp", y="FPS", hue="Metric")
# plt.show()

# --------------------------------------------------------

plt.figure(figsize=(10, 6))

df_chart_fps = pd.DataFrame({'FPS': data_chart['FPS'], 'Metoda': 'XCharts (Rysowanie na żywo)'})
df_image_fps = pd.DataFrame({'FPS': data_image['AvgFPS'], 'Metoda': 'Obraz (Przesłanie gotowej tekstury)'})
df_combined_fps = pd.concat([df_chart_fps, df_image_fps])

sns.boxplot(data=df_combined_fps, x='Metoda', y='FPS', palette='Set2', width=0.5)
sns.stripplot(data=df_combined_fps, x='Metoda', y='FPS', color='black', alpha=0.3, size=4, jitter=0.1)

plt.title('Wpływ metod wizualizacji na płynność gry (FPS) w VR', fontsize=14, fontweight='bold', pad=15)
plt.ylabel('Klatki na sekundę (FPS)', fontsize=12)
plt.xlabel('Metoda wizualizacji', fontsize=12)
plt.ylim(0, 180)
plt.tight_layout()
plt.savefig('porownanie_fps.png')
plt.show()

# --------------------------------------------------------

fig, axes = plt.subplots(1, 2, figsize=(14, 6))

methods_time = ['XCharts (Suma pracy CPU)', 'Flask Image (Średni transfer i render)']
values_time = [data_chart['ChartTime_ms'].sum(), data_image['TotalTime_ms'].mean()]

sns.barplot(x=methods_time, y=values_time, ax=axes[0], palette='pastel')
axes[0].set_title('Całkowity czas procesora / transferu [ms]', fontsize=12, fontweight='bold', pad=10)
axes[0].set_ylabel('Czas [ms]', fontsize=11)
for i, v in enumerate(values_time):
    axes[0].text(i, v + (max(values_time)*0.01), f"{v:.1f} ms", ha='center', fontweight='bold')

methods_mem = ['XCharts (Średnio na klatkę)', 'Image (Średnio na obraz)']
values_mem = [data_chart['AllocatedMemory_B'].mean(), (data_image['AllocatedMemory_KB'] * 1024).mean()]

sns.barplot(x=methods_mem, y=values_mem, ax=axes[1], palette='muted')
axes[1].set_title('Średnie zużycie pamięci sterty RAM [B]', fontsize=12, fontweight='bold', pad=10)
axes[1].set_ylabel('Pamięć [B]', fontsize=11)
for i, v in enumerate(values_mem):
    axes[1].text(i, v + (max(values_mem)*0.01), f"{v:.1f} B", ha='center', fontweight='bold')

plt.suptitle('Narzut systemowy: Wykresy Wektorowe vs. Obrazy', fontsize=15, fontweight='bold', y=0.98)
plt.tight_layout()
plt.savefig('narzut_systemowy_porownanie.png')
plt.show()

# --------------------------------------------------------

plt.figure(figsize=(12, 6))

data_chart = data_chart.sort_values(by='Timestamp')

sns.lineplot(data=data_chart, x='Timestamp', y='FPS', hue='Metric', marker='o', linewidth=1.5, palette='Set1')

plt.title('FPS w czasie trwania XCharts', fontsize=14, fontweight='bold', pad=15)
plt.xlabel('Czas systemowy', fontsize=12)
plt.ylabel('Klatki na sekundę', fontsize=12)
plt.xticks(rotation=30) 
plt.tight_layout()
plt.savefig('przebieg_fps_w_czasie.png')
plt.show()

# --------------------------------------------------------

metric_mapping = {
    'Velocity': ('01_predkosc.png', 'Prędkość [m/min]'),
    'Current': ('01_prad.png', 'Prąd [A]'),
    'Torque': ('01_moment.png', 'Moment [kNm]')
}

plot_data = []

for metric_go, (img_name, friendly_name) in metric_mapping.items():
    xcharts_sum_time = data_chart[data_chart['Metric'] == metric_go]['ChartTime_ms'].sum()
    
    flask_mean_time = data_image[data_image['ImageName'] == img_name]['TotalTime_ms'].mean()
    
    if pd.isna(flask_mean_time):
        flask_mean_time = 0.0
        
    plot_data.append({
        'Badana Zmienna': friendly_name,
        'Całkowity Czas [ms]': xcharts_sum_time,
        'Metoda Wizualizacji': 'XCharts (Suma czasu CPU)'
    })
    plot_data.append({
        'Badana Zmienna': friendly_name,
        'Całkowity Czas [ms]': flask_mean_time,
        'Metoda Wizualizacji': 'Flask Image (Pobranie i Render)'
    })

df_comparison = pd.DataFrame(plot_data)

plt.figure(figsize=(10, 6))

ax = sns.barplot(
    data=df_comparison, 
    x='Badana Zmienna', 
    y='Całkowity Czas [ms]', 
    hue='Metoda Wizualizacji', 
    palette='Set2'
)

for p in ax.patches:
    height = p.get_height()
    if height > 0:
        ax.annotate(
            f'{height:.1f} ms',
            xy=(p.get_x() + p.get_width() / 2, height),
            xytext=(0, 4),  
            textcoords="offset points",
            ha='center', 
            va='bottom', 
            fontsize=9, 
            fontweight='bold'
        )

plt.title('Porównanie całkowitego czasu dla poszczególnych metryk', fontsize=14, fontweight='bold', pad=15)
plt.xlabel('Badana metryka', fontsize=12)
plt.ylabel('Całkowity czas operacji [ms]', fontsize=12)
plt.legend(title='Metoda wizualizacji', loc='upper left')

plt.ylim(0, df_comparison['Całkowity Czas [ms]'].max() * 1.15)

plt.tight_layout()
plt.savefig('porownanie_czasu_metryki_bar.png')
plt.show()

real_time_chart = (data_chart['Timestamp'].max() - data_chart['Timestamp'].min()).total_seconds()
real_time_image = (data_image['Timestamp'].max() - data_image['Timestamp'].min()).total_seconds()

df_durations = pd.DataFrame({
    'Metoda wizualizacji': [
        f'XCharts\n{len(data_chart)} próbek', 
        f'Image\n{len(data_image)} wykresy'
    ],
    'Czas rzeczywisty całej sesji [s]': [real_time_chart, real_time_image]
})

plt.figure(figsize=(8, 6))

ax = sns.barplot(
    data=df_durations, 
    x='Metoda wizualizacji', 
    y='Czas rzeczywisty całej sesji [s]', 
    palette='Set2',
    width=0.4
)

for p in ax.patches:
    height = p.get_height()
    ax.annotate(
        f'{height:.3f} s',
        xy=(p.get_x() + p.get_width() / 2, height),
        xytext=(0, 4),
        textcoords="offset points",
        ha='center', 
        va='bottom', 
        fontsize=10, 
        fontweight='bold'
    )

plt.title('Rzeczywisty czas trwania całej sesji', fontsize=13, fontweight='bold', pad=15)
plt.ylabel('Rzeczywisty czas trwania [sekundy]', fontsize=11)
plt.xlabel('Metoda wizualizacji', fontsize=11)

plt.ylim(0, max(real_time_chart, real_time_image) * 1.15)

plt.tight_layout()
plt.savefig('porownanie_czasu_rzeczywistego.png')
plt.show()