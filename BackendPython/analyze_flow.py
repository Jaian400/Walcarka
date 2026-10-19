import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
import os

sns.set_theme(style="whitegrid")
output_dir = "wykresy_wyniki"
os.makedirs(output_dir, exist_ok=True)

def load_data(path, device_name):
    df = pd.read_csv(path, delimiter=";")
    for col in df.columns:
        if col not in ['Timestamp', 'Metric', 'ImageName']:
            df[col] = pd.to_numeric(df[col].astype(str).str.replace(',', '.'), errors='coerce')
    df['Timestamp'] = pd.to_datetime(df['Timestamp'])
    df['Urządzenie'] = device_name
    return df

img_q2 = load_data("data/image_transfer_results_q2.csv", "Quest 2")
chart_q2 = load_data("data/chart_profiler_results_q2.csv", "Quest 2")
img_q3 = load_data("data/image_transfer_results_q3.csv", "Quest 3")
chart_q3 = load_data("data/chart_profiler_results_q3.csv", "Quest 3")

df_chart_all = pd.concat([chart_q2, chart_q3])

plt.figure(figsize=(10, 6))
df_q2_combined_fps = pd.DataFrame({
    'FPS': pd.concat([chart_q2['FPS'], img_q2['AvgFPS']]),
    'Metoda': ['XCharts']*len(chart_q2) + ['Image']*len(img_q2)
})
sns.boxplot(data=df_q2_combined_fps, x='Metoda', y='FPS', hue='Metoda', palette='Set2', width=0.5, legend=False)
sns.stripplot(data=df_q2_combined_fps, x='Metoda', y='FPS', color='black', alpha=0.3, size=4, jitter=0.1)
plt.ylim(0, 150)
plt.tight_layout()
plt.savefig(f'{output_dir}/q2_fps.png')
plt.close()

fig, axes = plt.subplots(1, 2, figsize=(14, 6))
df_time_q2 = pd.DataFrame({
    'Metoda': ['XCharts', 'Image'], 
    'Czas': [chart_q2['ChartTime_ms'].sum(), img_q2['TotalTime_ms'].mean()]
})
sns.barplot(data=df_time_q2, x='Metoda', y='Czas', hue='Metoda', ax=axes[0], palette='pastel', legend=False)
for c in axes[0].containers: 
    axes[0].bar_label(c, fmt='%.1f ms')

df_mem_q2 = pd.DataFrame({
    'Metoda': ['XCharts', 'Image'], 
    'Pamięć': [chart_q2['AllocatedMemory_B'].mean(), (img_q2['AllocatedMemory_KB'] * 1024).mean()]
})
sns.barplot(data=df_mem_q2, x='Metoda', y='Pamięć', hue='Metoda', ax=axes[1], palette='muted', legend=False)
for c in axes[1].containers: 
    axes[1].bar_label(c, fmt='%.1f B')
plt.tight_layout()
plt.savefig(f'{output_dir}/q2_system.png')
plt.close()

plt.figure(figsize=(10, 6))
df_q3_combined_fps = pd.DataFrame({
    'FPS': pd.concat([chart_q3['FPS'], img_q3['AvgFPS']]),
    'Metoda': ['XCharts']*len(chart_q3) + ['Image']*len(img_q3)
})
sns.boxplot(data=df_q3_combined_fps, x='Metoda', y='FPS', hue='Metoda', palette='Set2', width=0.5, legend=False)
sns.stripplot(data=df_q3_combined_fps, x='Metoda', y='FPS', color='black', alpha=0.3, size=4, jitter=0.1)
plt.ylim(0, 150)
plt.tight_layout()
plt.savefig(f'{output_dir}/q3_fps.png')
plt.close()

fig, axes = plt.subplots(1, 2, figsize=(14, 6))
df_time_q3 = pd.DataFrame({
    'Metoda': ['XCharts', 'Image'], 
    'Czas': [chart_q3['ChartTime_ms'].sum(), img_q3['TotalTime_ms'].mean()]
})
sns.barplot(data=df_time_q3, x='Metoda', y='Czas', hue='Metoda', ax=axes[0], palette='pastel', legend=False)
for c in axes[0].containers: 
    axes[0].bar_label(c, fmt='%.1f ms')

df_mem_q3 = pd.DataFrame({
    'Metoda': ['XCharts', 'Image'], 
    'Pamięć': [chart_q3['AllocatedMemory_B'].mean(), (img_q3['AllocatedMemory_KB'] * 1024).mean()]
})
sns.barplot(data=df_mem_q3, x='Metoda', y='Pamięć', hue='Metoda', ax=axes[1], palette='muted', legend=False)
for c in axes[1].containers: 
    axes[1].bar_label(c, fmt='%.1f B')
plt.tight_layout()
plt.savefig(f'{output_dir}/q3_system.png')
plt.close()

plt.figure(figsize=(10, 6))
df_all_fps = pd.DataFrame({
    'FPS': pd.concat([chart_q2['FPS'], chart_q3['FPS'], img_q2['AvgFPS'], img_q3['AvgFPS']]),
    'Metoda': ['XCharts']*len(chart_q2) + ['XCharts']*len(chart_q3) + ['Image']*len(img_q2) + ['Image']*len(img_q3),
    'Urządzenie': ['Quest 2']*len(chart_q2) + ['Quest 3']*len(chart_q3) + ['Quest 2']*len(img_q2) + ['Quest 3']*len(img_q3)
})
sns.boxplot(data=df_all_fps, x='Metoda', y='FPS', hue='Urządzenie', palette='Set1', width=0.6)
plt.ylim(0, 150)
plt.tight_layout()
plt.savefig(f'{output_dir}/porownanie_fps.png')
plt.close()

df_chart_all['Czas_s'] = df_chart_all.groupby('Urządzenie')['Timestamp'].transform(lambda x: (x - x.min()).dt.total_seconds())
plt.figure(figsize=(10, 6))
sns.lineplot(data=df_chart_all, x='Czas_s', y='FPS', hue='Urządzenie', style='Urządzenie', markers=True, dashes=False, palette='Dark2')
plt.axhline(y=72, color='r', linestyle='--')
plt.tight_layout()
plt.savefig(f'{output_dir}/porownanie_linia_fps.png')
plt.close()

comp_data = [
    {'Metoda': 'XCharts', 'Urządzenie': 'Quest 2', 'Czas': chart_q2['ChartTime_ms'].sum(), 'RAM': chart_q2['AllocatedMemory_B'].mean()},
    {'Metoda': 'XCharts', 'Urządzenie': 'Quest 3', 'Czas': chart_q3['ChartTime_ms'].sum(), 'RAM': chart_q3['AllocatedMemory_B'].mean()},
    {'Metoda': 'Image', 'Urządzenie': 'Quest 2', 'Czas': img_q2['TotalTime_ms'].mean(), 'RAM': (img_q2['AllocatedMemory_KB'] * 1024).mean()},
    {'Metoda': 'Image', 'Urządzenie': 'Quest 3', 'Czas': img_q3['TotalTime_ms'].mean(), 'RAM': (img_q3['AllocatedMemory_KB'] * 1024).mean()}
]
df_sys_comp = pd.DataFrame(comp_data)

fig, axes = plt.subplots(1, 2, figsize=(14, 6))
sns.barplot(data=df_sys_comp, x='Metoda', y='Czas', hue='Urządzenie', ax=axes[0], palette='Set2')
for c in axes[0].containers: 
    axes[0].bar_label(c, fmt='%.1f ms')

sns.barplot(data=df_sys_comp, x='Metoda', y='RAM', hue='Urządzenie', ax=axes[1], palette='Set3')
for c in axes[1].containers: 
    axes[1].bar_label(c, fmt='%.1f B')
plt.tight_layout()
plt.savefig(f'{output_dir}/porownanie_system.png')
plt.close()